using Altinn.Register.Core.Npr;
using Altinn.Register.Integrations.Npr;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class NprIntegrationServiceCollectionExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the NPR client to the service collection.
        /// </summary>
        /// <returns>A <see cref="IHttpClientBuilder"/> for further configuring of the http client.</returns>
        public IHttpClientBuilder AddNprClient()
            => services.AddHttpClient<INprClient, NprClient>();
    }
}
