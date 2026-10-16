using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Npr;

/// <summary>
/// Represents an update to a person's NPR data, including the sequence number of the update,
/// the identifier of the person, and the time of the update.
/// </summary>
public sealed record NprUpdate
{
    /// <summary>
    /// Gets the sequence number of the update.
    /// </summary>
    public required uint SequenceNumber { get; init; }

    /// <summary>
    /// Gets the identifier of the person whose NPR data has been updated.
    /// </summary>
    public required PersonIdentifier PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the time when the update was made.
    /// </summary>
    public required DateTimeOffset UpdateTime { get; init; }
}
