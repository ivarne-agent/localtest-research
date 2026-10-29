using Altinn.Correspondence.API.Auth;
using Altinn.Correspondence.API.Filters;
using Altinn.Correspondence.API.Helpers;
using Altinn.Correspondence.API.Middleware;
using Altinn.Correspondence.API.Swagger;
using Altinn.Correspondence.Application;
using Altinn.ApiClients.Maskinporten.Config;
using Altinn.Correspondence.Common.Caching;
using Altinn.Correspondence.Common.Constants;
using Altinn.Correspondence.Core.Options;
using Altinn.Correspondence.Helpers;
using Altinn.Correspondence.Integrations;
using Altinn.Correspondence.Integrations.Azure;
using Altinn.Correspondence.Integrations.Hangfire;
using Altinn.Correspondence.Integrations.OpenTelemetry;
using Altinn.Correspondence.Integrations.Slack;
using Altinn.Correspondence.Persistence;
using Altinn.Correspondence.Persistence.Helpers;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Npgsql;
using StackExchange.Redis;
using System.Text.Json.Serialization;

BuildAndRun(args);

static ILogger<Program> CreateBootstrapLogger()
{
    return LoggerFactory.Create(builder =>
     {
         builder
             .AddConsole();
     }).CreateLogger<Program>();
}

static void BuildAndRun(string[] args)
{
    var bootstrapLogger = CreateBootstrapLogger();
    bootstrapLogger.LogInformation("Starting Altinn.Correspondence.API...");
    var builder = WebApplication.CreateBuilder(args);
#if LOCALTEST
    // localtest: resolve other components by name (http://<service>/...) from services__<service>__http__0; a name
    // without an entry fails at once, a real host (a dot, an IP, localhost) is used as it is (no pass-through;
    // fakes/ServiceDiscovery, planned-components/service-discovery.md).
    global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services);
#endif

    builder.Configuration
        .AddJsonFile("appsettings.json", true, true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true, true)
        .AddJsonFile("appsettings.local.json", true, true)
        .AddEnvironmentVariables();
#if LOCALTEST
    Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(builder.Configuration);
#endif
    StartupAppSettingsLogging.LogConfigurationKeys(builder.Configuration, bootstrapLogger, false);
    ConfigureServices(builder.Services, builder.Configuration, builder.Environment, bootstrapLogger);

    var generalSettings = builder.Configuration.GetSection(nameof(GeneralSettings)).Get<GeneralSettings>()
        ?? throw new InvalidOperationException($"Missing {nameof(GeneralSettings)} configuration section");
    bootstrapLogger.LogInformation("Running in environment {Environment} with base url {BaseUrl}", builder.Environment.EnvironmentName, generalSettings.CorrespondenceBaseUrl);
    builder.Services.ConfigureOpenTelemetry(generalSettings);
#if LOCALTEST
    // localtest: OTLP for traces, metrics and logs from the standard variables when OTEL_EXPORTER_OTLP_ENDPOINT is set,
    // nothing otherwise (TODO.md task 53). ConfigureOpenTelemetry adds no Azure Monitor exporter here: the
    // fakes force GeneralSettings:ApplicationInsightsConnectionString empty.
    if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
    {
        // The environment detector after the component's own resource lets OTEL_SERVICE_NAME win; the exporter's
        // HttpClient logs every export at Information, so it stays at Warning.
        OpenTelemetry.OpenTelemetryBuilderOtlpExporterExtensions.UseOtlpExporter(
            builder.Services.AddOpenTelemetry().ConfigureResource(resource => OpenTelemetry.Resources.ResourceBuilderExtensions.AddEnvironmentVariableDetector(resource)));
        builder.Services.AddLogging(logging => logging.AddFilter("System.Net.Http.HttpClient.Otlp", Microsoft.Extensions.Logging.LogLevel.Warning));
    }
#endif

    var app = builder.Build();
    bootstrapLogger.LogInformation("Application built");

    app.UseExceptionHandler();
    app.UseMiddleware<SwaggerDocumentCacheMiddleware>();
    app.UseCorrespondenceOpenApi();
    app.UseCors(AuthorizationConstants.ArbeidsflateCors);
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.UseMiddleware<SecurityHeadersMiddleware>();
    app.UseMiddleware<AcceptHeaderValidationMiddleware>();

    if (app.Environment.IsDevelopment())
    {
        bootstrapLogger.LogInformation("Running in development mode, applying migrations to database...");
        using var scope = app.Services.CreateScope();
        var _Db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (_Db != null)
        {
            _Db.MigrateWithLock();
        }
    }

    RecurringJobRegistration.Register(app.Services, app.Configuration, bootstrapLogger);

    app.Run();
}

static void ConfigureServices(IServiceCollection services, IConfiguration config, IHostEnvironment hostEnvironment, ILogger bootstrapLogger)
{
    var connectionString = GetConnectionString(config);

    services.AddHttpContextAccessor();
    services.AddSingleton(TimeProvider.System);
    services.AddHostedService<EdDsaSecurityKeysCacheService>();
    services.Configure<AttachmentStorageOptions>(config.GetSection(key: nameof(AttachmentStorageOptions)));
    services.Configure<AltinnOptions>(config.GetSection(key: nameof(AltinnOptions)));
    services.Configure<AzureResourceManagerOptions>(config.GetSection(key: nameof(AzureResourceManagerOptions)));
    services.Configure<DialogportenSettings>(config.GetSection(key: nameof(DialogportenSettings)));
    services.Configure<IdportenSettings>(config.GetSection(key: nameof(IdportenSettings)));
    services.Configure<MaskinportenSettings>(config.GetSection(key: nameof(MaskinportenSettings)));
    services.Configure<AltinnIdProviderSettings>(config.GetSection(key: nameof(AltinnIdProviderSettings)));
    services.Configure<GeneralSettings>(config.GetSection(key: nameof(GeneralSettings)));
    services.Configure<MaskinportenJwkRotationSettings>(config.GetSection(key: nameof(MaskinportenJwkRotationSettings)));

    services.AddControllers(options =>
    {
        options.Conventions.Add(new ShowInternalApisInDevelopmentApplicationModelConvention(hostEnvironment));
        options.Filters.Add<ClientErrorLoggingFilter>();
    }).AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    // Convert validation errors to AltinnValidationProblemDetails
    services.Configure<ApiBehaviorOptions>(options =>
    {
        options.InvalidModelStateResponseFactory = context => ProblemDetailsHelper.ToValidationProblemResult(context);
    });
    var altinnOptions = new AltinnOptions();
    config.GetSection(nameof(AltinnOptions)).Bind(altinnOptions);
    services.AddSingleton<SlackExceptionNotificationHandler>();
    services.AddExceptionHandler<SlackExceptionNotificationHandler>();
    services.AddCors(options =>
    {
        options.AddPolicy(name: AuthorizationConstants.ArbeidsflateCors,
                          policy =>
                          {
                              policy.WithOrigins(altinnOptions.ArbeidsflateOriginsCommaSeparated.Split(',')).SetIsOriginAllowedToAllowWildcardSubdomains();
                              policy.WithMethods("GET", "POST", "DELETE", "OPTIONS");
                              policy.WithHeaders("Authorization", "request-id", "request-context", "traceparent", "prefer");
                              policy.AllowCredentials();
                          });
    });
    var generalSettings = new GeneralSettings();
    config.GetSection(nameof(GeneralSettings)).Bind(generalSettings);
    services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = generalSettings.RedisConnectionString;
        options.InstanceName = "redisCache";
    });
    var dataProtectionRedisConnection = ConnectionMultiplexer.Connect(generalSettings.RedisConnectionString);
    services.AddDataProtection()
        .SetApplicationName("Altinn.Correspondence.API")
        .PersistKeysToStackExchangeRedis(dataProtectionRedisConnection, "DataProtection-Keys");
#pragma warning disable EXTEXP0018
    services.AddHybridCache(options => options.MaximumPayloadBytes = 1000 * 1000 * 10L);
#pragma warning restore EXTEXP0018
    services.AddSingleton<IHybridCacheWrapper, HybridCacheWrapper>();
    services.ConfigureAuthentication(config, hostEnvironment);
    services.ConfigureAuthorization(config);
    services.AddEndpointsApiExplorer();
    services.AddCorrespondenceOpenApi(config);

    services.AddApplicationHandlers();
    services.AddPersistence(config, bootstrapLogger);
    services.AddIntegrations(config);

    services.AddHttpClient();
    services.AddProblemDetails();

    // Register filters
    services.AddScoped<StatisticsApiKeyFilter>();

    services.ConfigureHangfire(config);
#if LOCALTEST
    Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, config);
#endif

    services.Configure<KestrelServerOptions>(options =>
    {
        options.Limits.MaxRequestBodySize = null;
        options.Limits.MinRequestBodyDataRate = null;
        options.Limits.MinResponseDataRate = null;
    });
    services.Configure<FormOptions>(options =>
    {
        options.ValueLengthLimit = int.MaxValue;
        options.MultipartBodyLengthLimit = long.MaxValue;
        options.MultipartHeadersLengthLimit = int.MaxValue;
    });
}

static string GetConnectionString(IConfiguration config)
{
    var connectionString = config.GetSection("DatabaseOptions:ConnectionString").Value ?? Environment.GetEnvironmentVariable("DatabaseOptions__ConnectionString");
    if (connectionString == null)
    {
        throw new ArgumentNullException("DatabaseOptions__ConnectionString");
    }
    if (string.IsNullOrWhiteSpace(new NpgsqlConnectionStringBuilder(connectionString).Password))
    {
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions());
        var token = credential
            .GetToken(
                new Azure.Core.TokenRequestContext(new[] { "https://ossrdbms-aad.database.windows.net/.default" })
            );
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        connectionStringBuilder.Password = token.Token;
        return connectionStringBuilder.ToString();

    }
    return connectionString;
}


public partial class Program { }
