using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Provides shared historical metadata for NPR elements.
/// </summary>
public abstract record HistoricalElement
{
    /// <summary>
    /// Gets a value indicating whether this entry is the current one.
    /// </summary>
    [JsonPropertyName("erGjeldende")]
    public bool? IsCurrent { get; init; }

    /// <summary>
    /// Gets the timestamp when the entry was last updated.
    /// </summary>
    [JsonPropertyName("ajourholdstidspunkt")]
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>
    /// Gets the timestamp when the entry became valid.
    /// </summary>
    [JsonPropertyName("gyldighetstidspunkt")]
    public DateTimeOffset? ValidFrom { get; init; }
}
