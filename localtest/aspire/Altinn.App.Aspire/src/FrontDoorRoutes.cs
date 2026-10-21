using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire;

/// <summary>One YARP route the front door gets for a manifest route.</summary>
/// <param name="Component">The component the route forwards to (its YARP cluster).</param>
/// <param name="Hosts">The hosts the route matches.</param>
/// <param name="Path">The YARP path pattern.</param>
/// <param name="Order">The YARP order: lower wins.</param>
/// <param name="RemovePrefix">The prefix the rewrite removes, or null.</param>
/// <param name="AddPrefix">The path the rewrite puts in its place, or null.</param>
internal sealed record FrontDoorRoute(
    string Component,
    IReadOnlyList<string> Hosts,
    string Path,
    int Order,
    string? RemovePrefix,
    string? AddPrefix
);

/// <summary>
/// Turns manifest routes into YARP routes with studioctl-server's matching (<c>studioctl-server/Proxy</c>,
/// <c>BoundTopologyIndex</c>): by host, then the longest path prefix on segment boundaries, the whole-host routes last;
/// a rewrite replaces the prefix and keeps the rest of the path.
/// </summary>
internal static class FrontDoorRoutes
{
    /// <summary>The catch-all YARP appends to a prefix; it also matches the bare prefix.</summary>
    public const string CatchAll = "{**catch-all}";

    private const char PathSeparator = '/';

    /// <summary>The order of the whole-host routes, after every prefixed one.</summary>
    public const int HostOnlyOrder = 0;

    /// <summary>
    /// The routes for a component's manifest routes. A route on the public origin's host also matches
    /// <c>localhost</c> and <c>127.0.0.1</c>: studioctl-server matches a request on another host against the public
    /// origin's host, which is how a request to <c>http://localhost:8000/</c> and the front door's own health check
    /// reach the components.
    /// </summary>
    public static IReadOnlyList<FrontDoorRoute> For(ResolvedComponent component, LocaltestTopology topology)
    {
        var canonicalHost = topology.Host(LocaltestTopology.App);
        return
        [
            .. component.Routes.Select(route => new FrontDoorRoute(
                component.Name,
                string.Equals(route.Host, canonicalHost, StringComparison.OrdinalIgnoreCase)
                    ? [route.Host, "localhost", "127.0.0.1"]
                    : [route.Host],
                route.Prefix.Length == 0
                    ? PathSeparator + CatchAll
                    : route.Prefix.TrimEnd(PathSeparator) + PathSeparator + CatchAll,
                // Longer prefixes first; YARP's own precedence would order literal segments the same way, but the
                // explicit order states it.
                route.Prefix.Length == 0
                    ? HostOnlyOrder
                    : -route.Prefix.Length,
                route.Rewrite is null ? null : route.Prefix,
                route.Rewrite
            )),
        ];
    }
}
