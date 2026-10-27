using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The host's CA bundle (<c>STUDIO_CA_BUNDLE</c> in the AppHost's environment), as studioctl passes it on: a copy of its
/// usable certificates in the data directory under their digest, mounted read-only into the containers with
/// <c>caBundle</c> (pdf3), with the variable pointing at the mount; nothing without one.
/// </summary>
public sealed class CaBundleTests
{
    /// <summary>
    /// The copy of studioctl's golden bundle (<c>internal/cmd/env/localtestv2/testdata/ca-bundle.pem</c>, a comment line
    /// and one certificate), as the golden file names it.
    /// </summary>
    private const string GoldenCopy = "ca-bundle-c34e2872c51439ef.pem";

    /// <summary>studioctl's golden bundle, which its golden file's <c>everything</c> case has.</summary>
    internal static string GoldenBundle() => Path.Combine(AppContext.BaseDirectory, "golden", "ca-bundle.pem");

    private static string Bundle(string content)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ca-bundle-").FullName, "corporate.pem");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

    /// <summary>A self-signed CA certificate as PEM, with a line break at the end.</summary>
    private static string Certificate(string name)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={name}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddHours(-1),
            DateTimeOffset.UtcNow.AddHours(1)
        );
        return certificate.ExportCertificatePem() + "\n";
    }

    private const string Malformed = "-----BEGIN CERTIFICATE-----\nnot base64!\n-----END CERTIFICATE-----\n";

    private static IConfiguration Configuration(string? value) =>
        new ConfigurationBuilder().AddInMemoryCollection([new(LocaltestManifest.CaBundleVariable, value)]).Build();

    private static List<(string Container, string Source)> BundleMounts(SampleModel model) =>
        [
            .. model.Containers.SelectMany(c =>
                c.Annotations.OfType<ContainerMountAnnotation>()
                    .Where(m => m.Target == LocaltestManifest.CaBundlePath)
                    .Select(m => (c.Name, m.Source!))
            ),
        ];

    [Fact]
    public void TheGoldenBundlesCopyIsNamedAsStudioctlNamesIt()
    {
        var (bundle, warning) = HostCaBundle.Read(Configuration(GoldenBundle()));

        Assert.Null(warning);
        Assert.Equal(GoldenCopy, bundle?.FileName);
    }

    [Fact]
    public void UnsetIsNoneAndAnUnusableBundleWarns()
    {
        Assert.Equal((null, null), HostCaBundle.Read(Configuration(null)));
        Assert.Equal((null, null), HostCaBundle.Read(Configuration("")));

        foreach (
            var source in new[]
            {
                "/no/such/ca-bundle.pem",
                Directory.CreateTempSubdirectory("ca-bundle-").FullName,
                Bundle(""),
                Bundle("bundle"),
                Bundle(Malformed),
            }
        )
        {
            var (bundle, warning) = HostCaBundle.Read(Configuration(source));

            Assert.Null(bundle);
            Assert.StartsWith($"{LocaltestManifest.CaBundleVariable}={source}", warning, StringComparison.Ordinal);
            Assert.EndsWith("the containers start without the CA bundle", warning, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnUnusableCertificateIsSkippedWithAWarning()
    {
        var good = Certificate("good");
        var source = Bundle(Malformed + good);

        var (bundle, warning) = HostCaBundle.Read(Configuration(source));

        Assert.NotNull(bundle);
        Assert.Equal(
            $"{LocaltestManifest.CaBundleVariable} ({source}): skipping 1 unusable certificate, the containers trust the rest: line 1: malformed PEM block",
            warning
        );
        var dataDirectory = Directory.CreateTempSubdirectory("data-").FullName;
        HostCaBundle.Ensure(dataDirectory, bundle);
        Assert.Equal(good, File.ReadAllText(Path.Combine(HostCaBundle.Directory(dataDirectory), bundle.FileName)));
    }

    [Fact]
    public async Task OnlyTheContainersWithCaBundleGetIt()
    {
        var model = SampleModel.Build(["everything"], caBundle: GoldenBundle());
        var copy = Path.Combine(SampleModel.DataDirectory, "ca-bundle", GoldenCopy);

        Assert.Equal(
            LocaltestManifest.Default.Containers.Where(c => c.Value.CaBundle).Select(c => (c.Key, copy)),
            BundleMounts(model)
        );
        Assert.Equal(["pdf3"], BundleMounts(model).Select(m => m.Container));
        Assert.True(
            model
                .Container("pdf3")
                .Annotations.OfType<ContainerMountAnnotation>()
                .Single(m => m.Target == LocaltestManifest.CaBundlePath)
                .IsReadOnly
        );
        foreach (var container in model.Containers)
        {
            var environment = await model.Environment(container);
            Assert.Equal(
                container.Name == "pdf3" ? LocaltestManifest.CaBundlePath : null,
                environment.GetValueOrDefault(LocaltestManifest.CaBundleVariable)
            );
        }
    }

    [Fact]
    public async Task WithoutABundleNothingChanges()
    {
        foreach (var model in new[] { SampleModel.Build(["everything"]), SampleModel.BuildMinimal() })
        {
            Assert.Empty(BundleMounts(model));
            foreach (var container in model.Containers)
            {
                Assert.DoesNotContain(LocaltestManifest.CaBundleVariable, (await model.Environment(container)).Keys);
            }
        }
    }

    [Fact]
    public void TheCopyIsReadableAndReplacedWhenTheBundleChanges()
    {
        var dataDirectory = Directory.CreateTempSubdirectory("data-").FullName;
        var firstCertificate = Certificate("first");
        var (first, _) = HostCaBundle.Read(Configuration(Bundle(firstCertificate)));
        var (second, _) = HostCaBundle.Read(Configuration(Bundle(Certificate("second"))));
        var folder = HostCaBundle.Directory(dataDirectory);

        HostCaBundle.Ensure(dataDirectory, first);
        var copy = Path.Combine(folder, first!.FileName);
        Assert.Equal(firstCertificate, File.ReadAllText(copy));
        Assert.Equal(
            "ca-bundle-" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(copy)))[..16] + ".pem",
            first.FileName
        );
        if (!OperatingSystem.IsWindows())
        {
            Assert.True(File.GetUnixFileMode(copy).HasFlag(UnixFileMode.OtherRead));
        }

        HostCaBundle.Ensure(dataDirectory, second);
        Assert.Equal([second!.FileName], Directory.EnumerateFiles(folder).Select(Path.GetFileName));

        HostCaBundle.Ensure(dataDirectory, null);
        Assert.False(Directory.Exists(folder));
    }
}
