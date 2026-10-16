using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Core;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.PartyImport.A2;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// A job that imports profile changes from At.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed partial class A2ProfileImportJob
    : Job
    , IHasJobName<A2ProfileImportJob>
{
    /// <inheritdoc/>
    public static string JobName => JobNames.A2ProfileChangesImport;

    private readonly ILogger<A2ProfileImportJob> _logger;
    private readonly IImportJobTracker _tracker;
    private readonly ICommandSender _sender;
    private readonly IA2PartyImportService _importService;
    private readonly JobCleanupHelper _cleanupHelper;
    private readonly TimeProvider _timeProvider;
    private readonly ImportMeters _meters;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2PartyImportJob"/> class.
    /// </summary>
    public A2ProfileImportJob(
        ILogger<A2ProfileImportJob> logger,
        IImportJobTracker tracker,
        ICommandSender sender,
        IA2PartyImportService importService,
        JobCleanupHelper cleanupHelper,
        TimeProvider timeProvider,
        IMetricsProvider metricsProvider)
    {
        _logger = logger;
        _tracker = tracker;
        _sender = sender;
        _importService = importService;
        _cleanupHelper = cleanupHelper;
        _timeProvider = timeProvider;
        _meters = metricsProvider.Get<ImportMeters>();
    }

    /// <inheritdoc/>
    protected override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await RunProfileImport(cancellationToken);
    }

    private async Task<ImportJobStatus> RunProfileImport(CancellationToken cancellationToken)
    {
        var start = _timeProvider.GetTimestamp();
        Log.StartingProfileImport(_logger);

        using var activity = RegisterTelemetry.StartActivity("import a2-profile-changes", ActivityKind.Internal);
        var progress = await _tracker.GetStatus(JobName, cancellationToken);
        Log.ProfileImportInitialProgress(_logger, progress);
        var startEnqueuedMax = progress.EnqueuedMax;

        var changes = _importService.GetUserProfileChanges(checked((uint)progress.EnqueuedMax), cancellationToken);
        await foreach (var page in changes.WithCancellation(cancellationToken))
        {
            if (page.Count == 0)
            {
                // Skip empty pages.
                continue;
            }

            var cmds = page.Select(static update => new ImportA2UserProfileCommand
            {
                UserId = update.UserId,
                PartyUuid = update.UserUuid,
                OwnerPartyUuid = update.OwnerPartyUuid,
                IsDeleted = update.IsDeleted,
                Tracking = new(JobName, update.ChangeId),
            });

            await _sender.Send(cmds, cancellationToken);
            Log.EnqueuedProfilesForImport(_logger, page.Count);

            var enqueuedMax = page[^1].ChangeId;
            var sourceMax = page.LastKnownChangeId;
            progress = await TrackQueueStatus(JobName, progress, new() { EnqueuedMax = enqueuedMax, SourceMax = sourceMax }, cancellationToken);
            _meters.PartiesEnqueued.Add(page.Count);

            if (enqueuedMax - progress.ProcessedMax > 50_000)
            {
                Log.PausingEnqueueingProfiles(_logger, enqueuedMax, progress.ProcessedMax);
                break;
            }
        }

        var duration = _timeProvider.GetElapsedTime(start);
        Log.FinishedProfileImport(_logger, duration);

        await _cleanupHelper.MaybeRunCleanup(JobName, startEnqueuedMax, in progress, cancellationToken);
        return progress;
    }

    private async Task<ImportJobStatus> TrackQueueStatus(string name, ImportJobStatus current, ImportJobQueueStatus newStatus, CancellationToken cancellationToken)
    {
        var (newProgress, _) = await _tracker.TrackQueueStatus(name, newStatus, cancellationToken);

        // TODO: this is working around a bug that currently exists in the tracker where the status returned is for whatever reason lower than the current status.
        // This should be removed once the bug is fixed.
        var sourceMax = NullMax(current.SourceMax, newProgress.SourceMax);
        var enqueuedMax = Math.Max(current.EnqueuedMax, newProgress.EnqueuedMax);
        var processedMax = Math.Max(current.ProcessedMax, newProgress.ProcessedMax);
        return new() { SourceMax = sourceMax, EnqueuedMax = enqueuedMax, ProcessedMax = processedMax };

        static ulong? NullMax(ulong? val1, ulong? val2)
            => (val1, val2) switch
            {
                (null, null) => null,
                (null, ulong u) => u,
                (ulong u, null) => u,
                (ulong u1, ulong u2) => Math.Max(u1, u2),
            };
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Profile import initial progress: EnqueuedMax = {EnqueuedMax}, SourceMax = {SourceMax}, ProcessedMax = {ProcessedMax}.")]
        private static partial void ProfileImportInitialProgress(ILogger logger, ulong enqueuedMax, ulong? sourceMax, ulong processedMax);

        public static void ProfileImportInitialProgress(ILogger logger, in ImportJobStatus status)
            => ProfileImportInitialProgress(logger, status.EnqueuedMax, status.SourceMax, status.ProcessedMax);

        [LoggerMessage(1, LogLevel.Information, "Enqueued {Count} profiles for import.")]
        public static partial void EnqueuedProfilesForImport(ILogger logger, int count);

        [LoggerMessage(2, LogLevel.Information, "More than 50'000 profiles in queue since last measurement. Pausing enqueueing. EnqueuedMax = {EnqueuedMax}, ProcessedMax = {ProcessedMax}.")]
        public static partial void PausingEnqueueingProfiles(ILogger logger, ulong enqueuedMax, ulong processedMax);

        [LoggerMessage(3, LogLevel.Information, "Starting profile import.")]
        public static partial void StartingProfileImport(ILogger logger);

        [LoggerMessage(4, LogLevel.Information, "Finished profile import in {Duration}.")]
        public static partial void FinishedProfileImport(ILogger logger, TimeSpan duration);
    }

    /// <summary>
    /// Meters for <see cref="A2ProfileImportJob"/>.
    /// </summary>
    private sealed class ImportMeters(Meter meter)
        : IMetrics<ImportMeters>
    {
        /// <summary>
        /// Gets a counter for the number of parties imported from A2.
        /// </summary>
        public Counter<int> PartiesEnqueued { get; }
            = meter.CreateCounter<int>("altinn.register.profile-import.a2.parties.enqueued", "The number of parties enqueued to be imported from A2.");

        /// <inheritdoc/>
        public static ImportMeters Create(Meter meter)
            => new ImportMeters(meter);
    }
}
