using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// A command for importing a party from NPR.
/// </summary>
public sealed record ImportNprPartyCommand
    : CommandBase
{
    /// <summary>
    /// Gets the person identifier.
    /// </summary>
    public required PersonIdentifier PersonIdentifier { get; init; }

    /// <summary>
    /// Gets the change ID.
    /// </summary>
    public required uint ChangeId { get; init; }

    /// <summary>
    /// Gets when the change was registered.
    /// </summary>
    public required DateTimeOffset ChangedTime { get; init; }

    /// <summary>
    /// Gets tracking information for the import job.
    /// </summary>
    public UpsertPartyTracking Tracking { get; init; }
}
