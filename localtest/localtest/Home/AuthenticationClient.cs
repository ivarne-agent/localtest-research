using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Localtest.IdPorten;
using Localtest.Proxy;
using Microsoft.AspNetCore.WebUtilities;

namespace Localtest.Home;

/// <summary>
/// Production Authentication, called over HTTP from <c>/Home</c>: the browser sign-in run headlessly, and the token
/// exchanges. Calls go straight to the component's upstream with the public <c>Host</c>, as the proxy forwards a browser's.
/// </summary>
public sealed class AuthenticationClient(IHttpClientFactory httpClientFactory, PlatformRoutes platformRoutes, LocaltestSettings localtest, AuthorizationCodes codes)
{
    /// <summary>The named client: Authentication's redirects and cookies are read, not followed or stored.</summary>
    public const string HttpClientName = "localtest-authentication";

    public sealed record SignInResult(string[] SetCookies, string Jwt);

    /// <summary>
    /// Signs <paramref name="user"/> in at <paramref name="acr"/> as a browser would, minus the picker page: production
    /// <c>authentication?goTo=</c> redirects to the fake ID-porten's <c>authorize</c>; the code for the chosen identity is
    /// issued here; production <c>upstream/callback</c> redeems it at the fake's token endpoint, validates the tokens,
    /// creates the session and answers the cookies. The JWT carries the user's own party, as production's does.
    /// </summary>
    public async Task<(SignInResult? Result, string? Error)> SignIn(TestUser user, string acr, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var authorize = await client.SendAsync(Request(HttpMethod.Get, "authentication/api/v1/authentication?goTo=" + Uri.EscapeDataString(localtest.PlatformEndpoint)), cancellationToken);
        if (authorize.Headers.Location is not { } upstream || !upstream.IsAbsoluteUri)
        {
            return (null, $"Production authorize answered {(int)authorize.StatusCode} without a redirect to the identity provider.");
        }

        var query = QueryHelpers.ParseQuery(upstream.Query);
        var redirectUri = query["redirect_uri"].ToString();
        if (string.IsNullOrEmpty(redirectUri) || string.IsNullOrEmpty(query["state"]))
        {
            return (null, $"Production authorize redirected to {upstream.GetLeftPart(UriPartial.Path)} without state or redirect_uri.");
        }

        var code = codes.Issue(new AuthorizationGrant(
            string.IsNullOrEmpty(user.Ssn) ? null : user.Ssn,
            user.UserId,
            acr,
            query["client_id"].ToString(),
            redirectUri,
            NullIfEmpty(query["nonce"]),
            NullIfEmpty(query["code_challenge"]),
            default));
        var callback = QueryHelpers.AddQueryString(new Uri(redirectUri).PathAndQuery.TrimStart('/'), new Dictionary<string, string?> { ["code"] = code, ["state"] = query["state"] });
        using var response = await client.SendAsync(Request(HttpMethod.Get, callback), cancellationToken);
        var setCookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToArray() : [];
        var jwt = setCookies.Select(c => CookieValue(c, "AltinnStudioRuntime")).FirstOrDefault(v => !string.IsNullOrEmpty(v));
        return jwt is null
            ? (null, $"Production sign-in answered {(int)response.StatusCode} without the AltinnStudioRuntime cookie: {await response.Content.ReadAsStringAsync(cancellationToken)}")
            : (new SignInResult(setCookies, jwt), null);
    }

    /// <summary>
    /// Production <c>exchange/{provider}</c> with <paramref name="externalToken"/>; the Altinn token, or null when the
    /// exchange refused it. <c>test=true</c> lets a Digdir consumer with a service-owner scope come out as <c>ttd</c>.
    /// </summary>
    public async Task<(string? Token, string? Error)> Exchange(string provider, string externalToken, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = Request(HttpMethod.Get, $"authentication/api/v1/exchange/{provider}?test=true");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", externalToken);
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? (body.Trim().Trim('"'), null)
            : (null, $"exchange/{provider} answered {(int)response.StatusCode}: {body}");
    }

    /// <summary>
    /// The production cookies for the browser: the party cookies set to the selected party (the old localtest's
    /// <c>UserSelect=userId.partyId</c>; the JWT keeps the user's own party), and <c>Secure</c> removed, as the proxy
    /// removes it from forwarded responses, because the browser talks plain http to the stack.
    /// </summary>
    public static IEnumerable<string> ForBrowser(IEnumerable<string> setCookies, int partyId, Guid partyUuid)
    {
        foreach (var setCookie in setCookies)
        {
            var parts = setCookie.Split(';').Select(p => p.Trim()).Where(p => !string.Equals(p, "secure", StringComparison.OrdinalIgnoreCase)).ToList();
            var name = parts[0].Split('=', 2)[0];
            parts[0] = name switch
            {
                "AltinnPartyId" => name + "=" + partyId.ToString(CultureInfo.InvariantCulture),
                "AltinnPartyUuid" => name + "=" + partyUuid.ToString(),
                _ => parts[0],
            };
            yield return string.Join("; ", parts);
        }
    }

    private HttpRequestMessage Request(HttpMethod method, string pathAndQuery)
    {
        var request = new HttpRequestMessage(method, platformRoutes.Upstreams["authentication"].TrimEnd('/') + "/" + pathAndQuery);
        request.Headers.Host = localtest.PublicHost;
        return request;
    }

    private static string? CookieValue(string setCookie, string name)
    {
        var pair = setCookie.Split(';', 2)[0].Split('=', 2);
        return pair.Length == 2 && pair[0].Trim() == name ? WebUtility.UrlDecode(pair[1].Trim()) : null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
