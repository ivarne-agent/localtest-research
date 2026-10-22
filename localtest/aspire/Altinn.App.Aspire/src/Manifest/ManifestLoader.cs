using System.Reflection;
using System.Text.Json;

namespace Altinn.App.Aspire.Manifest;

/// <summary>Reads the embedded manifest and checks what the resolver relies on.</summary>
internal static class ManifestLoader
{
    /// <summary>The manifest format version this reader knows.</summary>
    public const int SupportedVersion = 1;

    /// <summary>The embedded manifest's resource name.</summary>
    public const string ManifestResource = "Altinn.App.Aspire.manifest.json";

    /// <summary>The embedded schema's resource name.</summary>
    public const string SchemaResource = "Altinn.App.Aspire.manifest.schema.json";

    /// <summary>The embedded certificate's resource name.</summary>
    public const string CertificateResource = "Altinn.App.Aspire.localtest-accesstoken.pfx";

    private static readonly Lazy<LocaltestManifest> _default = new(() => Parse(ReadResource(ManifestResource)));

    /// <summary>The embedded manifest, parsed once.</summary>
    public static LocaltestManifest Default => _default.Value;

    /// <summary>The bytes of an embedded resource.</summary>
    public static byte[] ReadResource(string name)
    {
        using var stream =
            typeof(ManifestLoader).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The package has no embedded resource {name}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>Parses a manifest and checks its references; throws <see cref="InvalidDataException" />.</summary>
    public static LocaltestManifest Parse(byte[] json)
    {
        LocaltestManifest manifest;
        try
        {
            manifest =
                JsonSerializer.Deserialize(json, ManifestJsonContext.Default.LocaltestManifest)
                ?? throw new InvalidDataException("The localtest manifest is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"The localtest manifest does not parse: {e.Message}", e);
        }
        Check(manifest);
        return manifest;
    }

    private static void Check(LocaltestManifest m)
    {
        if (m.Version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"The localtest manifest has version {m.Version}; this package reads version {SupportedVersion}."
            );
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in m.Components)
        {
            if (!names.Add(c.Name))
            {
                throw new InvalidDataException($"The localtest manifest names component {c.Name} twice.");
            }
            if (c.Group is not null && !m.Groups.ContainsKey(c.Group))
            {
                throw new InvalidDataException($"Component {c.Name} is in the unknown group {c.Group}.");
            }
            var sources = new[] { c.Image.Research, c.Image.ThirdParty, c.Image.Core }.Count(s => s is not null);
            if (sources != 1)
            {
                throw new InvalidDataException($"Component {c.Name} must name exactly one image source.");
            }
        }
        foreach (var c in m.Components)
        {
            if (c.DependsOn.FirstOrDefault(dep => !names.Contains(dep)) is { } unknown)
            {
                throw new InvalidDataException($"Component {c.Name} depends on the unknown component {unknown}.");
            }
        }
        CheckApp(m.App);
        foreach (var (name, group) in m.Groups)
        {
            if (group.Implies.FirstOrDefault(implied => !m.Groups.ContainsKey(implied)) is { } unknown)
            {
                throw new InvalidDataException($"Group {name} implies the unknown group {unknown}.");
            }
        }
    }

    /// <summary>A variable the app section sets twice is refused, as the Go loader refuses it.</summary>
    private static void CheckApp(ManifestApp app)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        IEnumerable<string> all =
        [
            .. app.Environment.Keys,
            .. app.FixedEnvironment.Keys,
            .. app.AccessTokenSigning.Keys,
            app.Secrets.DirectoryEnv,
            .. app.Secrets.Files.Select(f => f.Env),
        ];
        if (all.FirstOrDefault(key => !keys.Add(key)) is { } duplicate)
        {
            throw new InvalidDataException($"The localtest manifest's app section sets {duplicate} twice.");
        }
    }

    /// <summary>The assembly's informational version, for diagnostics.</summary>
    public static string? PackageVersion =>
        typeof(ManifestLoader)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
}
