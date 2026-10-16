using System.Diagnostics.CodeAnalysis;
using CommunityToolkit.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Configuration helper for MassTransit.
/// </summary>
[ExcludeFromCodeCoverage]
internal abstract partial class MassTransitTransportHelper(MassTransitSettings settings, string busName)
{
    /// <summary>
    /// Gets a <see cref="MassTransitTransportHelper"/> for the specified settings.
    /// </summary>
    /// <param name="settings">The settings.</param>
    /// <param name="busName">The name of the bus.</param>
    /// <returns>A <see cref="MassTransitTransportHelper"/>.</returns>
    public static MassTransitTransportHelper For(MassTransitSettings settings, string busName)
        => settings.Transport switch
        {
            MassTransitTransport.InMemory => new InMemoryTransportHelper(settings, busName),
            MassTransitTransport.RabbitMq => new RabbitMqTransportHelper(settings, busName),
            MassTransitTransport.AzureServiceBus => new AzureServiceBusTransportHelper(settings, busName),
            _ => ThrowHelper.ThrowArgumentException<MassTransitTransportHelper>(nameof(settings), "Invalid transport"),
        };

    /// <summary>
    /// Gets a <see cref="MassTransitTransportHelper"/> for the test harness.
    /// </summary>
    /// <returns>A <see cref="MassTransitTransportHelper"/>.</returns>
    public static MassTransitTransportHelper ForTestHarness()
        => For(
            new MassTransitSettings
            {
                Transport = MassTransitTransport.InMemory,
            },
            "integration-test");

    /// <summary>
    /// Gets the settings for the bus.
    /// </summary>
    protected MassTransitSettings BusSettings => settings;

    /// <summary>
    /// Gets the name of the bus.
    /// </summary>
    protected string BusName => busName;

    /// <summary>
    /// Gets the health check registrations for the bus transport.
    /// </summary>
    public virtual IEnumerable<HealthCheckRegistration> HealthCheckRegistrations => [];

    /// <summary>
    /// Registers tracing for the bus transport.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/>.</param>
    public virtual void RegisterTracing(TracerProviderBuilder builder)
    {
    }

    /// <summary>
    /// Registers metrics for the bus transport.
    /// </summary>
    /// <param name="builder">The <see cref="MeterProviderBuilder"/>.</param>
    public virtual void RegisterMetrics(MeterProviderBuilder builder)
    {
    }

    /// <summary>
    /// Configures the host.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    public virtual void ConfigureHost(IHostApplicationBuilder builder)
    {
    }

    /// <summary>
    /// Configures migrations to be run as part of host-init.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder"/>.</param>
    public virtual void AddMigrations(IHostApplicationBuilder builder)
    {
    }

    /// <summary>
    /// Configures the bus.
    /// </summary>
    /// <param name="configurator">The bus configurator.</param>
    /// <param name="configureBus">The bus factory configurator.</param>
    public abstract void ConfigureBus(IBusRegistrationConfigurator configurator, Action<IBusRegistrationContext, IBusFactoryConfigurator> configureBus);
}
