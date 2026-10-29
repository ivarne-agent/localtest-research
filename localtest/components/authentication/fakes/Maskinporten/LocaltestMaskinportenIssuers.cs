#nullable enable
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace Altinn.Platform.Authentication.Localtest.Maskinporten;

/// <summary>
/// The Maskinporten instances whose tokens <c>exchange/maskinporten</c> accepts on the stack:
/// <c>Localtest:Authentication:MaskinportenWellKnownEndpoints</c>, a list the manifest sets (the stack's fake, test and
/// production Maskinporten). Production has two settings (<c>MaskinportenWellKnownConfigEndpoint</c> and its
/// alternative) and validates a token against the keys of either; here the token's <c>iss</c> picks one endpoint, by
/// production's own rule (the well-known URL contains the issuer), and the token is validated against that endpoint's
/// keys alone, so each issuer is held to its own metadata. Only the chosen endpoint is fetched: a test or production
/// Maskinporten that cannot be reached (offline) fails its own tokens, never the fake's.
/// </summary>
public static class LocaltestMaskinportenIssuers
{
    /// <summary>The trusted well-known endpoints, set once at start from configuration.</summary>
    public static IReadOnlyList<string> WellKnownEndpoints { get; set; } = [];

    /// <summary>The trusted well-known endpoint for the token's (unvalidated) issuer, or "" when none is trusted.</summary>
    public static string WellKnownFor(string token)
    {
        var issuer = new JwtSecurityTokenHandler().ReadJwtToken(token).Issuer;
        return string.IsNullOrEmpty(issuer)
            ? string.Empty
            : WellKnownEndpoints.FirstOrDefault(e => e.Contains(issuer, StringComparison.Ordinal)) ?? string.Empty;
    }
}
