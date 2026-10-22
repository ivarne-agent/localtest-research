using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Localtest.IdPorten;
using Localtest.Maskinporten;
using Localtest.Proxy;
using Localtest.Tokens;

namespace Localtest.Home;

/// <summary>
/// The old localtest's start page and test helpers, which the app-frontend Cypress suite, app-lib's
/// <c>LocaltestValidation</c>, scripts and developers use: <c>/</c>, <c>/Home</c>, <c>/Home/Index</c>,
/// <c>POST /Home/LogInTestUser</c>, <c>/Home/Localtest/Version</c>, <c>/Home/GetTestUserToken</c>,
/// <c>/Home/GetTestOrgToken</c>, <c>/Home/GetTestStudioToken</c>. Nothing is minted as an Altinn token here: user
/// tokens come from production Authentication's sign-in against the fake ID-porten, org and Studio tokens from its
/// production exchanges. Anonymous, local only.
/// </summary>
public static class HomeApi
{
    /// <summary>The localtest API version apps check through <c>LocaltestValidation</c> (they require 6 or later).</summary>
    public const int LocaltestApiVersion = 8;

    private const string DefaultServiceOwnerScopes = "altinn:serviceowner/instances.read altinn:serviceowner/instances.write";

    public static void Map(WebApplication app)
    {
        RequestDelegate startPage = StartPage;
        string[] getOrHead = [HttpMethods.Get, HttpMethods.Head];
        app.MapMethods("/", getOrHead, startPage);
        app.MapMethods("/Home", getOrHead, startPage);
        app.MapMethods("/Home/Index", getOrHead, startPage);

        app.MapGet("/Home/Localtest/Version", () => Results.Text(LocaltestApiVersion.ToString(CultureInfo.InvariantCulture), "application/json"));

        app.MapPost("/Home/LogInTestUser", LogInTestUser);

        app.MapGet("/Home/GetTestUserToken", (HttpContext context, int? userId, int? partyId, int? authenticationLevel, TestUsers testUsers, AuthenticationClient authentication, CancellationToken cancellationToken)
            => GetTestUserToken(context, userId, partyId, authenticationLevel, testUsers, authentication, cancellationToken));
        app.MapGet("/Home/GetTestUserToken/{userId:int}", (HttpContext context, int userId, int? partyId, int? authenticationLevel, TestUsers testUsers, AuthenticationClient authentication, CancellationToken cancellationToken)
            => GetTestUserToken(context, userId, partyId, authenticationLevel, testUsers, authentication, cancellationToken));

        // The org code (query or path) and the level are accepted for compatibility with the old localtest; the exchange
        // derives the org from the organisation number and always answers level 3.
        app.MapGet("/Home/GetTestOrgToken", (string? orgNumber, string? scopes, [FromKeyedServices(MaskinportenApi.KeyName)] SigningKey key, MaskinportenSettings settings, AuthenticationClient authentication, CancellationToken cancellationToken)
            => GetTestOrgToken(orgNumber, scopes, key, settings, authentication, cancellationToken));
        app.MapGet("/Home/GetTestOrgToken/{org}", (string org, string? orgNumber, string? scopes, [FromKeyedServices(MaskinportenApi.KeyName)] SigningKey key, MaskinportenSettings settings, AuthenticationClient authentication, CancellationToken cancellationToken)
            => GetTestOrgToken(orgNumber, scopes, key, settings, authentication, cancellationToken));

        app.MapGet("/Home/GetTestStudioToken", GetTestStudioToken);
    }

    private static async Task StartPage(HttpContext context)
    {
        var testUsers = context.RequestServices.GetRequiredService<TestUsers>();
        TestUser[] users;
        string? notice = null;
        try
        {
            users = await testUsers.GetAll(context.RequestAborted);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            users = [];
            notice = $"Could not read the test users from {testUsers.Endpoint}: {ex.Message}. Is the register component running?";
        }

        var query = context.Request.Query;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<title>Altinn localtest</title>")
            .Append("<style>body{font-family:system-ui,sans-serif;max-width:720px;margin:40px auto;padding:0 16px}")
            .Append("label{display:block;margin:14px 0 4px;font-weight:600}select,input[type=text]{width:100%;padding:6px;box-sizing:border-box}")
            .Append("button{margin-top:18px;padding:8px 18px}.notice{color:#a00}</style></head><body>")
            .Append("<h1>Altinn localtest</h1><p>Log in as a test user. The production Altinn Authentication signs the user in; the selected party is set in the party cookies.</p>");
        if (notice is not null || users.Length == 0)
        {
            html.Append("<p class=\"notice\">").Append(Encode(notice ?? "No test users: is the register component running?")).Append("</p>");
        }

        html.Append("<form method=\"post\" action=\"/Home/LogInTestUser\">")
            .Append("<input type=\"hidden\" name=\"action\" value=\"start\">")
            .Append("<input type=\"hidden\" name=\"RedirectUrl\" value=\"").Append(Encode(query["goto"].ToString())).Append("\">")
            .Append("<label for=\"UserSelect\">Test user</label><select id=\"UserSelect\" name=\"UserSelect\">");
        foreach (var user in users)
        {
            var others = user.OtherParties.ToList();
            if (others.Count > 0)
            {
                html.Append("<optgroup label=\"").Append(Encode(user.DisplayName)).Append("\">");
            }

            Option(html, $"{user.UserId}.{user.PartyId}", user.DisplayName);
            foreach (var party in others)
            {
                Option(html, $"{user.UserId}.{party.PartyId}", $"{user.DisplayName} for {party.DisplayName}");
            }

            if (others.Count > 0)
            {
                html.Append("</optgroup>");
            }
        }

        html.Append("</select><label for=\"AuthenticationLevel\">Authentication level</label><select id=\"AuthenticationLevel\" name=\"AuthenticationLevel\">");
        foreach (var level in AuthLevels.All)
        {
            html.Append("<option value=\"").Append(level.Number).Append('"').Append(level.Number == AuthLevels.Default ? " selected" : string.Empty)
                .Append('>').Append(Encode(level.Label)).Append("</option>");
        }

        html.Append("</select><label for=\"AppPathSelection\">App (org/app, optional)</label>")
            .Append("<input type=\"text\" id=\"AppPathSelection\" name=\"AppPathSelection\" placeholder=\"ttd/my-app\" value=\"").Append(Encode(query["app"].ToString())).Append("\">")
            .Append("<button type=\"submit\">Log in</button></form></body></html>");
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(html.ToString(), context.RequestAborted);
    }

    /// <summary>
    /// Form or JSON fields: <c>UserSelect</c> = <c>userId.partyId</c>, <c>AuthenticationLevel</c> 0-4 (-1 skips the login),
    /// <c>action</c> (<c>reauthenticate</c> answers 204 with the cookies; anything else redirects), <c>RedirectUrl</c>
    /// (same origin only), <c>AppPathSelection</c> (<c>org/app</c>).
    /// </summary>
    private static async Task<IResult> LogInTestUser(HttpContext context, TestUsers testUsers, AuthenticationClient authentication, LocaltestSettings localtest, CancellationToken cancellationToken)
    {
        var fields = await ReadFields(context.Request, cancellationToken);
        string? Field(string name) => fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        if (Field("AuthenticationLevel") != "-1")
        {
            if (!TryParseUserSelect(Field("UserSelect"), out var userId, out var partyId))
            {
                return Results.BadRequest("UserSelect must be userId.partyId.");
            }

            var level = Field("AuthenticationLevel") is { } requested
                ? int.TryParse(requested, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? AuthLevels.ForNumber(number) : null
                : AuthLevels.ForNumber(AuthLevels.Default);
            if (level is null)
            {
                return Results.BadRequest("AuthenticationLevel must be 0, 1, 2, 3 or 4.");
            }

            if (await SignIn(context, userId, partyId, level, testUsers, authentication, cancellationToken) is { Error: { } error })
            {
                return error;
            }
        }

        if (string.Equals(Field("action"), "reauthenticate", StringComparison.OrdinalIgnoreCase))
        {
            return Results.NoContent();
        }

        if (Field("RedirectUrl") is { } redirectUrl && IsOwnOrigin(redirectUrl, localtest))
        {
            return Results.Redirect(redirectUrl);
        }

        return Field("AppPathSelection") is { } app ? Results.Redirect($"/{app.Trim('/')}/") : Results.Redirect("/");
    }

    /// <summary>
    /// A user token without a browser: the production sign-in for the user, whose JWT is returned (it carries the user's
    /// own party, as production's does); the cookies are set too, with the party cookies on <paramref name="partyId"/>.
    /// </summary>
    private static async Task<IResult> GetTestUserToken(HttpContext context, int? userId, int? partyId, int? authenticationLevel, TestUsers testUsers, AuthenticationClient authentication, CancellationToken cancellationToken)
    {
        if (userId is not { } id)
        {
            return Results.BadRequest("userId is required.");
        }

        if (AuthLevels.ForNumber(authenticationLevel ?? AuthLevels.Default) is not { } level)
        {
            return Results.BadRequest("authenticationLevel must be 0, 1, 2, 3 or 4.");
        }

        var (jwt, error) = await SignIn(context, id, partyId, level, testUsers, authentication, cancellationToken);
        return error ?? Results.Text(jwt!);
    }

    /// <summary>An org token for an app running locally: a Maskinporten token for the organisation, through the production exchange.</summary>
    private static async Task<IResult> GetTestOrgToken(string? orgNumber, string? scopes, SigningKey key, MaskinportenSettings settings, AuthenticationClient authentication, CancellationToken cancellationToken)
    {
        var token = MaskinportenApi.Mint(key, settings, string.IsNullOrWhiteSpace(orgNumber) ? "991825827" : orgNumber, string.IsNullOrWhiteSpace(scopes) ? DefaultServiceOwnerScopes : scopes, MaskinportenApi.LocaltestClientId);
        var (exchanged, error) = await authentication.Exchange("maskinporten", token, cancellationToken);
        return exchanged is null ? Results.Problem(error, statusCode: 502) : Results.Text(exchanged);
    }

    /// <summary>Designer's credential against the platform, see <see cref="StudioToken"/>.</summary>
    private static async Task<IResult> GetTestStudioToken(StudioToken studioToken, CancellationToken cancellationToken)
    {
        var (token, error, status) = await studioToken.Get(cancellationToken);
        return token is null ? Results.Problem(error, statusCode: status) : Results.Text(token);
    }

    /// <summary>Signs the user in and appends the browser cookies; the JWT, or the error to answer.</summary>
    private static async Task<(string? Jwt, IResult? Error)> SignIn(HttpContext context, int userId, int? partyId, AuthLevels.Level level, TestUsers testUsers, AuthenticationClient authentication, CancellationToken cancellationToken)
    {
        TestUser[] users;
        try
        {
            users = await testUsers.GetAll(cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return (null, Results.Problem($"Could not read the test users from {testUsers.Endpoint}: {ex.Message}. Is the register component running?", statusCode: 502));
        }

        var user = users.FirstOrDefault(u => u.UserId == userId);
        if (user is null)
        {
            return (null, Results.NotFound($"No test user with userId {userId}."));
        }

        if (user.FindParty(partyId ?? user.PartyId) is not { } party)
        {
            return (null, Results.BadRequest($"Test user {userId} cannot act for party {partyId}."));
        }

        AuthenticationClient.SignInResult? result;
        string? error;
        try
        {
            (result, error) = await authentication.SignIn(user, level.Acr, cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            (result, error) = (null, $"Could not reach Authentication: {ex.Message}. Is the authentication component running?");
        }

        if (result is null)
        {
            return (null, Results.Problem(error, statusCode: 502));
        }

        foreach (var setCookie in AuthenticationClient.ForBrowser(result.SetCookies, party.PartyId, party.PartyUuid))
        {
            context.Response.Headers.Append("Set-Cookie", setCookie);
        }

        return (result.Jwt, null);
    }

    private static async Task<Dictionary<string, string?>> ReadFields(HttpRequest request, CancellationToken cancellationToken)
    {
        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (request.HasFormContentType)
        {
            foreach (var (key, value) in await request.ReadFormAsync(cancellationToken))
            {
                fields[key] = value.ToString();
            }
        }
        else if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var document = await JsonDocument.ParseAsync(request.Body, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    fields[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString(),
                        JsonValueKind.Null or JsonValueKind.Undefined => null,
                        _ => property.Value.GetRawText(),
                    };
                }
            }
        }

        return fields;
    }

    /// <summary>The old localtest's <c>UserSelect</c>: <c>userId.partyId</c>, or <c>userId</c> for the user's own party.</summary>
    private static bool TryParseUserSelect(string? value, out int userId, out int? partyId)
    {
        userId = 0;
        partyId = null;
        var parts = (value ?? string.Empty).Trim().Split('.');
        if (parts.Length > 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out userId))
        {
            return false;
        }

        if (parts.Length == 2)
        {
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var party))
            {
                return false;
            }

            partyId = party;
        }

        return true;
    }

    /// <summary>Relative URLs and URLs on the platform origin (where the apps run too); no open redirect.</summary>
    private static bool IsOwnOrigin(string url, LocaltestSettings localtest)
    {
        // Browsers drop tabs and newlines from URLs and read a backslash as a slash, so "/\t/evil.example" or
        // "/\\evil.example" would leave the origin; such URLs are refused outright.
        if (url.Any(c => char.IsControl(c) || c == '\\'))
        {
            return false;
        }

        if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var absolute)
            && Uri.Compare(absolute, new Uri(localtest.PlatformEndpoint), UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static void Option(StringBuilder html, string value, string text)
        => html.Append("<option value=\"").Append(Encode(value)).Append("\">").Append(Encode(text)).Append("</option>");

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
