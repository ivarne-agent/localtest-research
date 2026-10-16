using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fixtures;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests;

public sealed class StorageQueueSenderTests
    : IAsyncLifetime
{
    private StorageFixture? _storage;

    private CancellationToken CancellationToken
        => TestContext.Current.CancellationToken;

    private StorageFixture Storage
        => _storage ?? throw new InvalidOperationException("Storage fixture not initialized.");

    [Fact]
    public async Task CanSendMessage()
    {
        var queue = await Storage.CreateQueue();
        var client = Storage.CreateQueueClient(queue);

        var sender = new StorageQueueSender(client);

        var message = new BinaryData("Hello, world!");
        await sender.SendMessageAsync(message, CancellationToken);

        // Verify
        var received = await client.ReceiveMessageAsync(cancellationToken: CancellationToken);
        received.Value.ShouldNotBeNull();
        received.Value.Body.ToString().ShouldBe("Hello, world!");
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    public async ValueTask InitializeAsync()
    {
        _storage = await TestContext.Current.GetFixture<StorageFixture>();
    }
}
