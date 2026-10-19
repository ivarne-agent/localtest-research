#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Altinn.Common.AccessToken.Services;
using Altinn.Platform.Storage.Clients;
using Altinn.Platform.Storage.Localtest.Auth;
using Altinn.Platform.Storage.Localtest.Blobs;
using Altinn.Platform.Storage.Localtest.FileScan;
using Altinn.Platform.Storage.Repository;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Platform.Storage.Localtest;

/// <summary>
/// Localtest composition for the vendored Storage host. Called from the two <c>LOCALTEST</c> blocks in
/// <c>Program.cs</c>: <see cref="AddLocaltestConfiguration"/> instead of the Key Vault connect (before any setting is
/// read), and <see cref="AddLocaltestStorage"/> after the production registrations, where it replaces what has no
/// local counterpart. Postgres stays real (application metadata and texts included); blobs, the file-scan queue and
/// the Key Vault signing keys are local.
/// </summary>
public static class LocaltestStorageExtensions
{
    private const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public static void AddLocaltestConfiguration(ConfigurationManager config)
    {
        var platform = (config["GeneralSettings:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : DefaultPlatformEndpoint).TrimEnd('/') + "/";

        // Localtest defaults: override the vendored appsettings.json (which points at localhost ports and at22), but sit
        // just below the environment variables, so GeneralSettings__…, PlatformSettings__… etc. still win.
        var defaults = new MemoryConfigurationSource
        {
            InitialData = new Dictionary<string, string?>
            {
                ["GeneralSettings:PlatformEndpoint"] = platform,
                ["GeneralSettings:OpenIdWellKnownEndpoint"] = platform + "authentication/api/v1/openid/",
                ["GeneralSettings:Hostname"] = new Uri(platform).Host,
                ["GeneralSettings:OndemandEndpoint"] = platform + "storage/api/v1/",
                ["GeneralSettings:PdfGeneratorEndpoint"] = platform + "pdf/",
                ["GeneralSettings:AppMetadataCacheLifeTimeInSeconds"] = "30",
                ["GeneralSettings:TextResourceCacheLifeTimeInSeconds"] = "30",
                ["GeneralSettings:AppTitleCacheLifeTimeInSeconds"] = "30",
                ["PlatformSettings:ApiAuthorizationEndpoint"] = platform + "authorization/api/v1/",
                ["PlatformSettings:SubscriptionKey"] = "localtest",
                ["RegisterServiceSettings:ApiRegisterEndpoint"] = platform + "register/api/v1/",
            },
        };
        var lastEnvironmentSource = config.Sources.Select((source, index) => (source, index))
            .LastOrDefault(s => s.source is EnvironmentVariablesConfigurationSource);
        if (lastEnvironmentSource.source is not null)
        {
            config.Sources.Insert(lastEnvironmentSource.index, defaults);
        }
        else
        {
            config.Sources.Add(defaults);
        }

        // Settings that cannot be anything else without Azure: forced over appsettings and the environment.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["kvSetting:SecretUri"] = string.Empty,
            ["WolverineSettings:EnableSending"] = "false",
            ["GeneralSettings:DisableA2Endpoints"] = "true",
        });
    }

    public static WebApplicationBuilder AddLocaltestStorage(WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.Configure<LocaltestStorageOptions>(builder.Configuration.GetSection("Localtest:Storage"));

        // Blob storage: files under a volume instead of Azure Blob / Azurite.
        Replace<IBlobRepository, FileBlobRepository>(services);

        // File scan: no queue and no scanner; the element is marked clean as soon as the scan is requested.
        Replace<IFileScanQueueClient, LocaltestFileScanQueueClient>(services);

        // Key Vault: the shared localtest certificate (AccessTokenSettings:AccessTokenSigningCertificateFullPath) signs
        // outgoing platform access tokens through the production generator and validates incoming ones through the
        // production handler.
        Replace<IPublicSigningKeyProvider, LocaltestPublicSigningKeyProvider>(services);

        // Application metadata and texts: production repositories on Postgres only. The app runner pushes them through
        // the production endpoints (POST/PUT applications, POST/PUT applications/{org}/{app}/texts), as Designer does.

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
