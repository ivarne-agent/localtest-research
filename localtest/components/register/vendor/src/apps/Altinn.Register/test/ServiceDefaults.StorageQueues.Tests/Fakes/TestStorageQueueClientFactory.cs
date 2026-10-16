using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Queues;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fakes;

internal sealed class TestStorageQueueClientFactory(HttpPipelineTransport transport)
    : IStorageQueueClientFactory
{
    public QueueClient CreateClient(Uri accountUri, string queueName, TokenCredential credential)
        => new(
            new Uri(accountUri, queueName),
            credential,
            new QueueClientOptions
            {
                Transport = transport,
            });
}
