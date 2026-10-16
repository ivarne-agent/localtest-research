using System.Collections.Immutable;
using System.Diagnostics;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Core.Errors;
using Altinn.Register.Core.Npr;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.PartyImport.A2.Enrichers;

/// <summary>
/// An enricher that enriches parties with information from NPR.
/// </summary>
internal sealed class NprEnricher
    : IA2PartyImportSagaEnrichmentStep
{
    /// <inheritdoc/>
    public static string StepName
        => "npr";

    /// <inheritdoc/>
    public static bool IsEnabled(IConfiguration configuration)
        => true;

    /// <inheritdoc/>
    public static bool CanEnrich(A2PartyImportSagaEnrichmentCheckContext context)
        => context.Party is PersonRecord { Source.Value: PersonSource.NationalPopulationRegister };

    private readonly IPartyPersistence _parties;
    private readonly INprClient _nprClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="NprEnricher"/> class.
    /// </summary>
    public NprEnricher(IPartyPersistence parties, INprClient nprClient)
    {
        _parties = parties;
        _nprClient = nprClient;
    }

    /// <inheritdoc/>
    public async Task Run(A2PartyImportSagaEnrichmentRunContext context, CancellationToken cancellationToken)
    {
        Debug.Assert(context.Party is { PartyType.HasValue: true, PartyType.Value: PartyRecordType.Person });
        Debug.Assert(context.Party is { PersonIdentifier.HasValue: true });
        Debug.Assert(context.Party is PersonRecord);

        var result = await _nprClient.GetPerson(context.Party.PersonIdentifier.Value, cancellationToken);
        if (result.IsProblem && result.Problem.ErrorCode == Problems.PersonNotFound.ErrorCode)
        {
            // All environments contains persons that are not in NPR. These can be skipped.
            return;
        }

        result.EnsureSuccess();
        var nprPerson = result.Value;

        if (nprPerson.PersonIdentifier != context.Party.PersonIdentifier.Value)
        {
            // This happens if a person has ever changed their person-identifier and we used the old one to look them up.
            // NPR will return the correct person with the current person-identifier. But since we have a separate party
            // for each person-identifier, we need to save the data we get on the old party here.
            nprPerson = nprPerson with { PersonIdentifier = context.Party.PersonIdentifier.Value };
        }

        context.Party = ((PersonRecord)context.Party) with
        {
            PersonIdentifier = nprPerson.PersonIdentifier,
            DisplayName = nprPerson.DisplayName,
            FirstName = nprPerson.FirstName,
            MiddleName = nprPerson.MiddleName,
            LastName = nprPerson.LastName,
            ShortName = nprPerson.ShortName,
            Address = nprPerson.Address,
            MailingAddress = nprPerson.MailingAddress,
            DateOfBirth = FieldValue.From(nprPerson.DateOfBirth),
            DateOfDeath = FieldValue.From(nprPerson.DateOfDeath),
        };

        var guardianships = nprPerson.Guardians;
        var roleCount = guardianships.Sum(static g => g.Roles.Length);

        var guardians = await _parties.LookupParties(
            personIdentifiers: [.. guardianships.Select(static g => g.Guardian)],
            include: PartyFieldIncludes.PartyUuid | PartyFieldIncludes.PartyPersonIdentifier,
            cancellationToken: cancellationToken)
            .ToDictionaryAsync(
                keySelector: static p => p.PersonIdentifier.Value!,
                elementSelector: static p => p.PartyUuid.Value,
                cancellationToken: cancellationToken);

        var mapped = ImmutableArray.CreateBuilder<PartyExternalRoleAssignment>(roleCount);
        foreach (var guardianship in guardianships)
        {
            if (!guardians.TryGetValue(guardianship.Guardian, out var guardianUuid))
            {
                throw new InvalidOperationException($"Guardian not found in party database.");
            }

            foreach (var role in guardianship.Roles)
            {
                mapped.Add(new()
                {
                    ExternalRoleIdentifier = role,

                    // TODO: Replace with PartyExternalRoleAssignmentPartyRef.Person once the database logic is in place
                    ToParty = new PartyExternalRoleAssignmentPartyRef.PartyUuid { Uuid = guardianUuid },
                });
            }
        }

        // Note: For idempotency, this should not use Add, but rather overwrite any existing assignments from the same source
        context.RoleAssignments[ExternalRoleSource.CivilRightsAuthority] = new PartyExternalRoleAssignmentsUpdate.Full { Assignments = mapped.DrainToImmutableValueArray() };
    }
}
