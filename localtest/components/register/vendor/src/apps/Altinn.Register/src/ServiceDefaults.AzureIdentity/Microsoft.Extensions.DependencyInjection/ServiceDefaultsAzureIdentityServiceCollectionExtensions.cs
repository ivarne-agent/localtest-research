using Altinn.Authorization.ServiceDefaults.AzureIdentity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceDefaultsAzureIdentityServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds <see cref="ITokenCredentialProvider"/> service to the service collection,
        /// and configures it with default options.
        /// </summary>
        /// <returns><paramref name="services"/>.</returns>
        public IServiceCollection AddTokenCredentialProvider()
        {
            if (services.Contains(Marker.ServiceDescriptor))
            {
                return services;
            }

            services.Add(Marker.ServiceDescriptor);
            services.AddSingleton<ITokenCredentialProvider, DefaultTokenCredentialProvider>();
            services.AddOptions<TokenCredentialConfigurationOptions>();
            services.AddSingleton<IConfigureOptions<TokenCredentialOptions>, ConfigureCredentialOptionsFromConfiguration>();
            services.AddSingleton<IConfigureOptions<TokenCredentialConfigurationOptions>, ConfigureCredentialConfigurationOptionsFromConfiguration>();
            services.AddSingleton<IOptionsChangeTokenSource<TokenCredentialConfigurationOptions>>(s =>
            {
                return new ConfigurationChangeTokenSource<TokenCredentialConfigurationOptions>(
                    s.GetRequiredService<IConfiguration>().GetSection("Altinn:AzureIdentity"));
            });

            return services;
        }
    }

    private sealed class Marker
    {
        public static readonly ServiceDescriptor ServiceDescriptor = new(typeof(Marker), typeof(Marker), ServiceLifetime.Singleton);
    }
}
