using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The C# reader against studioctl's golden snapshot: for the default stack and for <c>--with everything</c>, every
/// component the package would add has the name, start order, image, environment, ports, mounts, aliases, extra hosts,
/// dependencies, command, container health check, health target and routes studioctl resolves. Excluded, because they
/// are studioctl's own: the dev builds' image references (the <c>everything</c> snapshot is <c>--dev all</c>), SELinux
/// relabelling of binds (<c>RelabelBinds</c>), the runtime user and user namespace (no component uses
/// <c>hostUser</c>, asserted below), the network resource and its name, the image pull policies, and the front door's
/// host routes (the Vite dev server, out of scope for the package).
/// </summary>
public sealed class GoldenComparisonTests
{
    private static readonly Golden _golden = Golden.Load();

    private static ManifestResolver Resolver() =>
        new(ManifestLoader.Default, Golden.CertificateDirectory, tag: null, getEnvironment: _ => null);

    public static TheoryData<string, string[]> Selections =>
        new() { { "default", [] }, { "everything", ["everything"] } };

    [Theory]
    [MemberData(nameof(Selections))]
    public void ComponentsMatchTheGoldenFile(string selection, string[] groups)
    {
        var resolver = Resolver();
        var components = resolver.Components(resolver.ResolveGroups(groups));
        var golden = _golden.Containers(selection);

        Assert.Equal(selection == "default" ? 13 : 22, golden.Count);
        Assert.Equal(golden.Select(g => g.Name), components.Select(c => c.Name));
        foreach (var (want, got) in golden.Zip(components))
        {
            if (!want.BuiltImage)
            {
                Assert.Equal(want.PulledImage, got.Image.Reference);
            }
            Assert.Equal(want.Environment, got.Environment);
            Assert.Equal(
                want.Ports,
                got.HostPort is { } port
                    ?
                    [
                        (
                            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            got.ContainerPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ),
                    ]
                    : []
            );
            Assert.Equal(
                want.Volumes,
                got.Mounts.Select(m => (m.IsVolume ? "volume" : "bind", m.Source, m.Target, m.ReadOnly)).ToList()
            );
            Assert.Equal(want.Command, got.Command);
            Assert.Equal(want.ExtraHosts, got.ExtraHosts);
            Assert.Equal(want.NetworkAliases, got.NetworkAliases);
            Assert.Equal(want.DependsOn, got.DependsOn);
            Assert.Equal(want.HealthCheckTest, got.ContainerHealthCheck);
            Assert.False(got.HostUser);
            Assert.Equal("", want.User);
            Assert.Equal("", want.UsernsMode);
        }
    }

    [Fact]
    public void TheDefaultImagesArePulledAtThePin()
    {
        var golden = _golden.Containers("default");
        Assert.All(golden, g => Assert.False(g.BuiltImage));
        Assert.Contains(
            Resolver().AllComponents(),
            c => c.Image.Reference.EndsWith(":" + ManifestLoader.Default.Images.Tag, StringComparison.Ordinal)
        );
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void HealthTargetsMatchTheGoldenFile(string selection, string[] groups)
    {
        var resolver = Resolver();
        var components = resolver.Components(resolver.ResolveGroups(groups));
        var targets = components
            .Where(c => c.HealthPath is not null && c.HostPort is not null)
            .Select(c => $"{c.Name} http://127.0.0.1:{c.HostPort}{c.HealthPath}")
            .Append(
                $"proxy {resolver.Topology.Url(LocaltestTopology.App)}{ManifestLoader.Default.FrontDoor.HealthPath}"
            );

        Assert.Equal(_golden.HealthTargets(selection), targets);
    }

    [Theory]
    [MemberData(nameof(Selections))]
    public void RoutesMatchTheGoldenFile(string selection, string[] groups)
    {
        var resolver = Resolver();
        var added = resolver.Components(resolver.ResolveGroups(groups)).Select(c => c.Name).ToHashSet();
        var hostRoutes = ManifestLoader.Default.FrontDoor.HostRoutes.Select(r => r.Name).ToHashSet();
        var golden = _golden.Routes(selection).Where(r => !hostRoutes.Contains(r.Component)).ToList();
        var routes = resolver
            .AllComponents()
            .SelectMany(c => c.Routes)
            .Select(r => new GoldenRoute(r.Component, r.Host, r.Prefix, r.Rewrite, added.Contains(r.Component)))
            .ToList();

        // studioctl writes every route and disables those of groups that are off; the package adds only the enabled ones.
        Assert.Equal(golden, routes);
    }

    [Fact]
    public void GroupsAndVolumesMatchTheGoldenFile()
    {
        var resolver = Resolver();
        foreach (var group in _golden.Root.GetProperty("groups").EnumerateObject())
        {
            var implies = group.Value.GetProperty("implies");
            IReadOnlyList<string> want =
                implies.ValueKind == System.Text.Json.JsonValueKind.Null
                    ? []
                    : [.. implies.EnumerateArray().Select(i => i.GetString()!)];
            Assert.Equal(want, resolver.Implies(group.Name));
        }
        Assert.Equal(
            _golden.Root.GetProperty("volumes").EnumerateArray().Select(v => v.GetString()),
            ManifestLoader.Default.Volumes.Select(v => v.Name)
        );
    }
}
