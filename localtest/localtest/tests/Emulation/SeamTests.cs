using System.Net;
using System.Text.Json;
using Localtest.Emulated;
using Localtest.Home;
using Localtest.IdPorten;
using Localtest.Provisioning;
using Localtest.Proxy;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests.Emulation;

/// <summary>
/// The owner's rule: the emulation is a folder the rest of the image does not depend on. Without a service at the front
/// door's own address nothing of it is registered or mapped, and the default stack behaves as before (the other test
/// classes, unchanged, are the rest of that proof).
/// </summary>
public sealed class SeamTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly string[] EmulatedPrefixes = ["/register", "/profile", "/authentication", "/authorization", "/accessmanagement", "/events", "/resourceregistry", "/notifications"];

    [Fact]
    public async Task WithoutASelfAddressTheEmulationRegistersAndMapsNothing()
    {
        // Host settings, as the minimal fixture passes them, so the builder sees them: only foreign addresses.
        await using var frontDoor = new MinimalFrontDoor(
            new Dictionary<string, string?> { ["register"] = "http://register:5020", ["storage"] = "http://storage:5010" },
            emulated: []);
        using var client = frontDoor.Client();
        var services = frontDoor.Services;

        Assert.Null(services.GetService<EmulationMode>());
        Assert.Null(services.GetService<IProvisioningTarget>());
        Assert.Null(services.GetService<ITestUserSource>());
        Assert.Null(services.GetService<IInProcessAuthentication>());
        Assert.Null(services.GetService<HomeBanner>());
        Assert.Null(services.GetService<IEmulatedServices>());
        Assert.Null(services.GetService<TestDataStore>());
        var patterns = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Select(e => "/" + e.RoutePattern.RawText?.TrimStart('/')).ToList();
        Assert.DoesNotContain(patterns, p => EmulatedPrefixes.Any(prefix => p.StartsWith(prefix, StringComparison.Ordinal)));

        using var routes = JsonDocument.Parse(await client.GetStringAsync("/localtest/api/routes", Ct));
        var register = routes.RootElement.GetProperty("platform").EnumerateArray().Single(r => r.GetProperty("service").GetString() == "register");
        Assert.Equal("http://register:5020", register.GetProperty("address").GetString());
        Assert.DoesNotContain("Minimal mode", await client.GetStringAsync("/", Ct), StringComparison.Ordinal);
        using var forwarded = await client.GetAsync("/correspondence/x", Ct);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, forwarded.StatusCode);
        Assert.False(forwarded.Headers.Contains(Localtest.Emulation.Header));
    }

    [Fact]
    public void ASelfAddressForAServiceWithoutAnEmulationIsRefusedAtStart()
    {
        using var frontDoor = new MinimalFrontDoor(new Dictionary<string, string?> { ["dialogporten-webapi"] = MinimalFrontDoor.Self });

        var error = Assert.Throws<InvalidOperationException>(() => frontDoor.Services);
        Assert.Contains("dialogporten-webapi", error.Message, StringComparison.Ordinal);
        Assert.Contains("to itself", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASelfAddressTheBuilderDidNotSeeIsRefusedInsteadOfLooping()
    {
        // The default fixture adds its configuration after the builder: a self-address there cannot switch the mode on.
        using var frontDoor = new FrontDoor(new Dictionary<string, string?> { ["register"] = MinimalFrontDoor.Self });

        var error = Assert.Throws<InvalidOperationException>(() => frontDoor.Services);
        Assert.Contains("after the builder", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFrontDoorsOwnEntryIsNotAnEmulatedService()
    {
        // The default stack: every runtime gives the front door its own address too (services__localtest__http__0).
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["services:localtest:http:0"] = "http://localtest:8080",
            ["services:register:http:0"] = "http://register:5020",
            ["services:postgres:http:0"] = "http://localtest:8080",
        }).Build();

        Assert.Null(EmulationMode.FromConfiguration(configuration));
    }

    [Fact]
    public void TheSelfAddressIsConfigurableAndComparedByOrigin()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Localtest:SelfAddress"] = "http://front-door:9000",
            ["services:register:http:0"] = "HTTP://Front-Door:9000/",
            ["services:profile:http:0"] = "http://localtest:8080",
        }).Build();

        var mode = EmulationMode.FromConfiguration(configuration)!;

        Assert.Equal(["register"], mode.Services);
        Assert.False(mode.Emulates("profile"));
    }
}
