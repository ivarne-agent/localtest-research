namespace Altinn.Register.Persistence.DbArgTypes;

/// <summary>
/// C# counterpart of <c>register.arg_role_party_ref_person_name</c>.
/// </summary>
internal sealed record ArgRolePartyRefPersonName
{
    /// <summary>
    /// Gets the first name of the person.
    /// </summary>
    public required string FirstName { get; init; }

    /// <summary>
    /// Gets the middle name of the person.
    /// </summary>
    public required string? MiddleName { get; init; }

    /// <summary>
    /// Gets the last name of the person.
    /// </summary>
    public required string LastName { get; init; }

    /// <summary>
    /// Gets the short name of the person.
    /// </summary>
    public required string ShortName { get; init; }

    /// <summary>
    /// Gets the display name of the person.
    /// </summary>
    public required string DisplayName { get; init; }
}
