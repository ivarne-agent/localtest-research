namespace Altinn.Register.Persistence.DbArgTypes;

/// <summary>
/// C# counterpart of <c>register.arg_role_assignment</c>.
/// </summary>
internal sealed record ArgRoleAssignment
{
    /// <summary>
    /// Gets the reference to the party that the role is assigned to.
    /// </summary>
    public required ArgRolePartyRef ToParty { get; init; }

    /// <summary>
    /// Gets the identifier of the role that is assigned.
    /// </summary>
    public required string Identifier { get; init; }
}
