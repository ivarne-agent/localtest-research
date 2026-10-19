#nullable enable
namespace Altinn.Platform.Authentication.Localtest;

/// <summary>Settings for the localtest Authentication fakes, bound from <c>Localtest:Authentication</c>.</summary>
public sealed class LocaltestAuthenticationOptions
{
    /// <summary>Base URL of the register component's localtest endpoints (test users and organisations).</summary>
    public string RegisterLocaltestEndpoint { get; set; } = "http://local.altinn.cloud:8000/register/api/localtest/";

    /// <summary>
    /// Base URL this service uses for its own platform calls (openid metadata, register, Maskinporten metadata).
    /// Defaults to <c>GeneralSettings:PlatformEndpoint</c>; in compose it is the localtest app's service name.
    /// </summary>
    public string? InternalPlatformEndpoint { get; set; }

    /// <summary>
    /// Base URL of the Profile API the production <c>ProfileService</c> calls (<c>internal/user</c>), answered by the
    /// register component. Defaults to <c>{InternalPlatformEndpoint}profile/api/v1/</c>.
    /// </summary>
    public string? ProfileEndpoint { get; set; }

    /// <summary>The acr the picker preselects when the app did not ask for a level.</summary>
    public string DefaultAcr { get; set; } = "idporten-loa-substantial";
}
