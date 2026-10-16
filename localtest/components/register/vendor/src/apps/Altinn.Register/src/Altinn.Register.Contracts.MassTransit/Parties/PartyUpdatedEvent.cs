using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit;

namespace Altinn.Register.Contracts.Parties;

/// <summary>
/// An event that is published when a party is updated.
/// </summary>
[MessageUrn("event:altinn-register:party-updated")]
public sealed record PartyUpdatedEvent
    : EventBase
{
    /// <summary>
    /// Gets the party that was updated.
    /// </summary>
    public required PartyReference Party { get; init; }
}
