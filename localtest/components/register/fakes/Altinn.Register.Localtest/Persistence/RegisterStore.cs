using System.Collections.Immutable;
using System.Globalization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Localtest.TestData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// The in-memory register built from <c>testData.json</c>: party records, external role assignments and the
/// synthesized event streams. Immutable after construction; edit the file and restart to change it.
/// </summary>
public sealed class RegisterStore
{
    /// <summary>Fixed timestamp used for created/modified so repeated loads are identical.</summary>
    public static readonly DateTimeOffset LoadedAt = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const uint SystemUserPartyIdBase = 900_000_000;

    public RegisterStore(IOptions<LocaltestRegisterOptions> options, StaticExternalRoleDefinitionPersistence roleDefinitions, ILogger<RegisterStore> logger)
    {
        var path = options.Value.TestDataPath;
        logger.LogInformation("Loading localtest register data from {Path}", path);
        var data = TestDataFile.Load(path);
        RoleDefinitions = roleDefinitions;

        var parties = new List<PartyRecord>();
        var assignments = new List<RoleAssignment>();
        ulong version = 0;

        var orgByPartyId = new Dictionary<int, (TestOrg Org, Guid Uuid)>();
        foreach (var org in data.Orgs.OrderBy(o => o.PartyId))
        {
            var uuid = org.PartyUuid ?? UuidFromPartyId(org.PartyId);
            orgByPartyId[org.PartyId] = (org, uuid);
        }

        foreach (var person in data.Persons.OrderBy(p => p.PartyId))
        {
            if (string.IsNullOrEmpty(person.Ssn))
            {
                logger.LogWarning("Skipping person with partyId {PartyId}: self-identified users are not supported yet", person.PartyId);
                continue;
            }

            if (!PersonIdentifier.TryParse(person.Ssn, null, out var identifier))
            {
                logger.LogWarning("Skipping person with partyId {PartyId}: '{Ssn}' is not a valid national identity number (mod-11 check)", person.PartyId, person.Ssn);
                continue;
            }

            var uuid = person.PartyUuid ?? UuidFromPartyId(person.PartyId);
            var displayName = string.Join(' ', new[] { person.FirstName, person.MiddleName, person.LastName }.Where(static s => !string.IsNullOrWhiteSpace(s)));
            parties.Add(new PersonRecord
            {
                PartyUuid = uuid,
                OwnerUuid = FieldValue.Null,
                PartyId = checked((uint)person.PartyId),
                ExternalUrn = PartyExternalRefUrn.PersonId.Create(identifier),
                DisplayName = displayName,
                PersonIdentifier = identifier,
                OrganizationIdentifier = FieldValue.Null,
                CreatedAt = LoadedAt,
                ModifiedAt = LoadedAt,
                UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(checked((uint)person.UserId)),
                Usernames = string.IsNullOrEmpty(person.UserName)
                    ? PartyHistoricalAggregate<string>.Empty
                    : PartyHistoricalAggregate<string>.CreateCurrent(person.UserName),
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                VersionId = ++version,
                Source = PersonSource.NationalPopulationRegister,
                FirstName = Text(person.FirstName),
                MiddleName = Text(person.MiddleName),
                LastName = person.LastName,
                ShortName = displayName,
                Address = StreetAddress(person),
                MailingAddress = MailingAddress(person.MailingAddress, person.MailingPostalCode, person.MailingPostalCity),
                DateOfBirth = DateOfBirth(person.Ssn),
                DateOfDeath = FieldValue.Null,
            });

            foreach (var (forPartyId, roles) in person.PartyRoles)
            {
                if (!int.TryParse(forPartyId, CultureInfo.InvariantCulture, out var forId) || !orgByPartyId.TryGetValue(forId, out var forOrg))
                {
                    logger.LogWarning("Person {PartyId} has roles for unknown party '{ForPartyId}'", person.PartyId, forPartyId);
                    continue;
                }

                foreach (var role in roles)
                {
                    if (!roleDefinitions.TryGetRoleDefinitionByRoleCode(role.Value, out var definition)
                        && !roleDefinitions.TryGetRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, role.Value.ToLowerInvariant(), out definition))
                    {
                        logger.LogWarning("Person {PartyId} has unknown role '{Role}' for party {ForPartyId}; it is not a CCR role code or identifier", person.PartyId, role.Value, forPartyId);
                        continue;
                    }

                    assignments.Add(new RoleAssignment(definition.Source, definition.Identifier, FromParty: forOrg.Uuid, ToParty: uuid));
                }
            }
        }

        foreach (var (org, uuid) in orgByPartyId.Values.OrderBy(o => o.Org.PartyId))
        {
            if (!OrganizationIdentifier.TryParse(org.OrgNumber, null, out var identifier))
            {
                logger.LogWarning("Skipping organisation with partyId {PartyId}: '{OrgNumber}' is not a valid organisation number (mod-11 check)", org.PartyId, org.OrgNumber);
                continue;
            }

            parties.Add(new OrganizationRecord
            {
                PartyUuid = uuid,
                OwnerUuid = FieldValue.Null,
                PartyId = checked((uint)org.PartyId),
                ExternalUrn = PartyExternalRefUrn.OrganizationId.Create(identifier),
                DisplayName = org.Name ?? org.OrgNumber,
                PersonIdentifier = FieldValue.Null,
                OrganizationIdentifier = identifier,
                CreatedAt = LoadedAt,
                ModifiedAt = LoadedAt,
                UserIds = PartyHistoricalAggregate<uint>.Empty,
                Usernames = PartyHistoricalAggregate<string>.Empty,
                IsDeleted = false,
                DeletedAt = FieldValue.Null,
                VersionId = ++version,
                Source = OrganizationSource.CentralCoordinatingRegister,
                UnitStatus = org.UnitStatus ?? "N",
                UnitType = Text(org.UnitType),
                TelephoneNumber = Text(org.TelephoneNumber),
                MobileNumber = Text(org.MobileNumber),
                FaxNumber = Text(org.FaxNumber),
                EmailAddress = Text(org.EMailAddress),
                InternetAddress = Text(org.InternetAddress),
                MailingAddress = MailingAddress(org.MailingAddress, org.MailingPostalCode, org.MailingPostalCity),
                BusinessAddress = MailingAddress(org.BusinessAddress, org.BusinessPostalCode, org.BusinessPostalCity),
                ParentOrganizationUuid = FieldValue.Unset,
            });

            if (org.ParentPartyId is int parentId)
            {
                if (orgByPartyId.TryGetValue(parentId, out var parent))
                {
                    // Sub-unit "has as registration unit" its main unit: from sub-unit to main unit, as the production SQL joins it.
                    assignments.Add(new RoleAssignment(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", FromParty: uuid, ToParty: parent.Uuid));
                }
                else
                {
                    logger.LogWarning("Organisation {PartyId} has unknown parentPartyId {ParentPartyId}", org.PartyId, parentId);
                }
            }
        }

        uint nextSystemUserPartyId = SystemUserPartyIdBase;
        foreach (var system in data.Systems)
        {
            foreach (var systemUser in system.SystemUsers)
            {
                var owner = orgByPartyId.Values.FirstOrDefault(o => o.Org.OrgNumber == systemUser.OrgNumber);
                if (owner.Org is null)
                {
                    logger.LogWarning("System user {Id} belongs to unknown organisation '{OrgNumber}'", systemUser.Id, systemUser.OrgNumber);
                    continue;
                }

                parties.Add(new SystemUserRecord
                {
                    PartyUuid = systemUser.Id,
                    OwnerUuid = owner.Uuid,
                    PartyId = nextSystemUserPartyId++,
                    ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(systemUser.Id),
                    DisplayName = system.Name,
                    PersonIdentifier = FieldValue.Null,
                    OrganizationIdentifier = FieldValue.Null,
                    CreatedAt = LoadedAt,
                    ModifiedAt = LoadedAt,
                    UserIds = PartyHistoricalAggregate<uint>.Empty,
                    Usernames = PartyHistoricalAggregate<string>.Empty,
                    IsDeleted = false,
                    DeletedAt = FieldValue.Null,
                    VersionId = ++version,
                    SystemUserType = SystemUserRecordType.Standard,
                });
            }
        }

        Parties = [.. parties];
        ByUuid = Parties.ToDictionary(p => p.PartyUuid.Value);
        ByPartyId = Parties.ToDictionary(p => p.PartyId.Value);
        ByPersonIdentifier = Parties.Where(p => p.PersonIdentifier.HasValue).ToDictionary(p => p.PersonIdentifier.Value.ToString());
        ByOrganizationIdentifier = Parties.Where(p => p.OrganizationIdentifier.HasValue).ToDictionary(p => p.OrganizationIdentifier.Value.ToString());
        ByUserId = Parties.Where(p => p.UserIds.Value.HasCurrentValue).ToDictionary(p => p.UserIds.Value.CurrentValue.Value);
        Assignments = [.. assignments];
        RoleEvents = [.. assignments.Select((a, i) => new ExternalRoleAssignmentEvent
        {
            VersionId = (ulong)i + 1,
            Type = ExternalRoleAssignmentEvent.EventType.Added,
            RoleSource = a.Source,
            RoleIdentifier = a.Identifier,
            FromParty = a.FromParty,
            ToParty = a.ToParty,
        })];
        MaxPartyVersionId = version;
        TestData = data;

        logger.LogInformation("Localtest register loaded: {Parties} parties, {Assignments} external role assignments", Parties.Length, Assignments.Length);
    }

    public StaticExternalRoleDefinitionPersistence RoleDefinitions { get; }

    public TestDataFile TestData { get; }

    /// <summary>All parties ordered by version id.</summary>
    public ImmutableArray<PartyRecord> Parties { get; }

    public IReadOnlyDictionary<Guid, PartyRecord> ByUuid { get; }

    public IReadOnlyDictionary<uint, PartyRecord> ByPartyId { get; }

    public IReadOnlyDictionary<string, PartyRecord> ByPersonIdentifier { get; }

    public IReadOnlyDictionary<string, PartyRecord> ByOrganizationIdentifier { get; }

    public IReadOnlyDictionary<uint, PartyRecord> ByUserId { get; }

    public ImmutableArray<RoleAssignment> Assignments { get; }

    public ImmutableArray<ExternalRoleAssignmentEvent> RoleEvents { get; }

    public ulong MaxPartyVersionId { get; }

    public bool IsMainUnitRole(RoleAssignment assignment)
        => RoleDefinitions.IsMainUnitRole(assignment.Source, assignment.Identifier);

    /// <summary>Main units of a party: assignments from the party with a main-unit role.</summary>
    public IEnumerable<PartyRecord> MainUnitsOf(Guid partyUuid)
        => Assignments.Where(a => a.FromParty == partyUuid && IsMainUnitRole(a)).Select(a => ByUuid[a.ToParty]).Distinct();

    /// <summary>Direct sub-units of a party: assignments to the party with a main-unit role.</summary>
    public IEnumerable<PartyRecord> SubUnitsOf(Guid partyUuid)
        => Assignments.Where(a => a.ToParty == partyUuid && IsMainUnitRole(a)).Select(a => ByUuid[a.FromParty]).Distinct().OrderBy(p => p.PartyUuid.Value);

    /// <summary>The party UUID used when test data has none: the party id, zero-padded into a GUID.</summary>
    public static Guid UuidFromPartyId(int partyId)
        => Guid.ParseExact(partyId.ToString("D32", CultureInfo.InvariantCulture), "N");

    private static FieldValue<string> Text(string? value)
        => string.IsNullOrEmpty(value) ? FieldValue.Null : value;

    private static FieldValue<StreetAddressRecord> StreetAddress(TestPerson p)
    {
        if (p.AddressStreetName is null && p.AddressPostalCode is null && p.AddressCity is null && p.AddressMunicipalNumber is null)
        {
            return FieldValue.Null;
        }

        return new StreetAddressRecord
        {
            MunicipalNumber = p.AddressMunicipalNumber,
            MunicipalName = p.AddressMunicipalName,
            StreetName = p.AddressStreetName,
            HouseNumber = p.AddressHouseNumber,
            HouseLetter = p.AddressHouseLetter,
            PostalCode = p.AddressPostalCode,
            City = p.AddressCity,
        };
    }

    private static FieldValue<MailingAddressRecord> MailingAddress(string? address, string? postalCode, string? city)
    {
        if (address is null && postalCode is null && city is null)
        {
            return FieldValue.Null;
        }

        return new MailingAddressRecord { Address = address, PostalCode = postalCode, City = city };
    }

    /// <summary>
    /// Date of birth from a national identity number, including synthetic test numbers (month + 80 for Tenor,
    /// month + 40 for other test data) and D-numbers (day + 40).
    /// </summary>
    private static FieldValue<DateOnly> DateOfBirth(string ssn)
    {
        if (ssn.Length != 11 || !ssn.All(char.IsAsciiDigit))
        {
            return FieldValue.Null;
        }

        var day = int.Parse(ssn[..2], CultureInfo.InvariantCulture);
        var month = int.Parse(ssn[2..4], CultureInfo.InvariantCulture);
        var year = int.Parse(ssn[4..6], CultureInfo.InvariantCulture);
        var individual = int.Parse(ssn[6..9], CultureInfo.InvariantCulture);

        if (day > 40)
        {
            day -= 40;
        }

        if (month > 80)
        {
            month -= 80;
        }
        else if (month > 40)
        {
            month -= 40;
        }

        var century = individual switch
        {
            < 500 => 1900,
            < 750 when year >= 54 => 1800,
            >= 900 when year >= 40 => 1900,
            _ => 2000,
        };

        try
        {
            return new DateOnly(century + year, month, day);
        }
        catch (ArgumentOutOfRangeException)
        {
            return FieldValue.Null;
        }
    }
}

/// <summary>An external role assignment: <paramref name="FromParty"/> has the role <paramref name="Identifier"/> held by <paramref name="ToParty"/>.</summary>
public sealed record RoleAssignment(ExternalRoleSource Source, string Identifier, Guid FromParty, Guid ToParty);
