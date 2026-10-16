using System.Net;
using Altinn.Register.Core.Utils;
using Altinn.Register.IntegrationTests.TestServices;
using Altinn.Register.IntegrationTests.Tracing;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.Database;
using Altinn.Register.TestUtils.MassTransit;
using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.IntegrationTests.Fixtures;

public sealed class TestWebApplication
    : IAsyncDisposable
{
    // random link-local IPv6 address
    public static readonly IPAddress RemoteTestIpAddress = IPAddress.Parse("fe80::215:5dff:fe8f:4e7f");

    public static async Task<TestWebApplication> Create(Action<IConfigurationBuilder>? configureConfiguration = null)
    {
        var fixture = await TestContext.Current.GetRequiredFixture<WebApplicationFixture>();

        return await fixture.CreateServer(configureConfiguration: configureConfiguration);
    }

    private readonly WebApplicationFactory<Program> _factory;
    private readonly PostgresDatabase _db;
    private readonly List<HttpClient> _clients = new();

    public TestWebApplication(
        WebApplicationFactory<Program> factory,
        PostgresDatabase db)
    {
        _factory = factory;
        _db = db;
    }

    public IServiceProvider Services
        => _factory.Services;

    public HttpClient CreateClient()
    {
        var jwtService = Services.GetRequiredService<TestJwtService>();

        ReadOnlySpan<DelegatingHandler> handlers = [
            new TracingHandler([]),
            new DefaultTestAuthorizationHandler(jwtService),
            new RedirectHandler(),
            new CookieContainerHandler(),
        ];

        for (var i = handlers.Length - 1; i > 0; i--)
        {
            handlers[i - 1].InnerHandler = handlers[i];
        }

        var serverHandler = _factory.Server.CreateHandler(httpContext =>
        {
            httpContext.Connection.RemoteIpAddress = RemoteTestIpAddress;
        });

        handlers[^1].InnerHandler = serverHandler;
        var client = new HttpClient(handlers[0])
        {
            BaseAddress = new Uri("http://register.test/"),
        };

        _clients.Add(client);
        return client;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        if (TestContext.Current.TestOutputHelper is { } output)
        {
            var busHarness = Services.GetRequiredService<ITestHarness>();
            var faults = await busHarness.Consumed.SelectExisting(static m => m.Exception is not null).ToListAsync(CancellationToken.None);

            foreach (var fault in faults)
            {
                output.WriteLine($"### FAULTED MESSAGE ###");
                output.WriteLine($"Message type: {fault.Context.SupportedMessageTypes.First()}");
                output.WriteLine($"Message destination: {fault.Context.DestinationAddress}");
                output.WriteLine(fault.Exception.ToString());
            }

            var receiveFaults = await busHarness.Sent
                .SelectExisting(static m => m.MessageObject is ReceiveFault)
                .Cast<ISentMessage<ReceiveFault>>()
                .ToListAsync(CancellationToken.None);

            foreach (var receiveFault in receiveFaults)
            {
                output.WriteLine($"### RECEIVE FAULT ###");
                output.WriteLine($"Message type: {receiveFault.Context.SupportedMessageTypes.First()}");
                output.WriteLine($"Message destination: {receiveFault.Context.DestinationAddress}");
                output.WriteLine(">>>>> MESSAGE OBJECT:");
                output.WriteLine(receiveFault.MessageObject.ToString() ?? "No details available.");
                output.WriteLine(">>>>> EXCEPTION DETAILS:");
                output.WriteLine(receiveFault.Exception?.ToString() ?? "No exception available.");
            }
        }

        foreach (var client in _clients)
        {
            client.Dispose();
        }

        await _factory.DisposeAsync().WaitAsync(timeout: TimeSpan.FromMinutes(5), timeProvider: TimeProvider.System);
        await ((IAsyncDisposable)_db).DisposeAsync();
    }

    private class DefaultTestAuthorizationHandler(TestJwtService jwt)
        : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            HttpRequestUtils.ApplyIntegrationTestAuthorization(request, jwt);

            return base.SendAsync(request, cancellationToken);
        }
    }
}
