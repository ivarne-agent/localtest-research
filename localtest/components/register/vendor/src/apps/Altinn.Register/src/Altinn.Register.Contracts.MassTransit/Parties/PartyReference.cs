using System.Diagnostics.CodeAnalysis;

namespace Altinn.Register.Contracts.Parties;

/// <summary>
/// Represents a reference to a party.
/// </summary>
public sealed record PartyReference
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartyReference"/> class.
    /// </summary>
    /// <param name="partyUuid">The party UUID.</param>
    [SetsRequiredMembers]
    public PartyReference(Guid partyUuid)
    {
        PartyUuid = partyUuid;
    }

    /// <summary>
    /// Gets the UUID of the party.
    /// </summary>
    public required Guid PartyUuid { get; init; }
}
