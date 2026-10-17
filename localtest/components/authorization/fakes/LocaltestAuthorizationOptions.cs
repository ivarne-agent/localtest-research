#nullable enable
namespace Altinn.Platform.Authorization.Localtest;

/// <summary>Settings for the localtest PDP fakes, bound from <c>Localtest:Authorization</c>.</summary>
public sealed class LocaltestAuthorizationOptions
{
    /// <summary>The localtest platform base URL (the ingress). Every platform endpoint default is derived from it.</summary>
    public string PlatformEndpoint { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>
    /// Folder with mounted policies: <c>{org}/{app}/policy.xml</c> for apps and <c>resourceregistry/{id}/policy.xml</c>
    /// (plus an optional <c>resource.json</c>) for resource registry resources. Missing folder means none.
    /// </summary>
    public string PoliciesPath { get; set; } = "/policies";

    /// <summary>Base URL the running apps are reached at; the app policy is <c>{AppsBaseUrl}{org}/{app}/api/v1/meta/authorizationpolicy</c>.</summary>
    public string AppsBaseUrl { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>Whether an app policy missing from <see cref="PoliciesPath"/> is fetched from the running app.</summary>
    public bool FetchPoliciesFromApps { get; set; } = true;

    /// <summary>How long a policy read from the folder or an app is reused, so edits apply almost immediately.</summary>
    public int PolicyCacheSeconds { get; set; } = 5;

    /// <summary>Base URL of the register component's localtest endpoints (test users and organisations).</summary>
    public string RegisterLocaltestEndpoint { get; set; } = "http://local.altinn.cloud:8000/register/api/localtest/";
}
