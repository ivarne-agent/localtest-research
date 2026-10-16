using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public sealed partial class A2PartyImportSaga
    : ISaga<A2PartyImportSaga, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaStartedBy<A2PartyImportSaga, ImportA2PartyCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaStartedBy<A2PartyImportSaga, ImportA2UserProfileCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaStartedBy<A2PartyImportSaga, ImportNprPartyCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaStartedBy<A2PartyImportSaga, ImportSirePartyCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaHandles<A2PartyImportSaga, EnrichA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaHandles<A2PartyImportSaga, CompleteA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>
    , ISagaHandles<A2PartyImportSaga, RetryA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>
{
    /// <inheritdoc/>
    public static string Name => nameof(A2PartyImportSaga);

    private readonly SagaContext<A2PartyImportSagaData> _context;
    private readonly IA2PartyImportService _importService;
    private readonly TimeProvider _timeProvider;
    private readonly IPartyPersistence _parties;
    private readonly IPartyExternalRolePersistence _roles;
    private readonly IImportJobTracker _tracker;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ILogger<A2PartyImportSaga> _logger;

    private A2PartyImportSagaData State => _context.State.Data!;

    private Guid SagaId => _context.SagaId;

    private void MarkComplete(bool error = false)
    {
        _context.State.Status = error ? SagaStatus.Faulted : SagaStatus.Completed;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="A2PartyImportSaga"/> class.
    /// </summary>
    public A2PartyImportSaga(
        SagaContext<A2PartyImportSagaData> context,
        IA2PartyImportService importService,
        TimeProvider timeProvider,
        IPartyPersistence parties,
        IPartyExternalRolePersistence roles,
        IImportJobTracker tracker,
        IConfiguration configuration,
        IServiceProvider services,
        ILogger<A2PartyImportSaga> logger)
    {
        _context = context;
        _importService = importService;
        _timeProvider = timeProvider;
        _parties = parties;
        _roles = roles;
        _tracker = tracker;
        _configuration = configuration;
        _services = services;
        _logger = logger;
    }

    private async Task Enrich(CancellationToken cancellationToken)
    {
        Debug.Assert(State.Party is not null);
        Debug.Assert(State.Enrichers.Count == 0);

        var context = new A2PartyImportSagaEnrichmentCheckContext { Party = State.Party, PartyIdentifier = State.PartyIdentifier };
        State.Enrichers = new(A2PartyImportSagaEnricher.For(_configuration, context));

        await ContinueEnrichment(cancellationToken);
    }

    private async Task ContinueEnrichment(CancellationToken cancellationToken)
    {
        if (State.Enrichers.Count > 0)
        {
            await _context.Send(
                  new EnrichA2PartyImportSagaCommand
                  {
                      CorrelationId = SagaId,
                      PartyIdentifier = State.PartyIdentifier,
                  },
                  cancellationToken);

            return;
        }

        await _context.Send(
            new CompleteA2PartyImportSagaCommand
            {
                CorrelationId = SagaId,
                PartyIdentifier = State.PartyIdentifier,
            },
            cancellationToken);
    }

    private async Task<FlowControl> FetchPartyFromA2(CancellationToken cancellationToken)
    {
        if (!State.PartyIdentifier.TryGetValue(out Guid partyUuid))
        {
            ThrowHelper.ThrowInvalidOperationException("FetchPartyFromA2 can only be called when PartyIdentifier is a PartyUuid");
        }

        using var activity = RegisterTelemetry.StartActivity("fetch party altinn 2", ActivityKind.Internal, tags: [new("party.uuid", partyUuid)]);

        var partyResult = await _importService.GetParty(partyUuid, cancellationToken);
        if (partyResult is { Problem.ErrorCode: var errorCode }
            && errorCode == Problems.PartyGone.ErrorCode)
        {
            // Party is gone, so we can skip it. These should be rare, so don't bother with tracking.
            Log.PartyGone(_logger, partyUuid);
            MarkComplete();
            return FlowControl.Break;
        }

        partyResult.EnsureSuccess();
        State.Party = partyResult.Value;
        return FlowControl.Continue;
    }

    /// <summary>
    /// State data for <see cref="A2PartyImportSaga"/>.
    /// </summary>
    public sealed class A2PartyImportSagaData
        : ISagaStateData<A2PartyImportSagaData>
    {
        /// <inheritdoc/>
        public static string StateType => "A2PartyImportSagaData@2";

        /// <summary>
        /// Gets the unique identifier for the party.
        /// </summary>
        public required ImportPartyIdentifier PartyIdentifier { get; init; }

        /// <summary>
        /// Gets tracking information for the import job.
        /// </summary>
        public required UpsertPartyTracking Tracking { get; init; }

        /// <summary>
        /// Gets or sets the party being upserted.
        /// </summary>
        public PartyRecord? Party { get; set; }

        /// <summary>
        /// Gets or sets the collection of role assignments grouped by external role source.
        /// </summary>
        public Dictionary<ExternalRoleSource, PartyExternalRoleAssignmentsUpdate> RoleAssignments { get; set; } = new();

        /// <summary>
        /// Gets or sets the remaining enrichers.
        /// </summary>
        public Queue<string> Enrichers { get; set; } = new();

        /// <summary>
        /// Clears non-initial state data.
        /// </summary>
        internal void Clear()
        {
            Party = null;
            RoleAssignments.Clear();
            Enrichers.Clear();
        }

        /// <inheritdoc/>
        static ValueTask<A2PartyImportSagaData?> ISagaStateData<A2PartyImportSagaData>.ReadAsync(
            Stream stream,
            string stateType,
            JsonSerializerOptions options,
            CancellationToken cancellationToken)
        {
            if (stateType == V1.StateType)
            {
                return V1.ReadAsync(stream, options, cancellationToken);
            }

            if (stateType == StateType)
            {
                return JsonSerializer.DeserializeAsync<A2PartyImportSagaData>(stream, options, cancellationToken);
            }

            return default;
        }

        /// <summary>
        /// Represents the version 1 format of the saga state data, which is used for migration purposes.
        /// </summary>
        internal sealed class V1
        {
            /// <summary>
            /// The state type identifier for version 1 of the saga state data.
            /// </summary>
            public static string StateType => "A2PartyImportSagaData";

            /// <summary>
            /// Reads the version 1 format of the saga state data from the provided stream and migrates it to the current format.
            /// </summary>
            /// <param name="stream">The stream containing the serialized version 1 saga state data.</param>
            /// <param name="options">The JSON serializer options.</param>
            /// <param name="cancellationToken">The cancellation token.</param>
            /// <returns>The migrated saga state data.</returns>
            public static async ValueTask<A2PartyImportSagaData?> ReadAsync(
                Stream stream,
                JsonSerializerOptions options,
                CancellationToken cancellationToken)
            {
                var data = await JsonSerializer.DeserializeAsync<V1>(stream, options, cancellationToken);
                return data?.Migrate();
            }

            /// <summary>
            /// Gets the unique identifier for the party.
            /// </summary>
            public required Guid PartyUuid { get; init; }

            /// <summary>
            /// Gets the unique identifier of the user associated with this instance.
            /// </summary>
            public required ulong? UserId { get; init; }

            /// <summary>
            /// Gets tracking information for the import job.
            /// </summary>
            public required UpsertPartyTracking Tracking { get; init; }

            /// <summary>
            /// Gets or sets the party being upserted.
            /// </summary>
            public PartyRecord? Party { get; set; }

            /// <summary>
            /// Gets or sets the collection of role assignments grouped by external role source.
            /// </summary>
            public Dictionary<ExternalRoleSource, IReadOnlyList<Assignment>> RoleAssignments { get; set; } = new();

            /// <summary>
            /// Gets or sets the remaining enrichers.
            /// </summary>
            public Queue<string> Enrichers { get; set; } = new();

            private A2PartyImportSagaData Migrate()
            {
                return new A2PartyImportSagaData
                {
                    PartyIdentifier = PartyUuid,
                    Tracking = Tracking,
                    Party = Party,
                    RoleAssignments = RoleAssignments.ToDictionary(
                        static kv => kv.Key,
                        static kv => (PartyExternalRoleAssignmentsUpdate)new PartyExternalRoleAssignmentsUpdate.Full
                        {
                            Assignments = kv.Value.Select(static a => new PartyExternalRoleAssignment
                            {
                                ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = a.ToPartyUuid },
                                ExternalRoleIdentifier = a.Identifier,
                            }).ToImmutableValueArray(),
                        }),
                    Enrichers = Enrichers,
                };
            }

            /// <summary>
            /// Represents a role-assignment to a party.
            /// </summary>
            internal sealed record Assignment
            {
                /// <summary>
                /// Gets the party which to assign the external role to.
                /// </summary>
                public required Guid ToPartyUuid { get; init; }

                /// <summary>
                /// Gets the role identifier.
                /// </summary>
                public required string Identifier { get; init; }
            }
        }
    }

    /// <summary>
    /// Enum representing the initiator of the import saga. This can be used to differentiate between different sources of imports.
    /// </summary>
    [StringEnumConverter]
    public enum ImportSagaInitiator
    {
        /// <summary>
        /// Saga initiated by item appearing on Altinn 2's feed.
        /// </summary>
        [JsonStringEnumMemberName("a2")]
        A2 = 1,
    }

    private enum FlowControl
    {
        Continue,
        Break,
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Party with UUID {PartyUuid} is gone.")]
        public static partial void PartyGone(ILogger logger, Guid partyUuid);

        [LoggerMessage(1, LogLevel.Information, "User with ID {UserId} is gone.")]
        public static partial void ProfileGone(ILogger logger, ulong userId);
    }
}
