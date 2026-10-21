using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace Altinn.App.Aspire.Tests;

/// <summary>
/// The sample's YARP routes, exactly as the model holds them, loaded into an in-process YARP (the library the front
/// door's image runs) in front of an echo server: proves the matching (host, longest prefix, the bare prefix, the
/// fall-through), the rewrite and the original Host without a container.
/// </summary>
public sealed class FrontDoorRoutingTests : IAsyncLifetime
{
    private const string ClusterPath = "/__cluster/";

    private WebApplication? _echo;
    private WebApplication? _proxy;
    private HttpClient? _client;

    public async ValueTask InitializeAsync()
    {
        _echo = Listen(WebApplication.CreateSlimBuilder());
        _echo.Run(context =>
            context.Response.WriteAsync(
                $"{context.Request.Path}{context.Request.QueryString}|{context.Request.Headers.Host}",
                context.RequestAborted
            )
        );
        await _echo.StartAsync();
        var echo = Address(_echo);

        var model = SampleModel.Build();
        var clusters = model
            .YarpClusterIds.Select(id => new ClusterConfig
            {
                ClusterId = id,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["echo"] = new() { Address = echo + ClusterPath + id },
                },
            })
            .ToList();
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddReverseProxy().LoadFromMemory(model.YarpRoutes, clusters);
        _proxy = Listen(builder);
        _proxy.MapReverseProxy();
        await _proxy.StartAsync();
        _client = new HttpClient { BaseAddress = new Uri(Address(_proxy)) };
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        if (_proxy is not null)
        {
            await _proxy.StopAsync();
            await _proxy.DisposeAsync();
        }
        if (_echo is not null)
        {
            await _echo.StopAsync();
            await _echo.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("local.altinn.cloud:8000", "/", "localtest-app", "/")]
    [InlineData("local.altinn.cloud:8000", "/Home/Localtest/Version", "localtest-app", "/Home/Localtest/Version")]
    [InlineData("local.altinn.cloud:8000", "/storage/api/v1/applications", "storage", "/storage/api/v1/applications")]
    [InlineData("local.altinn.cloud:8000", "/storage", "storage", "/storage")]
    [InlineData("local.altinn.cloud:8000", "/storage/", "storage", "/storage/")]
    [InlineData("local.altinn.cloud:8000", "/STORAGE/api", "storage", "/STORAGE/api")]
    [InlineData("local.altinn.cloud:8000", "/storagex", "localtest-app", "/storagex")]
    [InlineData(
        "local.altinn.cloud:8000",
        "/storage/dialogporten/api/localtest/enqueue",
        "dialogporten-adapter",
        "/storage/dialogporten/api/localtest/enqueue"
    )]
    [InlineData("local.altinn.cloud:8000", "/dialogporten/api/v1/x", "dialogporten-webapi", "/dialogporten/api/v1/x")]
    [InlineData("local.altinn.cloud:8000", "/dialogporten/graphql", "dialogporten-graphql", "/graphql")]
    [InlineData(
        "local.altinn.cloud:8000",
        "/dialogporten/graphql/stream?a=1",
        "dialogporten-graphql",
        "/graphql/stream?a=1"
    )]
    [InlineData("local.altinn.cloud:8000", "/dialogporten/graphqlx", "dialogporten-webapi", "/dialogporten/graphqlx")]
    [InlineData("local.altinn.cloud:8000", "/receipt/500000/abc", "localtest-app", "/receipt/500000/abc")]
    [InlineData("local.altinn.cloud:8000", "/correspondence/api/v1/x", "localtest-app", "/correspondence/api/v1/x")]
    [InlineData("localhost:8000", "/register/api/v1/parties", "register", "/register/api/v1/parties")]
    [InlineData("127.0.0.1:8000", "/", "localtest-app", "/")]
    [InlineData("pdf.local.altinn.cloud:8000", "/pdf", "pdf3", "/pdf")]
    [InlineData(
        "workflow-engine.local.altinn.cloud:8000",
        "/api/v1/health/ready",
        "workflow-engine",
        "/api/v1/health/ready"
    )]
    public async Task RoutesAsStudioctlServerDoes(string host, string path, string component, string upstreamPath)
    {
        var (cluster, forwardedPath, forwardedHost) = await Get(host, path);

        Assert.Equal("cluster_" + component, cluster);
        Assert.Equal(upstreamPath, forwardedPath);
        Assert.Equal(host, forwardedHost);
    }

    [Fact]
    public async Task AHostOfAGroupThatIsOffHasNoRoute()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "af.local.altinn.cloud:8000";
        using var response = await _client!.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<(string Cluster, string Path, string Host)> Get(string host, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        using var response = await _client!.SendAsync(request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var parts = body.Split('|');
        Assert.StartsWith(ClusterPath, parts[0], StringComparison.Ordinal);
        var rest = parts[0][ClusterPath.Length..];
        var slash = rest.IndexOfAny(['/', '?']);
        return slash < 0 ? (rest, "", parts[1]) : (rest[..slash], rest[slash..], parts[1]);
    }

    private static WebApplication Listen(WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        return builder.Build();
    }

    private static string Address(WebApplication app) =>
        app
            .Services.GetRequiredService<IServer>()
            .Features.GetRequiredFeature<IServerAddressesFeature>()
            .Addresses.First();
}
