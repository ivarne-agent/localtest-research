using System.Net.Sockets;
using Aspire.Hosting.ApplicationModel;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The workflow engine's dashboard links a workflow's trace to the Aspire dashboard's trace page
/// (<c>EngineSettings__Dashboard__TraceUrlTemplate</c>), on the dashboard's browser address. The dashboard resource is
/// built here as Aspire 13.6 builds it before the start (<c>DashboardEventHandlers</c>): an executable named
/// <c>aspire-dashboard</c> with an <c>https</c> or <c>http</c> endpoint; the tests' models run no dashboard.
/// </summary>
public sealed class DashboardTraceLinksTests
{
    private const string Variable = "EngineSettings__Dashboard__TraceUrlTemplate";

    private static void AddDashboard(SampleModel model, params (string Scheme, int Port)[] endpoints)
    {
        var dashboard = new ExecutableResource("aspire-dashboard", "dashboard", "/");
        foreach (var (scheme, port) in endpoints)
        {
            var endpoint = new EndpointAnnotation(ProtocolType.Tcp, scheme, name: scheme, port: port, isProxied: true);
            endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port);
            dashboard.Annotations.Add(endpoint);
        }
        model.Builder.Resources.Add(dashboard);
    }

    private static async Task<string?> TemplateOf(SampleModel model, string container)
    {
        var resource = model.Container(container);
        var expression = await model.EnvironmentExpressionValue(resource, Variable);
        if (expression is null)
        {
            return null;
        }
        // As the container's environment is resolved: for the container, on its network.
        return await expression.GetValueAsync(
            new ValueProviderContext { Caller = resource, Network = new NetworkIdentifier("aspire-container-network") },
            TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public async Task TheEngineLinksTracesToTheDashboardsHttpsAddress()
    {
        var model = SampleModel.Build([]);
        AddDashboard(model, ("https", 17043), ("http", 15043));

        Assert.Equal("https://localhost:17043/traces/detail/{traceId}", await TemplateOf(model, "workflow-engine"));
        foreach (var container in model.Containers.Where(c => c.Name != "workflow-engine"))
        {
            Assert.Null(await model.EnvironmentExpressionValue(container, Variable));
        }
    }

    [Fact]
    public async Task AnUnsecuredDashboardIsLinkedOverHttp()
    {
        var model = SampleModel.Build([]);
        AddDashboard(model, ("http", 15043));

        Assert.Equal("http://localhost:15043/traces/detail/{traceId}", await TemplateOf(model, "workflow-engine"));
    }

    [Fact]
    public async Task WithoutADashboardTheEngineGetsNoLink()
    {
        var model = SampleModel.Build([]);

        Assert.Null(await model.EnvironmentExpressionValue(model.Container("workflow-engine"), Variable));
        Assert.DoesNotContain(Variable, (await model.Environment(model.Container("workflow-engine"))).Keys);
    }

    /// <summary>The Grafana settings are studioctl's runner data while its monitoring group runs, not the manifest's.</summary>
    [Fact]
    public async Task TheEngineGetsNoGrafanaSettings()
    {
        var model = SampleModel.Build(["everything"]);
        AddDashboard(model, ("https", 17043));

        var environment = await model.Environment(model.Container("workflow-engine"));
        Assert.DoesNotContain("EngineSettings__Dashboard__GrafanaUrl", environment.Keys);
        Assert.DoesNotContain("EngineSettings__Dashboard__GrafanaTracesDatasourceUid", environment.Keys);
    }
}
