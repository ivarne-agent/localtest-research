#nullable enable

namespace Altinn.Platform.Storage.Localtest;

/// <summary>Settings for the localtest Storage fakes, bound from <c>Localtest:Storage</c>.</summary>
public sealed class LocaltestStorageOptions
{
    /// <summary>Folder that holds the file-backed blobs (a volume in compose).</summary>
    public string BlobRootPath { get; set; } = "/data/blobs";
}
