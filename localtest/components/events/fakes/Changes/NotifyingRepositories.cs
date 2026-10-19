#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Events.Models;
using Altinn.Platform.Events.Repository;
using CloudNative.CloudEvents;

namespace Altinn.Platform.Events.Localtest.Changes;

/// <summary>
/// Decorates the production <see cref="ICloudEventRepository"/>: raises <c>event created</c> after an insert, and
/// remembers which event a unit of work claimed so <see cref="NotifyingUnitOfWorkRepository"/> can raise
/// <c>event updated</c> once the status change is committed.
/// </summary>
public sealed class NotifyingCloudEventRepository(ICloudEventRepository inner, LocaltestChanges changes, ClaimedEvents claimed) : ICloudEventRepository
{
    public async Task<bool> CreateEvent(string cloudEvent, Guid? idempotencyKey, CancellationToken cancellationToken)
    {
        bool created = await inner.CreateEvent(cloudEvent, idempotencyKey, cancellationToken);
        if (created)
        {
            using var document = JsonDocument.Parse(cloudEvent);
            changes.Raise("event", "created", document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null);
        }

        return created;
    }

    public Task<List<CloudEvent>> GetAppEvents(string after, DateTime? from, DateTime? to, string subject, List<string> source, string resource, List<string> type, int size)
        => inner.GetAppEvents(after, from, to, subject, source, resource, type, size);

    public Task<List<CloudEvent>> GetEvents(string resource, string after, string subject, string alternativeSubject, List<string> type, int size)
        => inner.GetEvents(resource, after, subject, alternativeSubject, type, size);

    public async Task<ClaimedEvent> ClaimRegisteredEventAsync(UnitOfWork unitOfWork, CancellationToken cancellationToken)
    {
        var claimedEvent = await inner.ClaimRegisteredEventAsync(unitOfWork, cancellationToken);
        if (claimedEvent?.CloudEvent?.Id is { } id)
        {
            claimed.Remember(unitOfWork, id);
        }

        return claimedEvent!;
    }

    public Task MarkEventProcessedAsync(UnitOfWork unitOfWork, long sequenceNo, CancellationToken cancellationToken)
        => inner.MarkEventProcessedAsync(unitOfWork, sequenceNo, cancellationToken);

    public Task MarkEventRetryAsync(UnitOfWork unitOfWork, long sequenceNo, string retryReason, CancellationToken cancellationToken)
        => inner.MarkEventRetryAsync(unitOfWork, sequenceNo, retryReason, cancellationToken);
}

/// <summary>Raises <c>event updated</c> when a unit of work that claimed an event commits (processed or retry counted).</summary>
public sealed class NotifyingUnitOfWorkRepository(IUnitOfWorkRepository inner, LocaltestChanges changes, ClaimedEvents claimed) : IUnitOfWorkRepository
{
    public Task<UnitOfWork> StartUnitOfWork() => inner.StartUnitOfWork();

    public async Task CommitUnitOfWork(UnitOfWork unitOfWork)
    {
        await inner.CommitUnitOfWork(unitOfWork);
        if (claimed.Forget(unitOfWork) is { } id)
        {
            changes.Raise("event", "updated", id);
        }
    }

    public async Task RollbackUnitOfWork(UnitOfWork unitOfWork)
    {
        await inner.RollbackUnitOfWork(unitOfWork);
        claimed.Forget(unitOfWork);
    }

    public Task SaveUnitOfWork(UnitOfWork unitOfWork, string savepoint) => inner.SaveUnitOfWork(unitOfWork, savepoint);

    public Task RollbackUnitOfWorkToSavepoint(UnitOfWork unitOfWork, string savepoint) => inner.RollbackUnitOfWorkToSavepoint(unitOfWork, savepoint);
}

/// <summary>Which event each open unit of work claimed.</summary>
public sealed class ClaimedEvents
{
    private readonly ConditionalWeakTable<UnitOfWork, string> _claims = new();

    public void Remember(UnitOfWork unitOfWork, string eventId) => _claims.AddOrUpdate(unitOfWork, eventId);

    public string? Forget(UnitOfWork unitOfWork)
    {
        if (_claims.TryGetValue(unitOfWork, out var id))
        {
            _claims.Remove(unitOfWork);
            return id;
        }

        return null;
    }
}

/// <summary>Decorates the production <see cref="ITraceLogRepository"/>: raises <c>trace created</c> after each row.</summary>
public sealed class NotifyingTraceLogRepository(ITraceLogRepository inner, LocaltestChanges changes) : ITraceLogRepository
{
    public async Task CreateTraceLogEntry(TraceLog traceLog)
    {
        await inner.CreateTraceLogEntry(traceLog);
        changes.Raise("trace", "created", traceLog.CloudEventId?.ToString());
    }
}

/// <summary>Decorates the subscription repository chain: raises after creates, deletes and validations.</summary>
public sealed class NotifyingSubscriptionRepository(ISubscriptionRepository inner, LocaltestChanges changes) : ISubscriptionRepository
{
    public Task<Subscription> FindSubscription(Subscription eventsSubscription, CancellationToken cancellationToken)
        => inner.FindSubscription(eventsSubscription, cancellationToken);

    public async Task<Subscription> CreateSubscription(Subscription eventsSubscription)
    {
        var created = await inner.CreateSubscription(eventsSubscription);
        changes.Raise("subscription", "created", created?.Id.ToString());
        return created!;
    }

    public Task<Subscription> GetSubscription(int id) => inner.GetSubscription(id);

    public async Task DeleteSubscription(int id)
    {
        await inner.DeleteSubscription(id);
        changes.Raise("subscription", "deleted", id.ToString());
    }

    public async Task SetValidSubscription(int id)
    {
        await inner.SetValidSubscription(id);
        changes.Raise("subscription", "updated", id.ToString());
    }

    public Task<IReadOnlyList<Subscription>> GetSubscriptions(string resource, string subject, string eventType, CancellationToken cancellationToken)
        => inner.GetSubscriptions(resource, subject, eventType, cancellationToken);

    public Task<List<Subscription>> GetSubscriptionsByConsumer(string consumer, bool includeInvalid)
        => inner.GetSubscriptionsByConsumer(consumer, includeInvalid);
}
