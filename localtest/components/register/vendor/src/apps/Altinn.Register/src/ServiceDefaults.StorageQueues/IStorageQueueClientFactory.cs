using Azure.Core;
using Azure.Storage.Queues;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Factory for creating <see cref="QueueClient"/> instances.
/// </summary>
public interface IStorageQueueClientFactory
{
    /// <summary>
    /// Creates a <see cref="QueueClient"/> for the specified storage account URI, queue name, and credential.
    /// </summary>
    /// <param name="accountUri">The URI of the storage account.</param>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="credential">The token credential to use for authentication.</param>
    /// <returns>A <see cref="QueueClient"/> instance.</returns>
    QueueClient CreateClient(Uri accountUri, string queueName, TokenCredential credential);
}
