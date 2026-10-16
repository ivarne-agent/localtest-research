using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit.Commands;

/// <summary>
/// Registry for command queues.
/// </summary>
public interface ICommandQueueRegistry
{
    /// <summary>
    /// Registers a command queue.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="registration">The <see cref="CommandQueueInfo"/>.</param>
    void RegisterCommandQueue(Type commandType, CommandQueueInfo registration);

    /// <summary>
    /// Registers a command queue.
    /// </summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <param name="registration">The <see cref="CommandQueueInfo"/>.</param>
    void RegisterCommandQueue<T>(CommandQueueInfo registration)
        where T : CommandBase
        => RegisterCommandQueue(typeof(T), registration);

    /// <summary>
    /// Registers a remote command queue.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="queueUri">The queue URI.</param>
    void RegisterRemoteCommandQueue(Type commandType, Uri queueUri)
        => RegisterCommandQueue(commandType, CommandQueueInfo.Remote(queueUri));

    /// <summary>
    /// Registers a remote command queue.
    /// </summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <param name="queueUri">The queue URI.</param>
    void RegisterRemoteCommandQueue<T>(Uri queueUri)
        where T : CommandBase
        => RegisterCommandQueue<T>(CommandQueueInfo.Remote(queueUri));

    /// <summary>
    /// Registers a consumer command queue.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="queueUri">The queue URI.</param>
    /// <param name="consumerType">The consumer type.</param>
    void RegisterConsumerCommandQueue(Type commandType, Uri queueUri, Type consumerType)
        => RegisterCommandQueue(commandType, CommandQueueInfo.Consumer(queueUri, consumerType));

    /// <summary>
    /// Registers a consumer command queue.
    /// </summary>
    /// <typeparam name="TCommand">The command type.</typeparam>
    /// <typeparam name="TConsumer">The consumer type.</typeparam>
    /// <param name="queueUri">The queue URI.</param>
    void RegisterConsumerCommandQueue<TCommand, TConsumer>(Uri queueUri)
        where TCommand : CommandBase
        where TConsumer : IConsumer<TCommand>
        => RegisterCommandQueue<TCommand>(CommandQueueInfo.Consumer<TConsumer>(queueUri));
}
