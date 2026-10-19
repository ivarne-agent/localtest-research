using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Localtest.Tokens;

namespace Localtest.Maskinporten;

/// <summary>
/// A fake Maskinporten under <c>localtest/api/maskinporten/</c>: the discovery metadata and key set production
/// Authentication's <c>exchange/maskinporten</c> validates tokens against, and <see cref="Mint"/>, which signs the
/// Maskinporten-shaped tokens <c>/Home/GetTestOrgToken</c> exchanges. There is no token endpoint: nothing local runs a
/// Maskinporten client. Anonymous, local only.
/// </summary>
public static class MaskinportenApi
{
    /// <summary>The DI key of the fake Maskinporten's <see cref="SigningKey"/>.</summary>
    public const string KeyName = "maskinporten";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/localtest/api/maskinporten");

        api.MapGet("/.well-known/oauth-authorization-server", (MaskinportenSettings settings) => new AuthorizationServerMetadata(
            settings.Issuer,
            settings.Issuer + "token",
            settings.Issuer + "jwks",
            ["urn:ietf:params:oauth:grant-type:jwt-bearer"],
            ["RS256"]));

        api.MapGet("/jwks", ([FromKeyedServices(KeyName)] SigningKey key) => key.Jwks());
    }

    /// <summary>
    /// A token as Maskinporten issues it to a client of <paramref name="orgNumber"/> with <paramref name="scopes"/>
    /// (space-separated): <c>consumer</c> is the organisation number in ISO 6523 form, which the exchange turns into
    /// <c>urn:altinn:orgNumber</c> (and, for a service-owner scope, <c>urn:altinn:org</c>).
    /// </summary>
    public static string Mint(SigningKey key, MaskinportenSettings settings, string orgNumber, string scopes)
    {
        var now = DateTimeOffset.UtcNow;
        return key.CreateJwt(writer =>
        {
            writer.WriteString("iss", settings.Issuer);
            writer.WriteString("scope", scopes);
            writer.WriteString("client_id", "localtest");
            writer.WriteString("token_type", "Bearer");
            writer.WriteString("jti", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
            writer.WriteStartObject("consumer");
            writer.WriteString("authority", "iso6523-actorid-upis");
            writer.WriteString("ID", "0192:" + orgNumber);
            writer.WriteEndObject();
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
            writer.WriteNumber("exp", now.AddMinutes(30).ToUnixTimeSeconds());
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
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("grant_types_supported")] string[] GrantTypesSupported,
    [property: JsonPropertyName("token_endpoint_auth_signing_alg_values_supported")] string[] TokenEndpointAuthSigningAlgValuesSupported);
