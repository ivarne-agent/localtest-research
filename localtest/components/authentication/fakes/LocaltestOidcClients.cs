#nullable enable
using System;
using System.Collections.Generic;

namespace Altinn.Platform.Authentication.Localtest;

/// <summary>
/// The registered OIDC clients seeded by default (<c>Localtest:Authentication:OidcClients</c>, lowest-priority
/// configuration, so an environment variable such as <c>Localtest__Authentication__OidcClients__1__ClientSecret</c>
/// overrides one value and <c>…__OidcClients__3__…</c> adds a client).
/// <list type="bullet">
///   <item><c>localtest-smoke</c>: the localtest app's code-flow helper (<c>localtest/api/authentication/token</c>), which
///   redirects to the helper's own callback. The localtest app keeps the same id, secret and redirect URI
///   (<c>localtest/localtest/Authentication/SeededClients.cs</c>); keep the two in step.</item>
///   <item><c>arbeidsflate-localtest</c>: Arbeidsflate's BFF, with the URLs it runs on: <c>af.</c> and the public host,
///   on the public port (<see cref="ArbeidsflateOrigin"/>; <c>planned-components/dialogporten/arbeidsflate.md</c> section
///   3.4).</item>
///   <item><c>correspondence-localtest</c>: Correspondence's own OIDC client, which its attachment download challenges
///   when a browser follows an inbox link without a token (<c>DependencyInjection.cs</c>, <c>AddOpenIdConnect</c>); it
///   asks for <c>openid profile</c> only and gets the session's scope in the access token.</item>
/// </list>
/// The first two may use the four scopes Arbeidsflate's BFF requests, lower-case.
/// </summary>
public static class LocaltestOidcClients
{
    public const string SmokeClientId = "localtest-smoke";
    public const string SmokeClientSecret = "localtest-smoke-secret";
    public const string ArbeidsflateClientId = "arbeidsflate-localtest";
    public const string ArbeidsflateClientSecret = "arbeidsflate-localtest-secret";
    public const string CorrespondenceClientId = "correspondence-localtest";
    public const string CorrespondenceClientSecret = "correspondence-localtest-secret";

    private static readonly string[] Scopes =
    [
        "openid",
        "digdir:dialogporten.noconsent",
        "altinn:portal/enduser",
        "altinn:correspondence.forward.check",
    ];

    /// <summary>
    /// Arbeidsflate's origin with a trailing slash: the public platform endpoint's host with <c>af.</c> in front, on the
    /// same scheme and port (<c>http://af.local.altinn.cloud:8000/</c> by default), as the runners route it.
    /// </summary>
    public static string ArbeidsflateOrigin(string platform)
    {
        var uri = new Uri(platform);
        return new UriBuilder(uri) { Host = "af." + uri.Host, Path = "/" }.Uri.GetLeftPart(UriPartial.Authority) + "/";
    }

    /// <summary>The configuration entries; <paramref name="platform"/> is the public platform endpoint with a trailing slash.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> ConfigurationDefaults(string prefix, string platform)
    {
        var arbeidsflate = ArbeidsflateOrigin(platform);
        var clients = new[]
        {
            new LocaltestOidcClient
            {
                ClientId = SmokeClientId,
                ClientName = "Localtest code-flow helper",
                ClientSecret = SmokeClientSecret,
                RedirectUris = [platform + "localtest/api/authentication/callback"],
                PostLogoutRedirectUris = [platform],
                AllowedScopes = [.. Scopes],
            },
            new LocaltestOidcClient
            {
                ClientId = ArbeidsflateClientId,
                ClientName = "Arbeidsflate (localtest)",
                ClientSecret = ArbeidsflateClientSecret,
                RedirectUris = [arbeidsflate + "api/cb"],
                PostLogoutRedirectUris = [arbeidsflate],
                FrontchannelLogoutUri = arbeidsflate + "api/frontchannel-logout",
                AllowedScopes = [.. Scopes],
            },
            new LocaltestOidcClient
            {
                ClientId = CorrespondenceClientId,
                ClientName = "Correspondence attachment download (localtest)",
                ClientSecret = CorrespondenceClientSecret,
                // Correspondence forces the redirect URI to {CorrespondenceBaseUrl}/correspondence/api/v1/idporten-callback.
                RedirectUris = [platform + "correspondence/api/v1/idporten-callback"],
                AllowedScopes = ["openid", "profile"],
            },
        };

        for (var i = 0; i < clients.Length; i++)
        {
            var c = clients[i];
            var p = $"{prefix}:{i}:";
            yield return new(p + nameof(c.ClientId), c.ClientId);
            yield return new(p + nameof(c.ClientName), c.ClientName);
            yield return new(p + nameof(c.ClientSecret), c.ClientSecret);
            yield return new(p + nameof(c.ClientType), c.ClientType.ToString());
            yield return new(p + nameof(c.TokenEndpointAuthMethod), c.TokenEndpointAuthMethod.ToString());
            yield return new(p + nameof(c.FrontchannelLogoutUri), c.FrontchannelLogoutUri);
            yield return new(p + nameof(c.RequirePkce), c.RequirePkce.ToString());
            yield return new(p + nameof(c.RequireNonce), c.RequireNonce.ToString());
            yield return new(p + nameof(c.SubjectType), c.SubjectType.ToString());
            foreach (var entry in List(p + nameof(c.RedirectUris), c.RedirectUris))
            {
                yield return entry;
            }

            foreach (var entry in List(p + nameof(c.PostLogoutRedirectUris), c.PostLogoutRedirectUris))
            {
                yield return entry;
            }

            foreach (var entry in List(p + nameof(c.AllowedScopes), c.AllowedScopes))
            {
                yield return entry;
            }
        }
    }

    private static IEnumerable<KeyValuePair<string, string?>> List(string key, List<string> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            yield return new($"{key}:{i}", values[i]);
        }
    }
}
