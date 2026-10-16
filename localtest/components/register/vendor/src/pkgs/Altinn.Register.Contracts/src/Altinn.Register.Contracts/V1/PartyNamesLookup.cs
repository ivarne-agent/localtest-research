namespace Altinn.Register.Contracts.V1;

/// <summary>
/// Represents a list of lookup criteria when looking for a Party.
/// </summary>
public class PartyNamesLookup
{
    /// <summary>
    /// Gets or sets the list of identifiers for the parties to look for.
    /// </summary>
    [JsonPropertyName("parties")]
    public IReadOnlyList<PartyLookup>? Parties { get; set; }
}
