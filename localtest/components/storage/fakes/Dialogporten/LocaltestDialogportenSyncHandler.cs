#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Storage.Messages;

namespace Altinn.Platform.Storage.Localtest.Dialogporten;

/// <summary>
/// The localtest destination of the outbox. In production <c>OutboxService</c> publishes each
/// <see cref="SyncInstanceToDialogportenCommand"/> to the adapter's Azure Service Bus queue; in <c>Development</c>, which
/// the container runs as, Wolverine routes it to an in-process sequential local queue instead, where nothing handled it.
/// Wolverine discovers this handler in the host assembly (the fakes are compiled into it) and the handler hands the
/// command to the adapter's localtest enqueue endpoint, so the adapter's production handler takes it from there.
/// </summary>
public sealed class LocaltestDialogportenSyncHandler
{
    public static Task Handle(
        SyncInstanceToDialogportenCommand command,
        LocaltestDialogportenAdapterClient adapter,
        CancellationToken cancellationToken
    ) => adapter.Enqueue(command, cancellationToken);
}
