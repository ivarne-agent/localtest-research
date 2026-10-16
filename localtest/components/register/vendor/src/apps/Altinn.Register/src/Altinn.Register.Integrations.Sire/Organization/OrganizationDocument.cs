using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents a company from the SIRE lookup API (v1.0.1).
/// </summary>
internal sealed record OrganizationDocument
{
    /// <summary>
    /// Gets the 9-digit organization identifier.
    /// </summary>
    [JsonPropertyName("identifikator")]
    public string? Identifier { get; init; }

    /// <summary>
    /// Gets the company name.
    /// </summary>
    [JsonPropertyName("selskapetsNavn")]
    public string? CompanyName { get; init; }

    /// <summary>
    /// Gets the organization form (e.g. "AS", "ANS").
    /// </summary>
    [JsonPropertyName("organisasjonsform")]
    public string? OrganizationForm { get; init; }

    /// <summary>
    /// Gets the date the company was established.
    /// </summary>
    [JsonPropertyName("stiftelsesdato")]
    public string? EstablishedDate { get; init; }

    /// <summary>
    /// Gets the date the company was deleted, if applicable.
    /// </summary>
    [JsonPropertyName("slettetdato")]
    public string? DeletedDate { get; init; }

    /// <summary>
    /// Gets the postal address.
    /// </summary>
    [JsonPropertyName("postadresse")]
    public PostalAddress? PostalAddress { get; init; }

    /// <summary>
    /// Gets the business relationships.
    /// </summary>
    [JsonPropertyName("virksomhetsrelasjon")]
    public IReadOnlyList<BusinessRelationship>? BusinessRelationships { get; init; }
}
