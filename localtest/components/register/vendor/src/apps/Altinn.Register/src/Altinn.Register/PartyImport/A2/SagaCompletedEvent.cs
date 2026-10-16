using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// An event that is published when a saga is completed.
/// </summary>
[MessageUrn("event:saga:completed")]
public sealed record SagaCompletedEvent
    : EventBase
{
    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public required bool Success { get; init; }
}
