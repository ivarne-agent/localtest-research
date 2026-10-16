using System.Collections.Immutable;
using System.Diagnostics;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Sire;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport.A2.Enrichers;

/// <summary>
/// An enricher that enriches organization parties with information from SIRE.
/// </summary>
internal sealed partial class SireEnricher
    : IA2PartyImportSagaEnrichmentStep
{
    /// <inheritdoc/>
    public static string StepName => "sire";

    /// <inheritdoc/>
    public static bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context)
        => context.Party is OrganizationRecord { Source.Value: OrganizationSource.RegisteredWithSkatteetaten };

    private readonly ISireClient _sireClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="SireEnricher"/> class.
    /// </summary>
    public SireEnricher(ISireClient sireClient)
    {
        _sireClient = sireClient;
    }

    /// <inheritdoc/>
    public async Task Run(A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(context.Party is OrganizationRecord);
        Debug.Assert(context.Party is { OrganizationIdentifier.HasValue: true });

        var result = await _sireClient.GetOrganization(context.Party.OrganizationIdentifier.Value, cancellationToken);

        if (result.IsProblem && result.Problem.ErrorCode == Problems.OrganizationNotFound.ErrorCode)
        {
            return;
        }

        result.EnsureSuccess();
        var organization = result.Value;

        context.Party = ((OrganizationRecord)context.Party) with
        {
            DisplayName = organization.Name,
            UnitType = organization.UnitType,
            UnitStatus = organization.UnitStatus,
            IsDeleted = organization.DeletedAt is not null,
            DeletedAt = FieldValue.From(organization.DeletedAt),
            Source = OrganizationSource.RegisteredWithSkatteetaten,
            MailingAddress = organization.MailingAddress,
            TelephoneNumber = FieldValue.Null,
            MobileNumber = FieldValue.Null,
            FaxNumber = FieldValue.Null,
            EmailAddress = FieldValue.Null,
            InternetAddress = FieldValue.Null,
            BusinessAddress = FieldValue.Null,
        };

        MapRoleAssignments(context, organization);
    }

    /// <summary>
    /// Maps SIRE business relationships to role assignments using the typed
    /// </summary>
    private static void MapRoleAssignments(
        A2PartyImportSagaEnrichmentRunContext context,
        SireOrganization organization)
    {
        var relationships = organization.BusinessRelationships;

        if (relationships is not { Length: > 0 })
        {
            // SIRE role identifiers are drawn from the CCR role catalog, so assignments
            // are stored under ExternalRoleSource.CentralCoordinatingRegister. The party's
            // OrganizationSource = RegisteredWithSkatteetaten preserves provenance.
            context.RoleAssignments[ExternalRoleSource.CentralCoordinatingRegister] = PartyExternalRoleAssignmentsUpdate.Full.Empty;
            return;
        }

        var mapped = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>(relationships.Length);
        foreach (var rel in relationships)
        {
            PartyExternalRoleAssignmentPartyRef toParty;
            if (rel.RelatedPersonIdentifier is { } personId)
            {
                toParty = new PartyExternalRoleAssignmentPartyRef.Person
                {
                    PersonIdentifier = personId,
                    Name = null,
                    MailingAddress = null,
                };
            }
            else if (rel.RelatedOrganizationIdentifier is { } orgId)
            {
                toParty = new PartyExternalRoleAssignmentPartyRef.Organization
                {
                    OrganizationIdentifier = orgId,
                };
            }
            else
            {
                // Unreachable: OrganizationDocumentValidator rejects any SIRE relationship
                // that doesn't carry exactly one of RelatedPersonIdentifier or
                // RelatedOrganizationIdentifier. Asserting here turns drift in that contract
                // into a loud failure instead of a silent data loss. The generic overload
                // is used so the compiler accepts `toParty` as definitely assigned below.
                toParty = ThrowHelper.ThrowInvalidOperationException<PartyExternalRoleAssignmentPartyRef>(
                    "SireBusinessRelationship must have either RelatedPersonIdentifier or RelatedOrganizationIdentifier set.");
            }

            mapped.Add(new()
            {
                ExternalRoleIdentifier = rel.RoleIdentifier,
                ToParty = toParty,
            });
        }

        // Note: For idempotency, this overwrites any existing assignments from the same source.
        // Bucket is CentralCoordinatingRegister because SIRE role identifiers come from the CCR catalog.
        context.RoleAssignments[ExternalRoleSource.CentralCoordinatingRegister] = new PartyExternalRoleAssignmentsUpdate.Full { Assignments = mapped.DrainToImmutableValueArray() };
    }
}
