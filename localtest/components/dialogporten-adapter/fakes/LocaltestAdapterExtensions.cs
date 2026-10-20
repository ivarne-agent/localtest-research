using Altinn.DialogportenAdapter.Contracts;
using Localtest.Shared.Maskinporten;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Memory;
using Wolverine;

namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>
/// Localtest composition for the vendored adapter host, called from the three <c>LOCALTEST</c> additions in
/// <c>Program.cs</c>: configuration defaults before the first setting is read, service replacements after the production
/// registrations, and the localtest routes after the production ones.
/// </summary>
public static partial class LocaltestAdapterExtensions
{
    /// <summary>The production scope list (upstream <c>appsettings.json</c>), which the localtest Maskinporten echoes.</summary>
    private const string MaskinportenScope =
        "digdir:dialogporten.serviceprovider.admin digdir:dialogporten.serviceprovider.legacyhtml altinn:serviceowner/instances.write digdir:dialogporten.serviceprovider altinn:serviceowner/instances.read altinn:storage/instances.syncadapter altinn:register/partylookup.admin";

    /// <summary>
    /// Inserts the localtest defaults just below the environment variables, so they override the vendored
    /// <c>appsettings*.json</c> (tt02 and "PopulateFromEnvironmentVariable" values) while <c>DialogportenAdapter__...</c>
    /// variables still win; then forces what cannot be anything else without Azure.
    /// </summary>
    public static void AddLocaltestConfiguration(ConfigurationManager configuration)
    {
        var options = new LocaltestAdapterOptions();
        configuration.GetSection(LocaltestAdapterOptions.SectionName).Bind(options);
        var platform = options.PlatformEndpoint.TrimEnd('/') + "/";

        var defaults = new Dictionary<string, string?>
        {
            [$"{LocaltestAdapterOptions.SectionName}:PlatformEndpoint"] = platform,

            // One origin for everything. GetPlatformUri and GetAppUriForOrg (Settings.cs, LOCALTEST site) derive the
            // links in the dialogs from BaseUri; the internal endpoints are where the Refit clients send their requests.
            ["DialogportenAdapter:Altinn:BaseUri"] = platform,
            ["DialogportenAdapter:Altinn:InternalStorageEndpoint"] = platform,
            ["DialogportenAdapter:Altinn:InternalRegisterEndpoint"] = platform,
            ["DialogportenAdapter:Altinn:SubscriptionKey"] = "localtest",
            // DialogportenAdapter:Altinn:AltinnCdn stays upstream's https://altinncdn.no: the org list is read from the
            // internet, as Authentication, the Resource Registry and Dialogporten read it.
            ["DialogportenAdapter:Dialogporten:BaseUri"] = platform + "dialogporten",
            // Public: Arbeidsflate calls the delete action and the transmission receipts with the dialog token.
            ["DialogportenAdapter:Adapter:BaseUri"] = platform + "storage/dialogporten",
            ["DialogportenAdapter:Adapter:FeatureFlag:EnableSubmissionTransmissions"] = "true",
            ["DialogportenAdapter:Authentication:JwtBearerWellKnown"] =
                platform + "authentication/api/v1/openid/.well-known/openid-configuration",

            // The package's validators need all of these; the localtest IMaskinportenService (fakes/Maskinporten) reads
            // only ClientId, Scope, ExhangeToAltinnToken and UseAltinnTestOrg. A client id that is not an organisation
            // number is Digdir (991825827) at the localtest token endpoint, and without the Altinn test organisation the
            // exchanged token names digdir: production semantics, the adapter acts as Digdir.
            ["DialogportenAdapter:Maskinporten:Environment"] = "test",
            ["DialogportenAdapter:Maskinporten:TokenExchangeEnvironment"] = "tt02",
            ["DialogportenAdapter:Maskinporten:ClientId"] = "localtest-dialogporten-adapter",
            ["DialogportenAdapter:Maskinporten:EncodedJwk"] = Convert.ToBase64String("""{"kty":"RSA"}"""u8),
            ["DialogportenAdapter:Maskinporten:Scope"] = MaskinportenScope,
            ["DialogportenAdapter:Maskinporten:ExhangeToAltinnToken"] = "true",
            ["DialogportenAdapter:Maskinporten:UseAltinnTestOrg"] = "false",
            ["Localtest:Maskinporten:Issuer"] = platform + "localtest/api/maskinporten/",
            ["Localtest:Maskinporten:ExchangeEndpoint"] = platform + "authentication/api/v1/exchange/maskinporten",

            // Bound by Settings, read only by the Azure Service Bus bootstrap, which is behind #if !LOCALTEST.
            ["WolverineSettings:ServiceBusConnectionString"] = "unused-in-localtest",

            // Upstream logs at Warning; Information shows each sync, skip and no-op, and the localtest enqueue.
            ["Logging:LogLevel:Altinn.DialogportenAdapter"] = "Information",
        };

        var lastEnvironmentSource = configuration.Sources.ToList().FindLastIndex(s => s is EnvironmentVariablesConfigurationSource);
        configuration.Sources.Insert(
            lastEnvironmentSource < 0 ? configuration.Sources.Count : lastEnvironmentSource,
            new MemoryConfigurationSource { InitialData = defaults });

        // Forced: Azure Monitor throws without a connection string, and there is no Application Insights locally.
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationInsights:Enabled"] = "false",
        });
    }

    public static WebApplicationBuilder AddLocaltestAdapter(WebApplicationBuilder builder)
    {
        // The stack's Authentication publishes its OpenID metadata over plain http. This has to be a Configure: the
        // framework's own post-configure rejects an http metadata address before any later PostConfigure runs.
        builder.Services.Configure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            o => o.RequireHttpsMetadata = false);

        // Maskinporten: the localtest fake (a copy of components/shared/fakes/Maskinporten) replaces the package's
        // service, which the production registrations above added with TryAdd.
        builder.Services.AddLocaltestMaskinporten(builder.Configuration);
        return builder;
    }

    public static WebApplication MapLocaltestRoutes(WebApplication app)
    {
        // Storage's localtest Wolverine handler posts each outbox command here. Anonymous, as every api/localtest endpoint
        // in the stack. The command goes onto the local queue, so the production handler
        // (SyncDialogOnInstanceUpdatedHandler) and its retry and error policies process it as they process an ASB message.
        app.MapPost("storage/dialogporten/api/localtest/enqueue", async (
                [FromBody] SyncInstanceCommand command,
                [FromServices] IMessageBus bus,
                [FromServices] ILoggerFactory loggerFactory) =>
            {
                await bus.PublishAsync(command);
                LogEnqueued(loggerFactory.CreateLogger(typeof(LocaltestAdapterExtensions)), command.AppId, command.PartyId, command.InstanceId);
                return Results.Accepted();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }

    [LoggerMessage(LogLevel.Information, "Localtest: queued the sync of instance {PartyId}/{InstanceId} ({AppId})")]
    private static partial void LogEnqueued(ILogger logger, string appId, string partyId, Guid instanceId);
}
