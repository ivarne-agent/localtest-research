#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;
using Altinn.Platform.Authentication.Localtest.Keys;
using Altinn.Platform.Authentication.Localtest.Login;
using Altinn.Platform.Authentication.Localtest.Persistence;
using Altinn.Platform.Authentication.Services.Interfaces;
using Localtest.Shared.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.Platform.Authentication.Localtest;

/// <summary>
/// Localtest composition for the vendored Authentication host, called from the single <c>#if LOCALTEST</c> block in
/// <c>AuthenticationHost.cs</c>, placed just before the persistence layer: its settings are read eagerly there, and its
/// repositories use TryAdd, so registering the in-memory ones first is enough. Points the upstream provider at the
/// localtest app's fake ID-porten, and replaces Postgres with memory, and Key Vault with a local key. Profile is the production client, answered by the register component.
/// Seeds the registered OIDC clients (<see cref="LocaltestOidcClients"/>).
/// </summary>
public static class LocaltestAuthenticationExtensions
{
    public static WebApplicationBuilder AddLocaltestAuthentication(this WebApplicationBuilder builder)
    {
        var platform = (builder.Configuration["GeneralSettings:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : "http://local.altinn.cloud:8000/").TrimEnd('/') + "/";

        // URLs the browser follows, issuers and the metadata whose URL must contain a token's issuer use the public
        // platform endpoint; calls this service makes to another service use its service name (http://<service>/<prefix>/…,
        // resolved from services__<service>__http__0; fakes/ServiceDiscovery).
        // The fake ID-porten in the localtest app (localtest/localtest/IdPorten/); its issuer is the public URL, and the
        // token endpoint and the metadata (whose issuer is checked against the configured one) are called by name.
        var idporten = platform + "localtest/api/idporten";
        var internalIdporten = "http://localtest/localtest/api/idporten";

        // The fake Maskinporten, also in the localtest app. Production's exchange accepts a token whose iss the
        // well-known URL contains, and the fake's issuer is the public {platform}localtest/api/maskinporten/.
        var maskinportenWellKnown = platform + "localtest/api/maskinporten/.well-known/oauth-authorization-server";
        var profile = builder.Configuration["Localtest:Authentication:ProfileEndpoint"] is { Length: > 0 } profileConfigured
            ? profileConfigured.TrimEnd('/') + "/"
            : "http://profile/profile/api/v1/";

        // Lowest-priority defaults; environment variables and appsettings still win. Provider settings are bound
        // lazily through options, so adding them here, after the production registrations, is in time. AltinnHost has
        // read its own settings by now, so ServiceDefaults runs in environment UNKNOWN; LOCAL is not wanted (it would
        // fetch platform tokens from the remote test token generator).
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            // The level table covers the old localtest's levels 0-4 (LocaltestAuthLevels).
            InitialData = LocaltestAuthLevels.ConfigurationDefaults("OidcProviders:idporten:AuthLevels").Concat(LocaltestOidcClients.ConfigurationDefaults("Localtest:Authentication:OidcClients", platform)).Concat(new Dictionary<string, string?>
            {
                ["GeneralSettings:JwtCookieName"] = "AltinnStudioRuntime",

                // Base64, as production decodes it (Convert.FromBase64String) for client secrets and refresh tokens: the
                // bytes of "localtest-pepper".
                ["GeneralSettings:OidcRefreshTokenPepper"] = "bG9jYWx0ZXN0LXBlcHBlcg==",

                // The one upstream provider: the localtest app's fake ID-porten, whose login page is the test-user picker.
                // The browser goes to authorize; this service calls token and the metadata itself.
                ["OidcProviders:idporten:Issuer"] = idporten,
                ["OidcProviders:idporten:AuthorizationEndpoint"] = idporten + "/authorize",
                ["OidcProviders:idporten:TokenEndpoint"] = internalIdporten + "/token",
                // RP-initiated logout: openid/logout sends the browser here, and the fake sends it back to
                // logout/handleloggedout.
                ["OidcProviders:idporten:LogoutEndpoint"] = idporten + "/endsession",
                ["OidcProviders:idporten:WellKnownConfigEndpoint"] = internalIdporten + "/.well-known/openid-configuration",
                ["OidcProviders:idporten:ClientId"] = "localtest",
                ["OidcProviders:idporten:Scope"] = "openid",
                ["OidcProviders:idporten:DefaultAuthenticationMethod"] = "NotDefined",
                ["OidcProviders:idporten:ProviderClaims:0"] = "actual_iss",

                // ID-porten announces RFC 9207 (authorization_response_iss_parameter_supported in its discovery) and
                // signs id_tokens for the client, so the stricter of production's opt-in checks are on: the callback's
                // iss is checked against the discovery issuer, and the id_token's aud and exact iss are required. The
                // fake ID-porten satisfies both. (Production's own values for these are not public.)
                ["OidcProviders:idporten:ValidateCallbackIssuer"] = "true",
                ["OidcProviders:idporten:StrictIdTokenValidation"] = "true",
            }),
        });

        // Settings that follow from PlatformEndpoint or cannot be anything else without a database or Azure: forced over
        // the vendored appsettings (which carry at22 values) and the environment. PlatformEndpoint itself stays configurable.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostgreSQLSettings:EnableDBConnection"] = "false",
            ["Altinn:Npgsql:authentication:Migrate:Enabled"] = "false",
            ["FeatureManagement:AuditLog"] = "false",
            ["kvSetting:SecretUri"] = string.Empty,
            ["PlatformSettings:ApiNotificationsEndpoint"] = "http://notifications/notifications/api/v1/",
            ["PlatformSettings:ApiProfileEndpoint"] = profile,
            ["PlatformSettings:ApiResourceRegistryEndpoint"] = "http://resource-registry/resourceregistry/api/v1/",
            ["PlatformSettings:ApiRegisterInternalEndpoint"] = "http://register/register/api/v2/internal/",
            ["PlatformSettings:ApiRegisterEndpoint"] = "http://register/register/api/v1/",
            ["PlatformSettings:ApiAuthorizationEndpoint"] = "http://authorization/authorization/api/v1/",
            // The system-user client; upstream's appsettings point at at22.
            ["AccessManagementSettings:ApiAccessManagementEndpoint"] = "http://access-management/accessmanagement/api/v1/",
            ["GeneralSettings:IdPortenAlternativeWellKnownConfigEndpoint"] = internalIdporten + "/.well-known/openid-configuration",
            ["GeneralSettings:IdPortenWellKnownConfigEndpoint"] = internalIdporten + "/.well-known/openid-configuration",
            ["GeneralSettings:MaskinportenWellKnownAlternativeConfigEndpoint"] = maskinportenWellKnown,
            ["GeneralSettings:MaskinportenWellKnownConfigEndpoint"] = maskinportenWellKnown,
            ["GeneralSettings:DefaultOidcProvider"] = "idporten",
            // Its own metadata for the JwtCookie handler (fetched outside the factory, naming a public jwks_uri).
            ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
            ["GeneralSettings:PlatformEndpoint"] = platform,
            ["GeneralSettings:BaseUrl"] = platform.TrimEnd('/'),
            ["GeneralSettings:HostName"] = new Uri(platform).Host,
        });

        var services = builder.Services;
        services.AddOptions<LocaltestAuthenticationOptions>().BindConfiguration("Localtest:Authentication");

        // Service discovery: AddAltinnServiceDefaults registered AddServiceDiscovery() (with the pass-through provider)
        // and the factory clients' handler; this drops the pass-through, so a service name without an entry fails at
        // once, and keeps real hosts (the public origin, altinncdn.no, a client's logout URL) as they are.
        global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders(services);

        // Telemetry: AddAltinnServiceDefaults exports logs, metrics and traces over OTLP exactly when
        // OTEL_EXPORTER_OTLP_ENDPOINT is set, named by its own resource detector, which gives the manifest's name
        // (OTEL_SERVICE_NAME is the same); nothing to add here (TODO.md task 61).
        services.TryAddSingleton(TimeProvider.System);

        // Upstream identity and Profile are production code: the OIDC client and token validation against the localtest
        // app's fake ID-porten and Maskinporten, ProfileService against the register component (settings above).

        // Keys: no Key Vault, http metadata allowed.
        services.RemoveAll<ISigningKeysRetriever>();
        services.AddHttpClient<ISigningKeysRetriever, LocaltestSigningKeysRetriever>();
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Postgres: the tables the browser sign-in, the OIDC provider for registered clients (authorize, code, token,
        // refresh) and logout touch live in memory. The production persistence layer registers its repositories with
        // TryAdd after this, so these win.
        services.AddSingleton<IUnregisteredClientRepository, InMemoryUnregisteredClientRequestRepository>();
        services.AddSingleton<IUpstreamLoginTransactionRepository, InMemoryUpstreamLoginTransactionRepository>();
        services.AddSingleton<IOidcSessionRepository, InMemoryOidcSessionRepository>();
        services.AddSingleton<IOidcServerClientRepository, InMemoryOidcServerClientRepository>();
        services.AddSingleton<ILoginTransactionRepository, InMemoryLoginTransactionRepository>();
        services.AddSingleton<IAuthorizationCodeRepository, InMemoryAuthorizationCodeRepository>();
        services.AddSingleton<IRefreshTokenRepository, InMemoryRefreshTokenRepository>();

        // Cookies: production hard-codes Secure on the runtime, party and session cookies (OidcServerService,
        // AuthenticationController, LogoutController); on the stack's plain http they go without it (a copy of
        // components/shared/fakes/Cookies, see localtest/reviews/cookies.md).
        services.AddLocaltestCookiePolicy();

        // The Npgsql health check would report the missing database; the service's own check stays.
        services.PostConfigure<HealthCheckServiceOptions>(o =>
        {
            foreach (var registration in o.Registrations.Where(r => !string.Equals(r.Name, "authentication_health_check", StringComparison.Ordinal)).ToList())
            {
                o.Registrations.Remove(registration);
            }
        });

        return builder;
    }

    private static void Replace<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddSingleton<TService, TImplementation>();
    }
}
