using Altinn.Register.Core.Ccr;
using Altinn.Register.Integrations.Ccr.FileImport;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class CcrIntegrationFileImportServiceCollectionExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds CCR file import-related services to the specified service collection.
        /// </summary>
        /// <remarks>This method registers all services required for CCR file import functionality. Call
        /// this method during application startup to enable CCR file processing features.</remarks>
        /// <returns>The same instance of <see cref="IServiceCollection"/> that was provided, to support method chaining.</returns>
        public IServiceCollection AddCcrFileImportServices()
        {
            services.AddCcrFileProcessor();
            services.AddCcrFileService();

            return services;
        }

        /// <summary>
        /// Registers services for CCR file processing.
        /// </summary>
        public IServiceCollection AddCcrFileProcessor()
        {
            services.TryAddSingleton<ICcrFlatFileProcessor, CcrFlatFileProcessorFactory>();

            return services;
        }

        /// <summary>
        /// Adds the CCR flat file service to the dependency injection container.
        /// </summary>
        /// <returns>The current <see cref="IServiceCollection"/> instance with the CCR flat file service registered.</returns>
        public IServiceCollection AddCcrFileService()
        {
            if (services.Contains(CcrFileServiceMarker.ServiceDescriptor))
            {
                // already registered
                return services;
            }

            services.Add(CcrFileServiceMarker.ServiceDescriptor);
            services.AddSftpClient(nameof(ICcrFlatFileService)).BindConfiguration("Altinn:register:PartyImport:Ccr:Sftp").ValidateDataAnnotations();
            services.TryAddSingleton<ICcrFlatFileService, CcrDataTransfer>();

            return services;
        }

        private OptionsBuilder<SftpClientSettings> AddSftpClient(string name)
        {
            services.TryAddSingleton<INetworkFileSystemClientFactory, DefaultSftpClientFactory>();
            return services.AddOptions<SftpClientSettings>(name);
        }

    }

    private sealed class CcrFileServiceMarker
    {
        public static readonly ServiceDescriptor ServiceDescriptor = ServiceDescriptor.Singleton<CcrFileServiceMarker, CcrFileServiceMarker>();
    }
}
