using System.Net;
using Localtest.Proxy;
using Localtest.Shared.ServiceDiscovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests;

/// <summary>
/// The components' registration (<c>components/shared/fakes/ServiceDiscovery/</c>, a copy in each component's fakes):
/// a service name resolves from <c>services__{name}__http__0</c> or fails before any connection; a real host (a dot, an
/// IP address, <c>localhost</c>) is sent as it is, which the configuration provider alone would refuse.
/// </summary>
public sealed class ComponentServiceDiscoveryTests
{
    [Fact]
    public async Task AServiceNameResolvesFromItsEntry()
    {
        var (client, sent, provider) = Client(services => services.AddLocaltestServiceDiscovery());
        using var _ = provider;

        await client.GetAsync(new Uri("http://register/register/api/v1/parties/1"), TestContext.Current.CancellationToken);

        Assert.Equal("http://localtest-v2-register:5020/register/api/v1/parties/1", Assert.Single(sent).ToString());
    }

    [Fact]
    public async Task AServiceNameWithoutAnEntryFailsBeforeAnyConnection()
    {
        var (client, sent, provider) = Client(services => services.AddLocaltestServiceDiscovery());
        using var _ = provider;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetAsync(new Uri("http://dialogporten-adapter/storage/dialogporten/api/localtest/enqueue"), TestContext.Current.CancellationToken));

        Assert.Equal("The endpoint collection contains no endpoints", exception.Message);
        Assert.True(LocaltestServiceDiscovery.NoAddress(new HttpRequestException("wrapped", exception)));
        Assert.False(LocaltestServiceDiscovery.NoAddress(new HttpRequestException("Connection refused")));
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("http://local.altinn.cloud:8000/authentication/api/v1/openid/")]
    [InlineData("http://host.docker.internal:5005/ttd/app/instances")]
    [InlineData("https://altinncdn.no/orgs/altinn-orgs.json")]
    [InlineData("http://localhost:5090/notifications/api/v1/sms")]
    [InlineData("http://127.0.0.1:5005/x")]
    public async Task ARealHostIsSentAsItIs(string url)
    {
        var (client, sent, provider) = Client(services => services.AddLocaltestServiceDiscovery());
        using var _ = provider;

        await client.GetAsync(new Uri(url), TestContext.Current.CancellationToken);

        Assert.Equal(url, Assert.Single(sent).ToString());
    }

    [Fact]
    public async Task AfterAddServiceDiscoveryThePassThroughIsRemoved()
    {
        // As AddAltinnServiceDefaults registers it (Register, Authentication, Access Management, the Resource Registry).
        var (client, sent, provider) = Client(services =>
        {
            services.AddServiceDiscovery();
            services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
            services.AddLocaltestServiceEndpointProviders();
        });
        using var _ = provider;

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(new Uri("http://notifications/x"), TestContext.Current.CancellationToken));
        await client.GetAsync(new Uri("http://register/x"), TestContext.Current.CancellationToken);
        await client.GetAsync(new Uri("https://altinncdn.no/x"), TestContext.Current.CancellationToken);

        Assert.Equal(["http://localtest-v2-register:5020/x", "https://altinncdn.no/x"], sent.Select(u => u.ToString()));
    }

    [Theory]
    [InlineData("register", false)]
    [InlineData("dialogporten-webapi", false)]
    [InlineData("local.altinn.cloud", true)]
    [InlineData("host.docker.internal", true)]
    [InlineData("localhost", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("[::1]", true)]
    public void ARealHostIsOneThatIsNotASingleLabel(string host, bool real) => Assert.Equal(real, LocaltestServiceDiscovery.IsRealHost(host));

    [Fact]
    public void ThePrefixRoutingMatchesTheFrontDoorsTable()
    {
        var origin = new Uri("http://local.altinn.cloud:8000/");
        var table = PlatformRoutes.Table(LocaltestSettings.FromConfiguration(new ConfigurationBuilder().Build()))
            // Grafana is for the browser and OTLP for the apps on the host (rewritten anyway): no component calls them
            // on the public origin.
            .Where(r => r.Host is null && r.Rewrite is null && PlatformRoutes.GroupOf(r.Service) != PlatformRoutes.MonitoringGroup)
            .ToList();
        Assert.NotEmpty(table);
        foreach (var (_, prefix, service, _) in table)
        {
            Assert.Equal($"http://{service}{prefix}/api/v1/x?y=1", LocaltestPrefixRouting.ServiceUri(new Uri(origin, prefix + "/api/v1/x?y=1"), origin)?.ToString());
        }

        // The rewritten GraphQL path, another host and a path no service owns stay as they are.
        Assert.Null(LocaltestPrefixRouting.ServiceUri(new Uri("http://local.altinn.cloud:8000/dialogporten/graphql"), origin));
        Assert.Null(LocaltestPrefixRouting.ServiceUri(new Uri("http://af.local.altinn.cloud:8000/register/x"), origin));
        Assert.Null(LocaltestPrefixRouting.ServiceUri(new Uri("http://local.altinn.cloud:8000/localtest/api/x"), origin));
        Assert.Null(LocaltestPrefixRouting.ServiceUri(new Uri("http://local.altinn.cloud:8000/registerx/y"), origin));
    }

    [Fact]
    public async Task ThePrefixRoutingRunsBeforeTheResolver()
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["services:register:http:0"] = "http://localtest-v2-register:5020" })
            .Build());
        services.AddLogging();
        services.AddLocaltestServiceDiscovery();
        services.AddHttpClient("IRegister", c => c.BaseAddress = new Uri("http://local.altinn.cloud:8000/")).ConfigurePrimaryHttpMessageHandler(() => new Capture(sent));
        LocaltestPrefixRouting.AddTo(services, _ => new Uri("http://local.altinn.cloud:8000/"), "IRegister");
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("IRegister");

        await client.GetAsync("register/api/v1/parties/1", TestContext.Current.CancellationToken);
        await client.GetAsync("localtest/api/x", TestContext.Current.CancellationToken);

        Assert.Equal(["http://localtest-v2-register:5020/register/api/v1/parties/1", "http://local.altinn.cloud:8000/localtest/api/x"], sent.Select(u => u.ToString()));
    }

    private static (HttpClient Client, List<Uri> Sent, ServiceProvider Provider) Client(Action<IServiceCollection> register)
    {
        var sent = new List<Uri>();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["services:register:http:0"] = "http://localtest-v2-register:5020" })
            .Build());
        services.AddLogging();
        register(services);
        services.AddHttpClient("x").ConfigurePrimaryHttpMessageHandler(() => new Capture(sent));
        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IHttpClientFactory>().CreateClient("x"), sent, provider);
    }

    private sealed class Capture(List<Uri> sent) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            sent.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
