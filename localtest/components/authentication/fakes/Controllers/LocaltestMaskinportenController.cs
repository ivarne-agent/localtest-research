#nullable enable
using System;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Localtest.Keys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Altinn.Platform.Authentication.Localtest.Controllers;

/// <summary>
/// Just enough of a Maskinporten to let the production <c>exchange/maskinporten</c> endpoint validate the
/// tokens the localtest token endpoints mint: discovery metadata and a JWKS for the local signing key.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("authentication/api/v1/localtest/maskinporten")]
public sealed class LocaltestMaskinportenController(LocaltestSigningKeys keys, IOptions<GeneralSettings> generalSettings) : ControllerBase
{
    public static string Issuer(GeneralSettings settings)
        => new Uri(new Uri(settings.PlatformEndpoint), "authentication/api/v1/localtest/maskinporten/").ToString();

    [HttpGet(".well-known/oauth-authorization-server")]
    public IActionResult Metadata()
    {
        var issuer = Issuer(generalSettings.Value);
        return Ok(new
        {
            issuer,
            token_endpoint = issuer + "token",
            jwks_uri = issuer + "jwks",
            grant_types_supported = new[] { "urn:ietf:params:oauth:grant-type:jwt-bearer" },
            token_endpoint_auth_signing_alg_values_supported = new[] { "RS256" },
        });
    }

    [HttpGet("jwks")]
    public IActionResult Jwks()
        => Ok(new { keys = new[] { new { kty = keys.PublicJwk.Kty, use = "sig", alg = "RS256", kid = keys.PublicJwk.Kid, n = keys.PublicJwk.N, e = keys.PublicJwk.E } } });
}
