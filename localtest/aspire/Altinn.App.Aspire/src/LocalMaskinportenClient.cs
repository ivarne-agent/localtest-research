using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Altinn.App.Aspire;

/// <summary>
/// The local Maskinporten client, a port of studioctl's <c>internal/appsecrets/localclient.go</c>: what an app gets in
/// its secrets directory when no client of the developer's is there. It points at the stack's Maskinporten fake, so
/// the app libraries' real <c>MaskinportenClient</c> runs on the stack as it runs in a cluster: a signed grant to the
/// fake, then production Authentication's <c>exchange/maskinporten</c>, which names the app's org. The file is the
/// operator's format (the client wrapped in <c>MaskinportenSettings</c>), the same bytes as studioctl's for the same
/// origin, org and key (the shared golden file <c>internal/appsecrets/testdata/local-maskinporten.golden.json</c>).
/// </summary>
internal static class LocalMaskinportenClient
{
    /// <summary>The fake's path on the public origin; the trailing slash matters for the discovery document.</summary>
    public const string AuthorityPath = "/localtest/api/maskinporten/";

    private const string WrapperKey = "MaskinportenSettings";
    private const string ClientIdSuffix = "-localtest";

    private static readonly JsonSerializerOptions _indented = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The local client's authority for a stack at <paramref name="origin" /> (scheme, host and port).</summary>
    public static string Authority(string origin) => origin.TrimEnd('/') + AuthorityPath;

    /// <summary>The local client's id for an org.</summary>
    public static string ClientId(string org) => org + ClientIdSuffix;

    /// <summary>
    /// Whether a client is the local one rather than the developer's: its id ends in <c>-localtest</c> and its
    /// authority is the stack's Maskinporten fake.
    /// </summary>
    public static bool IsLocal(string? authority, string? clientId) =>
        clientId is not null
        && clientId.EndsWith(ClientIdSuffix, StringComparison.Ordinal)
        && Uri.TryCreate(authority, UriKind.Absolute, out var uri)
        && uri.AbsolutePath == AuthorityPath;

    /// <summary>The local client's file for an origin, an org and a JWK.</summary>
    public static byte[] Encode(string origin, string org, JsonObject jwk)
    {
        var root = new JsonObject
        {
            [WrapperKey] = new JsonObject
            {
                ["authority"] = Authority(origin),
                ["clientId"] = ClientId(org),
                ["jwk"] = jwk.DeepClone(),
            },
        };
        return Encoding.UTF8.GetBytes(root.ToJsonString(_indented) + "\n");
    }

    /// <summary>
    /// Writes the local client into the secrets directory, unless the developer put a client of their own there, which
    /// is never overwritten (an unreadable file is left alone too). The key is generated once per app and kept: a later
    /// start reuses the key of the local client already there and rewrites the file only when the origin moved.
    /// Nothing rotates it and nothing in it expires (the JWK carries no validity): rotation is the operator's in a
    /// cluster, out of scope locally. Atomic and owner-only, as studioctl writes it.
    /// </summary>
    /// <returns>The developer's client id when one is there (the local client is then not written), else null.</returns>
    public static string? Provision(string directory, string origin, string org)
    {
        var path = Path.Combine(directory, Manifest.AppEnvironment.MaskinportenFileName);
        JsonObject? jwk = null;
        if (File.Exists(path))
        {
            JsonObject? settings;
            string? authority;
            string? clientId;
            try
            {
                settings = JsonNode.Parse(File.ReadAllText(path))?[WrapperKey] as JsonObject;
                authority = settings?["authority"]?.GetValue<string>();
                clientId = settings?["clientId"]?.GetValue<string>();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException)
            {
                // Unreadable: the developer's to fix, never replaced here.
                return null;
            }
            if (!IsLocal(authority, clientId))
            {
                return clientId ?? "";
            }
            jwk = settings!["jwk"] as JsonObject;
        }
        var payload = Encode(origin, org, jwk ?? GenerateKey());
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(payload))
        {
            return null;
        }
        AppSecrets.WriteOwnerOnly(directory, Manifest.AppEnvironment.MaskinportenFileName, payload);
        return null;
    }

    /// <summary>A 2048-bit RSA key as a JWK with every field the app libraries' converter requires.</summary>
    public static JsonObject GenerateKey()
    {
        using var rsa = RSA.Create(2048);
        var key = rsa.ExportParameters(includePrivateParameters: true);
        static string B(byte[]? value) => Base64Url.EncodeToString(value);
        return new JsonObject
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["kid"] = "localtest-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8)),
            ["alg"] = "RS256",
            ["n"] = B(key.Modulus),
            ["e"] = B(key.Exponent),
            ["d"] = B(key.D),
            ["p"] = B(key.P),
            ["q"] = B(key.Q),
            ["qi"] = B(key.InverseQ),
            ["dp"] = B(key.DP),
            ["dq"] = B(key.DQ),
        };
    }
}
