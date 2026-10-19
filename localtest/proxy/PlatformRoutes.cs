namespace LocaltestProxy;

/// <summary>
/// Where each platform path goes. Hosts first (the PDF generator and the workflow engine have their own host names, as
/// in production and studioctl), then path prefixes on the main host. Matching is ordinal and case-sensitive, like the
/// nginx locations it replaces. Upstream addresses come from configuration (<c>Upstreams:{name}</c>, for example the
/// environment variable <c>Upstreams__storage=http://storage:5010</c>), so a component can be pointed elsewhere.
/// </summary>
public sealed class PlatformRoutes
{
    private static readonly Dictionary<string, string> DefaultUpstreams = new()
    {
        ["register"] = "http://register:5020",
        ["authentication"] = "http://authentication:5101",
        ["authorization"] = "http://authorization:5070",
        ["access-management"] = "http://access-management:5080",
        ["storage"] = "http://storage:5010",
        ["events"] = "http://events:5080",
        ["pdf3"] = "http://pdf3:5031",
        ["workflow-engine"] = "http://workflow-engine:9090",
    };

    /// <summary>Host name (without port) → upstream name, for every path on that host.</summary>
    private static readonly (string Host, string Upstream)[] Hosts =
    [
        ("pdf.local.altinn.cloud", "pdf3"),
        ("workflow-engine.local.altinn.cloud", "workflow-engine"),
    ];

    /// <summary>Path prefix → upstream name on the main host. The prefix is passed on unchanged.</summary>
    private static readonly (string Prefix, string Upstream)[] Prefixes =
    [
        ("/register/", "register"),
        ("/profile/", "register"),
        ("/authentication/", "authentication"),
        ("/Home/", "authentication"),
        ("/authorization/", "authorization"),
        // Production Access Management: authorized parties, the PDP's PIP, the apps' instance delegations and the app
        // runner's refresh.
        ("/accessmanagement/", "access-management"),
        ("/storage/", "storage"),
        // Production Events (events/api/v1/…) and its localtest inspection API (events/api/localtest/…, with an SSE stream).
        ("/events/", "events"),
    ];

    /// <summary>Exact paths → upstream name: the start page (test-user select) and its <c>/Home</c> route.</summary>
    private static readonly (string Path, string Upstream)[] ExactPaths =
    [
        ("/", "authentication"),
        ("/Home", "authentication"),
    ];

    private readonly Dictionary<string, string> _upstreams;

    private PlatformRoutes(Dictionary<string, string> upstreams) => _upstreams = upstreams;

    public static PlatformRoutes FromConfiguration(IConfiguration configuration)
    {
        var upstreams = new Dictionary<string, string>(DefaultUpstreams, StringComparer.OrdinalIgnoreCase);
        foreach (var setting in configuration.GetSection("Upstreams").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(setting.Value))
            {
                upstreams[setting.Key] = setting.Value;
            }
        }

        return new PlatformRoutes(upstreams);
    }

    public IReadOnlyDictionary<string, string> Upstreams => _upstreams;

    /// <summary>The upstream that owns every path on this host name (without port), or null for the main host.</summary>
    public Route? ResolveHost(string host)
    {
        foreach (var (routeHost, upstream) in Hosts)
        {
            if (string.Equals(host, routeHost, StringComparison.OrdinalIgnoreCase))
            {
                return new Route(upstream, _upstreams[upstream]);
            }
        }

        return null;
    }

    /// <summary>The upstream for a path on the main host, or null when no platform route matches.</summary>
    public Route? Resolve(string path)
    {
        foreach (var (exact, upstream) in ExactPaths)
        {
            if (string.Equals(path, exact, StringComparison.Ordinal))
            {
                return new Route(upstream, _upstreams[upstream]);
            }
        }

        foreach (var (prefix, upstream) in Prefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return new Route(upstream, _upstreams[upstream]);
            }
        }

        return null;
    }

    /// <summary>
    /// True when the path is a platform prefix without its trailing slash (<c>/storage</c>) and not an exact route of its
    /// own (<c>/Home</c>), which nginx redirected.
    /// </summary>
    public static bool IsPrefixWithoutSlash(string path)
        => !ExactPaths.Any(e => string.Equals(e.Path, path, StringComparison.Ordinal))
            && Prefixes.Any(p => string.Equals(p.Prefix, path + "/", StringComparison.Ordinal));
}

/// <summary>A resolved destination: a name for logs and error pages, and the base URL the request path is appended to.</summary>
public sealed record Route(string Name, string Destination);
