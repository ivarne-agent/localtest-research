using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents an international mailing address expressed as free-form lines.
/// </summary>
public sealed record InternationalFreeFormMailingAddress
{
    /// <summary>
    /// Gets the address lines.
    /// </summary>
    [JsonPropertyName("adresselinje")]
    public ImmutableValueArray<string> AddressLines { get; init; }

    /// <summary>
    /// Gets the postal code.
    /// </summary>
    [JsonPropertyName("postkode")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the city or place name.
    /// </summary>
    [JsonPropertyName("byEllerStedsnavn")]
    public string? CityOrPlaceName { get; init; }

    /// <summary>
    /// Gets the ISO country code for the address.
    /// </summary>
    [JsonPropertyName("landkode")]
    public string? CountryCode { get; init; }
}
