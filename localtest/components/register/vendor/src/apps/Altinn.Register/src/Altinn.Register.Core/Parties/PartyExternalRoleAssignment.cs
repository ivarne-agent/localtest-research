namespace Altinn.Register.Core.Parties;

/// <summary>
/// Represents an assignment of an external role to a party, where the party is identified by a reference and the role is identified by its external role identifier.
/// </summary>
public sealed record PartyExternalRoleAssignment
{
    /// <summary>
    /// Gets the reference to the party to which the external role is assigned.
    /// </summary>
    public required PartyExternalRoleAssignmentPartyRef ToParty { get; init; }

    /// <summary>
    /// Gets the identifier of the external role to assign to the party.
    /// </summary>
    public required string ExternalRoleIdentifier { get; init; }
}
