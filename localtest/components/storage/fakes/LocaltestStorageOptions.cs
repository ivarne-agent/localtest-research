#nullable enable

namespace Altinn.Platform.Storage.Localtest;

/// <summary>Settings for the localtest Storage fakes, bound from <c>Localtest:Storage</c>.</summary>
public sealed class LocaltestStorageOptions
{
    /// <summary>Folder that holds the file-backed blobs (a volume in compose).</summary>
    public string BlobRootPath { get; set; } = "/data/blobs";

    /// <summary>
    /// The Dialogporten adapter's base URL, under which its localtest enqueue endpoint is <c>api/localtest/enqueue</c>.
    /// Defaults to <c>{GeneralSettings:PlatformEndpoint}storage/dialogporten/</c>, the adapter's public prefix.
    /// </summary>
    public string DialogportenAdapterEndpoint { get; set; } = string.Empty;
}
