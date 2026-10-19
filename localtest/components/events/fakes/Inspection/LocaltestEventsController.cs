#nullable enable
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Localtest.Changes;
using Altinn.Platform.Events.Localtest.Startup;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Platform.Events.Localtest.Inspection;

/// <summary>
/// Localtest inspection API for the GUI and for tests: everything that was published, what happened to it, and every
/// webhook POST, without the production read endpoints' tokens, PDP filtering and 30-second delay. Anonymous, like
/// <c>register/api/localtest/</c>. Reads the production tables (<c>events.events</c>, <c>events.trace_log</c>,
/// <c>events.subscription</c>) and <c>localtest.delivery</c> with plain SQL; <c>DELETE events</c> resets.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("events/api/localtest")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class LocaltestEventsController(
    NpgsqlDataSource dataSource,
    LocaltestAdminDatabase adminDatabase,
    LocaltestEventsStream stream,
    LocaltestChanges changes,
    IMemoryCache memoryCache) : ControllerBase
{
    private static readonly JsonSerializerOptions StreamJson = new(JsonSerializerDefaults.Web);

    private const string DeliveryCountsJoin = @"
        left join lateral (
            select count(*) as total,
                   count(*) filter (where d.mode = 'delivered' and d.response_status between 200 and 299) as delivered,
                   count(*) filter (where d.mode = 'captured') as captured,
                   count(*) filter (where d.mode = 'delivered' and (d.response_status is null or d.response_status not between 200 and 299)) as failed
            from localtest.delivery d where d.event_id = e.cloudevent->>'id') dc on true";

    private const string EventColumns = @"e.sequenceno, e.cloudevent::text, e.registeredtime, e.idempotencykey, e.status, e.retrycount, e.retryreason,
        dc.total, dc.delivered, dc.captured, dc.failed";

    /// <summary>Published events in registration order. <c>next</c> is the cursor for the following page.</summary>
    [HttpGet("events")]
    public async Task<ActionResult<object>> GetEvents(
        [FromQuery] long? after,
        [FromQuery] int limit = 100,
        [FromQuery] string? org = null,
        [FromQuery] string? app = null,
        [FromQuery] string? type = null,
        [FromQuery] string? subject = null,
        [FromQuery] string? alternativeSubject = null,
        [FromQuery] string? resource = null,
        [FromQuery] string? instance = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 1000);
        short? wantedStatus = null;
        if (status is not null)
        {
            wantedStatus = status.ToLowerInvariant() switch
            {
                "registered" => 1,
                "processed" => 2,
                "retryexhausted" => 3,
                _ => null,
            };
            if (wantedStatus is null)
            {
                return BadRequest($"Unknown status '{status}'; use registered, processed or retryExhausted.");
            }
        }

        // App events carry urn:altinn:resource:app_{org}_{app}; org codes have no underscore.
        string? resourcePattern = (org, app) switch
        {
            (null, null) => null,
            (not null, not null) => $"urn:altinn:resource:app\\_{org}\\_{app}",
            (not null, null) => $"urn:altinn:resource:app\\_{org}\\_%",
            (null, not null) => $"urn:altinn:resource:app\\_%\\_{app}",
        };

        await using var command = dataSource.CreateCommand($@"
            select {EventColumns}
            from events.events e {DeliveryCountsJoin}
            where (@after::bigint is null or e.sequenceno > @after)
              and (@resource::text is null or e.cloudevent->>'resource' = @resource)
              and (@resourcepattern::text is null or e.cloudevent->>'resource' like @resourcepattern)
              and (@type::text is null or e.cloudevent->>'type' = @type)
              and (@subject::text is null or e.cloudevent->>'subject' = @subject)
              and (@alternativesubject::text is null or e.cloudevent->>'alternativesubject' = @alternativesubject)
              and (@instance::text is null or e.cloudevent->>'source' like '%/instances/' || @instance)
              and (@status::smallint is null or e.status = @status)
            order by e.sequenceno
            limit @limit");
        Add(command, "after", NpgsqlDbType.Bigint, after);
        Add(command, "resource", NpgsqlDbType.Text, resource);
        Add(command, "resourcepattern", NpgsqlDbType.Text, resourcePattern);
        Add(command, "type", NpgsqlDbType.Text, type);
        Add(command, "subject", NpgsqlDbType.Text, subject);
        Add(command, "alternativesubject", NpgsqlDbType.Text, alternativeSubject);
        Add(command, "instance", NpgsqlDbType.Text, instance);
        Add(command, "status", NpgsqlDbType.Smallint, wantedStatus);
        Add(command, "limit", NpgsqlDbType.Integer, limit + 1);

        var rows = await ReadEvents(command, cancellationToken);
        bool more = rows.Count > limit;
        if (more)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        return new { items = rows, next = more ? rows[^1].SequenceNo : (long?)null };
    }

    /// <summary>One event: the stored CloudEvent, its status, its trace log and its webhook deliveries.</summary>
    [HttpGet("events/{id}")]
    public async Task<ActionResult<object>> GetEvent(string id, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand($@"
            select {EventColumns}
            from events.events e {DeliveryCountsJoin}
            where e.cloudevent->>'id' = @id
            order by e.sequenceno");
        Add(command, "id", NpgsqlDbType.Text, id);
        var rows = await ReadEvents(command, cancellationToken);
        if (rows.Count == 0)
        {
            return NotFound();
        }

        // Ids are unique per source only; normally there is exactly one.
        return new
        {
            items = rows,
            traceLog = Guid.TryParse(id, out var guid) ? await QueryTraceLog(null, guid, 1000, cancellationToken) : [],
            deliveries = await QueryDeliveries(null, id, null, null, 1000, cancellationToken),
        };
    }

    /// <summary>Webhook POSTs, event deliveries and subscription validations alike, oldest first.</summary>
    [HttpGet("deliveries")]
    public Task<List<object>> GetDeliveries(
        [FromQuery] long? after,
        [FromQuery] int limit = 100,
        [FromQuery] string? eventId = null,
        [FromQuery] string? endpoint = null,
        [FromQuery] string? mode = null,
        CancellationToken cancellationToken = default)
        => QueryDeliveries(after, eventId, endpoint, mode, Math.Clamp(limit, 1, 1000), cancellationToken);

    /// <summary>Every subscription, validated or not.</summary>
    [HttpGet("subscriptions")]
    public async Task<List<object>> GetSubscriptions([FromQuery] string? consumer = null, CancellationToken cancellationToken = default)
    {
        await using var command = dataSource.CreateCommand(@"
            select id, endpointurl, sourcefilter, resourcefilter, subjectfilter, typefilter, consumer, createdby, ""time"", validated, includesubunits
            from events.subscription
            where (@consumer::text is null or consumer = @consumer)
            order by id");
        Add(command, "consumer", NpgsqlDbType.Text, consumer);
        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                id = reader.GetInt64(0),
                endPoint = reader.GetString(1),
                sourceFilter = Nullable<string>(reader, 2),
                resourceFilter = reader.GetString(3),
                subjectFilter = Nullable<string>(reader, 4),
                typeFilter = Nullable<string>(reader, 5),
                consumer = reader.GetString(6),
                createdBy = reader.GetString(7),
                created = reader.GetFieldValue<DateTimeOffset>(8),
                validated = reader.GetBoolean(9),
                includeSubunits = reader.GetBoolean(10),
            });
        }

        return result;
    }

    /// <summary>The production trace log rows (Registered, OutboundQueue, Unauthorized, WebhookPostResponse, …).</summary>
    [HttpGet("tracelog")]
    public async Task<ActionResult<List<object>>> GetTraceLog(
        [FromQuery] long? after, [FromQuery] string? eventId = null, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        Guid? id = null;
        if (eventId is not null)
        {
            if (!Guid.TryParse(eventId, out var parsed))
            {
                return new List<object>();
            }

            id = parsed;
        }

        return await QueryTraceLog(after, id, Math.Clamp(limit, 1, 1000), cancellationToken);
    }

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
    public async Task<IActionResult> Reset([FromQuery] bool subscriptions = false, CancellationToken cancellationToken = default)
    {
        // As the table owner: TRUNCATE takes a lock that waits for the claim loop's open transaction, if any.
        await adminDatabase.Execute(
            subscriptions
                ? "TRUNCATE events.events, events.trace_log, events.subscription, localtest.delivery"
                : "TRUNCATE events.events, events.trace_log, localtest.delivery",
            cancellationToken);
        if (memoryCache is MemoryCache cache)
        {
            // Subscription lookups, main units and PDP decisions.
            cache.Clear();
        }

        changes.Raise("all", "reset", null);
        return NoContent();
    }

    private async Task<List<object>> QueryTraceLog(long? after, Guid? eventId, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(@"
            select sequenceno, ""time"", cloudeventid, resource, eventtype, consumer, subscriptionid, responsecode, subscriberendpoint, activity
            from events.trace_log
            where (@after::bigint is null or sequenceno > @after)
              and (@eventid::uuid is null or cloudeventid = @eventid)
            order by sequenceno
            limit @limit");
        Add(command, "after", NpgsqlDbType.Bigint, after);
        Add(command, "eventid", NpgsqlDbType.Uuid, eventId);
        Add(command, "limit", NpgsqlDbType.Integer, limit);
        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                id = reader.GetInt64(0),
                time = reader.GetFieldValue<DateTimeOffset>(1),
                cloudEventId = reader.GetGuid(2),
                resource = Nullable<string>(reader, 3),
                eventType = Nullable<string>(reader, 4),
                consumer = Nullable<string>(reader, 5),
                subscriptionId = reader.IsDBNull(6) ? (long?)null : reader.GetInt64(6),
                responseCode = reader.IsDBNull(7) ? (int?)null : reader.GetInt32(7),
                subscriberEndpoint = Nullable<string>(reader, 8),
                activity = Nullable<string>(reader, 9),
            });
        }

        return result;
    }

    private async Task<List<object>> QueryDeliveries(long? after, string? eventId, string? endpoint, string? mode, int limit, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(@"
            select id, ""time"", event_id, event_type, kind, endpoint, mode, request_headers::text, request_body, response_status, response_body, duration_ms, error
            from localtest.delivery
            where (@after::bigint is null or id > @after)
              and (@eventid::text is null or event_id = @eventid)
              and (@endpoint::text is null or endpoint ilike @endpoint || '%')
              and (@mode::text is null or mode = @mode)
            order by id
            limit @limit");
        Add(command, "after", NpgsqlDbType.Bigint, after);
        Add(command, "eventid", NpgsqlDbType.Text, eventId);
        Add(command, "endpoint", NpgsqlDbType.Text, endpoint);
        Add(command, "mode", NpgsqlDbType.Text, mode);
        Add(command, "limit", NpgsqlDbType.Integer, limit);
        var result = new List<object>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new
            {
                id = reader.GetInt64(0),
                time = reader.GetFieldValue<DateTimeOffset>(1),
                eventId = Nullable<string>(reader, 2),
                eventType = Nullable<string>(reader, 3),
                kind = reader.GetString(4),
                endpoint = reader.GetString(5),
                mode = reader.GetString(6),
                requestHeaders = JsonNode.Parse(reader.GetString(7)),
                requestBody = reader.GetString(8),
                responseStatus = reader.IsDBNull(9) ? (int?)null : reader.GetInt32(9),
                responseBody = Nullable<string>(reader, 10),
                durationMs = reader.GetInt64(11),
                error = Nullable<string>(reader, 12),
            });
        }

        return result;
    }

    private static async Task<List<EventSummary>> ReadEvents(NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var result = new List<EventSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var cloudEvent = JsonNode.Parse(reader.GetString(1))!.AsObject();
            string? Attribute(string name) => cloudEvent[name]?.ToString();
            string? resource = Attribute("resource");
            string? source = Attribute("source");
            var (org, app) = OrgAndApp(resource);
            result.Add(new EventSummary(
                reader.GetInt64(0),
                Attribute("id"),
                Attribute("time"),
                reader.GetFieldValue<DateTimeOffset>(2),
                Attribute("type"),
                source,
                Attribute("subject"),
                Attribute("alternativesubject"),
                resource,
                Attribute("resourceinstance"),
                org,
                app,
                Instance(source),
                reader.IsDBNull(4) ? null : reader.GetInt16(4) switch { 1 => "registered", 2 => "processed", 3 => "retryExhausted", var other => other.ToString() },
                reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Nullable<string>(reader, 6),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                new DeliveryCounts(reader.GetInt64(7), reader.GetInt64(8), reader.GetInt64(9), reader.GetInt64(10)),
                cloudEvent));
        }

        return result;
    }

    private static void Add(NpgsqlCommand command, string name, NpgsqlDbType type, object? value)
        => command.Parameters.Add(new NpgsqlParameter(name, type) { Value = value ?? DBNull.Value });

    private static T? Nullable<T>(NpgsqlDataReader reader, int ordinal) where T : class
        => reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<T>(ordinal);

    private static (string? Org, string? App) OrgAndApp(string? resource)
    {
        const string prefix = "urn:altinn:resource:app_";
        if (resource is null || !resource.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        var rest = resource[prefix.Length..];
        int split = rest.IndexOf('_');
        return split <= 0 ? (null, null) : (rest[..split], rest[(split + 1)..]);
    }

    /// <summary><c>{partyId}/{instanceGuid}</c> from an app event's source (<c>…/{org}/{app}/instances/{partyId}/{guid}</c>).</summary>
    private static string? Instance(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        int index = Array.IndexOf(segments, "instances");
        return index >= 0 && index + 2 < segments.Length ? $"{segments[index + 1]}/{segments[index + 2]}" : null;
    }

    public sealed record DeliveryCounts(long Total, long Delivered, long Captured, long Failed);

    public sealed record EventSummary(
        long SequenceNo,
        string? Id,
        string? Time,
        DateTimeOffset RegisteredTime,
        string? Type,
        string? Source,
        string? Subject,
        string? AlternativeSubject,
        string? Resource,
        string? ResourceInstance,
        string? Org,
        string? App,
        string? Instance,
        string? Status,
        int RetryCount,
        string? RetryReason,
        Guid? IdempotencyKey,
        DeliveryCounts Deliveries,
        JsonObject CloudEvent);
}
