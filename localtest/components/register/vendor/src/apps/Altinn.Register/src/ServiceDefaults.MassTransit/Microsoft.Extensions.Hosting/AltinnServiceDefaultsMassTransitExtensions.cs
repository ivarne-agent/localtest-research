using System.Diagnostics.CodeAnalysis;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.MassTransit.Commands;
using CommunityToolkit.Diagnostics;
using MassTransit;
using MassTransit.Logging;
using MassTransit.Monitoring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Host builder extensions for MassTransit.
/// </summary>
[ExcludeFromCodeCoverage]
public static class AltinnServiceDefaultsMassTransitExtensions
{
    private static string DefaultConfigSectionName(string connectionName)
        => $"Altinn:MassTransit:{connectionName}";

    /// <summary>
    /// Configures migrations to run. This is intended to be used in an init-container.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    public static void AddAltinnMassTransitMigrations(this IHostApplicationBuilder builder)
    {
        var serviceDescriptor = builder.GetAltinnServiceDescriptor();

        AddAltinnMassTransitMigrations(builder, DefaultConfigSectionName(serviceDescriptor.Name), serviceDescriptor.Name);
    }

    private static void AddAltinnMassTransitMigrations(this IHostApplicationBuilder builder, string configurationSectionName, string busName)
    {
        Guard.IsNotNull(builder);

        var configuration = builder.Configuration.GetSection(configurationSectionName);
        MassTransitSettings settings = new();
        configuration.Bind(settings);

        MassTransitTransportHelper helper = MassTransitTransportHelper.For(settings, busName);
        helper.AddMigrations(builder);
    }

    /// <summary>
    /// Adds MassTransit to the host.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="configureSettings">Optional settings configuration delegate.</param>
    /// <param name="configureMassTransit">Optional bus registration configurator delegate.</param>
    /// <param name="configureBus">Optional bus factory configurator delegate.</param>
    /// <returns>A <see cref="IMassTransitBuilder"/> for further configuration.</returns>
    public static IMassTransitBuilder AddAltinnMassTransit(
        this IHostApplicationBuilder builder,
        Action<MassTransitSettings>? configureSettings = null,
        Action<IBusRegistrationConfigurator>? configureMassTransit = null,
        Action<IBusFactoryConfigurator>? configureBus = null)
    {
        var serviceDescriptor = builder.GetAltinnServiceDescriptor();

        return AddAltinnMassTransit(builder, serviceDescriptor.Name, configureSettings, configureMassTransit, configureBus);
    }

    /// <summary>
    /// Adds MassTransit to the host.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    /// <param name="busName">The name of the bus.</param>
    /// <param name="configureSettings">Optional settings configuration delegate.</param>
    /// <param name="configureMassTransit">Optional bus registration configurator delegate.</param>
    /// <param name="configureBus">Optional bus factory configurator delegate.</param>
    /// <returns>A <see cref="IMassTransitBuilder"/> for further configuration.</returns>
    public static IMassTransitBuilder AddAltinnMassTransit(
        this IHostApplicationBuilder builder,
        string busName,
        Action<MassTransitSettings>? configureSettings = null,
        Action<IBusRegistrationConfigurator>? configureMassTransit = null,
        Action<IBusFactoryConfigurator>? configureBus = null)
        => AddAltinnMassTransitCore(builder, DefaultConfigSectionName(busName), configureSettings, busName, configureMassTransit, configureBus);

    private static IMassTransitBuilder AddAltinnMassTransitCore(
        IHostApplicationBuilder builder,
        string configurationSectionName,
        Action<MassTransitSettings>? configureSettings,
        string busName,
        Action<IBusRegistrationConfigurator>? configureMassTransit,
        Action<IBusFactoryConfigurator>? configureBus = null)
    {
        Guard.IsNotNull(builder);

        var configuration = builder.Configuration.GetSection(configurationSectionName);
        if (builder.Services.Contains(Marker.ServiceDescriptor))
        {
            // already registered
            return new MassTransitBuilder(builder.Services, configuration);
        }

        builder.Services.Add(Marker.ServiceDescriptor);

        MassTransitSettings settings = new();
        configuration.Bind(settings);
        configureSettings?.Invoke(settings);

        builder.Services.AddOptions<MassTransitHostOptions>()
            .Configure(s => s.WaitUntilStarted = true);

        MassTransitTransportHelper helper = MassTransitTransportHelper.For(settings, busName);

        TimeSpan[] redeliveryIntervals;
        if (builder.Environment.IsDevelopment())
        {
            redeliveryIntervals = [
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(30),
            ];
        }
        else
        {
            redeliveryIntervals = [
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(15),
            ];
        }

        helper.ConfigureHost(builder);
        SetupCoreBusServices(
            builder.Services,
            helper,
            redeliveryIntervals,
            configureMassTransit,
            configureBus,
            static (s, c) => s.AddMassTransit(c));

        if (!settings.DisableHealthChecks)
        {
            foreach (var reg in helper.HealthCheckRegistrations)
            {
                builder.TryAddHealthCheck(reg);
            }
        }

        if (!settings.DisableTracing)
        {
            builder.Services.AddOpenTelemetry()
                .WithTracing(traceProviderBuilder =>
                {
                    traceProviderBuilder.AddSource(DiagnosticHeaders.DefaultListenerName);
                    helper.RegisterTracing(traceProviderBuilder);
                });
        }

        if (!settings.DisableMetrics)
        {
            builder.Services.AddOpenTelemetry()
                .WithMetrics(meterProviderBuilder =>
                {
                    meterProviderBuilder.AddMeter(InstrumentationOptions.MeterName);
                    helper.RegisterMetrics(meterProviderBuilder);
                });
        }

        return new MassTransitBuilder(builder.Services, configuration);
    }

    /// <summary>
    /// Setup core MassTransit services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="helper">The <see cref="MassTransitTransportHelper"/>.</param>
    /// <param name="redeliveryIntervals">Redelivery intervals (or <see langword="null"/> to disable redelivery)</param>
    /// <param name="configureMassTransit">Configuration delegate for <see cref="IBusRegistrationConfigurator"/>.</param>
    /// <param name="configureBus">Configuration delegate for <see cref="IBusFactoryConfigurator"/>.</param>
    /// <param name="addBus">The method used to add the bus to the service collection.</param>
    internal static void SetupCoreBusServices(
        IServiceCollection services,
        MassTransitTransportHelper helper,
        TimeSpan[]? redeliveryIntervals,
        Action<IBusRegistrationConfigurator>? configureMassTransit,
        Action<IBusFactoryConfigurator>? configureBus,
        Action<IServiceCollection, Action<IBusRegistrationConfigurator>> addBus)
    {
        services.AddSingleton<MassTransitLifecycleObserver>();
        services.AddSingleton<IBusLifetime>(s => s.GetRequiredService<MassTransitLifecycleObserver>());
        services.AddSingleton<IEndpointNameFormatter, AltinnEndpointNameFormatter>();
        services.AddSingleton<CommandQueueResolver>();
        services.AddSingleton<ICommandQueueRegistry>(s => s.GetRequiredService<CommandQueueResolver>());
        services.AddSingleton<ICommandQueueResolver>(s => s.GetRequiredService<CommandQueueResolver>());
        services.AddSingleton<CommandQueueRegistryEndpointConfigurationObserver>();
        services.AddScoped<ICommandSender, CommandSender>();
        addBus(services, configurator =>
        {
            configurator.AddBusObserver(s => s.GetRequiredService<MassTransitLifecycleObserver>());

            configurator.AddConfigureEndpointsCallback((ctx, name, cfg) =>
            {
                // kill-switch all CircuitBreaker exceptions
                cfg.UseKillSwitch(options => options
                    .SetExceptionFilter(f => f.Handle<BrokenCircuitException>())
                    .SetRestartTimeout(TimeSpan.FromMinutes(15))
                    .SetActivationThreshold(10)
                    .SetTripThreshold(1));

                // redeliver if all retries fail
                if (redeliveryIntervals is not null)
                {
                    cfg.UseScheduledRedelivery(r => r.Intervals(redeliveryIntervals));
                }

                // retry messages 1 times, in short order, before failing back to the broker
                cfg.UseMessageRetry(r => r.Intervals(
                    TimeSpan.FromSeconds(1)));

                cfg.UseInMemoryOutbox(ctx, x => x.ConcurrentMessageDelivery = true);
            });

            configurator.AddOptions<AltinnMassTransitOptions>();
            configurator.AddSendObserver<DiagnosticHeadersSendObserver>();

            configureMassTransit?.Invoke(configurator);
            helper.ConfigureBus(configurator, (ctx, cfg) =>
            {
                var observer = ctx.GetRequiredService<CommandQueueRegistryEndpointConfigurationObserver>();
                cfg.ConnectEndpointConfigurationObserver(observer);

                configureBus?.Invoke(cfg);
            });
        });
    }

    private sealed class Marker
    {
        public static readonly ServiceDescriptor ServiceDescriptor = ServiceDescriptor.Singleton<Marker, Marker>();
    }

    private sealed class DiagnosticHeadersSendObserver(IOptionsMonitor<AltinnMassTransitOptions> options)
        : ISendObserver
    {
        public Task PostSend<T>(SendContext<T> context)
            where T : class
        {
            return Task.CompletedTask;
        }

        public Task PreSend<T>(SendContext<T> context)
            where T : class
        {
            if (!context.Headers.TryGetHeader(DiagnosticHeaders.ActivityPropagation, out _))
            {
                // TODO: handle queries which should be children, not links
                context.Headers.Set(DiagnosticHeaders.ActivityPropagation, options.CurrentValue.ActivityPropagation);
            }

            return Task.CompletedTask;
        }

        public Task SendFault<T>(SendContext<T> context, Exception exception)
            where T : class
        {
            if (!context.Headers.TryGetHeader(DiagnosticHeaders.ActivityPropagation, out _))
            {
                context.Headers.Set(DiagnosticHeaders.ActivityPropagation, options.CurrentValue.ActivityPropagation);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CommandQueueRegistryEndpointConfigurationObserver(ICommandQueueRegistry registry)
        : IEndpointConfigurationObserver
    {
        public void EndpointConfigured<T>(T configurator)
            where T : IReceiveEndpointConfigurator
        {
            configurator.ConnectConsumerConfigurationObserver(new CommandQueueRegistryConsumerConfigurationObserver(registry, configurator.InputAddress));
        }
    }

    private sealed class CommandQueueRegistryConsumerConfigurationObserver(ICommandQueueRegistry registry, Uri queueUri)
        : IConsumerConfigurationObserver
    {
        public void ConsumerConfigured<TConsumer>(IConsumerConfigurator<TConsumer> configurator)
            where TConsumer : class
        {
        }

        public void ConsumerMessageConfigured<TConsumer, TMessage>(IConsumerMessageConfigurator<TConsumer, TMessage> configurator)
            where TConsumer : class
            where TMessage : class
        {
            if (TryGetCommandType(typeof(TMessage), out var commandType))
            {
                registry.RegisterConsumerCommandQueue(commandType, queueUri, typeof(TConsumer));
            }
        }

        private static bool TryGetCommandType(Type messageType, [NotNullWhen(true)] out Type? commandType)
        {
            if (typeof(CommandBase).IsAssignableFrom(messageType))
            {
                commandType = messageType;
                return true;
            }

            if (messageType.IsConstructedGenericType && messageType.GetGenericTypeDefinition() == typeof(Batch<>))
            {
                return TryGetCommandType(messageType.GenericTypeArguments[0], out commandType);
            }

            commandType = null;
            return false;
        }
    }
}
