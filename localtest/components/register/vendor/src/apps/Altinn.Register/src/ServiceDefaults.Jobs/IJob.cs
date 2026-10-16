namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// A job that can be run on a schedule or on host lifecycle events.
/// </summary>
public interface IJob<T>
{
    /// <summary>
    /// Checks if the job should run at this time.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="JobShouldRunResult"/> indicating whether the job should run.</returns>
    public ValueTask<JobShouldRunResult> ShouldRun(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the job.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The job outcome.</returns>
    public Task<T> RunAsync(CancellationToken cancellationToken = default);
}
