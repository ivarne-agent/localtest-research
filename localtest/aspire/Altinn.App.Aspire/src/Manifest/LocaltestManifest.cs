using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The localtest-v2 manifest (<c>localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json</c>, its
/// README beside it): the groups and the containers. Parsed and checked as studioctl's Go loader
/// (<c>manifest.go</c>) does: known keys only, every reference resolves, no dependency cycle, every container reached
/// by a group, the values in range, the public section valid, every placeholder in <c>envs</c> one of the public
/// origin's (<see cref="PublicOrigin" />) and every source of <c>files</c> one that ships with the manifest
/// (<see cref="ManifestFiles" />).
/// </summary>
internal sealed partial class LocaltestManifest
{
    /// <summary>The group that is always on.</summary>
    public const string DefaultGroup = "default";

    /// <summary>
    /// The group that replaces the default one in minimal mode (the front door emulating the rest); not an optional
    /// group.
    /// </summary>
    public const string MinimalGroup = "minimal";

    /// <summary>
    /// The group the package ignores: the OpenTelemetry collector and Grafana that studioctl runs. Aspire's dashboard
    /// receives the containers' and the apps' telemetry instead, so the group is not optional here, <c>everything</c>
    /// leaves it out and <c>WithGroup</c> refuses it.
    /// </summary>
    public const string MonitoringGroup = "monitoring";

    /// <summary>A container's port when the manifest names none.</summary>
    public const int DefaultPort = 8080;

    /// <summary>A container's health path when the manifest names none.</summary>
    public const string DefaultHealth = "/health";

    /// <summary>The health of a container that is up once its port accepts a connection (Postgres, Redis).</summary>
    public const string HealthTcp = "tcp";

    /// <summary>The embedded manifest's resource name.</summary>
    public const string Resource = "Altinn.App.Aspire.manifest.json";

    private const int MaxPort = 65535;

    /// <summary>
    /// A volume id: lower-case letters and digits in hyphen-separated words (<c>postgres-data</c>). Each runtime names
    /// the volume by its own prefix and the id (<see cref="StackSpec.VolumePrefix" />).
    /// </summary>
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex VolumeId();

    private static readonly Lazy<LocaltestManifest> _default = new(() =>
    {
        using var stream =
            typeof(LocaltestManifest).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The package has no embedded resource {Resource}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return Parse(copy.ToArray(), ManifestFiles.Embedded);
    });

    private readonly Dictionary<string, ManifestContainer> _containers;
    private readonly List<string> _names;

    private LocaltestManifest(
        IReadOnlyList<ManifestGroup> groups,
        ManifestPublic @public,
        Dictionary<string, ManifestContainer> containers,
        List<string> names,
        ManifestFiles files
    )
    {
        Files = files;
        Groups = groups;
        Public = @public;
        _containers = containers;
        _names = names;
    }

    /// <summary>The embedded manifest, parsed and checked once.</summary>
    public static LocaltestManifest Default => _default.Value;

    /// <summary>The groups in file order.</summary>
    public IReadOnlyList<ManifestGroup> Groups { get; }

    /// <summary>The files that ship with the manifest, which the containers' <c>files</c> name.</summary>
    public ManifestFiles Files { get; }

    /// <summary>The public section: the origin's default host and port, and the dedicated subdomains.</summary>
    public ManifestPublic Public { get; }

    /// <summary>
    /// The public origin at <paramref name="host" /> and <paramref name="port" /> with the manifest's subdomains: null
    /// is the manifest's default, a port of 0 one Aspire allocates when the AppHost runs.
    /// </summary>
    public PublicOrigin Origin(string? host = null, int? port = null) =>
        new(
            host ?? Public.Host,
            port switch
            {
                null => Public.Port,
                0 => null,
                _ => port,
            },
            Public.Subdomains
        );

    /// <summary>A container's envs with the placeholders resolved for <paramref name="origin" />.</summary>
    public IReadOnlyDictionary<string, OriginValue> Envs(string name, PublicOrigin origin) =>
        _containers[name].Envs.ToDictionary(kv => kv.Key, kv => origin.Resolve(kv.Value), StringComparer.Ordinal);

    /// <summary>The containers by name.</summary>
    public IReadOnlyDictionary<string, ManifestContainer> Containers => _containers;

    /// <summary>The container names in file order.</summary>
    public IReadOnlyList<string> Names => _names;

    /// <summary>The group names in file order.</summary>
    public IReadOnlyList<string> GroupNames => [.. Groups.Select(g => g.Name)];

    /// <summary>Every volume id, sorted; an id several containers name is listed once.</summary>
    public IReadOnlyList<string> VolumeIds =>
        [.. _containers.Values.SelectMany(c => c.Volumes.Keys).Distinct().Order(StringComparer.Ordinal)];

    /// <summary>
    /// The containers the named groups start, in start order: the groups' roots and their transitive dependencies, each
    /// after the ones it depends on, ties in file order. Unknown group names are ignored; the caller validates them.
    /// </summary>
    public IReadOnlyList<string> Select(IEnumerable<string> groups)
    {
        var selected = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string name)
        {
            if (selected.Add(name))
            {
                foreach (var dependency in _containers[name].DependsOn)
                {
                    Visit(dependency);
                }
            }
        }
        foreach (var name in groups)
        {
            foreach (var root in Groups.FirstOrDefault(g => g.Name == name)?.Containers ?? [])
            {
                Visit(root);
            }
        }
        return StartOrder(selected);
    }

    /// <summary>Parses a manifest and checks it; throws <see cref="InvalidDataException" /> naming the fault.</summary>
    /// <param name="json">The manifest.</param>
    /// <param name="files">What ships with it; null ships nothing.</param>
    public static LocaltestManifest Parse(byte[] json, ManifestFiles? files = null)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the manifest is not an object");
            }
            JsonElement? groupsElement = null;
            JsonElement? containersElement = null;
            JsonElement? publicElement = null;
            foreach (var property in root.EnumerateObject())
            {
                switch (property.Name)
                {
                    case "groups" when groupsElement is null:
                        groupsElement = property.Value;
                        break;
                    case "containers" when containersElement is null:
                        containersElement = property.Value;
                        break;
                    case "public" when publicElement is null:
                        publicElement = property.Value;
                        break;
                    case "groups" or "containers" or "public":
                        throw Invalid($"duplicate key {property.Name}");
                    default:
                        throw Invalid($"unknown key {property.Name}");
                }
            }
            var groups = groupsElement is { ValueKind: not JsonValueKind.Null } g
                ? g.Deserialize(ManifestJsonContext.Default.IReadOnlyListManifestGroup) ?? []
                : [];
            var @public = publicElement is { ValueKind: JsonValueKind.Object } p
                ? p.Deserialize(ManifestJsonContext.Default.ManifestPublic) ?? new ManifestPublic()
                : new ManifestPublic();
            var (containers, names) = ReadContainers(containersElement);
            var manifest = new LocaltestManifest(groups, @public, containers, names, files ?? ManifestFiles.None);
            manifest.Check();
            return manifest;
        }
        catch (JsonException e)
        {
            throw Invalid(e.Message, e);
        }
    }

    private static (Dictionary<string, ManifestContainer>, List<string>) ReadContainers(JsonElement? element)
    {
        if (element is not { ValueKind: JsonValueKind.Object } containers)
        {
            throw Invalid("no containers");
        }
        var map = new Dictionary<string, ManifestContainer>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var property in containers.EnumerateObject())
        {
            ManifestContainer container;
            CheckFileTargetsUnique(property);
            try
            {
                container =
                    property.Value.Deserialize(ManifestJsonContext.Default.ManifestContainer)
                    ?? throw Invalid($"container {property.Name} is null");
            }
            catch (JsonException e)
            {
                throw Invalid($"container {property.Name}: {e.Message}", e);
            }
            if (!map.TryAdd(property.Name, container))
            {
                throw Invalid($"duplicate container {property.Name}");
            }
            names.Add(property.Name);
        }
        return (map, names);
    }

    /// <summary>
    /// Refuses a <c>files</c> object that names a container path twice, which the deserializer would resolve silently.
    /// </summary>
    private static void CheckFileTargetsUnique(JsonProperty container)
    {
        if (
            container.Value.ValueKind != JsonValueKind.Object
            || !container.Value.TryGetProperty("files", out var files)
            || files.ValueKind != JsonValueKind.Object
        )
        {
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.EnumerateObject())
        {
            if (!seen.Add(file.Name))
            {
                throw Invalid($"container {container.Name}: duplicate files: container path \"{file.Name}\"");
            }
        }
    }

    private void Check()
    {
        if (_names.Count == 0)
        {
            throw Invalid("no containers");
        }
        foreach (var name in _names)
        {
            CheckContainer(name, _containers[name]);
        }
        CheckPublic();
        CheckGroups();
        if (StartOrder([.. _names]).Count != _names.Count)
        {
            var placed = StartOrder([.. _names]).ToHashSet(StringComparer.Ordinal);
            throw Invalid($"dependency cycle among {string.Join(", ", _names.Where(n => !placed.Contains(n)))}");
        }
        var reached = Select(GroupNames).ToHashSet(StringComparer.Ordinal);
        if (_names.FirstOrDefault(n => !reached.Contains(n)) is { } unreached)
        {
            throw Invalid($"container no group reaches: {unreached}");
        }
    }

    private void CheckContainer(string name, ManifestContainer c)
    {
        if (name.Length == 0 || name.Trim() != name || name.IndexOfAny([' ', '/', ':']) >= 0)
        {
            throw Invalid($"container name \"{name}\"");
        }
        string Fault(string what) => $"container {name}: {what}";
        if (string.IsNullOrWhiteSpace(c.Registry) || c.Registry.Contains(' ', StringComparison.Ordinal))
        {
            throw Invalid(Fault($"registry \"{c.Registry}\""));
        }
        if (string.IsNullOrWhiteSpace(c.Tag) || c.Tag.Contains(' ', StringComparison.Ordinal))
        {
            throw Invalid(Fault($"tag \"{c.Tag}\""));
        }
        if (c.Port is < 0 or > MaxPort)
        {
            throw Invalid(Fault($"port {c.Port}"));
        }
        if (c.Health is { Length: > 0 } health && health != HealthTcp && !health.StartsWith('/'))
        {
            throw Invalid(Fault($"health \"{health}\" is neither a path nor \"{HealthTcp}\""));
        }
        if (c.Envs.Keys.FirstOrDefault(k => k.Length == 0 || k.IndexOfAny(['=', ' ']) >= 0) is { } key)
        {
            throw Invalid(Fault($"envs key \"{key}\""));
        }
        foreach (var (id, target) in c.Volumes)
        {
            if (!VolumeId().IsMatch(id) || !target.StartsWith('/'))
            {
                throw Invalid(Fault($"volume \"{id}\" at \"{target}\""));
            }
        }
        CheckFiles(c, Fault);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dependency in c.DependsOn)
        {
            if (!_containers.ContainsKey(dependency) || dependency == name)
            {
                throw Invalid(Fault($"dependsOn: unknown container \"{dependency}\""));
            }
            if (!seen.Add(dependency))
            {
                throw Invalid(Fault($"dependsOn: duplicate \"{dependency}\""));
            }
        }
    }

    /// <summary>
    /// A container's files: each container path absolute and clean, and not a volume's path too; each source a clean
    /// relative path without <c>..</c>, and shipped with the manifest.
    /// </summary>
    private void CheckFiles(ManifestContainer c, Func<string, string> fault)
    {
        var volumeTargets = c.Volumes.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var (target, source) in c.Files.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (target == "/" || !target.StartsWith('/') || !IsClean(target[1..]))
            {
                throw Invalid(fault($"files: container path \"{target}\" is not an absolute, clean path"));
            }
            if (volumeTargets.Contains(target))
            {
                throw Invalid(fault($"duplicate files: container path \"{target}\" is a volume's too"));
            }
            if (
                source.Length == 0
                || source.StartsWith('/')
                || source.Contains('\\', StringComparison.Ordinal)
                || !IsClean(source)
            )
            {
                throw Invalid(fault($"files: {target}: source \"{source}\" is not a clean relative path"));
            }
            if (!Files.Ships(source))
            {
                throw Invalid(fault($"files: {target}: no such file beside the manifest: \"{source}\""));
            }
        }
    }

    /// <summary>A relative path whose segments are neither empty nor <c>.</c> nor <c>..</c>.</summary>
    private static bool IsClean(string relative) =>
        relative.Split('/').All(segment => segment.Length > 0 && segment != "." && segment != "..");

    private void CheckPublic()
    {
        if (PublicOrigin.HostFault(Public.Host) is { } fault)
        {
            throw Invalid("public: " + fault);
        }
        if (Public.Port is < 1 or > MaxPort)
        {
            throw Invalid($"public: port {Public.Port}");
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var sub in Public.Subdomains)
        {
            if (!PublicOrigin.HostLabel().IsMatch(sub))
            {
                throw Invalid($"public: subdomain \"{sub}\"");
            }
            if (!seen.Add(sub))
            {
                throw Invalid($"public: duplicate subdomain {sub}");
            }
        }
        var origin = Origin();
        foreach (var name in _names)
        {
            foreach (var (key, value) in _containers[name].Envs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (origin.PlaceholderFault(value) is { } placeholder)
                {
                    throw Invalid($"container {name}: envs {key}: {placeholder}");
                }
            }
        }
    }

    private void CheckGroups()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            if (string.IsNullOrEmpty(group.Name))
            {
                throw Invalid("a group without a name");
            }
            if (!names.Add(group.Name))
            {
                throw Invalid($"duplicate group {group.Name}");
            }
            if (group.Containers.Count == 0)
            {
                throw Invalid($"group {group.Name} has no containers");
            }
            if (group.Containers.FirstOrDefault(c => !_containers.ContainsKey(c)) is { } unknown)
            {
                throw Invalid($"group {group.Name}: unknown container \"{unknown}\"");
            }
        }
        if (!names.Contains(DefaultGroup))
        {
            throw Invalid($"no {DefaultGroup} group");
        }
    }

    /// <summary>The selected containers, each after its dependencies, ties in file order.</summary>
    private List<string> StartOrder(HashSet<string> selected)
    {
        var done = new HashSet<string>(StringComparer.Ordinal);
        var order = new List<string>();
        var progressed = true;
        while (order.Count < selected.Count && progressed)
        {
            progressed = false;
            foreach (var name in _names)
            {
                if (selected.Contains(name) && !done.Contains(name) && _containers[name].DependsOn.All(done.Contains))
                {
                    done.Add(name);
                    order.Add(name);
                    progressed = true;
                }
            }
        }
        return order;
    }

    private static InvalidDataException Invalid(string message, Exception? inner = null) =>
        new($"invalid localtest-v2 manifest: {message}", inner);
}

// The manifest's records have setters, not init accessors: the JSON source generator sets every init property in its
// object initializer, so a key the file leaves out would be null instead of the default below.

/// <summary>A group: its root containers, which start with their transitive dependencies.</summary>
internal sealed record ManifestGroup
{
    /// <summary>The group's name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The root containers.</summary>
    public IReadOnlyList<string> Containers { get; set; } = [];
}

/// <summary>One container: its image, environment, start order, volumes, port and health.</summary>
internal sealed record ManifestContainer
{
    /// <summary>The image repository, with the registry host.</summary>
    public string Registry { get; set; } = "";

    /// <summary>The image tag; a digest may follow it (<c>7.2-alpine@sha256:...</c>).</summary>
    public string Tag { get; set; } = "";

    /// <summary>The environment: literal strings but for the public origin's placeholders (<see cref="PublicOrigin" />).</summary>
    public IReadOnlyDictionary<string, string> Envs { get; set; } = new Dictionary<string, string>();

    /// <summary>The containers that start, and are healthy, before this one.</summary>
    public IReadOnlyList<string> DependsOn { get; set; } = [];

    /// <summary>
    /// Volume id to the path the volume is mounted at. The id names one volume for the whole manifest (containers
    /// naming the same id share it); the runtime prefixes it (<see cref="StackSpec.VolumeName" />).
    /// </summary>
    public IReadOnlyDictionary<string, string> Volumes { get; set; } = new Dictionary<string, string>();

    /// <summary>
    /// Path in the container to a file or folder that ships with the manifest (<see cref="ManifestFiles" />), relative
    /// to the manifest's directory: mounted read-only.
    /// </summary>
    public IReadOnlyDictionary<string, string> Files { get; set; } = new Dictionary<string, string>();

    /// <summary>The container's port; zero is <see cref="LocaltestManifest.DefaultPort" />.</summary>
    public int Port { get; set; }

    /// <summary>An HTTP path, or <c>tcp</c>; empty is <see cref="LocaltestManifest.DefaultHealth" />.</summary>
    public string? Health { get; set; }

    /// <summary>The image reference as written.</summary>
    [JsonIgnore]
    public string Image => Registry + ":" + Tag;

    /// <summary>The port with the default applied.</summary>
    [JsonIgnore]
    public int HttpPort => Port == 0 ? LocaltestManifest.DefaultPort : Port;

    /// <summary>The health with the default applied: an HTTP path, or <c>tcp</c>.</summary>
    [JsonIgnore]
    public string HealthCheck => string.IsNullOrEmpty(Health) ? LocaltestManifest.DefaultHealth : Health;

    /// <summary>
    /// Whether it is an HTTP service (an HTTP health path): every running one is announced to every container by a
    /// <c>services__&lt;name&gt;__http__0</c> variable.
    /// </summary>
    [JsonIgnore]
    public bool IsHttp => HealthCheck != LocaltestManifest.HealthTcp;
}
