using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The local Maskinporten client: the bytes studioctl writes (its golden file), a working key generated once and kept,
/// the origin followed, a developer's client left alone.
/// </summary>
public sealed class LocalMaskinportenClientTests : IDisposable
{
    private const string DefaultOrigin = "http://local.altinn.cloud:8000/";
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("altinn-app-aspire-");
    private readonly string _secrets;
    private readonly string _filePath;

    public LocalMaskinportenClientTests()
    {
        _secrets = Path.Combine(_folder.FullName, "apps", "ttd", "testapp", "secrets");
        _filePath = Path.Combine(_secrets, "maskinporten-settings.json");
    }

    public void Dispose() => _folder.Delete(recursive: true);

    private JsonObject Settings() => (JsonObject)JsonNode.Parse(File.ReadAllText(_filePath))!["MaskinportenSettings"]!;

    [Fact]
    public void TheFileIsTheBytesStudioctlWrites()
    {
        // studioctl's golden key: placeholder values, in the order its test gives them.
        var jwk = (JsonObject)
            JsonNode.Parse(
                """{"kty":"RSA","use":"sig","kid":"test-key","alg":"RS256","n":"modulus","e":"AQAB","d":"private-part","p":"p","q":"q","qi":"qi","dp":"dp","dq":"dq"}"""
            )!;

        Assert.Equal(
            Encoding.UTF8.GetString(
                File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", "local-maskinporten.golden.json"))
            ),
            Encoding.UTF8.GetString(LocalMaskinportenClient.Encode("http://local.altinn.cloud:8000", "ttd", jwk))
        );
    }

    [Fact]
    public void TheLocalClientPointsAtTheFakeWithAWorkingKey()
    {
        Assert.True(LocalMaskinportenClient.Provision(_secrets, DefaultOrigin, "ttd"));

        var settings = Settings();
        Assert.Equal("http://local.altinn.cloud:8000/localtest/api/maskinporten/", (string?)settings["authority"]);
        Assert.Equal("ttd-localtest", (string?)settings["clientId"]);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(_filePath));
        }
        var jwk = (JsonObject)settings["jwk"]!;
        foreach (var field in new[] { "kty", "use", "kid", "alg", "n", "e", "d", "p", "q", "qi", "dp", "dq" })
        {
            Assert.False(string.IsNullOrEmpty((string?)jwk[field]), field);
        }
        // The app libraries sign the grant with it: import it and sign.
        using var rsa = RSA.Create();
        rsa.ImportParameters(
            new RSAParameters
            {
                Modulus = Decode(jwk, "n"),
                Exponent = Decode(jwk, "e"),
                D = Decode(jwk, "d"),
                P = Decode(jwk, "p"),
                Q = Decode(jwk, "q"),
                InverseQ = Decode(jwk, "qi"),
                DP = Decode(jwk, "dp"),
                DQ = Decode(jwk, "dq"),
            }
        );
        var data = "grant"u8.ToArray();
        var signature = rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.True(rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        Assert.Equal(2048, rsa.KeySize);
    }

    private static byte[] Decode(JsonObject jwk, string field) =>
        System.Buffers.Text.Base64Url.DecodeFromChars((string)jwk[field]!);

    [Fact]
    public void TheKeyIsKeptAndTheOriginFollowed()
    {
        LocalMaskinportenClient.Provision(_secrets, DefaultOrigin, "ttd");
        var first = Settings()["jwk"]!.ToJsonString();
        var written = File.GetLastWriteTimeUtc(_filePath);

        Assert.False(LocalMaskinportenClient.Provision(_secrets, DefaultOrigin, "ttd"));
        Assert.Equal(written, File.GetLastWriteTimeUtc(_filePath));

        LocalMaskinportenClient.Provision(_secrets, "http://local.altinn.test:18000/", "ttd");
        Assert.Equal("http://local.altinn.test:18000/localtest/api/maskinporten/", (string?)Settings()["authority"]);
        Assert.Equal(first, Settings()["jwk"]!.ToJsonString());
        Assert.Single(Directory.EnumerateFileSystemEntries(_secrets));
    }

    [Fact]
    public void ADevelopersClientIsLeftAlone()
    {
        Directory.CreateDirectory(_secrets);
        const string developers = """
            {"MaskinportenSettings":{"authority":"https://test.maskinporten.no/","clientId":"real-client","jwk":{}}}
            """;
        File.WriteAllText(_filePath, developers);

        Assert.False(LocalMaskinportenClient.Provision(_secrets, DefaultOrigin, "ttd"));
        Assert.Equal(developers, File.ReadAllText(_filePath));

        File.WriteAllText(_filePath, "{not json");
        Assert.False(LocalMaskinportenClient.Provision(_secrets, DefaultOrigin, "ttd"));
        Assert.Equal("{not json", File.ReadAllText(_filePath));
    }

    [Theory]
    [InlineData("http://local.altinn.cloud:8000/localtest/api/maskinporten/", "ttd-localtest", true)]
    [InlineData("https://test.maskinporten.no/", "ttd-localtest", false)]
    [InlineData("http://local.altinn.cloud:8000/localtest/api/maskinporten/", "real-client", false)]
    [InlineData("http://local.altinn.cloud:8000/localtest/api/maskinporten", "ttd-localtest", false)]
    public void TheLocalClientIsRecognisedAsStudioctlRecognisesIt(string authority, string clientId, bool local) =>
        Assert.Equal(local, LocalMaskinportenClient.IsLocal(authority, clientId));
}
