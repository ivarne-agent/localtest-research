using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Digdir.Domain.Dialogporten.Infrastructure.Localtest;

/// <summary>
/// Runs localtest service replacements after every production registration without a second hook in the host's
/// <c>Program.cs</c>. Production registers some of what localtest replaces (the issuer caches) with a plain
/// <c>AddSingleton</c>, which wins over anything registered earlier, so the replacement has to come last. The one hook
/// sits right after <c>WebApplication.CreateBuilder</c>, where the configuration defaults must go in (the hosts read
/// their settings eagerly while registering), and it installs this factory, which gets the finished service collection.
/// </summary>
public static class LocaltestOverrides
{
    public static void AfterAllRegistrations(IHostBuilder host, Action<IServiceCollection> overrides)
        => host.UseServiceProviderFactory(new OverridingServiceProviderFactory(overrides));

    private sealed class OverridingServiceProviderFactory(Action<IServiceCollection> overrides)
        : IServiceProviderFactory<IServiceCollection>
    {
        // The host's own default in Development, which every localtest host runs in.
        private readonly DefaultServiceProviderFactory _inner = new(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        public IServiceCollection CreateBuilder(IServiceCollection services)
        {
            overrides(services);
            return services;
        }

        public IServiceProvider CreateServiceProvider(IServiceCollection services)
            => _inner.CreateServiceProvider(_inner.CreateBuilder(services));
    }
}
