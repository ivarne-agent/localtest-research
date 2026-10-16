using Azure.Core;
using Microsoft.Extensions.Options;

namespace Altinn.Authorization.ServiceDefaults.AzureIdentity;

/// <summary>
/// Default implementation of <see cref="ITokenCredentialProvider"/>.
/// </summary>
internal sealed class DefaultTokenCredentialProvider
    : ITokenCredentialProvider
{
    private readonly IOptionsMonitor<TokenCredentialOptions> _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultTokenCredentialProvider"/> class.
    /// </summary>
    public DefaultTokenCredentialProvider(IOptionsMonitor<TokenCredentialOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public TokenCredential GetCredential(string name)
    {
        var options = _options.Get(name);
        var builder = new DefaultTokenCredentialBuilder
        {
            Name = name,
        };

        foreach (var configure in options.TokenCredentialBuilderActions)
        {
            configure(builder);
        }

        return builder.Build();
    }
}
