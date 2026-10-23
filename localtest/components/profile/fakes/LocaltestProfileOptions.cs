#nullable enable
namespace Altinn.Profile.Localtest;

/// <summary>Settings under <c>Localtest:Profile</c>.</summary>
public sealed class LocaltestProfileOptions
{
    /// <summary>
    /// The platform origin with a trailing slash (default <c>http://local.altinn.cloud:8000/</c>). The OpenID metadata
    /// endpoint derives from it (public: the JwtCookie handler fetches it outside the factory).
    /// </summary>
    public string PlatformEndpoint { get; set; } = LocaltestProfileExtensions.DefaultPlatformEndpoint;

    /// <summary>The Register by its service name, under its prefix, with a trailing slash.</summary>
    public const string DefaultRegisterEndpoint = "http://register/register/api/";

    /// <summary>
    /// The Register's API with a trailing slash (default <see cref="DefaultRegisterEndpoint"/>); the seed reads the
    /// Register's localtest API (<c>localtest/test-users</c>, …) under it.
    /// </summary>
    public string RegisterEndpoint { get; set; } = DefaultRegisterEndpoint;

    /// <summary>
    /// How long the start waits for the Register's localtest API before it gives up on the start-up sync and leaves the
    /// seed to the recurring jobs (seconds, default 90).
    /// </summary>
    public int StartupSyncWaitSeconds { get; set; } = 90;
}
