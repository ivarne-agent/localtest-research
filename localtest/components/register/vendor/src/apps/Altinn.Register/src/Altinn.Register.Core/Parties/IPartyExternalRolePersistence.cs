using Altinn.Register.Contracts;
using Altinn.Register.Contracts.ExternalRoles;
using Altinn.Register.Core.Parties.Records;
using Altinn.Register.Core.Utils;

namespace Altinn.Register.Core.Parties;

/// <summary>
/// Persistence service for party roles.
/// </summary>
public interface IPartyExternalRolePersistence
{
    /// <summary>
    /// Gets all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/> is <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid"><see cref="PartyExternalRoleAssignmentRecord.FromParty"/>.</param>
    /// <param name="include">Data/fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> containing all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/>
    /// is <paramref name="partyUuid"/>.
    /// </returns>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/> is <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid"><see cref="PartyExternalRoleAssignmentRecord.FromParty"/>.</param>
    /// <param name="role">The role for which to get role-assignments.</param>
    /// <param name="include">Data/fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> containing all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/>
    /// is <paramref name="partyUuid"/>.
    /// </returns>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/> is <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid"><see cref="PartyExternalRoleAssignmentRecord.FromParty"/>.</param>
    /// <param name="roles">The roles for which to get role-assignments.</param>
    /// <param name="include">Data/fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> containing all roles where <see cref="PartyExternalRoleAssignmentRecord.FromParty"/>
    /// is <paramref name="partyUuid"/>.
    /// </returns>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsFromParty(
        Guid partyUuid,
        IReadOnlyList<ExternalRoleReference> roles,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles where <see cref="PartyExternalRoleAssignmentRecord.ToParty"/> is <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid"><see cref="PartyExternalRoleAssignmentRecord.ToParty"/>.</param>
    /// <param name="include">Data/fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> containing all roles where <see cref="PartyExternalRoleAssignmentRecord.ToParty"/>
    /// is <paramref name="partyUuid"/>.
    /// </returns>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsToParty(
        Guid partyUuid,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all roles where <see cref="PartyExternalRoleAssignmentRecord.ToParty"/> is <paramref name="partyUuid"/>.
    /// </summary>
    /// <param name="partyUuid"><see cref="PartyExternalRoleAssignmentRecord.ToParty"/>.</param>
    /// <param name="role">The role for which to get role-assignments.</param>
    /// <param name="include">Data/fields to include.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> containing all roles where <see cref="PartyExternalRoleAssignmentRecord.ToParty"/>
    /// is <paramref name="partyUuid"/>.
    /// </returns>
    public IAsyncEnumerable<PartyExternalRoleAssignmentRecord> GetExternalRoleAssignmentsToParty(
        Guid partyUuid,
        ExternalRoleReference role,
        PartyExternalRoleAssignmentFieldIncludes include = PartyExternalRoleAssignmentFieldIncludes.RoleAssignment,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts all external roles from <paramref name="partyUuid"/> by <paramref name="roleSource"/>.
    /// </summary>
    /// <param name="commandId">The command ID.</param>
    /// <param name="partyUuid">The party to upsert external roles from.</param>
    /// <param name="roleSource">The source of the external roles.</param>
    /// <param name="update">The update to apply to the external roles.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A set of changes performed by this upsert.</returns>
    public IAsyncSideEffectEnumerable<ExternalRoleAssignmentEvent> UpsertExternalRolesFromPartyBySource(
        Guid commandId,
        Guid partyUuid,
        ExternalRoleSource roleSource,
        PartyExternalRoleAssignmentsUpdate update,
        CancellationToken cancellationToken = default);
}
