using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;

namespace Localtest.IdPorten;

/// <summary>
/// A fake ID-porten under <c>localtest/api/idporten/</c>: an OpenID Connect authorization-code provider whose login page
/// is the test-user picker. Production Authentication talks to it exactly as to ID-porten: it redirects the browser to
/// <c>authorize</c> with state, nonce and an S256 PKCE challenge, redeems the code at <c>token</c> with the verifier, and
/// validates both returned tokens against <c>jwks</c> (signature, issuer, lifetime, nonce). Anonymous, local only.
/// </summary>
public static class IdPortenApi
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/localtest/api/idporten");

        api.MapGet("/.well-known/openid-configuration", (IdPortenSettings settings) => new OpenIdConfiguration(
            settings.Issuer,
            settings.Issuer + "/authorize",
            settings.Issuer + "/token",
            settings.Issuer + "/jwks",
            ["code"],
            ["pairwise"],
            ["RS256"],
            ["S256"],
            ["openid", "profile"],
            [.. AuthLevels.All.Select(l => l.Acr)]));

        api.MapGet("/jwks", (SigningKey key) => key.Jwks());

        api.MapGet("/authorize", async (HttpRequest request, TestUsers testUsers, CancellationToken cancellationToken) =>
        {
            var query = request.Query;
            if (string.IsNullOrEmpty(query["state"]) || string.IsNullOrEmpty(query["redirect_uri"]))
            {
                return Results.BadRequest("Missing state or redirect_uri. Start the login from an app, or from authentication/api/v1/authentication?goTo=...");
            }

            var (users, notice) = await Load(testUsers, cancellationToken);
            var level = AuthLevels.Highest(query["acr_values"]) ?? AuthLevels.ForNumber(AuthLevels.Default)!;
            var hidden = new Dictionary<string, string?>
            {
                ["client_id"] = query["client_id"],
                ["redirect_uri"] = query["redirect_uri"],
                ["state"] = query["state"],
                ["nonce"] = query["nonce"],
                ["code_challenge"] = query["code_challenge"],
                ["code_challenge_method"] = query["code_challenge_method"],
            };
            return Results.Content(PickerPage(users, hidden, level.Number, notice), "text/html; charset=utf-8");
        });

        api.MapPost("/authorize", async (HttpRequest request, TestUsers testUsers, AuthorizationCodes codes, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            var state = form["state"].ToString();
            var redirectUri = form["redirect_uri"].ToString();
            if (string.IsNullOrEmpty(state) || !Uri.TryCreate(redirectUri, UriKind.Absolute, out _))
            {
                return Results.BadRequest("Missing state or redirect_uri.");
            }

            if (!string.IsNullOrEmpty(form["code_challenge_method"]) && form["code_challenge_method"] != "S256")
            {
                return Results.BadRequest("Only the S256 code challenge method is supported.");
            }

            if (!TryParseUserSelect(form["UserSelect"], out var userId))
            {
                return Results.BadRequest("UserSelect must be userId or userId.partyId.");
            }

            var user = (await testUsers.GetAll(cancellationToken)).FirstOrDefault(u => u.UserId == userId);
            if (user is null)
            {
                return Results.NotFound($"No test user with userId {userId}.");
            }

            var level = int.TryParse(form["AuthenticationLevel"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? AuthLevels.ForNumber(number)
                : null;
            var code = codes.Issue(new AuthorizationGrant(
                string.IsNullOrEmpty(user.Ssn) ? null : user.Ssn,
                user.UserId,
                (level ?? AuthLevels.ForNumber(AuthLevels.Default)!).Acr,
                form["client_id"].ToString(),
                redirectUri,
                NullIfEmpty(form["nonce"]),
                NullIfEmpty(form["code_challenge"]),
                default));
            return Results.Redirect(QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?> { ["code"] = code, ["state"] = state }));
        });

        api.MapPost("/token", async (HttpRequest request, AuthorizationCodes codes, SigningKey key, IdPortenSettings settings, CancellationToken cancellationToken) =>
        {
            var form = await request.ReadFormAsync(cancellationToken);
            if (form["grant_type"] != "authorization_code")
            {
                return TokenError("unsupported_grant_type", "Only authorization_code is supported.");
            }

            if (codes.Redeem(form["code"].ToString()) is not { } grant)
            {
                return TokenError("invalid_grant", "Unknown, used or expired code.");
            }

            if (form["redirect_uri"] != grant.RedirectUri)
            {
                return TokenError("invalid_grant", "redirect_uri differs from the authorization request.");
            }

            if (!string.IsNullOrEmpty(form["client_id"]) && form["client_id"] != grant.ClientId)
            {
                return TokenError("invalid_client", "client_id differs from the authorization request.");
            }

            if (grant.CodeChallenge is { } challenge)
            {
                var verifier = form["code_verifier"].ToString();
                if (string.IsNullOrEmpty(verifier) || Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))) != challenge)
                {
                    return TokenError("invalid_grant", "code_verifier does not match the code challenge.");
                }
            }

            var now = DateTimeOffset.UtcNow;
            var sessionId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
            var idToken = key.CreateJwt(writer =>
            {
                WriteCommon(writer, settings, grant, now);
                writer.WriteString("aud", grant.ClientId);
                writer.WriteNumber("auth_time", now.ToUnixTimeSeconds());
                writer.WriteString("sid", sessionId);
                if (grant.Nonce is { } nonce)
                {
                    writer.WriteString("nonce", nonce);
                }
            });
            var accessToken = key.CreateJwt(writer =>
            {
                WriteCommon(writer, settings, grant, now);
                writer.WriteString("client_id", grant.ClientId);
                writer.WriteString("scope", "openid");
                writer.WriteString("token_type", "Bearer");
            });
            return Results.Json(
                new TokenResponse(idToken, accessToken, "Bearer", (int)TokenLifetime.TotalSeconds, "openid"),
                LocaltestJsonContext.Default.TokenResponse);
        });
    }

    /// <summary>
    /// Claims both tokens carry. <c>sub</c> is pairwise and opaque, as ID-porten's; the identity is <c>pid</c>, or, for a
    /// self-identified test user without one, <c>urn:altinn:userid</c>, which takes production's Profile-by-userId branch.
    /// </summary>
    private static void WriteCommon(Utf8JsonWriter writer, IdPortenSettings settings, AuthorizationGrant grant, DateTimeOffset now)
    {
        var identity = grant.Ssn ?? "userid:" + grant.UserId.ToString(CultureInfo.InvariantCulture);
        writer.WriteString("iss", settings.Issuer);
        writer.WriteString("sub", Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(grant.ClientId + "|" + identity))));
        if (grant.Ssn is { } ssn)
        {
            writer.WriteString("pid", ssn);
        }
        else
        {
            writer.WriteString("urn:altinn:userid", grant.UserId.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteString("acr", grant.Acr);
        writer.WriteStartArray("amr");
        writer.WriteStringValue("TestID");
        writer.WriteEndArray();
        writer.WriteString("actual_iss", "localtest");
        writer.WriteString("jti", Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        writer.WriteNumber("iat", now.ToUnixTimeSeconds());
        writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
        writer.WriteNumber("exp", now.Add(TokenLifetime).ToUnixTimeSeconds());
    }

    private static IResult TokenError(string error, string description)
        => Results.Json(new TokenErrorResponse(error, description), LocaltestJsonContext.Default.TokenErrorResponse, statusCode: 400);

    private static async Task<(TestUser[] Users, string? Notice)> Load(TestUsers testUsers, CancellationToken cancellationToken)
    {
        try
        {
            var users = await testUsers.GetAll(cancellationToken);
            return (users, users.Length == 0 ? $"No test users at {testUsers.Endpoint}." : null);
        }
        catch (HttpRequestException ex)
        {
            return ([], $"Could not read the test users from {testUsers.Endpoint}: {ex.Message}. Is the register component running?");
        }
    }

    /// <summary>The old localtest's <c>UserSelect</c>: <c>userId</c> or <c>userId.partyId</c> (the party is ignored here).</summary>
    private static bool TryParseUserSelect(string? value, out int userId)
    {
        userId = 0;
        var first = (value ?? string.Empty).Trim().Split('.')[0];
        return int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId);
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// The picker. Field names follow the old localtest's start page (<c>&lt;select id="UserSelect"&gt;</c> with
    /// <c>userId.partyId</c> values, <c>AuthenticationLevel</c>), which tests and scripts post. Each user's own party only:
    /// Authentication sets the party cookies, and the app's party selection handles acting for others.
    /// </summary>
    private static string PickerPage(TestUser[] users, Dictionary<string, string?> hidden, int selectedLevel, string? notice)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Pick a test user</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px}")
            .Append("label{display:block;margin:14px 0 4px;font-weight:600}select{width:100%;padding:6px;box-sizing:border-box}")
            .Append("button{margin-top:18px;padding:8px 18px}.notice{color:#a00}</style></head><body>")
            .Append("<h1>Pick a test user</h1><p>Localtest's ID-porten. Production Altinn Authentication validates the sign-in as it would ID-porten's.</p>");
        if (notice is { Length: > 0 })
        {
            html.Append("<p class=\"notice\">").Append(Encode(notice)).Append("</p>");
        }

        html.Append("<form method=\"post\" action=\"authorize\">");
        foreach (var (name, value) in hidden)
        {
            html.Append("<input type=\"hidden\" name=\"").Append(Encode(name)).Append("\" value=\"").Append(Encode(value ?? string.Empty)).Append("\">");
        }

        html.Append("<label for=\"UserSelect\">Test user</label><select id=\"UserSelect\" name=\"UserSelect\">");
        foreach (var user in users)
        {
            html.Append("<option value=\"").Append(user.UserId).Append('.').Append(user.PartyId).Append("\">").Append(Encode(user.DisplayName)).Append("</option>");
        }

        html.Append("</select><label for=\"AuthenticationLevel\">Authentication level</label><select id=\"AuthenticationLevel\" name=\"AuthenticationLevel\">");
        foreach (var level in AuthLevels.All)
        {
            html.Append("<option value=\"").Append(level.Number).Append('"').Append(level.Number == selectedLevel ? " selected" : string.Empty)
                .Append('>').Append(Encode(level.Label)).Append("</option>");
        }

        html.Append("</select><button type=\"submit\">Log in</button></form></body></html>");
        return html.ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}

/// <summary>Settings of the fake ID-porten. The issuer is public: the browser and Authentication both use it.</summary>
public sealed record IdPortenSettings(string Issuer)
{
    public static IdPortenSettings FromConfiguration(IConfiguration configuration)
    {
        var platform = (configuration["Localtest:PlatformEndpoint"] is { Length: > 0 } configured ? configured : "http://local.altinn.cloud:8000/").TrimEnd('/');
        return new IdPortenSettings(configuration["IdPorten:Issuer"] is { Length: > 0 } issuer ? issuer.TrimEnd('/') : platform + "/localtest/api/idporten");
    }
}

public sealed record OpenIdConfiguration(
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("authorization_endpoint")] string AuthorizationEndpoint,
    [property: JsonPropertyName("token_endpoint")] string TokenEndpoint,
    [property: JsonPropertyName("jwks_uri")] string JwksUri,
    [property: JsonPropertyName("response_types_supported")] string[] ResponseTypesSupported,
    [property: JsonPropertyName("subject_types_supported")] string[] SubjectTypesSupported,
    [property: JsonPropertyName("id_token_signing_alg_values_supported")] string[] IdTokenSigningAlgValuesSupported,
    [property: JsonPropertyName("code_challenge_methods_supported")] string[] CodeChallengeMethodsSupported,
    [property: JsonPropertyName("scopes_supported")] string[] ScopesSupported,
    [property: JsonPropertyName("acr_values_supported")] string[] AcrValuesSupported);

public sealed record TokenResponse(
    [property: JsonPropertyName("id_token")] string IdToken,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("scope")] string Scope);

public sealed record TokenErrorResponse(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("error_description")] string ErrorDescription);
