using Altinn.Authorization.ServiceDefaults.Leases;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;

namespace Altinn.Register.PartyImport;

/// <summary>
/// Helper for running periodic cleanup of party data.
/// </summary>
public sealed partial class JobCleanupHelper
{
    private readonly ILogger<JobCleanupHelper> _logger;
    private readonly IPartyPersistenceCleanupService _cleanupService;
    private readonly LeaseManager _leaseManager;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobCleanupHelper"/> class.
    /// </summary>
    public JobCleanupHelper(
        ILogger<JobCleanupHelper> logger,
        IPartyPersistenceCleanupService cleanupService,
        LeaseManager leaseManager,
        TimeProvider timeProvider)
    {
        _logger = logger;
        _cleanupService = cleanupService;
        _leaseManager = leaseManager;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Runs cleanup if new "pages" were enqueued during the job run, and cleanup has not been run in at least 15 minutes.
    /// </summary>
    /// <param name="jobName">The name of the job running the cleanup.</param>
    /// <param name="startEnqueuedMax">The start "enqueued max" for the job.</param>
    /// <param name="latestStatus">The latest status of the job.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    public Task MaybeRunCleanup(string jobName, ulong startEnqueuedMax, in ImportJobStatus latestStatus, CancellationToken cancellationToken)
    {
        const ulong CLEANUP_PAGE_SIZE = 10_000;

        var endEnqueuedMax = latestStatus.EnqueuedMax;
        var startPage = startEnqueuedMax / CLEANUP_PAGE_SIZE;
        var endPage = endEnqueuedMax / CLEANUP_PAGE_SIZE;

        if (endPage <= startPage)
        {
            // No new "pages" were enqueued during this run, so no cleanup is necessary.
            Log.SkippingCleanup_NoNewPage(_logger, jobName, startEnqueuedMax, endEnqueuedMax);
            return Task.CompletedTask;
        }

        return TryRunCleanup(jobName, cancellationToken);
    }

    private async Task TryRunCleanup(string jobName, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        ////await using var lease = await _leaseManager.AcquireLease(
        ////    LeaseNames.PartyCleanup,
        ////    info =>
        ////    {
        ////        // Only run cleanup if the last time it was run was more than 15 minutes ago.
        ////        if (info.LastReleasedAt is null)
        ////        {
        ////            return true;
        ////        }

        ////        if (now - info.LastReleasedAt.Value < TimeSpan.FromMinutes(15))
        ////        {
        ////            return false;
        ////        }

        ////        return true;
        ////    },
        ////    cancellationToken);
        await using var lease = await _leaseManager.AcquireLease(LeaseNames.PartyCleanup, cancellationToken);

        if (!lease.Acquired)
        {
            Log.SkippingCleanup_LeaseNotAcquired(_logger, jobName, lease.LastReleasedAt);
            return;
        }

        var start = _timeProvider.GetUtcNow();
        Log.StartingPeriodicPartyCleanup(_logger, jobName);
        await _cleanupService.RunPeriodicPartyCleanup(lease.Token);

        var duration = _timeProvider.GetUtcNow() - start;
        Log.FinishedPeriodicPartyCleanup(_logger, jobName, duration);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Skipping cleanup for job {JobName} since no new pages were enqueued during this run. Before job enqueued max: {StartEnqueuedMax}, after job enqueued max: {EndEnqueuedMax}")]
        public static partial void SkippingCleanup_NoNewPage(ILogger logger, string jobName, ulong startEnqueuedMax, ulong endEnqueuedMax);

        [LoggerMessage(1, LogLevel.Information, "Starting periodic party cleanup for job {JobName}.")]
        public static partial void StartingPeriodicPartyCleanup(ILogger logger, string jobName);

        [LoggerMessage(2, LogLevel.Information, "Finished periodic party cleanup for job {JobName} in {Duration}.")]
        public static partial void FinishedPeriodicPartyCleanup(ILogger logger, string jobName, TimeSpan duration);

        [LoggerMessage(3, LogLevel.Information, "Skipping cleanup for job {JobName} since lease could not be acquired or last cleanup was run less than 15 minutes ago. Lease last released at: {LeaseLastReleasedAt}")]
        public static partial void SkippingCleanup_LeaseNotAcquired(ILogger logger, string jobName, DateTimeOffset? leaseLastReleasedAt);
    }
}
