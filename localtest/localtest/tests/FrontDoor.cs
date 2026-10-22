using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Localtest.Tests;

/// <summary>
/// The localtest app in a test host, with the configuration a runtime would give it (<c>services:{name}:http:0</c>
/// entries), its data folder in a temporary directory, and a generated certificate standing in for the shared test
/// certificate the Studio token is signed with.
/// </summary>
internal sealed class FrontDoor : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _settings;

    public FrontDoor(IDictionary<string, string?>? services = null, string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Directory.CreateTempSubdirectory("localtest-tests-").FullName;
        var certificate = Path.Combine(DataDirectory, "test.pfx");
        if (!File.Exists(certificate))
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=localtest-tests", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllBytes(certificate, generated.Export(X509ContentType.Pkcs12));
        }

        _settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Proxy:AppRoutesFile"] = Path.Combine(DataDirectory, "app-routes.json"),
            ["Proxy:FrontendRoot"] = Path.Combine(DataDirectory, "no-frontend"),
            ["Localtest:AccessTokenCertificatePath"] = certificate,
        };
        foreach (var (name, address) in services ?? new Dictionary<string, string?>())
        {
            _settings[$"services:{name}:http:0"] = address;
        }
    }

    public string DataDirectory { get; }

    /// <summary>A client that does not follow redirects, for the 301s.</summary>
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(_settings));
}
