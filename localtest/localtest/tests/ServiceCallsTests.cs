using Localtest.Proxy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Localtest.Tests;

/// <summary>
/// What a call to a name without an address throws with the localtest app's registration (core and configuration
/// provider, no pass-through): the type and message <see cref="ServiceCalls.NoAddress"/> recognises.
/// </summary>
public sealed class ServiceCallsTests
{
    [Fact]
    public async Task ANameWithoutAnAddressThrowsInvalidOperationExceptionBeforeAnyConnection()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection().Build());
        services.AddServiceDiscoveryCore();
        services.AddConfigurationServiceEndpointProvider();
        services.AddHttpClient("x").AddServiceDiscovery();
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("x");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetAsync(new Uri("http://not-configured/x"), TestContext.Current.CancellationToken));

        Assert.Equal("The endpoint collection contains no endpoints", exception.Message);
        Assert.True(ServiceCalls.NoAddress(exception));
        Assert.True(ServiceCalls.NoAddress(new HttpRequestException("GET http://not-configured/x failed", exception)));
        Assert.True(ServiceCalls.Unreachable(exception));
    }

    [Fact]
    public void OtherFailuresAreNotANoAddress()
    {
        Assert.False(ServiceCalls.NoAddress(new HttpRequestException("Connection refused")));
        Assert.False(ServiceCalls.NoAddress(new InvalidOperationException("something else")));
        Assert.False(ServiceCalls.NoAddress(null));
        Assert.True(ServiceCalls.Unreachable(new HttpRequestException("Connection refused")));
    }
}
