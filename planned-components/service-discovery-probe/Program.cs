using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.ServiceDiscovery;

// Probe of Microsoft.Extensions.ServiceDiscovery's behaviour: a capturing primary handler records the URI the
// factory client finally sends to; nothing goes on the network.
var env = new Dictionary<string, string?>
{
    // the shape the runtime injects: services__<name>__<scheme>__<index>
    ["services:storage:http:0"] = "http://localtest-v2-storage:5010",
    ["services:both:http:0"] = "http://both-http:1",
    ["services:both:https:0"] = "https://both-https:2",
    ["services:onlyhttps:https:0"] = "https://onlyhttps-host:3",
    ["services:withpath:http:0"] = "http://withpath-host:4/base",
};
// Proves the double-underscore environment form maps to the same keys.
Environment.SetEnvironmentVariable("services__envform__http__0", "http://envform-host:5");

async Task Probe(string label, bool full, string url, bool resilience = false)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(env).AddEnvironmentVariables().Build());
    if (full) services.AddServiceDiscovery();
    else services.AddServiceDiscoveryCore().AddConfigurationServiceEndpointProvider();
    services.ConfigureHttpClientDefaults(http =>
    {
        if (resilience) http.AddStandardResilienceHandler();
        http.AddServiceDiscovery();
        http.ConfigurePrimaryHttpMessageHandler(() => new Capture());
    });
    services.AddHttpClient("x");
    await using var sp = services.BuildServiceProvider();
    var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("x");
    try
    {
        var response = await client.GetAsync(url);
        Console.WriteLine($"{label}: {url} -> {response.Headers.GetValues("x-sent").First()} (Host header: {response.Headers.GetValues("x-host").First()})");
    }
    catch (Exception e)
    {
        Console.WriteLine($"{label}: {url} -> {e.GetType().FullName}: {e.Message}");
    }
}

await Probe("configured", true, "http://storage/storage/api/v1/applications");
await Probe("env form", true, "http://envform/x");
await Probe("absent, AddServiceDiscovery (pass-through)", true, "http://register/register/api/v1/parties");
await Probe("absent, configuration provider only", false, "http://register/register/api/v1/parties");
await Probe("public origin, AddServiceDiscovery", true, "http://local.altinn.cloud:8000/storage/api/v1/x");
await Probe("http+https, both entries", true, "http+https://both/x");
await Probe("https+http, both entries", true, "https+http://both/x");
await Probe("http+https, https entry only", true, "http+https://onlyhttps/x");
await Probe("http, https entry only", true, "http://onlyhttps/x");
await Probe("endpoint with a path", true, "http://withpath/storage/x");
await Probe("configured, with standard resilience first", true, "http://storage/x", resilience: true);

// The YARP destination resolver, as the front door would register it.
async Task Yarp(string label, bool full, string address)
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(env).Build());
    if (full) services.AddServiceDiscovery();
    else services.AddServiceDiscoveryCore().AddConfigurationServiceEndpointProvider();
    services.AddReverseProxy().AddServiceDiscoveryDestinationResolver();
    await using var sp = services.BuildServiceProvider();
    var resolver = sp.GetRequiredService<IDestinationResolver>();
    try
    {
        var result = await resolver.ResolveDestinationsAsync(new Dictionary<string, DestinationConfig> { ["d"] = new() { Address = address } }, default);
        Console.WriteLine($"yarp {label}: {address} -> [{string.Join(", ", result.Destinations.Select(kv => kv.Key + "=" + kv.Value.Address))}] ({result.Destinations.Count} destinations)");
    }
    catch (Exception e)
    {
        Console.WriteLine($"yarp {label}: {address} -> {e.GetType().FullName}: {e.Message}");
    }
}

await Yarp("configured", true, "http://storage");
await Yarp("configured, path kept", true, "http://storage/storage");
await Yarp("absent, AddServiceDiscovery (pass-through)", true, "http://register");
await Yarp("absent, configuration provider only", false, "http://register");

sealed class Capture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Headers.Add("x-sent", request.RequestUri!.ToString());
        response.Headers.Add("x-host", request.Headers.Host ?? "(none: taken from the URI)");
        return Task.FromResult(response);
    }
}
