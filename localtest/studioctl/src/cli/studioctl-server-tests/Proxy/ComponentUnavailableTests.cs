using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Altinn.Studio.EnvTopology;
using Altinn.Studio.StudioctlServer.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Studioctl.Tests.Proxy;

public sealed class ComponentUnavailableTests
{
    private const string Host = "local.altinn.cloud";
    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    [Fact]
    public async Task AComponentOfAGroupThatIsOffAnswers503ProblemDetails()
    {
        var context = await Send(
            "/dialogporten/api/v1/serviceowner/dialogs",
            accept: "application/json",
            Route(
                "dialogporten-webapi",
                "/dialogporten",
                "http://127.0.0.1:8030",
                enabled: false,
                group: "dialogporten"
            )
        );

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(Body(context));
        var root = problem.RootElement;
        Assert.Equal("Component not running", root.GetProperty("title").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("type").GetString()));
        Assert.Equal("/dialogporten/api/v1/serviceowner/dialogs", root.GetProperty("instance").GetString());
        Assert.Equal("dialogporten-webapi", root.GetProperty("component").GetString());
        Assert.Equal("dialogporten", root.GetProperty("group").GetString());
        var detail = root.GetProperty("detail").GetString();
        Assert.Contains("studioctl env up -r localtest-v2 --with dialogporten", detail, StringComparison.Ordinal);
        Assert.Contains("--with everything", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AComponentOfAGroupThatIsOffAnswersABrowserWithAPage()
    {
        var context = await Send(
            "/receipt/500000/0b6e5b9c-0000-0000-0000-000000000000",
            accept: BrowserAccept,
            Route("receipt", "/receipt", "http://127.0.0.1:8034", enabled: false, group: "receipt")
        );

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        var page = Body(context);
        Assert.Contains("<title>Component not running</title>", page, StringComparison.Ordinal);
        Assert.Contains("<pre>studioctl env up -r localtest-v2 --with receipt</pre>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("src=", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AnEnabledComponentThatDoesNotAnswerAnswers503ProblemDetails()
    {
        var context = await Send(
            "/storage/api/v1/applications",
            accept: null,
            Route("storage", "/storage", $"http://127.0.0.1:{ClosedPort()}", enabled: true, group: null)
        );

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        using var problem = JsonDocument.Parse(Body(context));
        var root = problem.RootElement;
        Assert.Equal("Component not answering", root.GetProperty("title").GetString());
        Assert.Equal("storage", root.GetProperty("component").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("group").ValueKind);
        var detail = root.GetProperty("detail").GetString();
        Assert.Contains("studioctl env logs -r localtest-v2 -c storage", detail, StringComparison.Ordinal);
        Assert.Contains("studioctl env status -r localtest-v2", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEnabledComponentThatDoesNotAnswerAnswersABrowserWithAPage()
    {
        var context = await Send(
            "/dialogporten/api/v1/serviceowner/dialogs",
            accept: BrowserAccept,
            Route("dialogporten-webapi", "/dialogporten", $"http://127.0.0.1:{ClosedPort()}", true, "dialogporten")
        );

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("text/html; charset=utf-8", context.Response.ContentType);
        var page = Body(context);
        Assert.Contains("<h1>Component not answering</h1>", page, StringComparison.Ordinal);
        Assert.Contains("studioctl env logs -r localtest-v2 -c dialogporten-webapi", page, StringComparison.Ordinal);
    }

    [Fact]
    public void APathWithoutAComponentRouteStillReachesTheLocaltestApp()
    {
        var topology = new BoundTopologyIndex(
            new BoundTopologyConfig
            {
                Version = 1,
                Routes =
                [
                    Route("localtest", "", "http://127.0.0.1:8001", enabled: true, group: null),
                    Route("receipt", "/receipt", "http://127.0.0.1:8034", enabled: false, group: "receipt"),
                ],
            }
        );

        Assert.Equal("localtest", topology.MatchRequest(Host, "/receipts")?.Route.Component);
        Assert.Equal("localtest", topology.MatchRequest(Host, "/nosuch/path")?.Route.Component);
        Assert.Equal("receipt", topology.MatchRequest(Host, "/receipt/api/v1/users/current")?.Route.Component);
    }

    [Theory]
    [InlineData(BrowserAccept, true)]
    [InlineData("text/html", true)]
    [InlineData("application/json", false)]
    [InlineData("application/problem+json, text/html;q=0.5", false)]
    [InlineData("text/html;q=0.5, application/json", false)]
    [InlineData("*/*", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PrefersHtmlOnlyWhenTheRequestRanksItAboveJson(string? accept, bool expected)
    {
        var context = new DefaultHttpContext();
        if (accept is not null)
            context.Request.Headers.Accept = accept;

        Assert.Equal(expected, ComponentUnavailable.PrefersHtml(context.Request));
    }

    private static async Task<DefaultHttpContext> Send(string path, string? accept, params BoundTopologyRoute[] routes)
    {
        var services = new ServiceCollection();
        services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(new ProxyOptions { Listen = "8000", CanonicalHost = Host });
        services.AddHttpForwarder();
        services.AddSingleton(new PlatformTransformer());
        services.AddSingleton(new BoundTopologyOptions());
        services.Configure<BoundTopologyConfig>(
            BoundTopologyOptions.BoundName,
            config =>
            {
                config.Version = 1;
                config.Routes = [.. routes];
            }
        );
        services.AddSingleton<BoundTopologyIndexAccessor>();
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Method = "GET";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString(Host, 8000);
        context.Request.Path = path;
        if (accept is not null)
            context.Request.Headers.Accept = accept;
        context.Response.Body = new MemoryStream();

        await PlatformProxy.Handle(context);
        return context;
    }

    private static string Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, leaveOpen: true);
        return reader.ReadToEnd();
    }

    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static BoundTopologyRoute Route(string component, string prefix, string url, bool enabled, string? group) =>
        new()
        {
            Component = component,
            Enabled = enabled,
            Destination = new BoundTopologyDestination
            {
                Kind = "http",
                Location = "host",
                Url = url,
            },
            Match = new BoundTopologyRouteMatch { Host = Host, PathPrefix = prefix },
            Metadata = group is null ? [] : [new BoundTopologyMetadataEntry { Key = "group", Value = group }],
        };
}
