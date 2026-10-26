using System.Net;
using System.Text;
using System.Text.Json;
using Localtest.Proxy;
using Microsoft.AspNetCore.Http;

namespace Localtest.Tests;

/// <summary>
/// The monitoring group's routes: Grafana under <c>/grafana</c> (served from its sub-path, so the path is kept) and
/// OTLP over HTTP under <c>/otlp</c> to the collector's receiver at its root; the 503 when the group is off, which no
/// Aspire hint names; and the Home page's Grafana link while the route is live.
/// </summary>
public sealed class MonitoringRoutesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<FakeServer> Echo(string name) => FakeServer.Start(context =>
        context.Response.WriteAsync(name + " " + context.Request.Method + " " + context.Request.Path + context.Request.QueryString));

    private static HttpRequestMessage Request(HttpMethod method, string path, string? accept = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Host = "local.altinn.cloud:8000";
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        return request;
    }

    [Theory]
    [InlineData("/grafana/", "grafana", null)]
    [InlineData("/grafana/api/health", "grafana", null)]
    [InlineData("/otlp/v1/traces", "otel-collector", "/v1/traces")]
    [InlineData("/otlp/v1/logs", "otel-collector", "/v1/logs")]
    public void ThePrefixesResolveToTheMonitoringServices(string path, string service, string? rewritten)
    {
        var route = PlatformRoutes.Resolve(path);

        Assert.Equal(service, route?.Name);
        Assert.Equal(rewritten, route?.Rewrite?.Apply(path).Value);
        Assert.Equal(PlatformRoutes.MonitoringGroup, PlatformRoutes.GroupOf(service));
    }

    [Fact]
    public async Task GrafanaKeepsItsPathAndOtlpLosesThePrefix()
    {
        await using var grafana = await Echo("grafana");
        await using var collector = await Echo("otel-collector");
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?>
        {
            ["grafana"] = grafana.Origin,
            ["otel-collector"] = collector.Origin,
        });
        using var client = frontDoor.Client();

        using var dashboards = await client.SendAsync(Request(HttpMethod.Get, "/grafana/api/search?query=app"), Ct);
        using var post = Request(HttpMethod.Post, "/otlp/v1/traces");
        post.Content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
        post.Content.Headers.ContentType = new("application/json");
        using var traces = await client.SendAsync(post, Ct);
        using var bare = await client.SendAsync(Request(HttpMethod.Get, "/grafana"), Ct);

        Assert.Equal("grafana GET /grafana/api/search?query=app", await dashboards.Content.ReadAsStringAsync(Ct));
        Assert.Equal("otel-collector POST /v1/traces", await traces.Content.ReadAsStringAsync(Ct));
        Assert.Equal("{}", Assert.Single(collector.Requests).Body);
        Assert.Equal(HttpStatusCode.MovedPermanently, bare.StatusCode);
        Assert.Equal("/grafana/", bare.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("/grafana/", "grafana")]
    [InlineData("/otlp/v1/traces", "otel-collector")]
    public async Task WithTheGroupOffTheRouteIsNotRunningAndNamesOnlyStudioctl(string path, string service)
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Request(HttpMethod.Get, path), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = problem.RootElement;
        Assert.Equal(ComponentUnavailable.NotRunningTitle, root.GetProperty("title").GetString());
        Assert.Equal(service, root.GetProperty("component").GetString());
        Assert.Equal("monitoring", root.GetProperty("group").GetString());
        var detail = root.GetProperty("detail").GetString()!;
        Assert.Contains("studioctl env up -r localtest-v2 --with monitoring", detail, StringComparison.Ordinal);
        Assert.Contains("the Aspire dashboard", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("WithGroup", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("WithMonitoring", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePageForABrowserHasTheStudioctlCommandOnly()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Request(HttpMethod.Get, "/grafana/", "text/html"), Ct);

        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<pre>studioctl env up -r localtest-v2 --with monitoring</pre>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHomePageLinksToGrafanaOnlyWhileItsRouteIsLive()
    {
        await using var grafana = await Echo("grafana");
        await using var with = new FrontDoor(new Dictionary<string, string?> { ["grafana"] = grafana.Origin });
        await using var without = new FrontDoor();

        using var withClient = with.Client();
        using var withoutClient = without.Client();
        var on = await withClient.GetStringAsync("/", Ct);
        var off = await withoutClient.GetStringAsync("/", Ct);

        Assert.Contains("<a href=\"/grafana/\">Grafana</a>", on, StringComparison.Ordinal);
        Assert.DoesNotContain("/grafana/", off, StringComparison.Ordinal);
    }
}
