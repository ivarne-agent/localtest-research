using System.Diagnostics;
using System.Diagnostics.Metrics;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Core;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.PartyImport.A2;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// A job that imports parties from A2.
/// </summary>
public sealed partial class A2PartyImportJob
    : Job
    , IHasJobName<A2PartyImportJob>
{
    /// <inheritdoc/>
    public static string JobName => JobNames.A2PartyImportParty;

    private readonly ILogger<A2PartyImportJob> _logger;
    private readonly IImportJobTracker _tracker;
    private readonly ICommandSender _sender;
    private readonly IA2PartyImportService _importService;
    private readonly JobCleanupHelper _cleanupHelper;
    private readonly TimeProvider _timeProvider;
    private readonly ImportMeters _meters;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2PartyImportJob"/> class.
    /// </summary>
    public A2PartyImportJob(
        ILogger<A2PartyImportJob> logger,
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
    protected override async Task RunAsync(CancellationToken cancellationToken)
    {
        var start = _timeProvider.GetTimestamp();
        Log.StartingPartyImport(_logger);

        using var activity = RegisterTelemetry.StartActivity("import a2-parties", ActivityKind.Internal);
        var progress = await _tracker.GetStatus(JobName, cancellationToken);
        Log.PartyImportInitialProgress(_logger, in progress);
        var startEnqueuedMax = progress.EnqueuedMax;

        var changes = _importService.GetChanges(checked((uint)progress.EnqueuedMax), cancellationToken);
        await foreach (var page in changes.WithCancellation(cancellationToken))
        {
            if (page.Count == 0)
            {
                // Skip empty pages.
                continue;
            }

            var cmds = page.Select(static update => new ImportA2PartyCommand
            {
                PartyUuid = update.PartyUuid,
                ChangedTime = update.ChangeTime,
                ChangeId = update.ChangeId,
                Tracking = new(JobName, update.ChangeId),
            });

            await _sender.Send(cmds, cancellationToken);
            Log.EnqueuedPartiesForImport(_logger, page.Count);

            var enqueuedMax = page[^1].ChangeId;
            var sourceMax = page.LastKnownChangeId;
            progress = await TrackQueueStatus(JobName, progress, new() { EnqueuedMax = enqueuedMax, SourceMax = sourceMax }, cancellationToken);
            _meters.PartiesEnqueued.Add(page.Count);

            if (enqueuedMax - progress.ProcessedMax > 50_000)
            {
                Log.PausingEnqueueingParties(_logger, enqueuedMax, progress.ProcessedMax);
                break;
            }
        }

        var duration = _timeProvider.GetElapsedTime(start);
        Log.FinishedPartyImport(_logger, duration);

        await _cleanupHelper.MaybeRunCleanup(JobName, startEnqueuedMax, in progress, cancellationToken);
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
        [LoggerMessage(0, LogLevel.Information, "Enqueued {Count} parties for import.")]
        public static partial void EnqueuedPartiesForImport(ILogger logger, int count);

        [LoggerMessage(1, LogLevel.Information, "More than 50'000 parties in queue since last measurement. Pausing enqueueing. EnqueuedMax = {EnqueuedMax}, ProcessedMax = {ProcessedMax}.")]
        public static partial void PausingEnqueueingParties(ILogger logger, ulong enqueuedMax, ulong processedMax);

        [LoggerMessage(3, LogLevel.Information, "Starting party import.")]
        public static partial void StartingPartyImport(ILogger logger);

        [LoggerMessage(4, LogLevel.Information, "Finished party import in {Duration}.")]
        public static partial void FinishedPartyImport(ILogger logger, TimeSpan duration);

        [LoggerMessage(8, LogLevel.Information, "Party import initial progress: EnqueuedMax = {EnqueuedMax}, SourceMax = {SourceMax}, ProcessedMax = {ProcessedMax}.")]
        private static partial void PartyImportInitialProgress(ILogger logger, ulong enqueuedMax, ulong? sourceMax, ulong processedMax);

        public static void PartyImportInitialProgress(ILogger logger, in ImportJobStatus status)
            => PartyImportInitialProgress(logger, status.EnqueuedMax, status.SourceMax, status.ProcessedMax);
    }

    /// <summary>
    /// Meters for <see cref="A2PartyImportJob"/>.
    /// </summary>
    private sealed class ImportMeters(Meter meter)
        : IMetrics<ImportMeters>
    {
        /// <summary>
        /// Gets a counter for the number of parties imported from A2.
        /// </summary>
        public Counter<int> PartiesEnqueued { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.a2.parties.enqueued", "The number of parties enqueued to be imported from A2.");

        /// <inheritdoc/>
        public static ImportMeters Create(Meter meter)
            => new ImportMeters(meter);
    }
}
