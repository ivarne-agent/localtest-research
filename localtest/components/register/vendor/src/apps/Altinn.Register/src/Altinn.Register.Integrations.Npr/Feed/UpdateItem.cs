using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Npr.Feed;

/// <summary>
/// Represents an update item in the NPR feed.
/// </summary>
public sealed record UpdateItem
{
    /// <summary>
    /// Gets the sequence number of the update.
    /// </summary>
    [JsonPropertyName("sekvensnummer")]
    public required uint SequenceNumber { get; init; }

    /// <summary>
    /// Gets the update information.
    /// </summary>
    [JsonPropertyName("hendelse")]
    public UpdateInfo? UpdateInfo { get; init; }
}
