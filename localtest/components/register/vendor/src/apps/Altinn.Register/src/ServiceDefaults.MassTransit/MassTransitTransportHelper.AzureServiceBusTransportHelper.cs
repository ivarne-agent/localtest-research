using Azure.Core;
using Azure.Identity;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Configuration helper for MassTransit.
/// </summary>
internal abstract partial class MassTransitTransportHelper
{
    private sealed class AzureServiceBusTransportHelper(MassTransitSettings settings, string busName)
        : MassTransitTransportHelper(settings, busName)
    {
        private MassTransitAzureServiceBusSettings Settings => BusSettings.AzureServiceBus;

        public override void ConfigureBus(IBusRegistrationConfigurator configurator, Action<IBusRegistrationContext, IBusFactoryConfigurator> configureBus)
        {
            configurator.AddServiceBusMessageScheduler();

            configurator.UsingAzureServiceBus((ctx, cfg) =>
            {
                if (!string.IsNullOrEmpty(Settings.ConnectionString))
                {
                    cfg.Host(Settings.ConnectionString);
                }
                else
                {
                    cfg.Host(Settings.Endpoint, hostCfg =>
                    {
                        var logger = ctx.GetRequiredService<ILogger<AzureServiceBusTransportHelper>>();
                        List<TokenCredential> credentialList = [];

                        if (Settings.Credentials.Environment)
                        {
                            logger.LogInformation("adding environment credentials to asb token");
                            credentialList.Add(new EnvironmentCredential());
                        }

                        if (Settings.Credentials.WorkloadIdentity)
                        {
                            logger.LogInformation("adding workload identity credentials to asb token");
                            credentialList.Add(new WorkloadIdentityCredential());
                        }

                        if (Settings.Credentials.ManagedIdentity)
                        {
                            logger.LogInformation("adding managed identity credentials to asb token");
                            credentialList.Add(new ManagedIdentityCredential(new ManagedIdentityCredentialOptions()));
                        }

                        if (credentialList.Count == 0)
                        {
                            logger.LogError("No credentials configured for asb token");
                            throw new InvalidOperationException("No credentials configured for asb token");
                        }

                        var credential = new ChainedTokenCredential([.. credentialList]);
                        hostCfg.TokenCredential = credential;
                    });
                }

                cfg.SetNamespaceSeparatorToUnderscore();
                cfg.UseServiceBusMessageScheduler();

                configureBus(ctx, cfg);
                cfg.ConfigureEndpoints(ctx);
            });
        }
    }
}
