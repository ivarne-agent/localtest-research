using MassTransit;

namespace Altinn.Register.PartyImport;

/// <summary>
/// Consumer for upserting user records.
/// </summary>
public sealed partial class UpsertUserRecordConsumer
    : IConsumer<UpsertUserRecordCommand>
{
    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<UpsertUserRecordCommand> context)
    {
        throw new NotSupportedException("This consumer is not supported anymore and should not be used. It will be removed in a future release.");
    }

    /// <summary>
    /// Consumer definition for <see cref="UpsertUserRecordConsumer"/>.
    /// </summary>
    public sealed class Definition
        : ConsumerDefinition<UpsertUserRecordConsumer>
    {
        /// <inheritdoc/>
        protected override void ConfigureConsumer(
            IReceiveEndpointConfigurator endpointConfigurator,
            IConsumerConfigurator<UpsertUserRecordConsumer> consumerConfigurator,
            IRegistrationContext context)
        {
            base.ConfigureConsumer(endpointConfigurator, consumerConfigurator, context);

            consumerConfigurator.UseConcurrentMessageLimit(10, endpointConfigurator);
        }
    }
}
