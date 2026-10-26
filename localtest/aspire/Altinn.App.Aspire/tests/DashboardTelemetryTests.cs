using System.Net.Sockets;
using System.Text;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// Every localtest container sends its telemetry to the Aspire dashboard: Aspire's <c>WithOtlpExporter()</c>
/// variables, the manifest name as <c>OTEL_SERVICE_NAME</c> and studioctl's 5 s metric interval, the manifest's own
/// envs over all of them; with the container tunnel off, the dashboard's OTLP endpoints listen on every interface.
/// </summary>
public sealed class DashboardTelemetryTests
{
    public static TheoryData<string> Modes => new() { "everything", "minimal" };

    private static SampleModel Model(string mode) =>
        mode == "minimal" ? SampleModel.BuildMinimal() : SampleModel.Build(["everything"]);

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task EveryContainerExportsUnderItsManifestName(string mode)
    {
        var model = Model(mode);

        Assert.Equal(mode == "minimal" ? 4 : 22, model.Containers.Count);
        foreach (var container in model.Containers)
        {
            var environment = await model.RawEnvironment(container);

            // Without a dashboard in the model Aspire falls back to its default address; with one, see below.
            Assert.Equal(
                "http://localhost:18889",
                Assert.IsType<HostUrl>(environment["OTEL_EXPORTER_OTLP_ENDPOINT"]).Url
            );
            Assert.Equal("grpc", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
            Assert.Equal(container.Name, environment["OTEL_SERVICE_NAME"]);
            Assert.Equal("5000", environment["OTEL_METRIC_EXPORT_INTERVAL"]);
            Assert.Single(container.Annotations.OfType<OtlpExporterAnnotation>());
        }
    }

    [Fact]
    public async Task TheEndpointIsTheDashboardsOtlpEndpointOnTheContainersNetwork()
    {
        var model = SampleModel.Build([]);
        var dashboard = new ExecutableResource(DashboardTraceLinks.DashboardResource, "dashboard", "/");
        dashboard.Annotations.Add(
            new EndpointAnnotation(ProtocolType.Tcp, "http", name: "otlp-grpc", port: 18889, isProxied: true)
        );
        model.Builder.Resources.Add(dashboard);
        model.Builder.Configuration["AppHost:OtlpApiKey"] = "key";
        // Aspire reads the dashboard from the built model's services.
        using var app = model.Builder.Build();

        var environment = await model.RawEnvironment(model.Container("storage"));

        var endpoint = Assert.IsType<EndpointReference>(environment["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        Assert.Same(dashboard, endpoint.Resource);
        Assert.Equal("otlp-grpc", endpoint.EndpointName);
        // No network baked in: it resolves on the caller's, the container network for a container.
        Assert.Null(endpoint.ContextNetworkID);
        Assert.Equal("grpc", environment["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.Equal("x-otlp-api-key=key", environment["OTEL_EXPORTER_OTLP_HEADERS"]);
        Assert.Equal("storage", environment["OTEL_SERVICE_NAME"]);
        Assert.Equal("5000", environment["OTEL_METRIC_EXPORT_INTERVAL"]);
    }

    [Fact]
    public async Task TheManifestsOwnEnvsWin()
    {
        const string storageEnv = "\"GeneralSettings__PlatformEndpoint\": \"{{origin}}/\",";
        var json = Encoding.UTF8.GetString(ManifestBytes());
        Assert.Contains(storageEnv, json, StringComparison.Ordinal);
        var manifest = LocaltestManifest.Parse(
            Encoding.UTF8.GetBytes(
                json.Replace(
                    storageEnv,
                    storageEnv
                        + "\"OTEL_SERVICE_NAME\": \"platform-storage\", \"OTEL_METRIC_EXPORT_INTERVAL\": \"60000\", "
                        + "\"OTEL_EXPORTER_OTLP_PROTOCOL\": \"http/protobuf\",",
                    StringComparison.Ordinal
                )
            ),
            ManifestFiles.Embedded
        );
        var builder = DistributedApplication.CreateBuilder(
            new DistributedApplicationOptions { Args = [], DisableDashboard = true }
        );
        var options = new AltinnLocaltestOptions { DataDirectory = SampleModel.DataDirectory };
        var stack = new LocaltestStack(builder, manifest, options);
        LocaltestPlatform.Add(builder, new AltinnLocaltestResource("altinn-localtest", stack), "AltinnLocaltest", []);

        var storage = builder.Resources.OfType<ContainerResource>().Single(r => r.Name == "storage");
        var values = new Dictionary<string, object>();
        foreach (var annotation in storage.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(new EnvironmentCallbackContext(builder.ExecutionContext, storage, values));
        }

        Assert.Equal("platform-storage", values["OTEL_SERVICE_NAME"]);
        Assert.Equal("60000", values["OTEL_METRIC_EXPORT_INTERVAL"]);
        Assert.Equal("http/protobuf", values["OTEL_EXPORTER_OTLP_PROTOCOL"]);
        Assert.IsType<HostUrl>(values["OTEL_EXPORTER_OTLP_ENDPOINT"]);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("true", false)]
    [InlineData("false", true)]
    public void TheOtlpEndpointsGoHostWideOnlyWithoutTheTunnel(string? tunnel, bool hostWide)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ASPIRE_ENABLE_CONTAINER_TUNNEL"] = tunnel })
            .Build();

        Assert.Equal(hostWide, DashboardTelemetry.NeedsHostWideOtlp(configuration));
    }

    [Fact]
    public void HostWideMakesTheDashboardsOtlpEndpointsUnproxiedOnEveryInterface()
    {
        var dashboard = new ExecutableResource(DashboardTraceLinks.DashboardResource, "dashboard", "/");
        var browser = new EndpointAnnotation(ProtocolType.Tcp, "http", name: "http", port: 18888, isProxied: true);
        var grpc = new EndpointAnnotation(ProtocolType.Tcp, "http", name: "otlp-grpc", port: 18889, isProxied: true);
        var http = new EndpointAnnotation(ProtocolType.Tcp, "https", name: "otlp-http", port: 18890, isProxied: true);
        dashboard.Annotations.Add(browser);
        dashboard.Annotations.Add(grpc);
        dashboard.Annotations.Add(http);

        DashboardTelemetry.HostWideOtlp.Configure([new ContainerResource("other"), dashboard]);

        foreach (var (endpoint, port) in new[] { (grpc, 18889), (http, 18890) })
        {
            Assert.False(endpoint.IsProxied);
            Assert.Equal("0.0.0.0", endpoint.TargetHost);
            // The dashboard's own Kestrel listens on the configured port.
            Assert.Equal(port, endpoint.Port);
            Assert.Equal(port, endpoint.TargetPort);
        }
        Assert.True(browser.IsProxied);
        Assert.Equal("localhost", browser.TargetHost);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public void BothModesRegisterTheSubscriber(string mode)
    {
        var model = Model(mode);

        Assert.Single(
            model.Builder.Services,
            d =>
                d.ServiceType == typeof(IDistributedApplicationEventingSubscriber)
                && d.ImplementationType == typeof(DashboardTelemetry.HostWideOtlp)
        );
    }

    private static byte[] ManifestBytes()
    {
        using var stream = typeof(LocaltestManifest).Assembly.GetManifestResourceStream(LocaltestManifest.Resource)!;
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
