#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Events.Clients.Interfaces;
using Altinn.Platform.Events.Configuration;
using Altinn.Platform.Events.Localtest.Auth;
using Altinn.Platform.Events.Localtest.Delivery;
using Altinn.Platform.Events.Localtest.Changes;
using Altinn.Platform.Events.Localtest.Queues;
using Altinn.Platform.Events.Localtest.Startup;
using Altinn.Platform.Events.Repository;
using Altinn.Platform.Events.Services;
using Altinn.Platform.Events.Wolverine.Policies;
using Altinn.Platform.Events.Wolverine.Publishers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Wolverine;

namespace Altinn.Platform.Events.Localtest;

/// <summary>
/// Localtest composition for the vendored Events host, called from the two <c>LOCALTEST</c> blocks in
/// <c>Program.cs</c>. <see cref="AddLocaltestConfiguration"/> runs before any setting is read and turns off Service Bus
/// and Application Insights (Key Vault stays off because <c>kvSetting:SecretUri</c> is unset). <see cref="AddLocaltestEvents"/> runs after the
/// production registrations and replaces the Storage Queue publishers with the production in-process bus publishers
/// and the Key Vault keys with the shared test certificate, and adds the localtest startup scripts, the webhook
/// capture and the inspection API. The pipeline itself (controllers, services, Wolverine handlers, the claim loop, the
/// Postgres repositories and migrations, PDP and Register calls, webhook delivery) is production code.
/// </summary>
public static class LocaltestEventsExtensions
{
    private const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public static void AddLocaltestConfiguration(ConfigurationManager config)
    {
        var platform = (config["Localtest:Events:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : DefaultPlatformEndpoint).TrimEnd('/') + "/";

        // Localtest defaults: override the vendored appsettings.json (localhost ports, apps.altinn.no), but sit just
        // below the environment variables, so GeneralSettings__…, PlatformSettings__… etc. still win. Calls to another
        // service use its name (http://<service>/<prefix>/…, from services__<service>__http__0, fakes/ServiceDiscovery);
        // what a browser, a subscriber or a token sees (the next links, the CloudEvent source, the apps' host, the
        // JwtCookie metadata fetched outside the factory) stays on the public origin.
        var defaults = new Dictionary<string, string?>
        {
            ["Localtest:Events:PlatformEndpoint"] = platform,
            ["GeneralSettings:BaseUri"] = platform.TrimEnd('/'),
            ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
            ["PlatformSettings:ApiAuthorizationEndpoint"] = "http://authorization/authorization/api/v1/",
            ["PlatformSettings:RegisterApiBaseAddress"] = "http://register/register/api/",
            ["PlatformSettings:ApiEventsEndpoint"] = platform + "events/api/v1/",
            ["PlatformSettings:ApiProfileEndpoint"] = "http://profile/profile/api/v1/",
            // App events have sources under the apps' own origin; this makes them app events, and cuts
            // http://local.altinn.cloud:8000/ttd/app/instances/… to …/ttd/app, as for ttd.apps.altinn.no.
            ["PlatformSettings:AppsDomain"] = new Uri(platform).Host,
            ["PlatformSettings:SubscriptionKey"] = "localtest",
            // Production caches subscriptions and main units for an hour; locally new ones take effect at once.
            ["PlatformSettings:SubscriptionCachingLifetimeInSeconds"] = "5",
            ["PlatformSettings:MainUnitCachingLifetimeInSeconds"] = "30",
            ["EventsProcessingSettings:PrimaryTaskIdleDelaySeconds"] = "1",
        };

        // The production retry policies, with seconds instead of hours. Every array index in appsettings.json is
        // overridden, because a configuration source cannot shorten an array from a lower one.
        AddDelays(defaults, "RegistrationQueuePolicy", cooldown: [500, 1000, 2000], schedule: [2000, 5000, 10000, 10000, 10000]);
        AddDelays(defaults, "InboundQueuePolicy", cooldown: [500, 1000, 2000], schedule: [2000, 5000, 10000, 10000, 10000]);
        AddDelays(defaults, "ValidationQueuePolicy", cooldown: [1000], schedule: [2000, 5000, 10000, 10000, 10000]);
        AddDelays(defaults, "OutboundQueuePolicy", cooldown: [1000], schedule: [2000, 5000, 10000, 30000, 60000, 60000, 60000, 60000, 60000, 60000]);

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

        // Settings that cannot be anything else without Azure: forced over appsettings and the environment. (Key Vault is
        // not among them: production decides on kvSetting:SecretUri in SetConfigurationProviders, before this runs, and
        // skips Key Vault because nothing sets it locally.)
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            // No Service Bus transport: Wolverine routes the commands to the API's own handlers on local queues.
            ["WolverineSettings:EnableRegistrationPublisher"] = "false",
            ["WolverineSettings:EnableValidationPublisher"] = "false",
            ["WolverineSettings:EnableRegistrationListener"] = "false",
            ["WolverineSettings:EnableInboundListener"] = "false",
            ["WolverineSettings:EnableOutboundListener"] = "false",
            ["WolverineSettings:EnableValidationListener"] = "false",
        });
    }

    public static WebApplicationBuilder AddLocaltestEvents(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;
        services.Configure<LocaltestEventsOptions>(config.GetSection("Localtest:Events"));

        // Postgres: the production repositories on the shared server. Before anything starts, the localtest startup
        // scripts add the next trace log partition and the delivery table, and empty the tables (ResetOnStart).
        services.AddSingleton<LocaltestAdminDatabase>();
        services.AddSingleton<IHostedService, LocaltestDatabaseStartup>();

        // Change notifications for the inspection stream, raised after the production repositories have written.
        services.AddSingleton<LocaltestChanges>();
        services.AddSingleton<ClaimedEvents>();
        services.Decorate<ICloudEventRepository, NotifyingCloudEventRepository>();
        services.Decorate<IUnitOfWorkRepository, NotifyingUnitOfWorkRepository>();
        services.Decorate<ITraceLogRepository, NotifyingTraceLogRepository>();
        services.Decorate<ISubscriptionRepository, NotifyingSubscriptionRepository>();

        // Messaging: with every Service Bus flag off, production picks the legacy Storage Queue publishers. The bus
        // publishers send to Wolverine instead, whose default local routing hands the commands to the production
        // handlers in this process (registration, outbound delivery, subscription validation).
        services.RemoveAll<IRegistrationEventPublisher>();
        services.AddScoped<IRegistrationEventPublisher, RegistrationEventPublisher>();
        services.RemoveAll<ISubscriptionValidationPublisher>();
        services.AddScoped<ISubscriptionValidationPublisher, SubscriptionValidationPublisher>();
        Replace<IEventsQueueClient, LocaltestEventsQueueClient>(services);

        // Production attaches its retry policies only to Service Bus listeners; the same policies on the local queues.
        var wolverineSettings = config.GetSection("WolverineSettings").Get<WolverineSettings>() ?? new WolverineSettings();
        services.ConfigureWolverine(opts =>
        {
            opts.Policies.Add(new RegistrationEventHandlerPolicy(wolverineSettings));
            opts.Policies.Add(new InboundEventHandlerPolicy(wolverineSettings));
            opts.Policies.Add(new OutboundEventHandlerPolicy(wolverineSettings));
            opts.Policies.Add(new ValidationEventHandlerPolicy(wolverineSettings));
        });

        // Key Vault: the shared localtest certificate validates the apps' platform access tokens through the
        // production handler, and signs the outgoing platform/events tokens (to Register) through the production generator.
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Webhooks: every POST is recorded; endpoints outside the stack are answered locally (LocaltestEventsOptions.Delivery).
        services.AddSingleton<LocaltestDeliveryLog>();
        services.AddTransient<LocaltestWebhookDeliveryHandler>();
        services.AddHttpClient(WebhookService.HttpClientName).AddHttpMessageHandler<LocaltestWebhookDeliveryHandler>();

        services.AddSingleton<Inspection.LocaltestEventsStream>();

        return builder;
    }

    private static void AddDelays(Dictionary<string, string?> settings, string policy, int[] cooldown, int[] schedule)
    {
        for (int i = 0; i < cooldown.Length; i++)
        {
            settings[$"WolverineSettings:{policy}:CooldownDelaysMs:{i}"] = cooldown[i].ToString();
        }

        for (int i = 0; i < schedule.Length; i++)
        {
            settings[$"WolverineSettings:{policy}:ScheduleDelaysMs:{i}"] = schedule[i].ToString();
        }
    }

    private static void Replace<TService, TImplementation>(IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.RemoveAll<TService>();
        services.AddSingleton<TService, TImplementation>();
    }
}
