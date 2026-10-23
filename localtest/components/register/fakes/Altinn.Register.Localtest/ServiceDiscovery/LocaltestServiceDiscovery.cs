#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ServiceDiscovery;

namespace Localtest.Shared.ServiceDiscovery;

/// <summary>
/// Service discovery for a component in the localtest stack: another platform service is called by its name
/// (<c>http://register/register/api/v1/</c>), resolved from <c>services__register__http__0</c>, which the runtime sets
/// for every running HTTP container. There is no pass-through for a service name: a single-label host without that
/// entry (its group is off, or a typo) fails at once with the resolver's <see cref="InvalidOperationException"/> "The
/// endpoint collection contains no endpoints", instead of a DNS lookup and a connection error.
/// <para>
/// The configuration provider alone would refuse every other host as well (it answers no endpoints for a host that is
/// not a configured service), so the calls that legitimately leave the service names (the public origin through the
/// host gateway, <c>host.docker.internal</c> for the apps and webhooks, altinncdn.no, <c>localhost</c>) go through
/// <see cref="RealHostEndpointProviderFactory"/>: a host with a dot, an IP address or <c>localhost</c> is used as it
/// is. That is the only fallback, and it never sees a service name.
/// </para>
/// </summary>
public static class LocaltestServiceDiscovery
{
    /// <summary>
    /// Registers the resolver (core, configuration provider, the real-host provider) and adds it to every factory client.
    /// For a host that has no service discovery of its own.
    /// </summary>
    public static IServiceCollection AddLocaltestServiceDiscovery(this IServiceCollection services)
    {
        services.AddLocaltestServiceEndpointProviders();
        services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
        return services;
    }

    /// <summary>
    /// The resolver only, for a host whose own defaults already registered <c>AddServiceDiscovery()</c> and the
    /// factory clients' handler (<c>AddAltinnServiceDefaults</c>): removes the pass-through provider that registered,
    /// and adds the configuration and real-host providers (the configuration provider is added once).
    /// </summary>
    public static IServiceCollection AddLocaltestServiceEndpointProviders(this IServiceCollection services)
    {
        // The pass-through factory is internal to Microsoft.Extensions.ServiceDiscovery; there is no option that turns
        // it off, so its registration is removed by type name.
        foreach (var passThrough in services
            .Where(d => d.ServiceType == typeof(IServiceEndpointProviderFactory)
                && d.ImplementationType?.FullName == "Microsoft.Extensions.ServiceDiscovery.PassThrough.PassThroughServiceEndpointProviderFactory")
            .ToList())
        {
            services.Remove(passThrough);
        }

        services.AddServiceDiscoveryCore();
        services.AddConfigurationServiceEndpointProvider();
        if (!services.Any(d => d.ImplementationType == typeof(RealHostEndpointProviderFactory)))
        {
            // After the configuration provider: the providers run in registration order, each only while no endpoint
            // has been found.
            services.AddSingleton<IServiceEndpointProviderFactory, RealHostEndpointProviderFactory>();
        }

        return services;
    }

    /// <summary>
    /// True for a host that is not a service name: one with a dot (<c>local.altinn.cloud</c>,
    /// <c>host.docker.internal</c>, <c>altinncdn.no</c>), an IP address, or <c>localhost</c>. Service names are single
    /// labels (<c>register</c>, <c>dialogporten-webapi</c>).
    /// </summary>
    public static bool IsRealHost(string host)
        => host.Contains('.', StringComparison.Ordinal)
            || IPAddress.TryParse(host.Trim('[', ']'), out _)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a call failed because the name has no address (no <c>services__{name}__http__0</c>: its group is off).
    /// The resolver's own exception, also when wrapped; a call to an address that does not answer is an
    /// <see cref="System.Net.Http.HttpRequestException"/> instead.
    /// </summary>
    public static bool NoAddress(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is InvalidOperationException { Message: var message }
                && (message == NoEndpointsMessage || message.StartsWith("Unable to resolve endpoint", StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }

    // Microsoft.Extensions.ServiceDiscovery's message for a name it found no endpoint for (9.5.2 and 10.8.0).
    private const string NoEndpointsMessage = "The endpoint collection contains no endpoints";

    /// <summary>Resolves a real host (<see cref="IsRealHost"/>) to itself; declines a service name.</summary>
    public sealed class RealHostEndpointProviderFactory : IServiceEndpointProviderFactory
    {
        /// <inheritdoc />
        public bool TryCreateProvider(ServiceEndpointQuery query, [NotNullWhen(true)] out IServiceEndpointProvider? provider)
        {
            provider = null;
            if (query.ToString() is not { Length: > 0 } text
                || !Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "http://" + text, UriKind.Absolute, out var uri)
                || !IsRealHost(uri.Host))
            {
                return false;
            }

            var port = uri.IsDefaultPort ? 0 : uri.Port;
            EndPoint endPoint = IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address)
                ? new IPEndPoint(address, port)
                : new DnsEndPoint(uri.Host, port);
            provider = new RealHostEndpointProvider(endPoint);
            return true;
        }
    }

    private sealed class RealHostEndpointProvider(EndPoint endPoint) : IServiceEndpointProvider
    {
        public ValueTask PopulateAsync(IServiceEndpointBuilder endpoints, CancellationToken cancellationToken)
        {
            if (endpoints.Endpoints.Count == 0)
            {
                endpoints.Endpoints.Add(ServiceEndpoint.Create(endPoint));
            }

            return default;
        }

        public ValueTask DisposeAsync() => default;

        public override string ToString() => "Localtest real host";
    }
}
