using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Utils;

namespace Altinn.Register.Localtest.Persistence;

/// <summary>Read-only <see cref="IPartyExternalRolePersistence"/> over the <see cref="RegisterStore"/>.</summary>
public sealed class InMemoryPartyExternalRolePersistence(RegisterStoreProvider provider)
    : IPartyExternalRolePersistence
{
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(Guid partyUuid, PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment, CancellationToken cancellationToken = default)
        => Query(a => a.FromParty == partyUuid, include);

    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(Guid partyUuid, ExternalRoleReference role, PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment, CancellationToken cancellationToken = default)
        => Query(a => a.FromParty == partyUuid && Matches(a, role), include);

    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(Guid partyUuid, IReadOnlyList<ExternalRoleReference> roles, PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment, CancellationToken cancellationToken = default)
        => Query(a => a.FromParty == partyUuid && roles.Any(r => Matches(a, r)), include);

    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsToParty(Guid partyUuid, PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment, CancellationToken cancellationToken = default)
        => Query(a => a.ToParty == partyUuid, include);

    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsToParty(Guid partyUuid, ExternalRoleReference role, PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment, CancellationToken cancellationToken = default)
        => Query(a => a.ToParty == partyUuid && Matches(a, role), include);

    public IAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent> UpsertExternalRolesFromPartyBySource(Guid commandId, Guid partyUuid, ExternalRoleSource roleSource, PartyExternalRoleAssignmentsUpdate update, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The localtest register is read-only; change testData.json and POST register/api/localtest/apps/refresh.");

    private static bool Matches(RoleAssignment assignment, ExternalRoleReference role)
        => assignment.Source == role.Source && string.Equals(assignment.Identifier, role.Identifier, StringComparison.Ordinal);

    private IAsyncEnumerable<PartyExternalRoleAssignmentRecord> Query(Func<RoleAssignment, bool> predicate, PartyExternalRoleAssignmentFieldIncludes include)
    {
        var store = provider.Current;
        return store.Assignments.Where(predicate).Select(a => ToRecord(store, a, include)).ToAsyncEnumerable();
    }

    private static PartyExternalRoleAssignmentRecord ToRecord(RegisterStore store, RoleAssignment a, PartyExternalRoleAssignmentFieldIncludes include)
    {
        store.RoleDefinitions.TryGetRoleDefinition(a.Source, a.Identifier, out var definition);
        return new PartyExternalRoleAssignmentRecord
        {
            Source = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleSource) ? a.Source : FieldValue.Unset,
            Identifier = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleIdentifier) ? a.Identifier : FieldValue.Unset,
            FromParty = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleFromParty) ? a.FromParty : FieldValue.Unset,
            ToParty = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleToParty) ? a.ToParty : FieldValue.Unset,
            Name = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleDefinitionName) && definition is not null ? definition.Name : FieldValue.Unset,
            Description = include.HasFlag(PartyExternalRoleAssignmentFieldIncludes.RoleDefinitionDescription) && definition is not null ? definition.Description : FieldValue.Unset,
        };
    }
}
