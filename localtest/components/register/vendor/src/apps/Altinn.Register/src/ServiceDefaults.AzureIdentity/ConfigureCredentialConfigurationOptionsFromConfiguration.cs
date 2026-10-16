using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Configures <see cref="TokenCredentialConfigurationOptions"/> from configuration.
/// </summary>
internal sealed class ConfigureCredentialConfigurationOptionsFromConfiguration
    : IConfigureNamedOptions<TokenCredentialConfigurationOptions>
    , IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly IDisposable? _dispose;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCredentialConfigurationOptionsFromConfiguration"/> class.
    /// </summary>
    public ConfigureCredentialConfigurationOptionsFromConfiguration(
        IConfiguration configuration,
        IOptionsMonitorCache<TokenCredentialConfigurationOptions> cache)
    {
        _configuration = configuration.GetSection("Altinn:AzureIdentity");
        _dispose = ChangeToken.OnChange(_configuration.GetReloadToken, () =>
        {
            cache.Clear();
        });
    }

    /// <inheritdoc/>
    public void Configure(string? name, TokenCredentialConfigurationOptions options)
    {
        if (string.Equals(name ?? Options.DefaultName, Options.DefaultName, StringComparison.Ordinal))
        {
            _configuration.GetSection("Defaults").Bind(options);
        }
        else
        {
            _configuration.GetSection("Identities").GetSection(name!).Bind(options);
        }
    }

    /// <inheritdoc/>
    public void Configure(TokenCredentialConfigurationOptions options)
        => _configuration.Bind(options);

    /// <inheritdoc/>
    public void Dispose()
    {
        _dispose?.Dispose();
    }
}
