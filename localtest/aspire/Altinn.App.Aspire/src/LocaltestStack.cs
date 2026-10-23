using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>
/// Adds the manifest's containers to the application model: the default group's when the stack is created, a group's
/// when it is enabled. Each container is one persistent container resource named after its manifest key.
/// </summary>
internal sealed class LocaltestStack
{
    /// <summary>The name of the front door's HTTP endpoint, published on port 8000.</summary>
    public const string HttpEndpoint = "http";

    /// <summary>The name of Postgres's TCP endpoint, published only with <c>PostgresHostPort</c>.</summary>
    public const string TcpEndpoint = "tcp";

    /// <summary>The value of the <c>--with</c> group that enables every optional group.</summary>
    public const string Everything = "everything";

    private readonly IDistributedApplicationBuilder _builder;
    private readonly AltinnLocaltestOptions _options;
    private readonly SortedSet<string> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IResourceBuilder<ContainerResource>> _containers = new(StringComparer.Ordinal);
    private IResourceBuilder<AltinnLocaltestResource>? _parent;

    public LocaltestStack(
        IDistributedApplicationBuilder builder,
        LocaltestManifest manifest,
        AltinnLocaltestOptions options
    )
    {
        _builder = builder;
        _options = options;
        Manifest = manifest;
        DataDirectory = options.DataDirectory;
        CertificateDirectory = Path.Combine(options.DataDirectory, "certs");
    }

    public LocaltestManifest Manifest { get; }

    /// <summary>The package's data directory on the host.</summary>
    public string DataDirectory { get; }

    /// <summary>The host directory holding the shared test certificate.</summary>
    public string CertificateDirectory { get; }

    /// <summary>The enabled optional groups, sorted.</summary>
    public IReadOnlyCollection<string> EnabledGroups => _groups;

    /// <summary>The container resources by manifest name, in the order they were added.</summary>
    public IReadOnlyDictionary<string, IResourceBuilder<ContainerResource>> Containers => _containers;

    /// <summary>The front door: the localtest container.</summary>
    public IResourceBuilder<ContainerResource> FrontDoor => _containers[Topology.FrontDoor];

    /// <summary>The frontend dev server's port on the host, when one is registered with the front door.</summary>
    public int? FrontendDevServerPort { get; set; }

    /// <summary>
    /// The optional groups <see cref="AddGroups" /> takes: the manifest's, less the default and the minimal one.
    /// </summary>
    public IReadOnlyList<string> OptionalGroups =>
        [.. Manifest.GroupNames.Where(g => g != LocaltestManifest.DefaultGroup && g != LocaltestManifest.MinimalGroup)];

    /// <summary>The selected containers as they run now, with the groups enabled so far.</summary>
    public IReadOnlyList<ContainerSpec> Specs() =>
        StackSpec.Resolve(Manifest, _groups, CertificateDirectory, _options.Tag);

    /// <summary>Adds the default group's containers, then the groups of the options.</summary>
    public void Initialize(IResourceBuilder<AltinnLocaltestResource> parent, IEnumerable<string> groups)
    {
        _parent = parent;
        AddContainers();
        AddGroups(groups);
    }

    /// <summary>
    /// Enables groups (<c>everything</c> enables all) and adds the containers they start that are not added yet.
    /// </summary>
    /// <exception cref="ArgumentException">An unknown group, or the default one, which is always on.</exception>
    public void AddGroups(IEnumerable<string> groups)
    {
        foreach (var group in groups)
        {
            if (group == Everything)
            {
                _groups.UnionWith(OptionalGroups);
            }
            else if (OptionalGroups.Contains(group))
            {
                _groups.Add(group);
            }
            else
            {
                throw new ArgumentException(
                    group == LocaltestManifest.DefaultGroup
                        ? $"The {LocaltestManifest.DefaultGroup} group is always on."
                        : $"Unknown localtest group {group} (one of {string.Join(", ", [.. OptionalGroups, Everything])}).",
                    nameof(groups)
                );
            }
        }
        AddContainers();
    }

    private void AddContainers()
    {
        var added = Specs().Where(spec => !_containers.ContainsKey(spec.Name)).ToList();
        foreach (var spec in added)
        {
            _containers.Add(spec.Name, AddContainer(spec));
        }
        // The waits after the batch: a dependency is in the same batch or an earlier one.
        foreach (var spec in added)
        {
            var container = _containers[spec.Name];
            foreach (var dependency in spec.DependsOn)
            {
                container.WaitFor(_containers[dependency]);
            }
            if (spec.Name != Topology.FrontDoor && !spec.Health.Tcp)
            {
                // It may call the public origin from its first request on.
                container.WaitForStart(FrontDoor);
            }
        }
    }

    private IResourceBuilder<ContainerResource> AddContainer(ContainerSpec spec)
    {
        var image = spec.Image;
        var container = _builder
            .AddContainer(spec.Name, image.Image, image.Tag ?? "latest")
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
        // The services__ variables name every container running when the AppHost starts, so the environment is read
        // then: a group enabled after this container was added is in it.
        var name = spec.Name;
        container.WithEnvironment(context =>
        {
            foreach (var (key, value) in Specs().Single(s => s.Name == name).Environment)
            {
                context.EnvironmentVariables[key] = value;
            }
        });
        foreach (var mount in spec.Mounts)
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
        // Aspire gives a container its resource name as network alias, which is the manifest name.
        foreach (var host in spec.ExtraHosts)
        {
            container.WithContainerRuntimeArgs("--add-host", host);
        }
        AddEndpointsAndHealth(container, spec);
        return container;
    }

    private void AddEndpointsAndHealth(IResourceBuilder<ContainerResource> container, ContainerSpec spec)
    {
        if (spec.PublishedPort is { } published)
        {
            // Unproxied, on every interface: the containers reach the public origin through the host gateway, which a
            // loopback-only port would not answer. The health check is the front door's /health through that port.
            container
                .WithHttpEndpoint(port: published, targetPort: spec.Port, name: HttpEndpoint, isProxied: false)
                .WithEndpoint(HttpEndpoint, endpoint => endpoint.TargetHost = "0.0.0.0")
                .WithHttpHealthCheck(spec.Health.Path, endpointName: HttpEndpoint);
            return;
        }
        if (spec.Name == InNetworkHealthCheck.ProbeContainer && _options.PostgresHostPort is { } postgresPort)
        {
            container.WithEndpoint(
                port: postgresPort,
                targetPort: spec.Port,
                scheme: "tcp",
                name: TcpEndpoint,
                isProxied: false
            );
        }
        var key = $"{spec.Name}_in_network_check";
        InNetworkHealthCheck? check = null;
        _builder
            .Services.AddHealthChecks()
            .Add(
                new HealthCheckRegistration(
                    key,
                    services =>
                        check ??= new InNetworkHealthCheck(
                            spec.Health,
                            services.GetRequiredService<ResourceNotificationService>(),
                            services.GetRequiredService<IConfiguration>()
                        ),
                    failureStatus: null,
                    tags: null
                )
            );
        container.WithHealthCheck(key);
    }
}
