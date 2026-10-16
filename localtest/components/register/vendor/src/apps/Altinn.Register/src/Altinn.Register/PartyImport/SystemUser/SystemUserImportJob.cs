using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.UnitOfWork;

namespace Altinn.Register.PartyImport.SystemUser;

/// <summary>
/// A job that imports system users.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class SystemUserImportJob
    : Job
    , IHasJobName<SystemUserImportJob>
{
    /// <inheritdoc/>
    public static string JobName => JobNames.SystemUserImport;

    private readonly SystemUserImportService _service;
    private readonly IUnitOfWorkManager _uow;
    private readonly TimeProvider _timeProvider;
    private readonly IImportJobTracker _tracker;
    private readonly ICommandSender _sender;
    private readonly ILogger<SystemUserImportJob> _logger;
    private readonly JobCleanupHelper _cleanupHelper;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemUserImportJob"/> class.
    /// </summary>
    public SystemUserImportJob(
        SystemUserImportService service,
        IUnitOfWorkManager uow,
        TimeProvider timeProvider,
        IImportJobTracker tracker,
        ICommandSender sender,
        ILogger<SystemUserImportJob> logger,
        JobCleanupHelper cleanupHelper)
    {
        _service = service;
        _uow = uow;
        _timeProvider = timeProvider;
        _tracker = tracker;
        _sender = sender;
        _logger = logger;
        _cleanupHelper = cleanupHelper;
    }

    /// <inheritdoc/>
    protected override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var start = _timeProvider.GetTimestamp();
        Log.StartingSystemUserImport(_logger);

        await using var uow = await _uow.CreateAsync(cancellationToken, activityName: "import system-user");
        cancellationToken = uow.Token;

        var partyPersistence = uow.GetPartyPersistence();

        var progress = await _tracker.GetStatus(JobName, cancellationToken);
        Log.PartyImportInitialProgress(_logger, in progress);
        var startEnqueuedMax = progress.EnqueuedMax;

        State state;
        {
            await using var stateUow = await _uow.CreateAsync(cancellationToken, activityName: "get system-user import state");
            state = await stateUow.GetImportJobStatePersistence().GetState<State>(JobName, cancellationToken) switch
            {
                { HasValue: true } s => s.Value,
                _ => new State(),
            };
        }

        var partyIds = new List<uint>();
        var parties = new List<PartyRecord>();
        var commands = new List<UpsertPartyCommand>();
        var changes = _service.GetStream(state.ContinuationUrl, cancellationToken);
        await foreach (var page in changes.WithCancellation(cancellationToken))
        {
            if (page.Count == 0)
            {
                // Skip empty pages.
                continue;
            }

            partyIds.Clear();
            partyIds.EnsureCapacity(page.Count);

            parties.Clear();
            parties.EnsureCapacity(page.Count);

            commands.Clear();
            commands.EnsureCapacity(page.Count);

            foreach (var systemUser in page)
            {
                if (systemUser.OwnerPartyId.IsUint(out var partyId))
                {
                    partyIds.Add(partyId);
                }
            }

            await foreach (var party in partyPersistence.LookupParties(partyIds: partyIds, include: PartyFieldIncludes.PartyUuid, cancellationToken: cancellationToken))
            {
                parties.Add(party);
            }

            parties.Sort((a, b) => a.PartyId.Value.CompareTo(b.PartyId.Value));

            foreach (var systemUser in page)
            {
                Guid owner;
                var partyLookup = CollectionsMarshal.AsSpan(parties);

                if (systemUser.OwnerPartyId.IsGuid(out owner))
                {
                    Debug.Assert(owner != Guid.Empty);
                }
                else if (systemUser.OwnerPartyId.IsUint(out var partyId)
                    && partyLookup.BinarySearch(new PartyId(partyId)) is int partyIndex and >= 0)
                {
                    var ownerParty = partyLookup[partyIndex];

                    Debug.Assert(ownerParty.PartyUuid.HasValue);
                    owner = ownerParty.PartyUuid.Value;
                }
                else
                {
                    // Either, the owner-party-id is not valid, or it refers to a party that does not exist.
                    await HandleUnimportableSystemUser(systemUser, cancellationToken);
                    continue;
                }

                if (systemUser.Type.IsUnknown)
                {
                    await HandleUnimportableSystemUser(systemUser, cancellationToken);
                    continue;
                }

                var now = _timeProvider.GetUtcNow();
                var party = new SystemUserRecord
                {
                    PartyUuid = systemUser.Id,
                    OwnerUuid = owner,
                    PartyId = FieldValue.Null,
                    ExternalUrn = PartyExternalRefUrn.SystemUserUuid.Create(systemUser.Id),
                    DisplayName = systemUser.Name,
                    PersonIdentifier = FieldValue.Null,
                    OrganizationIdentifier = FieldValue.Null,
                    CreatedAt = now,
                    ModifiedAt = now,
                    UserIds = FieldValue.Unset,
                    Usernames = FieldValue.Unset,
                    IsDeleted = systemUser.IsDeleted,
                    DeletedAt = systemUser.IsDeleted ? now : FieldValue.Null,
                    VersionId = FieldValue.Unset,
                    SystemUserType = systemUser.Type.Value,
                };

                commands.Add(new UpsertPartyCommand
                {
                    Party = party,
                    Tracking = new(JobName, systemUser.SequenceNumber),
                });
            }

            await _sender.Send(commands, cancellationToken);
            await _tracker.TrackQueueStatus(JobName, new() { EnqueuedMax = page[^1].SequenceNumber, SourceMax = page.SequenceMax }, cancellationToken);

            if (page.NextUrl is not null)
            {
                await using var stateUow = await _uow.CreateAsync(cancellationToken, activityName: "persist system-user import state");
                await stateUow.GetImportJobStatePersistence().SetState(JobName, state with { ContinuationUrl = page.NextUrl }, cancellationToken);
                await stateUow.CommitAsync(cancellationToken);
            }
        }

        var duration = _timeProvider.GetElapsedTime(start);
        Log.FinishedSystemUserImport(_logger, duration);

        await _cleanupHelper.MaybeRunCleanup(JobName, startEnqueuedMax, in progress, cancellationToken);
    }

    private async Task HandleUnimportableSystemUser(SystemUserItem systemUser, CancellationToken cancellationToken)
    {
        var failedSystemUserCmd = new ImportSystemUserCommand
        {
            SystemUserId = systemUser.Id,
            Tracking = new(JobName, systemUser.SequenceNumber),
        };

        await _sender.Send(failedSystemUserCmd, cancellationToken);
    }

    private record State
        : IImportJobState<State>
    {
        public static string StateType => $"{JobName}:state@1";

        [JsonPropertyName("continuationUrl")]
        public string? ContinuationUrl { get; init; }
    }

    private record struct PartyId(uint Value)
        : IComparable<PartyRecord>
    {
        public readonly int CompareTo(PartyRecord? other)
            => Value.CompareTo(other?.PartyId.Value ?? 0);
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Starting system-user import.")]
        public static partial void StartingSystemUserImport(ILogger logger);

        [LoggerMessage(1, LogLevel.Information, "Finished system-user import in {Duration}.")]
        public static partial void FinishedSystemUserImport(ILogger logger, TimeSpan duration);

        [LoggerMessage(2, LogLevel.Information, "Initial import progress: EnqueuedMax = {EnqueuedMax}, ProcessedMax = {ProcessedMax}, SourceMax = {SourceMax}.")]
        private static partial void SystemUserImportInitialProgress(ILogger logger, ulong enqueuedMax, ulong? sourceMax, ulong processedMax);

        public static void PartyImportInitialProgress(ILogger logger, in ImportJobStatus status)
            => SystemUserImportInitialProgress(logger, status.EnqueuedMax, status.SourceMax, status.ProcessedMax);
    }
}
