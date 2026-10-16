using Altinn.Authorization.ServiceDefaults.MassTransit;
using MassTransit;

namespace Altinn.Register.PartyImport.Ccr;

/// <summary>
/// An internal event that is published when a saga is completed.
/// </summary>
[MessageUrn("event:ccr:xml-completed")]
public sealed record CcrXmlImportCompletedEvent
    : EventBase
{
    /// <summary>
    /// Gets a value indicating whether the operation completed successfully.
    /// </summary>
    public required bool Success { get; init; }
}
