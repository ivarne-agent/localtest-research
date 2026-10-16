using Altinn.Authorization.ServiceDefaults.StorageQueues.Utils;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Represents a receiver for an Azure Storage Queue, providing methods to receive messages
/// in batches and move messages to a poison queue when necessary.
/// </summary>
internal sealed class StorageQueueReceiver
{
    private readonly QueueClient _queue;
    private readonly QueueClient _poison;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageQueueReceiver"/> class.
    /// </summary>
    /// <param name="queue">The primary queue client.</param>
    /// <param name="poison">The poison queue client.</param>
    public StorageQueueReceiver(QueueClient queue, QueueClient poison)
    {
        _queue = queue;
        _poison = poison;
    }

    /// <summary>
    /// Receives messages from the queue in batches, yielding them batch by batch. The method continues to receive messages
    /// until the cancellation token is triggered or no more messages are available.
    /// </summary>
    /// <param name="batchSize">The maximum number of messages to fetch in a single batch.</param>
    /// <param name="visibilityTimeout">The duration for which the received messages are invisible to other consumers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An asynchronous stream of queue messages.</returns>
    /// <remarks>
    /// If the batch size is larger than what can be processed within the visibility timeout,
    /// there is an (increased) risk of messages being produced multiple times.
    /// </remarks>
    public IAsyncEnumerable<QueueMessage[]> ReceiveAllMessageAsync(
        int batchSize,
        TimeSpan visibilityTimeout,
        CancellationToken cancellationToken = default)
        => _queue.ReceiveAllMessageAsync(batchSize, visibilityTimeout, cancellationToken);

    /// <summary>
    /// Moves the specified message to the poison queue and deletes it from the original queue.
    /// This is typically used when a message has exceeded the maximum number of processing attempts
    /// and needs to be set aside for later inspection.
    /// </summary>
    /// <param name="message">The message to move to the poison queue.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task MoveToPoisonQueueAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        await _poison.SendMessageAsync(message.Body, cancellationToken: cancellationToken);
        await _queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
    }

    /// <summary>
    /// Marks a message as completed by deleting it from the queue.
    /// </summary>
    /// <param name="message">The message to mark as completed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task CompleteMessageAsync(QueueMessage message, CancellationToken cancellationToken = default)
    {
        await _queue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
    }

    /// <summary>
    /// Reschedules a message by updating its visibility timeout, making it invisible for a specified delay period.
    /// </summary>
    /// <param name="message">The message to reschedule.</param>
    /// <param name="delay">The duration for which the message should be invisible.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>The message's dequeue count will still have been incremented</remarks>
    public async Task RescheduleMessageAsync(QueueMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
    {
        await _queue.UpdateMessageAsync(
            messageId: message.MessageId,
            popReceipt: message.PopReceipt,
            visibilityTimeout: delay,
            cancellationToken: cancellationToken);
    }
}
