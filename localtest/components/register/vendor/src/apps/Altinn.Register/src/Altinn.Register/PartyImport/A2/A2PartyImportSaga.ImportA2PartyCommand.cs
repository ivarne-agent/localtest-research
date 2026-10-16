using System.Diagnostics;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public partial class A2PartyImportSaga
{
    /// <inheritdoc/>
    public static ValueTask<A2PartyImportSagaData> CreateInitialState(IServiceProvider services, ImportA2PartyCommand command)
        => ValueTask.FromResult(new A2PartyImportSagaData
        {
            PartyIdentifier = command.PartyUuid,
            Tracking = command.Tracking,
        });

    /// <inheritdoc/>
    public async Task Handle(ImportA2PartyCommand message, CancellationToken cancellationToken)
    {
        Debug.Assert(State.PartyIdentifier.TryGetValue(out Guid partyUuid) && partyUuid == message.PartyUuid);
        if (await FetchPartyFromA2(cancellationToken) == FlowControl.Break)
        {
            return;
        }

        await Enrich(cancellationToken);
    }
}
