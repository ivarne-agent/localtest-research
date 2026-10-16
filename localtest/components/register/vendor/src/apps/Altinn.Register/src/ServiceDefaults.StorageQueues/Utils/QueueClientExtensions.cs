using System.Runtime.CompilerServices;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Utils;

/// <summary>
/// Extensions for <see cref="QueueClient"/>.
/// </summary>
internal static class QueueClientExtensions
{
    /// <param name="queue">The queue client.</param>
    extension(QueueClient queue)
    {
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
        public async IAsyncEnumerable<QueueMessage[]> ReceiveAllMessageAsync(
            int batchSize,
            TimeSpan visibilityTimeout,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var response = await queue.ReceiveMessagesAsync(
                    maxMessages: batchSize,
                    visibilityTimeout: visibilityTimeout,
                    cancellationToken: cancellationToken);

                var messages = response.Value;
                if (messages is not { Length: > 0 })
                {
                    // No more messages available
                    yield break;
                }

                yield return messages;
            }
        }
    }
}
