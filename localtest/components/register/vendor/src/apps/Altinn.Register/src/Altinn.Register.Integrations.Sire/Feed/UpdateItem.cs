using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Sire;

namespace Altinn.Register.Integrations.Sire.Feed;

/// <summary>
/// Represents a single event in the SIRE feed (one entry of <c>hendelser</c>).
/// </summary>
public sealed record UpdateItem
{
    /// <summary>
    /// Gets the monotonic sequence number used to resume the feed from a known position.
    /// </summary>
    [JsonPropertyName("sekvensnummer")]
    public required uint SequenceNumber { get; init; }

    /// <summary>
    /// Gets the 9-digit organization identifier the event refers to.
    /// </summary>
    [JsonPropertyName("identifikator")]
    public string? Identifier { get; init; }

    /// <summary>
    /// Gets the time at which SIRE registered the event.
    /// </summary>
    [JsonPropertyName("registreringstidspunkt")]
    public DateTimeOffset? RegisteredAt { get; init; }

    /// <summary>
    /// Gets the kind of event (e.g. <c>NY</c>, <c>ENDRET</c>).
    /// </summary>
    [JsonPropertyName("hendelsetype")]
    public NonExhaustiveEnum<SireUpdateType>? UpdateType { get; init; }
}
