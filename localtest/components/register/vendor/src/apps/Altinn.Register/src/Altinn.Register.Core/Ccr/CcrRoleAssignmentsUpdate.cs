using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents an update to a role assignment in the context of the Norwegian Central Coordinating Register for Legal Entities (CCR).
/// </summary>
/// <remarks>
/// Equality is order-insensitive: two instances are considered equal when their role-assignment collections contain the same items
/// with the same multiplicities, regardless of order.
/// </remarks>
public sealed record CcrRoleAssignmentsUpdate
{
    /// <summary>
    /// Gets the collection of role assignments associated with the current entity.
    /// To be added or updated role assignments should be included in this collection.
    /// For a full update, this collection should contain all role assignments that should exist after the update.
    /// Contains both personal role assignments (Rolle) and connection role assignments (Knytning),
    /// distinguished by the RoleAssignmentType property of each CcrRoleAssignment.
    /// </summary>
    public required ImmutableValueArray<CcrRoleAssignment> RoleAssignments { get; init; }

    /// <summary>
    /// Gets the collection of role assignments to be removed.
    /// RD or KD fratraadt
    /// </summary>
    public required ImmutableValueArray<CcrRoleAssignment> RemoveRoleAssignments { get; init; }

    /// <summary>
    /// Gets the collection of SAMU role assignments to be removed in bulk operations.
    /// STYR: LEDE, NEST, MEDL, OBS.
    /// DELT: DTSO, DTPR
    /// SIGN: SIGN, SIFE, SIHV
    /// PROK: PROK, KENK, KGRL
    /// </summary>
    public required ImmutableValueArray<CcrRoleAssignment> BulkRemoveRoleAssignments { get; init; }
}
