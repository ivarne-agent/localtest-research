#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Profile.Core.Integrations;
using Altinn.Profile.Integrations.ContactRegister;
using Altinn.Profile.Integrations.OrganizationNotificationAddressRegistry;
using Altinn.Profile.Localtest.Auth;
using Altinn.Profile.Localtest.Brreg;
using Altinn.Profile.Localtest.Krr;
using Altinn.Profile.Localtest.Register;
using Altinn.Profile.Localtest.Seed;
using Altinn.Profile.Localtest.Startup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Altinn.Profile.Localtest;

/// <summary>
/// Localtest composition for the vendored Profile host, called from the two additive <c>#if LOCALTEST</c> blocks in
/// <c>Program.cs</c>. <see cref="AddLocaltestConfiguration"/> runs before any setting is read and supplies what
/// production gets from its deployment; <see cref="AddLocaltestServices"/> runs after the production registrations and
/// replaces only what reaches outside the stack: Key Vault's signing keys, KRR and brreg (both Maskinporten clients).
/// It adds the migrations at start, the seed and the inspection API. Everything else is production code: the
/// controllers, the services, the EF repositories on the shared Postgres, the KRR and organisation sync jobs, the
/// Register, PDP and Notifications clients.
/// </summary>
public static class LocaltestProfileExtensions
{
    public const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public static void AddLocaltestConfiguration(ConfigurationManager config)
    {
        var platform = (config["Localtest:Profile:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : DefaultPlatformEndpoint).TrimEnd('/') + "/";

        // Localtest defaults: override the vendored appsettings.json (localhost ports, kontaktregisteret.no, brreg), but sit
        // just below the environment variables, so GeneralSettings__…, JobSettings__… etc. still win. Calls to another
        // service use its name (http://<service>/<prefix>/…, from services__<service>__http__0, fakes/ServiceDiscovery); the
        // JwtCookie metadata, fetched outside the factory and naming a public jwks_uri, stays on the public origin.
        var defaults = new Dictionary<string, string?>
        {
            ["Localtest:Profile:PlatformEndpoint"] = platform,
            ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
            ["PlatformSettings:ApiAuthorizationEndpoint"] = "http://authorization/authorization/api/v1/",
            ["RegisterSettings:ApiRegisterEndpoint"] = LocaltestProfileOptions.DefaultRegisterEndpoint,
            ["NotificationsSettings:ApiNotificationsEndpoint"] = "http://notifications/notifications/api/",

            // Production: 600 s. A renamed or re-registered app test user shows at once.
            ["CoreSettings:ProfileCacheLifetimeSeconds"] = "10",

            // The two syncs that fill Profile's mirrors from the registers, off by default upstream and on in production,
            // here every minute against the localtest stand-ins (Krr/, Brreg/). Both clients ignore the URLs; they are set
            // to what the stand-ins read, for the jobs' logs.
            ["JobSettings:KrrSyncEnabled"] = "true",
            ["JobSettings:KrrSyncWaitTimeInMinutes"] = "1",
            ["JobSettings:OrgSyncEnabled"] = "true",
            ["JobSettings:OrgSyncWaitTimeInMinutes"] = "1",
            ["ContactAndReservationSettings:ChangesLogEndpoint"] = LocaltestProfileOptions.DefaultRegisterEndpoint + "localtest/krr/rest/v2/personer",
            ["OrganizationNotificationAddressSettings:ChangesLogEndpoint"] = LocaltestProfileOptions.DefaultRegisterEndpoint + "localtest/organizations",
        };

        var source = new MemoryConfigurationSource { InitialData = defaults };
        var lastEnvironmentSource = config.Sources.Select((s, index) => (s, index))
            .LastOrDefault(s => s.s is EnvironmentVariablesConfigurationSource);
        if (lastEnvironmentSource.s is not null)
        {
            config.Sources.Insert(lastEnvironmentSource.index, source);
        }
        else
        {
            config.Sources.Add(source);
        }

        // Settings that cannot be anything else without Azure: forced over appsettings and the environment.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["kvSetting:SecretUri"] = string.Empty,
        });
    }

    public static void AddLocaltestServices(IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<LocaltestProfileOptions>().BindConfiguration("Localtest:Profile");
        services.TryAddSingleton(TimeProvider.System);

        // Key Vault: the shared localtest certificate validates the apps' platform access tokens through the production
        // handler. Outgoing platform tokens (to Register and Notifications) are signed by the production generator with the
        // certificate AccessTokenSettings:AccessTokenSigningCertificateFullPath names.
        services.RemoveAll<IPublicSigningKeyProvider>();
        services.AddSingleton<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>();

        // The Register's localtest API, the only source of the test data.
        services.AddHttpClient<LocaltestRegisterTestData>();

        // KRR: the change log the production KRR sync reads (registered by AddMaskinportenClient as a Maskinporten client).
        services.RemoveAll<IContactRegisterHttpClient>();
        services.AddTransient<IContactRegisterHttpClient, LocaltestContactRegisterClient>();

        // brreg: the write API behind the organisation address endpoints and the change log the organisation sync reads.
        services.RemoveAll<IOrganizationNotificationAddressUpdateClient>();
        services.RemoveAll<IOrganizationNotificationAddressSyncClient>();
        services.AddScoped<LocaltestOrganizationNotificationAddressRegistry>();
        services.AddScoped<IOrganizationNotificationAddressUpdateClient>(sp => sp.GetRequiredService<LocaltestOrganizationNotificationAddressRegistry>());
        services.AddScoped<IOrganizationNotificationAddressSyncClient>(sp => sp.GetRequiredService<LocaltestOrganizationNotificationAddressRegistry>());

        // The seed: profile settings once per user, on the jobs' schedule as well as at start and on demand.
        services.AddScoped<LocaltestProfileSettingsSeeder>();
        services.AddSingleton<LocaltestSync>();
        var minutes = config.GetValue<uint?>("JobSettings:KrrSyncWaitTimeInMinutes") ?? 1;
        services.AddRecurringJob<LocaltestProfileSettingsSeedJob>(job =>
        {
            job.LeaseName = LocaltestSync.SettingsSeedLease;
            job.Interval = TimeSpan.FromMinutes(Math.Max(1, minutes));
        });

        // The migrations production runs in its pre-upgrade hook, then the first seed, before anything else starts.
        services.AddSingleton<IHostedService, LocaltestDatabaseStartup>();
    }
}
