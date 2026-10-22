using System.Text.Json.Serialization;

namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The localtest-v2 component manifest (<c>localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json</c>),
/// as studioctl's Go loader reads it. Field documentation is in <c>manifest.schema.json</c> beside it.
/// </summary>
internal sealed record LocaltestManifest
{
    /// <summary>The relative path of the schema, for editors.</summary>
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    /// <summary>The format version; this reader knows <see cref="ManifestLoader.SupportedVersion" />.</summary>
    public required int Version { get; init; }

    /// <summary>The prefix of studioctl's container names.</summary>
    public required string ContainerPrefix { get; init; }

    /// <summary>studioctl's network name (Aspire uses its own).</summary>
    public required string Network { get; init; }

    /// <summary>The shared test certificate.</summary>
    public required ManifestCertificate Certificate { get; init; }

    /// <summary>Where the images come from.</summary>
    public required ManifestImages Images { get; init; }

    /// <summary>Every named volume.</summary>
    public required IReadOnlyList<ManifestVolume> Volumes { get; init; }

    /// <summary>The shared Postgres.</summary>
    public required ManifestPostgres Postgres { get; init; }

    /// <summary>What the front door needs beyond the components' routes.</summary>
    public required ManifestFrontDoor FrontDoor { get; init; }

    /// <summary>The environment an app runs with against the stack.</summary>
    public required ManifestApp App { get; init; }

    /// <summary>The optional component groups by name.</summary>
    public required IReadOnlyDictionary<string, ManifestGroup> Groups { get; init; }

    /// <summary>Every container, in start and status order.</summary>
    public required IReadOnlyList<ManifestComponent> Components { get; init; }

    /// <summary>The named component, or null.</summary>
    public ManifestComponent? Component(string name) =>
        Components.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));
}

/// <summary>The shared test certificate.</summary>
internal sealed record ManifestCertificate
{
    /// <summary>The certificate's file name.</summary>
    public required string FileName { get; init; }

    /// <summary>The directory it is mounted at in a container.</summary>
    public required string ContainerDir { get; init; }

    /// <summary>The certificate's path in a container that mounts its directory.</summary>
    public string ContainerPath => ContainerDir + "/" + FileName;
}

/// <summary>Where the images come from.</summary>
internal sealed record ManifestImages
{
    /// <summary>The research images' repository prefix; the image name follows it.</summary>
    public required string RepositoryPrefix { get; init; }

    /// <summary>The research images' tag (the pin).</summary>
    public required string Tag { get; init; }

    /// <summary>The variable that overrides the research images' tag.</summary>
    public required string TagEnv { get; init; }

    /// <summary>The prefix of the per-image override variable.</summary>
    public required string OverrideEnvPrefix { get; init; }

    /// <summary>Third-party images, pinned by digest.</summary>
    public IReadOnlyDictionary<string, ManifestExternalImage> ThirdParty
    {
        get => field ?? new Dictionary<string, ManifestExternalImage>();
        init;
    }

    /// <summary>The images studioctl's localtest runtime runs too.</summary>
    public IReadOnlyDictionary<string, ManifestExternalImage> Core
    {
        get => field ?? new Dictionary<string, ManifestExternalImage>();
        init;
    }

    /// <summary>
    /// The per-image override variable of a research or third-party image: the prefix plus the name upper-cased with
    /// <c>-</c> turned into <c>_</c>.
    /// </summary>
    public string OverrideEnv(string name) => OverrideEnvPrefix + name.Replace('-', '_').ToUpperInvariant();
}

/// <summary>An image that is not one of the research builds.</summary>
internal sealed record ManifestExternalImage
{
    /// <summary>What the image is.</summary>
    public string? Description { get; init; }

    /// <summary>The repository, registry included.</summary>
    public required string Repository { get; init; }

    /// <summary>The tag, possibly with an <c>@sha256:</c> digest.</summary>
    public required string Tag { get; init; }

    /// <summary>Whether the tag moves.</summary>
    public bool Floating { get; init; }

    /// <summary>The variable that overrides the image with a complete reference.</summary>
    public string? OverrideEnv { get; init; }
}

/// <summary>A named volume.</summary>
internal sealed record ManifestVolume
{
    /// <summary>The volume's name.</summary>
    public required string Name { get; init; }

    /// <summary>What it holds.</summary>
    public string? Description { get; init; }
}

/// <summary>The shared Postgres.</summary>
internal sealed record ManifestPostgres
{
    /// <summary>The Postgres component's name.</summary>
    public required string Component { get; init; }

    /// <summary>The superuser.</summary>
    public required string User { get; init; }

    /// <summary>The superuser's password.</summary>
    public required string Password { get; init; }

    /// <summary>The init scripts baked into the image.</summary>
    public IReadOnlyList<ManifestInitScript> InitScripts
    {
        get => field ?? [];
        init;
    }
}

/// <summary>One init script baked into the Postgres image.</summary>
internal sealed record ManifestInitScript
{
    /// <summary>The source, relative to <c>localtest/</c>.</summary>
    public required string Source { get; init; }

    /// <summary>The path in the image.</summary>
    public required string Target { get; init; }
}

/// <summary>What the front door needs beyond the components' routes.</summary>
internal sealed record ManifestFrontDoor
{
    /// <summary>The path that proves the front door forwards.</summary>
    public required string HealthPath { get; init; }

    /// <summary>Routes to ports on the host itself.</summary>
    public IReadOnlyList<ManifestHostRoute> HostRoutes
    {
        get => field ?? [];
        init;
    }
}

/// <summary>A front-door route to a port on the host.</summary>
internal sealed record ManifestHostRoute
{
    /// <summary>The route's name.</summary>
    public required string Name { get; init; }

    /// <summary>What it is for.</summary>
    public string? Description { get; init; }

    /// <summary>The public host, with placeholders.</summary>
    public required string Host { get; init; }

    /// <summary>The path prefix; empty for the whole host.</summary>
    public required string Prefix { get; init; }

    /// <summary>The port on the host.</summary>
    public required int HostPort { get; init; }
}

/// <summary>An optional component group.</summary>
internal sealed record ManifestGroup
{
    /// <summary>What the group adds.</summary>
    public string? Description { get; init; }

    /// <summary>Whether studioctl starts it without a flag.</summary>
    public bool OnByDefault { get; init; }

    /// <summary>The groups this one cannot run without.</summary>
    public IReadOnlyList<string> Implies
    {
        get => field ?? [];
        init;
    }

    /// <summary>Whether it implies every other group.</summary>
    public bool ImpliesAll { get; init; }
}

/// <summary>One container of the stack.</summary>
internal sealed record ManifestComponent
{
    /// <summary>The component's name.</summary>
    public required string Name { get; init; }

    /// <summary>What it is.</summary>
    public string? Description { get; init; }

    /// <summary>Why a setting is what it is; loaders ignore them.</summary>
    public IReadOnlyList<string> Notes
    {
        get => field ?? [];
        init;
    }

    /// <summary>The optional group; null for the always-on components.</summary>
    public string? Group { get; init; }

    /// <summary>The image.</summary>
    public required ManifestImageSource Image { get; init; }

    /// <summary>The dev build (studioctl only).</summary>
    public ManifestDevBuild? Dev { get; init; }

    /// <summary>The port the component listens on in its container.</summary>
    public required int ContainerPort { get; init; }

    /// <summary>The host port it is published on; null for none.</summary>
    public int? HostPort { get; init; }

    /// <summary>The HTTP health path; null when it serves no HTTP.</summary>
    public string? HealthPath { get; init; }

    /// <summary>The health check the container runtime runs (not available in Aspire).</summary>
    public ManifestHealthCheck? ContainerHealthCheck { get; init; }

    /// <summary>The environment, with placeholders.</summary>
    public IReadOnlyDictionary<string, string> Environment
    {
        get => field ?? new Dictionary<string, string>();
        init;
    }

    /// <summary>The image's command replaced, with placeholders.</summary>
    public IReadOnlyList<string>? Command { get; init; }

    /// <summary>The mounts.</summary>
    public IReadOnlyList<ManifestMount> Mounts
    {
        get => field ?? [];
        init;
    }

    /// <summary>Extra DNS names on the network.</summary>
    public IReadOnlyList<string> NetworkAliases
    {
        get => field ?? [];
        init;
    }

    /// <summary>Whether the public host names resolve to the host gateway (default true).</summary>
    public bool? IngressHosts { get; init; }

    /// <summary>Extra names resolved to the host gateway.</summary>
    public IReadOnlyList<string> ExtraHostGatewayNames
    {
        get => field ?? [];
        init;
    }

    /// <summary>Whether the container runs as the developer's user.</summary>
    public bool HostUser { get; init; }

    /// <summary>The components that must be up first.</summary>
    public IReadOnlyList<string> DependsOn
    {
        get => field ?? [];
        init;
    }

    /// <summary>The front-door routes to the component.</summary>
    public IReadOnlyList<ManifestRoute> Routes
    {
        get => field ?? [];
        init;
    }

    /// <summary>Whether the container resolves the public host names to the host gateway.</summary>
    public bool MapsIngressHosts => IngressHosts ?? true;
}

/// <summary>The image of a component: exactly one field is set.</summary>
internal sealed record ManifestImageSource
{
    /// <summary>A research image's name.</summary>
    public string? Research { get; init; }

    /// <summary>A key of <see cref="ManifestImages.ThirdParty" />.</summary>
    public string? ThirdParty { get; init; }

    /// <summary>A key of <see cref="ManifestImages.Core" />.</summary>
    public string? Core { get; init; }
}

/// <summary>How studioctl's <c>--dev</c> builds a component; not used by the package.</summary>
internal sealed record ManifestDevBuild
{
    /// <summary>The <c>--dev</c> name when several components build together.</summary>
    public string? Selector { get; init; }

    /// <summary>The build context relative to <c>localtest/</c>.</summary>
    public required string Context { get; init; }

    /// <summary>The Dockerfile relative to the context.</summary>
    public string? Dockerfile { get; init; }
}

/// <summary>A container health check, run by the container runtime.</summary>
internal sealed record ManifestHealthCheck
{
    /// <summary>The test, with placeholders.</summary>
    public required IReadOnlyList<string> Test { get; init; }

    /// <summary>A Go duration.</summary>
    public required string Interval { get; init; }

    /// <summary>A Go duration.</summary>
    public required string Timeout { get; init; }

    /// <summary>The retries before unhealthy.</summary>
    public required int Retries { get; init; }

    /// <summary>A Go duration; null for none.</summary>
    public string? StartPeriod { get; init; }
}

/// <summary>One mount; <see cref="Kind" /> says which fields apply.</summary>
internal sealed record ManifestMount
{
    /// <summary>The certificate directory read-only at the certificate's container directory.</summary>
    public const string CertificateDir = "certificateDir";

    /// <summary>The certificate file read-only at <see cref="Target" />.</summary>
    public const string CertificateFile = "certificateFile";

    /// <summary>A named volume at <see cref="Target" />.</summary>
    public const string NamedVolume = "volume";

    /// <summary><see cref="CertificateDir" />, <see cref="CertificateFile" /> or <see cref="NamedVolume" />.</summary>
    public required string Kind { get; init; }

    /// <summary>The volume of a <see cref="NamedVolume" /> mount.</summary>
    public string? Volume { get; init; }

    /// <summary>The path in the container (not for <see cref="CertificateDir" />).</summary>
    public string? Target { get; init; }
}

/// <summary>A public host-and-prefix route.</summary>
internal sealed record ManifestRoute
{
    /// <summary>The public host, with placeholders.</summary>
    public required string Host { get; init; }

    /// <summary>The path prefix; empty for the whole host.</summary>
    public required string Prefix { get; init; }

    /// <summary>The path forwarded in place of the prefix; null to forward the path unchanged.</summary>
    public string? Rewrite { get; init; }
}

/// <summary>
/// The environment an app runs with against the stack, which every runner sets (studioctl's <c>run</c>, this package's
/// <c>AddAltinnApp</c>); the runner adds the Kestrel URL, the OpenTelemetry endpoint and the secrets directory's path.
/// </summary>
internal sealed record ManifestApp
{
    /// <summary>What the section is for.</summary>
    public string? Description { get; init; }

    /// <summary>Set unless the inherited environment has the variable (studioctl); with placeholders.</summary>
    public required IReadOnlyDictionary<string, string> Environment { get; init; }

    /// <summary>Set on every run whatever the inherited environment holds.</summary>
    public required IReadOnlyDictionary<string, string> FixedEnvironment { get; init; }

    /// <summary>The certificate variables, set when the runner has the certificate; with the runner's placeholders.</summary>
    public required IReadOnlyDictionary<string, string> AccessTokenSigning { get; init; }

    /// <summary>The secrets directory variable and its files.</summary>
    public required ManifestAppSecrets Secrets { get; init; }
}

/// <summary>The app's secrets directory and the files in it.</summary>
internal sealed record ManifestAppSecrets
{
    /// <summary>The variable holding the directory's path.</summary>
    public required string DirectoryEnv { get; init; }

    /// <summary>The files of the directory.</summary>
    public required IReadOnlyList<ManifestAppSecretFile> Files { get; init; }

    /// <summary>The named file.</summary>
    public ManifestAppSecretFile File(string name) =>
        Files.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"The localtest manifest names no app secrets file {name}.");
}

/// <summary>One file of the secrets directory.</summary>
internal sealed record ManifestAppSecretFile
{
    /// <summary>The development app codes' name.</summary>
    public const string AppCodes = "appCodes";

    /// <summary>The file's name in the manifest.</summary>
    public required string Name { get; init; }

    /// <summary>What it holds and who writes it.</summary>
    public string? Description { get; init; }

    /// <summary>The variable that names the file.</summary>
    public required string Env { get; init; }

    /// <summary>The file's name in the directory.</summary>
    public required string FileName { get; init; }

    /// <summary>Whether every runner writes it before the app starts.</summary>
    public bool WrittenOnEveryRun { get; init; }
}
