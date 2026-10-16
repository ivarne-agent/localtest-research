using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Builder for configuring mass transit.
/// </summary>
public interface IMassTransitBuilder
{
    /// <summary>
    /// Gets the service collection.
    /// </summary>
    public IServiceCollection Services { get; }
}

/// <summary>
/// Implementation of <see cref="IMassTransitBuilder"/>.
/// </summary>
internal class MassTransitBuilder
    : IMassTransitBuilder
{
    private readonly IConfiguration _configuration;

    /// <summary>
    /// Initializes a new instance of <see cref="MassTransitBuilder"/>.
    /// </summary>
    public MassTransitBuilder(IServiceCollection serviceProvider, IConfiguration configuration)
    {
        Services = serviceProvider;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public IServiceCollection Services { get; }
}
