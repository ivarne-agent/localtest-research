#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Localtest.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Altinn.Platform.Events.Localtest.Inspection;

/// <summary>
/// Localtest inspection API for the GUI and for tests: everything that was published, what happened to it, and every
/// webhook POST, without the production read endpoints' tokens, PDP filtering and 30-second delay. Anonymous, like
/// <c>register/api/localtest/</c>. Reads only; <c>DELETE events</c> resets.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("events/api/localtest")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class LocaltestEventsController(LocaltestEventsStore store, LocaltestEventsStream stream, IMemoryCache memoryCache) : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    /// <summary>Published events in registration order. <c>next</c> is the cursor for the following page.</summary>
    [HttpGet("events")]
    public ActionResult<object> GetEvents(
        [FromQuery] long? after,
        [FromQuery] int limit = 100,
        [FromQuery] string? org = null,
        [FromQuery] string? app = null,
        [FromQuery] string? type = null,
        [FromQuery] string? subject = null,
        [FromQuery] string? alternativeSubject = null,
        [FromQuery] string? resource = null,
        [FromQuery] string? instance = null,
        [FromQuery] string? status = null)
    {
        limit = Math.Clamp(limit, 1, 1000);
        EventStatus? wantedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<EventStatus>(status, ignoreCase: true, out var parsed))
            {
                return BadRequest($"Unknown status '{status}'; use registered, processed or retryExhausted.");
            }

            wantedStatus = parsed;
        }

        var rows = store.QueryEvents(events => events
            .Where(e => after is null || e.SequenceNo > after)
            .Where(e => org is null || OrgAndApp(e).Org == org)
            .Where(e => app is null || OrgAndApp(e).App == app)
            .Where(e => type is null || e.Type == type)
            .Where(e => subject is null || e.Subject == subject)
            .Where(e => alternativeSubject is null || e.AlternativeSubject == alternativeSubject)
            .Where(e => resource is null || e.Resource == resource)
            .Where(e => instance is null || Instance(e) == instance)
            .Where(e => wantedStatus is null || e.Status == wantedStatus)
            .OrderBy(e => e.SequenceNo)
            .Take(limit + 1));

        var page = rows.Take(limit).ToList();
        var deliveries = store.QueryDeliveries(d => d.EventId is not null && page.Any(e => e.Id == d.EventId));
        return new
        {
            items = page.Select(e => Summary(e, deliveries.Where(d => d.EventId == e.Id))),
            next = rows.Count > limit ? page[^1].SequenceNo : (long?)null,
        };
    }

    /// <summary>One event: the stored CloudEvent, its status, its trace log and its webhook deliveries.</summary>
    [HttpGet("events/{id}")]
    public ActionResult<object> GetEvent(string id)
    {
        var matches = store.QueryEvents(events => events.Where(e => e.Id == id));
        if (matches.Count == 0)
        {
            return NotFound();
        }

        var traceLog = store.QueryTraceLog(t => t.CloudEventId?.ToString() == id).OrderBy(t => t.Id);
        var deliveries = store.QueryDeliveries(d => d.EventId == id).OrderBy(d => d.Id).ToList();

        // Ids are unique per source only; normally there is exactly one.
        return new
        {
            items = matches.Select(e => new
            {
                summary = Summary(e, deliveries),
                cloudEvent = JsonNode.Parse(e.CloudEvent),
            }),
            traceLog,
            deliveries,
        };
    }

    /// <summary>Webhook POSTs, event deliveries and subscription validations alike, oldest first.</summary>
    [HttpGet("deliveries")]
    public IEnumerable<StoredDelivery> GetDeliveries(
        [FromQuery] long? after,
        [FromQuery] int limit = 100,
        [FromQuery] string? eventId = null,
        [FromQuery] string? endpoint = null,
        [FromQuery] string? mode = null)
        => store.QueryDeliveries(d =>
                (after is null || d.Id > after)
                && (eventId is null || d.EventId == eventId)
                && (endpoint is null || d.Endpoint.StartsWith(endpoint, StringComparison.OrdinalIgnoreCase))
                && (mode is null || d.Mode == mode))
            .OrderBy(d => d.Id)
            .Take(Math.Clamp(limit, 1, 1000));

    /// <summary>Every subscription, validated or not.</summary>
    [HttpGet("subscriptions")]
    public IEnumerable<object> GetSubscriptions([FromQuery] string? consumer = null)
        => store.QuerySubscriptions(s => consumer is null || s.Consumer == consumer)
            .Select(s => new
            {
                s.Id,
                endPoint = s.EndPoint?.ToString(),
                sourceFilter = s.SourceFilter?.ToString(),
                s.ResourceFilter,
                s.SubjectFilter,
                s.TypeFilter,
                s.Consumer,
                s.CreatedBy,
                s.Created,
                s.Validated,
                s.IncludeSubunits,
            });

    /// <summary>The production trace log rows (Registered, OutboundQueue, Unauthorized, WebhookPostResponse, …).</summary>
    [HttpGet("tracelog")]
    public IEnumerable<StoredTraceLog> GetTraceLog([FromQuery] long? after, [FromQuery] string? eventId = null, [FromQuery] int limit = 100)
        => store.QueryTraceLog(t => (after is null || t.Id > after) && (eventId is null || t.CloudEventId?.ToString() == eventId))
            .OrderBy(t => t.Id)
            .Take(Math.Clamp(limit, 1, 1000));

    /// <summary>
    /// Server-sent events, one <c>change</c> message per change:
    /// <c>{ component: "events", entity: event|subscription|trace|delivery|all, kind: created|updated|deleted|reset, id, at }</c>.
    /// The client refetches what it shows; nothing polls.
    /// </summary>
    [HttpGet("stream")]
    public async Task Stream(CancellationToken cancellationToken)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var (id, reader) = stream.Listen();
        try
        {
            await Response.WriteAsync(": connected\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                heartbeat.CancelAfter(TimeSpan.FromSeconds(15));
                try
                {
                    var change = await reader.ReadAsync(heartbeat.Token);
                    await Response.WriteAsync($"event: change\ndata: {JsonSerializer.Serialize(change, StreamJson)}\n\n", cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Keeps proxies from closing an idle connection.
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                }

                await Response.Body.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The browser went away.
        }
        finally
        {
            stream.Stop(id);
        }
    }

    /// <summary>Forgets every event, trace log row and delivery (and subscriptions with <c>subscriptions=true</c>), and empties the caches.</summary>
    [HttpDelete("events")]
    public IActionResult Reset([FromQuery] bool subscriptions = false)
    {
        store.Reset(subscriptions);
        if (memoryCache is MemoryCache cache)
        {
            // Subscription lookups, main units and PDP decisions.
            cache.Clear();
        }

        return NoContent();
    }

    private static object Summary(StoredEvent e, IEnumerable<StoredDelivery> deliveries)
    {
        var (org, app) = OrgAndApp(e);
        var list = deliveries.ToList();
        return new
        {
            e.SequenceNo,
            e.Id,
            e.Time,
            e.RegisteredTime,
            e.Type,
            e.Source,
            e.Subject,
            e.AlternativeSubject,
            e.Resource,
            e.ResourceInstance,
            org,
            app,
            instance = Instance(e),
            status = JsonNamingPolicy.CamelCase.ConvertName(e.Status.ToString()),
            e.RetryCount,
            e.RetryReason,
            e.IdempotencyKey,
            deliveries = new
            {
                total = list.Count,
                delivered = list.Count(d => d.Mode == "delivered" && d.ResponseStatus is >= 200 and < 300),
                captured = list.Count(d => d.Mode == "captured"),
                failed = list.Count(d => d.Mode == "delivered" && d.ResponseStatus is not (>= 200 and < 300)),
            },
        };
    }

    /// <summary>App events carry <c>urn:altinn:resource:app_{org}_{app}</c>; org codes have no underscore.</summary>
    private static (string? Org, string? App) OrgAndApp(StoredEvent e)
    {
        const string prefix = "urn:altinn:resource:app_";
        if (e.Resource is null || !e.Resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var rest = e.Resource[prefix.Length..];
        int split = rest.IndexOf('_');
        return split <= 0 ? (null, null) : (rest[..split], rest[(split + 1)..]);
    }

    /// <summary><c>{partyId}/{instanceGuid}</c> from an app event's source (<c>…/{org}/{app}/instances/{partyId}/{guid}</c>).</summary>
    private static string? Instance(StoredEvent e)
    {
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var source))
        {
            return null;
        }

        var segments = source.AbsolutePath.Trim('/').Split('/');
        int index = Array.IndexOf(segments, "instances");
        return index >= 0 && index + 2 < segments.Length ? $"{segments[index + 1]}/{segments[index + 2]}" : null;
    }
}
