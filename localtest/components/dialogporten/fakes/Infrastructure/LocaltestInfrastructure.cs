using Digdir.Domain.Dialogporten.Application.Externals;
using Digdir.Domain.Dialogporten.Application.Externals.AltinnAuthorization;
using Localtest.Shared.Maskinporten;
using Localtest.Shared.ServiceDiscovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>The replacements all three hosts share; the hosts call it from <see cref="LocaltestOverrides"/>.</summary>
public static class LocaltestInfrastructure
{
    /// <summary>
    /// <c>IMaskinportenService</c> becomes the shared localtest fake (a copy of <c>components/shared/fakes/Maskinporten</c>).
    /// The package hard-codes <c>maskinporten.no</c> and the per-environment exchange hosts, so configuration cannot
    /// redirect it. The three Maskinporten-backed clients (events, party names, authorization) keep their production
    /// handlers and settings; only the token they ask for comes from the localtest token endpoint and Authentication's
    /// <c>exchange/maskinporten</c>.
    /// <para>
    /// The four production clients on <c>Infrastructure:Altinn:BaseUri</c> (the Resource Registry, Access Management's
    /// metadata, the Register's party names, the PDP with Access Management's authorized parties) call the services by
    /// name: upstream uses one base for all of them, and <c>MetadataLinkProvider</c> builds the links it hands to clients
    /// from it, so the base stays the public origin and <see cref="LocaltestPrefixRouting"/> (a copy of
    /// <c>components/shared/fakes/ServiceDiscovery</c>), outermost on those clients, moves each request to the service
    /// that owns its prefix. The typed clients' names are their interfaces' short names.
    /// </para>
    /// </summary>
    public static IServiceCollection Apply(IServiceCollection services, IConfiguration configuration)
        => LocaltestPrefixRouting.AddTo(
            services.AddLocaltestMaskinporten(configuration),
            sp => sp.GetRequiredService<IOptions<InfrastructureSettings>>().Value.Altinn.BaseUri,
            nameof(IResourceRegistry),
            nameof(IAccessManagementMetadata),
            nameof(IPartyNameRegistry),
            nameof(IAltinnAuthorization));
}
