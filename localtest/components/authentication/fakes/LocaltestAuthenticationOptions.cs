#nullable enable
namespace Altinn.Platform.Authentication.Localtest;

/// <summary>Settings for the localtest Authentication fakes, bound from <c>Localtest:Authentication</c>.</summary>
public sealed class LocaltestAuthenticationOptions
{
    /// <summary>
    /// Base URL this service uses for its own platform calls (openid metadata, the fake ID-porten's token endpoint and
    /// metadata, register, Profile).
    /// Defaults to <c>GeneralSettings:PlatformEndpoint</c>; in compose it is the localtest app's service name.
    /// </summary>
    public string? InternalPlatformEndpoint { get; set; }

    /// <summary>
    /// Base URL of the Profile API the production <c>ProfileService</c> calls (<c>internal/user</c>), answered by the
    /// register component. Defaults to <c>{InternalPlatformEndpoint}profile/api/v1/</c>.
    /// </summary>
    public string? ProfileEndpoint { get; set; }
}
