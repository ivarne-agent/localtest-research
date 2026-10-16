using System.Diagnostics;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Utils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public partial class A2PartyImportSaga
{
    /// <inheritdoc/>
    public async Task Handle(CompleteA2PartyImportSagaCommand message, CancellationToken cancellationToken)
    {
        if (State.Party is null)
        {
            throw new InvalidOperationException("Party is not set");
        }

        PartyImportHelper.ValidatePartyForUpsert(State.Party, PersistenceFeatureFlag.FromConfiguration(_configuration));
        var partyResult = await _parties.UpsertParty(State.Party, cancellationToken);
        partyResult.EnsureSuccess();
        Debug.Assert(partyResult.Value.PartyUuid.HasValue);
        var partyUuid = partyResult.Value.PartyUuid.Value;

        await _context.Publish(
            new PartyUpdatedEvent
            {
                Party = partyUuid.ToPartyReferenceContract(),
            },
            cancellationToken);

        List<Task> publishTasks = [];
        foreach (var (source, update) in State.RoleAssignments)
        {
            publishTasks.Clear();
            var upsertEvts = _roles.UpsertExternalRolesFromPartyBySource(
                commandId: SagaId,
                partyUuid: partyUuid,
                roleSource: source,
                update: update,
                cancellationToken: cancellationToken);

            await foreach (var upsertEvt in upsertEvts.WithCancellation(cancellationToken))
            {
                var publishTask = upsertEvt.Type switch
                {
                    ExternalRoleAssignmentEvent.EventType.Added => _context.Publish(
                        new ExternalRoleAssignmentAddedEvent
                        {
                            VersionId = upsertEvt.VersionId,
                            Role = upsertEvt.ToPartyExternalRoleReferenceContract(),
                            From = upsertEvt.FromParty.ToPartyReferenceContract(),
                            To = upsertEvt.ToParty.ToPartyReferenceContract(),
                        },
                        cancellationToken),

                    ExternalRoleAssignmentEvent.EventType.Removed => _context.Publish(
                        new ExternalRoleAssignmentRemovedEvent
                        {
                            VersionId = upsertEvt.VersionId,
                            Role = upsertEvt.ToPartyExternalRoleReferenceContract(),
                            From = upsertEvt.FromParty.ToPartyReferenceContract(),
                            To = upsertEvt.ToParty.ToPartyReferenceContract(),
                        },
                        cancellationToken),

                    _ => ThrowHelper.ThrowInvalidOperationException<Task>($"The event type '{upsertEvt.Type}' is not supported."),
                };

                publishTasks.Add(publishTask);
            }

            await Task.WhenAll(publishTasks);
        }

        if (State.Tracking.HasValue)
        {
            await _tracker.TrackProcessedStatus(State.Tracking.JobName, new ImportJobProcessingStatus { ProcessedMax = State.Tracking.Progress }, cancellationToken);
        }

        MarkComplete();
    }
}
