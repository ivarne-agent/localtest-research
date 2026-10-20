#nullable enable

namespace Altinn.Studio.EnvTopology;

public static class BoundTopologyMetadataKeys
{
    public const string AppId = "appId";
    public const string ContainerId = "containerId";
    public const string Description = "description";

    /// <summary>
    /// The optional component group the route's component belongs to (localtest-v2). A disabled route with a group is
    /// a component whose group is off.
    /// </summary>
    public const string Group = "group";
    public const string HostPort = "hostPort";
    public const string Name = "name";
    public const string ProcessId = "processId";

    /// <summary>
    /// On a route with a path prefix: the path the forwarded request gets in place of that prefix (the rest of the path
    /// is kept), for an upstream that serves the component under another path than the public one.
    /// </summary>
    public const string RewritePrefix = "rewritePrefix";
    public const string Source = "source";
}
