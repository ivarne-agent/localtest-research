#nullable enable
using System;
using System.Threading.Tasks;
using Altinn.Platform.Events.Clients.Interfaces;
using Altinn.Platform.Events.Models;

namespace Altinn.Platform.Events.Localtest.Queues;

/// <summary>
/// Stands in for the Azure Storage Queue client of the legacy pipeline. Localtest runs the Wolverine pipeline
/// in-process, so only the endpoints the Azure Functions call back (<c>inbound</c>, <c>outbound</c>) still reach a
/// queue; they answer with a clear failure instead of trying to connect to Azurite.
/// </summary>
public sealed class LocaltestEventsQueueClient : IEventsQueueClient
{
    public Task<QueuePostReceipt> EnqueueRegistration(string content) => Unavailable("registration");

    public Task<QueuePostReceipt> EnqueueInbound(string content) => Unavailable("inbound");

    public Task<QueuePostReceipt> EnqueueOutbound(string content) => Unavailable("outbound");

    public Task<QueuePostReceipt> EnqueueSubscriptionValidation(string content) => Unavailable("subscription validation");

    private static Task<QueuePostReceipt> Unavailable(string queue) => Task.FromResult(new QueuePostReceipt
    {
        Success = false,
        Exception = new NotSupportedException(
            $"The legacy {queue} storage queue is not available in localtest; events are processed in-process."),
    });
}
