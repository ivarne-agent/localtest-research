using MassTransit;

namespace Altinn.Register.PartyImport.A2;

/// <summary>
/// Consumer for importing parties from A2.
/// </summary>
public sealed partial class A2PartyImportConsumer
    : IConsumer<ImportA2PartyCommand>
    , IConsumer<ImportA2UserProfileCommand>
    , IConsumer<ImportNprPartyCommand>
    , IConsumer<ImportSirePartyCommand>
    , IConsumer<CompleteA2PartyImportSagaCommand>
    , IConsumer<EnrichA2PartyImportSagaCommand>
    , IConsumer<RetryA2PartyImportSagaCommand>
{
    private readonly SagaManager _manager;

    /// <summary>
    /// Initializes a new instance of the <see cref="A2PartyImportConsumer"/> class.
    /// </summary>
    public A2PartyImportConsumer(SagaManager manager)
    {
        _manager = manager;
    }

    /// <inheritdoc />
    public Task Consume(ConsumeContext<ImportA2PartyCommand> context)
        => _manager.StartSaga<A2PartyImportSaga, ImportA2PartyCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<ImportA2UserProfileCommand> context)
        => _manager.StartSaga<A2PartyImportSaga, ImportA2UserProfileCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<ImportNprPartyCommand> context)
        => _manager.StartSaga<A2PartyImportSaga, ImportNprPartyCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<ImportSirePartyCommand> context)
        => _manager.StartSaga<A2PartyImportSaga, ImportSirePartyCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<CompleteA2PartyImportSagaCommand> context)
        => _manager.HandleMessage<A2PartyImportSaga, CompleteA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<EnrichA2PartyImportSagaCommand> context)
        => _manager.HandleMessage<A2PartyImportSaga, EnrichA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <inheritdoc />
    public Task Consume(ConsumeContext<RetryA2PartyImportSagaCommand> context)
        => _manager.HandleMessage<A2PartyImportSaga, RetryA2PartyImportSagaCommand, A2PartyImportSaga.A2PartyImportSagaData>(context);

    /// <summary>
    /// Consumer definition for <see cref="A2PartyImportConsumer"/>.
    /// </summary>
    public sealed class Definition
        : ConsumerDefinition<A2PartyImportConsumer>
    {
        /// <inheritdoc/>
        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<A2PartyImportConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            base.ConfigureConsumer(endpointConfigurator, consumerConfigurator, context);

            endpointConfigurator.ConcurrentMessageLimit = 3;
        }
    }
}
