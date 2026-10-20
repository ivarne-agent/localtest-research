namespace Altinn.Correspondence.Localtest;

/// <summary>Localtest settings of the Correspondence component, bound from <c>Localtest:Correspondence</c>.</summary>
public sealed class LocaltestCorrespondenceOptions
{
    public const string SectionName = "Localtest:Correspondence";

    /// <summary>
    /// The platform origin as the container reaches it, with a trailing slash. Every platform URL in the defaults derives
    /// from it, and the blob URLs (<c>{PlatformEndpoint}correspondence/api/localtest/blobs/{id}</c>) start with it.
    /// </summary>
    public string PlatformEndpoint { get; set; } = LocaltestCorrespondence.DefaultPlatformEndpoint;

    /// <summary>Root of the file-backed blob store: <c>attachments/{id}</c> and <c>reports/{name}</c> below it.</summary>
    public string BlobRootPath { get; set; } = "/mnt/storage";

    /// <summary>The least time between two fetches of Dialogporten's JWKS when a dialog token names an unknown key.</summary>
    public int DialogTokenKeyRefreshSeconds { get; set; } = 10;
}
