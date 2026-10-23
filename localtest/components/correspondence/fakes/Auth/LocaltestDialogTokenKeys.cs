using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ScottBrady.IdentityModel;
using ScottBrady.IdentityModel.Tokens;

namespace Altinn.Correspondence.Localtest.Auth;

/// <summary>
/// Dialogporten's dialog-token signing keys (EdDSA), read from <c>{PlatformGatewayUrl}dialogporten/api/v1/.well-known/jwks.json</c>
/// as upstream's <c>EdDsaSecurityKeysCacheService</c> does, but on demand: the first token, and any token whose <c>kid</c>
/// is not in the list, fetches the set again, at most once per <c>DialogTokenKeyRefreshSeconds</c> (10). Production
/// fetches at start and every 12 hours. The resolver is synchronous (it is called from the token handler), so the fetch
/// is a blocking request with a 5 second timeout; a failed fetch keeps the previous keys.
/// </summary>
public sealed class LocaltestDialogTokenKeys(
    IHttpClientFactory httpClientFactory,
    IOptions<LocaltestCorrespondenceOptions> options,
    TimeProvider timeProvider,
    ILogger<LocaltestDialogTokenKeys> logger)
{
    private readonly Lock _gate = new();
    private volatile IReadOnlyList<SecurityKey> _keys = [];
    private DateTimeOffset? _lastFetch;

    public string JwksUrl => options.Value.DialogTokenJwksUrl;

    public IReadOnlyList<string> KeyIds => [.. _keys.Select(k => k.KeyId)];

    public DateTimeOffset? LastFetch => _lastFetch;

    public IEnumerable<SecurityKey> Resolve(string? kid)
    {
        var found = Match(_keys, kid);
        if (found.Count > 0)
        {
            return found;
        }

        lock (_gate)
        {
            found = Match(_keys, kid);
            var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.DialogTokenKeyRefreshSeconds));
            if (found.Count == 0 && (_lastFetch is null || timeProvider.GetUtcNow() - _lastFetch >= interval))
            {
                Fetch(kid);
                found = Match(_keys, kid);
            }
        }

        // No match: hand the handler every key, so it reports a signature failure rather than a missing key.
        return found.Count > 0 ? found : _keys;
    }

    private static List<SecurityKey> Match(IReadOnlyList<SecurityKey> keys, string? kid)
        => string.IsNullOrEmpty(kid) ? [.. keys] : [.. keys.Where(k => string.Equals(k.KeyId, kid, StringComparison.Ordinal))];

    private void Fetch(string? kid)
    {
        _lastFetch = timeProvider.GetUtcNow();
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            // SendAsync, not Send: the service-discovery handler resolves the name in SendAsync only (a synchronous Send
            // passes it by and goes to dialogporten-webapi:80). The key resolver is synchronous, hence the wait.
            using var response = client.GetAsync(JwksUrl).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var jwks = new JsonWebKeySet(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var keys = new List<SecurityKey>();
            foreach (var jwk in jwks.Keys)
            {
                if (ExtendedJsonWebKeyConverter.TryConvertToEdDsaSecurityKey(jwk, out var key))
                {
                    if (string.IsNullOrEmpty(key.KeyId))
                    {
                        key.KeyId = jwk.Kid;
                    }

                    keys.Add(key);
                }
            }

            _keys = keys;
            logger.LogInformation("Localtest: fetched {Count} dialog-token key(s) from {Url} for kid {Kid}: {Kids}",
                keys.Count, JwksUrl, kid ?? "(none)", string.Join(", ", keys.Select(k => k.KeyId)));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Localtest: could not fetch the dialog-token keys from {Url}; keeping {Count} key(s)", JwksUrl, _keys.Count);
        }
    }
}
