using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Altinn.Register.Core.Mediator;

/// <summary>
/// Provides the API source to use for endpoints.
/// </summary>
internal sealed class ApiSourceSwitchProvider
    : IDisposable
{
    private readonly ConcurrentDictionary<string, ApiSource> _cache = new();
    private readonly IConfiguration _configuration;
    private readonly IDisposable _registration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiSourceSwitchProvider"/> class.
    /// </summary>
    public ApiSourceSwitchProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        _registration = ChangeToken.OnChange(
            configuration.GetReloadToken,
            static cache => cache.Clear(),
            _cache);
    }

    /// <summary>
    /// Get the <see cref="ApiSource"/> to use for the specified endpoint.
    /// </summary>
    /// <param name="endpointName">The endpoint name.</param>
    /// <returns>A <see cref="ApiSource"/>.</returns>
    public ApiSource GetSourceForEndpoint(string endpointName)
    {
        const ApiSource DEFAULT = ApiSource.A2;

        return _cache.GetOrAdd(endpointName, GetSourceForEndpointFromConfiguration, _configuration);

        static ApiSource GetSourceForEndpointFromConfiguration(string endpointName, IConfiguration configuration)
        {
            var stringValue = configuration[$"Altinn:register:ApiSource:Endpoints:{endpointName}"]
                ?? configuration["Altinn:register:ApiSource:Default"];

            if (string.IsNullOrEmpty(stringValue))
            {
                return DEFAULT;
            }

            if (string.Equals(stringValue, "DB", StringComparison.OrdinalIgnoreCase))
            {
                return ApiSource.DB;
            }

            return DEFAULT;
        }
    }

    /// <inheritdoc/>
    void IDisposable.Dispose()
    {
        _registration.Dispose();
    }
}
