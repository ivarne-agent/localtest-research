#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Altinn.Platform.Events.Models;

namespace Altinn.Platform.Events.Localtest.Persistence;

/// <summary>
/// The in-memory replacement for the <c>eventsdb</c> database: the rows of <c>events.events</c>,
/// <c>events.subscription</c> and <c>events.trace_log</c>, plus the webhook deliveries the capture handler records.
/// The repositories in this folder translate the production SQL onto it; everything is lost when the container stops.
/// One lock guards all of it, which is plenty for one developer's traffic.
/// </summary>
public sealed class LocaltestEventsStore
{
    private readonly object _gate = new();
    private readonly List<StoredEvent> _events = [];
    private readonly Dictionary<int, Subscription> _subscriptions = [];
    private readonly List<StoredTraceLog> _traceLog = [];
    private readonly List<StoredDelivery> _deliveries = [];
    private readonly HashSet<long> _claimed = [];
    private long _nextSequenceNo = 1;
    private int _nextSubscriptionId = 1;
    private long _nextTraceLogId = 1;
    private long _nextDeliveryId = 1;

    /// <summary>Raised after every change, outside the lock. Feeds the inspection stream.</summary>
    public event Action<LocaltestChange>? Changed;

    // events.events

    /// <summary><c>insert … ON CONFLICT DO NOTHING RETURNING sequenceno</c>: unique on (id, source) and on the idempotency key.</summary>
    public StoredEvent? InsertEvent(string cloudEventJson, Guid? idempotencyKey)
    {
        var stored = StoredEvent.Parse(cloudEventJson, idempotencyKey);
        lock (_gate)
        {
            if (_events.Any(e => (e.Id == stored.Id && e.Source == stored.Source)
                || (idempotencyKey is not null && e.IdempotencyKey == idempotencyKey)))
            {
                return null;
            }

            stored.SequenceNo = _nextSequenceNo++;
            stored.RegisteredTime = DateTimeOffset.UtcNow;
            stored.Status = EventStatus.Registered;
            _events.Add(stored);
        }

        Raise("event", "created", stored.Id);
        return stored;
    }

    public List<StoredEvent> QueryEvents(Func<IReadOnlyList<StoredEvent>, IEnumerable<StoredEvent>> query)
    {
        lock (_gate)
        {
            return query(_events).Select(e => e.Copy()).ToList();
        }
    }

    /// <summary><c>events.claim_registered_event()</c>: the oldest Registered row not locked by another unit of work.</summary>
    public StoredEvent? ClaimRegisteredEvent()
    {
        lock (_gate)
        {
            var next = _events.FirstOrDefault(e => e.Status == EventStatus.Registered && !_claimed.Contains(e.SequenceNo));
            if (next is null)
            {
                return null;
            }

            _claimed.Add(next.SequenceNo);
            return next.Copy();
        }
    }

    public void ReleaseClaim(long sequenceNo)
    {
        lock (_gate)
        {
            _claimed.Remove(sequenceNo);
        }
    }

    public void MarkEventProcessed(long sequenceNo)
    {
        string? id = UpdateEvent(sequenceNo, e => e.Status = EventStatus.Processed);
        if (id is not null)
        {
            Raise("event", "updated", id);
        }
    }

    /// <summary>The production retry update: counts the attempt and ends in RetryExhausted at <paramref name="maxRetryCount"/>.</summary>
    public void MarkEventRetry(long sequenceNo, string? retryReason, int maxRetryCount)
    {
        string? id = UpdateEvent(sequenceNo, e =>
        {
            if (e.Status != EventStatus.Registered)
            {
                return;
            }

            e.RetryCount++;
            e.LastRetried = DateTimeOffset.UtcNow;
            e.RetryReason = string.IsNullOrWhiteSpace(retryReason) ? null : retryReason;
            if (e.RetryCount >= maxRetryCount)
            {
                e.Status = EventStatus.RetryExhausted;
            }
        });
        if (id is not null)
        {
            Raise("event", "updated", id);
        }
    }

    private string? UpdateEvent(long sequenceNo, Action<StoredEvent> update)
    {
        lock (_gate)
        {
            var stored = _events.FirstOrDefault(e => e.SequenceNo == sequenceNo);
            if (stored is null)
            {
                return null;
            }

            update(stored);
            return stored.Id;
        }
    }

    // events.subscription

    public Subscription InsertSubscription(Subscription subscription)
    {
        Subscription stored;
        lock (_gate)
        {
            stored = CopySubscription(subscription);
            stored.Id = _nextSubscriptionId++;
            stored.Created = DateTime.UtcNow;
            stored.Validated = false;
            _subscriptions[stored.Id] = stored;
            stored = CopySubscription(stored);
        }

        Raise("subscription", "created", stored.Id.ToString());
        return stored;
    }

    public List<Subscription> QuerySubscriptions(Func<Subscription, bool> predicate)
    {
        lock (_gate)
        {
            return _subscriptions.Values.Where(predicate).OrderBy(s => s.Id).Select(CopySubscription).ToList();
        }
    }

    public void DeleteSubscription(int id)
    {
        bool removed;
        lock (_gate)
        {
            removed = _subscriptions.Remove(id);
        }

        if (removed)
        {
            Raise("subscription", "deleted", id.ToString());
        }
    }

    public void SetValidSubscription(int id)
    {
        bool updated = false;
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(id, out var subscription))
            {
                subscription.Validated = true;
                updated = true;
            }
        }

        if (updated)
        {
            Raise("subscription", "updated", id.ToString());
        }
    }

    // events.trace_log

    public void InsertTraceLog(TraceLog entry)
    {
        StoredTraceLog stored;
        lock (_gate)
        {
            stored = new StoredTraceLog(
                _nextTraceLogId++,
                DateTimeOffset.UtcNow,
                entry.CloudEventId,
                entry.Resource,
                entry.EventType,
                entry.Consumer,
                entry.SubscriptionId,
                entry.ResponseCode,
                entry.SubscriberEndpoint,
                entry.Activity.ToString());
            _traceLog.Add(stored);
        }

        Raise("trace", "created", stored.CloudEventId?.ToString() ?? stored.Id.ToString());
    }

    public List<StoredTraceLog> QueryTraceLog(Func<StoredTraceLog, bool> predicate)
    {
        lock (_gate)
        {
            return _traceLog.Where(predicate).ToList();
        }
    }

    // Webhook deliveries (localtest only; production has no such table)

    public StoredDelivery InsertDelivery(Func<long, StoredDelivery> create)
    {
        StoredDelivery stored;
        lock (_gate)
        {
            stored = create(_nextDeliveryId++);
            _deliveries.Add(stored);
        }

        Raise("delivery", "created", stored.Id.ToString());
        return stored;
    }

    public List<StoredDelivery> QueryDeliveries(Func<StoredDelivery, bool> predicate)
    {
        lock (_gate)
        {
            return _deliveries.Where(predicate).ToList();
        }
    }

    /// <summary>Empties everything; subscriptions only when asked. Claimed rows stay claimed until their unit of work ends.</summary>
    public void Reset(bool includeSubscriptions)
    {
        lock (_gate)
        {
            _events.Clear();
            _traceLog.Clear();
            _deliveries.Clear();
            if (includeSubscriptions)
            {
                _subscriptions.Clear();
            }
        }

        Raise("all", "reset", null);
    }

    private void Raise(string entity, string kind, string? id)
        => Changed?.Invoke(new LocaltestChange("events", entity, kind, id, DateTimeOffset.UtcNow));

    /// <summary>Copies the columns production stores; <c>AlternativeSubjectFilter</c> has no column, so it does not survive.</summary>
    private static Subscription CopySubscription(Subscription s) => new()
    {
        Id = s.Id,
        ResourceFilter = s.ResourceFilter,
        SourceFilter = s.SourceFilter,
        SubjectFilter = s.SubjectFilter,
        TypeFilter = s.TypeFilter,
        Consumer = s.Consumer,
        EndPoint = s.EndPoint,
        CreatedBy = s.CreatedBy,
        Created = s.Created,
        Validated = s.Validated,
        IncludeSubunits = s.IncludeSubunits,
    };

    /// <summary>
    /// SQL <c>LIKE</c> / <c>ILIKE</c> as the production functions use it: <c>%</c> any run, <c>_</c> one character,
    /// backslash escapes.
    /// </summary>
    public static bool Like(string? value, string pattern, bool ignoreCase)
    {
        if (value is null)
        {
            return false;
        }

        var regex = new System.Text.StringBuilder("^");
        for (int i = 0; i < pattern.Length; i++)
        {
            char c = pattern[i];
            if (c == '\\' && i + 1 < pattern.Length)
            {
                regex.Append(Regex.Escape(pattern[++i].ToString()));
            }
            else if (c == '%')
            {
                regex.Append(".*");
            }
            else if (c == '_')
            {
                regex.Append('.');
            }
            else
            {
                regex.Append(Regex.Escape(c.ToString()));
            }
        }

        regex.Append('$');
        var options = RegexOptions.Singleline | RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.IsMatch(value, regex.ToString(), options);
    }
}

/// <summary>Values of <c>events.event_status</c>.</summary>
public enum EventStatus
{
    Registered = 1,
    Processed = 2,
    RetryExhausted = 3,
}

/// <summary>A row of <c>events.events</c>, with the CloudEvent attributes the SQL reads through <c>cloudevent->>'…'</c>.</summary>
public sealed class StoredEvent
{
    public long SequenceNo { get; set; }

    public required string CloudEvent { get; init; }

    public DateTimeOffset RegisteredTime { get; set; }

    public Guid? IdempotencyKey { get; init; }

    public EventStatus Status { get; set; }

    public int RetryCount { get; set; }

    public DateTimeOffset? LastRetried { get; set; }

    public string? RetryReason { get; set; }

    public string? Id { get; init; }

    public string? Source { get; init; }

    public string? Subject { get; init; }

    public string? AlternativeSubject { get; init; }

    public string? Type { get; init; }

    public string? Resource { get; init; }

    public string? ResourceInstance { get; init; }

    public DateTimeOffset? Time { get; init; }

    public static StoredEvent Parse(string cloudEventJson, Guid? idempotencyKey)
    {
        using var document = JsonDocument.Parse(cloudEventJson);
        var root = document.RootElement;
        string? Attribute(string name) => root.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? (value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText())
            : null;

        return new StoredEvent
        {
            CloudEvent = cloudEventJson,
            IdempotencyKey = idempotencyKey,
            Id = Attribute("id"),
            Source = Attribute("source"),
            Subject = Attribute("subject"),
            AlternativeSubject = Attribute("alternativesubject"),
            Type = Attribute("type"),
            Resource = Attribute("resource"),
            ResourceInstance = Attribute("resourceinstance"),
            Time = DateTimeOffset.TryParse(Attribute("time"), out var time) ? time : null,
        };
    }

    public StoredEvent Copy() => (StoredEvent)MemberwiseClone();
}

/// <summary>A row of <c>events.trace_log</c>, plus the insert time production has as the <c>time</c> column default.</summary>
public sealed record StoredTraceLog(
    long Id,
    DateTimeOffset Time,
    Guid? CloudEventId,
    string? Resource,
    string? EventType,
    string? Consumer,
    int? SubscriptionId,
    int? ResponseCode,
    string? SubscriberEndpoint,
    string Activity);

/// <summary>One outgoing webhook POST, as the capture handler saw it.</summary>
public sealed record StoredDelivery(
    long Id,
    DateTimeOffset Time,
    string? EventId,
    string? EventType,
    string Kind,
    string Endpoint,
    string Mode,
    IReadOnlyDictionary<string, string> RequestHeaders,
    string RequestBody,
    int? ResponseStatus,
    string? ResponseBody,
    long DurationMs,
    string? Error);

/// <summary>The shape of every inspection stream message, shared with the other localtest components.</summary>
public sealed record LocaltestChange(string Component, string Entity, string Kind, string? Id, DateTimeOffset At);
