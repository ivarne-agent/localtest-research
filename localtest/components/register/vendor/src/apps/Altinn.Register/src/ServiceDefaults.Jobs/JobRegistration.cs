using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Altinn.Authorization.ServiceDefaults.Jobs.DelayStrategies;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// A job registration.
/// </summary>
/// <remarks>
/// This class is not intended to be used directly by consumers. Instead, override
/// <see cref="JobRegistration{T}"/>.
/// </remarks>
public abstract class JobRegistration(
    string name,
    string? leaseName,
    JobHostLifecycles runAt,
    IEnumerable<string> tags,
    Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
    Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
    : IJobRegistration
{
    private readonly ImmutableArray<string> _tags = ToTagArray(tags);

    private readonly Lock _lock = new();
    private Task? _ready;

    /// <inheritdoc/>
    public string JobName => name;

    /// <summary>
    /// Gets the lease name for the job. If not <see langword="null"/>, the job host will acquire a
    /// lease with this name before running the job to ensure that only one instance of the job is
    /// running across a cluster of job hosts.
    /// </summary>
    public string? LeaseName => leaseName;

    /// <summary>
    /// Gets the <see cref="JobHostLifecycles"/> that the job should run at. This indicates when the job
    /// should be run by the job host, such as at startup, shutdown. If the job should only run on schedule,
    /// this can be set to <see cref="JobHostLifecycles.None"/>.
    /// </summary>
    public JobHostLifecycles RunAt => runAt;

    /// <summary>
    /// Gets the description of the delay strategy used for this job.
    /// </summary>
    /// <remarks>
    /// This is only used for logging and telemetry purposes.
    /// </remarks>
    internal abstract string DelayStrategyDescription { get; }

    /// <summary>
    /// Gets the initial delay before the job should be run for the first time.
    /// </summary>
    internal abstract TimeSpan GetDelay(JobOutcome.InitialSentinel _);

    /// <summary>
    /// Gets the delay before the job should be run when the job is disabled.
    /// </summary>
    internal abstract TimeSpan GetDelay(JobOutcome.JobDisabledSentinel _);

    /// <summary>
    /// Runs the job. This is called by the job host when it's time to run the job.
    /// </summary>
    /// <param name="services">A scoped <see cref="IServiceProvider"/>.</param>
    /// <param name="telemetry">A telemetry instance for reporting job run metrics and traces.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>The time span until the next run.</returns>
    /// <remarks>
    /// This method should not throw except in fatal scenarios.
    /// </remarks>
    internal abstract Task<JobRunResult> RunAsync(
        IServiceProvider services,
        IJobRunTelemetry telemetry,
        CancellationToken cancellationToken);

    /// <summary>
    /// Waits for the job to be ready to run. This is only ran once per job registration.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// Wait for ready runs in singleton scope, so it should not resolve scoped services. Should never throw.
    /// </remarks>
    internal Task WaitForReady(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        Task? task;
        lock (_lock)
        {
            task = _ready;
            if (task is null)
            {
                task = waitForReady is null ? Task.CompletedTask : waitForReady(services, cancellationToken).AsTask();
                _ready = task;
            }
        }

        return task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Checks if the job has a specific tag.
    /// </summary>
    /// <param name="tag">The tag name.</param>
    /// <returns><see langword="true"/> if the job has the tag <paramref name="tag"/>, otherwise <see langword="false"/>.</returns>
    internal bool HasTag(string tag)
    {
        Guard.IsNotNullOrEmpty(tag);

        // _tags is immutable and sorted, so we can use binary search
        int index = _tags.AsSpan().BinarySearch(tag, StringComparer.Ordinal);
        return index >= 0;
    }

    /// <summary>
    /// Checks if the job is enabled or not.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// Enabled runs in singleton scope, so it should not resolve scoped services. Should never throw.
    ///
    /// Note: this is an earlier check than <see cref="IJob{T}.ShouldRun(CancellationToken)"/>, which
    /// technically runs in the scope of the job itself. This means that if this returns <see langword="false"/>,
    /// the job will not be created or run at all, and the job's <see cref="IJob{T}.ShouldRun(CancellationToken)"/>
    /// method will not be called. <see cref="Enabled(IServiceProvider, CancellationToken)"/> is typically used to
    /// check if the job is enabled based on configuration or other conditions that do not require the job to be
    /// instantiated.
    /// </remarks>
    /// <returns><see langword="true"/> if the job is enabled, otherwise <see langword="false"/>.</returns>
    internal ValueTask<JobShouldRunResult> Enabled(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        return enabled switch
        {
            null => ValueTask.FromResult(JobShouldRunResult.Yes),
            { } fn => fn(services, cancellationToken),
        };
    }

    private static ImmutableArray<string> ToTagArray(IEnumerable<string> tags)
    {
        if (tags is IReadOnlyCollection<string> { Count: 0 }
            || tags is ICollection<string> { Count: 0 })
        {
            return [];
        }

        var set = new SortedSet<string>(tags, StringComparer.Ordinal);
        return [.. set];
    }

    /// <summary>
    /// Gets the job name for a given job type.
    /// </summary>
    /// <param name="jobType">The job type.</param>
    /// <returns>The job name. Normally same as the type name.</returns>
    internal static string GetJobNameForType(Type jobType)
    {
        Guard.IsNotNull(jobType);

        var nameIface = jobType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IHasJobName<>) && i.GetGenericArguments()[0] == jobType);

        if (nameIface is not null)
        {
            var genericMethod = typeof(JobRegistration).GetMethod(nameof(GetJobNameFromIfaceImpl), BindingFlags.Static | BindingFlags.NonPublic)!.MakeGenericMethod(jobType);
            return (string)genericMethod.Invoke(null, [])!;
        }

        return jobType.Name;
    }

    private static string GetJobNameFromIfaceImpl<T>()
        where T : IHasJobName<T>
        => T.JobName;
}

/// <summary>
/// A registration for a job.
/// </summary>
public abstract class JobRegistration<T>(
    string name,
    string? leaseName,
    IDelayStrategy<T>? delayStrategy,
    JobHostLifecycles runAt,
    IEnumerable<string> tags,
    Func<IServiceProvider, CancellationToken, ValueTask<JobShouldRunResult>>? enabled,
    Func<IServiceProvider, CancellationToken, ValueTask>? waitForReady)
    : JobRegistration(name, leaseName, FinalRunAt(runAt, delayStrategy), tags, enabled, waitForReady)
    where T : notnull
{
    private readonly IDelayStrategy<T> _delayStrategy
        = delayStrategy ?? InfiniteDelayStrategy<T>.Instance;

    /// <inheritdoc/>
    internal sealed override string DelayStrategyDescription
        => _delayStrategy.Description;

    /// <inheritdoc/>
    internal sealed override TimeSpan GetDelay(JobOutcome.InitialSentinel value)
        => _delayStrategy.GetDelay(value);

    /// <inheritdoc/>
    internal sealed override TimeSpan GetDelay(JobOutcome.JobDisabledSentinel value)
        => _delayStrategy.GetDelay(value);

    /// <summary>
    /// Creates a new instance of the job.
    /// </summary>
    /// <param name="services">The service provider.</param>
    /// <returns>A new instance of <see cref="IJob{T}"/>.</returns>
    protected abstract IJob<T> Create(IServiceProvider services);

    /// <inheritdoc/>
    internal sealed override async Task<JobRunResult> RunAsync(
        IServiceProvider services,
        IJobRunTelemetry telemetry,
        CancellationToken cancellationToken)
    {
        IJob<T>? job = null;
        TimeProvider timeProvider;

        try
        {
            try
            {
                timeProvider = services.GetRequiredService<TimeProvider>();
                job = Create(services);
            }
            catch (Exception e)
            {
                telemetry.JobCreationFailed(e);
                var delay = _delayStrategy.GetDelay(JobOutcome.Failed(e));
                var exception = ExceptionDispatchInfo.Capture(e);
                return JobRunResult.Failure(delay: delay, exception);
            }

            try
            {
                using var shouldRunActivity = telemetry.StartShouldRun();
                var shouldRun = await job.ShouldRun(cancellationToken);
                if (!shouldRun.ShouldRun)
                {
                    telemetry.JobSkipped(shouldRun.Reason);
                    var delay = _delayStrategy.GetDelay(JobOutcome.Skipped);
                    return JobRunResult.Skipped(delay: delay);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.JobFailed(e);
                var delay = _delayStrategy.GetDelay(JobOutcome.Failed(e));
                var exception = ExceptionDispatchInfo.Capture(e);
                return JobRunResult.Failure(delay: delay, exception);
            }

            var start = timeProvider.GetTimestamp();
            using var activity = telemetry.StartRun();
            try
            {
                telemetry.JobStarting();

                // update the start point to be more correct
                start = timeProvider.GetTimestamp();
                var result = await job.RunAsync(cancellationToken);
                var elapsed = timeProvider.GetElapsedTime(start);

                var delay = _delayStrategy.GetDelay(JobOutcome.Succeeded(result));

                telemetry.JobCompleted(elapsed);
                activity?.SetStatus(ActivityStatusCode.Ok);
                return JobRunResult.Success(delay: delay, duration: elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                telemetry.JobFailed(e);
                activity?.SetStatus(ActivityStatusCode.Error, e.Message);

                var delay = _delayStrategy.GetDelay(JobOutcome.Failed(e));
                var exception = ExceptionDispatchInfo.Capture(e);
                return JobRunResult.Failure(delay: delay, exception);
            }
        }
        finally
        {
            try
            {
                if (job is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync();
                }
                else if (job is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception e)
            {
                telemetry.JobDisposalFailed(e);
            }
        }
    }

    private static JobHostLifecycles FinalRunAt(JobHostLifecycles runAt, IDelayStrategy<T>? delayStrategy)
    {
        if (runAt.HasFlag(JobHostLifecycles.Scheduled))
        {
            ThrowHelper.ThrowArgumentException(
                nameof(runAt),
                "Scheduled jobs is set by specifying a delay strategy, not by setting the RunAt to Scheduled. Remove the Scheduled flag from RunAt.");
        }

        if (delayStrategy is not null)
        {
            runAt |= JobHostLifecycles.Scheduled;
        }

        return runAt;
    }
}
