#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Authentication.Core.RepositoryInterfaces;
using Altinn.Platform.Authentication.Localtest.Identity;
using Altinn.Platform.Authentication.Localtest.Keys;
using Altinn.Platform.Authentication.Localtest.Persistence;
using Altinn.Platform.Authentication.Services.Interfaces;
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
/// repositories use TryAdd, so registering the in-memory ones first is enough. Replaces the upstream provider with the picker,
/// Profile with the register seed, Postgres with memory, and Key Vault with a local key.
/// </summary>
public static class LocaltestAuthenticationExtensions
{
    public static WebApplicationBuilder AddLocaltestAuthentication(this WebApplicationBuilder builder)
    {
        var platform = (builder.Configuration["GeneralSettings:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : "http://local.altinn.cloud:8000/").TrimEnd('/') + "/";
        var authn = platform + "authentication/api/v1/";

        // Lowest-priority defaults; environment variables and appsettings still win. Provider settings are bound
        // lazily through options, so adding them here, after the production registrations, is in time.
        builder.Configuration.Sources.Insert(0, new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Altinn:LocalDev"] = "true",
                ["GeneralSettings:JwtCookieName"] = "AltinnStudioRuntime",
                ["GeneralSettings:OidcRefreshTokenPepper"] = "localtest-pepper",
                ["Localtest:Authentication:RegisterLocaltestEndpoint"] = platform + "register/api/localtest/",

                // The one upstream provider: its authorization endpoint is the picker page in this host.
                ["OidcProviders:idporten:Issuer"] = authn + "localtest/idporten/",
                ["OidcProviders:idporten:AuthorizationEndpoint"] = authn + "localtest/login",
                ["OidcProviders:idporten:TokenEndpoint"] = authn + "localtest/idporten/token",
                ["OidcProviders:idporten:WellKnownConfigEndpoint"] = authn + "localtest/idporten/.well-known/openid-configuration",
                ["OidcProviders:idporten:ClientId"] = "localtest",
                ["OidcProviders:idporten:Scope"] = "openid",
                ["OidcProviders:idporten:DefaultAuthenticationMethod"] = "NotDefined",
                ["OidcProviders:idporten:ProviderClaims:0"] = "actual_iss",
                ["OidcProviders:idporten:AuthLevels:0:Acr"] = "idporten-loa-substantial",
                ["OidcProviders:idporten:AuthLevels:0:Level"] = "Sensitive",
                ["OidcProviders:idporten:AuthLevels:0:ClaimValues:0"] = "idporten-loa-substantial",
                ["OidcProviders:idporten:AuthLevels:0:ClaimValues:1"] = "Level3",
                ["OidcProviders:idporten:AuthLevels:1:Acr"] = "idporten-loa-high",
                ["OidcProviders:idporten:AuthLevels:1:Level"] = "VerySensitive",
                ["OidcProviders:idporten:AuthLevels:1:ClaimValues:0"] = "idporten-loa-high",
                ["OidcProviders:idporten:AuthLevels:1:ClaimValues:1"] = "Level4",
            },
        });

        // Settings that follow from PlatformEndpoint or cannot be anything else without a database or Azure: forced over
        // the vendored appsettings (which carry at22 values) and the environment. PlatformEndpoint itself stays configurable.
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostgreSQLSettings:EnableDBConnection"] = "false",
            ["Altinn:Npgsql:authentication:Migrate:Enabled"] = "false",
            ["FeatureManagement:AuditLog"] = "false",
            ["kvSetting:SecretUri"] = string.Empty,
            ["PlatformSettings:ApiNotificationsEndpoint"] = platform + "notifications/api/v1/",
            ["PlatformSettings:ApiProfileEndpoint"] = platform + "profile/api/v1/",
            ["PlatformSettings:ApiResourceRegistryEndpoint"] = platform + "resourceregistry/api/v1/",
            ["PlatformSettings:ApiRegisterInternalEndpoint"] = platform + "register/api/v2/internal/",
            ["PlatformSettings:ApiRegisterEndpoint"] = platform + "register/api/v1/",
            ["PlatformSettings:ApiAuthorizationEndpoint"] = platform + "authorization/api/v1/",
            ["GeneralSettings:IdPortenAlternativeWellKnownConfigEndpoint"] = authn + "localtest/idporten/.well-known/openid-configuration",
            ["GeneralSettings:IdPortenWellKnownConfigEndpoint"] = authn + "localtest/idporten/.well-known/openid-configuration",
            ["GeneralSettings:MaskinportenWellKnownAlternativeConfigEndpoint"] = authn + "localtest/maskinporten/.well-known/oauth-authorization-server",
            ["GeneralSettings:MaskinportenWellKnownConfigEndpoint"] = authn + "localtest/maskinporten/.well-known/oauth-authorization-server",
            ["GeneralSettings:DefaultOidcProvider"] = "idporten",
            ["GeneralSettings:OpenIdWellKnownEndpoint"] = authn + "openid/",
            ["GeneralSettings:PlatformEndpoint"] = platform,
            ["GeneralSettings:BaseUrl"] = platform.TrimEnd('/'),
            ["GeneralSettings:HostName"] = new Uri(platform).Host,
        });

        var services = builder.Services;
        services.AddOptions<LocaltestAuthenticationOptions>().BindConfiguration("Localtest:Authentication");
        services.TryAddSingleton(TimeProvider.System);

        // Upstream identity: the picker instead of ID-porten.
        services.AddSingleton<LocaltestTicketStore>();
        Replace<IOidcProvider, LocaltestOidcProvider>(services);
        Replace<IUpstreamTokenValidator, LocaltestUpstreamTokenValidator>(services);

        // Profile lookup from the register seed instead of the Profile service.
        services.AddHttpClient<RegisterLocaltestClient>();
        services.RemoveAll<IProfile>();
        services.AddSingleton<IProfile, LocaltestProfile>();

        // Keys: no Key Vault, http metadata allowed.
        services.AddSingleton<LocaltestSigningKeys>();
        services.RemoveAll<ISigningKeysRetriever>();
        services.AddHttpClient<ISigningKeysRetriever, LocaltestSigningKeysRetriever>();
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Postgres: the three tables the browser flow, refresh and logout touch live in memory. The production
        // persistence layer registers its repositories with TryAdd after this, so these win.
        services.AddSingleton<IUnregisteredClientRepository, InMemoryUnregisteredClientRequestRepository>();
        services.AddSingleton<IUpstreamLoginTransactionRepository, InMemoryUpstreamLoginTransactionRepository>();
        services.AddSingleton<IOidcSessionRepository, InMemoryOidcSessionRepository>();

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
