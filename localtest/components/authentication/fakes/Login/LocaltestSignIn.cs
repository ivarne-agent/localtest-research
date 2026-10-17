#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Platform.Authentication.Configuration;
using Altinn.Platform.Authentication.Core.Models.Oidc;
using Altinn.Platform.Authentication.Core.Services.Interfaces;
using Altinn.Platform.Authentication.Localtest.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Authentication.Localtest.Login;

/// <summary>
/// Runs the production browser sign-in without a browser: production authorize (which redirects to the picker),
/// a one-time code for the chosen identity, and the production upstream callback, which creates the session and
/// returns the cookies. Used by <c>/Home/LogInTestUser</c> and <c>/Home/GetTestUserToken</c>.
/// </summary>
public sealed class LocaltestSignIn(
    IOidcServerService oidcServer,
    LocaltestTicketStore tickets,
    IOptions<GeneralSettings> generalSettings)
{
    public sealed record Result(IReadOnlyList<CookieInstruction> Cookies, string Jwt);

    public sealed record Failure(int StatusCode, string Message);

    /// <summary>Signs <paramref name="user"/> in at <paramref name="level"/>; the JWT carries the user's own party.</summary>
    public async Task<(Result? Result, Failure? Failure)> SignIn(RegisterLocaltestClient.TestUser user, LocaltestAuthLevels.Entry level, IPAddress? clientIp, CancellationToken cancellationToken)
    {
        var goTo = new Uri(generalSettings.Value.BaseUrl.TrimEnd('/') + "/");
        var authorize = await oidcServer.AuthorizeUnregisteredClient(
            new AuthorizeUnregisteredClientRequest { GoTo = goTo.ToString(), CorrelationId = Guid.NewGuid() },
            cancellationToken);
        if (authorize.Kind != AuthorizeResultKind.RedirectUpstream || authorize.UpstreamAuthorizeUrl is null)
        {
            return (null, new Failure(500, $"Production authorize did not redirect upstream: {authorize.Kind} {authorize.LocalErrorMessage}"));
        }

        var query = QueryHelpers.ParseQuery(authorize.UpstreamAuthorizeUrl.Query);

        // Persons sign in by pid (Profile lookup by ssn); self-identified users by Altinn user id.
        var code = tickets.Issue(string.IsNullOrEmpty(user.Ssn) ? null : user.Ssn, user.UserId, level.Acr, query["nonce"].FirstOrDefault());
        var callback = await oidcServer.HandleUpstreamCallback(
            new UpstreamCallbackInput { Code = code, State = query["state"].FirstOrDefault(), ClientIp = clientIp ?? IPAddress.Loopback, UserAgentHash = null, CorrelationId = Guid.NewGuid() },
            existingSessionHandle: null,
            cancellationToken);
        if (callback.Kind != UpstreamCallbackResultKind.RedirectToGoTo)
        {
            return (null, new Failure(callback.StatusCode ?? 500, $"Production sign-in failed: {callback.Kind} {callback.LocalErrorMessage}"));
        }

        var jwt = callback.Cookies.FirstOrDefault(c => c.Name == generalSettings.Value.JwtCookieName)?.Value;
        return jwt is null
            ? (null, new Failure(500, "Sign-in produced no cookie token."))
            : (new Result(callback.Cookies, jwt), null);
    }

    /// <summary>
    /// Appends the production cookies, with the party cookies replaced by the selected party when one is given
    /// (the old localtest's <c>UserSelect=userId.partyId</c>); the JWT itself keeps the user's own party.
    /// </summary>
    public void AppendCookies(HttpResponse response, Result result, int? partyId, Guid? partyUuid)
    {
        var settings = generalSettings.Value;
        foreach (var cookie in result.Cookies)
        {
            var value = cookie.Value;
            if (partyId is int id && cookie.Name == settings.AltinnPartyCookieName)
            {
                value = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (partyUuid is Guid uuid && cookie.Name == settings.AltinnPartyUuidCookieName)
            {
                value = uuid.ToString();
            }

            response.Cookies.Append(cookie.Name, value ?? string.Empty, new CookieOptions
            {
                HttpOnly = cookie.HttpOnly,
                Secure = cookie.Secure,
                Path = cookie.Path ?? "/",
                Domain = cookie.Domain,
                Expires = cookie.Expires,
                SameSite = cookie.SameSite,
            });
        }
    }
}
