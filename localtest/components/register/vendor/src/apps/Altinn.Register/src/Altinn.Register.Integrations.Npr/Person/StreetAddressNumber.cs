using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a street address number.
/// </summary>
public sealed record StreetAddressNumber
{
    /// <summary>
    /// Gets the house number.
    /// </summary>
    [JsonPropertyName("husnummer")]
    public string? HouseNumber { get; init; }

    /// <summary>
    /// Gets the house letter.
    /// </summary>
    [JsonPropertyName("husbokstav")]
    public string? HouseLetter { get; init; }
}
