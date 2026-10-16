using Azure.Core;
using Azure.Storage.Queues;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Default implementation of <see cref="IStorageQueueClientFactory"/>.
/// </summary>
internal sealed class DefaultStorageQueueClientFactory
    : IStorageQueueClientFactory
{
    /// <inheritdoc/>
    public QueueClient CreateClient(Uri accountUri, string queueName, TokenCredential credential)
        => new QueueClient(new Uri(accountUri, queueName), credential);
}
