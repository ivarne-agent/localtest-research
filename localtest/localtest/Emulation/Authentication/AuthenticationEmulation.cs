using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Localtest.Home;
using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Tokens;
using Microsoft.AspNetCore.WebUtilities;

namespace Localtest.Emulated;

/// <summary>
/// Authentication in minimal mode, under <c>authentication/api/v1/</c>: the OIDC discovery and JWKS (the shared
/// certificate's key, which Storage and the apps validate the tokens with), the browser sign-in through the fake
/// ID-porten's picker (<c>authentication?goTo=</c>, <c>upstream/callback</c>), <c>logout</c>, <c>refresh</c> and the
/// exchanges (<c>maskinporten</c>, <c>altinnstudio</c>, <c>id-porten</c>). <see cref="InProcessAuthentication"/> gives
/// the front door's own Home and provisioning code the same sign-in and exchanges without HTTP.
/// </summary>
public sealed class AuthenticationEmulation(
    TokenMinter minter,
    TestDataStore store,
    AuthorizationCodes codes,
    IdPortenSettings idPorten,
    MaskinportenSettings maskinporten,
    [FromKeyedServices(MaskinportenApi.KeyName)] SigningKey maskinportenKey,
    [FromKeyedServices(IdPortenSettings.KeyName)] SigningKey idPortenKey,
    LocaltestSettings localtest)
{
    public const string Service = "authentication";

    /// <summary>The client id the emulation signs in at the fake ID-porten with, as production Authentication's localtest configuration.</summary>
    public const string ClientId = "localtest";

    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    // Sign-ins between the redirect to the picker and the callback, by state.
    private readonly ConcurrentDictionary<string, PendingSignIn> _pending = new(StringComparer.Ordinal);

    public string CallbackUri => localtest.PlatformEndpoint + "authentication/api/v1/upstream/callback";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/openid/.well-known/openid-configuration", (AuthenticationEmulation authentication) => Results.Json(
            new DiscoveryDocument(
                authentication.Issuer,
                authentication.Issuer + ".well-known/openid-configuration/jwks",
                authentication.Issuer + "authorize",
                authentication.Issuer + "token",
                authentication.Issuer + "logout",
                ["token"],
                ["pairwise"],
                ["RS256"]),
            EmulationJsonContext.Default.DiscoveryDocument));

        api.MapGet("/openid/.well-known/openid-configuration/jwks", (TokenMinter minter) => Results.Json(minter.Jwks(), EmulationJsonContext.Default.JsonWebKeySet));

        api.MapGet("/authentication", (HttpRequest request, AuthenticationEmulation authentication) => authentication.StartSignIn(request));

        api.MapGet("/upstream/callback", (HttpContext context, AuthenticationEmulation authentication) => authentication.Callback(context));

        api.MapGet("/logout", (HttpContext context, AuthenticationEmulation authentication) =>
        {
            foreach (var cookie in authentication.ClearCookies())
            {
                context.Response.Headers.Append("Set-Cookie", cookie);
            }

            return Results.Redirect(authentication.Localtest.PlatformEndpoint);
        });

        api.MapGet("/refresh", (HttpRequest request, TokenMinter minter) =>
        {
            var (payload, error) = minter.Validate(Jwt.FromRequest(request));
            return payload is { } claims
                ? Results.Text(minter.Reissue(claims, newLifetime: true))
                : Results.Text($"No valid Altinn token to refresh: {error}.", statusCode: StatusCodes.Status401Unauthorized);
        });

        api.MapGet("/exchange/{provider}", (string provider, bool? test, HttpRequest request, AuthenticationEmulation authentication) =>
        {
            var authorization = request.Headers.Authorization.ToString();
            var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization["Bearer ".Length..].Trim() : null;
            var (exchanged, error, status) = authentication.Exchange(provider, token, test ?? false);
            return exchanged is not null ? Results.Text(exchanged) : Results.Text(error, statusCode: status);
        });
    }

    public string Issuer => minter.Issuer;

    public LocaltestSettings Localtest => localtest;

    /// <summary>
    /// <c>authentication?goTo=</c>: to the fake ID-porten's picker with state, nonce and an S256 PKCE challenge, as
    /// production redirects to its upstream. A <c>goTo</c> off the platform's host goes to the start page instead.
    /// </summary>
    private IResult StartSignIn(HttpRequest request)
    {
        var goTo = request.Query["goTo"].ToString();
        if (!Uri.TryCreate(goTo, UriKind.Absolute, out var target)
            || !string.Equals(target.Host, new Uri(localtest.PlatformEndpoint).Host, StringComparison.OrdinalIgnoreCase))
        {
            return Results.Redirect(localtest.PlatformEndpoint);
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var expired in _pending.Where(p => now - p.Value.Started > PendingLifetime).Select(p => p.Key).ToList())
        {
            _pending.TryRemove(expired, out _);
        }

        var state = TokenMinter.Random();
        var verifier = TokenMinter.Random() + TokenMinter.Random();
        _pending[state] = new PendingSignIn(target.AbsoluteUri, verifier, now);
        var query = new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = ClientId,
            ["redirect_uri"] = CallbackUri,
            ["scope"] = "openid",
            ["state"] = state,
            ["nonce"] = TokenMinter.Random(),
            ["code_challenge"] = Challenge(verifier),
            ["code_challenge_method"] = "S256",
        };
        if (request.Query["acr_values"].ToString() is { Length: > 0 } acr)
        {
            query["acr_values"] = acr;
        }

        return Results.Redirect(QueryHelpers.AddQueryString(idPorten.Issuer + "/authorize", query));
    }

    /// <summary>The picker's redirect back: the code is redeemed in process (with the PKCE check), the user signed in, back to <c>goTo</c>.</summary>
    private IResult Callback(HttpContext context)
    {
        var query = context.Request.Query;
        if (!_pending.TryRemove(query["state"].ToString(), out var pending))
        {
            return Results.Text("Unknown or used state: start the sign-in again from authentication/api/v1/authentication?goTo=…", statusCode: StatusCodes.Status400BadRequest);
        }

        if (codes.Redeem(query["code"].ToString()) is not { } grant || grant.RedirectUri != CallbackUri)
        {
            return Results.Text("Unknown, used or expired code.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (grant.CodeChallenge is { } challenge && challenge != Challenge(pending.Verifier))
        {
            return Results.Text("The code was issued for another PKCE challenge.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (store.Current.UsersByUserId.GetValueOrDefault(grant.UserId) is not { } user)
        {
            return Results.Text($"No test user with userId {grant.UserId}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var level = AuthLevels.ForAcr(grant.Acr) ?? AuthLevels.ForNumber(AuthLevels.Default)!;
        foreach (var cookie in SignInCookies(user, minter.User(user, level)))
        {
            context.Response.Headers.Append("Set-Cookie", cookie);
        }

        return Results.Redirect(pending.GoTo);
    }

    /// <summary>A user's token and the cookies production's sign-in sets with it (no <c>Secure</c> on the stack's plain http).</summary>
    public (string Jwt, string[] SetCookies) SignIn(TestDataUser user, AuthLevels.Level level)
    {
        var jwt = minter.User(user, level);
        return (jwt, SignInCookies(user, jwt));
    }

    /// <summary>
    /// Production's sign-in cookies: the party cookies (the user's own party, or the preselected one from the test data),
    /// the token and the session.
    /// </summary>
    private string[] SignInCookies(TestDataUser user, string jwt)
    {
        var party = user.Party;
        if (user.Person.PreSelectedPartyId is { } preselected && preselected != party.PartyId
            && store.Current.ByPartyId.GetValueOrDefault(preselected) is { } chosen)
        {
            party = chosen;
        }

        var attributes = $"; domain={new Uri(localtest.PlatformEndpoint).Host}; path=/; samesite=lax";
        return
        [
            $"AltinnPartyId={party.PartyId}{attributes}",
            $"AltinnPartyUuid={party.PartyUuid}{attributes}",
            $"{TokenMinter.CookieName}={jwt}{attributes}; httponly",
            $"altinnsession={TokenMinter.Random()}{attributes}; httponly",
        ];
    }

    private string[] ClearCookies()
    {
        var attributes = $"; expires=Thu, 01 Jan 1970 00:00:00 GMT; domain={new Uri(localtest.PlatformEndpoint).Host}; path=/; samesite=lax";
        return [.. new[] { TokenMinter.CookieName, "altinnsession", "AltinnPartyId", "AltinnPartyUuid" }.Select(name => name + "=" + attributes)];
    }

    /// <summary>
    /// <c>exchange/{provider}</c>: an Altinn token for a Maskinporten token from the in-image fake (its signature and
    /// issuer checked), for a Studio platform token signed with the shared certificate (issuer <c>studio</c>), or for an
    /// ID-porten token from the in-image fake with an Altinn scope.
    /// </summary>
    public (string? Token, string? Error, int Status) Exchange(string provider, string? token, bool test)
    {
        switch (provider.ToLowerInvariant())
        {
            case "maskinporten":
            {
                var (payload, error) = Jwt.Validate(token, maskinportenKey.Jwks());
                if (payload is not { } claims || Jwt.Claim(claims, "iss") != maskinporten.Issuer)
                {
                    return (null, $"Not a token from localtest's Maskinporten: {error ?? "wrong issuer"}.", StatusCodes.Status401Unauthorized);
                }

                if (!claims.TryGetProperty("consumer", out var consumer) || consumer.ValueKind != JsonValueKind.Object
                    || Jwt.Claim(consumer, "ID")?.Split(':') is not [_, var orgNumber])
                {
                    return (null, "The Maskinporten token has no consumer organisation.", StatusCodes.Status401Unauthorized);
                }

                // An app's local Maskinporten client (<org>-localtest, which the app runners write) acts as ttd, as with
                // test=true: the stack's Authentication has the same rule.
                var localClient = Jwt.Claim(claims, "client_id")?.EndsWith("-localtest", StringComparison.Ordinal) == true;
                var org = Jwt.Claim(claims, "scope")?.Contains("altinn:serviceowner", StringComparison.Ordinal) == true ? OrgCode(orgNumber, test || localClient) : null;
                return (minter.Org(claims, orgNumber, org), null, StatusCodes.Status200OK);
            }

            case "altinnstudio":
            {
                var (payload, error) = minter.Validate(token);
                if (payload is not { } claims || Jwt.Claim(claims, "iss") is not ("studio" or "dev-studio" or "staging-studio"))
                {
                    return (null, $"Not a Studio token signed with the shared certificate: {error ?? "the issuer is not studio"}.", StatusCodes.Status401Unauthorized);
                }

                return (minter.Reissue(claims, newLifetime: false), null, StatusCodes.Status200OK);
            }

            case "id-porten":
            {
                var (payload, error) = Jwt.Validate(token, idPortenKey.Jwks());
                if (payload is not { } claims || Jwt.Claim(claims, "iss") != idPorten.Issuer)
                {
                    return (null, $"Not a token from localtest's ID-porten: {error ?? "wrong issuer"}.", StatusCodes.Status401Unauthorized);
                }

                if (!(Jwt.Claim(claims, "scope") ?? string.Empty).Split(' ').Any(s => s.StartsWith("altinn:", StringComparison.Ordinal)))
                {
                    return (null, "The ID-porten token has no Altinn scope.", StatusCodes.Status403Forbidden);
                }

                var user = Jwt.Claim(claims, "pid") is { } pid ? store.Current.UserBySsn(pid) : null;
                if (user is null)
                {
                    return (null, "No test user with the token's pid.", StatusCodes.Status401Unauthorized);
                }

                var level = AuthLevels.ForAcr(Jwt.Claim(claims, "acr"))?.Number ?? AuthLevels.Default;
                return (minter.IdPortenExchange(claims, user, level), null, StatusCodes.Status200OK);
            }

            default:
                return (null, $"Unknown token provider '{provider}': maskinporten, altinnstudio or id-porten.", StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>
    /// The org code of an organisation number, as production looks it up in the CDN's organisation list: the test
    /// organisations only. With <c>test=true</c> Digdir comes out as <c>ttd</c>, as in production.
    /// </summary>
    public static string? OrgCode(string orgNumber, bool test) => orgNumber switch
    {
        "991825827" => test ? "ttd" : "digdir",
        "405003309" => "ttd",
        _ => null,
    };

    private static string Challenge(string verifier) => Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private sealed record PendingSignIn(string GoTo, string Verifier, DateTimeOffset Started);
}

/// <summary>
/// The front door's own sign-in and exchanges in process (<see cref="IInProcessAuthentication"/>, used by Home's
/// <see cref="AuthenticationClient"/> in minimal mode): the same tokens as the emulated endpoints.
/// </summary>
public sealed class InProcessAuthentication(AuthenticationEmulation authentication, TestDataStore store) : IInProcessAuthentication
{
    public Task<(AuthenticationClient.SignInResult? Result, string? Error)> SignIn(TestUser user, string acr, CancellationToken cancellationToken)
    {
        if (store.Current.UsersByUserId.GetValueOrDefault(user.UserId) is not { } known)
        {
            return Task.FromResult<(AuthenticationClient.SignInResult?, string?)>((null, $"No test user with userId {user.UserId}."));
        }

        var (jwt, cookies) = authentication.SignIn(known, AuthLevels.ForAcr(acr) ?? AuthLevels.ForNumber(AuthLevels.Default)!);
        return Task.FromResult<(AuthenticationClient.SignInResult?, string?)>((new AuthenticationClient.SignInResult(cookies, jwt), null));
    }

    public Task<(string? Token, string? Error)> Exchange(string provider, string externalToken, CancellationToken cancellationToken)
    {
        var (token, error, status) = authentication.Exchange(provider, externalToken, test: true);
        return Task.FromResult(token is null ? ((string?)null, (string?)$"exchange/{provider} answered {status}: {error}") : (token, (string?)null));
    }
}

/// <summary>Production's discovery document, the fields the JWT handlers read.</summary>
public sealed record DiscoveryDocument(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("end_session_endpoint")] string EndSessionEndpoint,
    [property: JsonPropertyName("response_types_supported")] string[] ResponseTypesSupported,
    [property: JsonPropertyName("subject_types_supported")] string[] SubjectTypesSupported,
    [property: JsonPropertyName("id_token_signing_alg_values_supported")] string[] IdTokenSigningAlgValuesSupported);
