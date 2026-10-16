#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Model;
using Altinn.Platform.Authentication.Services.Interfaces;
using AltinnCore.Authentication.Constants;

namespace Altinn.Platform.Authentication.Localtest.Identity;

/// <summary>
/// Stands in for the upstream ID-porten token endpoint: turns the picker's one-time code into an id_token
/// carrying the picked identity. Everything after this point is production code.
/// </summary>
public sealed class LocaltestOidcProvider(LocaltestTicketStore tickets, TimeProvider timeProvider) : IOidcProvider
{
    public Task<OidcCodeResponse?> GetTokens(string authorizationCode, OidcProvider provider, string redirect_uri, string? codeVerifier, CancellationToken cancellationToken = default)
    {
        var picked = tickets.Redeem(authorizationCode);
        if (picked is null)
        {
            return Task.FromResult<OidcCodeResponse?>(null);
        }

        var now = timeProvider.GetUtcNow();
        var claims = new List<Claim>
        {
            new("sub", picked.Ssn ?? $"user-{picked.UserId}"),
            new("acr", picked.Acr),
            new("amr", "TestID"),
            new("sid", Guid.NewGuid().ToString("N")),
            new("jti", Guid.NewGuid().ToString("N")),
            new("auth_time", now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), ClaimValueTypes.Integer64),
            new("actual_iss", "localtest"),
        };

        if (!string.IsNullOrEmpty(picked.Ssn))
        {
            claims.Add(new Claim(provider.ClaimMappings?.Pid ?? "pid", picked.Ssn));
        }
        else if (picked.UserId is int userId)
        {
            // Self-identified test users carry the Altinn user id, which takes the Profile-by-userId branch in production.
            claims.Add(new Claim(AltinnCoreClaimTypes.UserId, userId.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrEmpty(picked.Nonce))
        {
            claims.Add(new Claim("nonce", picked.Nonce));
        }

        // Unsigned on purpose: the localtest upstream validator reads it without a signature check.
        var token = new JwtSecurityToken(provider.Issuer, provider.ClientId, claims, now.UtcDateTime, now.AddMinutes(5).UtcDateTime);
        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return Task.FromResult<OidcCodeResponse?>(new OidcCodeResponse
        {
            IdToken = encoded,
            AccessToken = encoded,
            TokenType = "Bearer",
            ExpiresIn = 300,
            Scope = "openid",
        });
    }

    public Task<PushedAuthorizationResponse?> PushAuthorizationRequest(OidcProvider provider, IDictionary<string, string> parameters, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Pushed authorization requests are not used by the localtest provider.");
}
