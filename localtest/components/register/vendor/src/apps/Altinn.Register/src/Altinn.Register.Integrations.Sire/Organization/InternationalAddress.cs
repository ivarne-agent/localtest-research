using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents an international address from the SIRE API.
/// </summary>
internal sealed record InternationalAddress
{
    /// <summary>
    /// Gets the address lines.
    /// </summary>
    [JsonPropertyName("adressetekst")]
    public IReadOnlyList<string>? AddressLines { get; init; }

    /// <summary>
    /// Gets the postal code.
    /// </summary>
    [JsonPropertyName("postkode")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the city or place name.
    /// </summary>
    [JsonPropertyName("byEllerStedsnavn")]
    public string? City { get; init; }

    /// <summary>
    /// Gets the ISO country code.
    /// </summary>
    [JsonPropertyName("landkode")]
    public string? CountryCode { get; init; }
}
