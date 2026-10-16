using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.Parties.Records;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.PartyImport;

/// <summary>
/// A command for upserting a party where the party has already been validated.
/// </summary>
public sealed record UpsertValidatedPartyCommand
    : CommandBase
{
    /// <summary>
    /// Creates a new instance of <see cref="UpsertValidatedPartyCommand"/> from a <see cref="UpsertPartyCommand"/>.
    /// </summary>
    /// <param name="source">The <see cref="UpsertPartyCommand"/> to create a <see cref="UpsertValidatedPartyCommand"/> from.</param>
    /// <returns>The new <see cref="UpsertValidatedPartyCommand"/>.</returns>
    public static UpsertValidatedPartyCommand From(UpsertPartyCommand source)
    {
        Guard.IsNotNull(source);

        return new()
        {
            Party = source.Party,
            Tracking = source.Tracking,
        };
    }

    /// <summary>
    /// Gets the party to import.
    /// </summary>
    public required PartyRecord Party { get; init; }

    /// <summary>
    /// Gets the tracking information for the import.
    /// </summary>
    public UpsertPartyTracking Tracking { get; init; }
}
