using System.Globalization;
using System.Text.Json.Nodes;

namespace Localtest.Emulated;

/// <summary>
/// Events in minimal mode, under <c>events/api/</c>: <c>POST v1/app</c> appends the cloud event (with an id and a time
/// when it has none) to a bounded list, <c>POST v1/subscriptions</c> echoes the subscription with an id (nothing is
/// delivered), and the Events component's inspection paths read them back: <c>GET/DELETE localtest/events</c>,
/// <c>localtest/events/{id}</c>, <c>localtest/subscriptions</c>, and <c>localtest/stream</c> (a server-sent event
/// stream that only keeps the connection alive in phase 1).
/// </summary>
public static class EventsEmulation
{
    public const string Service = "events";

    public static void Map(WebApplication app, string prefix)
    {
        var v1 = app.MapGroup(prefix + "/api/v1");

        v1.MapPost("/app", async (HttpRequest request, EventLog log) =>
        {
            if (await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted) is not JsonObject cloudEvent)
            {
                return EmulationResults.Problem(StatusCodes.Status400BadRequest, "Not a cloud event", "The body must be a JSON object.", Service);
            }

            return Results.Text(log.Add(cloudEvent));
        });

        v1.MapPost("/subscriptions", async (HttpRequest request, EventLog log) =>
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted) as JsonObject ?? [];
            string? Field(string name) => body.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value?.ToString();
            var subscription = log.Subscribe(Field("endPoint"), Field("sourceFilter"), Field("subjectFilter"), Field("alternativeSubjectFilter"), Field("typeFilter"));
            return Results.Text(subscription.ToJsonString(), "application/json", statusCode: StatusCodes.Status201Created);
        });

        var localtest = app.MapGroup(prefix + "/api/localtest");

        localtest.MapGet("/events", (long? after, int? limit, string? org, string? app, string? type, string? subject, EventLog log) =>
        {
            var take = Math.Clamp(limit ?? 100, 1, 1000);
            var rows = log.Events
                .Where(e => after is null || e.SequenceNo > after)
                .Where(e => org is null || e.Org == org)
                .Where(e => app is null || e.App == app)
                .Where(e => type is null || e.Type == type)
                .Where(e => subject is null || e.Subject == subject)
                .Take(take + 1)
                .ToList();
            var more = rows.Count > take;
            var items = new JsonArray([.. rows.Take(take).Select(e => (JsonNode)e.ToJson())]);
            return Results.Text(new JsonObject { ["items"] = items, ["next"] = more ? rows[take - 1].SequenceNo : null }.ToJsonString(), "application/json");
        });

        localtest.MapGet("/events/{id}", (string id, EventLog log) =>
        {
            var rows = log.Events.Where(e => e.Id == id).ToList();
            return rows.Count == 0
                ? Results.NotFound()
                : Results.Text(new JsonObject { ["items"] = new JsonArray([.. rows.Select(e => (JsonNode)e.ToJson())]), ["traceLog"] = new JsonArray(), ["deliveries"] = new JsonArray() }.ToJsonString(), "application/json");
        });

        localtest.MapDelete("/events", (bool? subscriptions, EventLog log) =>
        {
            log.Clear(subscriptions ?? false);
            return Results.NoContent();
        });

        localtest.MapGet("/subscriptions", (EventLog log) => Results.Text(new JsonArray([.. log.Subscriptions.Select(s => s.DeepClone())]).ToJsonString(), "application/json"));

        localtest.MapGet("/stream", async (HttpContext context) =>
        {
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            try
            {
                while (!context.RequestAborted.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), context.RequestAborted);
                    await context.Response.WriteAsync(": keep-alive\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // The client went away.
            }
        });
    }
}

/// <summary>The published events (the last 1,000) and the echoed subscriptions, in memory.</summary>
public sealed class EventLog
{
    public const int Capacity = 1000;

    private readonly Lock _lock = new();
    private readonly LinkedList<StoredEvent> _events = new();
    private readonly List<JsonObject> _subscriptions = [];
    private long _sequence;
    private int _subscriptionId;

    public IReadOnlyList<StoredEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<JsonObject> Subscriptions
    {
        get
        {
            lock (_lock)
            {
                return [.. _subscriptions];
            }
        }
    }

    /// <summary>Stores the event and answers its id (given, or a new one).</summary>
    public string Add(JsonObject cloudEvent)
    {
        var now = DateTimeOffset.UtcNow;
        if (string.IsNullOrEmpty(cloudEvent["id"]?.ToString()))
        {
            cloudEvent["id"] = Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(cloudEvent["time"]?.ToString()))
        {
            cloudEvent["time"] = now.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        lock (_lock)
        {
            _events.AddLast(new StoredEvent(++_sequence, now, cloudEvent));
            while (_events.Count > Capacity)
            {
                _events.RemoveFirst();
            }
        }

        return cloudEvent["id"]!.ToString();
    }

    public JsonObject Subscribe(string? endPoint, string? sourceFilter, string? subjectFilter, string? alternativeSubjectFilter, string? typeFilter)
    {
        lock (_lock)
        {
            var app = sourceFilter is { } source && source.Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: >= 2 } parts ? $"/app/{parts[^2]}/{parts[^1]}" : null;
            var subscription = new JsonObject
            {
                ["id"] = ++_subscriptionId,
                ["endPoint"] = endPoint,
                ["sourceFilter"] = sourceFilter,
                ["subjectFilter"] = subjectFilter,
                ["alternativeSubjectFilter"] = alternativeSubjectFilter,
                ["typeFilter"] = typeFilter,
                ["consumer"] = app,
                ["createdBy"] = app,
                ["created"] = DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            };
            _subscriptions.Add(subscription);
            return (JsonObject)subscription.DeepClone();
        }
    }

    public void Clear(bool subscriptions)
    {
        lock (_lock)
        {
            _events.Clear();
            if (subscriptions)
            {
                _subscriptions.Clear();
            }
        }
    }
}

/// <summary>One published event and the fields the inspection API filters on.</summary>
public sealed record StoredEvent(long SequenceNo, DateTimeOffset RegisteredTime, JsonObject CloudEvent)
{
    public string? Id => CloudEvent["id"]?.ToString();

    public string? Type => CloudEvent["type"]?.ToString();

    public string? Source => CloudEvent["source"]?.ToString();

    public string? Subject => CloudEvent["subject"]?.ToString();

    /// <summary>The org and app of an app event's source (<c>…/{org}/{app}/instances/{party}/{guid}</c>).</summary>
    public string? Org => AppPart(0);

    public string? App => AppPart(1);

    public JsonObject ToJson() => new()
    {
        ["sequenceNo"] = SequenceNo,
        ["id"] = Id,
        ["time"] = CloudEvent["time"]?.ToString(),
        ["registeredTime"] = RegisteredTime.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
        ["type"] = Type,
        ["source"] = Source,
        ["subject"] = Subject,
        ["alternativeSubject"] = CloudEvent["alternativesubject"]?.ToString(),
        ["org"] = Org,
        ["app"] = App,
        ["instance"] = Source is { } source && source.IndexOf("/instances/", StringComparison.Ordinal) is var at and >= 0 ? source[(at + "/instances/".Length)..] : null,
        ["status"] = "registered",
        ["cloudEvent"] = CloudEvent.DeepClone(),
    };

    private string? AppPart(int index)
    {
        if (Source is not { } source || source.IndexOf("/instances/", StringComparison.Ordinal) is not (var at and >= 0))
        {
            return null;
        }

        var parts = source[..at].Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[parts.Length - 2 + index] : null;
    }
}
