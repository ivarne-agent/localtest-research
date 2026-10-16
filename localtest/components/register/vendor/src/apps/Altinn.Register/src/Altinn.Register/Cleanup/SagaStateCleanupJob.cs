using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.UnitOfWork;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Cleanup;

/// <summary>
/// Represents a scheduled job responsible for cleaning up saga state data within the system.
/// </summary>
public sealed partial class SagaStateCleanupJob
    : Job
    , IHasJobName<SagaStateCleanupJob>
{
    /// <inheritdoc/>
    public static string JobName => JobNames.SagaStateCleanup;

    private readonly IUnitOfWorkManager _manager;
    private readonly IOptionsMonitor<SagaStateCleanupSettings> _settings;
    private readonly ILogger<SagaStateCleanupJob> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="SagaStateCleanupJob"/> class.
    /// </summary>
    public SagaStateCleanupJob(
        IUnitOfWorkManager manager,
        IOptionsMonitor<SagaStateCleanupSettings> settings,
        ILogger<SagaStateCleanupJob> logger,
        TimeProvider timeProvider)
    {
        _manager = manager;
        _settings = settings;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    protected override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var settings = _settings.CurrentValue;

        await using var uow = await _manager.CreateAsync(cancellationToken, activityName: "cleanup saga states");
        var cleanup = uow.GetRequiredService<ISagaStateCleanup>();

        var now = _timeProvider.GetUtcNow();
        var completedCutoff = now - TimeSpan.FromDays(settings.DeleteCompletedSagaStatesAfterDays);
        var faultedCutoff = now - TimeSpan.FromDays(settings.DeleteFaultedSagaStatesAfterDays);
        var inProgressCutoff = now - TimeSpan.FromDays(settings.DeleteInProgressSagaStatesAfterDays);

        var deleted = await cleanup.DeleteOldStates(
            completedBefore: completedCutoff,
            faultedBefore: faultedCutoff,
            inProgressBefore: inProgressCutoff,
            cancellationToken);

        await uow.CommitAsync(cancellationToken);
        Log.DeletedSagaStates(_logger, deleted, completedCutoff, faultedCutoff, inProgressCutoff);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Deleted {Deleted} saga states. Completed before {CompletedCutoff}, faulted before {FaultedCutoff}, in-progress before {InProgressCutoff}.")]
        public static partial void DeletedSagaStates(
            ILogger<SagaStateCleanupJob> logger,
            int deleted,
            DateTimeOffset completedCutoff,
            DateTimeOffset faultedCutoff,
            DateTimeOffset inProgressCutoff);
    }
}
