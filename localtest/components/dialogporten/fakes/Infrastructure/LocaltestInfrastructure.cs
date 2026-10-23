using Localtest.Shared.Maskinporten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>The replacements all three hosts share; the hosts call it from <see cref="LocaltestOverrides"/>.</summary>
public static class LocaltestInfrastructure
{
    /// <summary>
    /// <c>IMaskinportenService</c> becomes the shared localtest fake (a copy of <c>components/shared/fakes/Maskinporten</c>).
    /// The package hard-codes <c>maskinporten.no</c> and the per-environment exchange hosts, so configuration cannot
    /// redirect it. The three Maskinporten-backed clients (events, party names, authorization) keep their production
    /// handlers and settings; only the token they ask for comes from the localtest token endpoint and Authentication's
    /// <c>exchange/maskinporten</c>. The four clients on <c>Infrastructure:Altinn:BaseUri</c> call the services by name
    /// (<see cref="LocaltestServiceNames"/>).
    /// </summary>
    public static IServiceCollection Apply(IServiceCollection services, IConfiguration configuration)
        => LocaltestServiceNames.Apply(services.AddLocaltestMaskinporten(configuration));
}
