using System.Net;
using System.Text.Json;

namespace Localtest.Tests.Emulation;

/// <summary>
/// Storage's calls in minimal mode: it calls the PDP, the Register and Authentication's discovery by service name
/// (<c>http://authorization/authorization/api/v1/decision</c>), which its resolver sends to the front door's own address
/// (<c>services__authorization__http__0=http://localtest:8080</c>), so the request arrives with <c>Host: localtest:8080</c>,
/// or with the service name where a client sets the header itself. Every such host is the main host: the emulation
/// answers on the same paths as on the public host.
/// </summary>
public sealed class ServiceNameHostTests : IAsyncLifetime
{
    private MinimalFrontDoor _frontDoor = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        _frontDoor = new MinimalFrontDoor();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _frontDoor.DisposeAsync();

    [Theory]
    [InlineData("localtest:8080")]
    [InlineData("localtest")]
    [InlineData("register")]
    [InlineData("authorization")]
    [InlineData("authentication")]
    public async Task StoragesCallsAreAnsweredByTheEmulationOnAServiceNameHost(string host)
    {
        using var client = _frontDoor.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false, BaseAddress = new Uri("http://" + host) });
        client.DefaultRequestHeaders.Host = host;

        // The PDP (Storage's PEP): one permit.
        using var decision = await client.PostAsync(
            "/authorization/api/v1/decision",
            MinimalFrontDoor.Json("""{"request":{"accessSubject":[],"action":[],"resource":[]}}"""),
            Ct);
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode);
        Assert.Equal("authorization", Assert.Single(decision.Headers.GetValues(Localtest.Emulation.Header)));
        using (var body = JsonDocument.Parse(await decision.Content.ReadAsStringAsync(Ct)))
        {
            Assert.Equal("Permit", body.RootElement.GetProperty("response")[0].GetProperty("decision").GetString());
        }

        // The Register (Storage's RegisterService).
        using var party = await client.GetAsync("/register/api/v1/parties/500000", Ct);
        Assert.Equal(HttpStatusCode.OK, party.StatusCode);
        Assert.Equal("register", Assert.Single(party.Headers.GetValues(Localtest.Emulation.Header)));

        // Authentication's discovery document; its issuer stays the public origin whatever host asked.
        using var discovery = await client.GetAsync("/authentication/api/v1/openid/.well-known/openid-configuration", Ct);
        Assert.Equal(HttpStatusCode.OK, discovery.StatusCode);
        using (var body = JsonDocument.Parse(await discovery.Content.ReadAsStringAsync(Ct)))
        {
            Assert.Equal(MinimalFrontDoor.Origin + "/authentication/api/v1/openid/", body.RootElement.GetProperty("issuer").GetString());
        }
    }
}
