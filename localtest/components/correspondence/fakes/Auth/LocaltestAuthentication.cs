using Altinn.Correspondence.Common.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;

namespace Altinn.Correspondence.Localtest.Auth;

/// <summary>
/// The three changes token validation needs against the stack. All are <c>Configure</c> actions registered after
/// upstream's <c>ConfigureAuthentication</c>, so they run after its <c>AddJwtBearer</c>/<c>AddOpenIdConnect</c> lambdas
/// and before the post-configure step that builds each scheme's metadata manager (a <c>PostConfigure</c> would be too
/// late, see the Dialogporten README).
/// <list type="number">
/// <item>Every JWT bearer and OpenID Connect scheme may read its metadata over http: Authentication, the fake
/// Maskinporten and Dialogporten publish theirs on <c>http://local.altinn.cloud:8000</c>.</item>
/// <item>The Maskinporten scheme reads the fake Maskinporten's metadata instead of the hard-coded
/// <c>https://test.maskinporten.no/…</c> (what Notifications' condition check presents).</item>
/// <item>The Dialogporten scheme resolves the dialog token's key through <see cref="LocaltestDialogTokenKeys"/>, which
/// fetches Dialogporten's JWKS again when a token names an unknown key, instead of upstream's list loaded at start and every
/// 12 hours (<c>EdDsaSecurityKeysCacheService</c>, still running but no longer read): a Correspondence started before
/// Dialogporten serves its keys would otherwise refuse every dialog token for 12 hours.</item>
/// </list>
/// </summary>
public static class LocaltestAuthentication
{
    public static void Add(IServiceCollection services, IConfiguration config)
    {
        var platform = LocaltestCorrespondence.Platform(config);

        services.ConfigureAll<JwtBearerOptions>(o => o.RequireHttpsMetadata = false);
        services.ConfigureAll<OpenIdConnectOptions>(o => o.RequireHttpsMetadata = false);

        services.Configure<JwtBearerOptions>(AuthorizationConstants.MaskinportenScheme, o =>
            o.MetadataAddress = platform + "localtest/api/maskinporten/.well-known/oauth-authorization-server");

        services.AddSingleton<LocaltestDialogTokenKeys>();
        services.AddOptions<JwtBearerOptions>(AuthorizationConstants.DialogportenScheme)
            .Configure<LocaltestDialogTokenKeys>((o, keys) =>
                o.TokenValidationParameters.IssuerSigningKeyResolver = (_, _, kid, _) => keys.Resolve(kid));
    }
}
