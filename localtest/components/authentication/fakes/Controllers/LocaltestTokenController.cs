#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Localtest.Identity;
using Altinn.Platform.Authentication.Localtest.Keys;
using Altinn.Platform.Authentication.Localtest.Login;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Controllers;

/// <summary>
/// Token endpoints apps and tests used against the old localtest (<c>/Home/GetTestOrgToken</c>, ...).
/// Nothing is minted directly: org tokens go through the production Maskinporten exchange with a locally
/// signed Maskinporten-shaped token, and user tokens run the production sign-in headlessly.
/// </summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("Home")]
public sealed class LocaltestTokenController(
    LocaltestSigningKeys keys,
    RegisterLocaltestClient register,
    LocaltestSignIn signIn,
    IServer server,
    IHttpClientFactory httpClientFactory,
    IOptions<GeneralSettings> generalSettings,
    TimeProvider timeProvider) : ControllerBase
{
    private const string DefaultServiceOwnerScopes = "altinn:serviceowner/instances.read altinn:serviceowner/instances.write";

    /// <summary>Org token for an app running locally: the app backend calls this instead of Maskinporten.</summary>
    /// <remarks>
    /// The production exchange decides the authentication level of the result (3 for Maskinporten); <paramref name="authenticationLevel"/>
    /// and the org code (query or last path segment) are accepted for compatibility with the old localtest; the exchange derives the org from <paramref name="orgNumber"/>.
    /// </remarks>
    [HttpGet("GetTestOrgToken")]
    [HttpGet("GetTestOrgToken/{org}")]
    public async Task<IActionResult> GetTestOrgToken(
        [FromRoute(Name = "org")] string? routeOrg,
        [FromQuery] string? org,
        [FromQuery] string orgNumber = "991825827",
        [FromQuery] int authenticationLevel = 3,
        [FromQuery] string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var maskinportenToken = MintMaskinportenToken(orgNumber, string.IsNullOrWhiteSpace(scopes) ? DefaultServiceOwnerScopes : scopes);
        var exchanged = await Exchange("maskinporten", maskinportenToken, cancellationToken);
        return exchanged is null ? StatusCode(502, "The production exchange/maskinporten endpoint rejected the locally minted token.") : Ok(exchanged);
    }

    /// <summary>
    /// User token without a browser: runs the production sign-in with the picked identity and returns the cookie JWT
    /// (and sets the cookies, with the party cookies on <paramref name="partyId"/> when given). The JWT carries the
    /// user's own party, as production's does.
    /// </summary>
    [HttpGet("GetTestUserToken")]
    [HttpGet("GetTestUserToken/{userId:int}")]
    public async Task<IActionResult> GetTestUserToken(
        [FromRoute(Name = "userId")] int? routeUserId,
        [FromQuery] int? userId,
        [FromQuery] int? partyId,
        [FromQuery] int authenticationLevel = LocaltestAuthLevels.Default,
        CancellationToken cancellationToken = default)
    {
        var id = routeUserId ?? userId;
        if (id is null)
        {
            return BadRequest("userId is required.");
        }

        if (LocaltestAuthLevels.ForLevel(authenticationLevel) is not { } level)
        {
            return BadRequest("authenticationLevel must be 0, 1, 2, 3 or 4.");
        }

        var user = (await register.GetTestUsers(cancellationToken)).FirstOrDefault(u => u.UserId == id);
        if (user is null)
        {
            return NotFound($"No test user with userId {id}.");
        }

        var party = user.FindParty(partyId ?? user.PartyId);
        if (party is null)
        {
            return BadRequest($"Test user {id} cannot act for party {partyId}.");
        }

        var (result, failure) = await signIn.SignIn(user, level, HttpContext.Connection.RemoteIpAddress, cancellationToken);
        if (failure is not null)
        {
            return StatusCode(failure.StatusCode, failure.Message);
        }

        signIn.AppendCookies(Response, result!, party.Value.PartyId, party.Value.PartyUuid);
        return Ok(result!.Jwt);
    }

    private string MintMaskinportenToken(string orgNumber, string scopes)
    {
        var now = timeProvider.GetUtcNow();
        var issuer = LocaltestMaskinportenController.Issuer(generalSettings.Value);
        var claims = new List<Claim>
        {
            new("scope", scopes),
            new("client_id", "localtest"),
            new("jti", Guid.NewGuid().ToString("N")),
            new("consumer", $$"""{"authority":"iso6523-actorid-upis","ID":"0192:{{orgNumber}}"}""", JsonClaimValueTypes.Json),
            new("token_type", "Bearer"),
        };
        var token = new JwtSecurityToken(issuer, null, claims, now.UtcDateTime, now.AddMinutes(30).UtcDateTime, keys.SigningCredentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Calls this host's own production exchange endpoint over loopback, so the real validation and claim shaping run.</summary>
    private async Task<string?> Exchange(string provider, string externalToken, CancellationToken cancellationToken)
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(a => a.Replace("://+", "://127.0.0.1").Replace("://*", "://127.0.0.1").Replace("://[::]", "://127.0.0.1").Replace("://0.0.0.0", "://127.0.0.1"))
            .FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("No http listener address to call the exchange endpoint on.");
        using var client = httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{address.TrimEnd('/')}/authentication/api/v1/exchange/{provider}?test=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", externalToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim().Trim('"');
    }
}
