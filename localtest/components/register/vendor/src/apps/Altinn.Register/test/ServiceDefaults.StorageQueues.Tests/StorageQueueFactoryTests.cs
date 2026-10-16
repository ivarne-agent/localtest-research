using Altinn.Authorization.ServiceDefaults.AzureIdentity;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fakes;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fixtures;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.TestHelpers;
using Azure.Core;
using Azure.Storage.Queues.Models;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests;

public sealed class StorageQueueFactoryTests
    : IAsyncLifetime
{
    private StorageFixture? _storage;

    private StorageFixture Storage
        => _storage ?? throw new InvalidOperationException("Storage fixture not initialized.");

    [Fact]
    public async Task CreateSender_CanSendMessage()
    {
        var queue = await Storage.CreateQueue();
        var factory = CreateFactory(queue, expectedIdentityName: "sender-id");

        var sender = factory.CreateSender("sender");

        await sender.SendMessageAsync(new BinaryData("hello"), TestContext.Current.CancellationToken);

        var client = Storage.CreateQueueClient(queue);
        var received = await client.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        received.Value.Body.ToString().ShouldBe("hello");
    }

    [Fact]
    public async Task CreateReceiver_CanMoveToPoisonQueue()
    {
        var queue = await Storage.CreateQueue();
        var factory = CreateFactory(queue, expectedIdentityName: "receiver-id");

        var senderClient = Storage.CreateQueueClient(queue);
        await senderClient.SendMessageAsync("poison-me", cancellationToken: TestContext.Current.CancellationToken);

        var receiver = factory.CreateReceiver("receiver");
        var message = await ReceiveSingleMessage(receiver);

        await receiver.MoveToPoisonQueueAsync(message, TestContext.Current.CancellationToken);

        var queueReceived = await senderClient.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        queueReceived.Value.ShouldBeNull();

        var poisonClient = Storage.CreatePoisonQueueClient(queue);
        var poisonReceived = await poisonClient.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        poisonReceived.Value.ShouldNotBeNull();
        poisonReceived.Value.Body.ToString().ShouldBe("poison-me");
    }

    public async ValueTask InitializeAsync()
    {
        _storage = await TestContext.Current.GetFixture<StorageFixture>();
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private StorageQueueFactory CreateFactory(StorageFixture.QueueInfo queue, string expectedIdentityName)
    {
        var credentialProvider = new TestTokenCredentialProvider(queue.Credential, expectedIdentityName);
        var clientFactory = new TestStorageQueueClientFactory(Storage.CreateTransport());
        var options = new TestOptionsMonitor<StorageQueueSettings>(name =>
        {
            name.ShouldBeOneOf("sender", "receiver");
            return new StorageQueueSettings
            {
                IdentityName = expectedIdentityName,
                StorageAccountUri = queue.StorageAccountUri,
                QueueName = queue.QueueName,
                PoisonQueueName = queue.PoisonQueueName,
            };
        });

        return new StorageQueueFactory(credentialProvider, clientFactory, options);
    }

    private static async Task<QueueMessage> ReceiveSingleMessage(StorageQueueReceiver receiver)
    {
        await foreach (var batch in receiver.ReceiveAllMessageAsync(1, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken))
        {
            return batch.ShouldHaveSingleItem();
        }

        throw new InvalidOperationException("Expected a message.");
    }

    private sealed class TestTokenCredentialProvider(TokenCredential credential, string expectedName)
        : ITokenCredentialProvider
    {
        public TokenCredential GetCredential(string name)
        {
            name.ShouldBe(expectedName);
            return credential;
        }
    }
}
