using System.Text.Json.Serialization;

namespace Altinn.Register.Integrations.Sire.Feed;

/// <summary>
/// The outer envelope of a SIRE event-feed page.
/// </summary>
public sealed record UpdateFeed
{
    /// <summary>
    /// Gets the events in this page, ordered by <see cref="UpdateItem.SequenceNumber"/>.
    /// </summary>
    /// <remarks>
    /// Both the list itself and individual elements are nullable: the wire payload may
    /// omit <c>hendelser</c> entirely (empty page) or include explicit <c>null</c>
    /// entries inside the array. <see cref="UpdateItemValidator"/> handles each case
    /// as a Required error at <c>/hendelser/{index}</c>.
    /// </remarks>
    [JsonPropertyName("hendelser")]
    public IReadOnlyList<UpdateItem?>? Updates { get; init; }
}
