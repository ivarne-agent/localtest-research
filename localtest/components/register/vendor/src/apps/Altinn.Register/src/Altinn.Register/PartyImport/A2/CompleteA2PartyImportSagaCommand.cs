using Altinn.Authorization.ServiceDefaults.MassTransit;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Represents a command that is sent to complete a <see cref="A2PartyImportSaga"/> by upserting a user in the database.
/// </summary>
public sealed record CompleteA2PartyImportSagaCommand
    : CommandBase
{
    /// <summary>
    /// Gets the party UUID.
    /// </summary>
    /// <remarks>
    /// This is only used for easier debugging of failed messages.
    /// </remarks>
    [Obsolete("Use PartyIdentifier instead.")]
    public Guid PartyUuid
    {
        get => PartyIdentifier.TryGetValue(out Guid partyUuid) ? partyUuid : Guid.Empty;
        init
        {
            if (value != Guid.Empty && !PartyIdentifier.HasValue)
            {
                PartyIdentifier = value;
            }
        }
    }

    /// <summary>
    /// Gets the party identifier.
    /// </summary>
    /// <remarks>
    /// This is only used for easier debugging of failed messages.
    /// </remarks>
    public ImportPartyIdentifier PartyIdentifier { get; init; }
}
