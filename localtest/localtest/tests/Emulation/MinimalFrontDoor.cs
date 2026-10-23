using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Localtest.Emulated;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Localtest.Tests.Emulation;

/// <summary>
/// The localtest app in minimal mode: every service the front door emulates has its own address,
/// <c>http://localtest:8080</c>, as <c>studioctl env up --minimal</c> sets it. The settings go in as host settings
/// (<c>UseSetting</c>), which the builder's configuration sees, because the mode is decided from it (as from the
/// environment in a container); <paramref name="services"/> adds or overrides addresses (a fake Storage).
/// </summary>
internal sealed class MinimalFrontDoor : WebApplicationFactory<Program>
{
    public const string Self = "http://localtest:8080";
    public const string Public = "local.altinn.cloud:8000";
    public const string Origin = "http://local.altinn.cloud:8000";

    private readonly Dictionary<string, string?> _settings;

    public MinimalFrontDoor(IDictionary<string, string?>? services = null, IEnumerable<string>? emulated = null)
    {
        DataDirectory = Directory.CreateTempSubdirectory("localtest-minimal-tests-").FullName;
        Certificate = Path.Combine(DataDirectory, "test.pfx");
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localtest-tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(Certificate, generated.Export(X509ContentType.Pkcs12));
        }

        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            // In memory, as the minimal mode runs it (Proxy__AppRoutesFile=).
            ["Proxy:AppRoutesFile"] = string.Empty,
            ["Proxy:FrontendRoot"] = Path.Combine(DataDirectory, "no-frontend"),
            ["Localtest:AccessTokenCertificatePath"] = Certificate,
        };
        // As every runtime sets it, in both modes: the front door's own entry.
        _settings["services:localtest:http:0"] = Self;
        foreach (var name in emulated ?? EmulationMode.Emulatable)
        {
            _settings[$"services:{name}:http:0"] = Self;
        }

        foreach (var (name, address) in services ?? new Dictionary<string, string?>())
        {
            _settings[$"services:{name}:http:0"] = address;
        }
    }

    public string DataDirectory { get; }

    public string Certificate { get; }

    /// <summary>A client on the public host that does not follow redirects or keep cookies.</summary>
    public HttpClient Client()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri(Origin) });
        client.DefaultRequestHeaders.Host = Public;
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in _settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>The payload of a JWT.</summary>
    public static JsonElement Claims(string jwt)
    {
        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(jwt.Trim().Trim('"').Split('.')[1]));
        return document.RootElement.Clone();
    }

    public static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    public static AuthenticationHeaderValue Bearer(string token) => new("Bearer", token.Trim().Trim('"'));
}
