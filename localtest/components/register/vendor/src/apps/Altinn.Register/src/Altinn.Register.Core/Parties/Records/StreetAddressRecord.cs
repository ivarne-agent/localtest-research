namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents a street address.
/// </summary>
public record StreetAddressRecord
{
    /// <summary>
    /// Gets the municipal number of the street address.
    /// </summary>
    public string? MunicipalNumber { get; init; }

    /// <summary>
    /// Gets the municipal name of the street address.
    /// </summary>
    public string? MunicipalName { get; init; }

    /// <summary>
    /// Gets the street name of the street address.
    /// </summary>
    public string? StreetName { get; init; }

    /// <summary>
    /// Gets the house number of the street address.
    /// </summary>
    public string? HouseNumber { get; init; }

    /// <summary>
    /// Gets the house letter of the street address.
    /// </summary>
    public string? HouseLetter { get; init; }

    /// <summary>
    /// Gets the postal code of the mailing address.
    /// </summary>
    public string? PostalCode { get; init; }

    /// <summary>
    /// Gets the city of the mailing address.
    /// </summary>
    public string? City { get; init; }
}
