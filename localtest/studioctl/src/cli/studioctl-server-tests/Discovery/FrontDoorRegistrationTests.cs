using System.Net;
using Altinn.Studio.StudioctlServer.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Studioctl.Tests.Discovery;

public sealed class FrontDoorRegistrationTests
{
    [Fact]
    public async Task RegistersANewAppOnceAtItsHostGatewayAddressAndRemovesItWhenGone()
    {
        var handler = new RecordingHandler();
        var registration = Registration(handler, enabled: true);
        var app = App("ttd/frontend-test", 5005);

        await registration.Reconcile([app], TestContext.Current.CancellationToken);
        await registration.Reconcile([app], TestContext.Current.CancellationToken);
        await registration.Reconcile([App("ttd/frontend-test", 5006)], TestContext.Current.CancellationToken);
        await registration.Reconcile([], TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                "PUT http://local.altinn.cloud:8000/localtest/api/apps/ttd/frontend-test {\"url\":\"http://host.docker.internal:5005\"}",
                "PUT http://local.altinn.cloud:8000/localtest/api/apps/ttd/frontend-test {\"url\":\"http://host.docker.internal:5006\"}",
                "DELETE http://local.altinn.cloud:8000/localtest/api/apps/ttd/frontend-test ",
            ],
            handler.Calls
        );
    }

    [Fact]
    public async Task RetriesAFailedRegistrationOnTheNextRefresh()
    {
        var handler = new RecordingHandler { Status = HttpStatusCode.BadGateway };
        var registration = Registration(handler, enabled: true);
        var app = App("ttd/frontend-test", 5005);

        await registration.Reconcile([app], TestContext.Current.CancellationToken);
        handler.Status = HttpStatusCode.OK;
        await registration.Reconcile([app], TestContext.Current.CancellationToken);
        await registration.Reconcile([app], TestContext.Current.CancellationToken);

        Assert.Equal(2, handler.Calls.Count);
    }

    [Fact]
    public async Task DoesNothingWithoutLocaltestV2()
    {
        var handler = new RecordingHandler();

        await Registration(handler, enabled: false)
            .Reconcile([App("ttd/frontend-test", 5005)], TestContext.Current.CancellationToken);

        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void IsOnOnlyWithTheSettingAndThePublicOrigin()
    {
        static FrontDoorRegistrationOptions Options(string? registerApps, string? url) =>
            FrontDoorRegistrationOptions.FromConfiguration(
                new ConfigurationBuilder()
                    .AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Localtest:RegisterApps"] = registerApps,
                            ["Localtest:Url"] = url,
                        }
                    )
                    .Build()
            );

        Assert.True(Options("true", "http://local.altinn.cloud:8000/").Enabled);
        Assert.False(Options(null, "http://local.altinn.cloud:8000/").Enabled);
        Assert.False(Options("true", null).Enabled);
    }

    private static FrontDoorRegistration Registration(RecordingHandler handler, bool enabled) =>
        new(
            new FrontDoorRegistrationOptions(new Uri("http://local.altinn.cloud:8000/"), enabled),
            new ClientFactory(handler),
            NullLogger<FrontDoorRegistration>.Instance
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

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add($"{request.Method} {request.RequestUri} {body}");
            return new HttpResponseMessage(Status) { Content = new StringContent("{}") };
        }
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
