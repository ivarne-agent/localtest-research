namespace Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;

/// <summary>
/// A strategy for determining the delay before a job should run again based on the outcome of the previous run.
/// </summary>
/// <typeparam name="T">The type of the job outcome.</typeparam>
public interface IDelayStrategy<T>
    where T : notnull
{
    /// <summary>
    /// Gets a description of the delay strategy.
    /// </summary>
    /// <remarks>
    /// This is only used for logging and telemetry purposes.
    /// </remarks>
    public string Description { get; }

    /// <summary>
    /// Determines the delay before the job should run again based on the outcome of the previous run.
    /// </summary>
    /// <param name="outcome">The outcome of the previous job run.</param>
    /// <returns>The delay before the job should run again.</returns>
    public TimeSpan GetDelay(JobOutcome<T> outcome);
}
