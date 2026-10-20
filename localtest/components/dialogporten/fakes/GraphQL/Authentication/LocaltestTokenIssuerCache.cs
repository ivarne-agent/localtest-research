using AsyncKeyedLock;
using Digdir.Domain.Dialogporten.GraphQL.Common;
using Digdir.Domain.Dialogporten.GraphQL.Common.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace Digdir.Domain.Dialogporten.GraphQL.Localtest.Authentication;

/// <summary>
/// The GraphQL host's <see cref="TokenIssuerCache"/> with one change: the OpenID metadata is fetched through a document
/// retriever that allows plain http, because the localtest Authentication, Maskinporten and ID-porten publish theirs over http.
/// </summary>
public sealed class LocaltestTokenIssuerCache : ITokenIssuerCache, IDisposable
{
    private readonly Dictionary<string, string> _issuerMappings = [];
    private readonly AsyncNonKeyedLocker _initializationSemaphore = new(1);
    private bool _initialized;
    private readonly IReadOnlyCollection<JwtBearerTokenSchemasOptions> _jwtTokenSchemas;

    public LocaltestTokenIssuerCache(IOptions<GraphQlSettings> apiSettings)
    {
        _jwtTokenSchemas = apiSettings
            .Value
            .Authentication
            .JwtBearerTokenSchemas
            ?? throw new ArgumentException("JwtBearerTokenSchemas is required.");
    }

    public async Task<string?> GetIssuerForScheme(string schemeName)
    {
        await EnsureInitializedAsync();

        return _issuerMappings.TryGetValue(schemeName, out var issuer)
            ? issuer : null;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
        {
            return;
        }

        using var _ = await _initializationSemaphore.LockAsync();
        if (_initialized)
        {
            return;
        }

        foreach (var schema in _jwtTokenSchemas)
        {
            var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                schema.WellKnown,
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = false });
            var config = await configManager.GetConfigurationAsync();
            _issuerMappings[schema.Name] = config.Issuer;
        }

        _initialized = true;
    }

    public void Dispose() => _initializationSemaphore.Dispose();
}
