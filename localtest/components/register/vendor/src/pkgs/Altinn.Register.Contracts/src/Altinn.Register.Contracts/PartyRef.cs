using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// A reference to the owner of a party.
/// </summary>
public sealed record PartyRef
{
    private readonly Guid _uuid;
    private readonly PartyUrn.PartyUuid _urn = null!;

    /// <summary>
    /// Gets the UUID of the party.
    /// </summary>
    [JsonPropertyName("partyUuid")]
    public required Guid Uuid
    {
        get => _uuid;
        init
        {
            Guard.IsNotDefault(value);
            _uuid = value;
            _urn = PartyUrn.PartyUuid.Create(value);
        }
    }

    /// <summary>
    /// Gets the canonical URN of the party.
    /// </summary>
    [JsonPropertyName("urn")]
    public PartyUrn.PartyUuid Urn
        => _urn;
}
