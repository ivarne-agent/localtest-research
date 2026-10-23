using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Localtest.IdPorten;
using Localtest.Tokens;

namespace Localtest.Emulated;

/// <summary>
/// The minimal mode's Altinn tokens, built with the claim sets production Authentication issues (vendored at
/// <c>components/authentication/vendor/src/apps/Altinn.Authentication</c>; see the table in <c>../README.md</c>) and
/// signed with the shared test certificate (<c>Localtest:AccessTokenCertificatePath</c>), the key the discovery
/// document's <c>jwks</c> publishes. One key signs user, org, Studio and platform tokens, as in the default stack.
/// </summary>
public sealed class TokenMinter(LocaltestSettings localtest) : IDisposable
{
    /// <summary>The cookie the apps and Storage read the user token from.</summary>
    public const string CookieName = "AltinnStudioRuntime";

    /// <summary>Production's <c>JwtValidityMinutes</c>.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    /// <summary>Production's <c>DefaultPortalScopes</c>, the scopes of a portal sign-in.</summary>
    public const string PortalScopes = "openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read";

    private readonly Lazy<SigningKey> _key = new(() =>
    {
        using var certificate = X509CertificateLoader.LoadPkcs12FromFile(localtest.AccessTokenCertificatePath, password: null);
        return SigningKey.FromCertificate(certificate);
    });

    /// <summary>Production's issuer, <c>{PlatformEndpoint}authentication/api/v1/openid/</c>.</summary>
    public string Issuer { get; } = localtest.PlatformEndpoint + "authentication/api/v1/openid/";

    /// <summary>The key set of the discovery document: the shared certificate's key.</summary>
    public JsonWebKeySet Jwks() => _key.Value.Jwks();

    /// <summary>Validates a token this minter (or anything else holding the shared certificate) signed.</summary>
    public (JsonElement? Payload, string? Error) Validate(string? jwt) => Jwt.Validate(jwt, Jwks());

    /// <summary>
    /// A user's cookie token, as production's sign-in through ID-porten issues it (<c>ClaimsPrincipalBuilder</c>
    /// with <c>isAuthCookie</c>): the session, the party, the user, the acr and level, <c>amr</c> TestID and its method,
    /// the upstream's <c>actual_iss</c> (the provider claim localtest configures), the portal scopes.
    /// </summary>
    public string User(TestDataUser user, AuthLevels.Level level)
    {
        var sessionId = Guid.NewGuid().ToString();
        var now = DateTimeOffset.UtcNow;
        var person = user.Person;
        return _key.Value.CreateJwt(writer =>
        {
            writer.WriteString("sub", "urn:altinn:party:uuid:" + user.Party.PartyUuid);
            writer.WriteString("sid", sessionId);
            writer.WriteString("iss", Issuer);
            writer.WriteString("urn:altinn:party:uuid", user.Party.PartyUuid.ToString());
            writer.WriteString("jti", sessionId);
            writer.WriteNumber("urn:altinn:partyid", user.Party.PartyId);
            writer.WriteString("urn:altinn:userid", user.UserId.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(person.UserName))
            {
                writer.WriteString("urn:altinn:username", person.UserName);
            }

            writer.WriteString("originaliss", "idporten");
            if (user.Party.Kind == PartyKind.SelfIdentified)
            {
                writer.WriteString("email", user.Party.Name);
                writer.WriteString("urn:altinn:party:external-identifier", "urn:altinn:person:idporten-email:" + Uri.EscapeDataString(user.Party.Name));
            }

            writer.WriteString("acr", level.Acr);
            writer.WriteString("actual_iss", "localtest");
            writer.WriteNumber("urn:altinn:authlevel", level.Number);
            writer.WriteStartArray("amr");
            writer.WriteStringValue("TestID");
            writer.WriteEndArray();
            writer.WriteString("urn:altinn:authenticatemethod", "IdportenTestId");
            writer.WriteNumber("auth_time", now.ToUnixTimeSeconds());
            writer.WriteString("scope", PortalScopes);
            WriteLifetime(writer, now);
        });
    }

    /// <summary>
    /// Production's <c>exchange/maskinporten</c>: the Maskinporten token's claims less <c>aud</c>, <c>iss</c>,
    /// <c>client_amr</c> and <c>sid</c> (and its own lifetime), plus the organisation number, method <c>maskinporten</c>,
    /// level 3, the issuer and a session; <c>urn:altinn:org</c> only for a service-owner scope.
    /// </summary>
    public string Org(JsonElement maskinporten, string orgNumber, string? org)
    {
        var now = DateTimeOffset.UtcNow;
        return _key.Value.CreateJwt(writer =>
        {
            foreach (var claim in maskinporten.EnumerateObject())
            {
                if (claim.Name is not ("aud" or "iss" or "client_amr" or "sid" or "iat" or "nbf" or "exp"))
                {
                    claim.WriteTo(writer);
                }
            }

            writer.WriteString("urn:altinn:orgNumber", orgNumber);
            writer.WriteString("urn:altinn:authenticatemethod", "maskinporten");
            writer.WriteNumber("urn:altinn:authlevel", 3);
            if (org is not null)
            {
                writer.WriteString("urn:altinn:org", org);
            }

            writer.WriteString("iss", Issuer);
            writer.WriteString("sid", Guid.NewGuid().ToString());
            WriteLifetime(writer, now);
        });
    }

    /// <summary>
    /// Production's <c>exchange/id-porten</c>: the user's identity claims plus the ID-porten token's claims less
    /// <c>aud</c>, <c>iss</c>, <c>at_hash</c>, <c>sid</c> and <c>sub</c>, a new issuer and session.
    /// </summary>
    public string IdPortenExchange(JsonElement idPorten, TestDataUser user, int level)
    {
        var now = DateTimeOffset.UtcNow;
        return _key.Value.CreateJwt(writer =>
        {
            writer.WriteString("nameid", user.UserId.ToString(CultureInfo.InvariantCulture));
            writer.WriteString("urn:altinn:userid", user.UserId.ToString(CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(user.Person.UserName))
            {
                writer.WriteString("urn:altinn:username", user.Person.UserName);
            }

            writer.WriteNumber("urn:altinn:partyid", user.Party.PartyId);
            writer.WriteString("urn:altinn:party:uuid", user.Party.PartyUuid.ToString());
            writer.WriteString("urn:altinn:authenticatemethod", "IdportenTestId");
            writer.WriteNumber("urn:altinn:authlevel", level);
            foreach (var claim in idPorten.EnumerateObject())
            {
                if (claim.Name is not ("aud" or "iss" or "at_hash" or "sid" or "sub" or "iat" or "nbf" or "exp")
                    && !claim.Name.StartsWith("urn:altinn:", StringComparison.Ordinal))
                {
                    claim.WriteTo(writer);
                }
            }

            writer.WriteString("iss", Issuer);
            writer.WriteString("sid", Guid.NewGuid().ToString());
            WriteLifetime(writer, now);
        });
    }

    /// <summary>
    /// Production's <c>exchange/altinnstudio</c> and <c>refresh</c>: the presented token's claims unchanged, signed again
    /// with a new lifetime (<paramref name="newLifetime"/>; the Studio exchange keeps the presented one).
    /// </summary>
    public string Reissue(JsonElement claims, bool newLifetime)
    {
        var now = DateTimeOffset.UtcNow;
        return _key.Value.CreateJwt(writer =>
        {
            foreach (var claim in claims.EnumerateObject())
            {
                if (!newLifetime || claim.Name is not ("iat" or "nbf" or "exp"))
                {
                    claim.WriteTo(writer);
                }
            }

            if (newLifetime)
            {
                WriteLifetime(writer, now);
            }
        });
    }

    public void Dispose()
    {
        if (_key.IsValueCreated)
        {
            _key.Value.Dispose();
        }
    }

    private static void WriteLifetime(Utf8JsonWriter writer, DateTimeOffset now)
    {
        writer.WriteNumber("nbf", now.ToUnixTimeSeconds());
        writer.WriteNumber("exp", now.Add(Lifetime).ToUnixTimeSeconds());
        writer.WriteNumber("iat", now.ToUnixTimeSeconds());
    }

    /// <summary>A random hex value for states, nonces and verifiers.</summary>
    public static string Random() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
}
