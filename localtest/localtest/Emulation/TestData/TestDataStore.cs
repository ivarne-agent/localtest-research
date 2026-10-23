using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Altinn.Register.Localtest.TestData;

namespace Localtest.Emulated;

/// <summary>
/// The minimal mode's test data: the seed (<c>testData.json</c>, a copy of the Register component's, embedded) plus
/// every registered app's <c>testData.json</c>, merged with the rules of <c>localtest/testdata/README.md</c> by
/// <see cref="TestDataMerger"/> (a copy of the Register fake's): when every registered app has test data only the apps'
/// data is loaded, otherwise the apps' data is added to the seed. Readers take <see cref="Current"/> once per request; a
/// registration swaps the whole snapshot.
/// </summary>
public sealed class TestDataStore
{
    private readonly TestDataFile _seed;
    private readonly ILogger<TestDataStore> _logger;
    private readonly Lock _lock = new();
    private ImmutableSortedDictionary<string, TestDataFile?> _apps = ImmutableSortedDictionary.Create<string, TestDataFile?>(StringComparer.Ordinal);
    private volatile TestDataSnapshot _current;

    public TestDataStore(ILogger<TestDataStore> logger)
    {
        _logger = logger;
        using var stream = typeof(TestDataStore).Assembly.GetManifestResourceStream(SeedResource)
            ?? throw new InvalidOperationException($"The embedded seed {SeedResource} is missing.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        _seed = TestDataFile.Parse(reader.ReadToEnd(), "seed");
        _current = Build();
    }

    /// <summary>The embedded copy of <c>components/register/testdata/testData.json</c>.</summary>
    public const string SeedResource = "Localtest.Emulation.testData.json";

    public TestDataSnapshot Current => _current;

    /// <summary>Registers an app with its <c>testData.json</c> (null or empty: none) and rebuilds; replaces an earlier registration.</summary>
    /// <exception cref="System.Text.Json.JsonException">The file is not valid JSON.</exception>
    public TestDataSnapshot Register(string org, string app, ReadOnlyMemory<byte>? json)
    {
        var text = json is { Length: > 0 } body ? Encoding.UTF8.GetString(body.Span).Trim() : string.Empty;
        var data = text.Length == 0 || text == "null" ? null : TestDataFile.Parse(text, $"app {org}/{app}");
        lock (_lock)
        {
            _apps = _apps.SetItem(Key(org, app), data);
            return _current = Build();
        }
    }

    /// <summary>Removes the app's contribution and rebuilds; false when the app was not registered.</summary>
    public bool Unregister(string org, string app)
    {
        lock (_lock)
        {
            if (!_apps.ContainsKey(Key(org, app)))
            {
                return false;
            }

            _apps = _apps.Remove(Key(org, app));
            _current = Build();
            return true;
        }
    }

    private static string Key(string org, string app) => $"{org}/{app}";

    private TestDataSnapshot Build()
    {
        var withData = _apps.Where(a => a.Value is not null).ToList();
        var appsOnly = _apps.Count > 0 && withData.Count == _apps.Count;
        IEnumerable<(string Source, TestDataFile Data)> sources = withData.Select(a => ($"app {a.Key}", a.Value!));
        if (!appsOnly)
        {
            sources = sources.Prepend(("seed", _seed));
        }

        var conflicts = new List<string>();
        var merged = TestDataMerger.Merge(sources, conflicts);
        var snapshot = new TestDataSnapshot(merged, conflicts, appsOnly ? "apps" : "seed+apps");
        foreach (var message in conflicts.Concat(snapshot.Warnings))
        {
            _logger.LogWarning("Test data: {Message}", message);
        }

        return snapshot;
    }
}

/// <summary>A party of the test data: a person, a self-identified user, an organisation or a system user.</summary>
public sealed record TestParty(
    int PartyId,
    Guid PartyUuid,
    PartyKind Kind,
    string Name,
    string? Ssn,
    string? OrgNumber,
    string? UnitType,
    int? ParentPartyId,
    TestPerson? Person,
    TestOrg? Org)
{
    /// <summary>The old Register's <c>PartyType</c> numbers: 1 person, 2 organisation, 3 self-identified, 5 system user.</summary>
    public int PartyTypeNumber => Kind switch
    {
        PartyKind.Person => 1,
        PartyKind.Organisation => 2,
        PartyKind.SelfIdentified => 3,
        _ => 5,
    };
}

public enum PartyKind
{
    Person,
    Organisation,
    SelfIdentified,
    SystemUser,
}

/// <summary>What a user holds for another party: Altinn role codes (verbatim) and access packages.</summary>
public sealed record TestGrant(TestParty Party, IReadOnlyList<string> RoleCodes, IReadOnlyList<string> AccessPackages);

/// <summary>A loaded person or self-identified user, with the role codes on the own party and the grants for others.</summary>
public sealed record TestDataUser(TestPerson Person, TestParty Party, IReadOnlyList<string> SelfRoleCodes, IReadOnlyList<TestGrant> Grants)
{
    public int UserId => Person.UserId;
}

/// <summary>
/// One immutable view of the merged test data, indexed the ways the emulated services look things up. Persons and
/// organisations whose identifiers fail the mod-11 checks are skipped with a warning, as the Register component does.
/// </summary>
public sealed class TestDataSnapshot
{
    private const int SystemUserPartyIdBase = 900_000_000;

    public TestDataSnapshot(TestDataFile data, IReadOnlyList<string> conflicts, string source)
    {
        Data = data;
        Conflicts = conflicts;
        Source = source;
        var warnings = new List<string>();
        var parties = new List<TestParty>();

        foreach (var org in data.Orgs.OrderBy(o => o.PartyId))
        {
            if (!IsOrganisationNumber(org.OrgNumber))
            {
                warnings.Add($"Skipping organisation with partyId {org.PartyId}: '{org.OrgNumber}' is not a valid organisation number (mod-11 check)");
                continue;
            }

            var unitType = string.IsNullOrWhiteSpace(org.UnitType) ? (org.ParentPartyId is null ? "AS" : "BEDR") : org.UnitType;
            parties.Add(new TestParty(org.PartyId, org.PartyUuid ?? UuidFromPartyId(org.PartyId), PartyKind.Organisation, org.Name ?? org.OrgNumber, null, org.OrgNumber, unitType, org.ParentPartyId, null, org));
        }

        var persons = new List<(TestPerson Person, TestParty Party)>();
        foreach (var person in data.Persons.OrderBy(p => p.PartyId))
        {
            var uuid = person.PartyUuid ?? UuidFromPartyId(person.PartyId);
            TestParty party;
            if (string.IsNullOrEmpty(person.Ssn))
            {
                if (string.IsNullOrWhiteSpace(person.Email))
                {
                    warnings.Add($"Skipping self-identified person with partyId {person.PartyId}: no 'email' (self-identified users are e-mail registrations)");
                    continue;
                }

                party = new TestParty(person.PartyId, uuid, PartyKind.SelfIdentified, person.Email.Trim().ToLowerInvariant(), null, null, null, null, person, null);
            }
            else if (!IsPersonIdentifier(person.Ssn))
            {
                warnings.Add($"Skipping person with partyId {person.PartyId}: '{person.Ssn}' is not a valid national identity number (mod-11 check)");
                continue;
            }
            else
            {
                var name = string.Join(' ', new[] { person.FirstName, person.MiddleName, person.LastName }.Where(static s => !string.IsNullOrWhiteSpace(s)));
                party = new TestParty(person.PartyId, uuid, PartyKind.Person, name, person.Ssn, null, null, null, person, null);
            }

            parties.Add(party);
            persons.Add((person, party));
        }

        var systemUserPartyId = SystemUserPartyIdBase;
        foreach (var system in data.Systems)
        {
            foreach (var systemUser in system.SystemUsers)
            {
                if (!parties.Any(p => p.OrgNumber == systemUser.OrgNumber))
                {
                    warnings.Add($"System user {systemUser.Id} belongs to unknown organisation '{systemUser.OrgNumber}'");
                    continue;
                }

                parties.Add(new TestParty(systemUserPartyId++, systemUser.Id, PartyKind.SystemUser, system.Name, null, null, null, null, null, null));
            }
        }

        ByPartyId = parties.ToDictionary(p => p.PartyId);
        ByUuid = parties.GroupBy(p => p.PartyUuid).ToDictionary(g => g.Key, g => g.First());
        BySsn = parties.Where(p => p.Ssn is not null).ToDictionary(p => p.Ssn!, StringComparer.Ordinal);
        ByOrgNumber = parties.Where(p => p.OrgNumber is not null).ToDictionary(p => p.OrgNumber!, StringComparer.Ordinal);
        foreach (var org in parties.Where(p => p.ParentPartyId is { } parent && !ByPartyId.ContainsKey(parent)))
        {
            warnings.Add($"Organisation {org.PartyId} has unknown parentPartyId {org.ParentPartyId}");
        }

        var users = new List<TestDataUser>();
        foreach (var (person, party) in persons)
        {
            var self = new List<string>();
            var grants = new SortedDictionary<int, (List<string> Roles, List<string> Packages)>();
            foreach (var (forPartyId, roles) in person.PartyRoles)
            {
                if (forPartyId == person.PartyId.ToString(CultureInfo.InvariantCulture))
                {
                    self.AddRange(roles.Select(r => r.Value).Where(v => !self.Contains(v)));
                }
                else if (Grant(forPartyId, "roles") is { } grant)
                {
                    grant.Roles.AddRange(roles.Select(r => r.Value).Where(v => v.Length > 0 && !grant.Roles.Contains(v)));
                }
            }

            foreach (var (forPartyId, packages) in person.AccessPackages)
            {
                // Packages on the own party are not a grant for another party (the own party is listed once).
                if (forPartyId == person.PartyId.ToString(CultureInfo.InvariantCulture))
                {
                    continue;
                }

                if (Grant(forPartyId, "access packages") is { } grant)
                {
                    grant.Packages.AddRange(packages.Where(p => !grant.Packages.Contains(p)));
                }
            }

            users.Add(new TestDataUser(person, party, self, [.. grants.Select(g => new TestGrant(ByPartyId[g.Key], g.Value.Roles, g.Value.Packages))]));

            (List<string> Roles, List<string> Packages)? Grant(string forPartyId, string what)
            {
                if (!int.TryParse(forPartyId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || !ByPartyId.ContainsKey(id))
                {
                    warnings.Add($"Person {person.PartyId} has {what} for unknown party '{forPartyId}'");
                    return null;
                }

                if (!grants.TryGetValue(id, out var grant))
                {
                    grants[id] = grant = ([], []);
                }

                return grant;
            }
        }

        Users = users;
        UsersByUserId = users.ToDictionary(u => u.UserId);
        UsersByPartyId = users.ToDictionary(u => u.Party.PartyId);
        Warnings = warnings;
    }

    public TestDataFile Data { get; }

    /// <summary><c>apps</c> (every registered app has test data) or <c>seed+apps</c>.</summary>
    public string Source { get; }

    public IReadOnlyList<string> Conflicts { get; }

    public IReadOnlyList<string> Warnings { get; }

    public IReadOnlyDictionary<int, TestParty> ByPartyId { get; }

    public IReadOnlyDictionary<Guid, TestParty> ByUuid { get; }

    public IReadOnlyDictionary<string, TestParty> BySsn { get; }

    public IReadOnlyDictionary<string, TestParty> ByOrgNumber { get; }

    public IReadOnlyList<TestDataUser> Users { get; }

    public IReadOnlyDictionary<int, TestDataUser> UsersByUserId { get; }

    public IReadOnlyDictionary<int, TestDataUser> UsersByPartyId { get; }

    public TestDataUser? UserBySsn(string ssn) => BySsn.TryGetValue(ssn, out var party) ? UsersByPartyId.GetValueOrDefault(party.PartyId) : null;

    /// <summary>The direct sub-units of an organisation, by party id.</summary>
    public IEnumerable<TestParty> SubUnitsOf(int partyId) => ByPartyId.Values.Where(p => p.ParentPartyId == partyId).OrderBy(p => p.PartyId);

    /// <summary>
    /// Everything the user may act for, as the old <c>authorization/partylist</c> and the Register fake's <c>actingFor</c>
    /// define it: the own party, the parties the user holds a role or package for, then the sub-units of those.
    /// </summary>
    public IReadOnlyList<TestParty> ActingFor(TestDataUser user)
    {
        var parties = new List<TestParty> { user.Party };
        parties.AddRange(user.Grants.Select(g => g.Party).Where(p => p.PartyId != user.Party.PartyId));
        foreach (var grant in user.Grants)
        {
            parties.AddRange(SubUnitsOf(grant.Party.PartyId).Where(s => parties.All(p => p.PartyId != s.PartyId)).ToList());
        }

        return parties;
    }

    /// <summary>The role codes the user holds for the party: on the own party, given directly, or through the main unit.</summary>
    public IReadOnlyList<string> RoleCodes(TestDataUser user, TestParty party)
        => party.PartyId == user.Party.PartyId
            ? user.SelfRoleCodes
            : user.Grants.FirstOrDefault(g => g.Party.PartyId == party.PartyId)?.RoleCodes
                ?? user.Grants.FirstOrDefault(g => g.Party.PartyId == party.ParentPartyId)?.RoleCodes
                ?? [];

    /// <summary>The party UUID used when the test data has none: the party id, zero-padded into a GUID (as the Register).</summary>
    public static Guid UuidFromPartyId(int partyId) => Guid.ParseExact(partyId.ToString("D32", CultureInfo.InvariantCulture), "N");

    /// <summary>The national identity number's two mod-11 control digits, as the Register's <c>PersonIdentifier</c> checks them.</summary>
    public static bool IsPersonIdentifier(string value)
    {
        if (value.Length != 11 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        int[] k1Weights = [3, 7, 6, 1, 8, 9, 4, 5, 2];
        int[] k2Weights = [5, 4, 3, 2, 7, 6, 5, 4, 3, 2];
        var digits = value.Select(c => c - '0').ToArray();
        var k1Base = k1Weights.Select((w, i) => w * digits[i]).Sum() % 11;
        // The 2032 rule: the first control digit may be one of four values.
        var k1Valid = Enumerable.Range(11, 4).Any(n => (n - k1Base) % 11 == digits[9]);
        var k2 = (11 - (k2Weights.Select((w, i) => w * digits[i]).Sum() % 11)) % 11;
        return k1Valid && k2 == digits[10];
    }

    /// <summary>The organisation number's mod-11 control digit, as the Register's <c>OrganizationIdentifier</c> checks it.</summary>
    public static bool IsOrganisationNumber(string value)
    {
        if (value.Length != 9 || !value.All(char.IsAsciiDigit))
        {
            return false;
        }

        int[] weights = [3, 2, 7, 6, 5, 4, 3, 2];
        var sum = weights.Select((w, i) => w * (value[i] - '0')).Sum();
        return (11 - (sum % 11)) % 11 == value[8] - '0';
    }
}
