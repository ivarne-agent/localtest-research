namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Represents an external role assignment from a party.
/// </summary>
public sealed record A2PartyExternalRoleAssignment
    : IEquatable<A2PartyExternalRoleAssignment>
{
    /// <summary>
    /// Gets the party uuid of the receiving party.
    /// </summary>
    public required Guid ToPartyUuid { get; init; }

    /// <summary>
    /// Gets the role code.
    /// </summary>
    public required string RoleCode { get; init; }

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(
            ToPartyUuid,
            string.GetHashCode(RoleCode, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public bool Equals(A2PartyExternalRoleAssignment? other)
        => other is not null
            && ToPartyUuid == other.ToPartyUuid
            && string.Equals(RoleCode, other.RoleCode, StringComparison.OrdinalIgnoreCase);
}
