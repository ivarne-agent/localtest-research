using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Localtest.Authentication;
using Microsoft.AspNetCore.Http;

namespace Localtest.Tests;

/// <summary>
/// The front door at another public origin than the default (<c>http://origin.test:18000/</c>): the dedicated hosts,
/// the issuers and the redirect URIs follow <c>Localtest:PlatformEndpoint</c>, and the subdomains the runner lists.
/// </summary>
public sealed class PublicOriginTests
{
    private const string Origin = "http://origin.test:18000";
    private const string Public = "origin.test:18000";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> At(string? subdomains = null)
    {
        var settings = new Dictionary<string, string?> { ["Localtest:PlatformEndpoint"] = Origin + "/" };
        if (subdomains is not null)
        {
            settings["Localtest:PublicSubdomains"] = subdomains;
        }

        return settings;
    }

    private static Task<FakeServer> Echo(string name) => FakeServer.Start(context => context.Response.WriteAsync(name + " " + context.Request.Path));

    private static HttpRequestMessage Get(string path, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return request;
    }

    [Fact]
    public async Task TheDedicatedHostsAreTheConfiguredHostsSubdomains()
    {
        await using var pdf = await Echo("pdf3");
        await using var arbeidsflate = await Echo("arbeidsflate");
        await using var workflowEngine = await Echo("workflow-engine");
        await using var frontDoor = new FrontDoor(
            new Dictionary<string, string?> { ["pdf3"] = pdf.Origin, ["arbeidsflate"] = arbeidsflate.Origin, ["workflow-engine"] = workflowEngine.Origin },
            settings: At());
        using var client = frontDoor.Client();

        async Task<string> Body(string host)
        {
            using var response = await client.SendAsync(Get("/x", host), Ct);
            return await response.Content.ReadAsStringAsync(Ct);
        }

        Assert.Equal("pdf3 /x", await Body("pdf." + Public));
        Assert.Equal("arbeidsflate /x", await Body("af." + Public));
        Assert.Equal("workflow-engine /x", await Body("workflow-engine." + Public));
        // The default origin's names are not this origin's: served like the main host.
        Assert.NotEqual("pdf3 /x", await Body("pdf.local.altinn.cloud:8000"));
        Assert.DoesNotContain(pdf.Requests, r => r.Host.StartsWith("pdf.local", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASubdomainTheRunnerDoesNotListIsServedLikeTheMainHost()
    {
        await using var arbeidsflate = await Echo("arbeidsflate");
        await using var frontDoor = new FrontDoor(
            new Dictionary<string, string?> { ["arbeidsflate"] = arbeidsflate.Origin },
            settings: At("pdf,workflow-engine,app-frontend"));
        using var client = frontDoor.Client();

        using var health = await client.SendAsync(Get("/health", "af." + Public), Ct);

        Assert.Equal("ok", await health.Content.ReadAsStringAsync(Ct));
        Assert.Empty(arbeidsflate.Requests);
    }

    [Fact]
    public async Task AnUnknownSubdomainStopsTheStart()
    {
        await using var frontDoor = new FrontDoor(settings: At("pdf,grafana"));

        var error = Assert.ThrowsAny<Exception>(() => frontDoor.Client());

        Assert.Contains("grafana", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFrontendDevServerHostIsTheConfiguredHostsSubdomain()
    {
        await using var vite = await Echo("vite");
        await using var frontDoor = new FrontDoor(settings: At());
        using var client = frontDoor.Client();
        using var put = await client.PutAsJsonAsync("/localtest/api/routes/frontend-dev-server", new { url = vite.Origin }, Ct);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync(Ct));

        using var response = await client.SendAsync(Get("/main.js", "app-frontend." + Public), Ct);

        Assert.Equal("vite /main.js", await response.Content.ReadAsStringAsync(Ct));
        Assert.Equal("app-frontend." + Public, Assert.Single(vite.Requests).Host);
    }

    [Fact]
    public async Task TheRouteTableNamesTheConfiguredHosts()
    {
        await using var frontDoor = new FrontDoor(settings: At());
        using var client = frontDoor.Client();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/localtest/api/routes", Ct));

        var hosts = document.RootElement.GetProperty("platform").EnumerateArray()
            .Select(r => r.GetProperty("host").GetString()).OfType<string>().Order(StringComparer.Ordinal);
        Assert.Equal(["af.origin.test", "pdf.origin.test", "workflow-engine.origin.test"], hosts);
    }

    [Fact]
    public async Task TheIssuersAreOnTheConfiguredOrigin()
    {
        await using var frontDoor = new FrontDoor(settings: At());
        using var client = frontDoor.Client();

        using var idPorten = JsonDocument.Parse(await client.GetStringAsync("/localtest/api/idporten/.well-known/openid-configuration", Ct));
        using var maskinporten = JsonDocument.Parse(await client.GetStringAsync("/localtest/api/maskinporten/.well-known/oauth-authorization-server", Ct));

        Assert.Equal(Origin + "/localtest/api/idporten", idPorten.RootElement.GetProperty("issuer").GetString());
        Assert.Equal(Origin + "/localtest/api/maskinporten/", maskinporten.RootElement.GetProperty("issuer").GetString());
    }

    [Fact]
    public void TheSeededClientsRedirectToTheConfiguredOrigin()
    {
        var localtest = new LocaltestSettings(Origin + "/", "/certs/x.pfx");

        Assert.Equal(
            [Origin + "/localtest/api/authentication/callback", "http://af.origin.test:18000/api/cb"],
            SeededClients.All(localtest).Select(c => c.RedirectUri));
    }

    [Fact]
    public async Task AnIdPortenRedirectUriOnTheDefaultOriginIsRefused()
    {
        await using var frontDoor = new FrontDoor(settings: At());
        using var client = frontDoor.Client();

        using var response = await client.PostAsync(
            "/localtest/api/idporten/authorize",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["redirect_uri"] = "http://local.altinn.cloud:8000/authentication/api/v1/openid/callback",
                ["state"] = "s",
            }),
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(Origin, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }
}
