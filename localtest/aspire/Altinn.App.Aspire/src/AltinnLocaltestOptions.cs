namespace Altinn.App.Aspire;

/// <summary>Options of <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />.</summary>
public sealed class AltinnLocaltestOptions
{
    /// <summary>
    /// The tag that replaces every research image's <c>sha-</c> pin; null (the default) runs the manifest's pins.
    /// <c>latest</c> follows main (studioctl's <c>--latest</c>). Any tag that does not start with <c>sha-</c> floats and
    /// is pulled on every start, so a locally built image needs a <c>sha-</c> tag (for example <c>sha-local</c>).
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// The public origin's host: the front door answers on it and on its subdomains <c>pdf.</c>,
    /// <c>workflow-engine.</c>, <c>app-frontend.</c> and <c>af.</c> (the manifest's list). Null (the default) is the
    /// manifest's, <c>local.altinn.cloud</c>. Every name must resolve to this machine (the hosts file) for a browser;
    /// inside the stack the containers resolve them to the host gateway.
    /// </summary>
    public string? PublicHost { get; set; }

    /// <summary>
    /// The public origin's port, the front door's published port. Null (the default) is the manifest's, 8000; 0 is a
    /// free port Aspire allocates when the AppHost runs (every value derived from the origin then resolves to it).
    /// </summary>
    public int? PublicPort { get; set; }

    /// <summary>
    /// The package's data directory on the host; it holds the shared test certificate (<c>certs/</c>), which every
    /// container mounts, and the apps' secrets. Default <c>~/.altinn-app-aspire</c>.
    /// </summary>
    public string DataDirectory { get; set; } = DefaultDataDirectory;

    /// <summary>
    /// The optional groups to add (<c>dialogporten</c>, <c>receipt</c>, <c>correspondence</c>, <c>arbeidsflate</c>,
    /// <c>everything</c>). The same as calling <see cref="AltinnLocaltestExtensions.WithGroup" />.
    /// </summary>
    public ICollection<string> Groups { get; } = [];

    /// <summary>
    /// A loopback host port to publish Postgres on, for <c>psql</c> and other tools; null (the default) publishes
    /// nothing, as studioctl. The health checks do not need it.
    /// </summary>
    public int? PostgresHostPort { get; set; }

    /// <summary>The default of <see cref="DataDirectory" />: <c>~/.altinn-app-aspire</c>.</summary>
    internal static string DefaultDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".altinn-app-aspire");
}
