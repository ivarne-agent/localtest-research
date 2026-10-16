using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Register.Core.Parties;
using MassTransit;

namespace Altinn.Register.PartyImport;

/// <summary>
/// Consumer for validating parties from different sources and sending them for importing.
/// </summary>
public sealed class PartyImportValidationConsumer
    : IConsumer<UpsertPartyCommand>
{
    private readonly ICommandSender _sender;
    private readonly PersistenceFeatureFlag[] _flags;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartyImportValidationConsumer"/> class.
    /// </summary>
    public PartyImportValidationConsumer(ICommandSender sender, IConfiguration configuration)
    {
        _sender = sender;
        _flags = PersistenceFeatureFlag.FromConfiguration(configuration);
    }

    /// <summary>
    /// Consumes an <see cref="UpsertPartyCommand"/>. Validates the party and sends it to the batch consumer.
    /// </summary>
    /// <param name="context">The consume context.</param>
    public async Task Consume(ConsumeContext<UpsertPartyCommand> context)
    {
        PartyImportHelper.ValidatePartyForUpsert(context.Message.Party, _flags);

        await _sender.Send(UpsertValidatedPartyCommand.From(context.Message), context.CancellationToken);
    }

    /// <summary>
    /// Consumer definition for <see cref="PartyImportBatchConsumer"/>.
    /// </summary>
    public sealed class Definition
        : ConsumerDefinition<PartyImportValidationConsumer>
    {
        /// <inheritdoc/>
        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<PartyImportValidationConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            base.ConfigureConsumer(endpointConfigurator, consumerConfigurator, context);

            endpointConfigurator.PrefetchCount = 200;
        }
    }
}
