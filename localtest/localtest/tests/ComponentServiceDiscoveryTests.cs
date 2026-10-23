using System.Net;
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
