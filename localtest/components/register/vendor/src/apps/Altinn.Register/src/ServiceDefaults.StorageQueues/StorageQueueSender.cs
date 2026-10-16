using Azure.Storage.Queues;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// A sender for Azure Storage Queues.
/// </summary>
internal sealed class StorageQueueSender
    : IStorageQueueMessageSender
{
    private readonly QueueClient _queue;

    /// <summary>
    /// Creates a new instance of the <see cref="StorageQueueSender"/> class with the specified <see cref="QueueClient"/>.
    /// </summary>
    /// <param name="queue">The storage queue to send to.</param>
    public StorageQueueSender(QueueClient queue)
    {
        _queue = queue;
    }

    /// <summary>
    /// Sends a message to the storage queue.
    /// </summary>
    /// <param name="message">The message content to send. Must be valid UTF-8.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public async Task SendMessageAsync(BinaryData message, CancellationToken cancellationToken = default)
    {
        await _queue.SendMessageAsync(message, visibilityTimeout: null, timeToLive: null, cancellationToken);
    }
}
