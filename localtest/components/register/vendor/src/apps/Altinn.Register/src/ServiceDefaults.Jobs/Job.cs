namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// Base class for jobs that can be run on a schedule or on host lifecycle events.
/// </summary>
public abstract class Job<T>
    : IJob<T>
{
    /// <inheritdoc/>
    ValueTask<JobShouldRunResult> IJob<T>.ShouldRun(CancellationToken cancellationToken)
        => ShouldRun(cancellationToken);

    /// <inheritdoc/>
    Task<T> IJob<T>.RunAsync(CancellationToken cancellationToken)
        => RunAsync(cancellationToken);

    /// <inheritdoc cref="IJob{T}.ShouldRun(CancellationToken)"/>
    protected virtual ValueTask<JobShouldRunResult> ShouldRun(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(JobShouldRunResult.Yes);

    /// <inheritdoc cref="IJob{T}.RunAsync(CancellationToken)"/>
    protected abstract Task<T> RunAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Base class for jobs that can be run on a schedule or on host lifecycle events.
/// </summary>
public abstract class Job
    : IJob<Unit>
{
    /// <inheritdoc/>
    ValueTask<JobShouldRunResult> IJob<Unit>.ShouldRun(CancellationToken cancellationToken)
        => ShouldRun(cancellationToken);

    /// <inheritdoc/>
    async Task<Unit> IJob<Unit>.RunAsync(CancellationToken cancellationToken)
    {
        await RunAsync(cancellationToken);
        return Unit.Value;
    }

    /// <inheritdoc cref="IJob{T}.ShouldRun(CancellationToken)"/>
    protected virtual ValueTask<JobShouldRunResult> ShouldRun(CancellationToken cancellationToken = default)
        => ValueTask.FromResult(JobShouldRunResult.Yes);

    /// <inheritdoc cref="IJob{T}.RunAsync(CancellationToken)"/>
    protected abstract Task RunAsync(CancellationToken cancellationToken = default);
}
