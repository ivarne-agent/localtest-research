using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Localtest.Tokens;

/// <summary>
/// An RS256 signing key with its JWKS and a JWT writer: a key generated at start (the fake ID-porten's and Maskinporten's),
/// or the shared localtest certificate (platform access tokens). JWTs are written by hand (header and payload with
/// <see cref="Utf8JsonWriter"/>, signature with <see cref="RSA"/>) because the IdentityModel token libraries are not
/// verified for Native AOT. Generated keys need not survive a restart: the tokens live minutes, and Authentication's
/// localtest key retriever fetches the JWKS on every validation.
/// </summary>
public sealed class SigningKey : IDisposable
{
    private readonly RSA _rsa;
    private readonly RSAParameters _public;
    private readonly string? _thumbprint;

    public SigningKey()
        : this(RSA.Create(2048), keyId: null, thumbprint: null)
    {
    }

    private SigningKey(RSA rsa, string? keyId, string? thumbprint)
    {
        _rsa = rsa;
        _public = rsa.ExportParameters(includePrivateParameters: false);
        KeyId = keyId ?? Base64Url.EncodeToString(SHA256.HashData(_public.Modulus!))[..16];
        _thumbprint = thumbprint;
    }

    /// <summary>
    /// The key of a certificate with a private key. The header carries <c>kid</c> = the SHA-1 thumbprint in hex and
    /// <c>x5t</c>, as IdentityModel's <c>X509SigningCredentials</c> write them, so verifiers holding the certificate find it.
    /// </summary>
    public static SigningKey FromCertificate(X509Certificate2 certificate)
        => new(
            certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("The certificate has no RSA private key."),
            certificate.Thumbprint,
            Base64Url.EncodeToString(certificate.GetCertHash()));

    public string KeyId { get; }

    public JsonWebKeySet Jwks() => new(
    [
        new JsonWebKey("RSA", "sig", "RS256", KeyId, Base64Url.EncodeToString(_public.Modulus!), Base64Url.EncodeToString(_public.Exponent!)),
    ]);

    /// <summary>A signed JWT whose payload <paramref name="writeClaims"/> writes, between the object's braces.</summary>
    public string CreateJwt(Action<Utf8JsonWriter> writeClaims)
    {
        var header = Json(writer =>
        {
            writer.WriteString("alg", "RS256");
            writer.WriteString("typ", "JWT");
            writer.WriteString("kid", KeyId);
            if (_thumbprint is { } x5t)
            {
                writer.WriteString("x5t", x5t);
            }
        });
        var signingInput = Base64Url.EncodeToString(header) + "." + Base64Url.EncodeToString(Json(writeClaims));
        var signature = _rsa.SignData(System.Text.Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64Url.EncodeToString(signature);
    }

    public void Dispose() => _rsa.Dispose();

    private static byte[] Json(Action<Utf8JsonWriter> write)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            write(writer);
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}

public sealed record JsonWebKeySet(JsonWebKey[] Keys);

public sealed record JsonWebKey(
    [property: JsonPropertyName("kty")] string Kty,
    [property: JsonPropertyName("use")] string Use,
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("n")] string N,
    [property: JsonPropertyName("e")] string E);
