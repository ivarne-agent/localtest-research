using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Feed;

/// <summary>
/// Represents an update event in the NPR feed.
/// </summary>
public sealed record UpdateInfo
{
    /// <summary>
    /// Gets the identifier of the person whose NPR data has been updated.
    /// </summary>
    [JsonPropertyName("folkeregisteridentifikator")]
    public string? PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the time when the update was made.
    /// </summary>
    [JsonPropertyName("ajourholdstidspunkt")]
    public DateTimeOffset? UpdateTime { get; init; }
}
