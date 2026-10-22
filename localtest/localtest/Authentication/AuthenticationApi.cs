using System.Text.Json.Serialization;
using Localtest.Home;
using Localtest.IdPorten;
using Localtest.Proxy;

namespace Localtest.Authentication;

/// <summary>
/// Production Authentication as an OpenID Connect provider for a registered client, driven without a browser, under
/// <c>localtest/api/authentication/</c>: <c>code</c> (a code for the client and the verifier to redeem it), <c>token</c>
/// (the whole code flow, the token response with the access token's <c>pid</c>), <c>refresh</c> (the refresh grant),
/// <c>callback</c> (the seeded <c>localtest-smoke</c> client's redirect URI). The client acts with the seeded secret
/// (<see cref="SeededClients"/>). <c>code</c> and <c>token</c> also set the session cookies production's callback set, as
/// <c>/Home/GetTestUserToken</c> does, so a later <c>openid/logout</c> with them ends that session. Anonymous, local only.
/// </summary>
public static class AuthenticationApi
{
    public const string CallbackPath = "/localtest/api/authentication/callback";

    public static void Map(WebApplication app)
    {
        var api = app.MapGroup("/localtest/api/authentication");

        api.MapGet("/code", async Task<IResult> (HttpContext context, string? client, string? scope, int? userId, int? partyId, int? authenticationLevel, TestUsers testUsers, CodeFlow flow, LocaltestSettings localtest, CancellationToken cancellationToken) =>
        {
            var (authorization, error) = await Authorize(context, client, scope, userId, partyId, authenticationLevel, testUsers, flow, localtest, cancellationToken);
            return error is not null ? error : Results.Json(
                new CodeResponse(authorization!.ClientId, authorization.Code, authorization.CodeVerifier, authorization.RedirectUri, authorization.State, authorization.Nonce),
                LocaltestJsonContext.Default.CodeResponse);
        });

        api.MapGet("/token", async Task<IResult> (HttpContext context, string? client, string? scope, int? userId, int? partyId, int? authenticationLevel, TestUsers testUsers, CodeFlow flow, LocaltestSettings localtest, CancellationToken cancellationToken) =>
        {
            var (authorization, error) = await Authorize(context, client, scope, userId, partyId, authenticationLevel, testUsers, flow, localtest, cancellationToken);
            if (error is not null)
            {
                return error;
            }

            var (status, body) = await flow.Token(SeededClients.Find(authorization!.ClientId, localtest)!, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = authorization.ClientId,
                ["code"] = authorization.Code,
                ["code_verifier"] = authorization.CodeVerifier,
                ["redirect_uri"] = authorization.RedirectUri,
            }, cancellationToken);
            return TokenResult(status, body);
        });

        api.MapPost("/refresh", async Task<IResult> (HttpRequest request, CodeFlow flow, LocaltestSettings localtest, CancellationToken cancellationToken) =>
        {
            var fields = request.HasFormContentType ? await request.ReadFormAsync(cancellationToken) : null;
            string? Field(string name) => fields?[name] is { Count: > 0 } value ? value.ToString() : request.Query[name] is { Count: > 0 } query ? query.ToString() : null;

            var clientId = Field("client") ?? SeededClients.SmokeClientId;
            if (SeededClients.Find(clientId, localtest) is not { } client)
            {
                return UnknownClient(clientId, localtest);
            }

            if (Field("refresh_token") is not { Length: > 0 } refreshToken)
            {
                return Results.BadRequest("refresh_token is required (form or query).");
            }

            var (status, body) = await flow.Token(client, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = client.ClientId,
                ["refresh_token"] = refreshToken,
            }, cancellationToken);
            return TokenResult(status, body);
        });

        // localtest-smoke's redirect_uri. The helper reads the redirect without following it; a browser that follows it
        // (a test driving the picker itself) gets the parameters back to redeem the code with the verifier it kept.
        api.MapGet("/callback", (HttpRequest request) => Results.Json(
            request.Query.ToDictionary(p => p.Key, p => p.Value.ToString()),
            LocaltestJsonContext.Default.DictionaryStringString));
    }

    /// <summary>Resolves the client, the user, the party and the level as <c>/Home/GetTestUserToken</c> does, and runs the flow up to the code.</summary>
    private static async Task<(CodeFlow.Authorization? Authorization, IResult? Error)> Authorize(HttpContext context, string? clientId, string? scope, int? userId, int? partyId, int? authenticationLevel, TestUsers testUsers, CodeFlow flow, LocaltestSettings localtest, CancellationToken cancellationToken)
    {
        clientId ??= SeededClients.SmokeClientId;
        if (SeededClients.Find(clientId, localtest) is not { } client)
        {
            return (null, UnknownClient(clientId, localtest));
        }

        if (userId is not { } id)
        {
            return (null, Results.BadRequest("userId is required."));
        }

        if (AuthLevels.ForNumber(authenticationLevel ?? AuthLevels.Default) is not { } level)
        {
            return (null, Results.BadRequest("authenticationLevel must be 0, 1, 2, 3 or 4."));
        }

        TestUser[] users;
        try
        {
            users = await testUsers.GetAll(cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            return (null, Results.Problem($"Could not read the test users from {testUsers.Endpoint}: {ex.Message}. Is the register component running?", statusCode: 502));
        }

        if (users.FirstOrDefault(u => u.UserId == id) is not { } user)
        {
            return (null, Results.NotFound($"No test user with userId {id}."));
        }

        if (user.FindParty(partyId ?? user.PartyId) is not { } party)
        {
            return (null, Results.BadRequest($"Test user {id} cannot act for party {partyId}."));
        }

        CodeFlow.Authorization? authorization;
        string? error;
        try
        {
            (authorization, error) = await flow.Authorize(client, string.IsNullOrWhiteSpace(scope) ? SeededClients.DefaultScope : scope, user, level, cancellationToken);
        }
        catch (Exception ex) when (ServiceCalls.Unreachable(ex))
        {
            (authorization, error) = (null, $"Could not reach Authentication: {ex.Message}. Is the authentication component running?");
        }

        if (authorization is null)
        {
            return (null, Results.Problem(error, statusCode: 502));
        }

        foreach (var setCookie in AuthenticationClient.ForBrowser(authorization.SetCookies, party.PartyId, party.PartyUuid))
        {
            context.Response.Headers.Append("Set-Cookie", setCookie);
        }

        return (authorization, null);
    }

    /// <summary>Production's answer with its status; a success body gets the access token's <c>pid</c>.</summary>
    private static IResult TokenResult(int status, string body)
        => Results.Text(status == StatusCodes.Status200OK ? CodeFlow.WithPid(body) : body, "application/json", statusCode: status);

    private static IResult UnknownClient(string clientId, LocaltestSettings localtest)
        => Results.BadRequest($"Unknown client '{clientId}'. Seeded clients: {string.Join(", ", SeededClients.All(localtest).Select(c => c.ClientId))}.");
}

public sealed record CodeResponse(
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("code_verifier")] string CodeVerifier,
    [property: JsonPropertyName("redirect_uri")] string RedirectUri,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("nonce")] string Nonce);
