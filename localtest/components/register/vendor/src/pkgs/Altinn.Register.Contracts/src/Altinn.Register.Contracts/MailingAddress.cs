namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a mailing address.
/// </summary>
public record MailingAddress
{
    /// <summary>
    /// Gets the address part of the mailing address.
    /// </summary>
    [JsonPropertyName("address")]
    public string? Address { get; init; }

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
