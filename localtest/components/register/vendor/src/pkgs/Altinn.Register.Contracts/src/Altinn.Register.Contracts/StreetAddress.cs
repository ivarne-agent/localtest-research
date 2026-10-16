namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a street address.
/// </summary>
public record StreetAddress
{
    /// <summary>
    /// Gets the municipal number of the street address.
    /// </summary>
    [JsonPropertyName("municipalNumber")]
    public string? MunicipalNumber { get; init; }

    /// <summary>
    /// Gets the municipal name of the street address.
    /// </summary>
    [JsonPropertyName("municipalName")]
    public string? MunicipalName { get; init; }

    /// <summary>
    /// Gets the street name of the street address.
    /// </summary>
    [JsonPropertyName("streetName")]
    public string? StreetName { get; init; }

    /// <summary>
    /// Gets the house number of the street address.
    /// </summary>
    [JsonPropertyName("houseNumber")]
    public string? HouseNumber { get; init; }

    /// <summary>
    /// Gets the house letter of the street address.
    /// </summary>
    [JsonPropertyName("houseLetter")]
    public string? HouseLetter { get; init; }

    /// <summary>
    /// Gets the postal code of the mailing address.
    /// </summary>
    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the city of the mailing address.
    /// </summary>
    [JsonPropertyName("city")]
    public string? City { get; init; }
}
