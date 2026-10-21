using System.Net;
using System.Text;
using Altinn.Studio.EnvTopology;
using Altinn.Studio.StudioctlServer.Discovery;
using Altinn.Studio.StudioctlServer.Provisioning;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Studioctl.Tests.Provisioning;

public sealed class AppProvisionerTests
{
    [Fact]
    public void DialogportenRunsWhenTheRouteTableHasTheWebApiRoute()
    {
        var topology = Index(Route("localtest", "/"), Route("dialogporten-webapi", "/dialogporten"));

        Assert.True(AppProvisioner.DialogportenRunning(topology));
    }

    [Fact]
    public void DialogportenDoesNotRunWithoutTheGroupOrWithItsRouteDisabled()
    {
        Assert.False(AppProvisioner.DialogportenRunning(Index(Route("localtest", "/"))));
        Assert.False(
            AppProvisioner.DialogportenRunning(Index(Route("dialogporten-webapi", "/dialogporten", enabled: false)))
        );
        Assert.False(AppProvisioner.DialogportenRunning(new BoundTopologyIndex(null)));
    }

    [Fact]
    public async Task ProvisionsANewAppFromItsAddressOnceAndRemovesItWhenGone()
    {
        var handler = new RecordingHandler();
        var provisioner = Provisioner(handler, new TopologyMonitor(Topology(Route("localtest", "/"))));
        var app = App("ttd/frontend-test", 5005);

        await provisioner.Reconcile([app], TestContext.Current.CancellationToken);
        await provisioner.Reconcile([app], TestContext.Current.CancellationToken);
        await provisioner.Reconcile([], TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "GET http://127.0.0.1:5005/ttd/frontend-test/api/v1/applicationmetadata?checkOrgApp=false",
                "GET http://127.0.0.1:5005/ttd/frontend-test/api/v1/meta/authorizationpolicy",
                "GET http://127.0.0.1:5005/ttd/frontend-test/testData.json",
                "GET http://127.0.0.1:5005/ttd/frontend-test/api/v1/applicationlanguages",
                "GET http://local.altinn.cloud:8000/Home/GetTestStudioToken",
                "PUT http://local.altinn.cloud:8000/register/api/localtest/apps/ttd/frontend-test",
                "POST http://local.altinn.cloud:8000/storage/api/v1/applications?appId=ttd/frontend-test",
                "POST http://local.altinn.cloud:8000/authorization/api/v1/policies?org=ttd&app=frontend-test",
                "GET http://local.altinn.cloud:8000/resourceregistry/api/v1/resource/app_ttd_frontend-test/policy/subjects?reloadFromXacml=true",
                "POST http://local.altinn.cloud:8000/accessmanagement/api/localtest/refresh",
                "DELETE http://local.altinn.cloud:8000/register/api/localtest/apps/ttd/frontend-test",
                "POST http://local.altinn.cloud:8000/accessmanagement/api/localtest/refresh",
            ],
            handler.Calls
        );
    }

    [Fact]
    public async Task SyncsDialogportenOnceWhenTheGroupComesUpAfterTheApps()
    {
        var handler = new RecordingHandler();
        var topology = new TopologyMonitor(Topology(Route("localtest", "/")));
        var provisioner = Provisioner(handler, topology);
        var app = App("ttd/frontend-test", 5005);

        await provisioner.Reconcile([app], TestContext.Current.CancellationToken);
        Assert.DoesNotContain(handler.Calls, c => c.Contains("/dialogporten/", StringComparison.Ordinal));

        topology.Set(Topology(Route("localtest", "/"), Route("dialogporten-webapi", "/dialogporten")));
        await provisioner.Reconcile([app], TestContext.Current.CancellationToken);
        await provisioner.Reconcile([app], TestContext.Current.CancellationToken);

        Assert.Single(handler.Calls, c => c.EndsWith("/dialogporten/api/v1/localtest/sync", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DoesNothingWithoutTheProxy()
    {
        var handler = new RecordingHandler();
        var provisioner = new AppProvisioner(
            new AppProvisionerOptions(new Uri("http://local.altinn.cloud:8000/"), Enabled: false),
            new ClientFactory(handler),
            new BoundTopologyIndexAccessor(new TopologyMonitor(Topology()), new BoundTopologyOptions()),
            NullLogger<AppProvisioner>.Instance
        );

        await provisioner.Reconcile([App("ttd/frontend-test", 5005)], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Calls);
    }

    private static AppProvisioner Provisioner(RecordingHandler handler, TopologyMonitor topology) =>
        new(
            new AppProvisionerOptions(new Uri("http://local.altinn.cloud:8000/"), Enabled: true),
            new ClientFactory(handler),
            new BoundTopologyIndexAccessor(topology, new BoundTopologyOptions()),
            NullLogger<AppProvisioner>.Instance
        );

    private static DiscoveredApp App(string appId, int port) =>
        new(
            appId,
            AppEndpointUri.From(new Uri($"http://127.0.0.1:{port}/")),
            "process",
            ProcessId: null,
            Description: "test",
            ContainerId: null,
            Name: null,
            HostPort: port
        );

    private static BoundTopologyConfig Topology(params BoundTopologyRoute[] routes) =>
        new() { Version = 1, Routes = [.. routes] };

    /// <summary>The app answers its metadata and a policy, the platform the Studio token, the rest 404 or 200.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("no request uri");
            Calls.Add($"{request.Method} {uri}");
            var response = uri.Port switch
            {
                5005 when uri.AbsolutePath.EndsWith("/applicationmetadata", StringComparison.Ordinal) => Text("{}"),
                5005 when uri.AbsolutePath.EndsWith("/authorizationpolicy", StringComparison.Ordinal) => Text(
                    "<Policy/>"
                ),
                5005 => new HttpResponseMessage(HttpStatusCode.NotFound),
                _ when uri.AbsolutePath == "/Home/GetTestStudioToken" => Text("\"token\""),
                _ => new HttpResponseMessage(HttpStatusCode.OK),
            };
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Text(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8) };
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class TopologyMonitor(BoundTopologyConfig config) : IOptionsMonitor<BoundTopologyConfig>
    {
        private Action<BoundTopologyConfig, string?>? _listener;

        public BoundTopologyConfig CurrentValue => config;

        public BoundTopologyConfig Get(string? name) => config;

        public IDisposable? OnChange(Action<BoundTopologyConfig, string?> listener)
        {
            _listener = listener;
            return null;
        }

        public void Set(BoundTopologyConfig value)
        {
            config = value;
            _listener?.Invoke(value, BoundTopologyOptions.BoundName);
        }
    }

    private static BoundTopologyIndex Index(params BoundTopologyRoute[] routes) => new(Topology(routes));

    private static BoundTopologyRoute Route(string component, string prefix, bool enabled = true) =>
        new()
        {
            Component = component,
            Enabled = enabled,
            Destination = new BoundTopologyDestination
            {
                Kind = "http",
                Location = "host",
                Url = "http://127.0.0.1:8030",
            },
            Match = new BoundTopologyRouteMatch { Host = "local.altinn.cloud", PathPrefix = prefix },
        };
}
