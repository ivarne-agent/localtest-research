#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Authorization.Localtest.Infrastructure;

/// <summary>
/// Outgoing platform access tokens (to Register, ...) signed with a key made at start: the localtest services check
/// that the header is present, not its signature (no shared certificate yet).
/// </summary>
public sealed class LocaltestAccessTokenGenerator : IAccessTokenGenerator
{
    private readonly SigningCredentials _credentials = new(new RsaSecurityKey(RSA.Create(2048)) { KeyId = "localtest-authorization" }, SecurityAlgorithms.RsaSha256);
    private readonly JsonWebTokenHandler _handler = new();

    public string GenerateAccessToken(string issuer, string app) => Generate(issuer, app);

    public string GenerateAccessToken(string issuer, string app, X509Certificate2 certificate) => Generate(issuer, app);

    private string Generate(string issuer, string app)
        => _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Claims = new Dictionary<string, object> { ["urn:altinn:app"] = app },
            Expires = DateTime.UtcNow.AddMinutes(10),
            SigningCredentials = _credentials,
        });
}
