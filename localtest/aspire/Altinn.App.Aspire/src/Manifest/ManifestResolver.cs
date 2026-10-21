namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// Resolves the manifest into the components a group selection runs, with the Go loader's semantics
/// (<c>internal/cmd/env/localtestv2/components.go</c>, <c>groups.go</c>, <c>config/images.go</c>).
/// </summary>
internal sealed class ManifestResolver
{
    /// <summary>What an extra host name resolves to: the runtime's gateway to the host.</summary>
    public const string HostGateway = "host-gateway";

    private readonly LocaltestManifest _manifest;
    private readonly LocaltestTopology _topology;
    private readonly Placeholders _placeholders;
    private readonly Func<string, string?> _getEnvironment;
    private readonly string? _tag;
    private readonly string _certificateDirectory;

    /// <param name="manifest">The manifest.</param>
    /// <param name="certificateDirectory">The host directory holding the shared certificate.</param>
    /// <param name="tag">The research images' tag; null for the tag variable, then the manifest's pin.</param>
    /// <param name="getEnvironment">Reads the image variables; null for the process environment.</param>
    /// <param name="topology">The public hosts; null for the default topology.</param>
    public ManifestResolver(
        LocaltestManifest manifest,
        string certificateDirectory,
        string? tag = null,
        Func<string, string?>? getEnvironment = null,
        LocaltestTopology? topology = null
    )
    {
        _manifest = manifest;
        _topology = topology ?? LocaltestTopology.Default;
        _placeholders = new Placeholders(manifest, _topology);
        _getEnvironment = getEnvironment ?? System.Environment.GetEnvironmentVariable;
        _tag = tag;
        _certificateDirectory = certificateDirectory;
    }

    /// <summary>The manifest.</summary>
    public LocaltestManifest Manifest => _manifest;

    /// <summary>The topology.</summary>
    public LocaltestTopology Topology => _topology;

    /// <summary>The research images' tag: the option, else the tag variable, else the manifest's pin.</summary>
    public string ResearchTag => _tag ?? NonEmpty(_getEnvironment(_manifest.Images.TagEnv)) ?? _manifest.Images.Tag;

    /// <summary>
    /// The groups a selection enables: those on by default and those named, each with what it implies, transitively
    /// (<c>ResolveGroups</c> without <c>--without</c>). Sorted.
    /// </summary>
    public IReadOnlyList<string> ResolveGroups(IEnumerable<string> requested)
    {
        var enabled = new SortedSet<string>(StringComparer.Ordinal);
        var names = _manifest.Groups.Where(g => g.Value.OnByDefault).Select(g => g.Key).Concat(requested);
        foreach (var name in names)
        {
            if (!_manifest.Groups.ContainsKey(name))
            {
                throw new ArgumentException(
                    $"Unknown localtest group {name} (one of {string.Join(", ", _manifest.Groups.Keys.Order(StringComparer.Ordinal))})."
                );
            }
            enabled.UnionWith(Closure(name));
        }
        return [.. enabled];
    }

    /// <summary>A group and every group it implies, transitively; <c>impliesAll</c> implies every other group.</summary>
    public IReadOnlySet<string> Closure(string name)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([name]);
        while (pending.TryDequeue(out var next))
        {
            if (seen.Add(next))
            {
                foreach (var implied in Implies(next))
                {
                    pending.Enqueue(implied);
                }
            }
        }
        return seen;
    }

    /// <summary>The groups a group implies directly, sorted.</summary>
    public IReadOnlyList<string> Implies(string name)
    {
        var group = _manifest.Groups[name];
        return group.ImpliesAll
            ? [.. _manifest.Groups.Keys.Where(other => other != name).Order(StringComparer.Ordinal)]
            : [.. group.Implies.Order(StringComparer.Ordinal)];
    }

    /// <summary>The components of the always-on set and the enabled groups, in manifest order.</summary>
    public IReadOnlyList<ResolvedComponent> Components(IReadOnlyCollection<string> enabledGroups) =>
        [.. _manifest.Components.Where(c => c.Group is null || enabledGroups.Contains(c.Group)).Select(Resolve)];

    /// <summary>Every component, whatever its group.</summary>
    public IReadOnlyList<ResolvedComponent> AllComponents() => [.. _manifest.Components.Select(Resolve)];

    /// <summary>One component, resolved.</summary>
    public ResolvedComponent Resolve(ManifestComponent c)
    {
        try
        {
            return new ResolvedComponent
            {
                Name = c.Name,
                Group = c.Group,
                Image = ImageOf(c.Image),
                ContainerPort = c.ContainerPort,
                HostPort = c.HostPort,
                HealthPath = c.HealthPath,
                Environment = new SortedDictionary<string, string>(
                    c.Environment.ToDictionary(e => e.Key, e => _placeholders.Expand(e.Value), StringComparer.Ordinal),
                    StringComparer.Ordinal
                ),
                Command = c.Command is null ? null : _placeholders.ExpandAll(c.Command),
                ContainerHealthCheck = c.ContainerHealthCheck is null
                    ? null
                    : _placeholders.ExpandAll(c.ContainerHealthCheck.Test),
                Mounts = [.. c.Mounts.Select(Mount)],
                NetworkAliases = c.NetworkAliases,
                ExtraHosts = ExtraHosts(c),
                DependsOn = c.DependsOn,
                Routes =
                [
                    .. c.Routes.Select(r => new ResolvedRoute(
                        c.Name,
                        _placeholders.Expand(r.Host),
                        r.Prefix,
                        string.IsNullOrEmpty(r.Rewrite) ? null : r.Rewrite
                    )),
                ],
                HostUser = c.HostUser,
            };
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"Component {c.Name}: {e.Message}", e);
        }
    }

    /// <summary>
    /// The image of a component, as <c>config.DefaultImages</c> builds it: a research image is the repository prefix
    /// plus its name at the research tag (<c>latest</c> floats); a third-party or core image is the manifest's; then the
    /// image's override variable, a complete reference, wins and does not float.
    /// </summary>
    public ImageReference ImageOf(ManifestImageSource source)
    {
        ImageReference image;
        string? overrideEnv;
        if (source.Research is { } research)
        {
            var tag = ResearchTag;
            image = ImageReference.From(
                _manifest.Images.RepositoryPrefix + research,
                tag,
                string.Equals(tag, "latest", StringComparison.Ordinal)
            );
            overrideEnv = _manifest.Images.OverrideEnv(research);
        }
        else if (source.ThirdParty is { } thirdParty)
        {
            var external = _manifest.Images.ThirdParty[thirdParty];
            image = ImageReference.From(external.Repository, external.Tag, external.Floating);
            overrideEnv = _manifest.Images.OverrideEnv(thirdParty);
        }
        else
        {
            var core = source.Core!;
            var external = _manifest.Images.Core.TryGetValue(core, out var found)
                ? found
                : throw new InvalidDataException($"No core image {core}.");
            image = ImageReference.From(external.Repository, external.Tag, external.Floating);
            overrideEnv = external.OverrideEnv;
        }
        return overrideEnv is not null && NonEmpty(_getEnvironment(overrideEnv)) is { } reference
            ? ImageReference.Parse(reference)
            : image;
    }

    private ResolvedMount Mount(ManifestMount mount) =>
        mount.Kind switch
        {
            ManifestMount.CertificateDir => new ResolvedMount(
                false,
                _certificateDirectory,
                _manifest.Certificate.ContainerDir,
                true
            ),
            ManifestMount.CertificateFile => new ResolvedMount(
                false,
                Path.Combine(_certificateDirectory, _manifest.Certificate.FileName),
                mount.Target ?? throw new InvalidDataException("A certificateFile mount needs a target."),
                true
            ),
            ManifestMount.NamedVolume => new ResolvedMount(
                true,
                mount.Volume ?? throw new InvalidDataException("A volume mount needs a volume."),
                mount.Target ?? throw new InvalidDataException("A volume mount needs a target."),
                false
            ),
            _ => throw new InvalidDataException($"Unknown mount kind {mount.Kind}."),
        };

    private IReadOnlyList<string> ExtraHosts(ManifestComponent c)
    {
        var hosts = c.MapsIngressHosts ? _topology.IngressHosts : [];
        return [.. hosts.Concat(c.ExtraHostGatewayNames).Select(host => host + ":" + HostGateway)];
    }

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
