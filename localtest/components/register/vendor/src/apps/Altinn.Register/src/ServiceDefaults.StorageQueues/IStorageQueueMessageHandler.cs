using Azure.Storage.Queues.Models;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Interface for handling messages from Azure Storage Queues.
/// </summary>
/// <remarks>
/// All handlers <strong>must</strong> be idempotent. Azure Queue Storage provides
/// at-least-once delivery, so messages can be received multiple times after failures,
/// cancellation, visibility timeout expiry, or delete failures.
/// </remarks>
public interface IStorageQueueMessageHandler
{
    /// <summary>
    /// Handles a message from the queue. If the method completes successfully, the message is deleted from the queue.
    /// </summary>
    /// <param name="message">The message to handle.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public Task HandleMessageAsync(QueueMessage message, CancellationToken cancellationToken);
}
