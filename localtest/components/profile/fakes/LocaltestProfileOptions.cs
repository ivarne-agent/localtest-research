#nullable enable
namespace Altinn.Profile.Localtest;

/// <summary>Settings under <c>Localtest:Profile</c>.</summary>
public sealed class LocaltestProfileOptions
{
    /// <summary>
    /// The platform origin with a trailing slash (default <c>http://local.altinn.cloud:8000/</c>). The OpenID metadata,
    /// Register, PDP and Notifications endpoints derive from it, and the seed reads the Register's localtest API under it.
    /// </summary>
    public string PlatformEndpoint { get; set; } = LocaltestProfileExtensions.DefaultPlatformEndpoint;

    /// <summary>
    /// How long the start waits for the Register's localtest API before it gives up on the start-up sync and leaves the
    /// seed to the recurring jobs (seconds, default 90).
    /// </summary>
    public int StartupSyncWaitSeconds { get; set; } = 90;
}
