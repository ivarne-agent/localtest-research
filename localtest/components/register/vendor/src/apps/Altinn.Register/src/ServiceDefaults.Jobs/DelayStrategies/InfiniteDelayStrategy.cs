namespace Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;

/// <summary>
/// A delay strategy that returns an infinite delay regardless of the job outcome.
/// </summary>
/// <typeparam name="T">The job outcome type.</typeparam>
internal sealed class InfiniteDelayStrategy<T>
    : IDelayStrategy<T>
    where T : notnull
{
    /// <summary>
    /// Gets a singleton instance of the <see cref="InfiniteDelayStrategy{T}"/> class.
    /// </summary>
    internal static InfiniteDelayStrategy<T> Instance { get; } = new();

    /// <inheritdoc/>
    public string Description => "Infinite Delay";

    private InfiniteDelayStrategy()
    {
    }

    /// <summary>
    /// Gets an infinite delay regardless of the job outcome.
    /// </summary>
    /// <returns>An infinite <see cref="TimeSpan"/>.</returns>
    public TimeSpan GetDelay(JobOutcome<T> _)
        => Timeout.InfiniteTimeSpan;
}
