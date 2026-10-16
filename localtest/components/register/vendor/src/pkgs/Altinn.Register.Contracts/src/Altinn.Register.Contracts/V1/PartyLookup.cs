namespace Altinn.Register.Contracts.V1;

/// <summary>
/// Represents a lookup criteria when looking for a Party. Only one of the properties can be used at a time.
/// If none or more than one property have a value the lookup operation will respond with bad request.
/// </summary>
public record PartyLookup
{
    /// <summary>
    /// Gets or sets the social security number of the party to look for.
    /// </summary>
    public string? Ssn { get; set; }

    /// <summary>
    /// Gets or sets the organization number of the party to look for.
    /// </summary>
    public string? OrgNo { get; set; }
}
