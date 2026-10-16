using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Organization;

/// <summary>
/// Represents a business relationship from the SIRE API 
/// </summary>
internal sealed record BusinessRelationship
{
    /// <summary>
    /// Gets the timestamp when the relationship was last updated.
    /// </summary>
    [JsonPropertyName("ajourholdstidspunkt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the validity start timestamp.
    /// </summary>
    [JsonPropertyName("gyldighetstidspunkt")]
    public DateTimeOffset? ValidFrom { get; init; }

    /// <summary>
    /// Gets the end/termination timestamp.
    /// </summary>
    [JsonPropertyName("opphoerstidspunkt")]
    public DateTimeOffset? ValidTo { get; init; }

    /// <summary>
    /// Gets the relationship type (e.g. "styretsLeder").
    /// </summary>
    [JsonPropertyName("relasjonstype")]
    public string? RelationshipType { get; init; }

    /// <summary>
    /// Gets the related party identifier.
    /// </summary>
    [JsonPropertyName("relatertIdentifikator")]
    public RelatedIdentifier? RelatedIdentifier { get; init; }
}
