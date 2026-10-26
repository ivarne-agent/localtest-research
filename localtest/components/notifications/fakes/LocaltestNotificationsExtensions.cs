#nullable enable
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Notifications.Core.Integrations;
using Altinn.Notifications.Localtest.Auth;
using Altinn.Notifications.Localtest.Capture;
using Altinn.Notifications.Localtest.Messaging;
using Altinn.Notifications.Localtest.Startup;
using Localtest.Shared.Maskinporten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Altinn.Notifications.Localtest;

/// <summary>
/// Localtest composition for the vendored Notifications orders host, called from the two <c>LOCALTEST</c> blocks in
/// <c>Program.cs</c>. <see cref="AddLocaltestConfiguration"/> runs before any setting is read and gives the settings
/// production gets from its deployment. <see cref="AddLocaltestMessaging"/> stands where <c>AddWolverineServices</c> is
/// in production: it replaces the Service Bus publishers with the capture, the Key Vault keys with the shared test
/// certificate, and adds the Maskinporten fake for the condition check, the localtest startup scripts, the responder and
/// the inspection API. Everything else (controllers, order processing and its six loops, the Postgres repositories and
/// migrations, the Profile, Register and PDP calls, the instant order clients) is production code.
/// </summary>
public static class LocaltestNotificationsExtensions
{
    private const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    private static readonly string[] ProcessLoops =
    [
        "PastDueOrders", "RetryOrders", "EmailNotifications", "ComposedEmailNotifications", "SmsDaytimeNotifications", "SmsAnytimeNotifications",
    ];

    public static void AddLocaltestConfiguration(ConfigurationManager config)
    {
        var platform = (config["Localtest:Notifications:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : DefaultPlatformEndpoint).TrimEnd('/') + "/";

        // Localtest defaults: override the vendored appsettings.json (localhost ports, platform.at23.altinn.cloud), but sit
        // just below the environment variables, so GeneralSettings__…, PlatformSettings__… etc. still win. Calls to another
        // service use its name (http://<service>/<prefix>/…, from services__<service>__http__0, fakes/ServiceDiscovery); the
        // response links and the JwtCookie metadata (fetched outside the factory) stay on the public origin.
        var defaults = new Dictionary<string, string?>
        {
            ["Localtest:Notifications:PlatformEndpoint"] = platform,
            // Response links (links.self, shipment and order locations) are built from this, without a trailing slash.
            ["GeneralSettings:BaseUri"] = platform.TrimEnd('/'),
            ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
            ["PlatformSettings:ApiProfileEndpoint"] = "http://profile/profile/api/v1/",
            ["PlatformSettings:ApiRegisterEndpoint"] = "http://register/register/api/v1/",
            ["PlatformSettings:ApiAuthorizationEndpoint"] = "http://authorization/authorization/api/v1/",
            // The Email and SMS hosts' instant endpoints are served by this host (LocaltestInstantController).
            ["PlatformSettings:ApiShortMessageServiceEndpoint"] = "http://localhost:5090/notifications/sms/api/v1/",
            ["PlatformSettings:ApiInstantEmailServiceEndpoint"] = "http://localhost:5090/notifications/email/api/v1/",
            ["DialogportenSettings:Enabled"] = "false",
            // Production: 30 tasks and a 30 s idle delay per loop. One task and a second, so an order is processed in seconds.
            // SMS leaves at any hour: the window is 0 to 24 instead of 09 to 17 (Europe/Oslo).
            ["NotificationConfig:SmsSendWindowStartHour"] = "0",
            ["NotificationConfig:SmsSendWindowEndHour"] = "24",
            // The Maskinporten fake serves the token for the condition check; the package only needs the settings to exist.
            ["SendConditionClient:MaskinportenSettings:Environment"] = "test",
            ["SendConditionClient:MaskinportenSettings:ClientId"] = "localtest-notifications",
            ["SendConditionClient:MaskinportenSettings:EncodedJwk"] = "e30=",
            // Localtest:Maskinporten:TokenEndpoint and ExchangeEndpoint: the fake's defaults, by service name.
        };
        foreach (var loop in ProcessLoops)
        {
            defaults[$"NotificationConfig:{loop}ProcessLoopConfig:TaskCount"] = "1";
            defaults[$"NotificationConfig:{loop}ProcessLoopConfig:PrimaryTaskIdleDelaySeconds"] = "1";
        }

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

    public static void AddLocaltestMessaging(IServiceCollection services, IConfiguration config)
    {
        services.Configure<LocaltestNotificationsOptions>(config.GetSection("Localtest:Notifications"));

        // Postgres: the production repositories on the shared server. Before anything starts, the localtest startup script
        // creates the inbox and capture tables (and empties the data when ResetOnStart is set).
        services.AddSingleton<LocaltestAdminDatabase>();
        services.AddSingleton<IHostedService, LocaltestDatabaseStartup>();

        // Messaging: the publishers production registers in AddWolverineServices, as the capture, and the responder that
        // plays the Email and SMS hosts behind the queue.
        services.AddSingleton<LocaltestCaptureStore>();
        services.AddSingleton<LocaltestSendQueue>();
        services.AddSingleton<IEmailCommandPublisher, CapturingEmailPublisher>();
        services.AddSingleton<IComposedEmailCommandPublisher, CapturingComposedEmailPublisher>();
        services.AddSingleton<ISendSmsPublisher, CapturingSmsPublisher>();
        services.AddSingleton<IHostedService, LocaltestResponder>();

        // Key Vault: the shared localtest certificate validates the apps' platform access tokens through the production
        // handler, and signs the outgoing platform/notifications tokens (to Register and the PDP) through the production generator.
        services.RemoveAll<IPublicSigningKeyProvider>();
        services.AddSingleton<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>();

        // The condition check's Maskinporten token, with production semantics (shared copy, see components/shared/README.md).
        services.AddLocaltestMaskinporten(config);
    }
}
