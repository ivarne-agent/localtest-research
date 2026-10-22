using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Localtest.Tests;

/// <summary>
/// <c>PUT/DELETE/GET localtest/api/apps</c> against a fake app and a fake platform on loopback ports: validation, the
/// provisioning calls (the provisioning library's tests give the order), idempotency, failures, persistence; and the
/// frontend dev server's pushed route.
/// </summary>
public sealed class RegistrationTests : IAsyncLifetime
{
    private const string Org = "ttd";
    private const string App = "fake-app";
    private const string Metadata = """{"id":"ttd/fake-app","org":"ttd"}""";
    private const string TestData = """{"persons":[]}""";
    private const string Texts = """{"language":"nb","resources":[]}""";

    private static readonly string[] ProvisionCalls =
    [
        "GET /authentication/api/v1/exchange/altinnstudio?test=true",
        "PUT /register/api/localtest/apps/ttd/fake-app",
        "POST /storage/api/v1/applications?appId=ttd/fake-app",
        "POST /storage/api/v1/applications/ttd/fake-app/texts",
        "POST /authorization/api/v1/policies?org=ttd&app=fake-app",
        "GET /resourceregistry/api/v1/resource/app_ttd_fake-app/policy/subjects?reloadFromXacml=true",
        "POST /accessmanagement/api/localtest/refresh",
    ];

    private FakeServer _app = null!;
    private FakeServer _platform = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _app = await FakeServer.Start(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            context.Response.Headers.SetCookie = "runtime=1; path=/; secure; httponly";
            return path switch
            {
                "/ttd/fake-app/api/v1/applicationmetadata" => Json(context, Metadata),
                "/ttd/fake-app/api/v1/meta/authorizationpolicy" => context.Response.WriteAsync("<Policy RuleId=\"[ORG]:[app]\"/>"),
                "/ttd/fake-app/api/v1/applicationlanguages" => Json(context, """[{"language":"nb"}]"""),
                "/ttd/fake-app/api/v1/texts/nb" => Json(context, Texts),
                "/ttd/fake-app/testData.json" => Json(context, TestData),
                _ => NotFound(context),
            };
        });
        _platform = await FakeServer.Start(context =>
        {
            var path = context.Request.Path.Value ?? string.Empty;
            if (path.StartsWith("/authentication/api/v1/exchange/altinnstudio", StringComparison.Ordinal))
            {
                return context.Response.WriteAsync("\"studio-token\"");
            }

            if (context.Request.Method == "POST" && path.StartsWith("/storage/", StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status201Created;
            }

            return Task.CompletedTask;
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        await _platform.DisposeAsync();
    }

    private Dictionary<string, string?> Platform(params string[] extra)
    {
        var services = new Dictionary<string, string?>();
        foreach (var name in new[] { "authentication", "register", "storage", "authorization", "resource-registry", "access-management" }.Concat(extra))
        {
            services[name] = _platform.Origin;
        }

        return services;
    }

    private static Task Json(HttpContext context, string body)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(body);
    }

    private static Task NotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }

    private static Task<HttpResponseMessage> Put(HttpClient client, string path, string? url)
        => client.PutAsync(path, new StringContent(url is null ? "{}" : $$"""{"url":"{{url}}"}""", Encoding.UTF8, "application/json"), Ct);

    private static async Task<JsonElement> Body(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return document.RootElement.Clone();
    }

    [Theory]
    [InlineData("ttd/Not_Valid", "http://127.0.0.1:5005")]
    [InlineData("storage/app", "http://127.0.0.1:5005")]
    [InlineData("localtest/app", "http://127.0.0.1:5005")]
    [InlineData("ttd/app", "http://127.0.0.1:5005/ttd/app")]
    [InlineData("ttd/app", "http://127.0.0.1:5005/?x=1")]
    [InlineData("ttd/app", "ftp://127.0.0.1:5005")]
    [InlineData("ttd/app", "not a url")]
    [InlineData("ttd/app", null)]
    public async Task AnInvalidRegistrationIsRefusedAndRoutesNothing(string orgApp, string? url)
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();

        using var response = await Put(client, "/localtest/api/apps/" + orgApp, url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, (await Body(await client.GetAsync("/localtest/api/apps", Ct))).GetArrayLength());
        Assert.Empty(_platform.Requests);
    }

    [Fact]
    public async Task RegisteringRoutesAndProvisionsFromTheRunningApp()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();

        using var response = await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await Body(response);
        Assert.Equal(_app.Origin, body.GetProperty("url").GetString());
        var provisioning = body.GetProperty("provisioning");
        Assert.Equal("succeeded", provisioning.GetProperty("state").GetString());
        var result = provisioning.GetProperty("result");
        Assert.Equal(1, result.GetProperty("textCount").GetInt32());
        Assert.True(result.GetProperty("hasPolicy").GetBoolean());
        Assert.True(result.GetProperty("hasTestData").GetBoolean());
        Assert.Equal("Skipped", result.GetProperty("dialogporten").GetString());

        Assert.Equal(ProvisionCalls, _platform.Calls);
        var requests = _platform.Requests.ToArray();
        Assert.Equal("local.altinn.cloud:8000", requests[0].Host);
        Assert.All(requests.Where(r => r.Call.Contains(" /storage/", StringComparison.Ordinal) || r.Call.Contains(" /authorization/", StringComparison.Ordinal)), r => Assert.Equal("Bearer studio-token", r.Authorization));
        Assert.Equal(TestData, requests[1].Body);
        Assert.Equal(Metadata, requests[2].Body);
        Assert.Equal(Texts, requests[3].Body);
        Assert.Equal("<Policy RuleId=\"ttd:fake-app\"/>", requests[4].Body);

        // The app answers through the front door, its cookie without Secure.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ttd/fake-app/api/v1/applicationmetadata");
        using var routed = await client.SendAsync(request, Ct);
        Assert.Equal(Metadata, await routed.Content.ReadAsStringAsync(Ct));
        Assert.Equal("runtime=1; path=/; httponly", Assert.Single(routed.Headers.GetValues("Set-Cookie")));
    }

    [Fact]
    public async Task RegisteringAgainProvisionsAgainAndKeepsOneRoute()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();

        using var first = await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin);
        using var second = await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal((await Body(first)).GetProperty("registered").GetString(), (await Body(second)).GetProperty("registered").GetString());
        Assert.Equal(ProvisionCalls.Concat(ProvisionCalls), _platform.Calls);
        var listed = await Body(await client.GetAsync("/localtest/api/apps", Ct));
        Assert.Equal(1, listed.GetArrayLength());
        Assert.Equal("succeeded", listed[0].GetProperty("provisioning").GetProperty("state").GetString());
    }

    [Fact]
    public async Task DialogportenIsSyncedWhenTheAdapterHasAnAddress()
    {
        var services = Platform("dialogporten-webapi");
        services["dialogporten-adapter"] = _platform.Origin;
        await using var frontDoor = new FrontDoor(services);
        using var client = frontDoor.Client();

        using var response = await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin);

        Assert.Equal("Synced", (await Body(response)).GetProperty("provisioning").GetProperty("result").GetProperty("dialogporten").GetString());
        Assert.Equal("POST /dialogporten/api/v1/localtest/sync", _platform.Calls.Last());
    }

    [Fact]
    public async Task AFailedProvisioningKeepsTheRouteAndAnswersTheProblem()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();
        var closed = $"http://127.0.0.1:{FakeServer.ClosedPort()}";

        using var response = await Put(client, "/localtest/api/apps/ttd/fake-app", closed);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await Body(response);
        Assert.Contains($"GET {closed}/ttd/fake-app/api/v1/applicationmetadata?checkOrgApp=false failed", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
        var listed = await Body(await client.GetAsync("/localtest/api/apps", Ct));
        Assert.Equal(closed, listed[0].GetProperty("url").GetString());
        Assert.Equal("failed", listed[0].GetProperty("provisioning").GetProperty("state").GetString());
        Assert.DoesNotContain(_platform.Requests, r => !r.Call.Contains("/authentication/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task APlatformServiceWithoutAnAddressFailsTheProvisioningByName()
    {
        var services = Platform();
        services.Remove("storage");
        await using var frontDoor = new FrontDoor(services);
        using var client = frontDoor.Client();

        using var response = await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin);

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        var detail = (await Body(response)).GetProperty("detail").GetString()!;
        Assert.Contains("POST http://storage/storage/api/v1/applications?appId=ttd/fake-app failed: The endpoint collection contains no endpoints", detail, StringComparison.Ordinal);
        Assert.Contains("services__<name>__http__0 is not set", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletingDeprovisionsAndRemovesTheRouteAndCanBeRepeated()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();
        (await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin)).Dispose();
        _platform.Requests.Clear();

        using var deleted = await client.DeleteAsync("/localtest/api/apps/ttd/fake-app", Ct);
        using var again = await client.DeleteAsync("/localtest/api/apps/ttd/fake-app", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        string[] deprovision = ["DELETE /register/api/localtest/apps/ttd/fake-app", "POST /accessmanagement/api/localtest/refresh"];
        Assert.Equal(deprovision.Concat(deprovision), _platform.Calls);
        Assert.Equal(0, (await Body(await client.GetAsync("/localtest/api/apps", Ct))).GetArrayLength());
        using var gone = await client.GetAsync("/ttd/fake-app/api/v1/applicationmetadata", Ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public async Task RoutesSurviveARestart()
    {
        string dataDirectory;
        await using (var frontDoor = new FrontDoor(Platform()))
        {
            dataDirectory = frontDoor.DataDirectory;
            using var client = frontDoor.Client();
            (await Put(client, "/localtest/api/apps/ttd/fake-app", _app.Origin)).Dispose();
            (await Put(client, "/localtest/api/routes/frontend-dev-server", _app.Origin)).Dispose();
        }

        await using var restarted = new FrontDoor(Platform(), dataDirectory);
        using var restartedClient = restarted.Client();
        using var routed = await restartedClient.GetAsync("/ttd/fake-app/api/v1/applicationmetadata", Ct);
        Assert.Equal(Metadata, await routed.Content.ReadAsStringAsync(Ct));
        var listed = await Body(await restartedClient.GetAsync("/localtest/api/apps", Ct));
        Assert.Equal("succeeded", listed[0].GetProperty("provisioning").GetProperty("state").GetString());
        var table = await Body(await restartedClient.GetAsync("/localtest/api/routes", Ct));
        Assert.Equal(_app.Origin, table.GetProperty("frontendDevServer").GetProperty("url").GetString());
    }

    [Fact]
    public async Task TheDevServerRouteTakesItsHostWhileItIsSet()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();

        HttpRequestMessage OnDevHost()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/ttd/fake-app/api/v1/applicationmetadata");
            request.Headers.Host = "app-frontend.local.altinn.cloud:8000";
            return request;
        }

        using var set = await Put(client, "/localtest/api/routes/frontend-dev-server", _app.Origin);
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        using var routed = await client.SendAsync(OnDevHost(), Ct);
        Assert.Equal(Metadata, await routed.Content.ReadAsStringAsync(Ct));
        Assert.Equal("app-frontend.local.altinn.cloud:8000", _app.Requests.Last().Host);

        using var removed = await client.DeleteAsync("/localtest/api/routes/frontend-dev-server", Ct);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        using var fallsThrough = await client.SendAsync(OnDevHost(), Ct);
        Assert.Equal(HttpStatusCode.NotFound, fallsThrough.StatusCode);

        using var invalid = await Put(client, "/localtest/api/routes/frontend-dev-server", "http://127.0.0.1:8080/path");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task TheRouteTableListsEachServicesAddress()
    {
        await using var frontDoor = new FrontDoor(Platform());
        using var client = frontDoor.Client();

        var table = await Body(await client.GetAsync("/localtest/api/routes", Ct));

        var platform = table.GetProperty("platform").EnumerateArray().ToArray();
        Assert.Equal(_platform.Origin, platform.Single(r => r.GetProperty("prefix").GetString() == "/storage").GetProperty("address").GetString());
        Assert.Equal(JsonValueKind.Null, platform.Single(r => r.GetProperty("prefix").GetString() == "/receipt").GetProperty("address").ValueKind);
        Assert.Equal("/graphql", platform.Single(r => r.GetProperty("service").GetString() == "dialogporten-graphql").GetProperty("rewrite").GetString());
        Assert.Equal("pdf3", platform.Single(r => r.GetProperty("host").GetString() == "pdf.local.altinn.cloud").GetProperty("service").GetString());
    }
}
