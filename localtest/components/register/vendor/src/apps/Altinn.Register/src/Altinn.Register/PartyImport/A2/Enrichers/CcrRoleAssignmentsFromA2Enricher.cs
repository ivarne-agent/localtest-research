using System.Collections.Immutable;
using System.Diagnostics;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.PartyImport.A2;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2.Enrichers;

/// <summary>
/// Enriches organizations with roles from CCR.
/// </summary>
internal sealed partial class CcrRoleAssignmentsFromA2Enricher
    : IA2PartyImportSagaEnrichmentStep
{
    /// <inheritdoc/>
    public static string StepName
        => "ccr-roles";

    /// <inheritdoc/>
    public static bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context)
        => context.PartyIdentifier.TryGetValue(out Guid _)
        && context.Party is OrganizationRecord; // While we use A2 for role enrichment we need to fetch the roles for all organization types

    private readonly IA2PartyImportService _importService;
    private readonly IExternalRoleDefinitionPersistence _roleDefinitions;
    private readonly ILogger<CcrRoleAssignmentsFromA2Enricher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CcrRoleAssignmentsFromA2Enricher"/> class.
    /// </summary>
    public CcrRoleAssignmentsFromA2Enricher(
        IA2PartyImportService importService,
        IExternalRoleDefinitionPersistence roleDefinitions,
        ILogger<CcrRoleAssignmentsFromA2Enricher> logger)
    {
        _importService = importService;
        _roleDefinitions = roleDefinitions;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task Run(A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(context.Party is OrganizationRecord);
        Debug.Assert(context.Party.PartyId.HasValue);
        if (!context.PartyIdentifier.TryGetValue(out Guid partyUuid))
        {
            ThrowHelper.ThrowInvalidOperationException("PartyUserEnricher can only be run when PartyIdentifier is a PartyUuid");
        }

        var assignments = await _importService.GetExternalRoleAssignmentsFrom(context.Party.PartyId.Value, partyUuid, cancellationToken)
            .ToListAsync(cancellationToken);

        if (assignments.Count == 0)
        {
            context.RoleAssignments.Add(ExternalRoleSource.CentralCoordinatingRegister, PartyExternalRoleAssignmentsUpdate.Full.Empty);
            return;
        }

        var mapped = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>(assignments.Count);
        foreach (var assignment in assignments)
        {
            var roleDefinition = await _roleDefinitions.TryGetRoleDefinitionByRoleCode(assignment.RoleCode, cancellationToken);
            if (roleDefinition is null)
            {
                Log.RoleWithRoleCodeNotFound(_logger, assignment.RoleCode, partyUuid, assignment.ToPartyUuid);
                continue;
            }

            if (roleDefinition.Source != ExternalRoleSource.CentralCoordinatingRegister)
            {
                Log.RoleWithWrongSource(_logger, assignment.RoleCode, partyUuid, assignment.ToPartyUuid, roleDefinition.Source, roleDefinition.Identifier);
                continue;
            }

            mapped.Add(new()
            {
                ExternalRoleIdentifier = roleDefinition.Identifier,
                ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = assignment.ToPartyUuid },
            });
        }

        // Note: For idempotency, this should not use Add, but rather overwrite any existing assignments from the same source
        context.RoleAssignments[ExternalRoleSource.CentralCoordinatingRegister] = new PartyExternalRoleAssignmentsUpdate.Full { Assignments = mapped.DrainToImmutableValueArray() };
    }

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Warning, "External role-definition with role code '{RoleCode}' not found. Attempted to add from party '{FromPartyUuid}' to party '{ToPartyUuid}'.")]
        public static partial void RoleWithRoleCodeNotFound(ILogger logger, string roleCode, Guid fromPartyUuid, Guid toPartyUuid);

        [LoggerMessage(1, LogLevel.Warning, "External role-definition with role code '{RoleCode}' found, but with the wrong source '{Source}' and identifier '{Identifier}'. Attempted to add from party '{FromPartyUuid}' to party '{ToPartyUuid}'.")]
        public static partial void RoleWithWrongSource(ILogger logger, string roleCode, Guid fromPartyUuid, Guid toPartyUuid, ExternalRoleSource source, string identifier);
    }
}
