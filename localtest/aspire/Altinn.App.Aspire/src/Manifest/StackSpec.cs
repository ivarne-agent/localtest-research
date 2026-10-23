using System.Globalization;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// What every container gets alike, which the runner decides and the manifest does not hold: the same constants as
/// studioctl's loader (<c>internal/cmd/env/localtestv2/containers.go</c>) and topology (port 8000).
/// </summary>
internal static class Topology
{
    /// <summary>The public origin's host.</summary>
    public const string PublicHost = "local.altinn.cloud";

    /// <summary>The public origin's port, published from the front door.</summary>
    public const int IngressPort = 8000;

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

    /// <summary>Arbeidsflate's (the inbox's) public host.</summary>
    public const string ArbeidsflateHost = "af." + PublicHost;

    /// <summary>The frontend dev server's public host.</summary>
    public const string FrontendDevServerHost = "app-frontend.local.altinn.cloud";

    /// <summary>The public origin with a trailing slash.</summary>
    public static Uri PlatformUri { get; } = new(Url(PublicHost, IngressPort) + "/");

    /// <summary>
    /// The host names every container resolves to the host gateway, in studioctl's order: the public hosts, where the
    /// front door's port is published, and <see cref="HostGatewayName" />.
    /// </summary>
    public static IReadOnlyList<string> ExtraHostNames { get; } =
    [
        PublicHost,
        "pdf." + PublicHost,
        "workflow-engine." + PublicHost,
        FrontendDevServerHost,
        ArbeidsflateHost,
        HostGatewayName,
    ];

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

/// <summary>A bind mount (host path) or a named volume.</summary>
/// <param name="IsVolume">True for a named volume, false for a bind mount.</param>
/// <param name="Source">The volume's name (<see cref="StackSpec.VolumeName" />) or the host path.</param>
/// <param name="Target">The path in the container.</param>
/// <param name="ReadOnly">Whether it is read-only.</param>
internal sealed record MountSpec(bool IsVolume, string Source, string Target, bool ReadOnly);

/// <summary>One container as it runs: the manifest's container resolved for one selection of groups.</summary>
/// <param name="Name">The manifest name: the resource name and the network alias.</param>
/// <param name="Image">The image.</param>
/// <param name="Environment">The manifest's <c>envs</c> and the <c>services__</c> variables, sorted by name.</param>
/// <param name="Mounts">The certificate (directory and legacy file), then the named volumes sorted by id.</param>
/// <param name="Port">The container's port.</param>
/// <param name="Health">The health check.</param>
/// <param name="DependsOn">The containers that must be healthy first.</param>
/// <param name="ExtraHosts">The <c>--add-host</c> entries, <c>name:host-gateway</c>.</param>
/// <param name="PublishedPort">The host port, for the front door only.</param>
internal sealed record ContainerSpec(
    string Name,
    ImageReference Image,
    IReadOnlyDictionary<string, string> Environment,
    IReadOnlyList<MountSpec> Mounts,
    int Port,
    HealthTarget Health,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> ExtraHosts,
    int? PublishedPort
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
    /// The containers of the default group and the named optional groups, in start order: the manifest's
    /// <c>envs</c> plus a <c>services__&lt;name&gt;__http__0</c> for every selected HTTP container, the certificate
    /// read-only from <paramref name="certificateDirectory" />, the named volumes, the extra hosts, and port 8000 on
    /// the front door.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="groups">The optional groups that are on.</param>
    /// <param name="certificateDirectory">The host directory holding the shared certificate.</param>
    /// <param name="tag">Replaces every <c>sha-</c> pin when set (<c>latest</c>, as studioctl's <c>--latest</c>).</param>
    public static IReadOnlyList<ContainerSpec> Resolve(
        LocaltestManifest manifest,
        IEnumerable<string> groups,
        string certificateDirectory,
        string? tag = null
    )
    {
        var names = manifest.Select([LocaltestManifest.DefaultGroup, .. groups]);
        var services = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in names.Where(n => manifest.Containers[n].IsHttp))
        {
            services[$"services__{name}__http__0"] = Topology.Url(name, manifest.Containers[name].HttpPort);
        }
        var extraHosts = Topology.ExtraHostNames.Select(host => host + ":" + Topology.HostGateway).ToList();
        return [.. names.Select(name => Container(manifest, name, services, extraHosts, certificateDirectory, tag))];
    }

    private static ContainerSpec Container(
        LocaltestManifest manifest,
        string name,
        IReadOnlyDictionary<string, string> services,
        IReadOnlyList<string> extraHosts,
        string certificateDirectory,
        string? tag
    )
    {
        var c = manifest.Containers[name];
        var environment = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in c.Envs.Concat(services))
        {
            environment[key] = value;
        }
        List<MountSpec> mounts =
        [
            new(false, certificateDirectory, Topology.CertificateDirectory, true),
            new(
                false,
                Path.Combine(certificateDirectory, Topology.CertificateFileName),
                Topology.LegacyCertificateFile,
                true
            ),
            .. c
                .Volumes.OrderBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => new MountSpec(true, VolumeName(v.Key), v.Value, false)),
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
            name == Topology.FrontDoor ? Topology.IngressPort : null
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
