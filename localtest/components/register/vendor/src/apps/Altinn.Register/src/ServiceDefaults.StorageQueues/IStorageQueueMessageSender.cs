namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Defines an interface for sending messages to a storage queue, abstracting away the underlying implementation details.
/// </summary>
public interface IStorageQueueMessageSender
{
    /// <summary>
    /// Sends a message to the storage queue.
    /// </summary>
    /// <param name="message">The message content to send. Must be valid UTF-8.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task SendMessageAsync(BinaryData message, CancellationToken cancellationToken = default);
}
