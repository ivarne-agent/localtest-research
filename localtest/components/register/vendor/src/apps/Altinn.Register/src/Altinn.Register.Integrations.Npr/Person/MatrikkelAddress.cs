using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents a cadastral address for a person's current stay address.
/// </summary>
public sealed record MatrikkelAddress
{
    /// <summary>
    /// Gets the dwelling unit number for the cadastral address.
    /// </summary>
    [JsonPropertyName("bruksenhetsnummer")]
    public string? UnitNumber { get; init; }

    /// <summary>
    /// Gets the dwelling unit type for the cadastral address.
    /// </summary>
    [JsonPropertyName("bruksenhetstype")]
    public NonExhaustiveEnum<MatrikkelUnitType>? UnitType { get; init; }

    /// <summary>
    /// Gets the cadastral number of the cadastral address.
    /// </summary>
    [JsonPropertyName("matrikkelnummer")]
    public MatrikkelNumber? MatrikkelNumber { get; init; }

    /// <summary>
    /// Gets the sub-number of the cadastral address, if applicable.
    /// </summary>
    [JsonPropertyName("undernummer")]
    public long? SubNumber { get; init; }

    /// <summary>
    /// Gets the additional name of the cadastral address, if applicable.
    /// </summary>
    [JsonPropertyName("adressetilleggsnavn")]
    public string? AddressAdditionalName { get; init; }

    /// <summary>
    /// Gets the postal area of the cadastral address.
    /// </summary>
    [JsonPropertyName("poststed")]
    public PostalArea? PostalArea { get; init; }

    /// <summary>
    /// Gets the care-of address name of the cadastral address, if applicable.
    /// </summary>
    [JsonPropertyName("coAdressenavn")]
    public string? CareOfAddressName { get; init; }
}
