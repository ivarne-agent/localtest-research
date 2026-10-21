#nullable enable
using System.Collections.Generic;
using Altinn.Platform.Authentication.Core.Models.Oidc;

namespace Altinn.Platform.Authentication.Localtest;

/// <summary>Settings for the localtest Authentication fakes, bound from <c>Localtest:Authentication</c>.</summary>
public sealed class LocaltestAuthenticationOptions
{
    /// <summary>
    /// Base URL this service uses for its own platform calls (openid metadata, the fake ID-porten's token endpoint and
    /// metadata, register, Profile).
    /// Defaults to <c>GeneralSettings:PlatformEndpoint</c>; in compose it is the localtest app's service name.
    /// </summary>
    public string? InternalPlatformEndpoint { get; set; }

    /// <summary>
    /// Base URL of the Profile API the production <c>ProfileService</c> calls (<c>internal/user</c>), answered by the
    /// profile component. Defaults to <c>{InternalPlatformEndpoint}profile/api/v1/</c>.
    /// </summary>
    public string? ProfileEndpoint { get; set; }

    /// <summary>
    /// The registered OIDC clients (production's <c>oidcserver.client</c> rows), seeded into
    /// <see cref="Persistence.InMemoryOidcServerClientRepository"/> at start. Two are configured by default
    /// (<c>localtest-smoke</c>, <c>arbeidsflate-localtest</c>, see <c>LocaltestAuthenticationExtensions</c>); more are
    /// added as <c>Localtest:Authentication:OidcClients:{n}:*</c>.
    /// </summary>
    public List<LocaltestOidcClient> OidcClients { get; set; } = [];
}

/// <summary>
/// One registered client. The plain <see cref="ClientSecret"/> lives only in localtest configuration; the repository
/// stores production's HMAC hash of it (<c>ClientSecretHasher.HashHmac</c> with the decoded
/// <c>GeneralSettings:OidcRefreshTokenPepper</c>), so production's verifier runs unchanged.
/// </summary>
public sealed class LocaltestOidcClient
{
    public string? ClientId { get; set; }

    public string? ClientName { get; set; }

    public string? ClientSecret { get; set; }

    public ClientType ClientType { get; set; } = ClientType.Confidential;

    public TokenEndpointAuthMethod TokenEndpointAuthMethod { get; set; } = TokenEndpointAuthMethod.ClientSecretBasic;

    /// <summary>Exact strings: production compares the requested <c>redirect_uri</c> ordinally.</summary>
    public List<string> RedirectUris { get; set; } = [];

    /// <summary>Kept on the client for completeness; production's end-session never reads it (see the README).</summary>
    public List<string> PostLogoutRedirectUris { get; set; } = [];

    /// <summary>Called server-side by production's logout with <c>sid</c> and <c>iss</c>.</summary>
    public string? FrontchannelLogoutUri { get; set; }

    public string? BackchannelLogoutUri { get; set; }

    /// <summary>Lower-case: production lower-cases the allowed scopes and compares the requested ones as sent.</summary>
    public List<string> AllowedScopes { get; set; } = [];

    public bool RequirePkce { get; set; } = true;

    public bool RequireNonce { get; set; } = true;

    public SubjectType SubjectType { get; set; } = SubjectType.Public;
}
