using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Localtest.Home;
using Localtest.IdPorten;
using Microsoft.AspNetCore.WebUtilities;

namespace Localtest.Authentication;

/// <summary>
/// Production Authentication's authorization-code flow for a registered client, run headlessly: what a relying party
/// such as Arbeidsflate's BFF does with a browser. <c>authorize</c> with PKCE (S256), state, nonce and the level's
/// <c>acr_values</c>; the redirect to the fake ID-porten is answered for the chosen user without the picker and
/// production <c>upstream/callback</c> is called (the steps <see cref="AuthenticationClient.SignInUpstream"/> shares with
/// <c>/Home</c>'s sign-in); its redirect to the client's <c>redirect_uri</c> is read, not followed, for <c>code</c> and
/// <c>state</c>; <c>token</c> is called with <c>client_secret_basic</c>.
/// </summary>
public sealed class CodeFlow(AuthenticationClient authentication, IHttpClientFactory httpClientFactory)
{
    /// <summary>A code for the client, with what is needed to redeem it, and the session cookies the callback set.</summary>
    public sealed record Authorization(string ClientId, string Code, string CodeVerifier, string RedirectUri, string State, string Nonce, string[] SetCookies);

    public async Task<(Authorization? Result, string? Error)> Authorize(SeededClients.Client client, string scope, TestUser user, AuthLevels.Level level, CancellationToken cancellationToken)
    {
        var verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        var authorize = QueryHelpers.AddQueryString("authentication/api/v1/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code",
            ["client_id"] = client.ClientId,
            ["redirect_uri"] = client.RedirectUri,
            ["scope"] = scope,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["acr_values"] = level.Acr,
        });

        var (callback, error) = await authentication.SignInUpstream(authorize, user, level.Acr, cancellationToken);
        if (callback is null)
        {
            return (null, error);
        }

        if (callback.Location is not { IsAbsoluteUri: true } location)
        {
            return (null, $"Production upstream/callback answered {(int)callback.Status} without a redirect to the client: {callback.Body}");
        }

        var query = QueryHelpers.ParseQuery(location.Query);
        string? Parameter(string name) => query.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value.ToString() : null;
        if (Parameter("error") is { } clientError)
        {
            return (null, $"Production Authentication redirected to the client with error {clientError}: {Parameter("error_description")}");
        }

        if (location.GetLeftPart(UriPartial.Path) != new Uri(client.RedirectUri).GetLeftPart(UriPartial.Path) || Parameter("state") != state || Parameter("code") is not { } code)
        {
            return (null, $"Production upstream/callback redirected to {location} instead of the client's redirect_uri with a code and the request's state.");
        }

        return (new Authorization(client.ClientId, code, verifier, client.RedirectUri, state, nonce, callback.SetCookies), null);
    }

    /// <summary>Production <c>token</c> as the client, with <c>client_secret_basic</c>: the status and the body as answered.</summary>
    public async Task<(int Status, string Body)> Token(SeededClients.Client client, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        using var http = httpClientFactory.CreateClient(AuthenticationClient.HttpClientName);
        using var request = authentication.Request(HttpMethod.Post, "authentication/api/v1/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(client.ClientId + ":" + client.Secret)));
        request.Content = new FormUrlEncodedContent(form);
        using var response = await http.SendAsync(request, cancellationToken);
        return ((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    /// <summary>
    /// A token response with the access token's <c>pid</c> added (null when it has none), or the body unchanged when it
    /// is not a JSON object.
    /// </summary>
    public static string WithPid(string tokenResponse)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(tokenResponse);
        }
        catch (JsonException)
        {
            return tokenResponse;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return tokenResponse;
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    property.WriteTo(writer);
                }

                var pid = document.RootElement.TryGetProperty("access_token", out var accessToken) && accessToken.ValueKind == JsonValueKind.String
                    ? Claim(accessToken.GetString()!, "pid")
                    : null;
                if (pid is null)
                {
                    writer.WriteNull("pid");
                }
                else
                {
                    writer.WriteString("pid", pid);
                }

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    /// <summary>A string claim of a JWT's payload, unverified.</summary>
    private static string? Claim(string jwt, string name)
    {
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return payload.RootElement.ValueKind == JsonValueKind.Object && payload.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return null;
        }
    }
}
