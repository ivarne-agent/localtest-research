using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Static helpers for <see cref="JobOutcome{T}"/>.
/// </summary>
public static class JobOutcome
{
    /// <summary>
    /// A sentinel value representing an initial state before any job run has occurred.
    /// </summary>
    public static InitialSentinel None
        => default;

    /// <summary>
    /// A sentinel value representing a disabled job that did not run.
    /// </summary>
    public static JobDisabledSentinel Disabled
        => default;

    /// <summary>
    /// A sentinel value representing a skipped job that did not run.
    /// </summary>
    public static JobSkippedSentinel Skipped
        => default;

    /// <summary>
    /// A sentinel value representing a successful job that ran and returned the given result.
    /// </summary>
    /// <param name="exception">The exception thrown by the job.</param>
    /// <returns>A sentinel value representing a failed job.</returns>
    public static JobFailedSentinel Failed(Exception exception)
        => new(exception);

    /// <summary>
    /// A sentinel value representing a successful job that ran and returned the given result.
    /// </summary>
    /// <typeparam name="T">The job result type.</typeparam>
    /// <param name="result">The result returned by the job.</param>
    /// <returns>A sentinel value representing a successful job.</returns>
    public static JobSucceededSentinel<T> Succeeded<T>(T result)
        where T : notnull
        => new(result);

    /// <summary>
    /// A sentinel value representing an initial state before any job run has occurred.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly record struct InitialSentinel;

    /// <summary>
    /// A sentinel value representing a disabled job that did not run.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly record struct JobDisabledSentinel;

    /// <summary>
    /// A sentinel value representing a skipped job that did not run.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly record struct JobSkippedSentinel;

    /// <summary>
    /// A sentinel value representing a failed job that ran and threw the given exception.
    /// </summary>
    /// <param name="Exception">The job exception.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly record struct JobFailedSentinel(Exception Exception);

    /// <summary>
    /// A sentinel value representing a successful job that ran and returned the given result.
    /// The result must not be <see langword="null"/>.
    /// </summary> <param name="Result">The job result.</param>
    /// <typeparam name="T">The type of the job result.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public readonly record struct JobSucceededSentinel<T>(T Result)
        where T : notnull;

    /// <summary>
    /// An enumeration representing the possible outcomes of a job run.
    /// </summary>
    internal enum Outcome
        : byte
    {
        /// <summary>
        /// Initial state before any job run has occurred.
        /// </summary>
        None,

        /// <summary>
        /// Job is disabled, typically by configuration.
        /// </summary>
        JobDisabled,

        /// <summary>
        /// Job was skipped, <see cref="IJob{T}.ShouldRun(CancellationToken)"/> did not return <see cref="JobShouldRunResult.Yes"/>.
        /// </summary>
        JobSkipped,

        /// <summary>
        /// Job ran and failed with an exception.
        /// </summary>
        JobFailure,

        /// <summary>
        /// Job ran and succeeded.
        /// </summary>
        JobSuccess,
    }
}

/// <summary>
/// Represents the outcome of a job run.
/// </summary>
/// <typeparam name="T">The job result type.</typeparam>
public readonly record struct JobOutcome<T>
    where T : notnull
{
    private readonly JobOutcome.Outcome _outcome;
    private readonly T? _result;
    private readonly Exception? _exception;

    private JobOutcome(
        JobOutcome.Outcome outcome,
        T? result,
        Exception? exception)
    {
        _outcome = outcome;
        _result = result;
        _exception = exception;
    }

    /// <summary>
    /// Gets whether this <see cref="JobOutcome{T}"/> represents an initial state before any job run has occurred.
    /// </summary>
    public bool IsInitial
        => _outcome == JobOutcome.Outcome.None;

    /// <summary>
    /// Gets whether the job was disabled and did not run.
    /// </summary>
    public bool IsJobDisabled
        => _outcome == JobOutcome.Outcome.JobDisabled;

    /// <summary>
    /// Gets whether the job was skipped because <see cref="IJob{T}.ShouldRun(CancellationToken)"/> did not return <see cref="JobShouldRunResult.Yes"/>.
    /// </summary>
    public bool IsJobSkipped
        => _outcome == JobOutcome.Outcome.JobSkipped;

    /// <summary>
    /// Gets whether the job ran and failed with an exception.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Exception))]
    public bool IsJobFailure
        => _outcome == JobOutcome.Outcome.JobFailure;

    /// <summary>
    /// Gets whether the job ran and succeeded.
    /// </summary>
    [MemberNotNullWhen(true, nameof(Result))]
    public bool IsJobSuccess
        => _outcome == JobOutcome.Outcome.JobSuccess;

    /// <summary>
    /// Gets the exception thrown by the job if it failed.
    /// </summary>
    public Exception? Exception
        => _exception;

    /// <summary>
    /// Gets the result returned by the job if it succeeded.
    /// </summary>
    public T? Result
        => _result;

    /// <summary>
    /// Implicitly converts a <see cref="JobOutcome.InitialSentinel"/> to a <see cref="JobOutcome{T}"/> representing an initial state before any job run has occurred.
    /// </summary>
    [DebuggerStepThrough]
    public static implicit operator JobOutcome<T>(JobOutcome.InitialSentinel _)
        => new JobOutcome<T>(JobOutcome.Outcome.None, default, default);

    /// <summary>
    /// Implicitly converts a <see cref="JobOutcome.JobDisabledSentinel"/> to a <see cref="JobOutcome{T}"/> representing a disabled job.
    /// </summary>
    [DebuggerStepThrough]
    public static implicit operator JobOutcome<T>(JobOutcome.JobDisabledSentinel _)
        => new JobOutcome<T>(JobOutcome.Outcome.JobDisabled, default, default);

    /// <summary>
    /// Implicitly converts a <see cref="JobOutcome.JobSkippedSentinel"/> to a <see cref="JobOutcome{T}"/> representing a skipped job.
    /// </summary>
    [DebuggerStepThrough]
    public static implicit operator JobOutcome<T>(JobOutcome.JobSkippedSentinel _)
        => new JobOutcome<T>(JobOutcome.Outcome.JobSkipped, default, default);

    /// <summary>
    /// Implicitly converts a <see cref="JobOutcome.JobFailedSentinel"/> to a <see cref="JobOutcome{T}"/> representing a failed job with the given exception.
    /// </summary>
    /// <param name="sentinel">The sentinel representing a failed job.</param>
    [DebuggerStepThrough]
    public static implicit operator JobOutcome<T>(JobOutcome.JobFailedSentinel sentinel)
        => new JobOutcome<T>(JobOutcome.Outcome.JobFailure, default, sentinel.Exception);

    /// <summary>
    /// Implicitly converts a <see cref="JobOutcome.JobSucceededSentinel{T}"/> to a <see cref="JobOutcome{T}"/> representing a successful job with the given result.
    /// </summary>
    /// <param name="sentinel">The sentinel representing a successful job.</param>
    [DebuggerStepThrough]
    public static implicit operator JobOutcome<T>(JobOutcome.JobSucceededSentinel<T> sentinel)
        => new JobOutcome<T>(JobOutcome.Outcome.JobSuccess, sentinel.Result, default);
}
