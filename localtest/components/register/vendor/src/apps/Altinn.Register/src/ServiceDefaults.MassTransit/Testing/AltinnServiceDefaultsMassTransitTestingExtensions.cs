using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Altinn.Authorization.ServiceDefaults.MassTransit.Testing;

/// <summary>
/// Extension methods for setting up MassTransit testing services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class AltinnServiceDefaultsMassTransitTestingExtensions
{
    /// <summary>
    /// Adds the necessary services for MassTransit testing.
    /// </summary>
    /// <param name="service">The <see cref="IServiceCollection"/>.</param>
    /// <param name="output">Optional output writer for logging.</param>
    /// <param name="configureMassTransit">Optional bus registration configurator delegate.</param>
    /// <param name="configureBus">Optional bus factory configurator delegate.</param>
    /// <returns><paramref name="service"/>.</returns>
    public static IServiceCollection AddAltinnMassTransitTestHarness(
        this IServiceCollection service,
        TextWriter? output = null,
        Action<IBusRegistrationConfigurator>? configureMassTransit = null,
        Action<IBusFactoryConfigurator>? configureBus = null)
    {
        var helper = MassTransitTransportHelper.ForTestHarness();
        AltinnServiceDefaultsMassTransitExtensions.SetupCoreBusServices(
            service,
            helper,
            redeliveryIntervals: null,
            configureMassTransit,
            configureBus,
            (s, c) => s.AddMassTransitTestHarness(output ?? Console.Out, cfg =>
            {
                if (!Debugger.IsAttached)
                {
                    cfg.SetTestTimeouts(
                        testTimeout: TimeSpan.FromMinutes(3),
                        testInactivityTimeout: TimeSpan.FromMinutes(1));
                }

                c(cfg);
            }));

        return service;
    }
}
