using System.Text;
using System.Text.Json.Nodes;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The package ignores the manifest's <c>monitoring</c> group (studioctl's OpenTelemetry collector and Grafana), since
/// Aspire's dashboard does that job: it is not an optional group, <c>everything</c> leaves it out, no typed method is
/// required for it, and <c>WithGroup("monitoring")</c> refuses it. The embedded manifest has no such group yet, so the
/// tests run on a copy of it with one.
/// </summary>
public sealed class MonitoringGroupTests
{
    private const string Collector = "otel-collector";

    /// <summary>The embedded manifest with a <c>monitoring</c> group of one container that mounts a file.</summary>
    private static LocaltestManifest WithMonitoring()
    {
        using var stream = typeof(LocaltestManifest).Assembly.GetManifestResourceStream(LocaltestManifest.Resource)!;
        var manifest = JsonNode.Parse(stream)!;
        manifest["groups"]!
            .AsArray()
            .Add(new JsonObject { ["name"] = "monitoring", ["containers"] = new JsonArray(Collector) });
        manifest["containers"]![Collector] = new JsonObject
        {
            ["registry"] = "docker.io/otel/opentelemetry-collector-contrib",
            ["tag"] = "0.161.0",
            ["port"] = 13133,
            ["files"] = new JsonObject { ["/etc/otelcol-contrib/config.yaml"] = "monitoring/otel-collector.yaml" },
        };
        return LocaltestManifest.Parse(
            Encoding.UTF8.GetBytes(manifest.ToJsonString()),
            ManifestFiles.From(
                new Dictionary<string, byte[]>(StringComparer.Ordinal)
                {
                    ["monitoring/otel-collector.yaml"] = Encoding.UTF8.GetBytes("receivers: {}\n"),
                }
            )
        );
    }

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
        var stack = new LocaltestStack(builder, WithMonitoring(), options);
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
        Assert.DoesNotContain(Collector, builder.Resources.Select(r => r.Name));
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
        var required = GroupMethodsTests.RequiredGroups(WithMonitoring());

        Assert.DoesNotContain(LocaltestManifest.MonitoringGroup, required);
        Assert.Equal(GroupMethodsTests.RequiredGroups(LocaltestManifest.Default), required);
    }

    [Fact]
    public void ItsContainerWouldMountItsFilesReadOnly()
    {
        // The package never starts it, but the files mount as for any container.
        var spec = StackSpec
            .Resolve(WithMonitoring(), [LocaltestManifest.MonitoringGroup], "/certs-dir", "/files-dir")
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
