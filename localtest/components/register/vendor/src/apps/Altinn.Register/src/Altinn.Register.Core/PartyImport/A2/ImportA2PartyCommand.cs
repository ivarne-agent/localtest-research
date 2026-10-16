using Altinn.Authorization.ServiceDefaults.MassTransit;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// A command for importing a party from A2.
/// </summary>
public sealed record ImportA2PartyCommand
    : CommandBase
{
    /// <summary>
    /// Gets the party UUID.
    /// </summary>
    public required Guid PartyUuid { get; init; }

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
