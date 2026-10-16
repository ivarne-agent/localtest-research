using System.Buffers;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Altinn.Authorization.ServiceDefaults.Telemetry;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Utils;
using CommunityToolkit.Diagnostics;
using MassTransit;

namespace Altinn.Register.PartyImport;

/// <summary>
/// Consumer for upserting parties from different sources in batches.
/// </summary>
public sealed partial class PartyImportBatchConsumer
    : IConsumer<UpsertValidatedPartyCommand>
{
    private const int BATCH_SIZE = 10;

    private readonly ILogger<PartyImportBatchConsumer> _logger;
    private readonly IUnitOfWorkManager _uow;
    private readonly IImportJobTracker _tracker;
    private readonly ImportMeters _meters;
    private readonly PersistenceFeatureFlag[] _flags;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyImportBatchConsumer"/> class.
    /// </summary>
    public PartyImportBatchConsumer(
        ILogger<PartyImportBatchConsumer> logger,
        IUnitOfWorkManager uow,
        IImportJobTracker tracker,
        IMetricsProvider metricsProvider,
        IConfiguration configuration)
    {
        _logger = logger;
        _uow = uow;
        _tracker = tracker;
        _meters = metricsProvider.Get<ImportMeters>();
        _flags = PersistenceFeatureFlag.FromConfiguration(configuration);
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<UpsertValidatedPartyCommand> context)
    {
        await UpsertParties([context], context.CancellationToken);
    }

    /// <summary>
    /// Upserts a set of parties.
    /// </summary>
    /// <param name="upserts">The parties to upsert.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    private async Task UpsertParties(
        IReadOnlyList<ConsumeContext<UpsertValidatedPartyCommand>> upserts,
        CancellationToken cancellationToken)
    {
        using var tracking = TrackingHelper.Create(BATCH_SIZE);
        {
            await using var uow = await _uow.CreateAsync(cancellationToken);
            var persistence = uow.GetPartyPersistence();

            foreach (var context in upserts)
            {
                var party = context.Message.Party;

                // Note: even though the party has already been validated, we still run the validation logic here
                // since it's cheap and gives much better error messages than the database layer. This is mostly
                // to catch if anyone produces UpsertValidatedPartyCommand instances somewhere in the future without
                // actually validating the party.
                PartyImportHelper.ValidatePartyForUpsert(party, _flags);

                var result = await persistence.UpsertParty(party, cancellationToken);
                result.EnsureSuccess();

                tracking.Update(context.Message.Tracking);
                await context.Publish(
                    new PartyUpdatedEvent
                    {
                        Party = result.Value.PartyUuid.Value.ToPartyReferenceContract(),
                    },
                    cancellationToken);
            }

            await uow.CommitAsync(cancellationToken);
        }

        await FlushTracking(tracking, cancellationToken);

        _meters.PartiesUpserted.Add(upserts.Count);
        _meters.PartyBatchesSucceeded.Add(1);
        _meters.PartyBatchSize.Record(upserts.Count);
    }

    private async Task FlushTracking(TrackingHelper tracking, CancellationToken cancellationToken)
    {
        if (!tracking.Any())
        {
            return;
        }

        using var activity = RegisterTelemetry.StartActivity($"{nameof(FlushTracking)}");
        foreach (var info in tracking)
        {
            using var subActivity = RegisterTelemetry.StartActivity(
                $"track {info.JobName}",
                tags: [
                    new("job.name", info.JobName),
                    new("job.progress", info.Progress),
                ]);

            Log.TrackingProgressUpdated(_logger, info.JobName, info.Progress);
            await _tracker.TrackProcessedStatus(info.JobName, new ImportJobProcessingStatus { ProcessedMax = info.Progress }, cancellationToken);
        }
    }

    private sealed class TrackingHelper
        : IEnumerable<UpsertPartyTracking>
        , IDisposable
    {
        public static TrackingHelper Create(int size)
            => new(ArrayPool<UpsertPartyTracking>.Shared, size);

        private readonly ArrayPool<UpsertPartyTracking> _pool;
        private UpsertPartyTracking[]? _tracking;

        private TrackingHelper(ArrayPool<UpsertPartyTracking> pool, int size)
        {
            _pool = pool;
            _tracking = pool.Rent(size);

            Debug.Assert(_tracking[0].JobName is null);
        }

        public bool Any()
        {
            if (_tracking is null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(TrackingHelper));
            }

            return _tracking[0].JobName is not null;
        }

        public IEnumerator<UpsertPartyTracking> GetEnumerator()
        {
            if (_tracking is null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(TrackingHelper));
            }

            return _tracking.TakeWhile(static t => t.JobName is not null).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public void Update(UpsertPartyTracking trackingInfo)
        {
            if (_tracking is null)
            {
                ThrowHelper.ThrowObjectDisposedException(nameof(TrackingHelper));
            }

            if (trackingInfo.JobName is null)
            {
                return;
            }

            for (var i = 0; i < _tracking.Length; i++)
            {
                ref var existing = ref _tracking[i];
                if (existing.JobName is null)
                {
                    existing = trackingInfo;
                    return;
                }

                if (existing.JobName == trackingInfo.JobName)
                {
                    if (existing.Progress < trackingInfo.Progress)
                    {
                        existing = trackingInfo;
                    }

                    return;
                }
            }

            Debug.Fail("Tracking array is full.");
        }

        public void Dispose()
        {
            if (_tracking is not null)
            {
                _pool.Return(_tracking, clearArray: true);
                _tracking = null;
            }
        }
    }

    /// <summary>
    /// Consumer definition for <see cref="PartyImportBatchConsumer"/>.
    /// </summary>
    public sealed class Definition
        : ConsumerDefinition<PartyImportBatchConsumer>
    {
        private readonly bool _isTest;

        /// <summary>
        /// Initializes a new instance of the <see cref="Definition"/> class.
        /// </summary>
        public Definition(IHostEnvironment host)
        {
            _isTest = host.ApplicationName == "test";
        }

        /// <inheritdoc/>
        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<PartyImportBatchConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            base.ConfigureConsumer(endpointConfigurator, consumerConfigurator, context);

            endpointConfigurator.PrefetchCount = BATCH_SIZE * 5;
            endpointConfigurator.ConcurrentMessageLimit = 3;
        }
    }

    /// <summary>
    /// Meters for <see cref="PartyImportBatchConsumer"/>.
    /// </summary>
    private sealed class ImportMeters(Meter meter)
        : IMetrics<ImportMeters>
    {
        /// <summary>
        /// Gets a counter for the number of parties upserted.
        /// </summary>
        public Counter<int> PartiesUpserted { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.party.upsert.succeeded.total", description: "The number of parties upserted.");

        /// <summary>
        /// Gets a counter for the number of party-batches upserted.
        /// </summary>
        public Counter<int> PartyBatchesSucceeded { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.party.batch.succeeded.total", description: "The number of party-batches upserted.");

        /// <summary>
        /// Gets a histogram for the size of party batches upserted.
        /// </summary>
        public Histogram<int> PartyBatchSize { get; }
            = meter.CreateHistogram<int>("altinn.register.party-import.party.batch.size", description: "The size of party batches upserted.");

        public Counter<int> RoleAssignmentUpsertsSucceeded { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.role-assignment.upsert.succeeded.total", description: "The number of role assignment-upsert that has succeeded.");

        public Counter<int> RoleAssignmentBatchesSucceeded { get; }
            = meter.CreateCounter<int>("altinn.register.party-import.role-assignment.batch.succeeded.total", description: "The number of role assignment-batches that has succeeded.");

        public Histogram<int> RoleAssignmentBatchSize { get; }
            = meter.CreateHistogram<int>("altinn.register.party-import.role-assignment.batch.size", description: "The size of role assignment-batches upserted.");

        public Histogram<int> RoleAssignmentUpsertSize { get; }
            = meter.CreateHistogram<int>("altinn.register.party-import.role-assignment.count", description: "The number of role assignments in a single upsert.");

        /// <inheritdoc/>
        public static ImportMeters Create(Meter meter)
            => new ImportMeters(meter);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Trace, "Updating progress tracking for job '{JobName}' with progress {Progress}.")]
        public static partial void TrackingProgressUpdated(ILogger logger, string jobName, ulong progress);
    }
}
