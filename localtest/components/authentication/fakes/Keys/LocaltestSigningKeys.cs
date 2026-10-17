#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Authentication.Services.Interfaces;
using Microsoft.Extensions.Configuration;
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

/// <summary>
/// Stands in for the Key Vault backed provider: returns the shared localtest certificate
/// (<c>AccessTokenSettings:AccessTokenSigningCertificateFullPath</c>, the same pfx every app and component signs
/// platform access tokens with) for every issuer, so the production <c>AccessTokenHandler</c> validates the
/// tokens for real. Without the setting no keys are returned and every platform access token is rejected.
/// </summary>
public sealed class LocaltestPublicSigningKeyProvider : IPublicSigningKeyProvider
{
    private readonly Lazy<IEnumerable<SecurityKey>> _keys;

    public LocaltestPublicSigningKeyProvider(IConfiguration configuration)
    {
        _keys = new Lazy<IEnumerable<SecurityKey>>(() =>
        {
            var path = configuration["AccessTokenSettings:AccessTokenSigningCertificateFullPath"];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return [];
            }

            var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password: null);
            return [new X509SecurityKey(certificate)];
        });
    }

    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
        => Task.FromResult(_keys.Value);
}
