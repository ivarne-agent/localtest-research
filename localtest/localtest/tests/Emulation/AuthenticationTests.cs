using System.Net;
using System.Text.Json;
using Localtest.Emulated;
using Localtest.Maskinporten;
using Localtest.Tokens;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests.Emulation;

/// <summary>
/// The emulated Authentication: discovery and JWKS, the tokens' claim types against production's (the tables below,
/// from the vendored production code), the browser sign-in through the picker, refresh, the exchanges, and Home's
/// contract served in process.
/// </summary>
public sealed class AuthenticationTests
{
    /// <summary>
    /// A user's cookie token as production issues it after an ID-porten sign-in in the default stack:
    /// components/authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs:169-280
    /// (GetClaimsPrincipal with isAuthCookie; actual_iss is the provider claim localtest configures,
    /// fakes/LocaltestAuthenticationExtensions.cs:81) and Controllers/AuthenticationController.cs:908-934 (GenerateToken:
    /// iat, nbf, exp). urn:altinn:username only for a user with a user name; email and the external identifier only for
    /// a self-identified user.
    /// </summary>
    public static readonly string[] UserClaimTypes =
    [
        "sub", "sid", "iss", "urn:altinn:party:uuid", "jti", "urn:altinn:partyid", "urn:altinn:userid", "urn:altinn:username",
        "originaliss", "acr", "actual_iss", "urn:altinn:authlevel", "amr", "urn:altinn:authenticatemethod", "auth_time", "scope",
        "nbf", "exp", "iat",
    ];

    /// <summary>
    /// exchange/maskinporten: the Maskinporten token's claims (the in-image fake's: scope, client_id, token_type, jti,
    /// consumer) less aud, iss, client_amr and sid, plus these (Controllers/AuthenticationController.cs:430-532, 825-833;
    /// urn:altinn:org for a service-owner scope) and GenerateToken's lifetime.
    /// </summary>
    public static readonly string[] OrgClaimTypes =
    [
        "scope", "client_id", "token_type", "jti", "consumer",
        "urn:altinn:orgNumber", "urn:altinn:authenticatemethod", "urn:altinn:authlevel", "urn:altinn:org", "iss", "sid",
        "nbf", "exp", "iat",
    ];

    /// <summary>exchange/altinnstudio copies the presented claims unchanged (AuthenticationController.cs:374-417): the localtest app's Studio token's.</summary>
    public static readonly string[] StudioClaimTypes = ["iss", "urn:altinn:app", "nbf", "iat", "exp"];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string[] TypesOf(string jwt) => [.. MinimalFrontDoor.Claims(jwt).EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    [Fact]
    public async Task DiscoveryPublishesTheSharedCertificatesKeyOnThePublicOrigin()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();

        using var discovery = JsonDocument.Parse(await client.GetStringAsync("/authentication/api/v1/openid/.well-known/openid-configuration", Ct));
        var issuer = discovery.RootElement.GetProperty("issuer").GetString();
        Assert.Equal("http://local.altinn.cloud:8000/authentication/api/v1/openid/", issuer);
        var jwksUri = new Uri(discovery.RootElement.GetProperty("jwks_uri").GetString()!);
        using var jwks = JsonDocument.Parse(await client.GetStringAsync(jwksUri.PathAndQuery, Ct));
        using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(frontDoor.Certificate, null);
        Assert.Equal(certificate.Thumbprint, jwks.RootElement.GetProperty("keys")[0].GetProperty("kid").GetString());

        var token = await client.GetStringAsync("/Home/GetTestUserToken?userId=1337", Ct);
        var keys = new JsonWebKeySet([.. jwks.RootElement.GetProperty("keys").EnumerateArray().Select(k => new JsonWebKey(
            k.GetProperty("kty").GetString()!, "sig", "RS256", k.GetProperty("kid").GetString()!, k.GetProperty("n").GetString()!, k.GetProperty("e").GetString()!))]);
        var (payload, error) = Jwt.Validate(token, keys);
        Assert.Null(error);
        Assert.Equal(issuer, Jwt.Claim(payload!.Value, "iss"));
    }

    [Fact]
    public async Task TheTokensHaveProductionsClaimTypes()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();

        var sophie = await client.GetStringAsync("/Home/GetTestUserToken?userId=1337&authenticationLevel=3", Ct);
        Assert.Equal(UserClaimTypes.Order(StringComparer.Ordinal), TypesOf(sophie));
        var claims = MinimalFrontDoor.Claims(sophie);
        Assert.Equal("1337", claims.GetProperty("urn:altinn:userid").GetString());
        Assert.Equal(501337, claims.GetProperty("urn:altinn:partyid").GetInt32());
        Assert.Equal(3, claims.GetProperty("urn:altinn:authlevel").GetInt32());
        Assert.Equal("IdportenTestId", claims.GetProperty("urn:altinn:authenticatemethod").GetString());
        Assert.Contains("altinn:portal/enduser", claims.GetProperty("scope").GetString(), StringComparison.Ordinal);

        var selfIdentified = await client.GetStringAsync("/Home/GetTestUserToken?userId=1004&authenticationLevel=0", Ct);
        Assert.Equal(
            UserClaimTypes.Where(t => t != "urn:altinn:username").Concat(["email", "urn:altinn:party:external-identifier"]).Order(StringComparer.Ordinal),
            TypesOf(selfIdentified));

        var org = await client.GetStringAsync("/Home/GetTestOrgToken?org=ttd", Ct);
        Assert.Equal(OrgClaimTypes.Order(StringComparer.Ordinal), TypesOf(org));
        Assert.Equal("ttd", MinimalFrontDoor.Claims(org).GetProperty("urn:altinn:org").GetString());
        Assert.Equal("991825827", MinimalFrontDoor.Claims(org).GetProperty("urn:altinn:orgNumber").GetString());

        var studio = await client.GetStringAsync("/Home/GetTestStudioToken", Ct);
        Assert.Equal(StudioClaimTypes.Order(StringComparer.Ordinal), TypesOf(studio));
        Assert.Equal("studio.designer", MinimalFrontDoor.Claims(studio).GetProperty("urn:altinn:app").GetString());
    }

    [Fact]
    public async Task TheBrowserSignsInThroughThePickerWithPkceAndGetsProductionsCookies()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();
        var goTo = MinimalFrontDoor.Origin + "/ttd/testapp/";

        using var start = await client.GetAsync("/authentication/api/v1/authentication?goTo=" + Uri.EscapeDataString(goTo), Ct);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        var picker = start.Headers.Location!;
        Assert.Equal("/localtest/api/idporten/authorize", picker.AbsolutePath);
        var query = QueryHelpers.ParseQuery(picker.Query);
        Assert.Equal("S256", query["code_challenge_method"].ToString());

        using var pick = await client.PostAsync("/localtest/api/idporten/authorize", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = query["client_id"].ToString(),
            ["state"] = query["state"].ToString(),
            ["nonce"] = query["nonce"].ToString(),
            ["redirect_uri"] = query["redirect_uri"].ToString(),
            ["code_challenge"] = query["code_challenge"].ToString(),
            ["code_challenge_method"] = "S256",
            ["UserSelect"] = "1337.501337",
            ["AuthenticationLevel"] = "3",
        }), Ct);
        Assert.Equal(HttpStatusCode.Redirect, pick.StatusCode);
        var callback = pick.Headers.Location!;
        Assert.Equal("/authentication/api/v1/upstream/callback", callback.AbsolutePath);

        using var signedIn = await client.GetAsync(callback.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
        Assert.Equal(goTo, signedIn.Headers.Location!.ToString());
        var cookies = signedIn.Headers.GetValues("Set-Cookie").ToList();
        Assert.Equal(["AltinnPartyId", "AltinnPartyUuid", "AltinnStudioRuntime", "altinnsession"], cookies.Select(c => c.Split('=')[0]).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(cookies, c => c.Contains("secure", StringComparison.OrdinalIgnoreCase));
        var jwt = cookies.Single(c => c.StartsWith("AltinnStudioRuntime=", StringComparison.Ordinal)).Split(';')[0]["AltinnStudioRuntime=".Length..];
        Assert.Equal(3, MinimalFrontDoor.Claims(jwt).GetProperty("urn:altinn:authlevel").GetInt32());
        Assert.Equal("authentication", Assert.Single(signedIn.Headers.GetValues(Localtest.Emulation.Header)));

        // The callback's state is spent.
        using var again = await client.GetAsync(callback.PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);

        using var refresh = new HttpRequestMessage(HttpMethod.Get, "/authentication/api/v1/refresh");
        refresh.Headers.Authorization = MinimalFrontDoor.Bearer(jwt);
        using var refreshed = await client.SendAsync(refresh, Ct);
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.Equal(TypesOf(jwt), TypesOf(await refreshed.Content.ReadAsStringAsync(Ct)));
        using var anonymous = await client.GetAsync("/authentication/api/v1/refresh", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Fact]
    public async Task AnOffSiteGoToGoesToTheStartPage()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.GetAsync("/authentication/api/v1/authentication?goTo=https://example.com/", Ct);

        Assert.Equal("http://local.altinn.cloud:8000/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task TheMaskinportenExchangeChecksTheFakesSignature()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();
        var key = frontDoor.Services.GetRequiredKeyedService<SigningKey>(MaskinportenApi.KeyName);
        var settings = frontDoor.Services.GetRequiredService<MaskinportenSettings>();

        var serviceOwner = MaskinportenApi.Mint(key, settings, "991825827", "altinn:serviceowner/instances.read", "localtest-smoke");
        Assert.Equal("ttd", MinimalFrontDoor.Claims(await Exchange(client, serviceOwner, "?test=true")).GetProperty("urn:altinn:org").GetString());
        var plain = MinimalFrontDoor.Claims(await Exchange(client, MaskinportenApi.Mint(key, settings, "974760673", "altinn:instances.read", "x"), string.Empty));
        Assert.False(plain.TryGetProperty("urn:altinn:org", out _));
        Assert.Equal("974760673", plain.GetProperty("urn:altinn:orgNumber").GetString());

        using var other = new SigningKey();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/authentication/api/v1/exchange/maskinporten");
        request.Headers.Authorization = MinimalFrontDoor.Bearer(MaskinportenApi.Mint(other, settings, "991825827", "altinn:serviceowner/instances.read", "x"));
        using var refused = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static async Task<string> Exchange(HttpClient client, string token, string query)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/authentication/api/v1/exchange/maskinporten" + query);
        request.Headers.Authorization = MinimalFrontDoor.Bearer(token);
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("\"", body, StringComparison.Ordinal); // A raw JWT: app-lib's Maskinporten client parses it as is.
        return body;
    }

    [Fact]
    public async Task HomesContractWorksInProcessWithTheBanner()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();

        var start = await client.GetStringAsync("/", Ct);
        Assert.Contains("Minimal mode: Register, Profile, Authentication, Authorization (permits everything), Access Management, Events, Notifications and PDF are emulated. Do not report platform bugs from this mode", start, StringComparison.Ordinal);
        Assert.Contains("<option value=\"1337.500000\">Sophie Salt for DDG Fitness</option>", start, StringComparison.Ordinal);

        using var login = await client.PostAsync("/Home/LogInTestUser", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["action"] = "reauthenticate",
            ["UserSelect"] = "1337.500000",
            ["AuthenticationLevel"] = "2",
        }), Ct);
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        var cookies = login.Headers.GetValues("Set-Cookie").ToList();
        Assert.Equal(4, cookies.Count);
        Assert.Contains(cookies, c => c.StartsWith("AltinnPartyId=500000;", StringComparison.Ordinal));
        Assert.Equal("8", await client.GetStringAsync("/Home/Localtest/Version", Ct));
    }
}
