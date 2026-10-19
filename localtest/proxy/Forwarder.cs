using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Yarp.ReverseProxy.Forwarder;

namespace LocaltestProxy;

/// <summary>
/// Sends one request to its upstream with YARP's <see cref="IHttpForwarder"/>: an app route if the runner registered
/// one for <c>/{org}/{app}/</c>, otherwise a platform route, otherwise a 404 that says why. Responses stream through
/// unbuffered (server-sent events, downloads), and upgrades (WebSockets) are passed on.
/// </summary>
public sealed partial class Forwarder(IHttpForwarder forwarder, PlatformRoutes platform, AppRoutes apps, ILogger<Forwarder> logger)
{
    private static readonly HttpMessageInvoker Client = new(new SocketsHttpHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
        EnableMultipleHttp2Connections = true,
        ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
        ConnectTimeout = TimeSpan.FromSeconds(5),
        // Containers get new addresses when they are recreated; don't keep connections to the old ones for long.
        PooledConnectionLifetime = TimeSpan.FromSeconds(30),
    });

    // Long enough for PDF generation and big uploads; streams send keep-alives well within it.
    private static readonly ForwarderRequestConfig RequestConfig = new() { ActivityTimeout = TimeSpan.FromMinutes(5) };

    private static readonly HttpTransformer Transformer = new LocaltestTransformer();

    public async Task Forward(HttpContext context)
    {
        var request = context.Request;
        string path = request.Path.Value ?? "/";

        Route? route = null;
        var app = apps.Resolve(path, out bool appWithoutSlash);
        if (app is not null)
        {
            if (appWithoutSlash)
            {
                RedirectToSlash(context);
                return;
            }

            route = new Route($"{app.Org}/{app.App}", app.Upstream);
        }
        else if (PlatformRoutes.IsPrefixWithoutSlash(path))
        {
            RedirectToSlash(context);
            return;
        }
        else
        {
            route = platform.Resolve(path);
        }

        if (route is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("No app is registered for this path. Start it with run-app.sh <app-dir> <port>.\n");
            return;
        }

        await Send(context, route);
    }

    /// <summary>Forwards to the route, or answers 502 when the upstream does not.</summary>
    public async Task Send(HttpContext context, Route route)
    {
        var error = await forwarder.SendAsync(context, route.Destination, Client, RequestConfig, Transformer);
        if (error != ForwarderError.None)
        {
            var exception = context.GetForwarderErrorFeature()?.Exception;
            logger.LogWarning(exception, "{Method} {Path} -> {Route} ({Destination}): {Error}", context.Request.Method, context.Request.Path.Value, route.Name, route.Destination, error);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync($"localtest proxy: {route.Name} ({route.Destination}) did not answer: {error}.\n");
            }
        }
    }

    /// <summary><c>/ttd/app</c> → <c>/ttd/app/</c>, query kept: what nginx did for a prefix location.</summary>
    private static void RedirectToSlash(HttpContext context)
    {
        var request = context.Request;
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = request.PathBase + request.Path + "/" + request.QueryString;
    }

    /// <summary>
    /// YARP's default transform (headers copied, <c>X-Forwarded-For/Proto/Host</c> set) plus the two things the nginx
    /// ingress did: the original <c>Host</c> goes upstream, because the components and apps build their own links from
    /// it, and the <c>Secure</c> flag is removed from cookies, because the browser talks plain http to this origin
    /// while the production services set it.
    /// </summary>
    private sealed partial class LocaltestTransformer : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await Default.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            proxyRequest.Headers.Host = httpContext.Request.Host.Value;
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            bool result = await Default.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            var cookies = httpContext.Response.Headers.SetCookie;
            if (cookies.Count > 0)
            {
                httpContext.Response.Headers.SetCookie = cookies.Select(c => SecureAttribute().Replace(c ?? string.Empty, string.Empty)).ToArray();
            }

            return result;
        }

        [GeneratedRegex(@";\s*secure\s*(?=;|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SecureAttribute();
    }
}
