using Altinn.Correspondence.Core.Repositories;
using Altinn.Correspondence.Core.Services;
using Altinn.Correspondence.Localtest.Auth;
using Altinn.Correspondence.Localtest.AzureResources;
using Altinn.Correspondence.Localtest.Inspection;
using Altinn.Correspondence.Localtest.Storage;
using Localtest.Shared.Maskinporten;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Correspondence.Localtest;

/// <summary>
/// Localtest composition for the vendored Correspondence host, called from the two additive <c>LOCALTEST</c> blocks in
/// <c>Program.cs</c>. <see cref="AddLocaltestConfiguration"/> runs right after upstream's configuration chain, before
/// <c>ConfigureServices</c> reads any setting, and gives the settings production gets from its deployment.
/// <see cref="AddLocaltestServices"/> runs after every production registration (the last but Kestrel's) and replaces
/// what cannot run without Azure: the blob store, Azure Resource Manager and the Maskinporten client, plus the
/// http-metadata relaxations and the dialog-token key resolver. Everything else (controllers, Hangfire jobs, EF
/// repositories and migrations, the Dialogporten, Notifications, Register, Profile, PDP, Resource Registry, Events and
/// KRR clients) is production code talking to the stack.
/// </summary>
public static class LocaltestCorrespondence
{
    public const string DefaultPlatformEndpoint = "http://local.altinn.cloud:8000/";

    public static void AddLocaltestConfiguration(ConfigurationManager config)
    {
        var platform = Platform(config);

        // Localtest defaults. Program.cs adds appsettings.json, appsettings.Development.json (localhost, tt02) and the
        // environment variables a second time after CreateBuilder, so the defaults go just below that last environment
        // source: above every JSON file, below DatabaseOptions__…, GeneralSettings__… etc. from studioctl.
        var defaults = new Dictionary<string, string?>
        {
            [$"{LocaltestCorrespondenceOptions.SectionName}:PlatformEndpoint"] = platform,
            // A password keeps Entra ID out (Program.GetConnectionString, Persistence BuildAzureNpgsqlDataSource).
            ["DatabaseOptions:ConnectionString"] = "Host=postgres;Port=5432;Database=correspondence;Username=correspondence;Password=localtest",
            // Required by the options class and never read: the blob repository is replaced (Storage/FileStorageRepository).
            ["AttachmentStorageOptions:ConnectionString"] = "UseDevelopmentStorage=true",
            ["AltinnOptions:PlatformGatewayUrl"] = platform,
            ["AltinnOptions:OpenIdWellKnown"] = platform + "authentication/api/v1/openid/.well-known/openid-configuration",
            ["AltinnOptions:ArbeidsflateOriginsCommaSeparated"] = "http://af.local.altinn.cloud:8000",
            // The dialog token's iss, as the stack's Dialogporten writes it (no trailing slash).
            ["DialogportenSettings:Issuer"] = platform + "dialogporten/api/v1",
            ["IdportenSettings:Issuer"] = platform + "localtest/api/idporten",
            // The OIDC client the attachment download challenges a browser with (Authentication's seeded
            // correspondence-localtest, components/authentication/fakes/LocaltestOidcClients.cs); upstream's Development
            // file has the placeholders clientId/clientSecret.
            ["AltinnIdProviderSettings:ClientId"] = "correspondence-localtest",
            ["AltinnIdProviderSettings:ClientSecret"] = "correspondence-localtest-secret",
            // A client id is what makes Integrations register the production clients instead of upstream's dev stubs.
            // Nine digits: the fake Maskinporten takes it as Digdir's organisation number, and without UseAltinnTestOrg
            // the exchange names digdir, as Dialogporten's client does. EncodedJwk is never read (the fake replaces
            // IMaskinportenService); Scope stays upstream's list (appsettings.json).
            ["MaskinportenSettings:ClientId"] = "991825827",
            ["MaskinportenSettings:Environment"] = "test",
            ["MaskinportenSettings:EncodedJwk"] = "e30=",
            ["MaskinportenSettings:UseAltinnTestOrg"] = "false",
            ["MaskinportenSettings:ExhangeToAltinnToken"] = "true",
            ["Localtest:Maskinporten:Issuer"] = platform + "localtest/api/maskinporten/",
            ["Localtest:Maskinporten:ExchangeEndpoint"] = platform + "authentication/api/v1/exchange/maskinporten",
            // The URLs written into dialogs, notification condition endpoints and event sources. Not localhost: the
            // condition endpoint is left out for a localhost base URL (CreateNotificationOrderHandler).
            ["GeneralSettings:CorrespondenceBaseUrl"] = platform,
            // The dialogporten group's Redis; Dialogporten uses database 0.
            ["GeneralSettings:RedisConnectionString"] = "redis:6379,defaultDatabase=1",
            // KRR: an anonymous stand-in in the Register component's fakes, reached through the production client.
            ["GeneralSettings:ContactReservationRegistryBaseUrl"] = platform + "register/api/localtest/krr/",
            ["GeneralSettings:ArbeidsflateBaseUrl"] = "http://af.local.altinn.cloud:8000",
            ["GeneralSettings:DisableCallsToAltinn2"] = "true",
            // Production: 15 workers and a 4-worker migration server per replica.
            ["GeneralSettings:WorkerCountPerReplica"] = "2",
            ["GeneralSettings:MigrationWorkerCountPerReplica"] = "0",
            ["GeneralSettings:EnableDownloadAll"] = "true",
            ["GeneralSettings:SlackUrl"] = string.Empty,
            ["GeneralSettings:MalwareScanBypassWhiteList"] = string.Empty,
            // appsettings.Development.json logs at Debug and every SQL command.
            ["Logging:LogLevel:Default"] = "Information",
            ["Logging:LogLevel:Altinn.Correspondence"] = "Information",
            ["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "Warning",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Migrations"] = "Information",
            ["Logging:LogLevel:Npgsql"] = "Warning",
            ["Logging:LogLevel:Npgsql.Connection"] = "Warning",
            ["Logging:LogLevel:Npgsql.Command"] = "Warning",
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

        // Cannot be anything else without Azure: forced over appsettings and the environment.
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GeneralSettings:ApplicationInsightsConnectionString"] = string.Empty,
        });
    }

    public static void AddLocaltestServices(IServiceCollection services, IConfiguration config)
    {
        services.Configure<LocaltestCorrespondenceOptions>(config.GetSection(LocaltestCorrespondenceOptions.SectionName));

        // Blobs: files on the correspondence-blobs volume instead of Azure Storage accounts.
        services.RemoveAll<IStorageRepository>();
        services.AddSingleton<FileStorageRepository>();
        services.AddSingleton<IStorageRepository>(sp => sp.GetRequiredService<FileStorageRepository>());

        // Azure Resource Manager: storage account deployment and the container app's IP restrictions are no-ops.
        services.RemoveAll<IResourceManager>();
        services.AddScoped<IResourceManager, LocaltestResourceManager>();

        // Token validation over the stack's http metadata, the fake Maskinporten's metadata for the Maskinporten
        // scheme, and dialog-token keys fetched again on a miss.
        LocaltestAuthentication.Add(services, config);

        // Every Maskinporten-backed client (the Altinn platform clients and KRR), with production semantics
        // (a copy of components/shared/fakes/Maskinporten).
        services.AddLocaltestMaskinporten(config);

        // correspondence/api/localtest/ reads the production tables and Hangfire's.
        services.AddScoped<LocaltestCorrespondenceQueries>();
    }

    internal static string Platform(IConfiguration config)
        => (config[$"{LocaltestCorrespondenceOptions.SectionName}:PlatformEndpoint"] is { Length: > 0 } configured
            ? configured
            : DefaultPlatformEndpoint).TrimEnd('/') + "/";
}
