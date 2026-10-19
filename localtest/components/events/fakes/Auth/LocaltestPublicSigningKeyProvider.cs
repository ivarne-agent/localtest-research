#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Events.Localtest.Auth;

/// <summary>
/// Stands in for the Key Vault backed provider: returns the shared localtest certificate
/// (<c>AccessTokenSettings:AccessTokenSigningCertificateFullPath</c>, the pfx every app and component signs platform
/// access tokens with) for every issuer, so the production <c>AccessTokenHandler</c> validates the apps' tokens for
/// real. Without the setting no keys are returned and every platform access token is rejected.
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
