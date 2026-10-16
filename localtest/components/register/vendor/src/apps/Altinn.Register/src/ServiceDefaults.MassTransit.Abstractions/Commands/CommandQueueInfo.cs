using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit.Commands;

/// <summary>
/// Information about a command queue.
/// </summary>
/// <remarks>This mainly exists to have better error messages when things are wrongly configured.</remarks>
public record CommandQueueInfo
{
    /// <summary>
    /// Creates a new <see cref="CommandQueueInfo"/> for a remote command queue.
    /// </summary>
    /// <param name="queueUri">The queue URI.</param>
    /// <returns>A <see cref="CommandQueueInfo"/>.</returns>
    public static CommandQueueInfo Remote(Uri queueUri)
        => new(queueUri);

    /// <summary>
    /// Creates a new <see cref="CommandQueueInfo"/> for a command queue with a consumer type.
    /// </summary>
    /// <param name="queueUri">The queue URI.</param>
    /// <param name="consumerType">The consumer type.</param>
    /// <returns>A <see cref="CommandQueueInfo"/>.</returns>
    public static CommandQueueInfo Consumer(Uri queueUri, Type consumerType)
        => new CommandConsumerInfo(queueUri, consumerType);

    /// <summary>
    /// Creates a new <see cref="CommandQueueInfo"/> for a command queue with a consumer type.
    /// </summary>
    /// <typeparam name="T">The consumer type.</typeparam>
    /// <param name="queueUri">The queue URI.</param>
    /// <returns>A <see cref="CommandQueueInfo"/>.</returns>
    public static CommandQueueInfo Consumer<T>(Uri queueUri)
        where T : IConsumer
        => new CommandConsumerInfo<T>(queueUri);

    private CommandQueueInfo(Uri queueUri)
    {
        ArgumentNullException.ThrowIfNull(queueUri);

        QueueUri = queueUri;
    }

    /// <summary>
    /// Gets the queue URI.
    /// </summary>
    public Uri QueueUri { get; }

    /// <inheritdoc />
    public override string ToString()
        => $"{QueueUri}";

    /// <summary>
    /// Information about a command queue with a consumer type.
    /// </summary>
    /// <param name="QueueUri">The queue URI.</param>
    /// <param name="ConsumerType">The consumer type.</param>
    private record CommandConsumerInfo(Uri QueueUri, Type ConsumerType)
        : CommandQueueInfo(QueueUri)
    {
        /// <inheritdoc />
        public override string ToString()
            => $"{TypeCache.GetShortName(ConsumerType)} ({QueueUri})";
    }

    /// <summary>
    /// Information about a command queue with a consumer type.
    /// </summary>
    /// <typeparam name="T">The consumer type.</typeparam>
    /// <param name="QueueUri">The queue URI.</param>
    private record CommandConsumerInfo<T>(Uri QueueUri)
        : CommandConsumerInfo(QueueUri, typeof(T))
    {
        /// <inheritdoc />
        public override string ToString()
            => $"{TypeCache<T>.ShortName} ({QueueUri})";
    }
}
