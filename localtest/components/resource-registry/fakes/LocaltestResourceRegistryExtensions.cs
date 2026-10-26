#nullable enable
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.ResourceRegistry.Core;
using Altinn.ResourceRegistry.Core.Services.Interfaces;
using Altinn.ResourceRegistry.Localtest.Applications;
using Altinn.ResourceRegistry.Localtest.Infrastructure;
using Altinn.ResourceRegistry.Localtest.Policies;
using Altinn.ResourceRegistry.Localtest.Seed;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.ResourceRegistry.Localtest;

/// <summary>
/// The localtest hooks in the vendored host's <c>ResourceRegistryHost.Create</c>: <see cref="AddLocaltestConfiguration"/>
/// right after the builder is created (configuration defaults, before <c>MapPostgreSqlConfiguration</c> reads
/// <c>PostgreSQLSettings</c> eagerly) and <see cref="AddLocaltestResourceRegistry"/> right before <c>Build()</c> (the
/// DI replacements for Azure Blob, Key Vault and the application cache, and the built-in seed). Everything else in the
/// host (controllers, the service layer, the Postgres repositories and migrations, the org list from altinncdn.no,
/// platform access token and user token validation) runs as in production.
/// </summary>
public static class LocaltestResourceRegistryExtensions
{
    private const string Section = "Localtest:ResourceRegistry";

    /// <summary>Configuration defaults; called first thing in <c>ResourceRegistryHost.Create</c>.</summary>
    public static WebApplicationBuilder AddLocaltestConfiguration(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var options = new LocaltestResourceRegistryOptions();
        config.GetSection(Section).Bind(options);
        var platform = options.PlatformEndpoint.TrimEnd('/') + "/";
        var db = $"Host={options.DatabaseHost};Port={options.DatabasePort};Database={options.DatabaseName}";

        // Localtest values for settings the vendored appsettings.json carries at22/localhost values for. Inserted above
        // the JSON files but below the environment variables, so a single value can still be overridden per service.
        var defaults = new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                [$"{Section}:PlatformEndpoint"] = platform,
                [$"{Section}:PolicyStorePath"] = options.PolicyStorePath,

                // Database: the production connection strings and roles on the shared container (postgres-init/40-resource-registry.sh).
                ["PostgreSQLSettings:EnableDBConnection"] = "true",
                ["PostgreSQLSettings:AdminConnectionString"] = $"{db};Username=platform_authorization_admin;Password={{0}}",
                ["PostgreSQLSettings:ConnectionString"] = $"{db};Username=platform_authorization;Password={{0}};Include Error Detail=True",
                ["PostgreSQLSettings:AuthorizationDbAdminPwd"] = options.DatabasePassword,
                ["PostgreSQLSettings:AuthorizationDbPwd"] = options.DatabasePassword,

                // Other platform services by service name (http://<service>/<prefix>/…, resolved from
                // services__<service>__http__0; fakes/ServiceDiscovery). The issuer and the OpenID metadata (fetched outside
                // the factory, naming a public jwks_uri) stay on the public origin; the org list on altinncdn.no.
                ["PlatformSettings:StorageApiEndpoint"] = "http://storage/storage/api/v1/",
                ["PlatformSettings:ApiRegisterEndpoint"] = "http://register/register/api/v1/",
                ["PlatformSettings:ApiAccessManagementEndpoint"] = "http://access-management/accessmanagement/api/v1/",
                ["OidcProviders:altinn:Issuer"] = platform + "authentication/api/v1/openid/",
                ["OidcProviders:altinn:WellKnownConfigEndpoint"] = platform + "authentication/api/v1/openid/.well-known/openid-configuration",
            },
        };
        var lastEnvironmentSource = config.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        config.Sources.Insert(lastEnvironmentSource < 0 ? config.Sources.Count : lastEnvironmentSource, defaults);

        // Forced over every other source: no Key Vault (the signing keys come from the shared certificate below).
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["kvSetting:SecretUri"] = string.Empty,
        });

        return builder;
    }

    /// <summary>DI replacements and the seed; called right before <c>Build()</c>, after every production registration.</summary>
    public static WebApplicationBuilder AddLocaltestResourceRegistry(this WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var services = builder.Services;
        services.AddOptions<LocaltestResourceRegistryOptions>().BindConfiguration(Section);

        // Service discovery: AddAltinnServiceDefaults registered AddServiceDiscovery() (with the pass-through provider)
        // and the factory clients' handler; this drops the pass-through, so a service name without an entry fails at
        // once, and keeps real hosts (altinncdn.no, the public origin) as they are.
        global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders(services);

        // Telemetry (TODO.md task 53): AddAltinnServiceDefaults already exports logs, metrics and traces over OTLP exactly
        // when OTEL_EXPORTER_OTLP_ENDPOINT is set, but its resource detector names the service, so OTEL_SERVICE_NAME was
        // ignored. With the variable set this adds the environment detector after it, so the standard variables win
        // (fakes/Otlp/, a copy of ../shared/fakes/Otlp/); nothing otherwise. Not AddLocaltestOtlp: UseOtlpExporter()
        // throws next to ServiceDefaults' own exporters.
        global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlpResource(services.AddOpenTelemetry(), builder.Configuration);

        // Platform access tokens: the production handler validates the apps' and components' tokens against the shared
        // test certificate (AccessTokenSettings:AccessTokenSigningCertificateFullPath) instead of Key Vault keys. The
        // outgoing token (Register access-list calls) comes from the production generator, which production feeds from
        // Key Vault and never binds the client-side settings for, hence the Configure.
        services.Configure<Altinn.Common.AccessTokenClient.Configuration.AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Azure Blob: the two policy containers as folders on the policy store shared with the PDP.
        Replace<IPolicyRepository, LocaltestFilePolicyRepository>(services);

        // No 10-minute cache of Storage's application list: a pushed app is in resourcelist at once.
        services.RemoveAll<IApplications>();
        services.AddHttpClient<IApplications, LocaltestApplicationsClient>();

        // User tokens from the localtest authentication component: OpenID metadata over http (see LocaltestOidcMetadata).
        LocaltestOidcMetadata.AllowHttpProviders(config);

        // Non-app resources, applied at start through the production service layer.
        services.AddHostedService<LocaltestSeedService>();

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
