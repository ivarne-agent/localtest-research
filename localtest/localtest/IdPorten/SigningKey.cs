using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Localtest.IdPorten;

/// <summary>
/// An RS256 key generated at start, with its JWKS and a JWT writer. JWTs are written by hand (header and payload with
/// <see cref="Utf8JsonWriter"/>, signature with <see cref="RSA"/>) because the IdentityModel token libraries are not
/// verified for Native AOT. Nothing has to survive a restart: the tokens live minutes, and Authentication's localtest
/// key retriever fetches the JWKS on every validation.
/// </summary>
public sealed class SigningKey : IDisposable
{
    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RSAParameters _public;

    public SigningKey()
    {
        _public = _rsa.ExportParameters(includePrivateParameters: false);
        KeyId = Base64Url.EncodeToString(SHA256.HashData(_public.Modulus!))[..16];
    }

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
