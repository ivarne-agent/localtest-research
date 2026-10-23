#nullable enable

namespace Altinn.Platform.Storage.Localtest;

/// <summary>Settings for the localtest Storage fakes, bound from <c>Localtest:Storage</c>.</summary>
public sealed class LocaltestStorageOptions
{
    /// <summary>Folder that holds the file-backed blobs (the <c>localtest-v2-storage-blobs</c> volume in the stack).</summary>
    public string BlobRootPath { get; set; } = "/data/blobs";

    /// <summary>
    /// The Dialogporten adapter's base URL, under which its localtest enqueue endpoint is <c>api/localtest/enqueue</c>.
    /// Defaults to <c>http://dialogporten-adapter/storage/dialogporten/</c>: the adapter by its service name, under its
    /// prefix.
    /// </summary>
    public string DialogportenAdapterEndpoint { get; set; } = string.Empty;
}
