namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Defines a factory interface for creating instances of <see cref="IStorageQueueMessageSender"/>.
/// </summary>
public interface IStorageQueueMessageSenderFactory
{
    /// <summary>
    /// Creates a sender used to send messages to a storage queue.
    /// </summary>
    /// <param name="name">The name of the configuration to use.</param>
    /// <returns>A <see cref="IStorageQueueMessageSender"/> instance.</returns>
    public IStorageQueueMessageSender CreateSender(string name);
}
