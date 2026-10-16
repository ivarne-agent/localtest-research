namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Saga for importing parties from A2.
/// </summary>
public partial class A2PartyImportSaga
{
    /// <inheritdoc/>
    public async Task Handle(EnrichA2PartyImportSagaCommand message, CancellationToken cancellationToken)
    {
        if (!State.Enrichers.TryDequeue(out var enricherName))
        {
            // No more enrichers to run, continue.
            await _context.Send(
                  new CompleteA2PartyImportSagaCommand
                  {
                      CorrelationId = SagaId,
                      PartyIdentifier = State.PartyIdentifier,
                  },
                  cancellationToken);
            return;
        }

        var enricher = A2PartyImportSagaEnricher.Get(enricherName);
        var context = new A2PartyImportSagaEnrichmentRunContext { Party = State.Party!, PartyIdentifier = State.PartyIdentifier, RoleAssignments = State.RoleAssignments };
        await enricher.Run(_services, context, cancellationToken);
        State.Party = context.Party;

        await ContinueEnrichment(cancellationToken);
    }
}
