using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>
/// Adds the manifest's components and the front door to the application model: the always-on components when the
/// stack is created, a group's components when it is enabled.
/// </summary>
internal sealed class LocaltestStack
{
    /// <summary>The front door's resource name.</summary>
    public const string FrontDoorName = "frontdoor";

    /// <summary>The name of a component's HTTP endpoint.</summary>
    public const string HttpEndpoint = "http";

    /// <summary>The name of the TCP endpoint of a component without a host port (Postgres, Redis).</summary>
    public const string TcpEndpoint = "tcp";

    private readonly IDistributedApplicationBuilder _builder;
    private readonly ManifestResolver _resolver;
    private readonly SortedSet<string> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IResourceBuilder<ContainerResource>> _components = new(StringComparer.Ordinal);
    private readonly List<FrontDoorRoute> _routes = [];
    private IResourceBuilder<AltinnLocaltestResource>? _parent;
    private IResourceBuilder<YarpResource>? _frontDoor;
    private HashSet<string>? _only;

    public LocaltestStack(
        IDistributedApplicationBuilder builder,
        ManifestResolver resolver,
        string certificateDirectory
    )
    {
        _builder = builder;
        _resolver = resolver;
        CertificateDirectory = certificateDirectory;
    }

    public string CertificateDirectory { get; }

    public IReadOnlyCollection<string> EnabledGroups => _groups;

    public Uri PlatformUri => new(_resolver.Topology.Url(LocaltestTopology.App) + "/");

    public ManifestResolver Resolver => _resolver;

    /// <summary>The component resources by manifest name, in the order they were added.</summary>
    public IReadOnlyDictionary<string, IResourceBuilder<ContainerResource>> Components => _components;

    /// <summary>The front-door routes added so far.</summary>
    public IReadOnlyList<FrontDoorRoute> Routes => _routes;

    /// <summary>The front door.</summary>
    public IResourceBuilder<YarpResource> FrontDoor =>
        _frontDoor ?? throw new InvalidOperationException("The localtest stack is not initialised.");

    /// <summary>Every child resource: the front door and the components.</summary>
    public IEnumerable<IResource> Children => [FrontDoor.Resource, .. _components.Values.Select(c => c.Resource)];

    /// <summary>Adds the front door and the always-on components.</summary>
    public void Initialize(
        IResourceBuilder<AltinnLocaltestResource> parent,
        IEnumerable<string> groups,
        IEnumerable<string> onlyComponents
    )
    {
        _parent = parent;
        _only = DependencyClosure(onlyComponents);
        var port = _resolver.Topology.IngressPort;
        // The containers reach the front door as local.altinn.cloud:8000 through --add-host ...:host-gateway, so the
        // host port must listen on every interface: unproxied (DCP's proxy listens on loopback) with target host
        // 0.0.0.0, which DCP turns into the port's host IP.
        _frontDoor = _builder
            .AddYarp(FrontDoorName)
            .WithHostPort(port)
            .WithEndpoint(
                HttpEndpoint,
                endpoint =>
                {
                    endpoint.IsProxied = false;
                    endpoint.TargetHost = "0.0.0.0";
                }
            )
            .WithHttpHealthCheck(_resolver.Manifest.FrontDoor.HealthPath, endpointName: HttpEndpoint)
            .WithParentRelationship(parent);
        AddComponents(_resolver.Components([]));
        AddGroups(groups);
    }

    /// <summary>Enables groups and what they imply, and adds the components not added yet.</summary>
    public void AddGroups(IEnumerable<string> groups)
    {
        var requested = groups.ToList();
        if (requested.Count == 0)
        {
            return;
        }
        _groups.UnionWith(_resolver.ResolveGroups(requested));
        AddComponents(_resolver.Components(_groups).Where(c => !_components.ContainsKey(c.Name)).ToList());
    }

    /// <summary>The named components and what they depend on, transitively; null for every component.</summary>
    private HashSet<string>? DependencyClosure(IEnumerable<string> names)
    {
        var pending = new Queue<string>(names);
        if (pending.Count == 0)
        {
            return null;
        }
        var closure = new HashSet<string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out var name))
        {
            var component =
                _resolver.Manifest.Component(name)
                ?? throw new ArgumentException($"Unknown localtest component {name}.", nameof(names));
            if (closure.Add(name))
            {
                foreach (var dependency in component.DependsOn)
                {
                    pending.Enqueue(dependency);
                }
            }
        }
        return closure;
    }

    private void AddComponents(IReadOnlyList<ResolvedComponent> components)
    {
        var batch = _only is null ? components : [.. components.Where(c => _only.Contains(c.Name))];
        foreach (var component in batch)
        {
            _components.Add(component.Name, AddContainer(component));
        }
        // Waits after the whole batch, since a component may depend on one later in the manifest.
        foreach (var component in batch)
        {
            var container = _components[component.Name];
            foreach (var dependency in component.DependsOn)
            {
                container.WaitFor(_components[dependency]);
            }
        }
        FrontDoor.WithConfiguration(yarp =>
        {
            foreach (var component in batch.Where(c => c.Routes.Count > 0))
            {
                var container = _components[component.Name];
                var cluster = yarp.AddCluster(container.GetEndpoint(HttpEndpoint));
                foreach (var route in FrontDoorRoutes.For(component, _resolver.Topology))
                {
                    var yarpRoute = yarp.AddRoute(route.Path, cluster)
                        .WithMatchHosts([.. route.Hosts])
                        .WithOrder(route.Order)
                        .WithTransformUseOriginalHostHeader();
                    if (route.RemovePrefix is not null && route.AddPrefix is not null)
                    {
                        yarpRoute
                            .WithTransformPathRemovePrefix(route.RemovePrefix)
                            .WithTransformPathPrefix(route.AddPrefix);
                    }
                    _routes.Add(route);
                }
            }
        });
    }

    /// <summary>The resource name of a component: its manifest name, unless that is the parent's name.</summary>
    public string ResourceName(string component) =>
        string.Equals(component, _parent?.Resource.Name, StringComparison.OrdinalIgnoreCase)
            ? component + "-app"
            : component;

    private IResourceBuilder<ContainerResource> AddContainer(ResolvedComponent component)
    {
        if (component.HostUser)
        {
            throw new NotSupportedException(
                $"Component {component.Name} runs as the developer's user (hostUser), which the Aspire package does not support yet."
            );
        }
        var name = ResourceName(component.Name);
        var image = component.Image;
        var container = _builder
            .AddContainer(name, image.Image, image.Tag ?? "latest")
            .WithParentRelationship(_parent!)
            .WithLifetime(ContainerLifetime.Persistent);
        if (image.Registry is not null)
        {
            container.WithImageRegistry(image.Registry);
        }
        if (image.Sha256 is not null)
        {
            container.WithImageSHA256(image.Sha256);
        }
        if (image.Floating)
        {
            container.WithImagePullPolicy(ImagePullPolicy.Always);
        }
        foreach (var (key, value) in component.Environment)
        {
            container.WithEnvironment(key, value);
        }
        if (component.Command is { } command)
        {
            container.WithArgs([.. command]);
        }
        AddEndpointAndHealth(container, component);
        foreach (var mount in component.Mounts)
        {
            if (mount.IsVolume)
            {
                container.WithVolume(mount.Source, mount.Target, mount.ReadOnly);
            }
            else
            {
                container.WithBindMount(mount.Source, mount.Target, mount.ReadOnly);
            }
        }
        // Every container is reachable by its resource name; the manifest's aliases (and the component name, when the
        // resource name differs) are added to it.
        IEnumerable<string> aliases =
            name == component.Name ? component.NetworkAliases : [component.Name, .. component.NetworkAliases];
        foreach (var alias in aliases.Where(alias => alias != name).Distinct(StringComparer.Ordinal))
        {
            container.WithContainerNetworkAlias(alias);
        }
        foreach (var host in component.ExtraHosts)
        {
            container.WithContainerRuntimeArgs("--add-host", host);
        }
        if (component.ExtraHosts.Count > 0)
        {
            // It calls the platform through the front door from its first request on.
            container.WaitForStart(FrontDoor);
        }
        return container;
    }

    private void AddEndpointAndHealth(IResourceBuilder<ContainerResource> container, ResolvedComponent component)
    {
        if (component.HostPort is { } hostPort)
        {
            container.WithHttpEndpoint(
                port: hostPort,
                targetPort: component.ContainerPort,
                name: HttpEndpoint,
                isProxied: false
            );
            if (component.HealthPath is not null)
            {
                container.WithHttpHealthCheck(component.HealthPath, endpointName: HttpEndpoint);
            }
            return;
        }
        if (component.Routes.Count > 0)
        {
            throw new NotSupportedException($"Component {component.Name} has routes but no host port.");
        }
        // No published port in the manifest: a proxied TCP endpoint on a random loopback port, for the host-side probe.
        container.WithEndpoint(targetPort: component.ContainerPort, scheme: "tcp", name: TcpEndpoint);
        var protocol = ProbeOf(component);
        var key = $"{container.Resource.Name}_protocol_check";
        var endpoint = container.GetEndpoint(TcpEndpoint);
        _builder
            .Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(key, _ => new ProtocolHealthCheck(endpoint, protocol), null, null));
        container.WithHealthCheck(key);
    }

    /// <summary>The host-side probe that replaces a component's container health check.</summary>
    private ProtocolHealthCheck.Protocol ProbeOf(ResolvedComponent component)
    {
        if (component.Name == _resolver.Manifest.Postgres.Component)
        {
            return ProtocolHealthCheck.Protocol.Postgres;
        }
        if (component.Image.Image.EndsWith("/redis", StringComparison.Ordinal))
        {
            return ProtocolHealthCheck.Protocol.Redis;
        }
        throw new NotSupportedException(
            $"Component {component.Name} has no HTTP health path and no host-side probe in the Aspire package."
        );
    }
}
