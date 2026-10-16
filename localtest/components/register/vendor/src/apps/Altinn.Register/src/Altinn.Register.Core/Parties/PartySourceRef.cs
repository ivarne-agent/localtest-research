namespace Altinn.Register.Core.Parties;

/// <summary>
/// A party source-ref entity.
/// </summary>
public record PartySourceRef
{
    /// <summary>
    /// Gets the UUID of the party.
    /// </summary>
    public required Guid PartyUuid { get; init; }

    /// <summary>
    /// Gets the party source of this source-ref.
    /// </summary>
    public required PartySource PartySource { get; init; }

    /// <summary>
    /// Gets the source identifier of this source-ref.
    /// </summary>
    public required string SourceIdentifier { get; init; }

    /// <summary>
    /// Gets when the source created the referenced entity if available.
    /// </summary>
    public required DateTimeOffset? SourceCreated { get; init; }

    /// <summary>
    /// Gets when the source last updated the referenced entity if available.
    /// </summary>
    public required DateTimeOffset? SourceUpdated { get; init; }
}
