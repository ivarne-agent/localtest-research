#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.AccessManagement.Core.Repositories.Interfaces;
using Altinn.AccessManagement.Localtest.Infrastructure;
using Altinn.AccessManagement.Localtest.Policies;
using Altinn.Common.AccessToken.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.AccessManagement.Localtest;

/// <summary>
/// The localtest hooks in the vendored host's <c>AccessManagementHost.Create</c>: <see cref="AddLocaltestConfiguration"/>
/// right after the builder is created (configuration defaults), and <see cref="AddLocaltestAccessManagement"/> right
/// before <c>Build()</c> (the DI replacements for Azure Blob and Key Vault, and the startup refresh). The test-tool token
/// generator signs locally through a directive in the vendored <c>TokenGenerator.TestTool</c>. Everything else in the host
/// runs as in production.
/// </summary>
public static class LocaltestAccessManagementExtensions
{
    private const string Section = "Localtest:AccessManagement";

    /// <summary>Configuration defaults; called first thing in <c>AccessManagementHost.Create</c>, before any eager read.</summary>
    public static WebApplicationBuilder AddLocaltestConfiguration(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var options = new LocaltestAccessManagementOptions();
        config.GetSection(Section).Bind(options);
        var platform = options.PlatformEndpoint.TrimEnd('/') + "/";
        var db = $"Host={options.DatabaseHost};Port={options.DatabasePort};Database={options.DatabaseName};Include Error Detail=true";

        // Localtest values for settings the vendored appsettings.json carries at22/localhost values for. Inserted above
        // the JSON files but below the environment variables, so a single value can still be overridden per service.
        var defaults = new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                [$"{Section}:PlatformEndpoint"] = platform,
                [$"{Section}:PolicyStorePath"] = options.PolicyStorePath,
                [$"{Section}:DatabaseHost"] = options.DatabaseHost,
                [$"{Section}:DatabasePort"] = options.DatabasePort.ToString(),
                [$"{Section}:DatabaseName"] = options.DatabaseName,

                // Database: the production connection strings and roles, on the shared container
                // (postgres-init/45-access-management.sh). The passwords (AuthorizationDbAdminPwd, AuthorizationDbPwd) come
                // from the manifest, never a default here.
                ["PostgreSQLSettings:EnableDBConnection"] = "true",
                ["PostgreSQLSettings:AdminConnectionString"] = $"{db};Username=platform_authorization_admin;Password={{0}}",
                ["PostgreSQLSettings:ConnectionString"] = $"{db};Username=platform_authorization;Password={{0}}",

                // Other platform services by service name (http://<service>/<prefix>/…, resolved from
                // services__<service>__http__0; fakes/ServiceDiscovery). The issuer, the OpenID metadata (fetched outside the
                // factory, naming a public jwks_uri) and the host of the browser links stay on the public origin.
                ["PlatformSettings:ApiAuthenticationEndpoint"] = "http://authentication/authentication/api/v1/",
                ["PlatformSettings:ApiAuthorizationEndpoint"] = "http://authorization/authorization/api/v1/",
                ["PlatformSettings:ApiProfileEndpoint"] = "http://profile/profile/api/v1/",
                ["PlatformSettings:ApiRegisterEndpoint"] = "http://register/register/api/v1/",
                ["PlatformSettings:ApiResourceRegistryEndpoint"] = "http://resource-registry/resourceregistry/api/",
                ["PlatformSettings:JwtCookieName"] = "AltinnStudioRuntime",
                ["OidcProviders:altinn:Issuer"] = platform + "authentication/api/v1/openid/",
                ["OidcProviders:altinn:WellKnownConfigEndpoint"] = platform + "authentication/api/v1/openid/.well-known/openid-configuration",
                ["GeneralSettings:Hostname"] = new Uri(platform).Host,
                ["SblBridgeSettings:BaseApiUrl"] = platform + "sblbridge/",
                // libs Integration.Platform clients append the full service path (/register/api/v2/...), so the base is the
                // service itself. The SBL bridge does not exist locally (never called).
                ["Platform:Register:Endpoint"] = "http://register/",
                ["Platform:ResourceRegistry:Endpoint"] = "http://resource-registry/",
                ["Platform:SblBridge:Endpoint"] = platform,
                ["Platform:Notifications:Endpoint"] = "http://notifications/",
                // Validated eagerly by Integration.Platform; the test tool is never called (a directive in TokenGenerator.TestTool
                // signs with IAccessTokenGenerator instead).
                ["Platform:Token:TestTool:Endpoint"] = platform,
                ["Platform:Token:TestTool:Username"] = "localtest",
                ["Platform:Token:TestTool:Password"] = "localtest",
                ["Platform:Token:TestTool:Environment"] = "localtest",

                // The init-time Register import runs against the vendored Register; the periodic sync jobs and the outbox stay off.
                ["FeatureManagement:AccessMgmt.Core.HostedServices.RegisterSync"] = "false",
                ["FeatureManagement:AccessMgmt.Core.HostedServices.RegisterSync.Import"] = "true",
                ["FeatureManagement:AccessMgmt.Core.HostedServices.ResourceRegistrySync"] = "false",
                ["FeatureManagement:AccessMgmt.Core.HostedServices.Outbox.Handler"] = "false",
                ["FeatureManagement:AccessMgmt.Core.HostedServices.Outbox.Reaper"] = "false",
                // The internal authorized-parties API the PDP calls (authorizedparties, authorizedparty/{partyId}) sits
                // behind this flag; it is on in production.
                ["FeatureManagement:RightsDelegationApi"] = "true",
            },
        };
        var lastEnvironmentSource = config.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        config.Sources.Insert(lastEnvironmentSource < 0 ? config.Sources.Count : lastEnvironmentSource, defaults);

        // Forced over every other source. ServiceDefaults has already decided against Key Vault (no settings) by now; an
        // empty kvSetting:SecretUri still makes AccessManagementHost.ConfigureLibsIntegrations take the path without Key
        // Vault, and an empty lease blob endpoint keeps the lease provider off Azure Blob.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["kvSetting:SecretUri"] = string.Empty,
            ["Lease:StorageAccount:BlobEndpoint"] = string.Empty,
        });

        return builder;
    }

    /// <summary>DI replacements; called right before <c>Build()</c>, after every production registration.</summary>
    public static WebApplicationBuilder AddLocaltestAccessManagement(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;
        services.AddOptions<LocaltestAccessManagementOptions>().BindConfiguration(Section);

        // Service discovery: AddAltinnServiceDefaults registered AddServiceDiscovery() (with the pass-through provider)
        // and the factory clients' handler; this drops the pass-through, so a service name without an entry fails at
        // once, and keeps real hosts as they are.
        global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders(services);

        // Telemetry: AddAltinnServiceDefaults exports logs, metrics and traces over OTLP exactly when
        // OTEL_EXPORTER_OTLP_ENDPOINT is set, named by its own resource detector, which gives the manifest's name
        // (OTEL_SERVICE_NAME is the same); nothing to add here (TODO.md task 61).

        // Platform access tokens: production generator and handler with the shared test certificate standing in for
        // Key Vault on both sides (AccessTokenSettings:AccessTokenSigningCertificateFullPath). Production hands the
        // generator a Key Vault certificate and never binds the client-side settings, hence the Configure.
        services.Configure<Altinn.Common.AccessTokenClient.Configuration.AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Azure Blob: a versioned file store instead of the three blob containers.
        Replace<IPolicyFactory, LocaltestPolicyFactory>(services);

        // User tokens from the localtest authentication component: OpenID metadata over http (see LocaltestOidcMetadata).
        LocaltestOidcMetadata.AllowHttpProviders(config);

        services.AddHostedService<LocaltestStartupLogger>();
        services.AddScoped<Sync.LocaltestRefreshService>();

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
