using Altinn.Register.Contracts;
using Altinn.Register.Core.Utils;

namespace Altinn.Register.Core.Npr;

/// <summary>
/// Represents a guardianship as returned by the NPR API, including the guardian's identifier and their roles in relation to the ward.
/// </summary>
public sealed record NprGuardianship
{
    /// <summary>
    /// Gets the <see cref="PersonIdentifier"/> of the guardian.
    /// </summary>
    public required PersonIdentifier Guardian { get; init; }

    /// <summary>
    /// Gets the set of roles the guardian has in relation to the ward (e.g., "bank-ta-opp-lan-kreditter").
    /// </summary>
    public required ImmutableValueSet<string> Roles { get; init; }
}
