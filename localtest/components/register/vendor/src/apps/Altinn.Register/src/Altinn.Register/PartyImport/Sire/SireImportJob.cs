using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Core;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Sire;
using Altinn.Register.PartyImport.A2;

namespace Altinn.Register.PartyImport.Sire;

/// <summary>
/// A job that polls SIRE's event feed and enqueues an
/// <see cref="ImportSirePartyCommand"/> per change.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class SireImportJob
    : Job
    , IHasJobName<SireImportJob>
{
    /// <inheritdoc/>
    public static string JobName => JobNames.SireImport;

    private readonly ILogger<SireImportJob> _logger;
    private readonly IImportJobTracker _tracker;
    private readonly ICommandSender _sender;
    private readonly ISireEventClient _client;
    private readonly JobCleanupHelper _cleanupHelper;
    private readonly TimeProvider _timeProvider;
    private readonly ImportMeters _meters;

    /// <summary>
    /// Initializes a new instance of the <see cref="SireImportJob"/> class.
    /// </summary>
    public SireImportJob(
        ILogger<SireImportJob> logger,
        IImportJobTracker tracker,
        ICommandSender sender,
        ISireEventClient client,
        JobCleanupHelper cleanupHelper,
        TimeProvider timeProvider,
        IMetricsProvider metricsProvider)
    {
        _logger = logger;
        _tracker = tracker;
        _sender = sender;
        _client = client;
        _cleanupHelper = cleanupHelper;
        _timeProvider = timeProvider;
        _meters = metricsProvider.Get<ImportMeters>();
    }

    /// <summary>
    /// Runs the job once, bypassing the schedule gate. Intended for the debug-only
    /// "trigger" endpoint so we can exercise the import pipeline on demand. Calling
    /// this through the <c>IJob</c> interface would otherwise collide with the
    /// generic <c>IJob&lt;T&gt;</c> from <c>Altinn.Authorization.ServiceDefaults</c>
    /// at CI restore time.
    /// </summary>
    internal Task RunOnceAsync(CancellationToken cancellationToken)
        => RunAsync(cancellationToken);

    /// <inheritdoc/>
    protected override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var start = _timeProvider.GetTimestamp();
        Log.StartingPartyImport(_logger);

        using var activity = RegisterTelemetry.StartActivity("import sire parties", ActivityKind.Internal);
        var progress = await _tracker.GetStatus(JobName, cancellationToken);
        Log.PartyImportInitialProgress(_logger, in progress);
        var startEnqueuedMax = progress.EnqueuedMax;

        var updates = _client.GetUpdates(checked(((uint)progress.EnqueuedMax) + 1U), cancellationToken);
        await foreach (var page in updates.WithCancellation(cancellationToken))
        {
            if (page.Count == 0)
            {
                // Skip empty pages.
                continue;
            }

            var cmds = page.Select(static update => new ImportSirePartyCommand
            {
                OrganizationIdentifier = update.OrganizationIdentifier,
                ChangedTime = update.RegisteredAt,
                ChangeId = update.SequenceNumber,
                Tracking = new(JobName, update.SequenceNumber),
            });

            await _sender.Send(cmds, cancellationToken);
            Log.EnqueuedPartiesForImport(_logger, page.Count);

            var enqueuedMax = page[^1].SequenceNumber;
            progress = await TrackQueueStatus(JobName, progress, new() { EnqueuedMax = enqueuedMax, SourceMax = null }, cancellationToken);
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
        // This should be removed once the bug is fixed. (Mirrors NprImportJob's identical workaround.)
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
    /// Meters for <see cref="SireImportJob"/>.
    /// </summary>
    private sealed class ImportMeters(Meter meter)
        : IMetrics<ImportMeters>
    {
        /// <summary>
        /// Gets a counter for the number of parties imported from SIRE.
        /// </summary>
        public Counter<int> PartiesEnqueued { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.sire.parties.enqueued", "The number of parties enqueued to be imported from SIRE.");

        /// <inheritdoc/>
        public static ImportMeters Create(Meter meter)
            => new ImportMeters(meter);
    }
}
