using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fixtures;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests;

public sealed class StorageQueuePollJobTests
    : IAsyncLifetime
{
    private StorageFixture? _storage;

    private StorageFixture Storage
        => _storage ?? throw new InvalidOperationException("Storage fixture not initialized.");

    [Fact]
    public async Task RunAsync_WhenHandlerSucceeds_CompletesMessageAndReturnsSinglePage()
    {
        var queue = await Storage.CreateQueue();
        var receiver = CreateReceiver(queue);
        var processed = new List<string>();

        var services = new ServiceCollection();
        services.AddScoped<RecordingHandler>(_ => new RecordingHandler(processed));
        await using var provider = services.BuildServiceProvider();

        var job = new StorageQueuePollJob<RecordingHandler>(
            receiver,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorageQueuePollJob<RecordingHandler>>.Instance);

        var sender = Storage.CreateQueueClient(queue);
        await sender.SendMessageAsync("payload", cancellationToken: TestContext.Current.CancellationToken);

        var result = await ((IJob<StorageQueuePollJobRunResult>)job).RunAsync(TestContext.Current.CancellationToken);

        result.ShouldBe(StorageQueuePollJobRunResult.SinglePage);
        processed.ShouldBe(["payload"]);

        var remaining = await sender.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        remaining.Value.ShouldBeNull();
    }

    [Fact]
    public async Task RunAsync_WhenMessageHitsMaxRetries_MovesMessageToPoisonQueue()
    {
        var queue = await Storage.CreateQueue();
        var mainClient = Storage.CreateQueueClient(queue);
        var poisonClient = Storage.CreatePoisonQueueClient(queue);

        await mainClient.SendMessageAsync("bad-message", cancellationToken: TestContext.Current.CancellationToken);
        await BumpDequeueCountTo(queue, targetDequeueCount: 4);

        var receiver = CreateReceiver(queue);
        var services = new ServiceCollection();
        services.AddScoped<AlwaysFailHandler>();
        await using var provider = services.BuildServiceProvider();

        var job = new StorageQueuePollJob<AlwaysFailHandler>(
            receiver,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<StorageQueuePollJob<AlwaysFailHandler>>.Instance);

        var result = await ((IJob<StorageQueuePollJobRunResult>)job).RunAsync(TestContext.Current.CancellationToken);

        result.ShouldBe(StorageQueuePollJobRunResult.SinglePage);

        var remaining = await mainClient.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        remaining.Value.ShouldBeNull();

        var poisoned = await poisonClient.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        poisoned.Value.ShouldNotBeNull();
        poisoned.Value.Body.ToString().ShouldBe("bad-message");
    }

    public async ValueTask InitializeAsync()
    {
        _storage = await TestContext.Current.GetFixture<StorageFixture>();
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private StorageQueueReceiver CreateReceiver(StorageFixture.QueueInfo queue)
        => new(Storage.CreateQueueClient(queue), Storage.CreatePoisonQueueClient(queue));

    private async Task BumpDequeueCountTo(StorageFixture.QueueInfo queue, int targetDequeueCount)
    {
        var client = Storage.CreateQueueClient(queue);

        for (var expected = 1; expected <= targetDequeueCount; expected++)
        {
            var received = await client.ReceiveMessageAsync(
                visibilityTimeout: TimeSpan.FromSeconds(10),
                cancellationToken: TestContext.Current.CancellationToken);

            received.Value.ShouldNotBeNull();
            received.Value.DequeueCount.ShouldBe(expected);

            await client.UpdateMessageAsync(
                messageId: received.Value.MessageId,
                popReceipt: received.Value.PopReceipt,
                visibilityTimeout: TimeSpan.Zero,
                cancellationToken: TestContext.Current.CancellationToken);

            if (expected < targetDequeueCount)
            {
                continue;
            }
        }
    }

    private sealed class RecordingHandler(List<string> processedMessages)
        : IStorageQueueMessageHandler
    {
        public Task HandleMessageAsync(QueueMessage message, CancellationToken cancellationToken)
        {
            processedMessages.Add(message.Body.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class AlwaysFailHandler
        : IStorageQueueMessageHandler
    {
        public Task HandleMessageAsync(QueueMessage message, CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("boom"));
    }
}
