#nullable enable
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Model;
using Altinn.Platform.Authentication.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Authentication.Localtest.Identity;

/// <summary>Accepts the unsigned tokens the localtest provider issues; still enforces the nonce like production.</summary>
public sealed class LocaltestUpstreamTokenValidator : IUpstreamTokenValidator
{
    private static readonly JwtSecurityTokenHandler _handler = new();

    public Task<JwtSecurityToken> ValidateTokenAsync(string token, OidcProvider provider, UpstreamTokenKind kind, string? nonce, CancellationToken cancellationToken = default)
    {
        var jwt = _handler.ReadJwtToken(token);
        if (nonce is not null)
        {
            var actual = jwt.Claims.FirstOrDefault(c => c.Type == "nonce")?.Value;
            if (actual != nonce)
            {
                throw new SecurityTokenValidationException("Invalid nonce.");
            }
        }

        return Task.FromResult(jwt);
    }
}
