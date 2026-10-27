using System.Text;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The host's CA bundle (<c>STUDIO_CA_BUNDLE</c> in the AppHost's environment), as studioctl passes it on: a copy in the
/// data directory under its hash, mounted read-only into the containers with <c>caBundle</c> (pdf3), with the variable
/// pointing at the mount; nothing without one.
/// </summary>
public sealed class CaBundleTests
{
    /// <summary>
    /// The content of studioctl's golden bundle (<c>testCABundleContent</c> in
    /// <c>internal/cmd/env/localtestv2/cabundle_internal_test.go</c>), whose copy is <c>8901b546104a875e.pem</c>.
    /// </summary>
    private const string GoldenContent = "localtest-v2 test CA bundle\n";

    /// <summary>A file holding the golden bundle's content.</summary>
    internal static string GoldenBundle() => Bundle(GoldenContent);

    private static string Bundle(string content)
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory("ca-bundle-").FullName, "corporate.pem");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(content));
        return path;
    }

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
        Assert.Equal("8901b546104a875e.pem", bundle?.FileName);
    }

    [Fact]
    public void UnsetOrMissingIsNoneAndUnreadableWarns()
    {
        Assert.Equal((null, null), HostCaBundle.Read(Configuration(null)));
        Assert.Equal((null, null), HostCaBundle.Read(Configuration("")));
        Assert.Equal((null, null), HostCaBundle.Read(Configuration("/no/such/ca-bundle.pem")));

        var directory = Directory.CreateTempSubdirectory("ca-bundle-").FullName;
        var (bundle, warning) = HostCaBundle.Read(Configuration(directory));

        Assert.Null(bundle);
        Assert.StartsWith($"{LocaltestManifest.CaBundleVariable}={directory}: ", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyTheContainersWithCaBundleGetIt()
    {
        var model = SampleModel.Build(["everything"], caBundle: GoldenBundle());
        var copy = Path.Combine(SampleModel.DataDirectory, "ca-bundle", "8901b546104a875e.pem");

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
        var (first, _) = HostCaBundle.Read(Configuration(Bundle("first")));
        var (second, _) = HostCaBundle.Read(Configuration(Bundle("second")));
        var folder = HostCaBundle.Directory(dataDirectory);

        HostCaBundle.Ensure(dataDirectory, first);
        var copy = Path.Combine(folder, first!.FileName);
        Assert.Equal("first", File.ReadAllText(copy));
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
