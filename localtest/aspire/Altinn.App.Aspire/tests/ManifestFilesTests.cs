using System.Text;
using System.Text.Json.Nodes;
using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The manifest's <c>files</c>: the loader's validation (the Go loader's), the read-only mounts, and how the files are
/// written out before the containers start.
/// </summary>
public sealed class ManifestFilesTests
{
    private const string Valid = """
        {
          "groups": [{ "name": "default", "containers": ["localtest"] }],
          "public": { "host": "example.test", "port": 8000 },
          "containers": {
            "localtest": {
              "registry": "example.org/collector", "tag": "1",
              "files": {
                "/etc/collector/config.yaml": "monitoring/collector.yaml",
                "/etc/dashboards": "monitoring/dashboards"
              },
              "volumes": { "collector-data": "/data" }
            }
          }
        }
        """;

    private static ManifestFiles Shipped() =>
        ManifestFiles.From(
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["manifest.json"] = Encoding.UTF8.GetBytes(Valid),
                ["monitoring/collector.yaml"] = Encoding.UTF8.GetBytes("receivers: {}\n"),
                ["monitoring/dashboards/app.json"] = Encoding.UTF8.GetBytes("{}\n"),
                ["monitoring/dashboards/engine.json"] = Encoding.UTF8.GetBytes("{}\n"),
            }
        );

    private static LocaltestManifest Parse(string json, ManifestFiles? files = null) =>
        LocaltestManifest.Parse(Encoding.UTF8.GetBytes(json), files ?? Shipped());

    /// <summary>The valid manifest with the collector's files replaced by one entry.</summary>
    private static string WithFile(string target, string source)
    {
        var manifest = JsonNode.Parse(Valid)!;
        manifest["containers"]!["localtest"]!["files"] = new JsonObject { [target] = source };
        return manifest.ToJsonString();
    }

    [Fact]
    public void FilesAndFoldersAreRead()
    {
        var manifest = Parse(Valid);

        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["/etc/collector/config.yaml"] = "monitoring/collector.yaml",
                ["/etc/dashboards"] = "monitoring/dashboards",
            },
            manifest.Containers["localtest"].Files
        );
        // Only the folders beside the manifest ship, not the manifest itself.
        Assert.Equal(
            ["monitoring/collector.yaml", "monitoring/dashboards/app.json", "monitoring/dashboards/engine.json"],
            manifest.Files.Names
        );
        Assert.Equal("receivers: {}\n", Encoding.UTF8.GetString(manifest.Files.Read("monitoring/collector.yaml")));
        Assert.Throws<FileNotFoundException>(() => manifest.Files.Read("manifest.json"));
        // In the embedded manifest only the containers the package does not run mount files: the monitoring group's
        // and minimal mode's otel-files (an OTLP receiver).
        Assert.Equal(
            ["grafana", "otel-collector", "otel-files"],
            LocaltestManifest
                .Default.Containers.Where(c => c.Value.Files.Count > 0)
                .Select(c => c.Key)
                .Order(StringComparer.Ordinal)
        );
    }

    public static TheoryData<string, string, string> Faults =>
        new()
        {
            { "etc/config.yaml", "monitoring/collector.yaml", "not an absolute, clean path" },
            { "/", "monitoring/dashboards", "not an absolute, clean path" },
            { "/etc//config.yaml", "monitoring/collector.yaml", "not an absolute, clean path" },
            { "/etc/dashboards/", "monitoring/dashboards", "not an absolute, clean path" },
            { "/etc/../config.yaml", "monitoring/collector.yaml", "not an absolute, clean path" },
            { "/etc/config.yaml", "/monitoring/collector.yaml", "not a clean relative path" },
            { "/etc/config.yaml", "../monitoring/collector.yaml", "not a clean relative path" },
            { "/etc/config.yaml", "monitoring/../monitoring/collector.yaml", "not a clean relative path" },
            { "/etc/config.yaml", "./monitoring/collector.yaml", "not a clean relative path" },
            { "/etc/config", ".", "not a clean relative path" },
            { "/etc/config.yaml", "", "not a clean relative path" },
            { "/etc/config.yaml", "monitoring\\collector.yaml", "not a clean relative path" },
            { "/etc/config.yaml", "monitoring/grafana.yaml", "no such file beside the manifest" },
            { "/etc/config.yaml", "monitoring/coll", "no such file beside the manifest" },
            { "/etc/manifest.json", "manifest.json", "no such file beside the manifest" },
            { "/data", "monitoring/dashboards", "is a volume's too" },
        };

    [Theory]
    [MemberData(nameof(Faults))]
    public void ABadFileIsRefused(string target, string source, string fault)
    {
        var error = Assert.Throws<InvalidDataException>(() => Parse(WithFile(target, source)));

        Assert.Contains("container localtest:", error.Message, StringComparison.Ordinal);
        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AContainerPathNamedTwiceAnUnknownKeyAndNothingShippedAreRefused()
    {
        var twice = Valid.Replace(
            "\"/etc/dashboards\": \"monitoring/dashboards\"",
            "\"/etc/collector/config.yaml\": \"monitoring/dashboards\"",
            StringComparison.Ordinal
        );
        Assert.Contains(
            "duplicate files: container path \"/etc/collector/config.yaml\"",
            Assert.Throws<InvalidDataException>(() => Parse(twice)).Message,
            StringComparison.Ordinal
        );
        Assert.Throws<InvalidDataException>(() =>
            Parse(Valid.Replace("\"files\":", "\"args\": [], \"files\":", StringComparison.Ordinal))
        );
        Assert.Throws<InvalidDataException>(() =>
            Parse(Valid.Replace("\"monitoring/dashboards\"", "1", StringComparison.Ordinal))
        );
        Assert.Contains(
            "no such file beside the manifest",
            Assert.Throws<InvalidDataException>(() => Parse(Valid, ManifestFiles.None)).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void AContainersFilesAreMountedReadOnlyAfterTheCertificate()
    {
        var files = Path.Combine("/data-directory", "files");
        var spec = Assert.Single(StackSpec.Resolve(Parse(Valid), [], "/data-directory/certs", files));

        Assert.Equal(
            [
                new MountSpec(MountKind.Bind, "/data-directory/certs", "/certs", true),
                new MountSpec(
                    MountKind.Bind,
                    Path.Combine("/data-directory/certs", Topology.CertificateFileName),
                    Topology.LegacyCertificateFile,
                    true
                ),
                new MountSpec(
                    MountKind.Bind,
                    Path.Combine(files, "monitoring", "collector.yaml"),
                    "/etc/collector/config.yaml",
                    true
                ),
                new MountSpec(MountKind.Bind, Path.Combine(files, "monitoring", "dashboards"), "/etc/dashboards", true),
                new MountSpec(MountKind.Volume, StackSpec.VolumeName("collector-data"), "/data", false),
            ],
            spec.Mounts
        );
    }

    [Fact]
    public void EnsureWritesTheFilesReadableByEveryoneAndRemovesWhatNoLongerShips()
    {
        var directory = Path.Combine(Directory.CreateTempSubdirectory().FullName, "files");
        var stale = Path.Combine(directory, "monitoring", "old.yaml");
        var staleFolder = Path.Combine(directory, "grafana");
        var changed = Path.Combine(directory, "monitoring", "collector.yaml");
        foreach (var path in new[] { stale, Path.Combine(staleFolder, "dashboard.json"), changed })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "old");
        }
        using var before = new FileStream(changed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var files = Parse(Valid).Files;
        files.Ensure(directory);
        files.Ensure(directory);

        Assert.Equal("receivers: {}\n", File.ReadAllText(changed));
        Assert.Equal("{}\n", File.ReadAllText(Path.Combine(directory, "monitoring", "dashboards", "app.json")));
        Assert.False(File.Exists(stale));
        Assert.False(Directory.Exists(staleFolder));
        // Rewritten in place: a handle opened before reads the new content, as a running container's mount does.
        using (var reader = new StreamReader(before))
        {
            Assert.Equal("receivers: {}\n", reader.ReadToEnd());
        }
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode file =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            const UnixFileMode folder =
                file | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            Assert.Equal(file, File.GetUnixFileMode(changed));
            Assert.Equal(folder, File.GetUnixFileMode(directory));
            Assert.Equal(folder, File.GetUnixFileMode(Path.Combine(directory, "monitoring", "dashboards")));
        }
    }
}
