using Altinn.Authorization.ServiceDefaults.AzureIdentity;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fakes;
using Altinn.Authorization.ServiceDefaults.StorageQueues.Tests.Fixtures;
using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues.Tests;

public sealed class StorageQueuesServiceCollectionExtensionsTests
    : IAsyncLifetime
{
    private StorageFixture? _storage;

    private StorageFixture Storage
        => _storage ?? throw new InvalidOperationException("Storage fixture not initialized.");

    [Fact]
    public void AddStorageQueue_RegistersCoreServicesOnce()
    {
        var services = new ServiceCollection();

        services.AddStorageQueue("queue-a");
        services.AddStorageQueue("queue-b");

        services.Count(static d => d.ServiceType == typeof(IStorageQueueClientFactory)).ShouldBe(1);
        services.Count(static d => d.ServiceType == typeof(IStorageQueueMessageSenderFactory)).ShouldBe(1);
        services.Count(static d => d.ServiceType == typeof(StorageQueueFactory)).ShouldBe(1);
        services.Count(static d => d.ServiceType == typeof(ITokenCredentialProvider)).ShouldBe(1);
    }

    [Fact]
    public async Task AddStorageQueue_CanCreateNamedSender()
    {
        var queue = await Storage.CreateQueue();
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();

        services.AddSingleton<IConfiguration>(configuration);

        services.AddStorageQueue("test")
            .Configure(options =>
            {
                options.IdentityName = "test";
                options.StorageAccountUri = queue.StorageAccountUri;
                options.QueueName = queue.QueueName;
                options.PoisonQueueName = queue.PoisonQueueName;
            });
        services.AddSingleton<ITokenCredentialProvider>(new TestTokenCredentialProvider(queue.Credential));
        services.AddSingleton<IStorageQueueClientFactory>(new TestStorageQueueClientFactory(Storage.CreateTransport()));

        await using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IStorageQueueMessageSenderFactory>();
        var sender = factory.CreateSender("test");

        await sender.SendMessageAsync(new BinaryData("from-factory"), TestContext.Current.CancellationToken);

        var client = Storage.CreateQueueClient(queue);
        var received = await client.ReceiveMessageAsync(cancellationToken: TestContext.Current.CancellationToken);
        received.Value.Body.ToString().ShouldBe("from-factory");
    }

    [Fact]
    public void AddStorageQueueJob_RegistersHandlerAsScoped()
    {
        var services = new ServiceCollection();

        services.AddStorageQueueJob<TestMessageHandler>("test", settings =>
        {
            settings.MinimumInterval = TimeSpan.FromMilliseconds(50);
            settings.MaximumInterval = TimeSpan.FromSeconds(1);
        });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestMessageHandler>().ShouldNotBeNull();
        services.Any(static d => d.ServiceType == typeof(TestMessageHandler) && d.Lifetime == ServiceLifetime.Scoped).ShouldBeTrue();
    }

    public async ValueTask InitializeAsync()
    {
        _storage = await TestContext.Current.GetFixture<StorageFixture>();
    }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;

    private sealed class TestTokenCredentialProvider(TokenCredential credential)
        : ITokenCredentialProvider
    {
        public TokenCredential GetCredential(string name)
        {
            name.ShouldBe("test");
            return credential;
        }
    }

    private sealed class TestMessageHandler
        : IStorageQueueMessageHandler
    {
        public Task HandleMessageAsync(Azure.Storage.Queues.Models.QueueMessage message, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }
}
