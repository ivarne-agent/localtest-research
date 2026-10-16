using MassTransit;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Configuration helper for MassTransit.
/// </summary>
internal abstract partial class MassTransitTransportHelper
{
    private sealed class InMemoryTransportHelper(MassTransitSettings settings, string busName)
        : MassTransitTransportHelper(settings, busName)
    {
        public override void ConfigureBus(IBusRegistrationConfigurator configurator, Action<IBusRegistrationContext, IBusFactoryConfigurator> configureBus)
        {
            configurator.UsingInMemory((ctx, cfg) =>
            {
                cfg.UseInMemoryScheduler();

                configureBus(ctx, cfg);
                cfg.ConfigureEndpoints(ctx);
            });
        }
    }
}
