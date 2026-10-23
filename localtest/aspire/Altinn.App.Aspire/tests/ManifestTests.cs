using System.Text;
using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

/// <summary>The manifest reader: the embedded file, the defaults, the start order and the Go loader's validation.</summary>
public sealed class ManifestTests
{
    private const string Valid = """
        {
          "groups": [
            { "name": "default", "containers": ["web"] },
            { "name": "extra", "containers": ["worker"] }
          ],
          "containers": {
            "db": { "registry": "example.org/db", "tag": "1", "port": 5432, "health": "tcp",
                    "volumes": { "db-data": "/data" } },
            "web": { "registry": "example.org/web", "tag": "sha-1", "envs": { "A": "b" }, "dependsOn": ["db"] },
            "worker": { "registry": "example.org/worker", "tag": "2", "port": 81, "health": "/ready",
                        "dependsOn": ["web", "db"] }
          }
        }
        """;

    private static LocaltestManifest Parse(string json) => LocaltestManifest.Parse(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void TheEmbeddedManifestIsStudioctls()
    {
        var manifest = LocaltestManifest.Default;

        Assert.Equal(
            ["default", "minimal", "dialogporten", "receipt", "correspondence", "arbeidsflate"],
            manifest.GroupNames
        );
        Assert.Equal(22, manifest.Names.Count);
        Assert.Equal("postgres", manifest.Names[0]);
        var localtest = manifest.Containers["localtest"];
        Assert.Equal((8080, "/health", true), (localtest.HttpPort, localtest.HealthCheck, localtest.IsHttp));
        Assert.False(manifest.Containers["redis"].IsHttp);
        Assert.Equal(
            "ghcr.io/ivarne-agent/localtest-research/localtest-storage:" + manifest.Containers["storage"].Tag,
            manifest.Containers["storage"].Image
        );
    }

    [Fact]
    public void DefaultsAndStartOrder()
    {
        var manifest = Parse(Valid);

        Assert.Equal(["db", "web", "worker"], manifest.Names);
        Assert.Equal((8080, "/health"), (manifest.Containers["web"].HttpPort, manifest.Containers["web"].HealthCheck));
        Assert.Equal(["db", "web"], manifest.Select(["default"]));
        Assert.Equal(["db", "web", "worker"], manifest.Select(["default", "extra", "unknown"]));
        Assert.Equal(["db-data"], manifest.VolumeIds);
    }

    public static TheoryData<string, string, string> Faults =>
        new()
        {
            { "unknown key routes", "\"groups\": [", "\"routes\": {}, \"groups\": [" },
            { "hostPort", "\"tag\": \"1\",", "\"tag\": \"1\", \"hostPort\": 1," },
            { "implies", "{ \"name\": \"extra\",", "{ \"name\": \"extra\", \"implies\": [], " },
            { "unknown container", "\"dependsOn\": [\"db\"] }", "\"dependsOn\": [\"dbx\"] }" },
            { "unknown container", "\"containers\": [\"worker\"]", "\"containers\": [\"workerx\"]" },
            { "unknown container", "\"dependsOn\": [\"db\"] }", "\"dependsOn\": [\"web\"] }" },
            { "duplicate", "\"dependsOn\": [\"web\", \"db\"]", "\"dependsOn\": [\"web\", \"web\"]" },
            {
                "dependency cycle among db, web",
                "\"health\": \"tcp\",",
                "\"health\": \"tcp\", \"dependsOn\": [\"web\"],"
            },
            { "container no group reaches: worker", "\"containers\": [\"worker\"]", "\"containers\": [\"db\"]" },
            { "registry", "\"registry\": \"example.org/db\"", "\"registry\": \"\"" },
            { "tag", "\"tag\": \"1\"", "\"tag\": \" \"" },
            { "health \"ready\"", "\"health\": \"/ready\"", "\"health\": \"ready\"" },
            { "port 70000", "\"port\": 81", "\"port\": 70000" },
            { "no default group", "\"name\": \"default\"", "\"name\": \"base\"" },
            { "envs key", "\"envs\": { \"A\": \"b\" }", "\"envs\": { \"A=\": \"b\" }" },
            { "volume", "\"/data\"", "\"data\"" },
            { "volume \"Db_data\"", "\"db-data\":", "\"Db_data\":" },
            { "volume \"db--data\"", "\"db-data\":", "\"db--data\":" },
            { "duplicate group extra", "\"name\": \"default\"", "\"name\": \"extra\"" },
        };

    [Theory]
    [MemberData(nameof(Faults))]
    public void AnInvalidManifestIsRefused(string fault, string find, string replace)
    {
        Assert.True(Valid.Contains(find, StringComparison.Ordinal), find);
        var invalid = Valid.Replace(find, replace, StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() => Parse(invalid));

        Assert.Contains(fault, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateContainerAndTrailingDataAreRefused()
    {
        Assert.Contains(
            "duplicate container db",
            Assert
                .Throws<InvalidDataException>(() =>
                    Parse(Valid.Replace("\"web\": {", "\"db\": {}, \"web\": {", StringComparison.Ordinal))
                )
                .Message,
            StringComparison.Ordinal
        );
        Assert.Throws<InvalidDataException>(() => Parse(Valid + "{}"));
        Assert.Throws<InvalidDataException>(() => Parse("""{ "groups": [] }"""));
    }
}
