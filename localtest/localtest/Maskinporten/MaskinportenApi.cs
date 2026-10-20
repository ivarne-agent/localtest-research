using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Localtest.Tokens;

namespace Localtest.Maskinporten;

/// <summary>
/// A fake Maskinporten under <c>localtest/api/maskinporten/</c>: the discovery metadata and key set production
/// Authentication's <c>exchange/maskinporten</c> validates tokens against, and <see cref="Mint"/>, which signs the
/// Maskinporten-shaped tokens <c>/Home/GetTestOrgToken</c> exchanges, and the <c>token</c> endpoint a Maskinporten client
/// (the <c>IMaskinportenService</c> fake in <c>components/shared</c>, the Arbeidsflate BFF) posts a JWT grant to.
/// Anonymous, local only.
/// </summary>
public static class MaskinportenApi
{
    /// <summary>The DI key of the fake Maskinporten's <see cref="SigningKey"/>.</summary>
    public const string KeyName = "maskinporten";

    /// <summary>The <c>client_id</c> of tokens minted for <c>/Home/GetTestOrgToken</c>.</summary>
    public const string LocaltestClientId = "localtest";

    private const string JwtBearerGrant = "urn:ietf:params:oauth:grant-type:jwt-bearer";
    private const string DefaultOrgNumber = "991825827";

    /// <summary>The lifetime <see cref="Mint"/> gives a token, which the token response reports as <c>expires_in</c>.</summary>
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/localtest/api/maskinporten");

        api.MapGet("/.well-known/oauth-authorization-server", (MaskinportenSettings settings) => new AuthorizationServerMetadata(
            settings.Issuer,
            settings.Issuer + "jwks",
            [JwtBearerGrant],
            ["RS256"],
            settings.Issuer + "token",
            ["private_key_jwt"]));

        api.MapGet("/jwks", ([FromKeyedServices(KeyName)] SigningKey key) => key.Jwks());

        api.MapPost("/token", async (
            HttpRequest request,
            [FromKeyedServices(KeyName)] SigningKey key,
            MaskinportenSettings settings,
            ILogger<MaskinportenSettings> logger,
            CancellationToken cancellationToken) =>
        {
            if (!request.HasFormContentType)
            {
                return TokenError("invalid_request", "Expected a form post (application/x-www-form-urlencoded).");
            }

            var form = await request.ReadFormAsync(cancellationToken);
            if (form["grant_type"] != JwtBearerGrant)
            {
                return TokenError("unsupported_grant_type", $"Only {JwtBearerGrant} is supported.");
            }

            if (!TryReadAssertion(form["assertion"].ToString(), out var assertion))
            {
                return TokenError("invalid_request", "The assertion is missing or is not a JWT.");
            }

            using (assertion)
            {
                var payload = assertion.RootElement;
                if (payload.ValueKind != JsonValueKind.Object || StringClaim(payload, "iss") is not { Length: > 0 } clientId)
                {
                    return TokenError("invalid_request", "The assertion has no iss (the client id).");
                }

                var scope = StringClaim(payload, "scope") ?? string.Empty;
                string orgNumber;
                string rule;
                if (StringClaim(payload, "consumer_org") is { Length: > 0 } consumerOrg)
                {
                    (orgNumber, rule) = (consumerOrg, "consumer_org in the assertion");
                }
                else if (clientId.Length == 9 && clientId.All(char.IsAsciiDigit))
                {
                    (orgNumber, rule) = (clientId, "a nine-digit client id is the organisation number");
                }
                else
                {
                    (orgNumber, rule) = (DefaultOrgNumber, "default organisation number");
                }

                logger.LogInformation("Maskinporten token for client {ClientId}, organisation {OrgNumber} ({Rule}), scope '{Scope}'", clientId, orgNumber, rule, scope);

                return Results.Json(
                    new MaskinportenTokenResponse(Mint(key, settings, orgNumber, scope, clientId), "Bearer", (int)TokenLifetime.TotalSeconds, scope),
                    LocaltestJsonContext.Default.MaskinportenTokenResponse);
            }
        });
    }

    private static IResult TokenError(string error, string description)
        => Results.Json(new TokenErrorResponse(error, description), LocaltestJsonContext.Default.TokenErrorResponse, statusCode: 400);

    /// <summary>The payload of a compact JWT; the header and signature are not read, so an unsigned assertion passes.</summary>
    private static bool TryReadAssertion(string jwt, out JsonDocument payload)
    {
        payload = null!;
        var parts = jwt.Split('.');
        if (parts.Length < 2 || !Base64Url.IsValid(parts[1]))
        {
            return false;
        }

        try
        {
            payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? StringClaim(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// A token as Maskinporten issues it to a client of <paramref name="orgNumber"/> with <paramref name="scopes"/>
    /// (space-separated): <c>consumer</c> is the organisation number in ISO 6523 form, which the exchange turns into
    /// <c>urn:altinn:orgNumber</c> (and, for a service-owner scope, <c>urn:altinn:org</c>).
    /// <paramref name="clientId"/> is the token's <c>client_id</c>.
    /// </summary>
    public static string Mint(SigningKey key, MaskinportenSettings settings, string orgNumber, string scopes, string clientId)
    {
        var now = DateTimeOffset.UtcNow;
        return key.CreateJwt(writer =>
        {
            writer.WriteString("iss", settings.Issuer);
            writer.WriteString("scope", scopes);
            writer.WriteString("client_id", clientId);
            writer.WriteString("token_type", "Bearer");
            writer.WriteString("jti", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
            writer.WriteStartObject("consumer");
            writer.WriteString("authority", "iso6523-actorid-upis");
            writer.WriteString("ID", "0192:" + orgNumber);
            writer.WriteEndObject();
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", now.Add(TokenLifetime).ToUnixTimeSeconds());
        });
    }
}

/// <summary>
/// The fake Maskinporten's issuer, with a trailing slash like Maskinporten's. Production's exchange accepts a token when
/// the configured well-known URL contains its <c>iss</c>, so Authentication's <c>MaskinportenWellKnownConfigEndpoint</c>
/// is <c>{Issuer}.well-known/oauth-authorization-server</c>.
/// </summary>
public sealed record MaskinportenSettings(string Issuer)
{
    public static MaskinportenSettings FromConfiguration(IConfiguration configuration, LocaltestSettings localtest)
        => new(configuration["Maskinporten:Issuer"] is { Length: > 0 } issuer
            ? issuer.TrimEnd('/') + "/"
            : localtest.PlatformEndpoint + "localtest/api/maskinporten/");
}

public sealed record AuthorizationServerMetadata(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("grant_types_supported")] string[] GrantTypesSupported,
    [property: JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")] string[] TokenEndpointAuthSigningAlgValuesSupported,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("token_endpoint_auth_methods_supported")] string[] TokenEndpointAuthMethodsSupported);

public sealed record MaskinportenTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);
