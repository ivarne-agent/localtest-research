using Altinn.Authorization.ModelUtils;
using Altinn.Register.Contracts;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// Represents a single SIRE feed event after validation. The wire payload (a
/// <c>HendelseItem</c>) is parsed into this domain shape by <c>HendelseItemValidator</c>.
/// </summary>
public sealed record SireUpdate
{
    /// <summary>
    /// Gets the monotonic sequence number of the update.
    /// </summary>
    public required uint SequenceNumber { get; init; }

    /// <summary>
    /// Gets the organization identifier the event refers to.
    /// </summary>
    public required OrganizationIdentifier OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the time at which SIRE registered the event.
    /// </summary>
    public required DateTimeOffset RegisteredAt { get; init; }

    /// <summary>
    /// Gets the kind of event (<see cref="SireUpdateType.New"/>,
    /// <see cref="SireUpdateType.Changed"/>, …). Carried for observability — the consumer
    /// does not branch on it; every event triggers the same re-fetch + upsert action.
    /// </summary>
    public required NonExhaustiveEnum<SireUpdateType> UpdateType { get; init; }
}
