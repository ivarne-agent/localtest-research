using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a cadastral number for a person's current stay address.
/// </summary>
public sealed record MatrikkelNumber
{
    /// <summary>
    /// Gets the municipality number of the cadastral number.
    /// </summary>
    [JsonPropertyName("kommunenummer")]
    public string? MunicipalityNumber { get; init; }

    /// <summary>
    /// Gets the property number of the cadastral number.
    /// </summary>
    [JsonPropertyName("gaardsnummer")]
    public long? PropertyNumber { get; init; }

    /// <summary>
    /// Gets the section number of the cadastral number.
    /// </summary>
    [JsonPropertyName("bruksnummer")]
    public long? TitleNumber { get; init; }

    /// <summary>
    /// Gets the sub-section number of the cadastral number.
    /// </summary>
    [JsonPropertyName("festenummer")]
    public long? LeaseNumber { get; init; }
}
