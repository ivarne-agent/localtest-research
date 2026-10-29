using System.Buffers.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Localtest.Tests.Emulation;
using Microsoft.IdentityModel.Tokens;

namespace Localtest.Tests;

/// <summary>
/// The mock of the online test tool's <c>GET /mockporten/api/GetPlatformAccessToken</c> (<c>TokenGenerator/</c>): the token is
/// what <c>Altinn.Common.AccessTokenClient</c>'s <c>AccessTokenGenerator</c> writes, signed with the shared test
/// certificate (the test host's generated one), and it passes <c>Altinn.Common.AccessToken</c>'s validation; basic auth,
/// <c>app</c>, <c>org</c> and <c>ttl</c> as the real tool reads them.
/// </summary>
public sealed class TokenGeneratorTests
{
    private const string Path = "/mockporten/api/GetPlatformAccessToken";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheTokenCarriesTheProductionGeneratorsClaimsAndHeader()
    {
        await using var frontDoor = new FrontDoor();
        using var certificate = Certificate(frontDoor);
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        using var response = await Send(frontDoor, $"{Path}?env=localtest&ttl=3600&app=access-management");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
        var token = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain('"', token);
        var (header, payload) = Decode(token);
        Assert.Equal("RS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        Assert.Equal(certificate.Thumbprint, header.GetProperty("kid").GetString());
        Assert.Equal(Base64Url.EncodeToString(certificate.GetCertHash()), header.GetProperty("x5t").GetString());
        Assert.Equal("platform", payload.GetProperty("iss").GetString());
        Assert.Equal("access-management", payload.GetProperty("urn:altinn:app").GetString());
        Assert.Equal("platform.altinn.no", payload.GetProperty("aud").GetString());
        var iat = payload.GetProperty("iat").GetInt64();
        Assert.InRange(iat, before, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Equal(iat - 5, payload.GetProperty("nbf").GetInt64());
        Assert.Equal(iat + 3600, payload.GetProperty("exp").GetInt64());
        Assert.Equal(["urn:altinn:app", "nbf", "exp", "iat", "iss", "aud"], payload.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TheSignatureVerifiesAgainstTheCertificatesPublicKeyAsTheValidatorsCheckIt()
    {
        await using var frontDoor = new FrontDoor();
        using var withPrivateKey = Certificate(frontDoor);
        using var publicOnly = X509CertificateLoader.LoadCertificate(withPrivateKey.Export(X509ContentType.Cert));
        var token = await (await Send(frontDoor, $"{Path}?env=at22&app=authorization")).Content.ReadAsStringAsync(Ct);

        // The signature over the first two parts, with the public key alone.
        var parts = token.Split('.');
        using (var rsa = publicOnly.GetRSAPublicKey()!)
        {
            Assert.True(rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Base64Url.DecodeFromChars(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }

        // Altinn.Common.AccessToken's AccessTokenValidator: these parameters, the issuer's keys from the certificate.
        var principal = new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(publicOnly), out var validated);
        Assert.Equal("platform", validated.Issuer);
        Assert.Equal("authorization", principal.FindFirst("urn:altinn:app")?.Value);

        // Another certificate's key does not validate it.
        using var other = SelfSigned();
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, ValidationParameters(other), out _));
    }

    [Fact]
    public async Task WithoutBasicAuthTheAnswerIs401WithABasicChallenge()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var anonymous = await client.GetAsync($"{Path}?env=localtest&app=x.y", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal("Basic", anonymous.Headers.WwwAuthenticate.Single().Scheme);

        using var bearer = new HttpRequestMessage(HttpMethod.Get, $"{Path}?env=localtest&app=x.y");
        bearer.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "abc");
        using var bearerResponse = await client.SendAsync(bearer, Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, bearerResponse.StatusCode);

        // A Basic header that does not decode to user:password is a 400, as at the real tool.
        using var malformed = new HttpRequestMessage(HttpMethod.Get, $"{Path}?env=localtest&app=x.y");
        malformed.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes("no-colon")));
        using var malformedResponse = await client.SendAsync(malformed, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
    }

    [Theory]
    [InlineData("anyone", "anything")]
    [InlineData("", "")]
    [InlineData("localtest", "localtest")]
    public async Task AnyUserNameAndPasswordAreAccepted(string user, string password)
    {
        await using var frontDoor = new FrontDoor();
        using var ok = await Send(frontDoor, $"{Path}?app=ttd.app", user, password);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?env=localtest")]
    [InlineData("?env=localtest&app=")]
    [InlineData("?env=localtest&app=x")]
    [InlineData("?env=localtest&app=has%20space")]
    [InlineData("?env=localtest&app=trailing.")]
    [InlineData("?env=localtest&app=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task WithoutAValidAppTheAnswerIs400(string query)
    {
        await using var frontDoor = new FrontDoor();
        using var response = await Send(frontDoor, Path + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("app is required", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(null, 1800L)]
    [InlineData("60", 60L)]
    [InlineData("0", 0L)]
    [InlineData("4294967295", 4294967295L)]
    public async Task TtlIsTheLifetimeInSecondsWithTheRealToolsDefault(string? ttl, long lifetime)
    {
        await using var frontDoor = new FrontDoor();
        using var response = await Send(frontDoor, $"{Path}?app=ttd.app" + (ttl is null ? "" : "&ttl=" + ttl));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, payload) = Decode(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(lifetime, payload.GetProperty("exp").GetInt64() - payload.GetProperty("iat").GetInt64());
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("abc")]
    [InlineData("4294967296")]
    public async Task ATtlThatIsNotAnUnsignedIntegerIs400(string ttl)
    {
        await using var frontDoor = new FrontDoor();
        using var response = await Send(frontDoor, $"{Path}?app=ttd.app&ttl={ttl}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ttl", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task OrgChoosesTheIssuerAsAtTheRealTool()
    {
        await using var frontDoor = new FrontDoor();
        using var ttd = await Send(frontDoor, $"{Path}?app=ttd.app&org=ttd");
        Assert.Equal("ttd", Decode(await ttd.Content.ReadAsStringAsync(Ct)).Payload.GetProperty("iss").GetString());
        using var other = await Send(frontDoor, $"{Path}?app=ttd.app&org=digdir");
        Assert.Equal(HttpStatusCode.BadRequest, other.StatusCode);
    }

    [Fact]
    public async Task OnlyThePrefixedPathIsTheMock()
    {
        // The real tool's root path is not the front door's: it falls through to the app routes' 404.
        await using var frontDoor = new FrontDoor();
        using var root = await Send(frontDoor, "/api/GetPlatformAccessToken?env=localtest&app=ttd.app");
        Assert.Equal(HttpStatusCode.NotFound, root.StatusCode);
        using var post = new HttpRequestMessage(HttpMethod.Post, $"{Path}?app=ttd.app");
        using var client = frontDoor.Client();
        Assert.NotEqual(HttpStatusCode.OK, (await client.SendAsync(post, Ct)).StatusCode);
    }

    [Fact]
    public async Task TheRouteAnswersInMinimalModeToo()
    {
        await using var frontDoor = new MinimalFrontDoor();
        using var client = frontDoor.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Path}?env=localtest&app=ttd.app");
        request.Headers.Authorization = Basic("localtest", "localtest");
        using var response = await client.SendAsync(request, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ttd.app", Decode(await response.Content.ReadAsStringAsync(Ct)).Payload.GetProperty("urn:altinn:app").GetString());
    }

    private static async Task<HttpResponseMessage> Send(FrontDoor frontDoor, string pathAndQuery, string user = "localtest", string password = "localtest")
    {
        using var client = frontDoor.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        request.Headers.Authorization = Basic(user, password);
        return await client.SendAsync(request, Ct);
    }

    private static AuthenticationHeaderValue Basic(string user, string password)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));

    private static X509Certificate2 Certificate(FrontDoor frontDoor)
        => X509CertificateLoader.LoadPkcs12FromFile(System.IO.Path.Combine(frontDoor.DataDirectory, "test.pfx"), password: null);

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=other", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadCertificate(generated.Export(X509ContentType.Cert));
    }

    /// <summary>What <c>AccessTokenValidator.GetTokenValidationParameters</c> builds (Altinn.Common.AccessToken 5.1.x).</summary>
    private static TokenValidationParameters ValidationParameters(X509Certificate2 certificate) => new()
    {
        ValidateIssuerSigningKey = true,
        ValidateIssuer = false,
        ValidateAudience = false,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(10),
        IssuerSigningKeys = [new X509SecurityKey(certificate)],
    };

    private static (JsonElement Header, JsonElement Payload) Decode(string token)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        return (JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0])).RootElement, JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1])).RootElement);
    }
}
