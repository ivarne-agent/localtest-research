#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Authentication.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Authentication.Localtest.Keys;

/// <summary>An RSA key generated at startup. It signs the Maskinporten-shaped tokens the localtest token endpoints mint.</summary>
public sealed class LocaltestSigningKeys
{
    public LocaltestSigningKeys()
    {
        var rsa = RSA.Create(2048);
        Key = new RsaSecurityKey(rsa) { KeyId = "localtest-" + Guid.NewGuid().ToString("N")[..8] };
        PublicJwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)) { KeyId = Key.KeyId });
        PublicJwk.Use = "sig";
        PublicJwk.Alg = SecurityAlgorithms.RsaSha256;
    }

    public RsaSecurityKey Key { get; }

    public JsonWebKey PublicJwk { get; }

    public SigningCredentials SigningCredentials => new(Key, SecurityAlgorithms.RsaSha256);
}

/// <summary>
/// Fetches signing keys over plain http (production requires https for metadata), and answers for the
/// localtest Maskinporten issuer without a round trip.
/// </summary>
public sealed class LocaltestSigningKeysRetriever(HttpClient httpClient, LocaltestSigningKeys keys) : ISigningKeysRetriever
{
    public async Task<ICollection<SecurityKey>> GetSigningKeys(string url)
    {
        if (url.Contains("/localtest/maskinporten/", StringComparison.OrdinalIgnoreCase))
        {
            return [keys.Key];
        }

        var metadata = await Fetch(url, CancellationToken.None);
        if (!metadata.TryGetProperty("jwks_uri", out var jwksUri))
        {
            return [];
        }

        var jwks = await httpClient.GetStringAsync(jwksUri.GetString()!);
        return new JsonWebKeySet(jwks).GetSigningKeys();
    }

    public async Task<string?> GetIssuer(string url, CancellationToken cancellationToken = default)
    {
        var metadata = await Fetch(url, cancellationToken);
        return metadata.TryGetProperty("issuer", out var issuer) ? issuer.GetString() : null;
    }

    private async Task<JsonElement> Fetch(string url, CancellationToken cancellationToken)
    {
        using var doc = JsonDocument.Parse(await httpClient.GetStringAsync(url, cancellationToken));
        return doc.RootElement.Clone();
    }
}

/// <summary>Platform access tokens are not signature-checked locally; nothing ever needs these keys.</summary>
public sealed class LocaltestPublicSigningKeyProvider : IPublicSigningKeyProvider
{
    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
        => Task.FromResult<IEnumerable<SecurityKey>>([]);
}
