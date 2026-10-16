using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.Parties.Records;

namespace Altinn.Register.PartyImport;

/// <summary>
/// A command for upserting a party.
/// </summary>
public sealed record UpsertPartyCommand
    : CommandBase
{
    /// <summary>
    /// Gets the party to import.
    /// </summary>
    public required PartyRecord Party { get; init; }

    /// <summary>
    /// Gets the tracking information for the import.
    /// </summary>
    public UpsertPartyTracking Tracking { get; init; }
}
