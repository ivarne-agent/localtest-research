using Altinn.Authorization.ServiceDefaults.Jobs;

namespace Altinn.Authorization.ServiceDefaults.StorageQueues;

/// <summary>
/// Represents settings for a storage queue job.
/// </summary>
public interface IQueueJobSettings
{
    /// <summary>
    /// Gets or sets the minimum interval between job executions.
    /// </summary>
    public TimeSpan MinimumInterval { get; set; }

    /// <summary>
    /// Gets or sets the maximum interval between job executions.
    /// </summary>
    public TimeSpan MaximumInterval { get; set; }

    /// <summary>
    /// Gets or sets the delta backoff interval. Defaults to <see cref="MinimumInterval"/> if <see langword="null"/>.
    /// </summary>
    public TimeSpan? DeltaBackoff { get; set; }

    /// <summary>
    /// Gets a collection of tags that can be used to categorize the job.
    /// </summary>
    public ISet<string> Tags { get; }

    /// <summary>
    /// Gets or sets a delegate that will be called to check if the job is enabled or not.
    /// </summary>
    /// <remarks>
    /// This delegate will be called before each time the job is scheduled to run, and it runs in singleton scope.
    /// </remarks>
    public Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? Enabled { get; set; }

    /// <summary>
    /// Gets or sets a delegate that will be called before the first job from a job registration is run.
    /// </summary>
    public Func<IServiceProvider, CancellationToken, ValueTask>? WaitForReady { get; set; }
}
