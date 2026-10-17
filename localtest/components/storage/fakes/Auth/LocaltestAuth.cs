#nullable enable
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Common.PEP.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Localtest.Auth;

/// <summary>
/// Stands in for the Key Vault backed provider, whose constructor needs a Key Vault URI. Platform access tokens are not
/// signature-checked locally (<c>AccessTokenSettings:DisableAccessTokenVerification</c> is forced), so no keys are needed.
/// </summary>
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

public sealed class LocaltestClaimAccessHandler : AuthorizationHandler<ClaimAccessRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, ClaimAccessRequirement requirement)
    {
        var claims = context.User?.Claims ?? [];
        var hasClaim = claims.Any(c => c.Type.Equals(requirement.ClaimType) && c.Value.Equals(requirement.ClaimValue));
        var designerStandIn = requirement.ClaimType == "urn:altinn:app"
            && requirement.ClaimValue == "studio.designer"
            && claims.Any(c => c.Type == "urn:altinn:org" && !string.IsNullOrEmpty(c.Value));

        if (hasClaim || designerStandIn)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }

        return Task.CompletedTask;
    }
}
