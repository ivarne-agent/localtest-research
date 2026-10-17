#nullable enable

namespace Altinn.Platform.Storage.Localtest;

/// <summary>Settings for the localtest Storage fakes, bound from <c>Localtest:Storage</c>.</summary>
public sealed class LocaltestStorageOptions
{
    /// <summary>Folder that holds the file-backed blobs (a volume in compose).</summary>
    public string BlobRootPath { get; set; } = "/data/blobs";

    /// <summary>
    /// Base URL the running apps are reached under, as <c>{AppBaseUrl}{org}/{app}/api/v1/...</c>. Defaults to
    /// <c>GeneralSettings:PlatformEndpoint</c>, where the ingress routes <c>/{org}/{app}/</c> to the app.
    /// </summary>
    public string AppBaseUrl { get; set; } = "http://local.altinn.cloud:8000/";

    /// <summary>Fallback folder with <c>{org}/{app}/applicationmetadata.json</c> and <c>{org}/{app}/texts/resource.{lang}.json</c>.</summary>
    public string AppsPath { get; set; } = "/apps";

    /// <summary>How long a metadata or text answer from the app source is cached.</summary>
    public int AppCacheSeconds { get; set; } = 30;

    /// <summary>Timeout for one request to a running app.</summary>
    public int AppRequestTimeoutSeconds { get; set; } = 5;
}
