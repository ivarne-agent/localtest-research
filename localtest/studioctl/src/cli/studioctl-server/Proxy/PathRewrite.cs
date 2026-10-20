using Altinn.Studio.EnvTopology;

namespace Altinn.Studio.StudioctlServer.Proxy;

/// <summary>
/// A route that is forwarded under another path than the public one: the matched prefix is replaced and the rest of the
/// path is kept, so <c>/dialogporten/graphql/foo</c> with the prefix <c>/dialogporten/graphql</c> and the replacement
/// <c>/graphql</c> reaches the upstream as <c>/graphql/foo</c>. APIM does the same in front of Dialogporten's GraphQL host,
/// which serves <c>/graphql</c> with no path base. The replacement is the route's <c>rewritePrefix</c> metadata.
/// </summary>
internal readonly record struct PathRewrite(PathString Prefix, PathString Replacement)
{
    public static PathRewrite? From(BoundTopologyRoute route)
    {
        if (string.IsNullOrEmpty(route.Match.PathPrefix))
            return null;

        var replacement = route.Metadata.FirstOrDefault(static entry =>
            string.Equals(entry.Key, BoundTopologyMetadataKeys.RewritePrefix, StringComparison.Ordinal)
        );
        return replacement is null ? null : new PathRewrite(route.Match.PathPrefix, replacement.Value);
    }

    /// <summary>The path the upstream gets; a path outside the prefix (which the route would not have matched) is kept.</summary>
    public PathString Apply(PathString path) =>
        path.StartsWithSegments(Prefix, StringComparison.OrdinalIgnoreCase, out var rest)
            ? Replacement.Add(rest)
            : path;
}
