using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Configures <see cref="TokenCredentialOptions"/> from <see cref="TokenCredentialConfigurationOptions"/>.
/// </summary>
internal sealed class ConfigureCredentialOptionsFromConfiguration
    : IConfigureNamedOptions<TokenCredentialOptions>
    , IDisposable
{
    private readonly IOptionsMonitor<TokenCredentialConfigurationOptions> _credentialConfigurationOptions;
    private readonly IDisposable? _dispose;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigureCredentialOptionsFromConfiguration"/> class.
    /// </summary>
    public ConfigureCredentialOptionsFromConfiguration(
        IOptionsMonitor<TokenCredentialConfigurationOptions> credentialConfigurationOptions,
        IOptionsMonitorCache<TokenCredentialOptions> cache)
    {
        _credentialConfigurationOptions = credentialConfigurationOptions;
        _dispose = credentialConfigurationOptions.OnChange((_, name) =>
        {
            if (name == Options.DefaultName)
            {
                cache.Clear();
            }
            else
            {
                cache.TryRemove(name);
            }
        });
    }

    /// <inheritdoc/>
    public void Configure(string? name, TokenCredentialOptions options)
    {
        var defaultOptions = _credentialConfigurationOptions.Get(Options.DefaultName);
        var namedOptions = _credentialConfigurationOptions.Get(name ?? Options.DefaultName);

        var enableEnvironmentCredential = namedOptions.Environment.Enable ?? defaultOptions.Environment.Enable ?? false;
        var enableWorkloadIdentityCredential = namedOptions.WorkloadIdentity.Enable ?? defaultOptions.WorkloadIdentity.Enable ?? true;
        var enableManagedIdentityCredential = namedOptions.ManagedIdentity.Enable ?? defaultOptions.ManagedIdentity.Enable ?? true;

        if (enableEnvironmentCredential)
        {
            options.TokenCredentialBuilderActions.Add(builder => builder.Credentials.Add(new EnvironmentCredential()));
        }

        if (enableWorkloadIdentityCredential)
        {
            options.TokenCredentialBuilderActions.Add(builder => builder.Credentials.Add(new WorkloadIdentityCredential()));
        }

        if (enableManagedIdentityCredential)
        {
            options.TokenCredentialBuilderActions.Add(builder => builder.Credentials.Add(new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned)));
        }
    }

    /// <inheritdoc/>
    public void Configure(TokenCredentialOptions options)
        => Configure(Options.DefaultName, options);

    /// <inheritdoc/>
    public void Dispose()
    {
        _dispose?.Dispose();
    }
}
