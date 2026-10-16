using Altinn.Authorization.ServiceDefaults.AzureIdentity;
using Azure.Storage.Queues;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Factory for creating <see cref="QueueClient"/> instances based on named settings.
/// </summary>
internal sealed class StorageQueueFactory
    : IStorageQueueMessageSenderFactory
{
    private readonly IOptionsMonitor<StorageQueueSettings> _settings;
    private readonly ITokenCredentialProvider _credentialProvider;
    private readonly IStorageQueueClientFactory _clientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageQueueFactory"/> class.
    /// </summary>
    public StorageQueueFactory(
        ITokenCredentialProvider credentialProvider,
        IStorageQueueClientFactory clientFactory,
        IOptionsMonitor<StorageQueueSettings> settings)
    {
        _credentialProvider = credentialProvider;
        _clientFactory = clientFactory;
        _settings = settings;
    }

    /// <summary>
    /// Creates a receiver used to receive messages from a storage queue.
    /// </summary>
    /// <param name="name">The name of the configuration to use.</param>
    /// <returns>A <see cref="StorageQueueReceiver"/> instance.</returns>
    public StorageQueueReceiver CreateReceiver(string name)
    {
        var settings = _settings.Get(name);

        if (string.IsNullOrWhiteSpace(settings.PoisonQueueName))
        {
            ThrowHelper.ThrowInvalidOperationException($"Poison queue name must be specified for storage queue '{name}'.");
        }

        if (string.Equals(settings.QueueName, settings.PoisonQueueName, StringComparison.OrdinalIgnoreCase))
        {
            ThrowHelper.ThrowInvalidOperationException($"Queue name and poison queue name cannot be the same for storage queue '{name}'.");
        }

        var credential = _credentialProvider.GetCredential(settings.IdentityName);
        var queue = _clientFactory.CreateClient(settings.StorageAccountUri!, settings.QueueName!, credential);
        var poison = _clientFactory.CreateClient(settings.StorageAccountUri!, settings.PoisonQueueName!, credential);
        return new(queue, poison);
    }

    /// <summary>
    /// Creates a sender used to send messages to a storage queue.
    /// </summary>
    /// <param name="name">The name of the configuration to use.</param>
    /// <returns>A <see cref="StorageQueueSender"/> instance.</returns>
    public StorageQueueSender CreateSender(string name)
    {
        var settings = _settings.Get(name);
        var credential = _credentialProvider.GetCredential(settings.IdentityName);
        var queue = _clientFactory.CreateClient(settings.StorageAccountUri!, settings.QueueName!, credential);
        return new(queue);
    }

    /// <inheritdoc/>
    IStorageQueueMessageSender IStorageQueueMessageSenderFactory.CreateSender(string name)
        => CreateSender(name);
}
