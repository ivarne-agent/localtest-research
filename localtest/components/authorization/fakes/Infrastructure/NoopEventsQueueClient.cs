#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authorization.Clients.Interfaces;
using Altinn.Platform.Authorization.Models;
using Altinn.Platform.Authorization.Models.EventLog;

namespace Altinn.Platform.Authorization.Localtest.Infrastructure;

/// <summary>No Azure Storage Queue: audit events are dropped (the audit log feature is also forced off).</summary>
public sealed class NoopEventsQueueClient : IEventsQueueClient
{
    public Task<QueuePostReceipt> EnqueueAuthorizationEvent(AuthorizationEvent authorizationEvent, CancellationToken cancellationToken = default)
        => Task.FromResult(new QueuePostReceipt { Success = true });
}
