using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Localtest.Tokens;

namespace Localtest.Emulated;

/// <summary>Reading and checking compact RS256 JWTs by hand, as <see cref="SigningKey"/> writes them (no IdentityModel under AOT).</summary>
public static class Jwt
{
    /// <summary>The payload of a JWT, without any check; null when it is not a JWT.</summary>
    public static JsonElement? Payload(string? jwt)
    {
        var parts = (jwt ?? string.Empty).Split('.');
        if (parts.Length != 3 || !Base64Url.IsValid(parts[1]))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[1]));
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The payload when the JWT is signed with RS256 by a key of <paramref name="keys"/> and within its lifetime (no clock
    /// skew, as Storage validates); otherwise null and why.
    /// </summary>
    public static (JsonElement? Payload, string? Error) Validate(string? jwt, JsonWebKeySet keys)
    {
        var parts = (jwt ?? string.Empty).Split('.');
        if (parts.Length != 3 || !Base64Url.IsValid(parts[0]) || !Base64Url.IsValid(parts[1]) || !Base64Url.IsValid(parts[2]))
        {
            return (null, "not a JWT");
        }

        string? kid;
        try
        {
            using var header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));
            if (!header.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "RS256")
            {
                return (null, "not RS256");
            }

            kid = header.RootElement.TryGetProperty("kid", out var k) ? k.GetString() : null;
        }
        catch (JsonException)
        {
            return (null, "not a JWT");
        }

        var signed = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
        var signature = Base64Url.DecodeFromChars(parts[2]);
        var verified = keys.Keys.Where(k => kid is null || k.Kid == kid).Any(key =>
        {
            using var rsa = RSA.Create(new RSAParameters { Modulus = Base64Url.DecodeFromChars(key.N), Exponent = Base64Url.DecodeFromChars(key.E) });
            return rsa.VerifyData(signed, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        });
        if (!verified)
        {
            return (null, "the signature does not match the key");
        }

        if (Payload(jwt) is not { } payload)
        {
            return (null, "not a JWT");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (payload.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var expires) && expires < now)
        {
            return (null, "expired");
        }

        if (payload.TryGetProperty("nbf", out var nbf) && nbf.TryGetInt64(out var notBefore) && notBefore > now)
        {
            return (null, "not yet valid");
        }

        return (payload, null);
    }

    /// <summary>A string claim, or a number claim as its text; null when absent.</summary>
    public static string? Claim(JsonElement payload, string name)
        => payload.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            }
            : null;

    /// <summary>The bearer token of the request, or the <c>AltinnStudioRuntime</c> cookie.</summary>
    public static string? FromRequest(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return authorization["Bearer ".Length..].Trim();
        }

        return request.Cookies.TryGetValue(TokenMinter.CookieName, out var cookie) ? cookie : null;
    }
}
