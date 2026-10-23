#nullable enable
namespace Altinn.Platform.Authorization.Localtest;

/// <summary>Settings for the localtest PDP fakes, bound from <c>Localtest:Authorization</c>.</summary>
public sealed class LocaltestAuthorizationOptions
{
    /// <summary>The localtest platform base URL (the public origin): the OpenID metadata and the SBL links derive from it; the services are called by name.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>
    /// The policy store shared with the Access Management component, one folder per blob container: app policies in
    /// <c>metadata/{org}/{app}/policy.xml</c> (written here by the production <c>POST authorization/api/v1/policies</c>,
    /// pushed by the app runner) and delegation policies in <c>delegationpolicies/…</c> (written by Access Management,
    /// read here by version).
    /// </summary>
    public string PolicyStorePath { get; set; } = "/policy-store";
}
