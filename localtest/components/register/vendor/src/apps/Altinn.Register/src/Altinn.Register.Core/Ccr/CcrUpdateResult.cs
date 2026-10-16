using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.Core.Ccr;

/// <summary>
/// Represents the result of a CCR update operation, including updated organization party
/// identifiers and related role assignment events.
/// </summary>
public sealed record class CcrUpdateResult
{
    /// <summary>
    /// Gets the collection of organization party unique identifiers that have been updated.
    /// </summary>
    public ImmutableValueArray<Guid> UpdatedOrganizationPartyUuids { get; init; }

    /// <summary>
    /// Gets the collection of external role assignment events associated with this instance.
    /// </summary>
    public ImmutableValueArray<ExternalRoleAssignmentEvent> RoleAssignmentEvents { get; init; }
}
