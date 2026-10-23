namespace Altinn.Correspondence.Localtest;

/// <summary>Localtest settings of the Correspondence component, bound from <c>Localtest:Correspondence</c>.</summary>
public sealed class LocaltestCorrespondenceOptions
{
    public const string SectionName = "Localtest:Correspondence";

    /// <summary>
    /// The public origin, with a trailing slash: <c>AltinnOptions:PlatformGatewayUrl</c> (the base of the nine platform
    /// clients, and the OIDC authority and issuer), the links written into dialogs and notifications, and the blob URLs
    /// (<c>{PlatformEndpoint}correspondence/api/localtest/blobs/{id}</c>) derive from it. The platform clients reach the
    /// services by name (<c>fakes/ServiceDiscovery/LocaltestPrefixRouting</c>).
    /// </summary>
    public string PlatformEndpoint { get; set; } = LocaltestCorrespondence.DefaultPlatformEndpoint;

    /// <summary>Root of the file-backed blob store: <c>attachments/{id}</c> and <c>reports/{name}</c> below it.</summary>
    public string BlobRootPath { get; set; } = "/mnt/storage";

    /// <summary>Dialogporten's JWKS for the dialog tokens, by service name (the token's issuer is checked against <c>DialogportenSettings:Issuer</c>).</summary>
    public string DialogTokenJwksUrl { get; set; } = "http://dialogporten-webapi/dialogporten/api/v1/.well-known/jwks.json";

    /// <summary>The least time between two fetches of Dialogporten's JWKS when a dialog token names an unknown key.</summary>
    public int DialogTokenKeyRefreshSeconds { get; set; } = 10;
}
