using System.Collections.Immutable;
using System.Globalization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Localtest.TestData;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Urn;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>
/// One immutable snapshot of the in-memory register built from test data: party records, external role
/// assignments, the synthesized event streams and the per-user grants. <see cref="RegisterStoreProvider"/> swaps
/// whole snapshots when the seed is reloaded or an app's test data is registered.
/// </summary>
public sealed class RegisterStore
{
    /// <summary>Fixed timestamp used for created/modified so repeated loads are identical.</summary>
    public static readonly DateTimeOffset LoadedAt = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const uint SystemUserPartyIdBase = 900_000_000;

    public RegisterStore(TestDataFile data, StaticExternalRoleDefinitionPersistence roleDefinitions, ILogger logger)
    {
        RoleDefinitions = roleDefinitions;

        var parties = new List<PartyRecord>();
        var assignments = new List<RoleAssignment>();
        var warnings = new List<string>();
        ulong version = 0;

        void Warn(string message)
        {
            warnings.Add(message);
            logger.LogWarning("{Message}", message);
        }

        var orgByPartyId = new Dictionary<int, (TestOrg Org, Guid Uuid)>();
        foreach (var org in data.Orgs.OrderBy(o => o.PartyId))
        {
            var uuid = org.PartyUuid ?? UuidFromPartyId(org.PartyId);
            orgByPartyId[org.PartyId] = (org, uuid);
        }

        var loadedPersons = new List<(TestPerson Person, PartyRecord Party)>();
        foreach (var person in data.Persons.OrderBy(p => p.PartyId))
        {
            var uuid = person.PartyUuid ?? UuidFromPartyId(person.PartyId);
            var displayName = string.Join(' ', new[] { person.FirstName, person.MiddleName, person.LastName }.Where(static s => !string.IsNullOrWhiteSpace(s)));

            if (string.IsNullOrEmpty(person.Ssn))
            {
                // Self-identified user, as ID-porten creates them today: identified by e-mail, no username, display
                // name = the e-mail (register.get_or_create_email_user). Legacy username users are not modelled.
                if (string.IsNullOrWhiteSpace(person.Email))
                {
                    Warn($"Skipping self-identified person with partyId {person.PartyId}: no 'email' (self-identified users are e-mail registrations)");
                    continue;
                }

                var email = person.Email.Trim().ToLowerInvariant();
                var si = new SelfIdentifiedUserRecord
                {
                    PartyUuid = uuid,
                    OwnerUuid = FieldValue.Null,
                    PartyId = checked((uint)person.PartyId),
                    ExternalUrn = PartyExternalRefUrn.IDPortenEmail.Create(UrnEncoded.Create(email)),
                    DisplayName = email,
                    PersonIdentifier = FieldValue.Null,
                    OrganizationIdentifier = FieldValue.Null,
                    CreatedAt = LoadedAt,
                    ModifiedAt = LoadedAt,
                    UserIds = PartyHistoricalAggregate<uint>.CreateCurrent(checked((uint)person.UserId)),
                    Usernames = PartyHistoricalAggregate<string>.Empty,
                    IsDeleted = false,
                    DeletedAt = FieldValue.Null,
                    VersionId = ++version,
                    SelfIdentifiedUserType = SelfIdentifiedUserType.IdPortenEmail,
                    Email = email,
                    ExtRef = FieldValue.Null,
                };
                parties.Add(si);
                loadedPersons.Add((person, si));
                continue;
            }

            if (!PersonIdentifier.TryParse(person.Ssn, null, out var identifier))
            {
                Warn($"Skipping person with partyId {person.PartyId}: '{person.Ssn}' is not a valid national identity number (mod-11 check)");
                continue;
            }

            var record = new PersonRecord
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
            };
            parties.Add(record);
            loadedPersons.Add((person, record));
        }

        foreach (var (org, uuid) in orgByPartyId.Values.OrderBy(o => o.Org.PartyId))
        {
            if (!OrganizationIdentifier.TryParse(org.OrgNumber, null, out var identifier))
            {
                Warn($"Skipping organisation with partyId {org.PartyId}: '{org.OrgNumber}' is not a valid organisation number (mod-11 check)");
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
                UnitType = Text(string.IsNullOrWhiteSpace(org.UnitType) ? DefaultUnitType(org) : org.UnitType),
                TelephoneNumber = Text(org.TelephoneNumber),
                MobileNumber = Text(org.MobileNumber),
                FaxNumber = Text(org.FaxNumber),
                EmailAddress = Text(org.EMailAddress),
                InternetAddress = Text(org.InternetAddress),
                MailingAddress = MailingAddress(org.MailingAddress, org.MailingPostalCode, org.MailingPostalCity),
                BusinessAddress = MailingAddress(org.BusinessAddress, org.BusinessPostalCode, org.BusinessPostalCity),
                ParentOrganizationUuid = FieldValue.Unset,
            });

            if (string.IsNullOrWhiteSpace(org.UnitType))
            {
                Warn($"Organisation {org.PartyId} has no unitType; using {DefaultUnitType(org)} (set it explicitly in testData.json)");
            }

            if (org.ParentPartyId is int parentId)
            {
                if (orgByPartyId.TryGetValue(parentId, out var parent))
                {
                    // Sub-unit "has as registration unit" its main unit: from sub-unit to main unit, as the production SQL joins it.
                    assignments.Add(new RoleAssignment(ExternalRoleSource.CentralCoordinatingRegister, "hovedenhet", FromParty: uuid, ToParty: parent.Uuid));
                }
                else
                {
                    Warn($"Organisation {org.PartyId} has unknown parentPartyId {parentId}");
                }
            }
        }

        var byPartyId = parties.ToDictionary(p => p.PartyId.Value);
        OrgsByOrganizationNumber = orgByPartyId.Values
            .Where(o => byPartyId.ContainsKey(checked((uint)o.Org.PartyId)) && byPartyId[checked((uint)o.Org.PartyId)] is OrganizationRecord)
            .ToDictionary(o => o.Org.OrgNumber, o => o.Org);

        // Roles and access packages, after all parties exist so roles may point at any party.
        var users = new List<TestUser>();
        foreach (var (person, party) in loadedPersons)
        {
            var selfRoleCodes = new List<string>();
            var grants = new SortedDictionary<uint, PartyGrant>();

            PartyGrant? GrantFor(string forPartyId, string what)
            {
                if (!uint.TryParse(forPartyId, CultureInfo.InvariantCulture, out var forId) || !byPartyId.TryGetValue(forId, out var forParty))
                {
                    Warn($"Person {person.PartyId} has {what} for unknown party '{forPartyId}'");
                    return null;
                }

                if (!grants.TryGetValue(forId, out var grant))
                {
                    grants[forId] = grant = new PartyGrant(forParty, [], [], []);
                }

                return grant;
            }

            foreach (var (forPartyId, roles) in person.PartyRoles)
            {
                if (forPartyId == person.PartyId.ToString(CultureInfo.InvariantCulture))
                {
                    selfRoleCodes.AddRange(roles.Select(r => r.Value).Where(v => !selfRoleCodes.Contains(v)));
                    continue;
                }

                if (GrantFor(forPartyId, "roles") is not { } grant)
                {
                    continue;
                }

                foreach (var role in roles)
                {
                    ExternalRoleDefinition? definition = null;
                    var byCode = grant.Party is OrganizationRecord && roleDefinitions.TryGetRoleDefinitionByRoleCode(role.Value, out definition);
                    var byIdentifier = !byCode && grant.Party is OrganizationRecord
                        && roleDefinitions.TryGetRoleDefinition(ExternalRoleSource.CentralCoordinatingRegister, role.Value.ToLowerInvariant(), out definition);

                    if (definition is not null && (byCode || byIdentifier))
                    {
                        assignments.Add(new RoleAssignment(definition.Source, definition.Identifier, FromParty: grant.Party.PartyUuid.Value, ToParty: party.PartyUuid.Value));
                        AddDistinct(grant.CcrRoles, definition.Identifier);
                    }

                    // Every code is kept verbatim for urn:altinn:rolecode; a CCR identifier is given as its code.
                    var code = byIdentifier ? definition!.Code : role.Value;
                    if (!string.IsNullOrEmpty(code))
                    {
                        AddDistinct(grant.RoleCodes, code);
                    }
                }
            }

            foreach (var (forPartyId, packages) in person.AccessPackages)
            {
                if (GrantFor(forPartyId, "access packages") is { } grant)
                {
                    foreach (var package in packages)
                    {
                        AddDistinct(grant.AccessPackages, package);
                    }
                }
            }

            users.Add(new TestUser(person, party, selfRoleCodes, [.. grants.Values]));
        }

        uint nextSystemUserPartyId = SystemUserPartyIdBase;
        foreach (var system in data.Systems)
        {
            foreach (var systemUser in system.SystemUsers)
            {
                var owner = orgByPartyId.Values.FirstOrDefault(o => o.Org.OrgNumber == systemUser.OrgNumber);
                if (owner.Org is null || !byPartyId.ContainsKey(checked((uint)owner.Org.PartyId)))
                {
                    Warn($"System user {systemUser.Id} belongs to unknown organisation '{systemUser.OrgNumber}'");
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
        ByPersonIdentifier = Parties.Where(p => p.PersonIdentifier.HasValue).ToDictionary(p => p.PersonIdentifier.Value!.ToString());
        ByOrganizationIdentifier = Parties.Where(p => p.OrganizationIdentifier.HasValue).ToDictionary(p => p.OrganizationIdentifier.Value!.ToString());
        ByUserId = Parties.Where(p => p.UserIds.HasValue && p.UserIds.Value!.HasCurrentValue).ToDictionary(p => p.UserIds.Value!.CurrentValue.Value);
        Users = [.. users];
        UsersByUserId = Users.ToDictionary(u => u.Person.UserId);
        UsersByUsername = Users.Where(u => !string.IsNullOrEmpty(u.Person.UserName))
            .GroupBy(u => u.Person.UserName!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
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
        Warnings = [.. warnings];

        logger.LogInformation("Localtest register loaded: {Parties} parties, {Users} users, {Assignments} external role assignments", Parties.Length, Users.Length, Assignments.Length);
    }

    public StaticExternalRoleDefinitionPersistence RoleDefinitions { get; }

    public TestDataFile TestData { get; }

    /// <summary>All parties ordered by version id.</summary>
    public ImmutableArray<PartyRecord> Parties { get; }

    public IReadOnlyDictionary<Guid, PartyRecord> ByUuid { get; }

    public IReadOnlyDictionary<uint, PartyRecord> ByPartyId { get; }

    public IReadOnlyDictionary<string, PartyRecord> ByPersonIdentifier { get; }

    public IReadOnlyDictionary<string, PartyRecord> ByOrganizationIdentifier { get; }

    /// <summary>The test data of the organisations that loaded, by organisation number (for their notification addresses).</summary>
    public IReadOnlyDictionary<string, TestOrg> OrgsByOrganizationNumber { get; }

    public IReadOnlyDictionary<uint, PartyRecord> ByUserId { get; }

    public ImmutableArray<RoleAssignment> Assignments { get; }

    public ImmutableArray<ExternalRoleAssignmentEvent> RoleEvents { get; }

    public ulong MaxPartyVersionId { get; }

    /// <summary>The persons that loaded (including self-identified users), with what they hold for other parties.</summary>
    public ImmutableArray<TestUser> Users { get; }

    public IReadOnlyDictionary<int, TestUser> UsersByUserId { get; }

    public IReadOnlyDictionary<string, TestUser> UsersByUsername { get; }

    /// <summary>Problems found while building this snapshot (skipped persons, unknown parties, ...).</summary>
    public ImmutableArray<string> Warnings { get; }

    public TestUser? UserByPartyUuid(Guid partyUuid)
        => ByUuid.TryGetValue(partyUuid, out var party) && party.UserIds.HasValue && party.UserIds.Value.HasCurrentValue
            ? UsersByUserId.GetValueOrDefault(checked((int)party.UserIds.Value.CurrentValue.Value))
            : null;

    public TestUser? UserBySsn(string ssn)
        => ByPersonIdentifier.TryGetValue(ssn, out var party) && party.UserIds.HasValue && party.UserIds.Value.HasCurrentValue
            ? UsersByUserId.GetValueOrDefault(checked((int)party.UserIds.Value.CurrentValue.Value))
            : null;

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

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }


    /// <summary>
    /// The production Register always has a unit type (Access Management's import rejects an organisation without
    /// one). Old testData.json files leave it null: sub-units become BEDR and main units AS, with a load warning.
    /// </summary>
    private static string DefaultUnitType(TestOrg org) => org.ParentPartyId is null ? "AS" : "BEDR";

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

/// <summary>What a test user holds for one other party: CCR role identifiers, Altinn role codes (verbatim) and access packages.</summary>
public sealed record PartyGrant(PartyRecord Party, List<string> CcrRoles, List<string> RoleCodes, List<string> AccessPackages);

/// <summary>A loaded test person or self-identified user, with the role codes for their own party and grants for others.</summary>
public sealed record TestUser(TestPerson Person, PartyRecord Party, IReadOnlyList<string> SelfRoleCodes, IReadOnlyList<PartyGrant> Grants);

/// <summary>An external role assignment: <paramref name="FromParty"/> has the role <paramref name="Identifier"/> held by <paramref name="ToParty"/>.</summary>
public sealed record RoleAssignment(ExternalRoleSource Source, string Identifier, Guid FromParty, Guid ToParty);
