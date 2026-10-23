using System.Collections.Concurrent;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// Two apps in one AppHost, as the sample adds them (<c>AddAltinnApp&lt;Projects.App&gt;(localtest)</c> and
/// <c>AddAltinnApp&lt;Projects.App2&gt;(localtest, "app2")</c>): each has its own resource, endpoint, health checks,
/// secrets directory, registration and commands, and the commands of one do not touch the other.
/// </summary>
public sealed class MultipleAppsTests : IAsyncLifetime
{
    private readonly ConcurrentQueue<(string Method, string Path, string Body)> _requests = new();
    private WebApplication? _frontDoor;

    private Uri Platform() => new(_frontDoor!.Urls.Single() + "/");

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _frontDoor = builder.Build();
        _frontDoor.Run(async context =>
        {
            if (context.Request.Path.StartsWithSegments("/localtest/api", StringComparison.Ordinal))
            {
                using var reader = new StreamReader(context.Request.Body);
                _requests.Enqueue((context.Request.Method, context.Request.Path.Value!, await reader.ReadToEndAsync()));
            }
            await context.Response.WriteAsync("{\"provisioning\":{\"state\":\"succeeded\"}}");
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

    private static (SampleModel Model, ProjectResource App, ProjectResource App2) TwoApps()
    {
        var model = SampleModel.Build(["dialogporten"], withApp: true);
        var app2 = model.Builder.AddAltinnApp<SampleApp2Project>(model.Localtest, "app2");
        return (model, model.App!.Resource, app2.Resource);
    }

    private static AppProvisioning ProvisioningOf(IResource app) =>
        Assert.Single(app.Annotations.OfType<AppProvisioningAnnotation>()).Provisioning;

    [Fact]
    public async Task EachAppIsItsOwnResourceWithItsOwnEndpointAndHealth()
    {
        var (model, app, app2) = TwoApps();

        Assert.Equal(
            ["app", "app-rebuilder", "app2", "app2-rebuilder"],
            model.Resources.Select(r => r.Name).Where(n => n.StartsWith("app", StringComparison.Ordinal))
        );
        foreach (var resource in new[] { app, app2 })
        {
            var endpoint = Assert.Single(resource.Annotations.OfType<EndpointAnnotation>());
            Assert.Equal(
                (AltinnAppExtensions.HttpEndpoint, false, "0.0.0.0", (int?)null),
                (endpoint.Name, endpoint.IsProxied, endpoint.TargetHost, endpoint.Port)
            );
            Assert.Equal(
                $"http://0.0.0.0:{{{resource.Name}.bindings.http.targetPort}}",
                await model.EnvironmentExpression(resource, "Kestrel__EndPoints__Http__Url")
            );
            Assert.Contains(
                resource.Annotations.OfType<WaitAnnotation>(),
                w => w.Resource == model.Localtest.Resource && w.WaitType == WaitType.WaitUntilHealthy
            );
        }
        Assert.Contains("app_provisioning", app.Annotations.OfType<HealthCheckAnnotation>().Select(h => h.Key));
        Assert.Contains("app2_provisioning", app2.Annotations.OfType<HealthCheckAnnotation>().Select(h => h.Key));
        Assert.DoesNotContain("app_provisioning", app2.Annotations.OfType<HealthCheckAnnotation>().Select(h => h.Key));
        Assert.Equal(new AppId("ttd", "testapp"), ProvisioningOf(app).AppId);
        Assert.Equal(new AppId("ttd", "testapp2"), ProvisioningOf(app2).AppId);
        Assert.NotSame(ProvisioningOf(app), ProvisioningOf(app2));
        Assert.Contains(
            "ttd/testapp2's",
            Assert
                .Single(
                    app2.Annotations.OfType<ResourceCommandAnnotation>(),
                    c => c.Name == AltinnAppExtensions.DeprovisionCommand
                )
                .ConfirmationMessage,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task EachAppHasItsOwnSecretsDirectoryKeyedOnItsAppId()
    {
        var (model, app, app2) = TwoApps();

        Assert.Equal(
            "/data-directory/apps/ttd/testapp/secrets",
            (await model.Environment(app))["RUNTIME_APP_SECRETS_DIR"]
        );
        Assert.Equal(
            "/data-directory/apps/ttd/testapp2/secrets",
            (await model.Environment(app2))["RUNTIME_APP_SECRETS_DIR"]
        );
    }

    [Fact]
    public async Task EachAppRegistersItselfAndItsCommandsTouchOnlyIt()
    {
        var (model, app, app2) = TwoApps();
        Allocate(app, 14364);
        Allocate(app2, 14365);
        ProvisioningOf(app).PlatformUri = Platform();
        ProvisioningOf(app2).PlatformUri = Platform();
        using var built = model.Builder.Build();
        var services = built.Services;
        var cancellationToken = TestContext.Current.CancellationToken;

        // Both ready: two registrations, each with its own path and port.
        await model.Builder.Eventing.PublishAsync(new ResourceReadyEvent(app, services), cancellationToken);
        await model.Builder.Eventing.PublishAsync(new ResourceReadyEvent(app2, services), cancellationToken);
        Assert.Equal(
            [
                ("PUT", "/localtest/api/apps/ttd/testapp", """{"url":"http://host.docker.internal:14364"}"""),
                ("PUT", "/localtest/api/apps/ttd/testapp2", """{"url":"http://host.docker.internal:14365"}"""),
            ],
            _requests
        );
        var appStatus = ProvisioningOf(app).Status;
        Assert.Equal(ProvisioningState.Succeeded, appStatus.State);

        // app2's deprovision and provision: app2's registration only, and app's status stays as it was.
        _requests.Clear();
        Assert.True((await Run(app2, AltinnAppExtensions.DeprovisionCommand, services)).Success);
        Assert.True((await Run(app2, AltinnAppExtensions.ProvisionCommand, services)).Success);
        Assert.Equal(
            [
                ("DELETE", "/localtest/api/apps/ttd/testapp2", ""),
                ("PUT", "/localtest/api/apps/ttd/testapp2", """{"url":"http://host.docker.internal:14365"}"""),
            ],
            _requests
        );
        Assert.Same(appStatus, ProvisioningOf(app).Status);

        // And the other way round.
        _requests.Clear();
        Assert.True((await Run(app, AltinnAppExtensions.DeprovisionCommand, services)).Success);
        Assert.Equal([("DELETE", "/localtest/api/apps/ttd/testapp", "")], _requests);
    }

    [Fact]
    public void TwoAppsWithOneNameAreRefused()
    {
        var model = SampleModel.Build(["dialogporten"], withApp: true);

        var error = Assert.Throws<DistributedApplicationException>(() =>
            model.Builder.AddAltinnApp<SampleApp2Project>(model.Localtest)
        );

        Assert.Contains("'app'", error.Message, StringComparison.Ordinal);
    }

    private static void Allocate(ProjectResource app, int port)
    {
        var endpoint = Assert.Single(app.Annotations.OfType<EndpointAnnotation>());
        endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "localhost", port);
    }

    private static Task<ExecuteCommandResult> Run(ProjectResource app, string command, IServiceProvider services) =>
        Assert
            .Single(app.Annotations.OfType<ResourceCommandAnnotation>(), c => c.Name == command)
            .ExecuteCommand(
                new ExecuteCommandContext
                {
                    Services = services,
                    ResourceName = app.Name,
                    CancellationToken = TestContext.Current.CancellationToken,
                    Logger = NullLogger.Instance,
                    Arguments = new InteractionInputCollection([]),
                }
            );
}
