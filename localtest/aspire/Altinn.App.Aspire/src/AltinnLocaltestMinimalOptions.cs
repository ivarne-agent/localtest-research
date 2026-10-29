namespace Altinn.App.Aspire;

/// <summary>
/// Options of <see cref="AltinnLocaltestMinimalExtensions.AddAltinnLocaltestMinimal" />: the options of
/// <see cref="AltinnLocaltestOptions" /> that apply to minimal mode. There are no groups (the optional groups need the
/// real Register, Authentication and others) and no data directory option: minimal mode keeps no data, and the
/// certificate and the apps' secrets stay in the package's default data directory, <c>~/.altinn-app-aspire</c>, shared
/// with the full stack.
/// </summary>
public sealed class AltinnLocaltestMinimalOptions
{
    /// <summary>
    /// The tag that replaces every research image's <c>sha-</c> pin; null (the default) runs the manifest's pins.
    /// <c>latest</c> follows main. Any tag that does not start with <c>sha-</c> floats and
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
    /// A loopback host port to publish Postgres on, for <c>psql</c> while the AppHost runs; null (the default) publishes
    /// nothing. The health checks do not need it.
    /// </summary>
    public int? PostgresHostPort { get; set; }

    /// <summary>The data directory; the tests set it, as they set <see cref="AltinnLocaltestOptions.DataDirectory" />.</summary>
    internal string DataDirectory { get; set; } = AltinnLocaltestOptions.DefaultDataDirectory;
}
