using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Contracts;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// A command for importing a party from SIRE. Published by <c>SireImportJob</c> for each
/// event read from the SIRE feed; the consumer kicks off the A2 import saga which then
/// fetches the full organization via <c>ISireClient.GetOrganization</c> and runs the
/// existing <c>SireEnricher</c>.
/// </summary>
public sealed record ImportSirePartyCommand
    : CommandBase
{
    /// <summary>
    /// Gets the organization identifier.
    /// </summary>
    public required OrganizationIdentifier OrganizationIdentifier { get; init; }

    /// <summary>
    /// Gets the change ID (sekvensnummer from the SIRE event feed).
    /// </summary>
    public required uint ChangeId { get; init; }

    /// <summary>
    /// Gets when the change was registered at SIRE (registreringstidspunkt).
    /// </summary>
    public required DateTimeOffset ChangedTime { get; init; }

    /// <summary>
    /// Gets tracking information for the import job.
    /// </summary>
    public UpsertPartyTracking Tracking { get; init; }
}
