using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Sire;
using Altinn.Register.Core.Validation;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Validates and maps an <see cref="OrganizationDocument"/> to a <see cref="SireOrganization"/>.
/// </summary>
internal sealed class OrganizationDocumentValidator
    : IValidator<OrganizationDocument, SireOrganization>
{
    private readonly ILocationLookup _lookup;
    private readonly DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the OrganizationDocumentValidator with the specified
    /// location lookup and a single snapshot of "now".
    /// </summary>
    /// <param name="lookup">The location lookup service used to validate organization document locations.</param>
    /// <param name="now">
    /// A single moment-in-time used for every validity check across this document.
    /// </param>
    public OrganizationDocumentValidator(ILocationLookup lookup, DateTimeOffset now)
    {
        _lookup = lookup;
        _now = now;
    }

    /// <summary>
    /// Maximum tolerated drift between SIRE's clock and ours before an
    /// <c>opphoerstidspunkt</c> sitting in the future is treated as bad data.
    /// </summary>
    private static readonly TimeSpan FutureDateGrace = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Sanity check for <c>opphoerstidspunkt</c>. Per Skatteetaten's contract for SIRE,
    /// <c>gyldighetstidspunkt</c> and <c>opphoerstidspunkt</c> can only be in the present
    /// or past — never the future — and the response only contains currently-effective
    /// entries. A future-dated value therefore violates that contract; we surface it as
    /// a validation error rather than silently picking an interpretation. The
    /// <see cref="FutureDateGrace"/> window absorbs ordinary clock skew between SIRE
    /// and us so a few-minute drift doesn't trip the check.
    /// </summary>
    private static void ValidateNotFarFuture(
        ref ValidationContext context,
        DateTimeOffset? date,
        DateTimeOffset now,
        string path)
    {
        if (date is { } at && at > now + FutureDateGrace)
        {
            context.AddChildProblem(
                ValidationErrors.InvalidValue,
                path,
                detail: $"The provided value '{at:O}' is more than {(int)FutureDateGrace.TotalMinutes} minutes into the future.");
        }
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        OrganizationDocument input,
        [NotNullWhen(true)] out SireOrganization? validated)
    {
        OrganizationIdentifier? orgId = null;
        if (string.IsNullOrWhiteSpace(input.Identifier))
        {
            context.AddChildProblem(StdValidationErrors.Required, "/identifikator");
        }
        else
        {
            context.TryValidateChild(
                path: "/identifikator",
                input.Identifier,
                default(OrganizationIdentifierValidator),
                out orgId);
        }

        if (string.IsNullOrWhiteSpace(input.CompanyName))
        {
            context.AddChildProblem(StdValidationErrors.Required, "/selskapetsNavn");
        }

        // Map organisasjonsform via the validator so unknown values aggregate as errors
        // at "/organisasjonsform" rather than throwing. Missing/empty input yields the
        // default SL-code and succeeds.
        context.TryValidateChild(
            path: "/organisasjonsform",
            input.OrganizationForm,
            default(SireOrganizationFormMapper),
            out string? unitType);

        var now = _now;

        DateTimeOffset? deletedAt = null;
        if (!string.IsNullOrWhiteSpace(input.DeletedDate))
        {
            if (DateOnly.TryParseExact(input.DeletedDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDeletedOn))
            {
                deletedAt = new DateTimeOffset(parsedDeletedOn, TimeOnly.MinValue, TimeSpan.Zero);
            }
            else
            {
                context.AddChildProblem(
                    ValidationErrors.InvalidDate,
                    "/slettetdato",
                    detail: $"The value '{input.DeletedDate}' is not a valid date.");
            }
        }

        ValidateNotFarFuture(ref context, input.PostalAddress?.ValidTo, now, "/postadresse/opphoerstidspunkt");

        // Validate business relationships up-front so any per-relationship problems
        // are added to the context before we decide whether to bail out.        
        var businessRelationships = MapBusinessRelationships(
            ref context,
            input.BusinessRelationships,
            now);

        if (context.HasErrors)
        {
            validated = null;
            return false;
        }

        Debug.Assert(orgId is not null);
        Debug.Assert(unitType is not null);
        Debug.Assert(!string.IsNullOrWhiteSpace(input.CompanyName));

        MailingAddressRecord? mailingAddress = NormalizeAddress(input.PostalAddress);

        validated = new SireOrganization
        {
            OrganizationIdentifier = orgId!,
            Name = input.CompanyName,
            UnitType = unitType,
            UnitStatus = deletedAt is not null ? "S" : "E",
            DeletedAt = deletedAt,
            MailingAddress = mailingAddress,
            LastUpdated = input.PostalAddress?.UpdatedAt,
            BusinessRelationships = businessRelationships,
        };

        return true;
    }

    private MailingAddressRecord? NormalizeAddress(PostalAddress? postalAddress)
    {
        if (postalAddress is null)
        {
            return null;
        }

        // Any non-null opphoerstidspunkt means SIRE has marked this address as
        // terminated; treat it as no current address. Downstream consumers already
        // handle MailingAddress = null, and a stale address is worse than none for
        // mail delivery. (TryValidate has already added an error if the date sits
        // unreasonably far in the future — see ValidateNotFarFuture.)
        if (postalAddress.ValidTo is not null)
        {
            return null;
        }

        // Precedence rule: when both norskAdresse and utenlandskAdresse are present on
        // the same postadresse, the Norwegian one wins and the international one is
        // dropped.
        // Test: PostalAddress_BothNorwegianAndInternational_NorwegianWins.
        if (postalAddress.NorwegianAddress is { } norwegian)
        {
            return NormalizeNorwegianAddress(norwegian);
        }

        if (postalAddress.InternationalAddress is { } international)
        {
            return NormalizeInternationalAddress(international);
        }

        return null;
    }

    private static MailingAddressRecord? NormalizeNorwegianAddress(NorwegianAddress address)
    {
        var lines = NormalizeAddressLines(address.AddressLines, isForeign: false);
        var postalCode = NormalizeNorwegianPostalCode(address.PostalCode);
        var city = address.City?.Trim();

        // Mirror FREG/ER: surface the postal-code/city as a trailing address line for
        // display, but only when there are already address lines and the postal code
        // isn't already at the start of the last line. The PostalCode/City fields still
        // carry the structured values separately.
        if (lines.Count > 0 && (postalCode is null || !lines[^1].StartsWith(postalCode, StringComparison.Ordinal)))
        {
            var postalLine = $"{postalCode} {city}".Trim();
            if (postalLine.Length > 0)
            {
                lines.Add(postalLine);
            }
        }

        if (lines.Count == 0 && postalCode is null && city is null)
        {
            return null;
        }

        return new MailingAddressRecord
        {
            Address = lines.Count > 0 ? string.Join(' ', lines) : null,
            PostalCode = postalCode,
            City = city,
        };
    }

    private MailingAddressRecord? NormalizeInternationalAddress(InternationalAddress address)
    {
        var lines = NormalizeAddressLines(address.AddressLines, isForeign: true);
        var postalCode = GetInternationalPostalCode(address.PostalCode, address.CountryCode);
        var city = address.City?.Trim();

        // Mirror FREG/ER: surface city in address lines if not already present anywhere
        // in them (Contains, not StartsWith — international layouts vary, the city may
        // appear in the middle of a line such as "1600 Pennsylvania Avenue Washington").
        if (!string.IsNullOrEmpty(city)
            && !lines.Any(line => line.Contains(city, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(city);
        }

        // Add country name if not already present in address lines.
        if (TryLookupCountryName(address.CountryCode, out var countryName)
            && !lines.Any(line => line.Contains(countryName, StringComparison.OrdinalIgnoreCase)))
        {
            lines.Add(countryName);
        }

        if (lines.Count == 0 && postalCode is null && city is null)
        {
            return null;
        }

        return new MailingAddressRecord
        {
            Address = lines.Count > 0 ? string.Join(' ', lines) : null,
            PostalCode = postalCode,
            City = city,
        };
    }

    /// <summary>
    /// Normalizes address lines by stripping C/O prefixes and normalizing postbox prefixes.
    /// </summary>
    private static List<string> NormalizeAddressLines(IReadOnlyList<string>? addressLines, bool isForeign)
    {
        var result = new List<string>();
        if (addressLines is null)
        {
            return result;
        }

        bool isFirstLine = true;
        foreach (var line in addressLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var normalized = line;

            // C/O prefixes are a first-line convention in Norwegian addressing — mirrors
            // NPR, which only ever calls RemoveCareOfPrefix on the value destined for
            // line 0 (its dedicated CareOfAddressName field). For SIRE's freeform
            // adressetekst we apply the same first-line-only rule so that a later line
            // accidentally starting with "CO" or "V/" isn't stripped.
            if (isFirstLine)
            {
                normalized = RemoveCareOfPrefix(normalized);
                isFirstLine = false;
            }

            normalized = AddPostboxIfNeeded(normalized, isForeign);
            result.Add(normalized);
        }

        return result;
    }

    /// <summary>
    /// Pads Norwegian postal codes to 4 digits. Returns "0000" sentinel for missing/invalid codes.
    /// </summary>
    private static string? NormalizeNorwegianPostalCode(string? postalCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        var trimmed = postalCode.Trim();
        if (!trimmed.All(char.IsDigit))
        {
            return null;
        }

        var padded = trimmed.PadLeft(4, '0');
        return padded == "0000" ? null : padded;
    }

    /// <summary>
    /// Formats international postal codes as "{COUNTRYCODE}-{number}" when country code is present and number is digits only.
    /// </summary>
    private static string? GetInternationalPostalCode(string? postalCode, string? countryCode)
    {
        if (string.IsNullOrWhiteSpace(postalCode))
        {
            return null;
        }

        var trimmed = postalCode.Trim();
        if (!string.IsNullOrWhiteSpace(countryCode) && trimmed.All(char.IsDigit))
        {
            return $"{countryCode.Trim().ToUpperInvariant()}-{trimmed}";
        }

        return trimmed;
    }

    private bool TryLookupCountryName(string? countryCode, [NotNullWhen(true)] out string? countryName)
    {
        if (string.IsNullOrEmpty(countryCode))
        {
            countryName = null;
            return false;
        }

        if (_lookup.TryGetCountry(countryCode, out var country))
        {
            countryName = country.Name;
            return true;
        }

        // fallback to the country code itself
        countryName = countryCode;
        return true;
    }

    private static readonly ImmutableArray<string> CareOfPrefixes
        = [
            "C/O ",
            "C/O: ",
            "C/O",
            "CO. ",
            "CO ",
            "CO/ ",
            "CO/",
            "C.O. ",
            "C.O.",
            "CO: ",
            "C\\O ",
            "C/0 ",
            "V/ ",
            "V/",
        ];

    private static string RemoveCareOfPrefix(string input)
    {
        var prefix = CareOfPrefixes.FirstOrDefault(
            p => input.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        return prefix is null
            ? input.Trim()
            : input[prefix.Length..].Trim();
    }

    private static readonly ImmutableArray<string> PostBoxPrefixes
        = [
            "postboks",
            "postoffice box ",
            "P.O Box ",
            "P.O.Box ",
            "PO box ",
            "GPO Box ",
            "PO. box ",
            "postbox",
            "boks ",
            "box ",
            "pb.",
            "p.b.",
            "p. b.",
            "p b ",
        ];

    private static string AddPostboxIfNeeded(string input, bool isForeign)
    {
        var prefix = PostBoxPrefixes.FirstOrDefault(
            p => input.StartsWith(p, StringComparison.OrdinalIgnoreCase));

        if (prefix is null)
        {
            return input;
        }

        var remainder = input[prefix.Length..].Trim();
        var postBoxPrefix = isForeign ? "Postbox" : "Postboks";
        return $"{postBoxPrefix} {remainder}".Trim();
    }

    private static ImmutableValueArray<SireBusinessRelationship> MapBusinessRelationships(
        ref ValidationContext context,
        IReadOnlyList<BusinessRelationship>? relationships,
        DateTimeOffset now)
    {
        if (relationships is null or { Count: 0 })
        {
            return [];
        }

        var result = ImmutableArray.CreateBuilder<SireBusinessRelationship>(relationships.Count);
        for (var i = 0; i < relationships.Count; i++)
        {
            var path = $"/virksomhetsrelasjon/{i}";
            if (TryMapBusinessRelationship(ref context, relationships[i], now, path, out var mapped))
            {
                result.Add(mapped);
            }
        }

        return result.DrainToImmutableValueArray();
    }

    /// <summary>
    /// Validates and maps a single SIRE business relationship. Returns false when the
    /// entry should be skipped (either because validation added an error or because the
    /// relationship is terminated). All branches that bail out add a problem to the
    /// context at the appropriate sub-path.
    /// </summary>
    private static bool TryMapBusinessRelationship(
        ref ValidationContext context,
        BusinessRelationship rel,
        DateTimeOffset now,
        string path,
        [NotNullWhen(true)] out SireBusinessRelationship? mapped)
    {
        mapped = null;

        if (string.IsNullOrWhiteSpace(rel.RelationshipType))
        {
            context.AddChildProblem(StdValidationErrors.Required, path + "/relasjonstype");
            return false;
        }

        // Any non-null opphoerstidspunkt means SIRE has terminated this relationship;
        // skip it. Far-future validTo values are flagged as bad data — see
        // ValidateNotFarFuture for the rationale. Full-upsert downstream clears any
        // previously-known assignments not present in this fresh document.
        if (rel.ValidTo is not null)
        {
            ValidateNotFarFuture(ref context, rel.ValidTo, now, path + "/opphoerstidspunkt");
            return false;
        }

        if (!TryValidateRelatedIdentifier(ref context, rel.RelatedIdentifier, path, out var personId, out var orgId))
        {
            return false;
        }

        // Map relasjonstype via the validator so an unknown value adds an aggregated
        // error at "/relasjonstype" instead of throwing — this lets a single SIRE
        // response surface every unrecognised relasjonstype at once.
        if (!context.TryValidateChild(
            path: path + "/relasjonstype",
            rel.RelationshipType,
            default(SireRoleMapper),
            out string? roleIdentifier))
        {
            return false;
        }

        mapped = new SireBusinessRelationship
        {
            RoleIdentifier = roleIdentifier,
            RelatedPersonIdentifier = personId,
            RelatedOrganizationIdentifier = orgId,
        };
        return true;
    }

    /// <summary>
    /// Validates a SIRE <c>relatertIdentifikator</c> and parses its value into either a
    /// <see cref="PersonIdentifier"/> or an <see cref="OrganizationIdentifier"/> based on
    /// the wire-format <c>verdi</c>. Returns false (and adds a problem) when the
    /// identifier element is missing, the identifier type is unsupported, or the value
    /// cannot be parsed as either a person- or org-number.
    /// </summary>
    private static bool TryValidateRelatedIdentifier(
        ref ValidationContext context,
        RelatedIdentifier? relatedId,
        string path,
        out PersonIdentifier? personId,
        out OrganizationIdentifier? orgId)
    {
        personId = null;
        orgId = null;

        if (relatedId is null || string.IsNullOrWhiteSpace(relatedId.Value))
        {
            context.AddChildProblem(StdValidationErrors.Required, path + "/relatertIdentifikator");
            return false;
        }

        if (!string.Equals(relatedId.IdentifierType, "taxIdentificationNumber", StringComparison.Ordinal))
        {
            context.AddChildProblem(
                ValidationErrors.InvalidValue,
                path + "/relatertIdentifikator/identifikatortype",
                detail: $"Expected 'taxIdentificationNumber', got '{relatedId.IdentifierType}'.");
            return false;
        }

        if (PersonIdentifier.TryParse(relatedId.Value, provider: null, out var parsedPerson))
        {
            personId = parsedPerson;
            return true;
        }

        if (OrganizationIdentifier.TryParse(relatedId.Value, provider: null, out var parsedOrg))
        {
            orgId = parsedOrg;
            return true;
        }

        context.AddChildProblem(
            ValidationErrors.InvalidValue,
            path + "/relatertIdentifikator/verdi",
            detail: $"The value '{relatedId.Value}' is not a valid person or organization number.");
        return false;
    }
}
