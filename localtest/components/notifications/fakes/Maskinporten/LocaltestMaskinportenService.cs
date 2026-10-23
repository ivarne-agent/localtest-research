using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Altinn.ApiClients.Maskinporten.Interfaces;
using Altinn.ApiClients.Maskinporten.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Localtest.Shared.Maskinporten;

/// <summary>
/// <see cref="IMaskinportenService"/> for the localtest stack. The package hard-codes <c>https://*.maskinporten.no</c> and the
/// per-environment exchange hosts, so configuration cannot redirect it. This posts an unsigned JWT assertion to the localtest
/// app's fake Maskinporten token endpoint and, when the client definition asks for it, exchanges the result through
/// production Authentication's <c>exchange/maskinporten</c>, as the package does. Only the two
/// <see cref="IClientDefinition"/> overloads and <see cref="ExchangeToAltinnToken"/> are implemented: they are what the
/// package's <c>MaskinportenTokenHandler</c> calls.
/// </summary>
public sealed class LocaltestMaskinportenService(
    IHttpClientFactory httpClientFactory,
    IOptions<LocaltestMaskinportenOptions> options,
    ITokenCacheProvider cache) : IMaskinportenService
{
    private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";
    private static readonly TimeSpan CacheMargin = TimeSpan.FromSeconds(5);

    public Task<TokenResponse> GetToken(IClientDefinition clientDefinition, bool disableCaching = false)
        => GetToken(clientDefinition, requestContext: null!, disableCaching);

    public async Task<TokenResponse> GetToken(IClientDefinition clientDefinition, MaskinportenRequestContext requestContext, bool disableCaching = false)
    {
        var settings = clientDefinition.ClientSettings;
        var tokenResponse = await GetMaskinportenToken(settings, disableCaching);
        if (settings.ExhangeToAltinnToken != true)
        {
            return tokenResponse;
        }

        // As in production: the test organisation (Digdir becomes ttd) only when the consumer asks for it.
        return await ExchangeToAltinnToken(
            tokenResponse,
            settings.TokenExchangeEnvironment ?? settings.Environment,
            disableCaching: disableCaching,
            isTestOrg: settings.UseAltinnTestOrg == true);
    }

    public async Task<TokenResponse> ExchangeToAltinnToken(TokenResponse tokenResponse, string environment, string userName = null!, string password = null!, bool disableCaching = false, bool isTestOrg = false)
    {
        var cacheKey = "exchange:" + Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(tokenResponse.AccessToken + isTestOrg)));
        if (!disableCaching && await cache.TryGetToken(cacheKey) is (true, var cached))
        {
            return cached;
        }

        var url = options.Value.ExchangeEndpoint + (isTestOrg ? "?test=true" : "");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenResponse.AccessToken);
        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await ReadSuccessBody(response, "exchange/maskinporten");

        // Authentication answers the token as plain text.
        var exchanged = new TokenResponse
        {
            AccessToken = body.Trim(),
            ExpiresIn = tokenResponse.ExpiresIn,
            Scope = tokenResponse.Scope,
            TokenType = "altinn",
        };
        await cache.Set(cacheKey, exchanged, TimeToLive(exchanged));
        return exchanged;
    }

    public Task<TokenResponse> GetToken(JsonWebKey jwk, string environment, string clientId, string scope, string resource, string consumerOrgNo = null!, bool disableCaching = false)
        => throw Unsupported();

    public Task<TokenResponse> GetToken(X509Certificate2 cert, string environment, string clientId, string scope, string resource, string consumerOrgNo = null!, bool disableCaching = false)
        => throw Unsupported();

    public Task<TokenResponse> GetToken(string base64EncodedJWK, string environment, string clientId, string scope, string resource, string consumerOrgNo = null!, bool disableCaching = false)
        => throw Unsupported();

    private async Task<TokenResponse> GetMaskinportenToken(IMaskinportenSettings settings, bool disableCaching)
    {
        var cacheKey = JsonSerializer.Serialize(new { settings.ClientId, settings.Scope, settings.Resource, settings.ConsumerOrgNo });
        if (!disableCaching && await cache.TryGetToken(cacheKey) is (true, var cached))
        {
            return cached;
        }

        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = settings.ClientId,
            ["scope"] = settings.Scope ?? "",
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddSeconds(10).ToUnixTimeSeconds(),
        };
        if (!string.IsNullOrEmpty(settings.ConsumerOrgNo))
        {
            claims["consumer_org"] = settings.ConsumerOrgNo;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = JwtBearerGrant,
                ["assertion"] = UnsignedJwt(claims),
            }),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var response = await httpClientFactory.CreateClient().SendAsync(request);
        var body = await ReadSuccessBody(response, "the Maskinporten token endpoint");

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body)!;
        await cache.Set(cacheKey, tokenResponse, TimeToLive(tokenResponse));
        return tokenResponse;
    }

    /// <summary>An <c>alg: none</c> JWT: the localtest token endpoint reads the payload only.</summary>
    private static string UnsignedJwt(Dictionary<string, object> claims)
        => $"{Base64Url("""{"alg":"none","typ":"JWT"}"""u8.ToArray())}.{Base64Url(JsonSerializer.SerializeToUtf8Bytes(claims))}.";

    private static string Base64Url(byte[] bytes) => Base64UrlEncoder.Encode(bytes);

    private static TimeSpan TimeToLive(TokenResponse token)
        => TimeSpan.FromSeconds(token.ExpiresIn) is var lifetime && lifetime > CacheMargin ? lifetime - CacheMargin : TimeSpan.Zero;

    private static async Task<string> ReadSuccessBody(HttpResponseMessage response, string what)
    {
        var body = await response.Content.ReadAsStringAsync();
        return response.IsSuccessStatusCode
            ? body
            : throw new HttpRequestException($"Localtest Maskinporten fake: {what} answered {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }

    private static NotSupportedException Unsupported()
        => new("Localtest Maskinporten fake: use a client definition (GetToken(IClientDefinition, ...)).");
}
