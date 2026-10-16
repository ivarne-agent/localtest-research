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
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.Services.Interfaces;
using Altinn.Platform.Authentication.Localtest.Identity;
using Altinn.Platform.Authentication.Localtest.Keys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
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
    LocaltestTicketStore tickets,
    IOidcServerService oidcServer,
    IServer server,
    IHttpClientFactory httpClientFactory,
    IOptions<GeneralSettings> generalSettings,
    IOptions<LocaltestAuthenticationOptions> options,
    TimeProvider timeProvider) : ControllerBase
{
    private const string DefaultServiceOwnerScopes = "altinn:serviceowner/instances.read altinn:serviceowner/instances.write";

    /// <summary>Org token for an app running locally: the app backend calls this instead of Maskinporten.</summary>
    [HttpGet("GetTestOrgToken")]
    public async Task<IActionResult> GetTestOrgToken(
        [FromQuery] string org,
        [FromQuery] string orgNumber = "991825827",
        [FromQuery] int authenticationLevel = 3,
        [FromQuery] string? scopes = null,
        CancellationToken cancellationToken = default)
    {
        var maskinportenToken = MintMaskinportenToken(orgNumber, string.IsNullOrWhiteSpace(scopes) ? DefaultServiceOwnerScopes : scopes);
        var exchanged = await Exchange("maskinporten", maskinportenToken, cancellationToken);
        return exchanged is null ? StatusCode(502, "The production exchange/maskinporten endpoint rejected the locally minted token.") : Ok(exchanged);
    }

    /// <summary>User token without a browser: runs the production sign-in with the picked identity and returns the cookie JWT.</summary>
    [HttpGet("GetTestUserToken")]
    public async Task<IActionResult> GetTestUserToken(
        [FromQuery] int userId,
        [FromQuery] int authenticationLevel = 3,
        CancellationToken cancellationToken = default)
    {
        var register = HttpContext.RequestServices.GetRequiredService<RegisterLocaltestClient>();
        var user = (await register.GetTestUsers(cancellationToken)).FirstOrDefault(u => u.UserId == userId);
        if (user is null)
        {
            return NotFound($"No test user with userId {userId}.");
        }

        var goTo = new Uri(generalSettings.Value.BaseUrl.TrimEnd('/') + "/");
        var authorize = await oidcServer.AuthorizeUnregisteredClient(
            new AuthorizeUnregisteredClientRequest { GoTo = goTo.ToString(), CorrelationId = Guid.NewGuid() },
            cancellationToken);
        if (authorize.Kind != AuthorizeResultKind.RedirectUpstream || authorize.UpstreamAuthorizeUrl is null)
        {
            return StatusCode(500, $"Production authorize did not redirect upstream: {authorize.Kind} {authorize.LocalErrorMessage}");
        }

        var query = QueryHelpers.ParseQuery(authorize.UpstreamAuthorizeUrl.Query);
        var acr = authenticationLevel >= 4 ? "idporten-loa-high" : options.Value.DefaultAcr;
        var code = tickets.Issue(user.Ssn, user.UserId, acr, query["nonce"].FirstOrDefault());
        var callback = await oidcServer.HandleUpstreamCallback(
            new UpstreamCallbackInput { Code = code, State = query["state"].FirstOrDefault(), ClientIp = HttpContext.Connection.RemoteIpAddress ?? System.Net.IPAddress.Loopback, UserAgentHash = null, CorrelationId = Guid.NewGuid() },
            existingSessionHandle: null,
            cancellationToken);
        if (callback.Kind != UpstreamCallbackResultKind.RedirectToGoTo)
        {
            return StatusCode(callback.StatusCode ?? 500, $"Production sign-in failed: {callback.Kind} {callback.LocalErrorMessage}");
        }

        foreach (var cookie in callback.Cookies)
        {
            Response.Cookies.Append(cookie.Name, cookie.Value ?? string.Empty, new CookieOptions { HttpOnly = cookie.HttpOnly, Secure = cookie.Secure, Path = cookie.Path ?? "/", Domain = cookie.Domain, Expires = cookie.Expires, SameSite = cookie.SameSite });
        }

        var jwt = callback.Cookies.FirstOrDefault(c => c.Name == generalSettings.Value.JwtCookieName)?.Value;
        return jwt is null ? StatusCode(500, "Sign-in produced no cookie token.") : Ok(jwt);
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
