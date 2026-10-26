using System.Diagnostics;
using Localtest.Proxy;
using Localtest.Shared.Otlp;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Localtest.Telemetry;

/// <summary>
/// The front door's OpenTelemetry, by the stack's rule (<see cref="LocaltestOtlp"/>, a copy of
/// <c>components/shared/fakes/Otlp/</c>): traces, metrics and logs over OTLP exactly when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (or a per-signal endpoint) is set, with the protocol from
/// <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> and the name from <c>OTEL_SERVICE_NAME</c> (default <see cref="ServiceName"/>).
/// Without the variable nothing is registered: no provider, no listener, no exporter.
/// </summary>
/// <remarks>
/// <para>ASP.NET Core and HttpClient instrumentation: a forwarded request is a server span and, under it, the forwarder's
/// client span, whose <c>traceparent</c> goes on to the destination (the forwarder's handler propagates the current
/// activity). The server span is named after the route (<c>GET storage</c>, <c>GET ttd/app</c>) instead of the catch-all
/// pattern, and the client span carries the route in <c>peer.service</c>.</para>
/// <para>Not traced (<see cref="IsTraced"/>): the monitoring group's routes, <c>/otlp</c> (a span about forwarding OTLP
/// would be exported through the same route, so telemetry would feed on itself) and <c>/grafana</c> (the observer's own
/// page and query traffic, a burst of orphan spans for every dashboard view, with no Grafana spans to join, since Grafana
/// exports none); <c>/health</c> (the runners' probes, every few seconds); and the app-frontend bundle under
/// <c>/app-frontend/</c> (dozens of static files per page). Their child spans are dropped with them: the default sampler
/// follows the parent. Metrics still count every request.</para>
/// <para>Minimal mode: each emulated call is a span of <see cref="Emulated"/> named after its component
/// (<c>Register GET /register/api/v1/parties/{partyId}</c>), with <c>peer.service</c> set to the service name. One
/// process has one resource, so <c>service.name</c> stays <c>localtest</c> for these spans.</para>
/// </remarks>
public static class FrontDoorTelemetry
{
    /// <summary>The front door's <c>service.name</c> unless <c>OTEL_SERVICE_NAME</c> says otherwise.</summary>
    public const string ServiceName = "localtest";

    /// <summary>The source of the emulated services' spans in minimal mode.</summary>
    public const string EmulationSourceName = "Localtest.Emulation";

    /// <summary>The attribute naming the component behind a span: the forwarder's destination, or the emulated service.</summary>
    public const string PeerServiceTag = "peer.service";

    /// <summary>The route a forwarded request took (a service, <c>{org}/{app}</c>, <c>frontend-dev-server</c>), on its server span.</summary>
    public const string RouteTag = "localtest.route";

    public static readonly ActivitySource Emulated = new(EmulationSourceName);

    /// <summary>Registers the providers when the endpoint variable is set in the builder's configuration (the environment).</summary>
    public static void Add(WebApplicationBuilder builder)
    {
        if (!LocaltestOtlp.IsEnabled(builder.Configuration))
        {
            return;
        }

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddSource(EmulationSourceName)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.Filter = IsTraced;
                    options.EnrichWithHttpResponse = NameAfterRoute;
                })
                .AddHttpClientInstrumentation(options => options.EnrichWithHttpRequestMessage = NamePeer))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .AddLocaltestOtlp(builder.Configuration);
    }

    /// <summary>Whether a request to the front door gets a server span; see the remarks for what is left out.</summary>
    public static bool IsTraced(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "/";
        if (path == "/health" || path.StartsWith("/app-frontend/", StringComparison.Ordinal))
        {
            return false;
        }

        return PlatformRoutes.Resolve(path) is not { } route || PlatformRoutes.GroupOf(route.Name) != PlatformRoutes.MonitoringGroup;
    }

    /// <summary>Marks the current request's span with the route the forwarder takes; nothing when it is not traced.</summary>
    public static void MarkRoute(Proxy.Route route) => Activity.Current?.SetTag(RouteTag, route.Name);

    // After the instrumentation named the span from the endpoint's pattern: the forwarder's catch-all ({**path}) says
    // nothing, the route does.
    private static void NameAfterRoute(Activity activity, HttpResponse response)
    {
        if (activity.GetTagItem(RouteTag) is string route)
        {
            activity.DisplayName = $"{response.HttpContext.Request.Method} {route}";
        }
    }

    private static void NamePeer(Activity activity, HttpRequestMessage request)
    {
        if (activity.Parent?.GetTagItem(RouteTag) is string route)
        {
            activity.SetTag(PeerServiceTag, route);
        }
    }
}
