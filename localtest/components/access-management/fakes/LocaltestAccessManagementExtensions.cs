#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.AccessManagement.Core.Repositories.Interfaces;
using Altinn.AccessManagement.Localtest.Infrastructure;
using Altinn.AccessManagement.Localtest.Policies;
using Altinn.Authorization.Integration.Platform;
using Altinn.Common.AccessToken.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.AccessManagement.Localtest;

/// <summary>
/// The one localtest hook in the vendored host (<c>AccessManagementHost.Create</c>, right before <c>Build()</c>):
/// localtest configuration defaults and the DI replacements for Azure Blob, Key Vault and the test-tool token
/// generator. Everything else in the host runs as in production.
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
                [$"{Section}:BootstrapConnectionString"] = options.BootstrapConnectionString,
                [$"{Section}:DatabasePassword"] = options.DatabasePassword,

                // Database: the production connection strings and roles, on the shared container (see LocaltestDatabase).
                ["PostgreSQLSettings:EnableDBConnection"] = "true",
                ["PostgreSQLSettings:AdminConnectionString"] = $"{db};Username=platform_authorization_admin;Password={{0}}",
                ["PostgreSQLSettings:ConnectionString"] = $"{db};Username=platform_authorization;Password={{0}}",
                ["PostgreSQLSettings:AuthorizationDbAdminPwd"] = options.DatabasePassword,
                ["PostgreSQLSettings:AuthorizationDbPwd"] = options.DatabasePassword,

                // Other platform services, all behind the ingress.
                ["PlatformSettings:ApiAuthenticationEndpoint"] = platform + "authentication/api/v1/",
                ["PlatformSettings:ApiAuthorizationEndpoint"] = platform + "authorization/api/v1/",
                ["PlatformSettings:ApiProfileEndpoint"] = platform + "profile/api/v1/",
                ["PlatformSettings:ApiRegisterEndpoint"] = platform + "register/api/v1/",
                ["PlatformSettings:ApiResourceRegistryEndpoint"] = platform + "resourceregistry/api/",
                ["PlatformSettings:JwtCookieName"] = "AltinnStudioRuntime",
                ["OidcProviders:altinn:Issuer"] = platform + "authentication/api/v1/openid/",
                ["OidcProviders:altinn:WellKnownConfigEndpoint"] = platform + "authentication/api/v1/openid/.well-known/openid-configuration",
                ["GeneralSettings:Hostname"] = new Uri(platform).Host,
                ["SblBridgeSettings:BaseApiUrl"] = platform + "sblbridge/",
                // libs Integration.Platform clients append the full service path (/register/api/v2/...), so the base is the ingress.
                ["Platform:Register:Endpoint"] = platform,
                ["Platform:ResourceRegistry:Endpoint"] = platform,
                ["Platform:SblBridge:Endpoint"] = platform,
                ["Platform:Notifications:Endpoint"] = platform,
                // Validated eagerly by Integration.Platform although the generator is replaced below.
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

        // Cannot be anything else without Azure: forced over every other source.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["kvSetting:SecretUri"] = string.Empty,
            ["Lease:StorageAccount:BlobEndpoint"] = string.Empty,
            ["ApplicationInsights:InstrumentationKey"] = string.Empty,
        });

        return builder;
    }

    /// <summary>DI replacements; called right before <c>Build()</c>, after every production registration.</summary>
    public static WebApplicationBuilder AddLocaltestAccessManagement(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;
        services.AddOptions<LocaltestAccessManagementOptions>().BindConfiguration(Section);

        // Platform access tokens: production generator and handler with the shared test certificate standing in for
        // Key Vault on both sides (AccessTokenSettings:AccessTokenSigningCertificateFullPath). Production hands the
        // generator a Key Vault certificate and never binds the client-side settings, hence the Configure.
        services.Configure<Altinn.Common.AccessTokenClient.Configuration.AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);
        Replace<ITokenGenerator, LocaltestTokenGenerator>(services);

        // Azure Blob: a versioned file store instead of the three blob containers.
        Replace<IPolicyFactory, LocaltestPolicyFactory>(services);

        // Resource Registry: app resources derived from the local Storage's application list and the pushed app
        // policies, as the production Resource Registry derives them. Two stand-ins over one loader: the delegation
        // services' client and the integration client behind the production resource sync (providers, resources,
        // role/package-resource links), which the refresh runs.
        services.AddHttpClient(nameof(ResourceRegistry.LocaltestAppResources), c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ResourceRegistry.LocaltestAppResources>();
        Replace<Altinn.AccessManagement.Core.Clients.Interfaces.IResourceRegistryClient, ResourceRegistry.LocaltestResourceRegistryClient>(services);
        Replace<Altinn.Authorization.Integration.Platform.ResourceRegistry.IAltinnResourceRegistry, ResourceRegistry.LocaltestAltinnResourceRegistry>(services);

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
