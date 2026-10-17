#nullable enable
namespace Altinn.Platform.Authorization.Localtest;

/// <summary>Settings for the localtest PDP fakes, bound from <c>Localtest:Authorization</c>.</summary>
public sealed class LocaltestAuthorizationOptions
{
    /// <summary>The localtest platform base URL (the ingress). Every platform endpoint default is derived from it.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>
    /// The policy store shared with the Access Management component, one folder per blob container: app policies in
    /// <c>metadata/{org}/{app}/policy.xml</c> (written here by the production <c>POST authorization/api/v1/policies</c>,
    /// pushed by the app runner) and delegation policies in <c>delegationpolicies/…</c> (written by Access Management,
    /// read here by version).
    /// </summary>
    public string PolicyStorePath { get; set; } = "/policy-store";

    /// <summary>
    /// Folder with resource registry stand-ins: <c>resourceregistry/{id}/policy.xml</c> (plus an optional
    /// <c>resource.json</c>) per resource. Missing folder means none.
    /// </summary>
    public string PoliciesPath { get; set; } = "/policies";

    /// <summary>Base URL of the register component's localtest endpoints (test users and organisations).</summary>
    public string RegisterLocaltestEndpoint { get; set; } = "http://local.altinn.cloud:8000/register/api/localtest/";
}
