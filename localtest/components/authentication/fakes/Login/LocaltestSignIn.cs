#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// Runs the production browser sign-in without a browser: production authorize (which redirects to the upstream
/// provider, the localtest app's fake ID-porten), the picker's form post there for the chosen identity (which answers
/// with a one-time code), and the production upstream callback, which redeems the code at the provider's token endpoint,
/// validates the tokens, creates the session and returns the cookies. Used by <c>/Home/LogInTestUser</c> and
/// <c>/Home/GetTestUserToken</c>.
/// </summary>
public sealed class LocaltestSignIn(
    IOidcServerService oidcServer,
    IHttpClientFactory httpClientFactory,
    IOptions<GeneralSettings> generalSettings)
{
    /// <summary>The named client for the picker post: it must see the provider's redirect, not follow it.</summary>
    public const string PickerClient = "localtest-idporten-picker";

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

        // The picker's form, as a browser would post it: the authorize request's parameters plus the chosen user and level.
        var query = QueryHelpers.ParseQuery(authorize.UpstreamAuthorizeUrl.Query);
        var form = query.ToDictionary(p => p.Key, p => p.Value.ToString());
        form["UserSelect"] = user.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        form["AuthenticationLevel"] = level.Level.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var picker = new UriBuilder(authorize.UpstreamAuthorizeUrl) { Query = string.Empty }.Uri;
        using var response = await httpClientFactory.CreateClient(PickerClient).PostAsync(picker, new FormUrlEncodedContent(form), cancellationToken);
        var location = response.Headers.Location;
        var code = location is null ? null : QueryHelpers.ParseQuery(location.Query)["code"].FirstOrDefault();
        if (string.IsNullOrEmpty(code))
        {
            return (null, new Failure(502, $"The ID-porten picker at {picker} answered {(int)response.StatusCode} without a code: {await response.Content.ReadAsStringAsync(cancellationToken)}"));
        }

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
