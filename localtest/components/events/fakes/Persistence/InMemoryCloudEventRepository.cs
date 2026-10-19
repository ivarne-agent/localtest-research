#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Configuration;
using Altinn.Platform.Events.Models;
using Altinn.Platform.Events.Repository;
using CloudNative.CloudEvents;
using CloudNative.CloudEvents.SystemTextJson;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Events.Localtest.Persistence;

/// <summary>
/// <see cref="CloudEventRepository"/> on <see cref="LocaltestEventsStore"/>. Each method follows the SQL the production
/// repository runs (the insert, <c>getappevents_v2</c>, <c>getevents_v2</c>, <c>claim_registered_event</c> and the two
/// status updates in <c>Migration/v0.59</c>), including the 30-second delay before an event is readable.
/// </summary>
public sealed class InMemoryCloudEventRepository(
    LocaltestEventsStore store,
    InMemoryUnitOfWorkRepository unitsOfWork,
    IOptions<EventsProcessingSettings> eventsProcessingSettings) : ICloudEventRepository
{
    /// <summary>Production reads only events registered at least this long ago, so in-flight ones are not skipped by an <c>after</c> cursor.</summary>
    private static readonly TimeSpan ReadDelay = TimeSpan.FromSeconds(30);

    public Task<bool> CreateEvent(string cloudEvent, Guid? idempotencyKey, CancellationToken cancellationToken)
        => Task.FromResult(store.InsertEvent(cloudEvent, idempotencyKey) is not null);

    public Task<List<CloudEvent>> GetAppEvents(string after, DateTime? from, DateTime? to, string subject, List<string> source, string resource, List<string> type, int size)
    {
        var rows = store.QueryEvents(events =>
        {
            long? first = AfterSequenceNo(events, after);
            DateTimeOffset readable = DateTimeOffset.UtcNow - ReadDelay;
            long? last = events.Where(e => e.RegisteredTime <= readable).Select(e => (long?)e.SequenceNo).Max();

            return events
                .Where(e => subject is null || e.Subject == subject)
                .Where(e => from is null || e.Time >= ToOffset(from.Value))
                .Where(e => to is null || e.Time <= ToOffset(to.Value))
                .Where(e => type is null || type.Any(t => LocaltestEventsStore.Like(e.Type, t, ignoreCase: true)))
                .Where(e => source is null || source.Any(s => LocaltestEventsStore.Like(e.Source, s, ignoreCase: true)))
                .Where(e => resource is null || e.Resource == resource)
                .Where(e => first is null || e.SequenceNo > first)
                .Where(e => last is null || e.SequenceNo <= last)
                .OrderBy(e => e.SequenceNo)
                .Take(size);
        });

        return Task.FromResult(rows.Select(e => DeserializeAndConvertTime(e.CloudEvent)).ToList());
    }

    public Task<List<CloudEvent>> GetEvents(string resource, string after, string subject, string alternativeSubject, List<string> type, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        var rows = store.QueryEvents(events =>
        {
            long? first = AfterSequenceNo(events, after);
            DateTimeOffset readable = DateTimeOffset.UtcNow - ReadDelay;

            return events
                .Where(e => e.Resource == resource)
                .Where(e => subject is null || e.Subject == subject)
                .Where(e => alternativeSubject is null || e.AlternativeSubject == alternativeSubject)
                .Where(e => type is null || type.Any(t => LocaltestEventsStore.Like(e.Type, t, ignoreCase: false)))
                .Where(e => e.RegisteredTime <= readable)
                .Where(e => first is null || e.SequenceNo > first)
                .OrderBy(e => e.SequenceNo)
                .Take(size);
        });

        return Task.FromResult(rows.Select(e => DeserializeAndConvertTime(e.CloudEvent)).ToList());
    }

    public Task<ClaimedEvent> ClaimRegisteredEventAsync(UnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var claimed = store.ClaimRegisteredEvent();
        if (claimed is null)
        {
            return Task.FromResult<ClaimedEvent>(null!);
        }

        unitsOfWork.RecordClaim(unitOfWork, claimed.SequenceNo);
        return Task.FromResult(new ClaimedEvent
        {
            SequenceNo = claimed.SequenceNo,
            CloudEvent = DeserializeAndConvertTime(claimed.CloudEvent),
        });
    }

    public Task MarkEventProcessedAsync(UnitOfWork unitOfWork, long sequenceNo, CancellationToken cancellationToken)
    {
        unitsOfWork.Enqueue(unitOfWork, () => store.MarkEventProcessed(sequenceNo));
        return Task.CompletedTask;
    }

    public Task MarkEventRetryAsync(UnitOfWork unitOfWork, long sequenceNo, string retryReason, CancellationToken cancellationToken)
    {
        int maxRetryCount = eventsProcessingSettings.Value.MaxRetryCount;
        unitsOfWork.Enqueue(unitOfWork, () => store.MarkEventRetry(sequenceNo, retryReason, maxRetryCount));
        return Task.CompletedTask;
    }

    /// <summary>The <c>_after</c> lookup: no cursor is null, an unknown id is 0 (everything), a known id its first sequence number.</summary>
    private static long? AfterSequenceNo(IReadOnlyList<StoredEvent> events, string after)
    {
        if (string.IsNullOrEmpty(after))
        {
            return null;
        }

        return events.Where(e => e.Id == after).Select(e => (long?)e.SequenceNo).Min() ?? 0;
    }

    /// <summary>Npgsql sends an unspecified-kind <see cref="DateTime"/> for <c>timestamptz</c> as UTC.</summary>
    private static DateTimeOffset ToOffset(DateTime value)
        => value.Kind == DateTimeKind.Local ? new DateTimeOffset(value) : new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    /// <summary>Same as the production repository's private helper.</summary>
    private static CloudEvent DeserializeAndConvertTime(string eventString)
    {
        var formatter = new JsonEventFormatter();
        CloudEvent cloudEvent = formatter.DecodeStructuredModeMessage(new MemoryStream(Encoding.UTF8.GetBytes(eventString)), null, null);

        if (cloudEvent.Time != null)
        {
            cloudEvent.Time = cloudEvent.Time.Value.ToUniversalTime();
        }

        return cloudEvent;
    }
}
