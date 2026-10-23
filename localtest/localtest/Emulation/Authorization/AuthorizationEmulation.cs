using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Localtest.Emulated;

/// <summary>
/// Authorization in minimal mode, under <c>authorization/api/</c>: a PDP that permits everything (<c>decision</c>),
/// the user's parties and their validation from the test data, the policy push of provisioning (kept, not evaluated),
/// and the Authorization fake's <c>localtest/decision</c> helper. No obligations: a Permit without an
/// authentication-level obligation is allowed at any level.
/// </summary>
public static class AuthorizationEmulation
{
    public const string Service = "authorization";

    private const string StatusOk = "urn:oasis:names:tc:xacml:1.0:status:ok";

    /// <summary>The XACML category of each of the request's shorthand lists.</summary>
    private static readonly (string Property, string CategoryId)[] Shorthands =
    [
        ("accessSubject", "urn:oasis:names:tc:xacml:1.0:subject-category:access-subject"),
        ("action", "urn:oasis:names:tc:xacml:3.0:attribute-category:action"),
        ("resource", "urn:oasis:names:tc:xacml:3.0:attribute-category:resource"),
        ("recipientSubject", "urn:oasis:names:tc:xacml:1.0:subject-category:recipient-subject"),
        ("intermediarySubject", "urn:oasis:names:tc:xacml:1.0:subject-category:intermediary-subject"),
        ("requestingMachine", "urn:oasis:names:tc:xacml:1.0:subject-category:requesting-machine"),
    ];

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapPost("/decision", async (HttpRequest request) =>
        {
            using var document = await EmulationJson.ReadDocument(request);
            return document is null
                ? EmulationResults.Problem(StatusCodes.Status400BadRequest, "Not a XACML JSON request", "The body is not JSON.", Service)
                : Json(Decide(document.RootElement));
        });

        v1.MapGet("/parties", (int? userId, TestDataStore store) =>
        {
            var data = store.Current;
            if (userId is not { } id || data.UsersByUserId.GetValueOrDefault(id) is not { } user)
            {
                return Results.Json(Array.Empty<Party>(), EmulationJsonContext.Default.PartyArray);
            }

            return Results.Json(PartyList(data, user).ToArray(), EmulationJsonContext.Default.PartyArray);
        });

        v1.MapGet("/parties/{partyId:int}/validate", (int partyId, int? userId, TestDataStore store) =>
        {
            var data = store.Current;
            var valid = userId is { } id && data.UsersByUserId.GetValueOrDefault(id) is { } user && data.ActingFor(user).Any(p => p.PartyId == partyId);
            return Results.Json(valid, EmulationJsonContext.Default.Boolean);
        });

        // Provisioning step 3, when an app runner pushes the policy over HTTP (in process otherwise): kept per app.
        v1.MapPost("/policies", async (HttpRequest request, string? org, string? app, PolicyStore policies) =>
        {
            if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(app))
            {
                return EmulationResults.Problem(StatusCodes.Status400BadRequest, "org and app are required", "POST policies?org={org}&app={app} with the policy XML.", Service);
            }

            using var reader = new StreamReader(request.Body);
            policies.Set(org, app, await reader.ReadToEndAsync(request.HttpContext.RequestAborted));
            return Results.Ok();
        });

        // The Authorization fake's helper: the request an app's PEP would send for a user, party, app and action.
        app.MapGet(prefix + "/api/localtest/decision", (int userId, int partyId, string org, string app, string? action, string? instanceId, string? task) =>
        {
            var resource = new JsonArray { Attribute("urn:altinn:org", org), Attribute("urn:altinn:app", app), Attribute("urn:altinn:partyid", partyId.ToString(CultureInfo.InvariantCulture)) };
            if (!string.IsNullOrEmpty(instanceId))
            {
                resource.Add(Attribute("urn:altinn:instance-id", instanceId));
            }

            if (!string.IsNullOrEmpty(task))
            {
                resource.Add(Attribute("urn:altinn:task", task));
            }

            var request = new JsonObject
            {
                ["request"] = new JsonObject
                {
                    ["accessSubject"] = new JsonArray { new JsonObject { ["attribute"] = new JsonArray { Attribute("urn:altinn:userid", userId.ToString(CultureInfo.InvariantCulture)) } } },
                    ["action"] = new JsonArray { new JsonObject { ["attribute"] = new JsonArray { Attribute("urn:oasis:names:tc:xacml:1.0:action:action-id", action ?? "read", includeInResult: true) } } },
                    ["resource"] = new JsonArray { new JsonObject { ["attribute"] = resource } },
                },
            };
            using var document = JsonDocument.Parse(request.ToJsonString());
            return Json(Decide(document.RootElement));
        });
    }

    /// <summary>
    /// <c>Permit</c> for every request. A single request gets one result; a multi-request one per
    /// <c>multiRequests.requestReference</c>. Each result echoes, as <c>category</c>, the attributes of the referenced
    /// categories marked <c>includeInResult</c>, as the real PDP does: Storage maps results to instances by the echoed
    /// instance and action ids, app-lib reads the permitted actions and the signing key-role organisations from them.
    /// </summary>
    public static JsonObject Decide(JsonElement root)
    {
        var request = Property(root, "request") ?? root;
        var categories = new List<(string? Id, string CategoryId, JsonElement Category)>();
        foreach (var (property, categoryId) in Shorthands)
        {
            if (Property(request, property) is { } list)
            {
                foreach (var category in Items(list))
                {
                    categories.Add((String(category, "id"), String(category, "categoryId") ?? categoryId, category));
                }
            }
        }

        if (Property(request, "category") is { } generic)
        {
            foreach (var category in Items(generic))
            {
                categories.Add((String(category, "id"), String(category, "categoryId") ?? string.Empty, category));
            }
        }

        var references = Property(request, "multiRequests") is { } multi && Property(multi, "requestReference") is { ValueKind: JsonValueKind.Array } refs
            ? refs.EnumerateArray()
                .Select(r => Property(r, "referenceId") is { ValueKind: JsonValueKind.Array } ids ? ids.EnumerateArray().Select(i => i.GetString()).ToHashSet(StringComparer.Ordinal) : [])
                .ToList()
            : null;

        var results = new JsonArray();
        if (references is null)
        {
            results.Add(Result(categories.Select(c => (c.CategoryId, c.Category))));
        }
        else
        {
            foreach (var reference in references)
            {
                results.Add(Result(categories.Where(c => c.Id is not null && reference.Contains(c.Id)).Select(c => (c.CategoryId, c.Category))));
            }
        }

        return new JsonObject { ["response"] = results };
    }

    /// <summary>The acting-for list as party objects: the own party and the parties held for, organisations with their sub-units.</summary>
    public static IEnumerable<Party> PartyList(TestDataSnapshot data, TestDataUser user)
        => new[] { user.Party }.Concat(user.Grants.Select(g => g.Party).Where(p => p.PartyId != user.Party.PartyId))
            .Select(p => RegisterModels.ToParty(data, p, withSubUnits: true));

    private static JsonObject Result(IEnumerable<(string CategoryId, JsonElement Category)> categories)
    {
        var echoed = new JsonArray();
        foreach (var (categoryId, category) in categories)
        {
            var attributes = new JsonArray();
            if (Property(category, "attribute") is { } list)
            {
                foreach (var attribute in Items(list).Where(a => Property(a, "includeInResult") is { ValueKind: JsonValueKind.True }))
                {
                    attributes.Add(new JsonObject
                    {
                        ["attributeId"] = String(attribute, "attributeId"),
                        ["value"] = String(attribute, "value"),
                        ["dataType"] = DataType(String(attribute, "dataType")),
                        ["includeInResult"] = true,
                    });
                }
            }

            if (attributes.Count > 0)
            {
                echoed.Add(new JsonObject { ["categoryId"] = categoryId, ["attribute"] = attributes });
            }
        }

        return new JsonObject
        {
            ["decision"] = "Permit",
            ["status"] = new JsonObject { ["statusCode"] = new JsonObject { ["value"] = StatusOk } },
            ["category"] = echoed,
        };
    }

    private static IResult Json(JsonObject body) => Results.Text(body.ToJsonString(), "application/json");

    /// <summary>The XML Schema type the real PDP answers for a request's short data type (<c>string</c>).</summary>
    private static string DataType(string? dataType)
        => string.IsNullOrEmpty(dataType) ? "http://www.w3.org/2001/XMLSchema#string"
            : dataType.Contains(':', StringComparison.Ordinal) ? dataType
            : "http://www.w3.org/2001/XMLSchema#" + dataType;

    /// <summary>A property by name, case-insensitively: the PEP writes camelCase, the Authorization fake's helper PascalCase.</summary>
    private static JsonElement? Property(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.ValueKind == JsonValueKind.Null ? null : property.Value;
            }
        }

        return null;
    }

    /// <summary>An array's items, or a single object as one item (the JSON profile allows both).</summary>
    private static IEnumerable<JsonElement> Items(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.Array => element.EnumerateArray(),
            JsonValueKind.Object => [element],
            _ => [],
        };

    private static string? String(JsonElement element, string name)
        => Property(element, name) is { } value ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText() : null;

    private static JsonObject Attribute(string id, string value, bool includeInResult = false)
        => new() { ["attributeId"] = id, ["value"] = value, ["dataType"] = "string", ["includeInResult"] = includeInResult };
}

/// <summary>The apps' policies as provisioning pushed them, kept per app (read by nothing in phase 1; the policy PDP of phase 3 would).</summary>
public sealed class PolicyStore
{
    private readonly ConcurrentDictionary<string, string> _policies = new(StringComparer.Ordinal);

    public void Set(string org, string app, string policy) => _policies[$"{org}/{app}"] = policy;

    public string? Get(string org, string app) => _policies.GetValueOrDefault($"{org}/{app}");

    public bool Remove(string org, string app) => _policies.TryRemove($"{org}/{app}", out _);
}
