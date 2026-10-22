using System.Net;
using System.Text.Json;
using Localtest.Proxy;
using Microsoft.AspNetCore.Http;

namespace Localtest.Tests;

/// <summary>The front door forwarding by service name: routes, rewrites, hosts, redirects and the 503s.</summary>
public sealed class ForwarderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Task<FakeServer> Echo(string name) => FakeServer.Start(context =>
    {
        context.Response.Headers["X-Upstream"] = name;
        return context.Response.WriteAsync(name + " " + context.Request.Path + context.Request.QueryString);
    });

    private static HttpRequestMessage Get(string path, string host = "local.altinn.cloud:8000", string? accept = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        if (accept is not null)
        {
            request.Headers.Accept.ParseAdd(accept);
        }

        return request;
    }

    [Fact]
    public async Task APlatformPathGoesToItsServiceByNameWithThePublicHost()
    {
        await using var storage = await Echo("storage");
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?> { ["storage"] = storage.Origin });
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/storage/api/v1/applications?x=1"), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("storage /storage/api/v1/applications?x=1", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("local.altinn.cloud:8000", Assert.Single(storage.Requests).Host);
    }

    [Fact]
    public async Task GraphqlBeforeDialogportenAndTheAdapterBeforeStorage()
    {
        await using var graphql = await Echo("graphql");
        await using var webapi = await Echo("webapi");
        await using var adapter = await Echo("adapter");
        await using var storage = await Echo("storage");
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?>
        {
            ["dialogporten-graphql"] = graphql.Origin,
            ["dialogporten-webapi"] = webapi.Origin,
            ["dialogporten-adapter"] = adapter.Origin,
            ["storage"] = storage.Origin,
        });
        using var client = frontDoor.Client();

        async Task<string> Body(string path)
        {
            using var response = await client.SendAsync(Get(path), Ct);
            return await response.Content.ReadAsStringAsync(Ct);
        }

        Assert.Equal("graphql /graphql", await Body("/dialogporten/graphql"));
        Assert.Equal("graphql /graphql/x?q=1", await Body("/dialogporten/graphql/x?q=1"));
        Assert.Equal("webapi /dialogporten/api/v1/enduser/dialogs", await Body("/dialogporten/api/v1/enduser/dialogs"));
        Assert.Equal("adapter /storage/dialogporten/api/v1/syncDialog", await Body("/storage/dialogporten/api/v1/syncDialog"));
        Assert.Equal("storage /storage/api/v1/x", await Body("/storage/api/v1/x"));
    }

    [Fact]
    public async Task ADedicatedHostOwnsEveryPathBeforeThePrefixesAndTheLocaltestAppsOwn()
    {
        await using var pdf = await Echo("pdf3");
        await using var storage = await Echo("storage");
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?> { ["pdf3"] = pdf.Origin, ["storage"] = storage.Origin });
        using var client = frontDoor.Client();

        using var prefixed = await client.SendAsync(Get("/storage/api/v1/x", "pdf.local.altinn.cloud:8000"), Ct);
        using var health = await client.SendAsync(Get("/health", "pdf.local.altinn.cloud:8000"), Ct);

        Assert.Equal("pdf3 /storage/api/v1/x", await prefixed.Content.ReadAsStringAsync(Ct));
        Assert.Equal("pdf3 /health", await health.Content.ReadAsStringAsync(Ct));
        Assert.Empty(storage.Requests);
    }

    [Theory]
    [InlineData("/storage", "/storage/")]
    [InlineData("/storage?a=1", "/storage/?a=1")]
    [InlineData("/storage/dialogporten", "/storage/dialogporten/")]
    public async Task ABarePrefixIsRedirectedToItsSlashedPath(string path, string location)
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get(path), Ct);

        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal(location, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task AnOptionalServiceWithoutAnAddressIsNotRunningWithBothRuntimesHints()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/receipt/1/2"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = problem.RootElement;
        Assert.Equal(ComponentUnavailable.NotRunningTitle, root.GetProperty("title").GetString());
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("receipt", root.GetProperty("component").GetString());
        Assert.Equal("receipt", root.GetProperty("group").GetString());
        Assert.Equal("/receipt/1/2", root.GetProperty("instance").GetString());
        var detail = root.GetProperty("detail").GetString()!;
        Assert.Contains("services__receipt__http__0", detail, StringComparison.Ordinal);
        Assert.Contains("studioctl env up -r localtest-v2 --with receipt", detail, StringComparison.Ordinal);
        Assert.Contains(".WithReceipt()", detail, StringComparison.Ordinal);
        Assert.Contains(".WithGroup(\"receipt\")", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("\\u0022", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADialogportenServiceNamesItsGroup()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/dialogporten/graphql"), Ct);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("dialogporten-graphql", problem.RootElement.GetProperty("component").GetString());
        Assert.Equal("dialogporten", problem.RootElement.GetProperty("group").GetString());
        Assert.Contains(".WithDialogporten()", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADefaultServiceWithoutAnAddressHasNoGroup()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/register/api/v1/parties"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(JsonValueKind.Null, problem.RootElement.GetProperty("group").ValueKind);
        Assert.Contains("AddAltinnLocaltest()", problem.RootElement.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABrowserGetsTheSameAsAPage()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/correspondence/api/v1/x", accept: "text/html,application/xhtml+xml,*/*;q=0.8"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("<h1>Component not running</h1>", html, StringComparison.Ordinal);
        Assert.Contains("studioctl env up -r localtest-v2 --with correspondence", html, StringComparison.Ordinal);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAddressThatRefusesIsNotAnswering()
    {
        await using var frontDoor = new FrontDoor(new Dictionary<string, string?> { ["storage"] = $"http://127.0.0.1:{FakeServer.ClosedPort()}" });
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/storage/api/v1/x"), Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal(ComponentUnavailable.NotAnsweringTitle, problem.RootElement.GetProperty("title").GetString());
        Assert.Equal("storage", problem.RootElement.GetProperty("component").GetString());
        var detail = problem.RootElement.GetProperty("detail").GetString()!;
        Assert.Contains("did not answer (Request: ", detail, StringComparison.Ordinal);
        Assert.Contains("studioctl env logs -r localtest-v2 -c storage", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAppPathWithoutARegisteredAppKeepsItsHint()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var response = await client.SendAsync(Get("/ttd/nothing/api/v1/applicationmetadata"), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.StartsWith("No app is registered for this path.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLocaltestAppsOwnEndpointsStayOnTheMainHost()
    {
        await using var frontDoor = new FrontDoor();
        using var client = frontDoor.Client();

        using var health = await client.SendAsync(Get("/health"), Ct);
        using var version = await client.SendAsync(Get("/Home/Localtest/Version"), Ct);

        Assert.Equal("ok", await health.Content.ReadAsStringAsync(Ct));
        Assert.Equal("8", await version.Content.ReadAsStringAsync(Ct));
    }
}
