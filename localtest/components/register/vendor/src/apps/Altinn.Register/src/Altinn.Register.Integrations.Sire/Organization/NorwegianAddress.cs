using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents a Norwegian address from the SIRE API.
/// </summary>
internal sealed record NorwegianAddress
{
    /// <summary>
    /// Gets the address lines.
    /// </summary>
    [JsonPropertyName("adressetekst")]
    public IReadOnlyList<string>? AddressLines { get; init; }

    /// <summary>
    /// Gets the postal code.
    /// </summary>
    [JsonPropertyName("postnummer")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the postal place name.
    /// </summary>
    [JsonPropertyName("poststedsnavn")]
    public string? City { get; init; }
}
