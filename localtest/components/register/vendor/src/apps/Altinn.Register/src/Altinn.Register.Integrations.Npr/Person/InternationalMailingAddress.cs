using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a structured international mailing address.
/// </summary>
public sealed record InternationalMailingAddress
{
    /// <summary>
    /// Gets the care-of name for the address.
    /// </summary>
    [JsonPropertyName("coAdressenavn")]
    public string? CareOfAddressName { get; init; }

    /// <summary>
    /// Gets the primary address name.
    /// </summary>
    [JsonPropertyName("adressenavn")]
    public string? AddressName { get; init; }

    /// <summary>
    /// Gets the building designation.
    /// </summary>
    [JsonPropertyName("bygning")]
    public string? Building { get; init; }

    /// <summary>
    /// Gets the floor number.
    /// </summary>
    [JsonPropertyName("etasjenummer")]
    public string? FloorNumber { get; init; }

    /// <summary>
    /// Gets the unit name.
    /// </summary>
    [JsonPropertyName("boenhet")]
    public string? UnitName { get; init; }

    /// <summary>
    /// Gets the postbox.
    /// </summary>
    [JsonPropertyName("postboks")]
    public string? PostBox { get; init; }

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
    /// Gets the district name.
    /// </summary>
    [JsonPropertyName("distriktsnavn")]
    public string? DistrictName { get; init; }

    /// <summary>
    /// Gets the region.
    /// </summary>
    [JsonPropertyName("region")]
    public string? Region { get; init; }

    /// <summary>
    /// Gets the ISO country code for the address.
    /// </summary>
    [JsonPropertyName("landkode")]
    public string? CountryCode { get; init; }
}
