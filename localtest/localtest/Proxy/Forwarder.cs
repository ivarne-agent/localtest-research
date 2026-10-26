using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using Localtest.Telemetry;
using Microsoft.Extensions.ServiceDiscovery.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Localtest.Proxy;

/// <summary>
/// Sends one request to its destination with YARP's <see cref="IHttpForwarder"/>: an app route if the runner registered
/// one for <c>/{org}/{app}/</c>, otherwise a platform route, otherwise a 404 that says why. Platform destinations are
/// service names (<c>http://storage</c>), resolved per request by service discovery's handler around the forwarder's
/// own; app and dev-server routes are addresses the runner gave (<c>http://host.docker.internal:5005</c>) and go
/// through the plain handler, since a name the configuration does not know does not resolve. Responses stream through
/// unbuffered (server-sent events, downloads), and upgrades (WebSockets) are passed on. A service without an address
/// and a destination that does not answer are 503 problems (<see cref="ComponentUnavailable"/>).
/// </summary>
public sealed partial class Forwarder
{
    // Long enough for PDF generation and big uploads; streams send keep-alives well within it.
    private static readonly ForwarderRequestConfig RequestConfig = new() { ActivityTimeout = TimeSpan.FromMinutes(5) };

    private static readonly HttpTransformer PlatformTransformer = new LocaltestTransformer(stripSecure: false, rewrite: null);
    private static readonly HttpTransformer AppTransformer = new LocaltestTransformer(stripSecure: true, rewrite: null);

    private readonly IHttpForwarder _forwarder;
    private readonly AppRoutes _apps;
    private readonly ILogger<Forwarder> _logger;
    private readonly HttpMessageInvoker _direct;
    private readonly HttpMessageInvoker _resolving;

    public Forwarder(IHttpForwarder forwarder, AppRoutes apps, IServiceDiscoveryHttpMessageHandlerFactory discovery, ILogger<Forwarder> logger)
    {
        _forwarder = forwarder;
        _apps = apps;
        _logger = logger;
        _direct = new HttpMessageInvoker(CreateHandler());
        _resolving = new HttpMessageInvoker(discovery.CreateHandler(CreateHandler()));
    }

    public async Task Forward(HttpContext context)
    {
        var request = context.Request;
        string path = request.Path.Value ?? "/";

        Route? route = null;
        var app = _apps.Resolve(path, out bool appWithoutSlash);
        if (app is not null)
        {
            if (appWithoutSlash)
            {
                RedirectToSlash(context);
                return;
            }

            route = new Route($"{app.Org}/{app.App}", app.Url, RouteKind.App);
        }
        else if (PlatformRoutes.IsPrefixWithoutSlash(path))
        {
            RedirectToSlash(context);
            return;
        }
        else
        {
            route = PlatformRoutes.Resolve(path);
        }

        if (route is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("No app is registered for this path. Start it with studioctl run or an Aspire AppHost (or, with this app as the front door, run-app.sh <app-dir> <port>).\n");
            return;
        }

        await Send(context, route);
    }

    /// <summary>Forwards to the route, or answers 503 when its service has no address or the destination does not answer.</summary>
    public async Task Send(HttpContext context, Route route)
    {
        var transformer = route switch
        {
            { Rewrite: { } rewrite } => new LocaltestTransformer(stripSecure: route.Kind == RouteKind.App, rewrite),
            { Kind: RouteKind.App } => AppTransformer,
            _ => PlatformTransformer,
        };
        var invoker = route.Kind == RouteKind.Platform ? _resolving : _direct;
        FrontDoorTelemetry.MarkRoute(route);
        var error = await _forwarder.SendAsync(context, route.Destination, invoker, RequestConfig, transformer);
        if (error == ForwarderError.None)
        {
            return;
        }

        var exception = context.GetForwarderErrorFeature()?.Exception;
        var path = context.Request.Path.Value ?? "/";
        if (route.Kind == RouteKind.Platform && ServiceCalls.NoAddress(exception))
        {
            _logger.LogDebug("{Method} {Path} -> {Route}: no address (services__{Service}__http__0 is not set)", context.Request.Method, path, route.Name, route.Name);
            if (!context.Response.HasStarted)
            {
                await ComponentUnavailable.Write(context, ComponentUnavailable.NotRunning(route.Name, path));
            }

            return;
        }

        // A client that goes away (an event stream closed, a Cypress navigation) is normal, not worth a warning.
        var canceled = error is ForwarderError.RequestCanceled or ForwarderError.RequestBodyCanceled or ForwarderError.ResponseBodyCanceled or ForwarderError.UpgradeRequestCanceled or ForwarderError.UpgradeResponseCanceled;
        _logger.Log(canceled ? LogLevel.Debug : LogLevel.Warning, exception, "{Method} {Path} -> {Route} ({Destination}): {Error}", context.Request.Method, path, route.Name, route.Destination, error);
        if (!canceled && !context.Response.HasStarted)
        {
            var detail = exception is null ? error.ToString() : $"{error}: {exception.Message}";
            await ComponentUnavailable.Write(context, ComponentUnavailable.NotAnswering(route, path, route.Destination, detail));
        }
    }

    private static SocketsHttpHandler CreateHandler() => new()
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
    };

    /// <summary><c>/ttd/app</c> → <c>/ttd/app/</c>, query kept: what nginx did for a prefix location.</summary>
    private static void RedirectToSlash(HttpContext context)
    {
        var request = context.Request;
        context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
        context.Response.Headers.Location = request.PathBase + request.Path + "/" + request.QueryString;
    }

    /// <summary>
    /// YARP's default transform (headers copied, <c>X-Forwarded-For/Proto/Host</c> set) plus what the nginx ingress did:
    /// the original <c>Host</c> goes upstream, because the components and apps build their own links from it (service
    /// discovery keeps a <c>Host</c> that is set). A route with a <see cref="PathRewrite"/> is forwarded under its
    /// replacement path. The platform components set no <c>Secure</c> cookie over plain http, so their
    /// <c>Set-Cookie</c> headers pass unchanged; on an app's route (<paramref name="stripSecure"/>) <c>Secure</c> is still
    /// removed, because app-lib's <c>keepAlive</c> sets the runtime cookie <c>Secure</c> in every environment
    /// (localtest/reviews/cookies.md; task 22 removes this).
    /// </summary>
    private sealed partial class LocaltestTransformer(bool stripSecure, PathRewrite? rewrite) : HttpTransformer
    {
        public override async ValueTask TransformRequestAsync(HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
        {
            await Default.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);
            proxyRequest.Headers.Host = httpContext.Request.Host.Value;
            if (rewrite is not null)
            {
                proxyRequest.RequestUri = RequestUtilities.MakeDestinationAddress(destinationPrefix, rewrite.Apply(httpContext.Request.Path), httpContext.Request.QueryString);
            }
        }

        public override async ValueTask<bool> TransformResponseAsync(HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            bool result = await Default.TransformResponseAsync(httpContext, proxyResponse, cancellationToken);
            var cookies = httpContext.Response.Headers.SetCookie;
            if (stripSecure && cookies.Count > 0)
            {
                httpContext.Response.Headers.SetCookie = cookies.Select(c => SecureAttribute().Replace(c ?? string.Empty, string.Empty)).ToArray();
            }

            return result;
        }

        [GeneratedRegex(@";\s*secure\s*(?=;|$)", RegexOptions.IgnoreCase)]
        private static partial Regex SecureAttribute();
    }
}
