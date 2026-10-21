#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Authorization.Clients.Interfaces;
using Altinn.Platform.Authorization.Localtest.Infrastructure;
using Altinn.Platform.Authorization.Localtest.Pip;
using Altinn.Platform.Authorization.Localtest.Policies;
using Altinn.Platform.Authorization.Repositories.Interface;
using Altinn.Platform.Authorization.Services.Implementation;
using Altinn.Platform.Authorization.Services.Interface;
using AltinnCore.Authentication.JwtCookie;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Platform.Authorization.Localtest;

/// <summary>
/// Localtest composition for the vendored PDP, called from the single <c>#if LOCALTEST</c> block in <c>Program.cs</c>
/// right after the production <c>ConfigureServices</c>. Replaces the Azure Blob policy store, the OED PIP, the audit queue
/// and the Key Vault backed token handling. Register (v1 parties), Profile (<c>internal/user</c>, answered by the profile
/// component), Storage (instance authinfo), Access Management (roles, access packages, delegations) and the Resource
/// Registry (resources, resource policies, access lists) stay production HTTP clients pointed at the localtest platform.
/// </summary>
public static class LocaltestAuthorizationExtensions
{
    private const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public static WebApplicationBuilder AddLocaltestAuthorization(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var platform = (config["Localtest:Authorization:PlatformEndpoint"] is { Length: > 0 } configured ? configured : DefaultPlatformEndpoint).TrimEnd('/') + "/";

        // Localtest values for settings the vendored appsettings.json carries at22/localhost values for. Inserted above
        // the JSON files but below the environment variables, so a single endpoint can still be pointed elsewhere
        // (a standalone run without the ingress). The production services bind these lazily through IOptions.
        var defaults = new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["Localtest:Authorization:PlatformEndpoint"] = platform,
                ["PlatformSettings:ApiRegisterEndpoint"] = platform + "register/api/v1/",
                ["PlatformSettings:ApiStorageEndpoint"] = platform + "storage/api/v1/",
                ["PlatformSettings:ApiResourceRegistryEndpoint"] = platform + "resourceregistry/api/v1/",
                ["PlatformSettings:ApiAccessManagementEndpoint"] = platform + "accessmanagement/api/v1/",
                ["PlatformSettings:ApiProfileEndpoint"] = platform + "profile/api/v1/",
                ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
                ["GeneralSettings:RuntimeCookieName"] = "AltinnStudioRuntime",
                ["GeneralSettings:BridgeApiEndpoint"] = platform + "sblbridge/",
                ["GeneralSettings:SBLBaseAdress"] = platform,

                // Production caches for whole minutes; 1 is the smallest valid value. App policies bypass it (see
                // LocaltestPolicyRetrievalPoint), so this mainly bounds profile and party lookups.
                ["GeneralSettings:PolicyCacheTimeout"] = "1",
                ["GeneralSettings:RoleCacheTimeout"] = "1",
                ["GeneralSettings:MainUnitCacheTimeout"] = "1",
                ["GeneralSettings:KeyrolePartyIdsCacheTimeout"] = "1",

                // Feature flags as in production (manifests/deploy/prod/config/environment.env), except the audit log.
                ["FeatureManagement:AccessManagementAuthorizedParties"] = "true",
                ["FeatureManagement:SystemUserAccessPackageAuthorization"] = "true",
                ["FeatureManagement:UserAccessPackageAuthorization"] = "true",
                ["FeatureManagement:DecisionRequestLogRequestOnError"] = "true",
            },
        };
        var lastEnvironmentSource = config.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        config.Sources.Insert(lastEnvironmentSource < 0 ? config.Sources.Count : lastEnvironmentSource, defaults);

        // Cannot be anything else without Azure: forced over every other source.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:AuditLog"] = "false",
            ["FeatureManagement:AuditLogDuplicateMeasurement"] = "false",
        });

        var services = builder.Services;
        services.AddOptions<LocaltestAuthorizationOptions>().BindConfiguration("Localtest:Authorization");
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient();

        // ConfigureServices bound GeneralSettings eagerly for the JWT cookie scheme, before the defaults above existed.
        // Configure (not PostConfigure) runs before the scheme's own post-configure builds the metadata manager.
        services.Configure<JwtCookieOptions>(JwtCookieDefaults.AuthenticationScheme, o => o.MetadataAddress = config["GeneralSettings:OpenIdWellKnownEndpoint"]);

        // Policies: a file store (written by the production policies endpoint, which the app runner calls) instead of
        // Azure Blob; the production PRP, without its cache for app policies.
        services.RemoveAll<IPolicyRepository>();
        services.AddSingleton<IPolicyRepository, LocaltestPolicyRepository>();
        services.TryAddSingleton<PolicyRetrievalPoint>();
        Replace<IPolicyRetrievalPoint, LocaltestPolicyRetrievalPoint>(services);

        // PIPs. Roles, access packages, delegation changes and authorized parties come from the real Access Management
        // component through the production AccessManagementWrapper (registered by Program.cs). The Altinn 2 roles
        // endpoint (IRoles, SBL bridge) keeps its production wrapper and answers empty: nothing locally calls it.
        Replace<IOedRoleAssignmentWrapper, NoOedRoleAssignments>(services);

        // Azure: no audit queue, no Key Vault.
        Replace<IEventsQueueClient, NoopEventsQueueClient>(services);
        // Platform access tokens: production generator and handler, with the shared localtest certificate standing in
        // for Key Vault on both sides (AccessTokenSettings:AccessTokenSigningCertificateFullPath).
        // Production passes the generator a Key Vault certificate explicitly and never binds the client-side settings,
        // so the resolver's AccessTokenSigningCertificateFullPath would be empty without this.
        services.Configure<Altinn.Common.AccessTokenClient.Configuration.AccessTokenSettings>(builder.Configuration.GetSection("AccessTokenSettings"));
        services.RemoveAll<IAccessTokenGenerator>();
        services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

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
