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

/// <summary>
/// Fetches provider metadata and signing keys over plain http, which production's <c>HttpDocumentRetriever</c> refuses.
/// No cache: the localtest app's fake ID-porten and Maskinporten generate their keys at start, and a fetch per
/// validation picks up a restarted one at once.
/// </summary>
public sealed class LocaltestSigningKeysRetriever(HttpClient httpClient) : ISigningKeysRetriever
{
    public async Task<ICollection<SecurityKey>> GetSigningKeys(string url)
    {
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
