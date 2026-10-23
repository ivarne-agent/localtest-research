using System.Globalization;
using System.Text.Json;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The package against studioctl's golden snapshots, read by relative path from the Go tree:
/// <c>internal/cmd/env/localtestv2/testdata/environment.golden.json</c> (the containers of <c>env up</c>, of
/// <c>env up --with everything --latest --dev-...</c> and of <c>env up --minimal</c>) and <c>internal/cmd/app/testdata/app-environment.golden.json</c>
/// (the app's environment). Per container, every field both runners have is compared byte for byte with the
/// application model the package builds: the selection and its start order, the image reference (the default stack;
/// the <c>everything</c> snapshot runs <c>latest</c> and dev builds), the environment with the <c>services__</c>
/// variables, the mounts (a named volume by its manifest id: each runner prefixes the id with its own prefix; a tmpfs
/// of minimal mode by its path and options, which the package passes as <c>--tmpfs path:options</c>), the
/// published port, the extra hosts, the network alias, the dependencies and the health target. Not compared, being one
/// runner's own: the network, the container names, the volume names, pull policies, SELinux relabelling, dev builds.
/// </summary>
public sealed class GoldenComparisonTests
{
    private const string DataDirectory = "/home/dev/.altinn-studio";

    private static readonly JsonElement _golden = Load("environment.golden.json");

    /// <summary>studioctl's volume prefix, as the snapshot states it.</summary>
    private static readonly string _studioctlVolumePrefix = _golden.GetProperty("volumePrefix").GetString()!;

    public static TheoryData<string> Selections => new() { "default", "everything", "minimal" };

    private static JsonElement Load(string file)
    {
        using var document = JsonDocument.Parse(
            File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "golden", file))
        );
        return document.RootElement.Clone();
    }

    private static SampleModel Model(string selection) =>
        selection switch
        {
            "minimal" => SampleModel.BuildMinimal(dataDirectory: DataDirectory),
            "everything" => SampleModel.Build(["everything"], dataDirectory: DataDirectory),
            _ => SampleModel.Build([], dataDirectory: DataDirectory),
        };

    /// <summary>The snapshot's container resources, by manifest name, in start order.</summary>
    private static List<(string Name, JsonElement Resource)> GoldenContainers(string selection) =>
        [
            .. _golden
                .GetProperty(selection)
                .GetProperty("resources")
                .EnumerateArray()
                .Where(r => r.GetProperty("id").GetString()!.StartsWith("container:", StringComparison.Ordinal))
                .Select(r => (r.GetProperty("id").GetString()!["container:localtest-v2-".Length..], r)),
        ];

    /// <summary>The volume id of a runner's volume name, which must carry the runner's prefix.</summary>
    private static string VolumeId(string name, string prefix)
    {
        Assert.StartsWith(prefix, name, StringComparison.Ordinal);
        return name[prefix.Length..];
    }

    /// <summary>The values that follow <paramref name="flag" /> in a container's runtime arguments, in order.</summary>
    private static List<string> Pairs(IReadOnlyList<string> args, string flag) =>
        [.. args.Select((arg, i) => (arg, i)).Where(a => a.arg == flag).Select(a => args[a.i + 1])];

    private static List<string> Strings(JsonElement resource, string property) =>
        resource.TryGetProperty(property, out var value) ? [.. value.EnumerateArray().Select(v => v.GetString()!)] : [];

    [Theory]
    [MemberData(nameof(Selections))]
    public void TheSelectionAndItsStartOrderMatch(string selection)
    {
        var model = Model(selection);
        var golden = GoldenContainers(selection).Select(c => c.Name).ToList();

        Assert.Equal(
            selection switch
            {
                "default" => 13,
                "everything" => 22,
                _ => 4,
            },
            golden.Count
        );
        Assert.Equal(golden, model.Platform.Resource.Stack.Specs().Select(s => s.Name));
        Assert.Equal(
            golden.Order(StringComparer.Ordinal),
            model.Containers.Select(c => c.Name).Order(StringComparer.Ordinal)
        );
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public async Task EveryContainerRunsAsStudioctlRunsIt(string selection)
    {
        var model = Model(selection);
        var specs = model.Platform.Resource.Stack.Specs().ToDictionary(s => s.Name);
        foreach (var (name, golden) in GoldenContainers(selection))
        {
            var container = model.Container(name);
            var runtimeArgs = await SampleModel.RuntimeArgs(container);

            if (selection != "everything")
            {
                Assert.Equal(golden.GetProperty("image").GetString(), "image:pulled:" + specs[name].Image.Reference);
            }
            var environment = await model.Environment(container);
            Assert.Equal(
                Strings(golden, "env").Order(StringComparer.Ordinal),
                environment.Select(kv => kv.Key + "=" + kv.Value).Order(StringComparer.Ordinal)
            );
            Assert.Equal(
                golden
                    .GetProperty("volumes")
                    .EnumerateArray()
                    .Select(v =>
                    {
                        var type = v.GetProperty("Type").GetString()!;
                        var source = v.GetProperty("HostPath").GetString()!;
                        return (
                            type,
                            type == "volume" ? VolumeId(source, _studioctlVolumePrefix) : source,
                            v.GetProperty("ContainerPath").GetString()!,
                            v.GetProperty("ReadOnly").GetBoolean(),
                            v.TryGetProperty("Options", out var options) ? options.GetString()! : ""
                        );
                    }),
                [
                    .. container
                        .Annotations.OfType<ContainerMountAnnotation>()
                        .Select(m =>
                            m.Type == ContainerMountType.Volume
                                ? ("volume", VolumeId(m.Source!, StackSpec.VolumePrefix), m.Target, m.IsReadOnly, "")
                                : ("bind", m.Source!, m.Target, m.IsReadOnly, "")
                        ),
                    .. Pairs(runtimeArgs, "--tmpfs")
                        .Select(t => t.Split(':', 2))
                        .Select(t => ("tmpfs", "", t[0], false, t.Length > 1 ? t[1] : "")),
                ]
            );
            Assert.Equal(
                golden.TryGetProperty("ports", out var ports)
                    ? ports
                        .EnumerateArray()
                        .Select(p =>
                            (p.GetProperty("HostPort").GetString(), p.GetProperty("ContainerPort").GetString())
                        )
                        .ToList()
                    : [],
                container
                    .Annotations.OfType<EndpointAnnotation>()
                    .Select(e =>
                        (
                            e.Port?.ToString(CultureInfo.InvariantCulture),
                            e.TargetPort?.ToString(CultureInfo.InvariantCulture)
                        )
                    )
                    .ToList()
            );
            Assert.Equal(Strings(golden, "extraHosts"), Pairs(runtimeArgs, "--add-host"));
            // Nothing else is passed to the runtime: the --tmpfs and --add-host pairs.
            Assert.Equal(
                2 * (Pairs(runtimeArgs, "--tmpfs").Count + Pairs(runtimeArgs, "--add-host").Count),
                runtimeArgs.Count
            );
            Assert.Equal(Strings(golden, "networkAliases"), [container.Name]);
            Assert.Equal(
                Strings(golden, "dependsOn").Select(d => d["container:localtest-v2-".Length..]),
                SampleModel.HealthyWaits(container)
            );
            Assert.True(golden.GetProperty("waitForReady").GetBoolean());
            Assert.True(golden.GetProperty("readinessProbe").GetBoolean());
            Assert.Single(container.Annotations.OfType<HealthCheckAnnotation>());
        }
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void TheHealthTargetsMatch(string selection)
    {
        var specs = Model(selection).Platform.Resource.Stack.Specs();

        Assert.Equal(
            Strings(_golden.GetProperty(selection), "healthTargets"),
            specs.Select(s => $"{s.Name} {s.Health}")
        );
        Assert.Equal(
            _golden.GetProperty(selection).GetProperty("frontDoorHealth").GetString(),
            new Uri(Topology.PlatformUri, specs.Single(s => s.Name == Topology.FrontDoor).Health.Path).ToString()
        );
    }

    [Fact]
    public void TheGroupsAndVolumesMatch()
    {
        var manifest = LocaltestManifest.Default;
        var groups = _golden.GetProperty("groups");

        Assert.Equal(manifest.GroupNames.Order(StringComparer.Ordinal), groups.EnumerateObject().Select(g => g.Name));
        foreach (var group in groups.EnumerateObject())
        {
            // The minimal group replaces the default one; the snapshot lists it alone.
            string[] selection =
                group.Name == LocaltestManifest.MinimalGroup
                    ? [LocaltestManifest.MinimalGroup]
                    : [LocaltestManifest.DefaultGroup, group.Name];
            Assert.Equal(group.Value.EnumerateArray().Select(c => c.GetString()), manifest.Select(selection));
        }
        Assert.Equal(
            Strings(_golden, "volumes").Select(name => VolumeId(name, _studioctlVolumePrefix)),
            manifest.VolumeIds
        );
    }

    /// <summary>
    /// Minimal mode announces at the front door exactly the services studioctl announces there, and mounts no volume.
    /// </summary>
    [Fact]
    public void MinimalModeEmulatesWhatStudioctlEmulatesAndMountsNoVolume()
    {
        var model = Model("minimal");
        const string frontDoor = "=http://localtest:8080";
        var golden = GoldenContainers("minimal")
            .SelectMany(c => Strings(c.Resource, "env"))
            .Where(e => e.EndsWith(frontDoor, StringComparison.Ordinal))
            .Select(e => e["services__".Length..e.IndexOf("__http__0", StringComparison.Ordinal)])
            .Where(name => name != Topology.FrontDoor)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        Assert.Equal(golden, model.Minimal.Resource.Emulated.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(
            model.Containers.SelectMany(c => c.Annotations.OfType<ContainerMountAnnotation>()),
            m => m.Type == ContainerMountType.Volume
        );
    }

    /// <summary>
    /// The package's volumes are its own: <c>altinn-localtest-&lt;id&gt;</c>, never studioctl's names, so a reset in
    /// one runtime leaves the other's data alone.
    /// </summary>
    [Fact]
    public void ThePackageNamesItsVolumesByItsOwnPrefix()
    {
        Assert.Equal("altinn-localtest-", StackSpec.VolumePrefix);
        Assert.Equal(AltinnLocaltestExtensions.DefaultName + "-", StackSpec.VolumePrefix);
        Assert.NotEqual(StackSpec.VolumePrefix, _studioctlVolumePrefix);
        var volumes = Model("everything")
            .Containers.SelectMany(c => c.Annotations.OfType<ContainerMountAnnotation>())
            .Where(m => m.Type == ContainerMountType.Volume)
            .Select(m => m.Source!)
            .Distinct()
            .Order(StringComparer.Ordinal);
        Assert.Equal(LocaltestManifest.Default.VolumeIds.Select(id => "altinn-localtest-" + id), volumes);
    }

    public static TheoryData<string> AppCases =>
        [
            .. Load("app-environment.golden.json")
                .GetProperty("cases")
                .EnumerateArray()
                .Select(c => c.GetProperty("name").GetString()!),
        ];

    /// <summary>Every case of studioctl's app environment snapshot, from the same inputs, byte for byte.</summary>
    [Theory]
    [MemberData(nameof(AppCases))]
    public void TheAppEnvironmentMatches(string name)
    {
        var golden = Load("app-environment.golden.json")
            .GetProperty("cases")
            .EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == name);
        var signing = golden.GetProperty("signing");
        var current = golden.GetProperty("current");

        var environment = AppEnvironment.Build(
            current.ValueKind == JsonValueKind.Null ? [] : [.. current.EnumerateArray().Select(e => e.GetString()!)],
            golden.GetProperty("kestrelUrl").GetString()!,
            int.Parse(golden.GetProperty("ingressPort").GetString()!, CultureInfo.InvariantCulture),
            golden.GetProperty("appFrontendAssetBaseUrl").GetString()!,
            golden.GetProperty("secretsDir").GetString()!,
            golden.GetProperty("keysDir").GetString()!,
            signing.GetProperty("FullPath").GetString() is { Length: > 0 } fullPath
                ? new AppCertificate(
                    fullPath,
                    signing.GetProperty("KeysFolder").GetString()!,
                    signing.GetProperty("FileName").GetString()!
                )
                : null
        );

        Assert.Equal(
            golden.GetProperty("environment").EnumerateObject().Select(p => (p.Name, p.Value.GetString())),
            environment.Select(kv => (kv.Key, (string?)kv.Value))
        );
    }
}
