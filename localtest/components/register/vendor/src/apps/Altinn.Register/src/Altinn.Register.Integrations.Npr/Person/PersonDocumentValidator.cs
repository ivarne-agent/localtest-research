using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Authorization.ProblemDetails.Validation;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Utils;
using Altinn.Register.Core.Validation;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Validator for validating a <see cref="PersonDocument"/>.
/// </summary>
public sealed class PersonDocumentValidator(ILocationLookup lookup)
    : IValidator<PersonDocument, NprPerson>
    , IValidator<GuardianshipOrPowerOfAttorneyElement, Optional<NprGuardianship>>
    , IValidator<Guardianship, Optional<NprGuardianship>>
    , IValidator<GuardianshipServiceArea, Optional<string>>
    , IValidator<PersonStatusElement, PersonStatus>
    , IValidator<IdentificationNumberElement, PersonIdentifier>
    , IValidator<BirthElement, Optional<DateOnly>>
    , IValidator<AddressProtectionElement, Optional<AddressConfidentialityLevel>>
    , IValidator<ResidentialAddressElement, Optional<StreetAddressRecord>>
    , IValidator<NameElement, Optional<PersonName>>
    , IValidator<DeathElement?, PersonDocumentValidator.PersonDeath>
    , IValidator<MailingAddressElement, Optional<PersonDocumentValidator.MailingAddressRecordExt>>
    , IValidator<CurrentStayAddressElement, Optional<PersonDocumentValidator.MailingAddressRecordExt>>
    , IValidator<ResidentialAddressElement, Optional<PersonDocumentValidator.MailingAddressRecordExt>>
    , IValidator<InternationalMailingAddressElement, Optional<PersonDocumentValidator.MailingAddressRecordExt>>
    , IValidator<FreeFormMailingAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<StreetAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<StreetAddress, StreetAddressRecord>
    , IValidator<PostBoxAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<InternationalFreeFormMailingAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<InternationalMailingAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<MatrikkelAddress, PersonDocumentValidator.MailingAddressRecordExt>
    , IValidator<MatrikkelAddress, StreetAddressRecord>
    , IValidator<PostalArea, PersonDocumentValidator.PostalInfo>
{
    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        PersonDocument input,
        [NotNullWhen(true)] out NprPerson? validated)
    {
        context.TryValidateChild(
            path: "/identifikasjonsnummer",
            input.IdentificationNumber,
            ActiveElementValidator.Required<IdentificationNumberElement, PersonIdentifier, PersonDocumentValidator>(this),
            out PersonIdentifier? personIdentifier);

        context.TryValidateChild(
            path: "/status",
            input.Status,
            ActiveElementValidator.Required<PersonStatusElement, PersonStatus, PersonDocumentValidator>(this),
            out PersonStatus status);

        context.TryValidateChild(
            path: "/navn",
            input.Name,
            ActiveElementValidator.Optional<NameElement, PersonName, PersonDocumentValidator>(this),
            out Optional<PersonName> personNameOpt);

        context.TryValidateChild(
            path: "/foedsel",
            input.Birth,
            ActiveElementValidator.Optional<BirthElement, DateOnly, PersonDocumentValidator>(this),
            out Optional<DateOnly> dateOfBirth);

        context.TryValidateChild(
            path: "/doedsfall",
            input.Death,
            this,
            out PersonDeath death);

        context.TryValidateChild(
            path: "/adressebeskyttelse",
            input.AddressProtection,
            ActiveElementValidator.Optional<AddressProtectionElement, AddressConfidentialityLevel, PersonDocumentValidator>(this),
            out Optional<AddressConfidentialityLevel> addressProtectionLevel);

        PersonName personName;
        if (personNameOpt.HasValue)
        {
            personName = personNameOpt.Value;
        }
        else
        {
            personName = PersonName.Missing;
        }

        MailingAddressRecordExt? mailingAddress = null;
        StreetAddressRecord? address = null;
        if (!addressProtectionLevel.HasValue || addressProtectionLevel.Value is not (AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential))
        {
            context.TryValidateChild(
                path: "/bostedsadresse",
                input.RegisteredResidentialAddress,
                ActiveElementValidator.Optional<ResidentialAddressElement, StreetAddressRecord, PersonDocumentValidator>(this),
                out Optional<StreetAddressRecord> registeredResidentialAddress);

            address = registeredResidentialAddress.HasValue ? registeredResidentialAddress.Value : null;
            mailingAddress = TryGetMailingAddress(ref context, input, status);

            if (mailingAddress is { IsFirstLineCareOfAddress: true })
            {
                var hasGuardian = !input.GuardianshipOrPowerOfAttorney.IsDefaultOrEmpty;
                var prefix = hasGuardian ? "v/" : "c/o ";
                mailingAddress = mailingAddress with { Address = prefix + mailingAddress.Address };
            }
            else if (mailingAddress is not null && mailingAddress.IsEmpty)
            {
                mailingAddress = null;
            }

            if (address is not null && address.IsEmpty)
            {
                address = null;
            }
        }

        ImmutableArray<NprGuardianship>.Builder? guardiansBuilder = null;
        if (!input.GuardianshipOrPowerOfAttorney.IsDefaultOrEmpty)
        {
            context.TryValidateChild(
                path: "/vergemaalEllerFremtidsfullmakt",
                input.GuardianshipOrPowerOfAttorney,
                ActiveElementValidator.ArrayOfOptional<GuardianshipOrPowerOfAttorneyElement, NprGuardianship, PersonDocumentValidator>(this),
                out guardiansBuilder);
        }

        // fixup guardianships according to the requirement of NprPerson.Guardians
        ImmutableValueArray<NprGuardianship> guardians = [];
        if (guardiansBuilder is not null)
        {
            SortGuardianships(guardiansBuilder);

            if (guardiansBuilder.Count > 1)
            {
                MergeGuardianshipsForSameGuardian(guardiansBuilder);
            }

            guardians = guardiansBuilder.DrainToImmutableValueArray();
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        Debug.Assert(personIdentifier is not null);
        validated = new NprPerson
        {
            // Person identifiers
            PersonIdentifier = personIdentifier,

            // Name components
            DisplayName = personName.DisplayName,
            FirstName = personName.FirstName,
            MiddleName = personName.MiddleName,
            LastName = personName.LastName,
            ShortName = personName.ShortName,

            // Birth and death
            DateOfBirth = dateOfBirth.HasValue ? dateOfBirth.Value : null,
            DateOfDeath = death.DateOfDeath,

            // Addresses
            Address = address,
            MailingAddress = mailingAddress,

            // Guardianships
            Guardians = guardians,
        };
        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        GuardianshipOrPowerOfAttorneyElement input,
        [NotNullWhen(true)] out Optional<NprGuardianship> validated)
    {
        if (input.Guardianship is not null)
        {
            return context.TryValidateChild(
                path: "/verge",
                input.Guardianship,
                this,
                out validated);
        }

        // we ignore power of attorney entries
        validated = default;
        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        Guardianship input,
        [NotNullWhen(true)] out Optional<NprGuardianship> validated)
    {
        if (input.GuardianIdentifier is null || input.ServiceAreas.IsDefaultOrEmpty)
        {
            // ignore guardianships with no guardian or roles
            validated = default;
            return true;
        }

        context.TryValidateChild(
            path: "/foedselsEllerDNummer",
            input.GuardianIdentifier,
            default(PersonIdentifierValidator),
            out PersonIdentifier? guardianIdentifier);

        context.TryValidateChild(
            path: "/tjenesteomraade",
            input.ServiceAreas.AsEnumerable(),
            ListValidator.ForEnumerable<GuardianshipServiceArea, Optional<string>, PersonDocumentValidator>(this),
            out IEnumerable<Optional<string>>? roles);

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        Debug.Assert(guardianIdentifier is not null);
        Debug.Assert(roles is not null);
        validated = new NprGuardianship
        {
            Guardian = guardianIdentifier,
            Roles = ImmutableValueSet<string>.Create(
                roles.Where(static r => r.HasValue).Select(static r => r.Value),
                StringComparer.Ordinal),
        };
        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        GuardianshipServiceArea input,
        [NotNullWhen(true)] out Optional<string> validated)
    {
        if (string.IsNullOrEmpty(input.ServiceOwner))
        {
            context.AddChildProblem(StdValidationErrors.Required, path: "/vergeTjenestevirksomhet");
        }

        if (string.IsNullOrEmpty(input.ServiceTask))
        {
            context.AddChildProblem(StdValidationErrors.Required, path: "/vergeTjenesteoppgave");
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        Debug.Assert(input.ServiceOwner is not null);
        Debug.Assert(input.ServiceTask is not null);
        if (!GuardianshipRoleMapper.TryFindRoleByNprValues(
            vergeTjenestevirksomhet: input.ServiceOwner,
            vergeTjenesteoppgave: input.ServiceTask,
            out var role))
        {
            context.AddChildProblem(
                ValidationErrors.UnknownGuardianshipRole,
                path: ["/vergeTjenestevirksomhet", "/vergeTjenesteoppgave"],
                extensions: [new("vergeTjenestevirksomhet", input.ServiceOwner), new("vergeTjenesteoppgave", input.ServiceTask)],
                detail: $"No guardianship role found for service owner '{input.ServiceOwner}' and service task '{input.ServiceTask}'.");

            validated = default;
            return false;
        }

        // we support some combinations of tjenestevirksomhet and tjenesteoppgave which we do not create roles for.
        // TryFindRoleByNprValues returns true for these, while leaving the out variable as `null`.
        validated = default;
        if (role is not null)
        {
            validated = role.Identifier;
        }

        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        AddressProtectionElement input,
        [NotNullWhen(true)] out Optional<AddressConfidentialityLevel> validated)
    {
        if (!context.TryValidateChild(path: "/graderingsnivaa", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        validated = level;
        return true;
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        PersonStatusElement input,
        [NotNullWhen(true)] out PersonStatus validated)
    {
        const string PATH = "/status";

        if (input.Status is null)
        {
            context.AddChildProblem(StdValidationErrors.Required, path: PATH);
            validated = default;
            return false;
        }

        return context.TryValidateChild(
            path: PATH,
            input.Status.Value,
            default(NonExhaustiveEnumValidator<PersonStatus>),
            out validated);
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        IdentificationNumberElement input,
        [NotNullWhen(true)] out PersonIdentifier? validated)
    {
        const string PATH = "/identifikasjonsnummer";

        if (string.IsNullOrEmpty(input.IdentificationNumber))
        {
            context.AddChildProblem(StdValidationErrors.Required, path: PATH);
            validated = default;
            return false;
        }

        return context.TryValidateChild(path: PATH, input.IdentificationNumber, default(PersonIdentifierValidator), out validated);
    }

    /// <inheritdoc/>
    public bool TryValidate(
        ref ValidationContext context,
        BirthElement input,
        [NotNullWhen(true)] out Optional<DateOnly> validated)
    {
        const string PATH = "/foedselsdato";

        if (string.IsNullOrEmpty(input.DateOfBirth))
        {
            // there exist birth-elements with no date of birth, we treat these as if the element itself was missing
            validated = default;
            return true;
        }

        if (!DateOnly.TryParseExact(input.DateOfBirth, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validatedDate))
        {
            context.AddChildProblem(ValidationErrors.InvalidDate, path: PATH, detail: $"The value '{input.DateOfBirth}' is not a valid date.");
            validated = default;
            return false;
        }

        validated = validatedDate;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<NameElement, Optional<PersonName>>.TryValidate(
        ref ValidationContext context,
        NameElement input,
        out Optional<PersonName> validated)
    {
        var firstName = input.FirstName?.Trim();
        var middleName = input.MiddleName?.Trim();
        var lastName = input.LastName?.Trim();

        if (string.IsNullOrEmpty(firstName))
        {
            context.AddChildProblem(StdValidationErrors.Required, path: "/fornavn");
        }

        if (string.IsNullOrEmpty(lastName))
        {
            context.AddChildProblem(StdValidationErrors.Required, path: "/etternavn");
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        var shortName = input.ShortName;

        Debug.Assert(firstName is not null);
        Debug.Assert(lastName is not null);
        validated = PersonName.Create(
            firstName: firstName,
            middleName: middleName,
            lastName: lastName,
            shortName: shortName);

        return true;
    }

    /// <inheritdoc/>
    bool IValidator<DeathElement?, PersonDeath>.TryValidate(
        ref ValidationContext context,
        DeathElement? input,
        out PersonDeath validated)
    {
        const string PATH = "/doedsdato";

        if (input is null)
        {
            validated = new(null);
            return true;
        }

        if (string.IsNullOrEmpty(input.DateOfDeath))
        {
            validated = default;
            return true;
        }

        if (!DateOnly.TryParseExact(input.DateOfDeath, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            context.AddChildProblem(ValidationErrors.InvalidDate, path: PATH, detail: $"The value '{input.DateOfDeath}' is not a valid date.");
            validated = default;
            return false;
        }

        validated = new(parsedDate);
        return true;
    }

    private MailingAddressRecordExt? TryGetMailingAddress(
        ref ValidationContext context,
        PersonDocument input,
        PersonStatus status)
    {
        MailingAddressRecordExt? result;
        return status switch
        {
            _ when TryAddress(ref context, path: "/postadresse", input.MailingAddress, this, out result) => result,
            _ when TryAddress(ref context, path: "/oppholdsadresse", input.CurrentStayAddress, this, out result) => result,
            PersonStatus.Emigrated when TryAddress(ref context, path: "/utenlandskAdresse", input.InternationalMailingAddress, this, out result) => result,
            _ when TryAddress(ref context, path: "/bostedsadresse", input.RegisteredResidentialAddress, this, out result) => result,
            not PersonStatus.Emigrated when TryAddress(ref context, path: "/utenlandskAdresse", input.InternationalMailingAddress, this, out result) => result,
            _ => null,
        };

        static bool TryAddress<TIn, TValidator>(
            ref ValidationContext context,
            string path,
            ActiveElement<TIn> input,
            TValidator validator,
            [NotNullWhen(true)] out MailingAddressRecordExt? validated)
            where TIn : HistoricalElement
            where TValidator : IValidator<TIn, Optional<MailingAddressRecordExt>>
        {
            if (!context.TryValidateChild(path: path, input, ActiveElementValidator.Optional<TIn, MailingAddressRecordExt, TValidator>(validator), out Optional<MailingAddressRecordExt> address))
            {
                validated = null;
                return false;
            }

            validated = address.HasValue ? address.Value : null;
            return address.HasValue;
        }
    }

    /// <inheritdoc/>
    bool IValidator<MailingAddressElement, Optional<MailingAddressRecordExt>>.TryValidate(
        ref ValidationContext context,
        MailingAddressElement input,
        [NotNullWhen(true)] out Optional<MailingAddressRecordExt> validated)
    {
        if (!context.TryValidateChild(path: "/adressegradering", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        if (level is AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential)
        {
            validated = default;
            return true;
        }

        if (input.FreeFormMailingAddress is not null)
        {
            if (context.TryValidateChild(path: "/postadresseIFrittFormat", input.FreeFormMailingAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.StreetAddress is not null)
        {
            if (context.TryValidateChild(path: "/vegadresse", input.StreetAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.PostboxAddress is not null)
        {
            if (context.TryValidateChild(path: "/postboksadresse", input.PostboxAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        validated = default;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<CurrentStayAddressElement, Optional<MailingAddressRecordExt>>.TryValidate(
        ref ValidationContext context,
        CurrentStayAddressElement input,
        [NotNullWhen(true)] out Optional<MailingAddressRecordExt> validated)
    {
        if (!context.TryValidateChild(path: "/adressegradering", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        if (level is AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential)
        {
            validated = default;
            return true;
        }

        if (input.IsUnknown)
        {
            validated = default;
            return true;
        }

        if (input.MatrikkelAddress is not null)
        {
            if (context.TryValidateChild(path: "/matrikkeladresse", input.MatrikkelAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.StreetAddress is not null)
        {
            if (context.TryValidateChild(path: "/vegadresse", input.StreetAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.InternationalMailingAddress is not null)
        {
            if (context.TryValidateChild(path: "/utenlandskAdresse", input.InternationalMailingAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        validated = default;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<ResidentialAddressElement, Optional<MailingAddressRecordExt>>.TryValidate(
        ref ValidationContext context,
        ResidentialAddressElement input,
        [NotNullWhen(true)] out Optional<MailingAddressRecordExt> validated)
    {
        if (!context.TryValidateChild(path: "/adressegradering", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        if (level is AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential)
        {
            validated = default;
            return true;
        }

        if (input.UnknownResidentialAddress is not null)
        {
            validated = new MailingAddressRecordExt
            {
                Address = null,
                PostalCode = null,
                City = null,
                IsFirstLineCareOfAddress = false,
            };
            return true;
        }

        if (input.StreetAddress is not null)
        {
            if (context.TryValidateChild(path: "/vegadresse", input.StreetAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.MatrikkelAddress is not null)
        {
            if (context.TryValidateChild(path: "/matrikkeladresse", input.MatrikkelAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        validated = default;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<ResidentialAddressElement, Optional<StreetAddressRecord>>.TryValidate(
        ref ValidationContext context,
        ResidentialAddressElement input,
        out Optional<StreetAddressRecord> validated)
    {
        if (!context.TryValidateChild(path: "/adressegradering", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        if (level is AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential)
        {
            validated = default;
            return true;
        }

        if (input.UnknownResidentialAddress is not null)
        {
            validated = new StreetAddressRecord
            {
                // All nulls
            };
            return true;
        }

        if (input.StreetAddress is not null)
        {
            if (context.TryValidateChild(path: "/vegadresse", input.StreetAddress, this, out StreetAddressRecord? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.MatrikkelAddress is not null)
        {
            if (context.TryValidateChild(path: "/matrikkeladresse", input.MatrikkelAddress, this, out StreetAddressRecord? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        validated = default;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<InternationalMailingAddressElement, Optional<MailingAddressRecordExt>>.TryValidate(
        ref ValidationContext context,
        InternationalMailingAddressElement input,
        [NotNullWhen(true)] out Optional<MailingAddressRecordExt> validated)
    {
        if (!context.TryValidateChild(path: "/adressegradering", input.ConfidentialityLevel, default(NonExhaustiveEnumValidator<AddressConfidentialityLevel>), out AddressConfidentialityLevel level))
        {
            validated = default;
            return false;
        }

        if (level is AddressConfidentialityLevel.Confidential or AddressConfidentialityLevel.StrictlyConfidential)
        {
            validated = default;
            return true;
        }

        if (input.FreeFormInternationalMailingAddress is not null)
        {
            if (context.TryValidateChild(path: "/utenlandskAdresseIFrittFormat", input.FreeFormInternationalMailingAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        if (input.InternationalMailingAddress is not null)
        {
            if (context.TryValidateChild(path: "/utenlandskAdresse", input.InternationalMailingAddress, this, out MailingAddressRecordExt? result))
            {
                validated = result;
                return true;
            }

            validated = default;
            return false;
        }

        validated = default;
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<FreeFormMailingAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        FreeFormMailingAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        List<string> addressLines;
        if (input.AddressLines.IsDefaultOrEmpty)
        {
            addressLines = [];
        }
        else
        {
            addressLines = [.. input.AddressLines];
        }

        if (addressLines.Count > 0 && (postalInfo.Code is null || !addressLines[^1].StartsWith(postalInfo.Code, StringComparison.Ordinal)))
        {
            addressLines.Add($"{postalInfo.Code} {postalInfo.Name}".Trim());
        }

        var address = string.Join(' ', addressLines).Trim();
        validated = new MailingAddressRecordExt
        {
            Address = address,
            PostalCode = postalInfo.Code,
            City = postalInfo.Name,
            IsFirstLineCareOfAddress = false,
        };

        return true;
    }

    /// <inheritdoc/>
    bool IValidator<StreetAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        StreetAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        validated = BuildMailingAddressFromStreetAddress(
            streetName: input.StreetName,
            houseNumber: input.StreetNumber?.HouseNumber,
            houseLetter: input.StreetNumber?.HouseLetter,
            postal: postalInfo,
            coAddressName: input.CareOfAddressName,
            additionalAddressName: input.AdditionalAddressName);
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<StreetAddress, StreetAddressRecord>.TryValidate(
        ref ValidationContext context,
        StreetAddress input,
        [NotNullWhen(true)] out StreetAddressRecord? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        TryGetMunicipalName(input.MunicipalNumber, out var municipalName);

        validated = new StreetAddressRecord
        {
            MunicipalNumber = input.MunicipalNumber,
            MunicipalName = municipalName,
            StreetName = input.StreetName,
            HouseNumber = input.StreetNumber?.HouseNumber,
            HouseLetter = input.StreetNumber?.HouseLetter,
            PostalCode = postalInfo.Code,
            City = postalInfo.Name,
        };
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<PostBoxAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        PostBoxAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        validated = BuildMailingAddressFromPostBoxAddress(
            postBoxOwner: input.Owner,
            postBox: input.PostBox,
            postal: postalInfo);
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<InternationalFreeFormMailingAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        InternationalFreeFormMailingAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        List<string> addressLines;
        if (input.AddressLines.IsDefaultOrEmpty)
        {
            addressLines = [];
        }
        else
        {
            addressLines = [.. input.AddressLines];
        }

        TryLookupCountryName(input.CountryCode, out string? countryDisplay);

        var cityOrPlaceName = input.CityOrPlaceName?.Trim();
        if (!string.IsNullOrEmpty(cityOrPlaceName)
            && !addressLines.Any(line => line.Contains(cityOrPlaceName, StringComparison.OrdinalIgnoreCase)))
        {
            addressLines.Add(cityOrPlaceName);
        }

        if (!string.IsNullOrEmpty(countryDisplay)
            && !addressLines.Any(line => line.Contains(countryDisplay, StringComparison.OrdinalIgnoreCase)))
        {
            addressLines.Add(countryDisplay);
        }

        validated = new MailingAddressRecordExt
        {
            Address = string.Join(' ', addressLines),
            PostalCode = input.PostalCode,
            City = cityOrPlaceName,
            IsFirstLineCareOfAddress = false,
        };
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<InternationalMailingAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        InternationalMailingAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        TryLookupCountryName(input.CountryCode, out string? countryDisplay);

        bool isFirstLineCo = false;
        List<string> addressLines = [];
        if (!string.IsNullOrWhiteSpace(input.CareOfAddressName))
        {
            addressLines.Add(RemoveCareOfPrefix(input.CareOfAddressName));
            isFirstLineCo = true;
        }

        if (!string.IsNullOrWhiteSpace(input.PostBox))
        {
            addressLines.Add(AddPostboxIfNeeded(input.PostBox, true));
        }
        else
        {
            string lineAddressName = string.Join(
                ' ',
                Enumerable.Where(
                    [input.AddressName, input.UnitName, input.FloorNumber],
                    static s => !string.IsNullOrWhiteSpace(s)));

            addressLines.Add(string.Join(", ", Enumerable.Where(
                [input.Building, lineAddressName],
                static s => !string.IsNullOrWhiteSpace(s))));
        }

        string? postCode = GetInternationalPostalCode(input.CountryCode, input.PostalCode);

        addressLines.Add(string.Join(' ', Enumerable.Where(
            [postCode, input.CityOrPlaceName],
            static s => !string.IsNullOrWhiteSpace(s))));

        if (!string.IsNullOrWhiteSpace(countryDisplay))
        {
            addressLines.Add(countryDisplay);
        }

        validated = new MailingAddressRecordExt
        {
            Address = string.Join(' ', addressLines),
            PostalCode = postCode,
            City = input.CityOrPlaceName?.Trim(),
            IsFirstLineCareOfAddress = isFirstLineCo,
        };
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<MatrikkelAddress, MailingAddressRecordExt>.TryValidate(
        ref ValidationContext context,
        MatrikkelAddress input,
        [NotNullWhen(true)] out MailingAddressRecordExt? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        List<string> addressLines = new List<string>();
        bool isCoAddress = false;

        if (postalInfo.Code == null)
        {
            postalInfo = postalInfo with { Code = "0000" };
        }

        if (!string.IsNullOrWhiteSpace(input.CareOfAddressName))
        {
            addressLines.Add(RemoveCareOfPrefix(input.CareOfAddressName));
            isCoAddress = true;
        }

        if (!string.IsNullOrWhiteSpace(input.AddressAdditionalName))
        {
            addressLines.Add(input.AddressAdditionalName.Trim());
        }

        // Cadastral number (matrikkeladresse) format:
        // municipality no.-land no./title no./share no./sub no.
        var cadastralNumber = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(input.MatrikkelNumber?.MunicipalityNumber))
        {
            cadastralNumber.Append(input.MatrikkelNumber.MunicipalityNumber.Trim()).Append('-');
        }

        if (input.MatrikkelNumber?.PropertyNumber is { } propertyNumber)
        {
            cadastralNumber.Append($"{propertyNumber}/");
        }

        if (input.MatrikkelNumber?.TitleNumber is { } titleNumber)
        {
            cadastralNumber.Append($"{titleNumber}/");
        }

        if (input.MatrikkelNumber?.LeaseNumber is { } leaseNumber)
        {
            cadastralNumber.Append($"{leaseNumber}/");
        }

        if (input.SubNumber is { } subNumber)
        {
            cadastralNumber.Append($"{subNumber}");
        }

        while (cadastralNumber.Length > 0 && (cadastralNumber[^1] == '-' || cadastralNumber[^1] == '/'))
        {
            cadastralNumber.Length--;
        }

        if (cadastralNumber.Length > 0)
        {
            addressLines.Add(cadastralNumber.ToString());
        }

        if (postalInfo.Code != "0000" && !string.IsNullOrEmpty(postalInfo.Name))
        {
            addressLines.Add(postalInfo.Code.Trim().PadLeft(4, '0') + " " + postalInfo.Name.Trim());
        }

        validated = new MailingAddressRecordExt
        {
            Address = string.Join(' ', addressLines),
            PostalCode = postalInfo.Code,
            City = postalInfo.Name,
            IsFirstLineCareOfAddress = isCoAddress,
        };
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<MatrikkelAddress, StreetAddressRecord>.TryValidate(
        ref ValidationContext context,
        MatrikkelAddress input,
        [NotNullWhen(true)] out StreetAddressRecord? validated)
    {
        PostalInfo postalInfo = default;
        if (input.PostalArea is not null)
        {
            context.TryValidateChild(path: "/poststed", input.PostalArea, this, out postalInfo);
        }

        string? municipalNumber = null;
        string? municipalName = null;
        if (input.MatrikkelNumber?.MunicipalityNumber is { } mn)
        {
            municipalNumber = mn.Trim();
            TryGetMunicipalName(municipalNumber, out municipalName);
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        validated = new StreetAddressRecord
        {
            MunicipalNumber = municipalNumber,
            MunicipalName = municipalName,
            PostalCode = postalInfo.Code,
            City = postalInfo.Name,
        };
        return true;
    }

    /// <inheritdoc/>
    bool IValidator<PostalArea, PostalInfo>.TryValidate(
        ref ValidationContext context,
        PostalArea input,
        out PostalInfo validated)
    {
        if (input.PostalCode is not null && !IsDigitsOnly(input.PostalCode))
        {
            context.AddChildProblem(ValidationErrors.InvalidValue, path: "/postnummer", detail: $"The postal code '{input.PostalCode}' is not valid. It must contain digits only.");
        }

        if (context.HasErrors)
        {
            validated = default;
            return false;
        }

        validated = new(
            Code: input.PostalCode?.PadLeft(4, '0'),
            Name: input.PostalName);
        return true;
    }

    private readonly record struct PersonDeath(DateOnly? DateOfDeath);

    private readonly record struct PostalInfo(
        string? Code,
        string? Name);

    private static bool IsDigitsOnly(ReadOnlySpan<char> str)
        => !str.ContainsAnyExcept(DigitsOnly);

    private static readonly SearchValues<char> DigitsOnly
        = SearchValues.Create("0123456789".AsSpan());

    private sealed record MailingAddressRecordExt
        : MailingAddressRecord
    {
        public bool IsFirstLineCareOfAddress { get; init; }
    }

    private static MailingAddressRecordExt BuildMailingAddressFromStreetAddress(
        string? streetName,
        string? houseNumber,
        string? houseLetter,
        PostalInfo postal,
        string? coAddressName,
        string? additionalAddressName)
    {
        List<string> addressLines = [];
        bool isFirstLineCoAddress = false;

        if (!string.IsNullOrEmpty(coAddressName))
        {
            addressLines.Add(RemoveCareOfPrefix(coAddressName));
            isFirstLineCoAddress = true;
        }

        if (!string.IsNullOrEmpty(additionalAddressName))
        {
            addressLines.Add(additionalAddressName);
        }

        if (!string.IsNullOrEmpty(streetName))
        {
            string streetLine = streetName;

            if (!string.IsNullOrEmpty(houseNumber))
            {
                streetLine += ' ' + houseNumber.Trim();

                if (!string.IsNullOrEmpty(houseLetter))
                {
                    streetLine += houseLetter;
                }
            }

            addressLines.Add(streetLine);
        }

        if (postal.Code != "0000" && !string.IsNullOrEmpty(postal.Name))
        {
            addressLines.Add($"{postal.Code} {postal.Name}");
        }

        return new MailingAddressRecordExt
        {
            Address = string.Join(' ', addressLines),
            PostalCode = postal.Code,
            City = postal.Name,
            IsFirstLineCareOfAddress = isFirstLineCoAddress,
        };
    }

    private static MailingAddressRecordExt BuildMailingAddressFromPostBoxAddress(
        string? postBoxOwner,
        string? postBox,
        PostalInfo postal)
    {
        List<string> addressLines = new List<string>();
        bool isFirstLineCoAddress = false;

        if (!string.IsNullOrEmpty(postBoxOwner))
        {
            addressLines.Add(RemoveCareOfPrefix(postBoxOwner));
            isFirstLineCoAddress = true;
        }

        if (!string.IsNullOrWhiteSpace(postBox))
        {
            addressLines.Add(AddPostboxIfNeeded(postBox, false));
        }

        if (postal.Code != "0000" && !string.IsNullOrEmpty(postal.Name))
        {
            addressLines.Add($"{postal.Code} {postal.Name}");
        }

        return new MailingAddressRecordExt
        {
            Address = string.Join(' ', addressLines),
            PostalCode = postal.Code,
            City = postal.Name,
            IsFirstLineCareOfAddress = isFirstLineCoAddress,
        };
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
        foreach (var prefix in CareOfPrefixes)
        {
            if (input.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return input[prefix.Length..].Trim();
            }
        }

        return input.Trim();
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

    private static string AddPostboxIfNeeded(string postBoxAddress, bool isForeign)
    {
        foreach (var prefix in PostBoxPrefixes)
        {
            if (postBoxAddress.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                postBoxAddress = postBoxAddress[prefix.Length..].Trim();
                break;
            }
        }

        string postBoxPrefix = isForeign ? "Postbox" : "Postboks";
        return $"{postBoxPrefix} {postBoxAddress}".Trim();
    }

    private bool TryLookupCountryName(string? countryCode, out string? countryDisplay)
    {
        if (string.IsNullOrEmpty(countryCode))
        {
            countryDisplay = null;
            return false;
        }

        if (lookup.TryGetCountry(countryCode, out var country))
        {
            countryDisplay = country.Name;
            return true;
        }

        // fallback, use the passed in country code
        countryDisplay = countryCode;
        return true;
    }

    private bool TryGetMunicipalName(string? municipalNumber, out string? municipalName)
    {
        if (string.IsNullOrEmpty(municipalNumber))
        {
            municipalName = null;
            return false;
        }

        if (lookup.TryGetMunicipality(municipalNumber, out var municipality))
        {
            municipalName = municipality.Name;
            return true;
        }

        municipalName = null;
        return false;
    }

    private static string? GetInternationalPostalCode(string? countryCode, string? postalCode)
    {
        postalCode = postalCode?.Trim();

        if (string.IsNullOrEmpty(countryCode)
            || string.IsNullOrEmpty(postalCode)
            || !IsDigitsOnly(postalCode))
        {
            return postalCode;
        }

        return $"{countryCode.Trim().ToUpperInvariant()}-{postalCode}";
    }

    private static void SortGuardianships(ImmutableArray<NprGuardianship>.Builder guardians)
    {
        guardians.Sort(static (g1, g2) => string.CompareOrdinal(g1.Guardian.ToString(), g2.Guardian.ToString()));
    }

    private static void MergeGuardianshipsForSameGuardian(ImmutableArray<NprGuardianship>.Builder guardians)
    {
        HashSet<PersonIdentifier> guardianIds = new();

        var hasDuplicates = false;
        foreach (var guardianship in guardians)
        {
            if (!guardianIds.Add(guardianship.Guardian))
            {
                hasDuplicates = true;
                break;
            }
        }

        if (!hasDuplicates)
        {
            return;
        }

        var merged = guardians
            .GroupBy(g => g.Guardian)
            .Select(Merge)
            .ToList();

        guardians.Clear();
        guardians.AddRange(merged);
        SortGuardianships(guardians);

        static NprGuardianship Merge(IGrouping<PersonIdentifier, NprGuardianship> group)
        {
            var enumerator = group.GetEnumerator();
            enumerator.MoveNext(); // guaranteed to have at least one element

            var result = enumerator.Current;
            while (enumerator.MoveNext())
            {
                result = result with { Roles = result.Roles.UnionWith(enumerator.Current.Roles) };
            }

            return result;
        }
    }
}
