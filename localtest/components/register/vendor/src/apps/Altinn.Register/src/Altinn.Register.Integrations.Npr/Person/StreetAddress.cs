using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a street address.
/// </summary>
public sealed record StreetAddress
{
    /// <summary>
    /// Gets the municipality number for the address.
    /// </summary>
    [JsonPropertyName("kommunenummer")]
    public string? MunicipalNumber { get; init; }

    /// <summary>
    /// Gets the street name.
    /// </summary>
    [JsonPropertyName("adressenavn")]
    public string? StreetName { get; init; }

    /// <summary>
    /// Gets the street number.
    /// </summary>
    [JsonPropertyName("adressenummer")]
    public StreetAddressNumber? StreetNumber { get; init; }

    /// <summary>
    /// Gets the postal place for the address.
    /// </summary>
    [JsonPropertyName("poststed")]
    public PostalArea? PostalArea { get; init; }

    /// <summary>
    /// Gets the care-of address name when the street address is registered as a care-of address.
    /// </summary>
    [JsonPropertyName("coAdressenavn")]
    public string? CareOfAddressName { get; init; }

    /// <summary>
    /// Gets the additional address name when the street address is registered with an additional address name.
    /// </summary>
    [JsonPropertyName("adressetilleggsnavn")]
    public string? AdditionalAddressName { get; init; }
}
