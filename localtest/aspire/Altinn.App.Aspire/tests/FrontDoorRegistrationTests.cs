using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The app's registration with the front door against an in-process fake of the localtest container's registration
/// API: the <c>PUT</c> on the app's ready event and the <c>provision</c> command, the <c>DELETE</c> of
/// <c>deprovision</c>, the dev-server route, and a refusal or an unreachable front door as a degraded app.
/// </summary>
public sealed class FrontDoorRegistrationTests : IAsyncLifetime
{
    private readonly ConcurrentQueue<(string Method, string Path, string Body)> _requests = new();
    private WebApplication? _frontDoor;
    private int _status = StatusCodes.Status200OK;

    private Uri Platform() => new(_frontDoor!.Urls.Single() + "/");

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _frontDoor = builder.Build();
        _frontDoor.Run(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            _requests.Enqueue((context.Request.Method, context.Request.Path.Value!, await reader.ReadToEndAsync()));
            context.Response.StatusCode = _status;
            await context.Response.WriteAsync(
                _status == 200
                    ? "{\"provisioning\":{\"state\":\"succeeded\"}}"
                    : "{\n\"title\":\"Provisioning failed\"\n}"
            );
        });
        await _frontDoor.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_frontDoor is not null)
        {
            await _frontDoor.DisposeAsync();
        }
    }

    private static (SampleModel Model, IHost Built) Sample()
    {
        var model = SampleModel.Build();
        return (model, model.Builder.Build());
    }

    private AppProvisioning Provisioning(int appPort) => new(new AppId("ttd", "testapp"), Platform(), () => appPort);

    [Fact]
    public async Task ProvisionPutsTheAppsAddressAndDeprovisionDeletesIt()
    {
        var (model, built) = Sample();
        using var _ = built;
        var provisioning = Provisioning(14364);
        var cancellationToken = TestContext.Current.CancellationToken;

        var provisioned = await provisioning.ProvisionAsync(built.Services, model.App!.Resource, cancellationToken);
        var deprovisioned = await provisioning.DeprovisionAsync(built.Services, model.App.Resource, cancellationToken);

        Assert.Equal(
            [
                ("PUT", "/localtest/api/apps/ttd/testapp", """{"url":"http://host.docker.internal:14364"}"""),
                ("DELETE", "/localtest/api/apps/ttd/testapp", ""),
            ],
            _requests
        );
        Assert.Equal(ProvisioningState.Succeeded, provisioned.State);
        Assert.Contains(
            $"PUT {Platform()}localtest/api/apps/ttd/testapp answered 200: {{\"provisioning\":{{\"state\":\"succeeded\"}}}}",
            provisioned.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ProvisioningState.Succeeded, deprovisioned.State);
    }

    [Fact]
    public async Task ARefusalIsADegradedAppWithTheFrontDoorsAnswer()
    {
        var (model, built) = Sample();
        using var _ = built;
        _status = StatusCodes.Status502BadGateway;
        var provisioning = Provisioning(14364);

        var status = await provisioning.ProvisionAsync(
            built.Services,
            model.App!.Resource,
            TestContext.Current.CancellationToken
        );
        var health = await new ProvisioningHealthCheck(provisioning).CheckHealthAsync(
            new HealthCheckContext(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProvisioningState.Failed, status.State);
        Assert.Contains(
            "answered 502: { \"title\":\"Provisioning failed\" }",
            status.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(HealthStatus.Degraded, health.Status);
        Assert.Equal(status.Message, health.Description);
    }

    [Fact]
    public async Task AnUnreachableFrontDoorIsADegradedAppWithTheUrl()
    {
        var (model, built) = Sample();
        using var _ = built;
        var platform = Platform();
        await _frontDoor!.StopAsync(TestContext.Current.CancellationToken);
        var provisioning = new AppProvisioning(new AppId("ttd", "testapp"), platform, () => 14364);

        var status = await provisioning.ProvisionAsync(
            built.Services,
            model.App!.Resource,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(ProvisioningState.Failed, status.State);
        Assert.Contains(
            $"provision failed ({platform}localtest/api/apps/ttd/testapp)",
            status.Message,
            StringComparison.Ordinal
        );
        provisioning.Reset();
        Assert.Equal(ProvisioningState.NotRun, provisioning.Status.State);
    }

    [Fact]
    public async Task TheDevServerRouteIsAPutOfItsAddress()
    {
        var answer = await FrontDoorClient.PutAsync(
            Platform(),
            FrontDoorClient.DevServerPath,
            FrontDoorClient.HostAddress(8080),
            TestContext.Current.CancellationToken
        );

        Assert.True(answer.Succeeded);
        Assert.Equal(
            [("PUT", "/localtest/api/routes/frontend-dev-server", """{"url":"http://host.docker.internal:8080"}""")],
            _requests
        );
    }
}
