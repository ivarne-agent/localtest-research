using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The package ignores the manifest's <c>monitoring</c> group (studioctl's OpenTelemetry collector, its Victoria stores
/// and Grafana), since Aspire's dashboard does that job: it is not an optional group, <c>everything</c> leaves it out,
/// no typed method is required for it, and <c>WithGroup("monitoring")</c> refuses it.
/// </summary>
public sealed class MonitoringGroupTests
{
    private const string Collector = "otel-collector";

    private static readonly string[] _containers =
    [
        "victoria-metrics",
        "victoria-traces",
        "victoria-logs",
        Collector,
        "grafana",
    ];

    private static (IDistributedApplicationBuilder Builder, IResourceBuilder<AltinnLocaltestResource> Localtest) Build(
        IReadOnlyList<string>? groups = null
    )
    {
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var options = new AltinnLocaltestOptions { DataDirectory = SampleModel.DataDirectory };
        foreach (var group in groups ?? [])
        {
            options.Groups.Add(group);
        }
        var stack = new LocaltestStack(builder, LocaltestManifest.Default, options);
        var localtest = LocaltestPlatform.Add(
            builder,
            new AltinnLocaltestResource(AltinnLocaltestExtensions.DefaultName, stack),
            "AltinnLocaltest",
            options.Groups
        );
        return (builder, localtest);
    }

    [Fact]
    public void TheGroupIsNotOptionalAndEverythingLeavesItOut()
    {
        var (builder, localtest) = Build();

        Assert.Contains(LocaltestManifest.MonitoringGroup, localtest.Resource.Stack.Manifest.GroupNames);
        Assert.DoesNotContain(LocaltestManifest.MonitoringGroup, localtest.Resource.Stack.OptionalGroups);
        Assert.Equal(
            ["dialogporten", "receipt", "correspondence", "arbeidsflate"],
            localtest.Resource.Stack.OptionalGroups
        );

        localtest.WithEverything();

        Assert.Equal(["arbeidsflate", "correspondence", "dialogporten", "receipt"], localtest.Resource.Groups);
        Assert.Empty(builder.Resources.Select(r => r.Name).Intersect(_containers));
        Assert.Equal(_containers, LocaltestManifest.Default.Select([LocaltestManifest.MonitoringGroup]));
    }

    [Fact]
    public void WithGroupRefusesItPointingAtTheDashboard()
    {
        var (builder, localtest) = Build();

        var error = Assert.Throws<ArgumentException>(() => localtest.WithGroup(LocaltestManifest.MonitoringGroup));

        Assert.Contains("Aspire dashboard", error.Message, StringComparison.Ordinal);
        Assert.Contains("monitoring", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Collector, builder.Resources.Select(r => r.Name));
        Assert.Contains(
            "Aspire dashboard",
            Assert.Throws<ArgumentException>(() => Build(["monitoring"])).Message,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void TheMethodTestRequiresNoMethodForIt()
    {
        var required = GroupMethodsTests.RequiredGroups(LocaltestManifest.Default);

        Assert.DoesNotContain(LocaltestManifest.MonitoringGroup, required);
        Assert.Equal(["dialogporten", "receipt", "correspondence", "arbeidsflate"], required);
    }

    [Fact]
    public void ItsContainerWouldMountItsFilesReadOnly()
    {
        // The package never starts it, but the files mount as for any container.
        var spec = StackSpec
            .Resolve(LocaltestManifest.Default, [LocaltestManifest.MonitoringGroup], "/certs-dir", "/files-dir")
            .Single(s => s.Name == Collector);

        Assert.Contains(
            new MountSpec(
                MountKind.Bind,
                Path.Combine("/files-dir", "monitoring", "otel-collector.yaml"),
                "/etc/otelcol-contrib/config.yaml",
                true
            ),
            spec.Mounts
        );
    }
}
