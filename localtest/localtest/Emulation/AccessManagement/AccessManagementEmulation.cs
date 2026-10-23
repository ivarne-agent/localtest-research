using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Localtest.Emulated;

/// <summary>
/// Access Management in minimal mode, under <c>accessmanagement/api/</c>: the signed-in user's authorized parties
/// (the monorepo backend's party list) from the test data, and the apps' instance delegations and revocations,
/// recorded and echoed as delegated (nothing reads them back: everything is permitted anyway).
/// </summary>
public static class AccessManagementEmulation
{
    public const string Service = "access-management";

    /// <summary>
    /// The marker an authorized party gets when the user may act for it without any role (an empty role list in the test
    /// data, as the old <c>authorization/partylist</c> allowed): the backend keeps only parties with a role, package or
    /// resource.
    /// </summary>
    public const string ActingForResource = "urn:altinn:resource:localtest-acting-for";

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapGet("/enduser/authorizedparties", (HttpRequest request, TokenMinter minter, TestDataStore store) =>
        {
            var (claims, error) = minter.Validate(Jwt.FromRequest(request));
            if (claims is not { } payload)
            {
                return EmulationResults.Problem(StatusCodes.Status401Unauthorized, "No valid user token", $"authorizedparties needs the signed-in user's token: {error}.", Service);
            }

            var data = store.Current;
            var user = int.TryParse(Jwt.Claim(payload, "urn:altinn:userid"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId)
                ? data.UsersByUserId.GetValueOrDefault(userId)
                : null;
            var parties = user is null ? [] : AuthorizedParties(data, user).ToList();
            return Results.Json(new AuthorizedPartiesResponse(parties), EmulationJsonContext.Default.AuthorizedPartiesResponse);
        });

        v1.MapPost("/app/delegations/resource/{resourceId}/instance/{instanceId}", (string resourceId, string instanceId, HttpRequest request, DelegationLog log) =>
            Delegate(request, resourceId, instanceId, "Delegated", log));

        v1.MapPost("/app/delegationrevoke/resource/{resourceId}/instance/{instanceId}", (string resourceId, string instanceId, HttpRequest request, DelegationLog log) =>
            Delegate(request, resourceId, instanceId, "Revoked", log));

        // Provisioning step 4 over HTTP: nothing to refresh, the parties are computed from the store on each request.
        app.MapPost(prefix + "/api/localtest/refresh", () => Results.NoContent());
    }

    /// <summary>The user's own party, then the parties held for, organisations with their sub-units (which inherit the roles).</summary>
    public static IEnumerable<AuthorizedParty> AuthorizedParties(TestDataSnapshot data, TestDataUser user)
    {
        yield return ToAuthorized(data, user.Party, user.SelfRoleCodes.Count > 0 ? user.SelfRoleCodes : ["PRIV"], [], withSubunits: false);
        foreach (var grant in user.Grants.Where(g => g.Party.PartyId != user.Party.PartyId))
        {
            yield return ToAuthorized(data, grant.Party, grant.RoleCodes, grant.AccessPackages, withSubunits: true);
        }
    }

    private static AuthorizedParty ToAuthorized(TestDataSnapshot data, TestParty party, IReadOnlyList<string> roles, IReadOnlyList<string> packages, bool withSubunits)
        => new(
            party.PartyUuid,
            party.PartyId,
            party.Name,
            party.OrgNumber,
            party.Ssn,
            party.Kind switch
            {
                PartyKind.Organisation => "Organization",
                PartyKind.SelfIdentified => "SelfIdentified",
                PartyKind.SystemUser => "SystemUser",
                _ => "Person",
            },
            party.UnitType,
            IsDeleted: false,
            OnlyHierarchyElementWithNoAccess: false,
            [.. packages],
            roles.Count == 0 && packages.Count == 0 ? [ActingForResource] : [],
            [.. roles],
            [],
            withSubunits ? [.. data.SubUnitsOf(party.PartyId).Select(s => ToAuthorized(data, s, roles, packages, withSubunits: false))] : []);

    /// <summary>Echoes the request as a <c>DelegationResponse</c> with every right in <paramref name="status"/>, and records it.</summary>
    private static async Task<IResult> Delegate(HttpRequest request, string resourceId, string instanceId, string status, DelegationLog log)
    {
        var body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted) as JsonObject ?? [];
        var rights = new JsonArray();
        if (body["rights"] is JsonArray requested)
        {
            foreach (var right in requested.OfType<JsonObject>())
            {
                rights.Add((JsonNode)new JsonObject
                {
                    ["resource"] = right["resource"]?.DeepClone(),
                    ["action"] = right["action"]?.DeepClone(),
                    ["status"] = status,
                });
            }
        }

        var response = new JsonObject
        {
            ["from"] = body["from"]?.DeepClone(),
            ["to"] = body["to"]?.DeepClone(),
            ["resourceId"] = resourceId,
            ["instanceId"] = instanceId,
            ["rights"] = rights,
        };
        var json = response.ToJsonString();
        log.Add(json);
        return Results.Text(json, "application/json");
    }
}

/// <summary>The instance delegations and revocations apps made, newest last (the last 1,000).</summary>
public sealed class DelegationLog
{
    private const int Capacity = 1000;
    private readonly ConcurrentQueue<string> _entries = new();

    public IEnumerable<string> Entries => _entries;

    public void Add(string response)
    {
        _entries.Enqueue(response);
        while (_entries.Count > Capacity && _entries.TryDequeue(out _))
        {
        }
    }
}

/// <summary>Access Management's <c>enduser/authorizedparties</c> answer.</summary>
public sealed record AuthorizedPartiesResponse(IReadOnlyList<AuthorizedParty> Data);

public sealed record AuthorizedParty(
    Guid PartyUuid,
    int PartyId,
    string Name,
    string? OrganizationNumber,
    string? PersonId,
    string Type,
    string? UnitType,
    bool IsDeleted,
    bool OnlyHierarchyElementWithNoAccess,
    IReadOnlyList<string> AuthorizedAccessPackages,
    IReadOnlyList<string> AuthorizedResources,
    IReadOnlyList<string> AuthorizedRoles,
    IReadOnlyList<AuthorizedInstance> AuthorizedInstances,
    IReadOnlyList<AuthorizedParty> Subunits);

public sealed record AuthorizedInstance(string ResourceId, string InstanceId);
