namespace Altinn.App.Aspire;

/// <summary>Options of <see cref="AltinnLocaltestExtensions.AddAltinnLocaltest" />.</summary>
public sealed class AltinnLocaltestOptions
{
    /// <summary>
    /// The tag every research image runs. Null (the default) takes <c>STUDIOCTL_LOCALTEST_V2_TAG</c> when it is set,
    /// otherwise the manifest's pin; <c>latest</c> follows main and is pulled on every start. A per-image variable
    /// (<c>STUDIOCTL_IMAGE_LOCALTEST_V2_STORAGE</c> and so on) still wins, as with studioctl.
    /// </summary>
    public string? Tag { get; set; }

    /// <summary>
    /// The package's data directory on the host; it holds the shared test certificate (<c>certs/</c>), which every
    /// component mounts. Default <c>~/.altinn-app-aspire</c>.
    /// </summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".altinn-app-aspire");

    /// <summary>
    /// The optional component groups to add (<c>dialogporten</c>, <c>receipt</c>, <c>correspondence</c>,
    /// <c>arbeidsflate</c>, <c>everything</c>), each with the groups it implies. The same as calling
    /// <see cref="AltinnLocaltestExtensions.WithGroup" />.
    /// </summary>
    public ICollection<string> Groups { get; } = [];

    /// <summary>
    /// For diagnosis: when not empty, only these components and what they depend on are added (with the front door),
    /// for example <c>postgres</c> alone. Empty (the default) adds the whole selection.
    /// </summary>
    public ICollection<string> OnlyComponents { get; } = [];
}
