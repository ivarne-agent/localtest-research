using Altinn.ApiClients.Maskinporten.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Localtest.Shared.Maskinporten;

public static class LocaltestMaskinportenExtensions
{
    /// <summary>
    /// Replaces the package's <see cref="IMaskinportenService"/> with <see cref="LocaltestMaskinportenService"/>. Call it
    /// after the consumer's own Maskinporten registrations, which add the package's service with <c>TryAdd</c>.
    /// </summary>
    public static IServiceCollection AddLocaltestMaskinporten(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<LocaltestMaskinportenOptions>(configuration.GetSection(LocaltestMaskinportenOptions.SectionName));
        services.AddHttpClient();
        services.RemoveAll<IMaskinportenService>();
        return services.AddSingleton<IMaskinportenService, LocaltestMaskinportenService>();
    }
}
