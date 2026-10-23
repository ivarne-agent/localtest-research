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
    /// <c>latest</c> follows main and is pulled on every start.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// A loopback host port to publish Postgres on, for <c>psql</c> while the AppHost runs; null (the default) publishes
    /// nothing. The health checks do not need it.
    /// </summary>
    public int? PostgresHostPort { get; set; }

    /// <summary>The data directory; the tests set it, as they set <see cref="AltinnLocaltestOptions.DataDirectory" />.</summary>
    internal string DataDirectory { get; set; } = AltinnLocaltestOptions.DefaultDataDirectory;
}
