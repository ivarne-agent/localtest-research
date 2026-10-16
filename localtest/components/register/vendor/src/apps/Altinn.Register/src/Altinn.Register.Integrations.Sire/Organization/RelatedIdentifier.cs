using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents an identifier for a related party in the SIRE API.
/// </summary>
internal sealed record RelatedIdentifier
{
    /// <summary>
    /// Gets the identifier type (e.g. "taxIdentificationNumber").
    /// </summary>
    [JsonPropertyName("identifikatortype")]
    public string? IdentifierType { get; init; }

    /// <summary>
    /// Gets the identifier value.
    /// </summary>
    [JsonPropertyName("verdi")]
    public string? Value { get; init; }

    /// <summary>
    /// Gets the country code.
    /// </summary>
    [JsonPropertyName("landkode")]
    public string? CountryCode { get; init; }
}
