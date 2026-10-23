using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Altinn.Register.Localtest.TestData;

namespace Localtest.Emulated;

/// <summary>
/// The Register in minimal mode, under <c>register/api/</c>, from the test-data store: parties by id, UUID and lookup,
/// the party list with sub-units, the two URN queries, persons (SSN and last name, 429 after two failed lookups per
/// user, as production) and organisations; and the Register fake's <c>localtest/test-users</c> for tools.
/// </summary>
public sealed class RegisterEmulation
{
    public const string Service = "register";

    /// <summary>Production's <c>PersonLookupSettings</c>: two failed lookups per user, counted for an hour.</summary>
    private const int MaximumFailedLookups = 2;

    private static readonly TimeSpan FailedLookupsLifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, (int Count, DateTimeOffset Since)> _failedLookups = new(StringComparer.Ordinal);

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapGet("/parties/{partyId:int}", (int partyId, TestDataStore store) =>
            store.Current is var data && data.ByPartyId.GetValueOrDefault(partyId) is { } party
                ? Results.Json(RegisterModels.ToParty(data, party), EmulationJsonContext.Default.Party)
                : Results.NotFound());

        v1.MapGet("/parties/byuuid/{partyUuid:guid}", (Guid partyUuid, TestDataStore store) =>
            store.Current is var data && data.ByUuid.GetValueOrDefault(partyUuid) is { } party
                ? Results.Json(RegisterModels.ToParty(data, party), EmulationJsonContext.Default.Party)
                : Results.NotFound());

        v1.MapPost("/parties/lookup", async (HttpRequest request, TestDataStore store) =>
        {
            var lookup = await EmulationJson.Read(request, EmulationJsonContext.Default.PartyLookup);
            var data = store.Current;
            var party = lookup switch
            {
                { Ssn: { Length: > 0 } ssn } => data.BySsn.GetValueOrDefault(ssn),
                { OrgNo: { Length: > 0 } orgNo } => data.ByOrgNumber.GetValueOrDefault(orgNo),
                _ => null,
            };
            return party is null ? Results.NotFound() : Results.Json(RegisterModels.ToParty(data, party), EmulationJsonContext.Default.Party);
        });

        v1.MapPost("/parties/partylist", async (HttpRequest request, bool? fetchSubUnits, TestDataStore store) =>
        {
            var ids = await EmulationJson.Read(request, EmulationJsonContext.Default.Int32Array) ?? [];
            var data = store.Current;
            var parties = ids.Select(data.ByPartyId.GetValueOrDefault).OfType<TestParty>()
                .Select(p => RegisterModels.ToParty(data, p, fetchSubUnits ?? false)).ToArray();
            return Results.Json(parties, EmulationJsonContext.Default.PartyArray);
        });

        v1.MapGet("/organizations/{orgNumber}", (string orgNumber, TestDataStore store) =>
            store.Current.ByOrgNumber.GetValueOrDefault(orgNumber) is { Org: { } org } party
                ? Results.Json(RegisterModels.ToOrganization(party, org), EmulationJsonContext.Default.Organization)
                : Results.NotFound());

        v1.MapGet("/persons", (HttpRequest request, TestDataStore store, RegisterEmulation register) => register.Person(request, store.Current));

        // The two URN queries app-lib makes (the party id, and the party UUID for Notifications), under the Register's
        // newer API (the paths app-lib calls, under ApiRegisterEndpoint).
        v1.MapPost("/apps/parties/query", (HttpRequest request, TestDataStore store) => Query(request, store.Current));
        v1.MapPost("/access-management/parties/query", (HttpRequest request, TestDataStore store) => Query(request, store.Current));

        // The Register fake's views for the picker and tools (Home and the ID-porten picker read the store in process).
        app.MapGet(prefix + "/api/localtest/test-users", (TestDataStore store) =>
        {
            var data = store.Current;
            return Results.Json(data.Users.Select(u => TestUser(data, u)).ToArray(), EmulationJsonContext.Default.TestUserEntryArray);
        });
        app.MapGet(prefix + "/api/localtest/organizations", (TestDataStore store) => Results.Json(
            store.Current.ByPartyId.Values.Where(p => p.Org is not null).OrderBy(p => p.PartyId)
                .Select(p => new TestOrgEntry(p.PartyId, p.PartyUuid, p.OrgNumber!, p.Name, p.ParentPartyId, p.UnitType, [.. p.Org!.NotificationAddresses.Select(a => new TestNotificationAddressEntry(a.Email, a.MobileNumber))]))
                .ToArray(),
            EmulationJsonContext.Default.TestOrgEntryArray));
    }

    /// <summary><c>GET persons</c> with <c>X-Ai-NationalIdentityNumber</c> and <c>X-Ai-LastName</c> (Base64 UTF-8).</summary>
    private IResult Person(HttpRequest request, TestDataSnapshot data)
    {
        var user = Jwt.Payload(Jwt.FromRequest(request)) is { } claims ? Jwt.Claim(claims, "urn:altinn:userid") ?? "anonymous" : "anonymous";
        var now = DateTimeOffset.UtcNow;
        if (_failedLookups.TryGetValue(user, out var failed) && now - failed.Since < FailedLookupsLifetime && failed.Count >= MaximumFailedLookups)
        {
            return EmulationResults.Problem(StatusCodes.Status429TooManyRequests, "Too many failed person lookups", $"User {user} has made {failed.Count} failed person lookups; try again later.", Service);
        }

        var ssn = request.Headers["X-Ai-NationalIdentityNumber"].ToString();
        string lastName;
        try
        {
            lastName = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers["X-Ai-LastName"].ToString()));
        }
        catch (FormatException)
        {
            lastName = string.Empty;
        }

        if (data.BySsn.GetValueOrDefault(ssn) is { Person: { } person } party
            && person.LastName.Length > 0
            && string.Equals(person.LastName.Trim(), lastName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(RegisterModels.ToPerson(party, person), EmulationJsonContext.Default.Person);
        }

        _failedLookups.AddOrUpdate(user, (1, now), (_, old) => now - old.Since < FailedLookupsLifetime ? (old.Count + 1, old.Since) : (1, now));
        return Results.NotFound();
    }

    private static async Task<IResult> Query(HttpRequest request, TestDataSnapshot data)
    {
        var query = await EmulationJson.Read(request, EmulationJsonContext.Default.UrnQuery);
        var items = (query?.Data ?? []).Select(urn => Resolve(data, urn)).OfType<TestParty>()
            .Select(p => new PartyQueryItem(
                p.PartyUuid,
                p.PartyId,
                RegisterModels.TypeName(p),
                p.Name,
                data.UsersByPartyId.GetValueOrDefault(p.PartyId) is { } user ? new PartyQueryUser(user.UserId) : null))
            .ToList();
        return Results.Json(new PartyQueryResult(items), EmulationJsonContext.Default.PartyQueryResult);
    }

    /// <summary>The party a party URN names: by party id or UUID, national identity number, organisation number or user id.</summary>
    public static TestParty? Resolve(TestDataSnapshot data, string urn)
    {
        static string? After(string urn, string prefix) => urn.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? urn[prefix.Length..] : null;
        return urn switch
        {
            _ when After(urn, "urn:altinn:person:identifier-no:") is { } ssn => data.BySsn.GetValueOrDefault(ssn),
            _ when After(urn, "urn:altinn:organization:identifier-no:") is { } orgNo => data.ByOrgNumber.GetValueOrDefault(orgNo),
            _ when After(urn, "urn:altinn:party:id:") is { } id && int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var partyId) => data.ByPartyId.GetValueOrDefault(partyId),
            _ when After(urn, "urn:altinn:party:uuid:") is { } uuid && Guid.TryParse(uuid, out var partyUuid) => data.ByUuid.GetValueOrDefault(partyUuid),
            _ when After(urn, "urn:altinn:user:id:") is { } user && int.TryParse(user, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId) => data.UsersByUserId.GetValueOrDefault(userId)?.Party,
            _ => null,
        };
    }

    private static TestUserEntry TestUser(TestDataSnapshot data, TestDataUser user)
    {
        var p = user.Person;
        List<TestUserPartyEntry> parties =
        [
            new(user.Party.PartyId, user.Party.PartyUuid, null, user.Party.Name, null, user.SelfRoleCodes, []),
            .. user.Grants.Select(g => new TestUserPartyEntry(g.Party.PartyId, g.Party.PartyUuid, g.Party.OrgNumber, g.Party.Name, g.Party.UnitType, g.RoleCodes, g.AccessPackages)),
        ];
        var actingFor = data.ActingFor(user).Select(party => new ActingForEntry(
            party.PartyId,
            party.PartyUuid,
            RegisterModels.TypeName(party),
            party.OrgNumber,
            party.Ssn,
            party.Name,
            party.UnitType,
            party.ParentPartyId,
            data.RoleCodes(user, party))).ToList();
        return new TestUserEntry(
            p.UserId,
            user.Party.PartyId,
            user.Party.PartyUuid,
            p.Ssn,
            user.Party.Name,
            p.UserName,
            p.Email,
            p.Language,
            parties,
            RegisterModels.TypeName(user.Party),
            user.SelfRoleCodes,
            actingFor,
            p.Reserved ?? false);
    }
}

/// <summary>
/// The test users for the picker and Home in process (<see cref="IdPorten.ITestUserSource"/>), from the store: the
/// user's own party first, then the parties the user holds something for directly, as the Register fake's
/// <c>test-users</c> lists them.
/// </summary>
public sealed class InProcessTestUsers(TestDataStore store) : IdPorten.ITestUserSource
{
    public Task<IdPorten.TestUser[]> GetAll(CancellationToken cancellationToken)
    {
        var data = store.Current;
        return Task.FromResult(data.Users.Select(u => new IdPorten.TestUser(
            u.UserId,
            u.Party.PartyId,
            u.Party.PartyUuid,
            string.IsNullOrEmpty(u.Person.Ssn) ? null : u.Person.Ssn,
            u.Party.Name,
            [
                new IdPorten.TestUserParty(u.Party.PartyId, u.Party.PartyUuid, u.Party.Name),
                .. u.Grants.Select(g => new IdPorten.TestUserParty(g.Party.PartyId, g.Party.PartyUuid, g.Party.Name)),
            ])).ToArray());
    }
}
