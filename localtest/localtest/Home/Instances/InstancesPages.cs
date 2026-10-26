using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace Localtest.Home.Instances;

/// <summary>The HTML of the list, the instance page and the error page; Storage's JSON is read with <see cref="JsonDocument"/> (no serializer metadata under Native AOT).</summary>
public static class InstancesPages
{
    private const string Style =
        "body{font-family:system-ui,sans-serif;max-width:1200px;margin:24px auto;padding:0 16px;color:#1a1a1a;background:#fff}"
        + "table{border-collapse:collapse;width:100%;margin:8px 0 16px;font-size:.92em}th,td{border-bottom:1px solid #ddd;padding:5px 8px;text-align:left;vertical-align:top}"
        + "th{background:#f3f3f3}code,pre{font-family:ui-monospace,monospace;font-size:.92em}pre{background:#f6f6f6;padding:10px;overflow-x:auto;white-space:pre-wrap;word-break:break-all}"
        + ".badge{display:inline-block;padding:0 6px;border-radius:4px;background:#eee;margin:1px 4px 1px 0;font-size:.85em}.error{color:#a00}"
        + ".links a{display:inline-block;margin-right:10px}.muted{color:#666}a{color:#0b5cad}td.wrap{word-break:break-all}";

    /// <summary>The list: one row per instance, each linking to its page with the same token; a next-page link when Storage gave one.</summary>
    public static IResult List(IQueryCollection query, TokenChoice token, StorageClient.Answer answer, InstanceLinks links, IReadOnlyDictionary<int, string>? urns)
    {
        var html = Start("Storage instances", query);
        Asked(html, token, answer);
        if (answer.Status != StatusCodes.Status200OK)
        {
            StorageError(html, answer);
            return Page(html, answer.Status);
        }

        using var document = JsonDocument.Parse(answer.Body);
        var root = document.RootElement;
        var instances = root.TryGetProperty("instances", out var array) && array.ValueKind == JsonValueKind.Array ? array : default;
        var count = instances.ValueKind == JsonValueKind.Array ? instances.GetArrayLength() : 0;
        html.Append("<p>").Append(count).Append(count == 1 ? " instance" : " instances").Append(" on this page.</p>");
        if (count > 0)
        {
            html.Append("<table><thead><tr><th>Instance</th><th>App</th><th>Owner</th><th>Process</th><th>Status</th><th>Created</th><th>Last changed</th><th>Data</th><th>Links</th></tr></thead><tbody>");
            foreach (var instance in instances.EnumerateArray())
            {
                if (!Id(instance, out var partyId, out var instanceGuid))
                {
                    continue;
                }

                html.Append("<tr><td class=\"wrap\"><a href=\"").Append(Encode(InstancePath(partyId, instanceGuid, token))).Append("\">")
                    .Append(Encode($"{partyId}/{instanceGuid}")).Append("</a></td><td>").Append(Encode(Text(instance, "appId")))
                    .Append("</td><td>").Append(partyId).Append("</td><td>").Append(Encode(ProcessSummary(instance)))
                    .Append("</td><td>").Append(StatusBadges(instance)).Append("</td><td>").Append(Encode(Text(instance, "created")))
                    .Append("</td><td>").Append(Encode(Text(instance, "lastChanged"))).Append("</td><td>")
                    .Append(instance.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data.GetArrayLength().ToString(CultureInfo.InvariantCulture) : "<span class=\"muted\">not included</span>")
                    .Append("</td><td class=\"links\">");
                LinkList(html, Links(instance, partyId, instanceGuid, links, urns));
                html.Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        if (Text(root, "next") is { Length: > 0 } next && QueryHelpers.ParseQuery(new Uri(next).Query).TryGetValue("continuationToken", out var continuation))
        {
            var pairs = Pairs(query).Where(p => p.Key != "continuationToken").Append(new("continuationToken", continuation.ToString()));
            html.Append("<p><a id=\"next\" href=\"/Home/Instances/Results").Append(Encode(StorageQuery.QueryString(pairs))).Append("\">Next page</a></p>");
        }

        RawJson(html, answer.Body);
        return Page(html, StatusCodes.Status200OK);
    }

    /// <summary>One instance: its fields, its data elements (downloads through the proxy), the links and the similar instances.</summary>
    public static IResult Instance(int partyId, Guid instanceGuid, TokenChoice token, StorageClient.Answer answer, InstanceLinks links, IReadOnlyDictionary<int, string>? urns)
    {
        var html = Start($"Instance {partyId}/{instanceGuid}", null);
        Asked(html, token, answer);
        if (answer.Status != StatusCodes.Status200OK)
        {
            StorageError(html, answer);
            return Page(html, answer.Status);
        }

        using var document = JsonDocument.Parse(answer.Body);
        var instance = document.RootElement;
        var found = Links(instance, partyId, instanceGuid, links, urns).ToList();
        html.Append("<h2>Links</h2>");
        if (found.Count == 0)
        {
            html.Append("<p class=\"muted\">None: the app is not registered with the front door, and neither the inbox nor the receipt runs.</p>");
        }
        else
        {
            html.Append("<p class=\"links\" id=\"links\">");
            LinkList(html, found);
            html.Append("</p>");
        }

        html.Append("<h2>Fields</h2><table id=\"fields\"><tbody>");
        foreach (var (path, value) in Flatten(instance, string.Empty).Where(f => !f.Path.StartsWith("data", StringComparison.Ordinal) || f.Path.StartsWith("dataValues", StringComparison.Ordinal)))
        {
            html.Append("<tr><th>").Append(Encode(path)).Append("</th><td class=\"wrap\">").Append(Encode(value)).Append("</td></tr>");
        }

        html.Append("</tbody></table><h2>Data elements</h2>");
        if (!instance.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            html.Append("<p class=\"muted\">None.</p>");
        }
        else
        {
            html.Append("<table id=\"data\"><thead><tr><th>Id</th><th>Data type</th><th>File name</th><th>Content type</th><th>Size</th><th>Created</th><th>Last changed</th><th>Locked</th><th></th></tr></thead><tbody>");
            foreach (var element in data.EnumerateArray())
            {
                var id = Text(element, "id");
                html.Append("<tr><td><code>").Append(Encode(id)).Append("</code></td><td>").Append(Encode(Text(element, "dataType")))
                    .Append("</td><td>").Append(Encode(Text(element, "filename"))).Append("</td><td>").Append(Encode(Text(element, "contentType")))
                    .Append("</td><td>").Append(Encode(Text(element, "size"))).Append("</td><td>").Append(Encode(Text(element, "created")))
                    .Append("</td><td>").Append(Encode(Text(element, "lastChanged"))).Append("</td><td>").Append(Encode(Text(element, "locked"))).Append("</td><td>");
                if (Guid.TryParse(id, out var dataGuid))
                {
                    html.Append("<a href=\"").Append(Encode($"/Home/Instances/{partyId}/{instanceGuid}/data/{dataGuid}" + StorageQuery.QueryString(token.ToQuery())))
                        .Append("\">Download</a>");
                }

                html.Append("</td></tr>");
            }

            html.Append("</tbody></table>");
        }

        Similar(html, instance, partyId, token);
        RawJson(html, answer.Body);
        return Page(html, StatusCodes.Status200OK);
    }

    /// <summary>A failure before Storage answered: the token, or Storage itself.</summary>
    public static IResult Error(string title, TestTokens.Failure failure, IQueryCollection query)
    {
        var html = Start(title, query);
        html.Append("<p class=\"error\" id=\"error\">").Append(Encode(failure.Message)).Append("</p>");
        return Page(html, failure.Status);
    }

    /// <summary>The links of an instance, each only while it works: the owner's inbox, the dialog, the app or the receipt.</summary>
    private static IEnumerable<(string Label, string Url)> Links(JsonElement instance, int partyId, Guid instanceGuid, InstanceLinks links, IReadOnlyDictionary<int, string>? urns)
    {
        if (links.Arbeidsflate)
        {
            yield return ("Inbox", links.Inbox(urns?.GetValueOrDefault(partyId)));
        }

        if (links.Dialogs && InstanceLinks.DialogId(instance, instanceGuid) is { } dialogId)
        {
            yield return ("Dialog", links.Dialog(dialogId));
        }

        if (links.Running(Text(instance, "appId"), partyId, instanceGuid, Text(instance, "process.ended").Length > 0) is { } running)
        {
            yield return running;
        }
    }

    /// <summary>
    /// "Similar instances": the form filled in with the same owner, app, org, process step or status. A test user's query
    /// always names the owner and a service owner's the org, as Storage requires; links that come out the same are shown once.
    /// </summary>
    private static void Similar(StringBuilder html, JsonElement instance, int partyId, TokenChoice token)
    {
        var owner = partyId.ToString(CultureInfo.InvariantCulture);
        var appId = Text(instance, "appId");
        var org = Text(instance, "org");
        var ended = Text(instance, "process.ended").Length > 0;
        var step = ended ? ("process.endEvent", Text(instance, "process.endEvent")) : ("process.currentTask", Text(instance, "process.currentTask.elementId"));
        static string Flag(JsonElement instance, string path) => Text(instance, path) == "true" ? "true" : "false";
        (string Label, (string Name, string Value)[] Pairs)[] candidates =
        [
            ("Same owner", [("instanceOwner.partyId", owner)]),
            ("Same app", [("appId", appId)]),
            ("Same owner and app", [("appId", appId), ("instanceOwner.partyId", owner)]),
            ("Same org", [("org", org)]),
            (ended ? "Same app, same end event" : "Same app, same task", [("appId", appId), step]),
            ("Same app, same archived and deleted status",
            [
                ("appId", appId), ("status.isArchived", Flag(instance, "status.isArchived")),
                ("status.isSoftDeleted", Flag(instance, "status.isSoftDeleted")), ("status.isHardDeleted", Flag(instance, "status.isHardDeleted")),
            ]),
        ];

        html.Append("<h2>Similar instances</h2><p class=\"muted\">Each opens the form filled in.");
        if (token.Kind == TokenKind.User)
        {
            html.Append(" A test user's query always names the owner.");
        }
        else if (token.Kind == TokenKind.Org)
        {
            html.Append(" A service owner's query always names the org.");
        }

        html.Append("</p><ul id=\"similar\">");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (label, pairs) in candidates)
        {
            if (pairs.Any(p => p.Value.Length == 0))
            {
                continue;
            }

            var storage = pairs.ToList();
            if (token.Kind == TokenKind.User && storage.All(p => p.Name != "instanceOwner.partyId"))
            {
                storage.Add(("instanceOwner.partyId", owner));
            }
            else if (token.Kind == TokenKind.Org && storage.All(p => p.Name != "org") && org.Length > 0)
            {
                storage.Insert(0, ("org", org));
            }

            var storageQuery = StorageQuery.QueryString(storage.Select(p => new KeyValuePair<string, string>(p.Name, p.Value)));
            if (!seen.Add(storageQuery))
            {
                continue;
            }

            var href = "/Home/Instances" + StorageQuery.QueryString(token.ToQuery().Concat(storage.Select(p => new KeyValuePair<string, string>(p.Name, p.Value))));
            html.Append("<li><a href=\"").Append(Encode(href)).Append("\">").Append(Encode(label)).Append("</a> <code>")
                .Append(Encode(string.Join(" ", storage.Select(p => $"{p.Name}={p.Value}")))).Append("</code></li>");
        }

        html.Append("</ul>");
    }

    private static StringBuilder Start(string title, IQueryCollection? query)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>").Append(Encode(title)).Append("</title><style>").Append(Style).Append("</style></head><body>")
            .Append("<p><a href=\"/\">Altinn localtest</a> &rsaquo; <a href=\"/Home/Instances")
            .Append(query is null ? string.Empty : Encode(StorageQuery.QueryString(Pairs(query))))
            .Append("\" id=\"edit\">").Append(query is null ? "Storage instances" : "Edit this search").Append("</a></p><h1>").Append(Encode(title)).Append("</h1>");
        return html;
    }

    private static void Asked(StringBuilder html, TokenChoice token, StorageClient.Answer answer)
        => html.Append("<p class=\"muted\">As ").Append(Encode(token.Describe())).Append(": <code>").Append(Encode(answer.Request))
            .Append("</code> answered ").Append(answer.Status).Append(".</p>");

    private static void StorageError(StringBuilder html, StorageClient.Answer answer)
        => html.Append("<p class=\"error\" id=\"error\">Storage answered ").Append(answer.Status).Append(answer.Status == StatusCodes.Status401Unauthorized ? " (no or an invalid token)" : string.Empty)
            .Append(":</p><pre>").Append(Encode(answer.Body.Length == 0 ? "(no body)" : answer.Body)).Append("</pre>");

    private static void RawJson(StringBuilder html, string body)
    {
        string pretty;
        try
        {
            using var document = JsonDocument.Parse(body);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                document.WriteTo(writer);
            }

            pretty = Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            pretty = body;
        }

        html.Append("<details><summary>Storage's JSON</summary><pre>").Append(Encode(pretty)).Append("</pre></details>");
    }

    private static void LinkList(StringBuilder html, IEnumerable<(string Label, string Url)> links)
    {
        foreach (var (label, url) in links)
        {
            html.Append("<a href=\"").Append(Encode(url)).Append("\">").Append(Encode(label)).Append("</a>");
        }
    }

    private static IResult Page(StringBuilder html, int status)
    {
        html.Append("</body></html>");
        return Results.Content(html.ToString(), "text/html; charset=utf-8", Encoding.UTF8, status);
    }

    /// <summary>The instance's page with the same token.</summary>
    private static string InstancePath(int partyId, Guid instanceGuid, TokenChoice token)
        => $"/Home/Instances/{partyId}/{instanceGuid}" + StorageQuery.QueryString(token.ToQuery());

    /// <summary>Storage's instance id, <c>{partyId}/{instanceGuid}</c>.</summary>
    private static bool Id(JsonElement instance, out int partyId, out Guid instanceGuid)
    {
        partyId = 0;
        instanceGuid = Guid.Empty;
        var parts = Text(instance, "id").Split('/');
        return parts.Length == 2 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out partyId) && Guid.TryParse(parts[1], out instanceGuid);
    }

    private static string ProcessSummary(JsonElement instance)
    {
        if (Text(instance, "process.ended") is { Length: > 0 } ended)
        {
            return $"ended {ended}" + (Text(instance, "process.endEvent") is { Length: > 0 } endEvent ? $" ({endEvent})" : string.Empty);
        }

        return Text(instance, "process.currentTask.elementId") is { Length: > 0 } task
            ? task + (Text(instance, "process.currentTask.name") is { Length: > 0 } name ? $" ({name})" : string.Empty)
            : "not started";
    }

    private static string StatusBadges(JsonElement instance)
    {
        var badges = new StringBuilder();
        foreach (var (path, label) in new[] { ("status.isArchived", "archived"), ("status.isSoftDeleted", "soft-deleted"), ("status.isHardDeleted", "hard-deleted") })
        {
            if (Text(instance, path) == "true")
            {
                badges.Append("<span class=\"badge\">").Append(label).Append("</span>");
            }
        }

        return badges.Length == 0 ? "<span class=\"muted\">active</span>" : badges.ToString();
    }

    /// <summary>A scalar at a dotted path as text (<c>true</c>/<c>false</c> for booleans), or empty.</summary>
    private static string Text(JsonElement element, string path)
    {
        var current = element;
        foreach (var name in path.Split('.'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => current.GetRawText(),
            _ => string.Empty,
        };
    }

    /// <summary>Every scalar of the instance by its path (<c>process.currentTask.elementId</c>, <c>completeConfirmations[0].stakeHolderId</c>).</summary>
    private static IEnumerable<(string Path, string Value)> Flatten(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    foreach (var field in Flatten(property.Value, path.Length == 0 ? property.Name : $"{path}.{property.Name}"))
                    {
                        yield return field;
                    }
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    foreach (var field in Flatten(item, $"{path}[{index++}]"))
                    {
                        yield return field;
                    }
                }

                break;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;
            default:
                yield return (path, element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.GetRawText());
                break;
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> Pairs(IQueryCollection query)
        => query.SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string>(q.Key, v ?? string.Empty)));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
