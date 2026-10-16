namespace Altinn.Register.Contracts;

/// <summary>
/// Represents the assignment of an external role to a party.
/// </summary>
public sealed record ExternalRoleAssignment
{
    /// <summary>
    /// Gets the role being assigned.
    /// </summary>
    [JsonPropertyName("role")]
    public required ExternalRoleRef Role { get; init; }

    /// <summary>
    /// Gets the party the role is assigned to.
    /// </summary>
    [JsonPropertyName("to")]
    public required PartyRef ToParty { get; init; }

    /// <summary>
    /// Gets the party the role is assigned from.
    /// </summary>
    [JsonPropertyName("from")]
    public required PartyRef FromParty { get; init; }
}
