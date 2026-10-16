using System.Buffers;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Contracts.Parties;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Utils;
using CommunityToolkit.Diagnostics;
using MassTransit;

namespace Altinn.Register.PartyImport.Ccr;

/// <summary>
/// Consumes <see cref="ImportCcrXmlCommand"/> messages produced by the
/// <see cref="CcrImportJob"/> and applies each organization update through the
/// <see cref="CcrService"/>.
/// </summary>
public sealed partial class ImportCcrXmlConsumer
    : IConsumer<ImportCcrXmlCommand>
{
    private readonly CcrService _ccrService;
    private readonly ILogger<ImportCcrXmlConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportCcrXmlConsumer"/> class.
    /// </summary>
    public ImportCcrXmlConsumer(
        CcrService ccrService,
        ILogger<ImportCcrXmlConsumer> logger)
    {
        _ccrService = ccrService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ImportCcrXmlCommand> context)
    {
        var message = context.Message;

        if (message.OrganizationIdentifier is { } orgId)
        {
            Log.ConsumingCcrUpdate(_logger, orgId);
        }
        else
        {
            Log.ConsumingCcrUpdateWithoutOrganizationIdentifier(_logger);
        }

        var result = await _ccrService.UpdateFromCcr(
            commandId: message.CommandId,
            input: new ReadOnlySequence<byte>(message.Document),
            federate: message.Federate ?? true,
            cancellationToken: context.CancellationToken);

        await context.PublishBatch(
            result.UpdatedOrganizationPartyUuids.Select(
                uuid => new PartyUpdatedEvent { Party = uuid.ToPartyReferenceContract() }),
            context.CancellationToken);

        if (result.RoleAssignmentEvents.Length > 0)
        {
            await PublishRoleUpdates(context, result.RoleAssignmentEvents, context.CancellationToken);
        }

        await context.Publish(new CcrXmlImportCompletedEvent { Success = true }, context.CancellationToken);
    }

    private static async Task PublishRoleUpdates(
        ConsumeContext context,
        ImmutableValueArray<ExternalRoleAssignmentEvent> roleEvents,
        CancellationToken cancellationToken)
    {
        var publishTasks = new List<Task>(roleEvents.Length);

        foreach (var upsertEvt in roleEvents)
        {
            var publishTask = upsertEvt.Type switch
            {
                ExternalRoleAssignmentEvent.EventType.Added => context.Publish(
                    new ExternalRoleAssignmentAddedEvent
                    {
                        VersionId = upsertEvt.VersionId,
                        Role = upsertEvt.ToPartyExternalRoleReferenceContract(),
                        From = upsertEvt.FromParty.ToPartyReferenceContract(),
                        To = upsertEvt.ToParty.ToPartyReferenceContract(),
                    },
                    cancellationToken),

                ExternalRoleAssignmentEvent.EventType.Removed => context.Publish(
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

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Information, "Consuming CCR update for organization {OrganizationIdentifier}.")]
        public static partial void ConsumingCcrUpdate(ILogger logger, OrganizationIdentifier organizationIdentifier);

        [LoggerMessage(1, LogLevel.Information, "Consuming CCR update without organization identifier.")]
        public static partial void ConsumingCcrUpdateWithoutOrganizationIdentifier(ILogger logger);
    }
}
