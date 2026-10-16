using System.Runtime.ExceptionServices;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Represents the result of a job run.
/// </summary>
internal sealed record JobRunResult
{
    /// <summary>
    /// Creates a <see cref="JobRunResult"/> representing a successful job run.
    /// </summary>
    /// <param name="delay">The delay before the job should be run again.</param>
    /// <param name="duration">The duration of the job run.</param>
    /// <returns>A <see cref="JobRunResult"/> representing a successful job run.</returns>
    public static JobRunResult Success(TimeSpan delay, TimeSpan duration)
        => new(Outcome.Success, exception: null, delay) { Duration = duration };

    /// <summary>
    /// Creates a <see cref="JobRunResult"/> representing a failed job run.
    /// </summary>
    /// <param name="delay">The delay before the job should be run again.</param>
    /// <param name="exception">The exception that caused the job to fail.</param>
    /// <returns>A <see cref="JobRunResult"/> representing a failed job run.</returns>
    public static JobRunResult Failure(TimeSpan delay, ExceptionDispatchInfo exception)
        => new(Outcome.Failure, exception, delay);

    /// <summary>
    /// Creates a <see cref="JobRunResult"/> representing a skipped job run.
    /// </summary>
    /// <param name="delay">The delay before the job should be run again.</param>
    /// <returns>A <see cref="JobRunResult"/> representing a skipped job run.</returns>
    public static JobRunResult Skipped(TimeSpan delay)
        => new(Outcome.Skipped, exception: null, delay);

    private readonly Outcome _outcome;
    private readonly ExceptionDispatchInfo? _exception;

    /// <summary>
    /// Gets the delay before the job should be run again.
    /// </summary>
    public TimeSpan Delay { get; private init; }

    /// <summary>
    /// Gets the duration of the job run.
    /// </summary>
    /// <remarks>
    /// This is only set if the job succeeded.
    /// </remarks>
    public TimeSpan Duration { get; private init; }

    /// <summary>
    /// Rethrows the exception if the job run failed.
    /// </summary>
    public void RethrowIfFailure()
    {
        if (_exception is not null)
        {
            _exception.Throw();
        }
    }

    private JobRunResult(Outcome outcome, ExceptionDispatchInfo? exception, TimeSpan delay)
    {
        _outcome = outcome;
        _exception = exception;
        Delay = delay;
    }

    private enum Outcome
    {
        /// <summary>
        /// Indicates that <see cref="JobRegistration.Enabled(IServiceProvider, CancellationToken)"/> returned <see langword="false"/>.
        /// </summary>
        Disabled,

        /// <summary>
        /// Indicates that <see cref="IJob{T}.ShouldRun(CancellationToken)"/> did not return <see cref="JobShouldRunResult.Yes"/>.
        /// </summary>
        Skipped,

        /// <summary>
        /// Indicates that the job ran successfully.
        /// </summary>
        Success,

        /// <summary>
        /// Indicates that the job ran, but failed with an exception.
        /// </summary>
        Failure,
    }
}
