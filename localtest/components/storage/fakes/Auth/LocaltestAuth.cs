#nullable enable
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Common.PEP.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Storage.Localtest.Auth;

/// <summary>
/// Stands in for the Key Vault backed provider, whose constructor needs a Key Vault URI. Platform access tokens are not
/// signature-checked locally (<c>AccessTokenSettings:DisableAccessTokenVerification</c> is forced), so no keys are needed.
/// </summary>
public sealed class LocaltestPublicSigningKeyProvider : IPublicSigningKeyProvider
{
    public Task<IEnumerable<SecurityKey>> GetSigningKeys(string issuer)
        => Task.FromResult<IEnumerable<SecurityKey>>([]);
}

/// <summary>
/// Outgoing platform access tokens (Storage to Register). Production signs them with a Key Vault certificate and sends
/// no header when it has none; the localtest components only check that the header is present, so a short-lived token
/// signed with a key generated at startup is enough.
/// </summary>
public sealed class LocaltestAccessTokenGenerator : IAccessTokenGenerator
{
    private static readonly SigningCredentials Credentials = new(
        new RsaSecurityKey(RSA.Create(2048)) { KeyId = "localtest-storage" },
        SecurityAlgorithms.RsaSha256);

    public string GenerateAccessToken(string issuer, string app)
    {
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: [new Claim("urn:altinn:app", app)],
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: Credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateAccessToken(string issuer, string app, X509Certificate2 certificate)
        => GenerateAccessToken(issuer, app);
}

/// <summary>
/// Replaces the PEP <c>ClaimAccessHandler</c> (which also calls <c>Fail()</c>, so it cannot be complemented, only replaced).
/// Same rule as production, plus: <c>urn:altinn:app=studio.designer</c> is also met by a service-owner token
/// (<c>urn:altinn:org</c> claim, e.g. from <c>/Home/GetTestOrgToken</c>), because localtest has no Designer token issuer.
/// </summary>
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
