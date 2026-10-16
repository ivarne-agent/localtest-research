namespace Altinn.Register.Contracts.V1;

/// <summary>
/// Class representing a party
/// </summary>
public record Party
{
    /// <summary>
    /// Gets or sets the ID of the party
    /// </summary>
    public required int PartyId { get; set; }

    /// <summary>
    /// Gets or sets the UUID of the party
    /// </summary>
    public required Guid PartyUuid { get; set; }

    /// <summary>
    /// Gets or sets the external reference of the party
    /// </summary>
    public PartyExternalRefUrn? ExternalUrn { get; set; }

    /// <summary>
    /// Gets or sets the type of party
    /// </summary>
    public PartyType PartyTypeName { get; set; }

    /// <summary>
    /// Gets the parties org number
    /// </summary>
    public string? OrgNumber { get; set; }

    /// <summary>
    /// Gets the parties ssn
    /// </summary>
    public string? SSN { get; set; }

    /// <summary>
    /// Gets or sets the UnitType
    /// </summary>
    public string? UnitType { get; set; }

    /// <summary>
    /// Gets or sets the Name
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Gets or sets the IsDeleted
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether if the reportee in the list is only there for showing the hierarchy (a parent unit with no access)
    /// </summary>
    public bool OnlyHierarchyElementWithNoAccess { get; set; }

    /// <summary>
    /// Gets or sets the person details for this party (will only be set if the party type is Person)
    /// </summary>
    public Person? Person { get; set; }

    /// <summary>
    /// Gets or sets the organization details for this party (will only be set if the party type is Organization)
    /// </summary>
    public Organization? Organization { get; set; }

    /// <summary>
    /// Gets or sets the value of ChildParties
    /// </summary>
    public IReadOnlyList<Party>? ChildParties { get; set; }

    /// <summary>
    /// Gets or sets the (optional) date and time this party was last changed in Altinn.
    /// </summary>
    public DateTimeOffset? LastChangedInAltinn { get; set; }

    /// <summary>
    /// Gets or sets the (optional) date and time when the entity was last updated in the external register.
    /// </summary>
    public DateTimeOffset? LastChangedInExternalRegister { get; set; }
}
