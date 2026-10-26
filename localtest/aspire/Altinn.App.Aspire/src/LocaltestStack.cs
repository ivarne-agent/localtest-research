using Altinn.App.Aspire.Manifest;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>
/// Adds the manifest's containers to the application model: the default group's when the stack is created, a group's
/// when it is enabled. Each container is one container resource named after its manifest key: persistent, with the
/// package's named volumes; in minimal mode the minimal group's, for the session, with a tmpfs for each volume.
/// </summary>
internal sealed class LocaltestStack
{
    /// <summary>The name of the front door's HTTP endpoint, published on the public origin's port.</summary>
    public const string HttpEndpoint = "http";

    /// <summary>The name of Postgres's TCP endpoint, published only with <c>PostgresHostPort</c>.</summary>
    public const string TcpEndpoint = "tcp";

    /// <summary>The value of the <c>--with</c> group that enables every optional group.</summary>
    public const string Everything = "everything";

    private readonly IDistributedApplicationBuilder _builder;
    private readonly string? _tag;
    private readonly int? _postgresHostPort;
    private readonly SortedSet<string> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IResourceBuilder<ContainerResource>> _containers = new(StringComparer.Ordinal);
    private IResourceBuilder<AltinnLocaltestResourceBase>? _parent;

    /// <summary>A stack of the default group and the optional groups.</summary>
    public LocaltestStack(
        IDistributedApplicationBuilder builder,
        LocaltestManifest manifest,
        AltinnLocaltestOptions options
    )
        : this(
            builder,
            manifest,
            options.DataDirectory,
            options.Tag,
            options.PostgresHostPort,
            OriginOf(manifest, options.PublicHost, options.PublicPort),
            minimal: false
        ) { }

    /// <summary>A stack in minimal mode.</summary>
    public LocaltestStack(
        IDistributedApplicationBuilder builder,
        LocaltestManifest manifest,
        AltinnLocaltestMinimalOptions options
    )
        : this(
            builder,
            manifest,
            options.DataDirectory,
            options.Tag,
            options.PostgresHostPort,
            OriginOf(manifest, options.PublicHost, options.PublicPort),
            minimal: true
        ) { }

    private LocaltestStack(
        IDistributedApplicationBuilder builder,
        LocaltestManifest manifest,
        string dataDirectory,
        string? tag,
        int? postgresHostPort,
        PublicOrigin origin,
        bool minimal
    )
    {
        _builder = builder;
        Origin = origin;
        _tag = tag;
        _postgresHostPort = postgresHostPort;
        Manifest = manifest;
        Minimal = minimal;
        DataDirectory = dataDirectory;
        CertificateDirectory = Path.Combine(dataDirectory, "certs");
        FilesDirectory = Path.Combine(dataDirectory, "files");
    }

    public LocaltestManifest Manifest { get; }

    /// <summary>The public origin: the options' host and port, the manifest's defaults, a null port Aspire allocates.</summary>
    public PublicOrigin Origin { get; }

    /// <summary>
    /// The front door's port as the host sees it: the published port, which Aspire knows once it has allocated the
    /// endpoints, before any container or app starts. Every value derived from a public origin whose port Aspire
    /// allocates refers to it.
    /// </summary>
    public EndpointReferenceExpression PublicPortExpression =>
        FrontDoor.GetEndpoint(HttpEndpoint, KnownNetworkIdentifiers.LocalhostNetwork).Property(EndpointProperty.Port);

    /// <summary>
    /// The public origin with a trailing slash. With a port Aspire allocates, it is known once the endpoints are
    /// allocated (when the AppHost runs); before that it throws.
    /// </summary>
    public Uri PlatformUri =>
        new(
            (
                Origin.Port is { } port
                    ? Origin.Url.Render(port)
                    : Origin.Url.Render(
                        FrontDoor.GetEndpoint(HttpEndpoint, KnownNetworkIdentifiers.LocalhostNetwork).Port
                    )
            ) + "/"
        );

    /// <summary>A value derived from the public origin, as an environment variable's value: a string or an expression.</summary>
    public object EnvironmentValue(OriginValue value) => value.ToEnvironmentValue(PublicPortExpression);

    /// <summary>
    /// Whether the stack runs in minimal mode: the minimal group, session containers, tmpfs, no optional groups.
    /// </summary>
    public bool Minimal { get; }

    /// <summary>The package's data directory on the host.</summary>
    public string DataDirectory { get; }

    /// <summary>The host directory holding the shared test certificate.</summary>
    public string CertificateDirectory { get; }

    /// <summary>The host directory holding the files that ship with the manifest, which containers mount read-only.</summary>
    public string FilesDirectory { get; }

    /// <summary>The enabled optional groups, sorted.</summary>
    public IReadOnlyCollection<string> EnabledGroups => _groups;

    /// <summary>The container resources by manifest name, in the order they were added.</summary>
    public IReadOnlyDictionary<string, IResourceBuilder<ContainerResource>> Containers => _containers;

    /// <summary>The front door: the localtest container.</summary>
    public IResourceBuilder<ContainerResource> FrontDoor => _containers[Topology.FrontDoor];

    /// <summary>The frontend dev server's port on the host, when one is registered with the front door.</summary>
    public int? FrontendDevServerPort { get; set; }

    /// <summary>
    /// The optional groups <see cref="AddGroups" /> takes: the manifest's, less the default and the minimal one and the
    /// one the package ignores (<see cref="LocaltestManifest.MonitoringGroup" />).
    /// </summary>
    public IReadOnlyList<string> OptionalGroups =>
        [
            .. Manifest.GroupNames.Where(g =>
                g
                    is not (
                        LocaltestManifest.DefaultGroup
                        or LocaltestManifest.MinimalGroup
                        or LocaltestManifest.MonitoringGroup
                    )
            ),
        ];

    /// <summary>The selected containers as they run now, with the groups enabled so far.</summary>
    public IReadOnlyList<ContainerSpec> Specs() =>
        Minimal
            ? StackSpec.ResolveMinimal(Manifest, CertificateDirectory, FilesDirectory, _tag, Origin)
            : StackSpec.Resolve(Manifest, _groups, CertificateDirectory, FilesDirectory, _tag, Origin);

    /// <summary>Adds the default group's containers (the minimal group's in minimal mode), then the given groups.</summary>
    public void Initialize(IResourceBuilder<AltinnLocaltestResourceBase> parent, IEnumerable<string> groups)
    {
        _parent = parent;
        AddContainers();
        AddGroups(groups);
    }

    /// <summary>
    /// Enables groups (<c>everything</c> enables all) and adds the containers they start that are not added yet.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// An unknown group, the default one, which is always on, or the monitoring one, which the package ignores.
    /// </exception>
    /// <exception cref="InvalidOperationException">A group in minimal mode, which has none.</exception>
    public void AddGroups(IEnumerable<string> groups)
    {
        foreach (var group in groups)
        {
            if (Minimal)
            {
                // Unreachable through the public API, which offers no group method on the minimal resource.
                throw new InvalidOperationException(
                    $"Minimal mode has no optional groups ({group}): they need the real Register, Authentication and "
                        + "others. Use AddAltinnLocaltest() for the full stack."
                );
            }
            if (group == Everything)
            {
                _groups.UnionWith(OptionalGroups);
            }
            else if (OptionalGroups.Contains(group))
            {
                _groups.Add(group);
            }
            else if (group == LocaltestManifest.MonitoringGroup)
            {
                throw new ArgumentException(
                    $"The {LocaltestManifest.MonitoringGroup} group (studioctl's OpenTelemetry collector and Grafana) is "
                        + "not run by Aspire: the Aspire dashboard shows the containers' and the apps' traces, metrics "
                        + "and logs.",
                    nameof(groups)
                );
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

    /// <summary>The public origin of the options, checked: a host name, a port from 0 (allocated) to 65535.</summary>
    private static PublicOrigin OriginOf(LocaltestManifest manifest, string? host, int? port)
    {
        if (host is not null && PublicOrigin.HostFault(host) is { } fault)
        {
            throw new ArgumentException($"PublicHost: {fault}.", nameof(host));
        }
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "PublicPort: 0 (allocated) to 65535.");
        }
        return manifest.Origin(host, port);
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
            // Minimal mode's containers live for the AppHost's run: a persistent one would keep its tmpfs across runs
            // while it kept running, so the clean slate would depend on whether it was replaced.
            .WithLifetime(Minimal ? ContainerLifetime.Session : ContainerLifetime.Persistent);
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
        // Its telemetry goes to the dashboard: Aspire's OTLP variables first, then the package's defaults over them
        // (DashboardTelemetry), then the manifest's envs, which win over both.
        container.WithOtlpExporter();
        // The services__ variables name every container running when the AppHost starts, so the environment is read
        // then: a group enabled after this container was added is in it.
        var name = spec.Name;
        container.WithEnvironment(context =>
        {
            DashboardTelemetry.Apply(name, context.EnvironmentVariables);
            foreach (var (key, value) in Specs().Single(s => s.Name == name).Environment)
            {
                context.EnvironmentVariables[key] = EnvironmentValue(value);
            }
            // The dashboard is in the model by now (Aspire adds it before the start), so the engine links its traces
            // there; its own envs win.
            if (
                name == DashboardTraceLinks.WorkflowEngine
                && DashboardTraceLinks.TraceUrlTemplate(_builder.Resources) is { } template
            )
            {
                context.EnvironmentVariables.TryAdd(DashboardTraceLinks.TraceUrlTemplateVariable, template);
            }
        });
        foreach (var mount in spec.Mounts)
        {
            switch (mount.Kind)
            {
                case MountKind.Volume:
                    container.WithVolume(mount.Source, mount.Target, mount.ReadOnly);
                    break;
                case MountKind.Tmpfs:
                    // Aspire has no tmpfs mount; the runtime's own flag, the same in Podman and Docker. Over a path an
                    // image declares a VOLUME for (Postgres's), it also keeps the runtime from creating an anonymous
                    // volume there.
                    container.WithContainerRuntimeArgs("--tmpfs", mount.TmpfsArgument);
                    break;
                default:
                    container.WithBindMount(mount.Source, mount.Target, mount.ReadOnly);
                    break;
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
        if (spec.Published)
        {
            // Unproxied, on every interface: the containers reach the public origin through the host gateway, which a
            // loopback-only port would not answer. The health check is the front door's /health through that port.
            // Without a port (PublicPort = 0) Aspire allocates a free one for the unproxied endpoint while it prepares
            // the run, before it evaluates any environment.
            container
                .WithHttpEndpoint(port: Origin.Port, targetPort: spec.Port, name: HttpEndpoint, isProxied: false)
                .WithEndpoint(HttpEndpoint, endpoint => endpoint.TargetHost = "0.0.0.0")
                .WithHttpHealthCheck(spec.Health.Path, endpointName: HttpEndpoint);
            return;
        }
        if (spec.Name == InNetworkHealthCheck.ProbeContainer && _postgresHostPort is { } postgresPort)
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
