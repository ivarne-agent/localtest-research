namespace Localtest.Proxy;

/// <summary>
/// Where each platform path goes, in code: every routed service of the stack by its service name. Dedicated hosts
/// first (they own every path), then path prefixes on any other host, the longer prefix of a service inside another's
/// first. Matching is ordinal and case-sensitive, like the nginx locations this replaced. A destination is
/// <c>http://{service}</c> with no port: the forwarder resolves the name through service discovery from
/// <c>services__{service}__http__0</c> (the configuration provider only, so a service without that entry is "not
/// running", see <see cref="ComponentUnavailable"/>).
/// </summary>
public static class PlatformRoutes
{
    /// <summary>
    /// The group of the OpenTelemetry collector and Grafana. Only studioctl runs it; the Aspire package ignores it, since
    /// the Aspire dashboard receives the stack's telemetry there.
    /// </summary>
    public const string MonitoringGroup = "monitoring";

    /// <summary>
    /// The service the <c>/otlp</c> route forwards to: the stack's OTLP receiver, whichever runs (the runner announces it
    /// as <c>services__otlp__http__0</c>), not a container of its own.
    /// </summary>
    public const string OtlpService = "otlp";

    /// <summary>
    /// Subdomain of the public host → the service that owns every path on it, the localtest app's own included. The host
    /// is <c>{subdomain}.{public host}</c> (<see cref="LocaltestSettings.HostOf"/>); a subdomain the runner does not list
    /// is off, and its host is served like the main host.
    /// </summary>
    private static readonly (string Subdomain, string Service)[] HostRoutes =
    [
        (LocaltestSettings.PdfSubdomain, "pdf3"),
        (LocaltestSettings.WorkflowEngineSubdomain, "workflow-engine"),
        (LocaltestSettings.ArbeidsflateSubdomain, "arbeidsflate"),
    ];

    /// <summary>
    /// Path prefixes on the main host, in match order. A prefix matches the path itself and everything below it on a
    /// segment boundary. <c>Rewrite</c> replaces the prefix (the rest of the path is kept); without one the path is passed
    /// on unchanged. Without a rewrite, the bare prefix (<c>/storage</c>) is redirected to its slashed form, as nginx did.
    /// </summary>
    private static readonly PrefixRoute[] Prefixes =
    [
        // Dialogporten's GraphQL host serves /graphql with no path base; APIM rewrites in front of it, so do we. Before
        // /dialogporten, which it is inside. The bare path is the GraphQL endpoint itself, so it is not redirected.
        new("/dialogporten/graphql", "dialogporten-graphql", Rewrite: "/graphql"),
        new("/dialogporten", "dialogporten-webapi"),
        // The adapter's own API is under Storage's prefix (its webhook from Storage's outbox); before /storage.
        new("/storage/dialogporten", "dialogporten-adapter"),
        new("/storage", "storage"),
        new("/register", "register"),
        new("/profile", "profile"),
        new("/authentication", "authentication"),
        new("/authorization", "authorization"),
        // Production Access Management: authorized parties, the PDP's PIP, the apps' instance delegations and refresh.
        new("/accessmanagement", "access-management"),
        // Production Events (events/api/v1/…) and its localtest inspection API (events/api/localtest/…, with an SSE stream).
        new("/events", "events"),
        new("/resourceregistry", "resource-registry"),
        new("/notifications", "notifications"),
        new("/receipt", "receipt"),
        new("/correspondence", "correspondence"),
        // Grafana serves from its sub-path (GF_SERVER_SERVE_FROM_SUB_PATH), so the path is passed on unchanged.
        new("/grafana", "grafana"),
        // OTLP over HTTP for the apps on the host, to the stack's OTLP receiver at its root: /otlp/v1/traces reaches it
        // as /v1/traces. otlp is a role, not a container: the runner announces whichever receiver runs (the monitoring
        // group's otel-collector, minimal mode's otel-files) as services__otlp__http__0. The containers send over gRPC
        // to the receiver directly.
        new("/otlp", OtlpService, Rewrite: ""),
    ];

    /// <summary>
    /// The optional group each service belongs to in today's runtimes (studioctl's manifest groups, the Aspire
    /// package's <c>With…()</c>); a service not listed is in the default stack.
    /// </summary>
    private static readonly Dictionary<string, string> Groups = new(StringComparer.Ordinal)
    {
        ["redis"] = "dialogporten",
        ["dialogporten-webapi"] = "dialogporten",
        ["dialogporten-graphql"] = "dialogporten",
        ["dialogporten-service"] = "dialogporten",
        ["dialogporten-adapter"] = "dialogporten",
        ["receipt"] = "receipt",
        ["correspondence"] = "correspondence",
        ["arbeidsflate"] = "arbeidsflate",
        ["arbeidsflate-bff"] = "arbeidsflate",
        ["grafana"] = MonitoringGroup,
        ["otel-collector"] = MonitoringGroup,
        // Minimal mode always runs its receiver (otel-files), so the only stack without one is the default stack
        // without the monitoring group: the "not running" answer for /otlp names that group.
        [OtlpService] = MonitoringGroup,
        ["victoria-metrics"] = MonitoringGroup,
        ["victoria-traces"] = MonitoringGroup,
        ["victoria-logs"] = MonitoringGroup,
    };

    /// <summary>Every routed service, once, in table order.</summary>
    public static IEnumerable<string> Services
        => HostRoutes.Select(h => h.Service).Concat(Prefixes.Select(p => p.Service)).Distinct(StringComparer.Ordinal);

    /// <summary>The table at the public host, for <c>GET localtest/api/routes</c>: the host routes that are on, then the prefixes.</summary>
    public static IEnumerable<(string? Host, string? Prefix, string Service, string? Rewrite)> Table(LocaltestSettings localtest)
        => HostRoutes.Where(h => localtest.HostOf(h.Subdomain) is not null)
            .Select(h => (localtest.HostOf(h.Subdomain), (string?)null, h.Service, (string?)null))
            .Concat(Prefixes.Select(p => ((string?)null, (string?)p.Prefix, p.Service, p.Rewrite)));

    /// <summary>The path prefix of each service that has one.</summary>
    public static IReadOnlyDictionary<string, string> PrefixOf
        => Prefixes.GroupBy(p => p.Service).ToDictionary(g => g.Key, g => g.First().Prefix, StringComparer.Ordinal);

    /// <summary>The service's optional group, or null for a service of the default stack.</summary>
    public static string? GroupOf(string service) => Groups.GetValueOrDefault(service);

    /// <summary>The destination the forwarder resolves: <c>http://{service}</c>.</summary>
    public static string Destination(string service) => "http://" + service;

    /// <summary>
    /// The service that owns every path on this host name (without port) at the public host, or null for the main host,
    /// any other host, and the host of a subdomain that is off.
    /// </summary>
    public static Route? ResolveHost(string host, LocaltestSettings localtest)
    {
        foreach (var (subdomain, service) in HostRoutes)
        {
            if (localtest.IsHostOf(subdomain, host))
            {
                return Route.Platform(service);
            }
        }

        return null;
    }

    /// <summary>The route for a path on the main host, or null when no platform prefix matches.</summary>
    public static Route? Resolve(string path)
    {
        foreach (var prefix in Prefixes)
        {
            if (prefix.Matches(path))
            {
                return Route.Platform(prefix.Service, prefix.Rewrite is null ? null : new PathRewrite(prefix.Prefix, prefix.Rewrite));
            }
        }

        return null;
    }

    /// <summary>True when a platform prefix owns the path.</summary>
    public static bool IsPlatformPath(string path) => Prefixes.Any(p => p.Matches(path));

    /// <summary>
    /// True when the path is a platform prefix without its trailing slash (<c>/storage</c>), which nginx redirected. Not
    /// for a rewritten prefix (<c>/dialogporten/graphql</c> is the GraphQL endpoint).
    /// </summary>
    public static bool IsPrefixWithoutSlash(string path)
        => Prefixes.FirstOrDefault(p => p.Matches(path)) is { Rewrite: null } prefix && string.Equals(prefix.Prefix, path, StringComparison.Ordinal);

    private sealed record PrefixRoute(string Prefix, string Service, string? Rewrite = null)
    {
        public bool Matches(string path)
            => path.StartsWith(Prefix, StringComparison.Ordinal) && (path.Length == Prefix.Length || path[Prefix.Length] == '/');
    }
}

/// <summary>
/// A route that is forwarded under another path than the public one: the prefix is replaced and the rest of the path
/// kept, so <c>/dialogporten/graphql/x</c> reaches the upstream as <c>/graphql/x</c>.
/// </summary>
public sealed record PathRewrite(string Prefix, string Replacement)
{
    public PathString Apply(PathString path)
        => path.StartsWithSegments(Prefix, StringComparison.Ordinal, out var rest) ? new PathString(Replacement).Add(rest) : path;
}

/// <summary>What a route forwards to.</summary>
public enum RouteKind
{
    /// <summary>A platform service, by name through service discovery.</summary>
    Platform,

    /// <summary>An app the runner registered, at the address it gave.</summary>
    App,

    /// <summary>The frontend dev server the runner registered, at the address it gave.</summary>
    FrontendDevServer,
}

/// <summary>
/// A resolved destination: a name for logs and error pages (the service, <c>{org}/{app}</c>, or
/// <c>frontend-dev-server</c>), the base URL the request path is appended to, and its kind. An app's cookies still lose
/// <c>Secure</c>, see <see cref="Forwarder"/>.
/// </summary>
public sealed record Route(string Name, string Destination, RouteKind Kind, PathRewrite? Rewrite = null)
{
    public static Route Platform(string service, PathRewrite? rewrite = null)
        => new(service, PlatformRoutes.Destination(service), RouteKind.Platform, rewrite);
}
