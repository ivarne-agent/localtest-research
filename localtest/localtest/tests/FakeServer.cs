using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Localtest.Tests;

/// <summary>
/// A Kestrel server on a loopback port that records every request (<c>METHOD /path?query</c>, and the <c>Host</c>,
/// <c>Authorization</c> and <c>traceparent</c> it got) and answers with the handler: a fake app or a fake platform the
/// localtest app calls through real sockets.
/// </summary>
internal sealed class FakeServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeServer(WebApplication app, Uri url)
    {
        _app = app;
        Url = url;
    }

    /// <summary>The origin, <c>http://127.0.0.1:{port}</c>, without a trailing slash.</summary>
    public Uri Url { get; }

    public string Origin => Url.GetLeftPart(UriPartial.Authority);

    public ConcurrentQueue<Recorded> Requests { get; } = new();

    public IEnumerable<string> Calls => Requests.Select(r => r.Call);

    public static async Task<FakeServer> Start(Func<HttpContext, Task> handler)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        FakeServer? server = null;
        app.Run(async context =>
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync(context.RequestAborted);
            server!.Requests.Enqueue(new Recorded(
                $"{context.Request.Method} {context.Request.Path}{context.Request.QueryString}",
                context.Request.Host.Value ?? string.Empty,
                context.Request.Headers.Authorization.ToString(),
                body,
                context.Request.Headers["traceparent"].ToString()));
            await handler(context);
        });
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        server = new FakeServer(app, new Uri(address));
        return server;
    }

    /// <summary>A loopback port nothing listens on.</summary>
    public static int ClosedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();

    internal sealed record Recorded(string Call, string Host, string Authorization, string Body, string Traceparent);
}
