using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ProblemDetails;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using CommunityToolkit.Diagnostics;
using V1Models = Altinn.Register.Contracts.V1;
using V1PartyType = Altinn.Register.Contracts.V1.PartyType;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Implementation of <see cref="IA2PartyImportService"/>.
/// </summary>
internal sealed partial class A2PartyImportService
    : IA2PartyImportService
{
    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<A2PartyImportService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2PartyImportService"/> class.
    /// </summary>
    public A2PartyImportService(
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<A2PartyImportService> logger)
    {
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2PartyChangePage> GetChanges(
        uint fromExclusive = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PartyChangesResponse response;
        while (true)
        {
            response = await GetPartyChangesPage(fromExclusive, cancellationToken);

            if (response.PartyChangeList.Count == 0)
            {
                break;
            }

            yield return MapPartyChangePage(response);

            Assert(response.LastChangeInSegment > fromExclusive);
            fromExclusive = response.LastChangeInSegment;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<A2UserProfileChangePage> GetUserProfileChanges(
        uint fromExclusive = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ItemStreamPage<UserProfileEvent> response;
        while (true)
        {
            response = await GetUserProfileChangesPage(fromExclusive, cancellationToken);

            if (response.Data.Count == 0)
            {
                break;
            }

            Assert(response.Stats.PageEnd is { } pageEnd && pageEnd > fromExclusive);
            yield return MapUserProfileChangePage(response);

            fromExclusive = response.Stats.PageEnd.Value;
        }
    }

    /// <inheritdoc />
    public async Task<Result<PartyRecord>> GetParty(Guid partyUuid, CancellationToken cancellationToken = default)
    {
        var url = $"register/api/parties?ignoreCache=true&partyuuid={partyUuid}";

        using var responseMessage = await _httpClient.GetAsync(url, cancellationToken);
        if (responseMessage.StatusCode == HttpStatusCode.Gone)
        {
            return Problems.PartyGone.Create([
                new("partyUuid", partyUuid.ToString()),
                new("source", "a2-party"),
            ]);
        }

        if (responseMessage.StatusCode == HttpStatusCode.NotFound)
        {
            return Problems.ReferencedPartyNotFound.Create([
                new("partyUuid", partyUuid.ToString()),
                new("source", "a2-party"),
            ]);
        }

        if (!responseMessage.IsSuccessStatusCode)
        {
            return Problems.PartyFetchFailed.Create([
                new("partyUuid", partyUuid.ToString()),
                new("source", "a2-party"),
                new("http.status", ((int)responseMessage.StatusCode).ToString()),
            ]);
        }

        var response = await responseMessage.Content.ReadFromJsonAsync<V1Models.Party>(_options, cancellationToken);
        if (response is null)
        {
            return Problems.PartyFetchFailed.Create([
                new("partyUuid", partyUuid.ToString()),
                new("source", "a2-party"),
                new("error", "Failed to parse party response."),
            ]);
        }

        Assert(response.PartyUuid == partyUuid);
        return MapParty(response);
    }

    /// <inheritdoc />
    public Task<Result<A2ProfileRecord>> GetOrCreatePersonUser(Guid partyUuid, CancellationToken cancellationToken = default)
    {
        var url = $"profile/api/users/getorcreate/{partyUuid}";

        return GetPartyUser(url, partyUuid, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<A2ProfileRecord>> GetPartyUser(Guid partyUuid, CancellationToken cancellationToken = default)
    {
        var url = $"profile/api/users?userUUID={partyUuid}";

        return GetPartyUser(url, partyUuid, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<A2ProfileRecord>> GetProfile(ulong userId, CancellationToken cancellationToken = default)
    {
        var url = $"profile/api/users/{userId}";

        var profileResult = await GetPartyProfile(url, [new("userId", userId.ToString())], cancellationToken);
        if (profileResult.IsProblem)
        {
            return profileResult.Problem;
        }

        var profile = profileResult.Value;
        Assert(profile.UserId == userId);
        return MapProfile(profile);
    }

    private async Task<Result<A2ProfileRecord>> GetPartyUser(
        string url,
        Guid partyUuid,
        CancellationToken cancellationToken)
    {
        var profileResult = await GetPartyProfile(
            url,
            [
                new("partyUuid", partyUuid.ToString()),
                new("source", "a2-user"),
            ],
            cancellationToken);
        if (profileResult.IsProblem)
        {
            return profileResult.Problem;
        }

        var profile = profileResult.Value;
        Assert(profile.UserUuid == partyUuid);
        Assert(profile.Party.PartyUuid == partyUuid);
        return MapProfile(profile);
    }

    private async Task<Result<PartyProfile>> GetPartyProfile(
        string url,
        ProblemExtensionData diagnostics,
        CancellationToken cancellationToken)
    {
        using var responseMessage = await _httpClient.GetAsync(url, cancellationToken);
        if (responseMessage.StatusCode == HttpStatusCode.Gone)
        {
            return Problems.PartyGone.Create(diagnostics);
        }

        if (responseMessage.StatusCode == HttpStatusCode.NotFound)
        {
            return Problems.ReferencedPartyNotFound.Create(diagnostics);
        }

        if (!responseMessage.IsSuccessStatusCode)
        {
            Log.FailedToFetchPartyProfile(_logger, url, responseMessage.StatusCode);
            return Problems.PartyFetchFailed.Create([.. diagnostics, new("http.status", ((int)responseMessage.StatusCode).ToString())]);
        }

        PartyProfile? response;
        try
        {
            response = await responseMessage.Content.ReadFromJsonAsync<PartyProfile>(_options, cancellationToken);
        }
        catch (JsonException ex)
        {
            Log.FailedToDeserializePartyProfile(_logger, url, ex);
            return Problems.PartyFetchFailed.Create([.. diagnostics, new("error", "Failed to parse party response.")]);
        }

        if (response is null)
        {
            return Problems.PartyFetchFailed.Create(diagnostics);
        }

        return response;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<A2PartyExternalRoleAssignment> GetExternalRoleAssignmentsFrom(
        uint fromPartyId,
        Guid fromPartyUuid,
        CancellationToken cancellationToken = default)
    {
        return Core(fromPartyId, fromPartyUuid, cancellationToken).Distinct();

        async IAsyncEnumerable<A2PartyExternalRoleAssignment> Core(
            uint fromPartyId,
            Guid fromPartyUuid,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var url = $"register/api/parties/partyroles/{fromPartyId}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);

            // TODO: remove once SBL bridge is fixed.
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // SBL bridge return 404 if no role-assignments exist.
                yield break;
            }

            await foreach (var item in response.Content.ReadFromJsonAsAsyncEnumerable<PartyRoleAssignmentItem>(_options, cancellationToken))
            {
                if (item is null)
                {
                    ThrowHelper.ThrowInvalidOperationException("Failed to parse party role assignment.");
                }

                var assignment = new A2PartyExternalRoleAssignment
                {
                    ToPartyUuid = item.ToPartyUuid,
                    RoleCode = item.RoleCode,
                };

                yield return assignment;

                switch (item.RoleCode)
                {
                    case "KOMK":
                    case "SREVA":
                    case "KNUF":
                    case "KEMN":
                        assignment = assignment with { RoleCode = "KONT" };
                        yield return assignment;
                        break;
                }
            }
        }
    }

    private async Task<PartyChangesResponse> GetPartyChangesPage(uint fromExclusive, CancellationToken cancellationToken)
    {
        var url = $"register/api/parties/partychanges/{fromExclusive}";

        Log.FetchingPartyChangesPage(_logger, fromExclusive);
        var response = await _httpClient.GetFromJsonAsync<PartyChangesResponse>(url, _options, cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("Failed to parse party changes.");
        }

        return response;
    }

    private async Task<ItemStreamPage<UserProfileEvent>> GetUserProfileChangesPage(uint fromExclusive, CancellationToken cancellationToken)
    {
        var url = $"profile/api/userprofileevents?eventId={fromExclusive + 1}";

        Log.FetchingUserProfileChangesPage(_logger, fromExclusive);
        var response = await _httpClient.GetFromJsonAsync<ItemStreamPage<UserProfileEvent>>(url, _options, cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException("Failed to parse user profile changes.");
        }

        if (response.Stats is { PageStart: null, PageEnd: null })
        {
            var (pageStart, pageEnd) = response.Data.Count switch
            {
                0 => (response.Stats.SequenceMax, response.Stats.SequenceMax),
                _ => (response.Data[0].UserChangeEventId, response.Data[^1].UserChangeEventId),
            };

            response = response with
            {
                Stats = response.Stats with
                {
                    PageStart = pageStart,
                    PageEnd = pageEnd,
                },
            };
        }

        return response;
    }

    private A2ProfileRecord MapProfile(PartyProfile profile)
    {
        var userId = checked((uint)profile.UserId);
        var userName = Normalize(profile.UserName);
        var userUuid = profile.UserUuid;
        var profileType = MapUserType(profile.UserType);
        var partyUuid = profile.Party.PartyUuid;
        var partyId = profile.Party.PartyId;

        return new A2ProfileRecord
        {
            UserId = userId,
            UserUuid = userUuid,
            IsActive = profile.IsActive,
            UserName = userName,
            ProfileType = profileType,
            PartyUuid = partyUuid,
            PartyId = checked((uint)partyId),
            ExternalAuthenticationReference = Normalize(profile.ExternalIdentity),
            LastChangedAt = profile.Party.LastChangedInExternalRegister ?? profile.Party.LastChangedInAltinn,
        };
    }

    private static A2UserProfileType MapUserType(byte value)
        => value switch
        {
            1 => A2UserProfileType.Person,
            2 => A2UserProfileType.SelfIdentifiedUser,
            3 => A2UserProfileType.EnterpriseUser,
            _ => ThrowHelper.ThrowArgumentOutOfRangeException<A2UserProfileType>(nameof(value), value, "Unknown user type."),
        };

    private A2PartyChangePage MapPartyChangePage(PartyChangesResponse response)
    {
        var changes = response.PartyChangeList.Select(static change => new A2PartyChange
        {
            ChangeId = change.ChangeId,
            PartyId = change.PartyId,
            PartyUuid = change.PartyUuid,
            ChangeTime = change.LastChangedTime,
        }).ToImmutableArray();

        return new A2PartyChangePage(changes, response.LastAvailableChange);
    }

    private A2UserProfileChangePage MapUserProfileChangePage(ItemStreamPage<UserProfileEvent> response)
    {
        var changes = response.Data.Select(static change => new A2UserProfileChange
        {
            UserId = change.UserId,
            ChangeId = change.UserChangeEventId,
            UserUuid = change.UserUuid,
            OwnerPartyUuid = change.OwnerPartyUuid,
            UserName = change.UserName,
            IsDeleted = change.IsDeleted,
            ProfileType = change.UserType switch
            {
                UserProfileType.Person => A2UserProfileType.Person,
                UserProfileType.Enterprise => A2UserProfileType.EnterpriseUser,
                UserProfileType.SelfIdentified => A2UserProfileType.SelfIdentifiedUser,
                _ => ThrowHelper.ThrowNotSupportedException<A2UserProfileType>($"User profile type {change.UserType} is not supported."),
            },
        }).ToImmutableArray();

        return new(changes, response.Stats.SequenceMax);
    }

    private PartyRecord MapParty(V1Models.Party party)
    {
        return party.PartyTypeName switch
        {
            V1PartyType.Person => MapPerson(party, _timeProvider.GetUtcNow()),
            V1PartyType.Organisation => MapOrganization(party, _timeProvider.GetUtcNow()),
            V1PartyType.SelfIdentified => MapSelfIdentifiedUser(party, _timeProvider.GetUtcNow()),
            _ => ThrowHelper.ThrowNotSupportedException<PartyRecord>($"Party type {party.PartyTypeName} is not supported."),
        };

        static StreetAddressRecord? MapStreetAddress(
            string? municipalNumber,
            string? municipalName,
            string? streetName,
            string? houseNumber,
            string? houseLetter,
            string? postalCode,
            string? city)
        {
            municipalNumber = Normalize(municipalNumber);
            municipalName = Normalize(municipalName);
            streetName = Normalize(streetName);
            houseNumber = Normalize(houseNumber);
            houseLetter = Normalize(houseLetter);
            postalCode = Normalize(postalCode);
            city = Normalize(city);

            if (municipalNumber is null
                && municipalName is null
                && streetName is null
                && houseNumber is null
                && houseLetter is null
                && postalCode is null
                && city is null)
            {
                return null;
            }

            return new StreetAddressRecord
            {
                MunicipalNumber = municipalNumber,
                MunicipalName = municipalName,
                StreetName = streetName,
                HouseNumber = houseNumber,
                HouseLetter = houseLetter,
                PostalCode = postalCode,
                City = city,
            };
        }

        static MailingAddressRecord? MapMailingAddress(
            string? address,
            string? postalCode,
            string? city)
        {
            address = Normalize(address);
            postalCode = Normalize(postalCode);
            city = Normalize(city);

            if (address is null
                && postalCode is null
                && city is null)
            {
                return null;
            }

            return new MailingAddressRecord
            {
                Address = address,
                PostalCode = postalCode,
                City = city,
            };
        }

        static DateOnly? CalculateDateOfBirth(PersonIdentifier personIdentifier)
        {
            var s = personIdentifier.ToString().AsSpan();
            var d1 = s[0] - '0';
            var d2 = s[1] - '0';
            var m1 = s[2] - '0';
            var m2 = s[3] - '0';
            var y1 = s[4] - '0';
            var y2 = s[5] - '0';
            var i1 = s[6] - '0';
            var i2 = s[7] - '0';
            var i3 = s[8] - '0';

            var dayComponent = (d1 * 10) + d2;
            var monthComponent = (m1 * 10) + m2;
            var yearComponent = (y1 * 10) + y2;
            var individualComponent = (i1 * 100) + (i2 * 10) + i3;

            if (monthComponent >= 80)
            {
                // Test person
                monthComponent -= 80;
            }

            if (dayComponent >= 40)
            {
                // D-number
                dayComponent -= 40;
            }

            var year = individualComponent switch
            {
                >= 500 and < 750 when yearComponent > 54 => 1800 + yearComponent,
                >= 900 when yearComponent > 39 => 1900 + yearComponent,
                >= 500 when yearComponent < 40 => 2000 + yearComponent,
                _ => 1900 + yearComponent,
            };

            try
            {
                return new DateOnly(year, monthComponent, dayComponent);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid date of birth
                return null;
            }
        }

        static SelfIdentifiedUserRecord MapSelfIdentifiedUser(V1Models.Party party, DateTimeOffset now)
        {
            var partyUuid = party.PartyUuid;
            var partyId = checked((uint)party.PartyId);
            var displayName = Normalize(party.Name);

            if (displayName is null)
            {
                displayName = "Selvidentifisert bruker uten navn";
            }

            return new SelfIdentifiedUserRecord
            {
                // party fields
                PartyUuid = partyUuid,
                PartyId = partyId,
                ExternalUrn = FieldValue.Null, // we cannot conclude about the external urn of a SI user based on the party object from A2
                DisplayName = displayName,
                PersonIdentifier = null,
                OrganizationIdentifier = null,
                CreatedAt = now,
                ModifiedAt = now,
                IsDeleted = FieldValue.Unset, // we cannot conclude about the is-deleted status of a SI user based on the party object from A2
                DeletedAt = FieldValue.Unset,
                OwnerUuid = FieldValue.Null,
                UserIds = FieldValue.Unset,
                Usernames = FieldValue.Unset,
                VersionId = FieldValue.Unset,
                SelfIdentifiedUserType = FieldValue.Null, // we cannot conclude about the SI user type based on the party object from A2
                Email = FieldValue.Null, // we cannot conclude about the email based on the party object from A2
                ExtRef = FieldValue.Null, // we cannot conclude about the external reference based on the party object from A2
            };
        }

        static PersonRecord MapPerson(V1Models.Party party, DateTimeOffset now)
        {
            Debug.Assert(party.Person is not null);
            var person = party.Person;

            var partyUuid = party.PartyUuid;
            var partyId = checked((uint)party.PartyId);
            var personIdentifier = MapPersonIdentifier(person.SSN.AsSpan().Trim());
            var firstName = Normalize(person.FirstName);
            var middleName = Normalize(person.MiddleName);
            var lastName = Normalize(person.LastName);
            var shortName = Normalize(person.Name);
            var displayName = MapPersonName(
                firstName: firstName,
                middleName: middleName,
                lastName: lastName,
                shortName: shortName);
            var isDeleted = false; // we cannot conclude about the is-deleted status of a person user based on the party object from A2
            FieldValue<DateTimeOffset> deletedAt = FieldValue.Null;

            var address = MapStreetAddress(
                municipalNumber: person.AddressMunicipalNumber,
                municipalName: person.AddressMunicipalName,
                streetName: person.AddressStreetName,
                houseNumber: person.AddressHouseNumber,
                houseLetter: person.AddressHouseLetter,
                postalCode: person.AddressPostalCode,
                city: person.AddressCity);

            var mailingAddress = MapMailingAddress(
                address: person.MailingAddress,
                postalCode: person.MailingPostalCode,
                city: person.MailingPostalCity);

            DateOnly? dateOfBirth = CalculateDateOfBirth(personIdentifier);
            DateOnly? dateOfDeath = person.DateOfDeath is null ? null : DateOnly.FromDateTime(person.DateOfDeath.Value);

            if (displayName is null && lastName is not null)
            {
                if (firstName is null)
                {
                    displayName = lastName;
                    firstName = "Mangler";
                }
                else
                {
                    displayName = $"{firstName} {lastName}";
                }

                if (middleName is not null)
                {
                    displayName += $" {middleName}";
                }
            }
            else if (firstName is null && lastName is null && middleName is null && displayName is null)
            {
                firstName = "Mangler";
                lastName = "Navn";
                displayName = "Mangler Navn";
            }
            else if (firstName is null && lastName is null && displayName is not null)
            {
                if (IsSyntheticImportName(displayName))
                {
                    firstName = "Mangler";
                    lastName = "Navn";
                    displayName = "Mangler Navn";
                }
                else
                {
                    var components = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    lastName = components.Length > 0 ? components[0] : "Navn";
                    firstName = components.Length > 1 ? components[1] : "Mangler";
                    displayName = $"{firstName} {lastName}";
                }
            }

            if (isDeleted)
            {
                deletedAt = party.LastChangedInExternalRegister ?? party.LastChangedInAltinn ?? now;
            }

            return new PersonRecord
            {
                // party fields
                PartyUuid = partyUuid,
                PartyId = partyId,
                ExternalUrn = PartyExternalRefUrn.PersonId.Create(personIdentifier),
                DisplayName = displayName,
                PersonIdentifier = personIdentifier,
                OrganizationIdentifier = null,
                CreatedAt = now,
                ModifiedAt = now,
                IsDeleted = isDeleted,
                DeletedAt = deletedAt,
                UserIds = FieldValue.Unset,
                Usernames = FieldValue.Unset,
                VersionId = FieldValue.Unset,
                OwnerUuid = FieldValue.Null,

                // person fields
                Source = PersonSource.NationalPopulationRegister,
                FirstName = firstName,
                MiddleName = middleName,
                LastName = lastName,
                ShortName = shortName ?? displayName,
                Address = address,
                MailingAddress = mailingAddress,
                DateOfBirth = FieldValue.From(dateOfBirth),
                DateOfDeath = FieldValue.From(dateOfDeath),
            };
        }

        static string? MapPersonName(
            string? firstName,
            string? middleName,
            string? lastName,
            string? shortName)
        {
            if (firstName is null && lastName is null)
            {
                return shortName;
            }

            var builder = new StringBuilder((firstName?.Length ?? 0) + (middleName?.Length ?? 0) + (lastName?.Length ?? 0) + 3);
            if (firstName is not null)
            {
                builder.Append(firstName);
                builder.Append(' ');
            }

            if (middleName is not null)
            {
                builder.Append(middleName);
                builder.Append(' ');
            }

            if (lastName is not null)
            {
                builder.Append(lastName);
                builder.Append(' ');
            }

            builder.Length--; // remove trailing space
            return builder.ToString();
        }

        static PersonIdentifier MapPersonIdentifier(ReadOnlySpan<char> source)
        {
            if (source.SequenceEqual("00000000000"))
            {
                ThrowHelper.ThrowArgumentException(nameof(source), "Person identifier cannot be all zeroes.");
            }

            return PersonIdentifier.Parse(source);
        }

        static OrganizationRecord MapOrganization(V1Models.Party party, DateTimeOffset now)
        {
            Debug.Assert(party.Organization is not null);
            var organization = party.Organization;

            var partyUuid = party.PartyUuid;
            var partyId = checked((uint)party.PartyId);
            var organizationNumber = MapOrganizationIdentifier(organization.OrgNumber.AsSpan().Trim());
            var name = Normalize(organization.Name);
            var unitStatus = Normalize(organization.UnitStatus);
            var unitType = Normalize(organization.UnitType);
            var telephoneNumber = Normalize(organization.TelephoneNumber);
            var mobileNumber = Normalize(organization.MobileNumber);
            var faxNumber = Normalize(organization.FaxNumber);
            var emailAddress = Normalize(organization.EMailAddress);
            var internetAddress = Normalize(organization.InternetAddress);
            var isDeleted = party.IsDeleted;
            FieldValue<DateTimeOffset> deletedAt = FieldValue.Null;

            var mailingAddress = MapMailingAddress(
                address: organization.MailingAddress,
                postalCode: organization.MailingPostalCode,
                city: organization.MailingPostalCity);

            var businessAddress = MapMailingAddress(
                address: organization.BusinessAddress,
                postalCode: organization.BusinessPostalCode,
                city: organization.BusinessPostalCity);

            if (string.IsNullOrEmpty(name))
            {
                name = "Organisasjon med manglende navn";
            }

            if (isDeleted)
            {
                deletedAt = party.LastChangedInExternalRegister ?? party.LastChangedInAltinn ?? now;
            }

            return new OrganizationRecord
            {
                // party fields
                PartyUuid = partyUuid,
                PartyId = partyId,
                ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(organizationNumber),
                DisplayName = name,
                PersonIdentifier = null,
                OrganizationIdentifier = organizationNumber,
                CreatedAt = now,
                ModifiedAt = now,
                IsDeleted = isDeleted,
                DeletedAt = deletedAt,
                UserIds = FieldValue.Unset,
                Usernames = FieldValue.Unset,
                VersionId = FieldValue.Unset,
                OwnerUuid = FieldValue.Null,

                // organization fields
                Source = organizationNumber.ToString() switch
                {
                    var s when s.StartsWith('0') => OrganizationSource.RegisteredWithSkatteetaten,
                    _ => OrganizationSource.CentralCoordinatingRegister,
                },
                UnitStatus = unitStatus,
                UnitType = unitType,
                TelephoneNumber = telephoneNumber,
                MobileNumber = mobileNumber,
                FaxNumber = faxNumber,
                EmailAddress = emailAddress,
                InternetAddress = internetAddress,
                MailingAddress = mailingAddress,
                BusinessAddress = businessAddress,
            };
        }

        static OrganizationIdentifier MapOrganizationIdentifier(ReadOnlySpan<char> source)
        {
            // there are some organization numbers that are 8 in length,
            // they should be 0-prefixed.
            if (source.Length == 8)
            {
                Span<char> padded = stackalloc char[9];
                padded[0] = '0';
                source.CopyTo(padded[1..]);
                return OrganizationIdentifier.Parse(padded);
            }

            return OrganizationIdentifier.Parse(source);
        }

        static bool IsSyntheticImportName(string name)
            => string.Equals(name, "Inserted By ER Import", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Ikke i Altinn register", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "Inserted By FReg Import", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

    private static void Assert([DoesNotReturnIf(false)] bool condition, [CallerArgumentExpression(nameof(condition))] string? conditionString = null)
    {
        if (!condition)
        {
            ThrowHelper.ThrowInvalidOperationException($"Assertion failed: {conditionString}");
        }
    }

    /// <summary>
    /// A2 party change model.
    /// </summary>
    internal sealed class PartyChange
    {
        /// <summary>
        /// Gets the id of this change.
        /// </summary>
        [JsonPropertyName("ChangeId")]
        public required uint ChangeId { get; init; }

        /// <summary>
        /// Gets the party id of the party that changed.
        /// </summary>
        [JsonPropertyName("PartyId")]
        public required int PartyId { get; init; }

        /// <summary>
        /// Gets the party uuid of the party that changed.
        /// </summary>
        [JsonPropertyName("PartyUuid")]
        public required Guid PartyUuid { get; init; }

        /// <summary>
        /// Gets the time of the change.
        /// </summary>
        [JsonPropertyName("LastChangedTime")]
        public required DateTimeOffset LastChangedTime { get; init; }
    }

    /// <summary>
    /// A2 party change page model.
    /// </summary>
    internal sealed class PartyChangesResponse
    {
        /// <summary>
        /// Gets the list of party changes.
        /// </summary>
        [JsonPropertyName("PartyChangeList")]
        public required IReadOnlyList<PartyChange> PartyChangeList { get; init; }

        /// <summary>
        /// Gets the highest change id available at the time of the request.
        /// </summary>
        [JsonPropertyName("LastAvailableChange")]
        public required uint LastAvailableChange { get; init; }

        /// <summary>
        /// Gets the last change id in the page.
        /// </summary>
        [JsonPropertyName("LastChangeInSegment")]
        public required uint LastChangeInSegment { get; init; }
    }

    /// <summary>
    /// A2 party role assignment model.
    /// </summary>
    internal sealed class PartyRoleAssignmentItem
    {
        /// <summary>
        /// Gets the party id of the party that the role is assigned to.
        /// </summary>
        [JsonPropertyName("PartyId")]
        public required int ToPartyId { get; init; }

        /// <summary>
        /// Gets the party uuid of the party that the role is assigned to.
        /// </summary>
        [JsonPropertyName("PartyUuid")]
        public required Guid ToPartyUuid { get; init; }

        /// <summary>
        /// Gets the kind of assignment.
        /// </summary>
        [JsonPropertyName("PartyRelation")]
        public required string PartyRelation { get; init; }

        /// <summary>
        /// Gets the role code of the role that is assigned.
        /// </summary>
        [JsonPropertyName("RoleCode")]
        public required string RoleCode { get; init; }
    }

    /// <summary>
    /// Partial A2 party profile model.
    /// </summary>
    internal sealed class PartyProfile
    {
        /// <summary>
        /// Gets the user id.
        /// </summary>
        [JsonPropertyName("UserId")]
        public required ulong UserId { get; init; }

        /// <summary>
        /// Gets the user UUID (should be the same as the party UUID).
        /// </summary>
        [JsonPropertyName("UserUUID")]
        public required Guid? UserUuid { get; init; }

        /// <summary>
        /// Gets the external identity-string, if any (for self-identified users, often empty string instead of null).
        /// </summary>
        [JsonPropertyName("ExternalIdentity")]
        public string? ExternalIdentity { get; init; }

        /// <summary>
        /// Gets a value indicating whether the profile is active or not.
        /// </summary>
        [JsonPropertyName("IsActive")]
        public bool IsActive { get; init; }

        /// <summary>
        /// Gets the user name, if any.
        /// </summary>
        [JsonPropertyName("UserName")]
        public required string? UserName { get; init; }

        /// <summary>
        /// Gets the user type.
        /// </summary>
        [JsonPropertyName("UserType")]
        public required byte UserType { get; init; }

        /// <summary>
        /// Gets the party object.
        /// </summary>
        [JsonPropertyName("Party")]
        public required V1Models.Party Party { get; init; }
    }

    /// <summary>
    /// A "page" of items in a stream from the A2 system.
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    internal sealed record ItemStreamPage<T>
    {
        /// <summary>
        /// Gets stats for the page.
        /// </summary>
        [JsonPropertyName("stats")]
        public required StreamPageStats Stats { get; init; }

        /// <summary>
        /// Gets the data in the page.
        /// </summary>
        [JsonPropertyName("data")]
        public required IReadOnlyList<T> Data { get; init; }
    }

    /// <summary>
    /// Stats for a stream page in the A2 system.
    /// </summary>
    internal sealed record StreamPageStats
    {
        /// <summary>
        /// Gets the first item in the page.
        /// </summary>
        [JsonPropertyName("pageStart")]
        public uint? PageStart { get; init; }

        /// <summary>
        /// Gets the last item in the page.
        /// </summary>
        [JsonPropertyName("pageEnd")]
        public uint? PageEnd { get; init; }

        /// <summary>
        /// Gets the highest sequence number in all the pages.
        /// </summary>
        [JsonPropertyName("sequenceMax")]
        public required uint SequenceMax { get; init; }
    }

    /// <summary>
    /// An update event for a user profile in the A2 system.
    /// </summary>
    internal sealed class UserProfileEvent
    {
        /// <summary>
        /// Gets the event id.
        /// </summary>
        [JsonPropertyName("userChangeEventId")]
        public required uint UserChangeEventId { get; init; }

        /// <summary>
        /// Gets the user UUID.
        /// </summary>
        [JsonPropertyName("userUuid")]
        public required Guid UserUuid { get; init; }

        /// <summary>
        /// Gets the user id.
        /// </summary>
        [JsonPropertyName("userId")]
        public required uint UserId { get; init; }

        /// <summary>
        /// Gets the party UUID of the owner of this user profile. For person, and SI users, this should be the same as the user UUID.
        /// </summary>
        [JsonPropertyName("ownerPartyUuid")]
        public required Guid OwnerPartyUuid { get; init; }

        /// <summary>
        /// Gets the user name, if any.
        /// </summary>
        [JsonPropertyName("userName")]
        public string? UserName { get; init; }

        /// <summary>
        /// Gets the type of user profile.
        /// </summary>
        [JsonPropertyName("userType")]
        public required UserProfileType UserType { get; init; }

        /// <summary>
        /// Gets whether the user profile is deleted or not.
        /// </summary>
        [JsonPropertyName("isDeleted")]
        public required bool IsDeleted { get; init; }
    }

    /// <summary>
    /// The type of user (in profile) in the A2 system.
    /// </summary>
    [StringEnumConverter]
    internal enum UserProfileType
    {
        /// <summary>
        /// A person user.
        /// </summary>
        [JsonStringEnumMemberName("SSNIdentified")]
        Person,

        /// <summary>
        /// An enterprise user.
        /// </summary>
        [JsonStringEnumMemberName("EnterpriseIdentified")]
        Enterprise,

        /// <summary>
        /// A self-identified user.
        /// </summary>
        [JsonStringEnumMemberName("SelfIdentified")]
        SelfIdentified,
    }

    private static partial class Log
    {
        [LoggerMessage(1, LogLevel.Debug, "Fetching party changes from {FromExclusive}.")]
        public static partial void FetchingPartyChangesPage(ILogger logger, uint fromExclusive);

        [LoggerMessage(2, LogLevel.Error, "Failed to deserialize party profile from {Url}.")]
        public static partial void FailedToDeserializePartyProfile(ILogger logger, string url, Exception exception);

        [LoggerMessage(3, LogLevel.Error, "Failed to fetch party profile from {Url}. Status code: {StatusCode}.")]
        public static partial void FailedToFetchPartyProfile(ILogger logger, string url, HttpStatusCode statusCode);

        [LoggerMessage(4, LogLevel.Debug, "Fetching user profile changes from {FromExclusive}.")]
        public static partial void FetchingUserProfileChangesPage(ILogger logger, uint fromExclusive);

        [LoggerMessage(5, LogLevel.Error, "Failed to fetch profile from {Url}. Status code: {StatusCode}.")]
        public static partial void FailedToFetchProfile(ILogger logger, string url, HttpStatusCode statusCode);
    }
}
