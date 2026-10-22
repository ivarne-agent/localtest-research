namespace Altinn.App.Aspire;

/// <summary>Options of <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />.</summary>
public sealed class AltinnLocaltestOptions
{
    /// <summary>
    /// The tag that replaces every research image's <c>sha-</c> pin; null (the default) runs the manifest's pins.
    /// <c>latest</c> follows main (studioctl's <c>--latest</c>) and is pulled on every start.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// The package's data directory on the host; it holds the shared test certificate (<c>certs/</c>), which every
    /// container mounts, and the apps' secrets. Default <c>~/.altinn-app-aspire</c>.
    /// </summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".altinn-app-aspire");

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
}
