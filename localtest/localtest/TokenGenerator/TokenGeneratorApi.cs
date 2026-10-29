using System.Buffers.Text;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Localtest.Tokens;

namespace Localtest.TokenGenerator;

/// <summary>
/// A mock of the online test tool's platform access token generator (<c>altinn-testtools-token-generator</c>, source
/// <c>Altinn/AltinnTestTools</c>, <c>TokenGenerator/GetPlatformAccessToken.cs</c>), under the fixed prefix
/// <see cref="Prefix"/>: <c>GET /mockporten/api/GetPlatformAccessToken?env&amp;app&amp;ttl&amp;org</c> with basic auth.
/// The path below the prefix and the interface are the real tool's, which altinn-auth#4052 plans for Mockporten, so a
/// client can later be pointed there instead; later test-tool-compatible endpoints go under the same prefix. Basic auth must be present,
/// as at the real tool, and any user name and password are accepted. The token is signed with the shared test
/// certificate (<see cref="LocaltestSettings.AccessTokenCertificatePath"/>) the way <c>Altinn.Common.AccessTokenClient</c>'s
/// <c>AccessTokenGenerator</c> signs one in production, so every component's <c>AccessTokenHandler</c> accepts it.
/// </summary>
public static partial class TokenGeneratorApi
{
    /// <summary>The issuer the real tool uses when <c>org</c> is absent (its <c>PlatformAccessTokenIssuerName</c>).</summary>
    public const string PlatformIssuer = "platform";

    /// <summary>The other issuer the real tool signs for (<c>org=ttd</c>, its <c>TtdAccessTokenIssuerName</c>).</summary>
    public const string TtdIssuer = "ttd";

    /// <summary>The real tool's default lifetime in seconds; it has no maximum (any unsigned 32-bit value).</summary>
    public const uint DefaultTtlSeconds = 1800;

    /// <summary><c>AccessTokenSettings.ValidFromAdjustmentSeconds</c>' default: <c>nbf</c> is five seconds before now.</summary>
    private const int ValidFromAdjustmentSeconds = -5;

    /// <summary>The audience <c>AccessTokenGenerator</c> writes (the validators do not check it).</summary>
    private const string Audience = "platform.altinn.no";

    private const string BasicChallenge = "Basic realm=\"localtest - TokenGenerator\"";

    /// <summary>The front door's prefix for the test tool's endpoints (fixed; an app may not register the org <c>mockporten</c>).</summary>
    public const string Prefix = "/mockporten";

    public static void Map(WebApplication app)
        => app.MapGet(Prefix + "/api/GetPlatformAccessToken", (HttpRequest request, LocaltestSettings settings, ILoggerFactory loggers) =>
        {
            if (BasicUser(request) is not { } user)
            {
                // The real tool: 401 with a Basic challenge when the header is missing or not Basic; 400 when the
                // credentials do not decode.
                if (request.Headers.Authorization.ToString().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
                {
                    return Results.Text("The basic credentials do not decode as user:password.\n", statusCode: StatusCodes.Status400BadRequest);
                }

                request.HttpContext.Response.Headers.WWWAuthenticate = BasicChallenge;
                return Results.Text("Basic authentication is required (any user name and password are accepted locally).\n", statusCode: StatusCodes.Status401Unauthorized);
            }

            var errors = new List<string>();
            var app = request.Query["app"].ToString();
            if (!IsDottedIdentifier(app))
            {
                errors.Add("app is required: letters, digits, '_' and '-' in dot-separated parts, at most 50 characters.");
            }

            var issuer = request.Query["org"].ToString() is { Length: > 0 } org ? org : PlatformIssuer;
            if (issuer is not (PlatformIssuer or TtdIssuer))
            {
                errors.Add($"org is the issuer: {PlatformIssuer} (the default) or {TtdIssuer}.");
            }

            var ttl = DefaultTtlSeconds;
            if (request.Query["ttl"].ToString() is { Length: > 0 } ttlText && !uint.TryParse(ttlText, out ttl))
            {
                errors.Add("ttl is the lifetime in seconds, a non-negative integer (default 1800).");
            }

            if (errors.Count > 0)
            {
                return Results.Text(string.Join('\n', errors) + "\n", statusCode: StatusCodes.Status400BadRequest);
            }

            if (!File.Exists(settings.AccessTokenCertificatePath))
            {
                return Results.Text(
                    $"The shared test certificate is not at {settings.AccessTokenCertificatePath} (Localtest:AccessTokenCertificatePath).\n",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var env = request.Query["env"].ToString();
            loggers.CreateLogger("Localtest.TokenGenerator").LogInformation("Platform access token for app {App}, issuer {Issuer}, ttl {Ttl} s (env '{Env}', user '{User}')", app, issuer, ttl, env, user);
            return Results.Text(Create(settings.AccessTokenCertificatePath, issuer, app, ttl, DateTimeOffset.UtcNow), "text/plain", Encoding.UTF8);
        });

    /// <summary>
    /// A platform access token as <c>AccessTokenGenerator.GenerateAccessToken(issuer, app)</c> writes it: header
    /// <c>alg</c> RS256, <c>kid</c> the certificate's thumbprint and <c>x5t</c> (<see cref="SigningKey.FromCertificate"/>);
    /// claims <c>urn:altinn:app</c>, <c>nbf</c> (five seconds before now), <c>exp</c> (now plus the lifetime),
    /// <c>iat</c>, <c>iss</c> and <c>aud</c> <c>platform.altinn.no</c>.
    /// </summary>
    public static string Create(string certificatePath, string issuer, string app, uint ttlSeconds, DateTimeOffset now)
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password: null);
        using var key = SigningKey.FromCertificate(certificate);
        return key.CreateJwt(writer =>
        {
            writer.WriteString("urn:altinn:app", app);
            writer.WriteNumber("nbf", now.AddSeconds(ValidFromAdjustmentSeconds).ToUnixTimeSeconds());
            writer.WriteNumber("exp", now.ToUnixTimeSeconds() + ttlSeconds);
            writer.WriteNumber("iat", now.ToUnixTimeSeconds());
            writer.WriteString("iss", issuer);
            writer.WriteString("aud", Audience);
        });
    }

    /// <summary>The user name of a decodable <c>Basic</c> header, or null.</summary>
    private static string? BasicUser(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        if (!header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var encoded = header["Basic ".Length..].Trim();
        if (encoded.Length == 0 || !Base64.IsValid(encoded))
        {
            return null;
        }

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
        var colon = decoded.IndexOf(':');
        return colon < 0 ? null : decoded[..colon];
    }

    /// <summary>The real tool's <c>IsValidDottedIdentifier</c>.</summary>
    private static bool IsDottedIdentifier(string value) => value.Length is > 0 and <= 50 && DottedIdentifier().IsMatch(value);

    [GeneratedRegex(@"^(?:[a-zA-Z0-9_-]\.?)+[a-zA-Z0-9_-]$")]
    private static partial Regex DottedIdentifier();
}
