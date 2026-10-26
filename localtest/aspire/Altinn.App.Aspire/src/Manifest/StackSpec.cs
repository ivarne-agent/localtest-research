using System.Globalization;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// What every container gets alike, which the runner decides and the manifest does not hold: the same constants as
/// studioctl's loader (<c>internal/cmd/env/localtestv2/containers.go</c>). The public origin's defaults are the
/// manifest's (<see cref="LocaltestManifest.Public" />).
/// </summary>
internal static class Topology
{
    /// <summary>The container whose port is published as the public origin: the localtest app.</summary>
    public const string FrontDoor = "localtest";

    /// <summary>What the extra host names resolve to: the runtime's gateway to the host.</summary>
    public const string HostGateway = "host-gateway";

    /// <summary>The name by which a container reaches the host (the apps, Events' webhooks).</summary>
    public const string HostGatewayName = "host.docker.internal";

    /// <summary>Where every container finds the shared test certificate.</summary>
    public const string CertificateDirectory = "/certs";

    /// <summary>The certificate's file name.</summary>
    public const string CertificateFileName = "localtest-accesstoken.pfx";

    /// <summary>
    /// The path Events, Notifications and Receipt read their signing certificate from (their production default), until
    /// those images link it to <see cref="CertificateDirectory" />; mounted in every container, as studioctl does.
    /// </summary>
    public const string LegacyCertificateFile = "/accesstoken/accesstokencredentials.pfx";

    /// <summary>Arbeidsflate's (the inbox's) subdomain of the public host.</summary>
    public const string ArbeidsflateSubdomain = "af";

    /// <summary>The frontend dev server's subdomain of the public host.</summary>
    public const string FrontendDevServerSubdomain = "app-frontend";

    /// <summary>The PDF service's subdomain of the public host.</summary>
    public const string PdfSubdomain = "pdf";

    /// <summary>The workflow engine's subdomain of the public host.</summary>
    public const string WorkflowEngineSubdomain = "workflow-engine";

    /// <summary>
    /// The front door's list of the public host's subdomains, comma-separated, from the manifest (studioctl's
    /// <c>publicSubdomainsVariable</c>): it routes each listed one and refuses to start on one it does not know.
    /// </summary>
    public const string PublicSubdomainsVariable = "Localtest__PublicSubdomains";

    /// <summary><c>http://host:port</c>, the port always written, as studioctl writes its public URLs.</summary>
    public static string Url(string host, int port) =>
        $"{Uri.UriSchemeHttp}://{host}:{port.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>A container's health check: an HTTP GET of <see cref="Path" /> answering 200, or a TCP connection.</summary>
/// <param name="Name">The container, which is also its host name on the network.</param>
/// <param name="Port">The container's port.</param>
/// <param name="Path">The HTTP path; null for a TCP check.</param>
internal sealed record HealthTarget(string Name, int Port, string? Path)
{
    /// <summary>Whether the check is a TCP connection.</summary>
    public bool Tcp => Path is null;

    /// <summary>
    /// The check as studioctl's golden file describes it: <c>tcp postgres:5432</c> or
    /// <c>http://storage:5010/health</c>.
    /// </summary>
    public override string ToString() =>
        Tcp ? $"tcp {Name}:{Port.ToString(CultureInfo.InvariantCulture)}" : Topology.Url(Name, Port) + Path;
}

/// <summary>What a mount is: a host path, a named volume, or a tmpfs (memory, gone with the container).</summary>
internal enum MountKind
{
    /// <summary>A bind mount of a host path.</summary>
    Bind,

    /// <summary>A named volume.</summary>
    Volume,

    /// <summary>A tmpfs: minimal mode's form of every manifest volume.</summary>
    Tmpfs,
}

/// <summary>A bind mount (host path), a named volume or a tmpfs.</summary>
/// <param name="Kind">The kind of mount.</param>
/// <param name="Source">The volume's name (<see cref="StackSpec.VolumeName" />) or the host path; empty for a tmpfs.</param>
/// <param name="Target">The path in the container.</param>
/// <param name="ReadOnly">Whether it is read-only.</param>
/// <param name="Options">A tmpfs's options (<c>mode=1777</c>, <c>size=1g,mode=1777</c>); empty for the others.</param>
internal sealed record MountSpec(MountKind Kind, string Source, string Target, bool ReadOnly, string Options = "")
{
    /// <summary>The tmpfs's argument of <c>--tmpfs</c>, the same in Podman and Docker: <c>path:options</c>.</summary>
    public string TmpfsArgument => Options.Length == 0 ? Target : Target + ":" + Options;
}

/// <summary>One container as it runs: the manifest's container resolved for one selection of groups.</summary>
/// <param name="Name">The manifest name: the resource name and the network alias.</param>
/// <param name="Image">The image.</param>
/// <param name="Environment">
/// The manifest's <c>envs</c> resolved for the public origin, what the runner adds, and the <c>services__</c> variables,
/// sorted by name.
/// </param>
/// <param name="Mounts">
/// The certificate (directory and legacy file), then the manifest's files sorted by their path in the container, then
/// the named volumes (tmpfs in minimal mode) sorted by id.
/// </param>
/// <param name="Port">The container's port.</param>
/// <param name="Health">The health check.</param>
/// <param name="DependsOn">The containers that must be healthy first.</param>
/// <param name="ExtraHosts">The <c>--add-host</c> entries, <c>name:host-gateway</c>.</param>
/// <param name="Published">Whether the container's port is published as the public origin's: the front door only.</param>
internal sealed record ContainerSpec(
    string Name,
    ImageReference Image,
    IReadOnlyDictionary<string, OriginValue> Environment,
    IReadOnlyList<MountSpec> Mounts,
    int Port,
    HealthTarget Health,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ExtraHosts,
    bool Published
);

/// <summary>Resolves the manifest into the containers one selection of groups runs, as studioctl's loader does.</summary>
internal static class StackSpec
{
    /// <summary>The tag prefix of the research images' pins, which <c>Tag</c> replaces.</summary>
    public const string PinnedTagPrefix = "sha-";

    /// <summary>
    /// The prefix of the package's volumes, <c>altinn-localtest-&lt;id&gt;</c> for the manifest's volume id: the
    /// default parent resource's name and a hyphen, whatever the parent is called. studioctl has its own prefix, so a
    /// reset in one runtime leaves the other's data alone.
    /// </summary>
    public const string VolumePrefix = AltinnLocaltestExtensions.DefaultName + "-";

    /// <summary>The package's name of the manifest's volume <paramref name="id" />.</summary>
    public static string VolumeName(string id) => VolumePrefix + id;

    /// <summary>
    /// The mode bits of every minimal-mode tmpfs: writable for an image's own non-root user (Storage, the front door's
    /// app user), as the named volume was; a tmpfs mount point is root's, 0755, whatever the image's directory was.
    /// studioctl's <c>tmpfsMode</c> (<c>internal/cmd/env/localtestv2/minimal.go</c>).
    /// </summary>
    public const string TmpfsMode = "mode=1777";

    /// <summary>
    /// The options of a minimal-mode tmpfs by manifest volume id, beyond <see cref="TmpfsMode" />: Storage's blobs are
    /// the app's uploads, bounded at 1 GB. studioctl's <c>tmpfsOptions</c>.
    /// </summary>
    private static readonly Dictionary<string, string> _tmpfsOptions = new(StringComparer.Ordinal)
    {
        ["storage-blobs"] = "size=1g," + TmpfsMode,
    };

    /// <summary>
    /// The front door's file of registered app routes; minimal mode sets it empty, so the routes stay in memory.
    /// </summary>
    public const string AppRoutesFileVariable = "Proxy__AppRoutesFile";

    /// <summary>The options of the minimal-mode tmpfs for a manifest volume id.</summary>
    public static string TmpfsOptions(string id) => _tmpfsOptions.GetValueOrDefault(id, TmpfsMode);

    /// <summary>
    /// The containers minimal mode leaves to the front door: every HTTP container of the default group that the minimal
    /// group does not start, in start order. studioctl's <c>emulated</c>, as a set (the variables are sorted by name).
    /// </summary>
    public static IReadOnlyList<string> Emulated(LocaltestManifest manifest)
    {
        var running = manifest.Select([LocaltestManifest.MinimalGroup]);
        return
        [
            .. manifest
                .Select([LocaltestManifest.DefaultGroup])
                .Where(name => manifest.Containers[name].IsHttp && !running.Contains(name)),
        ];
    }

    /// <summary>
    /// The containers of the default group and the named optional groups, in start order: the manifest's
    /// <c>envs</c> resolved for the public origin plus a <c>services__&lt;name&gt;__http__0</c> for every selected HTTP
    /// container, the certificate read-only from <paramref name="certificateDirectory" />, the manifest's files
    /// read-only from <paramref name="filesDirectory" />, the named volumes, the extra hosts (the origin's host names),
    /// and the origin's port on the front door.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="groups">The optional groups that are on.</param>
    /// <param name="certificateDirectory">The host directory holding the shared certificate.</param>
    /// <param name="filesDirectory">The host directory holding the files that ship with the manifest.</param>
    /// <param name="tag">Replaces every <c>sha-</c> pin when set (<c>latest</c>, as studioctl's <c>--latest</c>).</param>
    /// <param name="origin">The public origin; null is the manifest's default.</param>
    public static IReadOnlyList<ContainerSpec> Resolve(
        LocaltestManifest manifest,
        IEnumerable<string> groups,
        string certificateDirectory,
        string filesDirectory,
        string? tag = null,
        PublicOrigin? origin = null
    ) =>
        Resolve(
            manifest,
            [LocaltestManifest.DefaultGroup, .. groups],
            [],
            certificateDirectory,
            filesDirectory,
            tag,
            origin ?? manifest.Origin(),
            minimal: false
        );

    /// <summary>
    /// Minimal mode, as studioctl's <c>env up --minimal</c>: the containers of the minimal group in place of the
    /// default one, in start order, as <see cref="Resolve(LocaltestManifest, IEnumerable{string}, string, string, string?, PublicOrigin?)" />
    /// resolves them but for three things: every emulated container (<see cref="Emulated" />) is announced at the
    /// front door (<c>services__&lt;name&gt;__http__0=http://localtest:8080</c>), every manifest volume is a tmpfs
    /// (<see cref="TmpfsOptions" />), and the front door keeps its app routes in memory
    /// (<see cref="AppRoutesFileVariable" /> empty).
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="certificateDirectory">The host directory holding the shared certificate.</param>
    /// <param name="filesDirectory">The host directory holding the files that ship with the manifest.</param>
    /// <param name="tag">Replaces every <c>sha-</c> pin when set.</param>
    /// <param name="origin">The public origin; null is the manifest's default.</param>
    public static IReadOnlyList<ContainerSpec> ResolveMinimal(
        LocaltestManifest manifest,
        string certificateDirectory,
        string filesDirectory,
        string? tag = null,
        PublicOrigin? origin = null
    ) =>
        Resolve(
            manifest,
            [LocaltestManifest.MinimalGroup],
            Emulated(manifest),
            certificateDirectory,
            filesDirectory,
            tag,
            origin ?? manifest.Origin(),
            minimal: true
        );

    private static IReadOnlyList<ContainerSpec> Resolve(
        LocaltestManifest manifest,
        IEnumerable<string> groups,
        IReadOnlyList<string> emulated,
        string certificateDirectory,
        string filesDirectory,
        string? tag,
        PublicOrigin origin,
        bool minimal
    )
    {
        var names = manifest.Select(groups);
        var services = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => manifest.Containers[n].IsHttp))
        {
            services[$"services__{name}__http__0"] = Topology.Url(name, manifest.Containers[name].HttpPort);
        }
        var frontDoor = Topology.Url(Topology.FrontDoor, manifest.Containers[Topology.FrontDoor].HttpPort);
        foreach (var name in emulated)
        {
            services[$"services__{name}__http__0"] = frontDoor;
        }
        var extraHosts = origin
            .Hosts.Append(Topology.HostGatewayName)
            .Select(host => host + ":" + Topology.HostGateway)
            .ToList();
        return
        [
            .. names.Select(name =>
                Container(
                    manifest,
                    name,
                    services,
                    extraHosts,
                    certificateDirectory,
                    filesDirectory,
                    tag,
                    origin,
                    minimal
                )
            ),
        ];
    }

    private static ContainerSpec Container(
        LocaltestManifest manifest,
        string name,
        IReadOnlyDictionary<string, string> services,
        IReadOnlyList<string> extraHosts,
        string certificateDirectory,
        string filesDirectory,
        string? tag,
        PublicOrigin origin,
        bool minimal
    )
    {
        var c = manifest.Containers[name];
        var environment = new SortedDictionary<string, OriginValue>(StringComparer.Ordinal);
        foreach (var (key, value) in manifest.Envs(name, origin))
        {
            environment[key] = value;
        }
        foreach (var (key, value) in services)
        {
            environment[key] = OriginValue.Literal(value);
        }
        if (name == Topology.FrontDoor)
        {
            environment[Topology.PublicSubdomainsVariable] = OriginValue.Literal(string.Join(",", origin.Subdomains));
            if (minimal)
            {
                environment[AppRoutesFileVariable] = OriginValue.Literal("");
            }
        }
        List<MountSpec> mounts =
        [
            new(MountKind.Bind, certificateDirectory, Topology.CertificateDirectory, true),
            new(
                MountKind.Bind,
                Path.Combine(certificateDirectory, Topology.CertificateFileName),
                Topology.LegacyCertificateFile,
                true
            ),
            .. c
                .Files.OrderBy(f => f.Key, StringComparer.Ordinal)
                .Select(f => new MountSpec(
                    MountKind.Bind,
                    Path.Combine(filesDirectory, f.Value.Replace('/', Path.DirectorySeparatorChar)),
                    f.Key,
                    true
                )),
            .. c
                .Volumes.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v =>
                    minimal
                        ? new MountSpec(MountKind.Tmpfs, "", v.Value, false, TmpfsOptions(v.Key))
                        : new MountSpec(MountKind.Volume, VolumeName(v.Key), v.Value, false)
                ),
        ];
        return new ContainerSpec(
            name,
            ImageOf(c, tag),
            environment,
            mounts,
            c.HttpPort,
            new HealthTarget(name, c.HttpPort, c.IsHttp ? c.HealthCheck : null),
            c.DependsOn,
            extraHosts,
            name == Topology.FrontDoor
        );
    }

    /// <summary>
    /// The image at its tag, or at <paramref name="tag" /> for a <c>sha-</c> pin. A tag that names no one build (not a
    /// pin, no digest: <c>latest</c>, <c>tt02</c>) floats: it is pulled on every start.
    /// </summary>
    public static ImageReference ImageOf(ManifestContainer c, string? tag)
    {
        var resolved = tag is not null && c.Tag.StartsWith(PinnedTagPrefix, StringComparison.Ordinal) ? tag : c.Tag;
        var floating =
            !resolved.StartsWith(PinnedTagPrefix, StringComparison.Ordinal)
            && !resolved.Contains("@sha256:", StringComparison.Ordinal);
        return ImageReference.From(c.Registry, resolved, floating);
    }
}
