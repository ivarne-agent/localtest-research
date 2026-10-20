#nullable enable
using System.Collections.Generic;

namespace Altinn.Platform.Authentication.Localtest;

/// <summary>
/// The registered OIDC clients seeded by default (<c>Localtest:Authentication:OidcClients</c>, lowest-priority
/// configuration, so an environment variable such as <c>Localtest__Authentication__OidcClients__1__ClientSecret</c>
/// overrides one value and <c>…__OidcClients__2__…</c> adds a client).
/// <list type="bullet">
///   <item><c>localtest-smoke</c>: the localtest app's code-flow helper (<c>localtest/api/authentication/token</c>), which
///   redirects to the helper's own callback. The localtest app keeps the same id, secret and redirect URI
///   (<c>localtest/localtest/Authentication/SeededClients.cs</c>); keep the two in step.</item>
///   <item><c>arbeidsflate-localtest</c>: Arbeidsflate's BFF, with the URLs it will run on
///   (<c>planned-components/dialogporten/arbeidsflate.md</c> section 3.4).</item>
/// </list>
/// Both may use the four scopes Arbeidsflate's BFF requests, lower-case.
/// </summary>
public static class LocaltestOidcClients
{
    public const string SmokeClientId = "localtest-smoke";
    public const string SmokeClientSecret = "localtest-smoke-secret";
    public const string ArbeidsflateClientId = "arbeidsflate-localtest";
    public const string ArbeidsflateClientSecret = "arbeidsflate-localtest-secret";
    public const string ArbeidsflateOrigin = "http://af.local.altinn.cloud:8000/";

    private static readonly string[] Scopes =
    [
        "openid",
        "digdir:dialogporten.noconsent",
        "altinn:portal/enduser",
        "altinn:correspondence.forward.check",
    ];

    /// <summary>The configuration entries; <paramref name="platform"/> is the public platform endpoint with a trailing slash.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> ConfigurationDefaults(string prefix, string platform)
    {
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
                RedirectUris = [ArbeidsflateOrigin + "api/cb"],
                PostLogoutRedirectUris = [ArbeidsflateOrigin],
                FrontchannelLogoutUri = ArbeidsflateOrigin + "api/frontchannel-logout",
                AllowedScopes = [.. Scopes],
            },
        };

        for (var i = 0; i < clients.Length; i++)
        {
            var c = clients[i];
            var p = $"{prefix}:{i}:";
            yield return new(p + nameof(c.ClientId), c.ClientId);
            yield return new(p + nameof(c.ClientName), c.ClientName);
            yield return new(p + nameof(c.ClientSecret), c.ClientSecret);
            yield return new(p + nameof(c.ClientType), "Confidential");
            yield return new(p + nameof(c.TokenEndpointAuthMethod), "ClientSecretBasic");
            yield return new(p + nameof(c.FrontchannelLogoutUri), c.FrontchannelLogoutUri);
            yield return new(p + nameof(c.RequirePkce), "true");
            yield return new(p + nameof(c.RequireNonce), "true");
            yield return new(p + nameof(c.SubjectType), "Pairwise");
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
