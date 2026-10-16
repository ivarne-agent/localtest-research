using System.Diagnostics.CodeAnalysis;

namespace Altinn.Authorization.ServiceDefaults.MassTransit.Commands;

/// <summary>
/// Resolver for command queues.
/// </summary>
public interface ICommandQueueResolver
{
    /// <summary>
    /// Attempts to resolve the queue URI for the specified command type.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <param name="queueUri">The resolved queue URI.</param>
    /// <returns><see langword="true"/> if a queue could be resolved, otherwise <see langword="false"/>.</returns>
    bool TryGetQueueUriForCommandType(Type commandType, [NotNullWhen(true)] out Uri? queueUri);

    /// <summary>
    /// Attempts to resolve the queue URI for the specified command type.
    /// </summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <param name="queueUri">The resolved queue URI.</param>
    /// <returns><see langword="true"/> if a queue could be resolved, otherwise <see langword="false"/>.</returns>
    bool TryGetQueueUriForCommandType<T>([NotNullWhen(true)] out Uri? queueUri)
        where T : CommandBase
        => TryGetQueueUriForCommandType(typeof(T), out queueUri);

    /// <summary>
    /// Resolves the queue URI for the specified command type.
    /// </summary>
    /// <param name="commandType">The command type.</param>
    /// <returns>The resolved queue URI.</returns>
    Uri GetQueueUriForCommandType(Type commandType)
        => TryGetQueueUriForCommandType(commandType, out var queueUri)
            ? queueUri
            : throw new InvalidOperationException($"No queue URI could be resolved for command type '{commandType}'.");

    /// <summary>
    /// Resolves the queue URI for the specified command type.
    /// </summary>
    /// <typeparam name="T">The command type.</typeparam>
    /// <returns>The resolved queue URI.</returns>
    Uri GetQueueUriForCommandType<T>()
        where T : CommandBase
        => GetQueueUriForCommandType(typeof(T));
}
