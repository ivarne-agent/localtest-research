namespace Altinn.Register.Contracts.V1;

/// <summary>
/// Represents a list of party names for each corresponding identifier
/// </summary>
public class PartyNamesLookupResult
{
    /// <summary>
    /// Gets or sets the list of identifiers for the parties to look for.
    /// </summary>
    [JsonPropertyName("partyNames")]
    public IReadOnlyList<PartyName>? PartyNames { get; set; }
}
