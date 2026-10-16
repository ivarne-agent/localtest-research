using Altinn.Register.Core.Ccr;
using Altinn.Register.Integrations.Ccr.Xml;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class CcrIntegrationXmlServiceCollectionExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers services for CCR XML processing.
        /// </summary>
        public IServiceCollection AddCcrXmlProcessor()
        {
            services.TryAddSingleton<ICcrXmlProcessor, CcrXmlProcessor>();

            return services;
        }
    }
}
