using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Platform.Receipt.Configuration;
using Altinn.Platform.Receipt.Extensions;
using Altinn.Platform.Receipt.Health;
using Altinn.Platform.Receipt.Services;
using Altinn.Platform.Receipt.Services.Interfaces;
using Altinn.Platform.Receipt.Telemetry;
using AltinnCore.Authentication.JwtCookie;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

ILogger logger;

string applicationInsightsConnectionString = string.Empty;

var builder = WebApplication.CreateBuilder(args);

ConfigureWebHostCreationLogging();

await SetConfigurationProviders(builder.Configuration);

builder.UseGracefulShutdown();

ConfigureApplicationLogging(builder.Logging);

ConfigureServices(builder.Services, builder.Configuration);
#if LOCALTEST
Altinn.Platform.Receipt.Localtest.LocaltestReceipt.Apply(builder.Services, builder.Configuration);
#endif

var app = builder.Build();

Configure(builder.Configuration);

app.Run();

void ConfigureApplicationLogging(ILoggingBuilder logging)
{
    logging.AddOpenTelemetry(builder =>
    {
        builder.IncludeFormattedMessage = true;
        builder.IncludeScopes = true;
    });
}

void ConfigureWebHostCreationLogging()
{
    var logFactory = LoggerFactory.Create(builder =>
    {
        builder.AddFilter("Altinn.Platform.Receipt.Program", LogLevel.Debug).AddConsole();
    });

    logger = logFactory.CreateLogger<Program>();
}

async Task SetConfigurationProviders(ConfigurationManager config)
{
    string basePath = Directory.GetParent(Directory.GetCurrentDirectory()).FullName;
    config.SetBasePath(basePath);
    config.AddJsonFile(
        basePath + "altinn-appsettings/altinn-dbsettings-secret.json",
        optional: true,
        reloadOnChange: true
    );
    if (basePath == "/")
    {
        config.AddJsonFile(basePath + "app/appsettings.json", optional: false, reloadOnChange: true);
    }
    else
    {
        config.AddJsonFile(
            Directory.GetCurrentDirectory() + "/appsettings.json",
            optional: false,
            reloadOnChange: true
        );
    }

    config.AddEnvironmentVariables();

    await ConnectToKeyVaultAndSetApplicationInsights(config);

    config.AddCommandLine(args);
}

async Task ConnectToKeyVaultAndSetApplicationInsights(ConfigurationManager config)
{
    KeyVaultSettings keyVaultSettings = new();
    config.GetSection("kvSetting").Bind(keyVaultSettings);
    if (!string.IsNullOrEmpty(keyVaultSettings.SecretUri))
    {
        logger.LogInformation("Program // Set app insights connection string // App");

        var azureCredentials = new DefaultAzureCredential();

        var client = new SecretClient(new Uri(keyVaultSettings.SecretUri), azureCredentials);

        config.AddAzureKeyVault(new Uri(keyVaultSettings.SecretUri), azureCredentials);

        try
        {
            KeyVaultSecret keyVaultSecret = await client.GetSecretAsync("ApplicationInsights--ConnectionString");
            applicationInsightsConnectionString = keyVaultSecret.Value;
        }
        catch (Exception vaultException)
        {
            logger.LogError(vaultException, $"Unable to read application insights key.");
        }
    }
}

void AddAzureMonitorTelemetryExporters(IServiceCollection services, string applicationInsightsConnectionString)
{
    services.Configure<OpenTelemetryLoggerOptions>(logging =>
        logging.AddAzureMonitorLogExporter(o =>
        {
            o.ConnectionString = applicationInsightsConnectionString;
            o.EnableTraceBasedLogsSampler = false;
        })
    );
    services.ConfigureOpenTelemetryMeterProvider(metrics =>
        metrics.AddAzureMonitorMetricExporter(o =>
        {
            o.ConnectionString = applicationInsightsConnectionString;
        })
    );
    services.ConfigureOpenTelemetryTracerProvider(tracing =>
        tracing.AddAzureMonitorTraceExporter(o =>
        {
            o.ConnectionString = applicationInsightsConnectionString;
            o.SamplingRatio = 1.0f;
            o.TracesPerSecond = null;
        })
    );
}

void ConfigureServices(IServiceCollection services, IConfiguration config)
{
    var attributes = new List<KeyValuePair<string, object>>(2)
    {
        KeyValuePair.Create("service.name", (object)"platform-receipt"),
    };

    services
        .AddOpenTelemetry()
        .ConfigureResource(resourceBuilder => resourceBuilder.AddAttributes(attributes))
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation();
            metrics.AddMeter("Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http");
        })
        .WithTracing(tracing =>
        {
            if (builder.Environment.IsDevelopment())
            {
                tracing.SetSampler(new AlwaysOnSampler());
            }

            tracing.AddAspNetCoreInstrumentation();

            tracing.AddHttpClientInstrumentation();

            tracing.AddProcessor(new RequestFilterProcessor(new HttpContextAccessor()));
        });

    if (!string.IsNullOrEmpty(applicationInsightsConnectionString))
    {
        AddAzureMonitorTelemetryExporters(services, applicationInsightsConnectionString);
    }

    services.AddControllersWithViews();
    services.AddHealthChecks().AddCheck<HealthCheck>("receipt_health_check");
    GeneralSettings generalSettings = config.GetSection("GeneralSettings").Get<GeneralSettings>();
    services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));

    services
        .AddAuthentication(JwtCookieDefaults.AuthenticationScheme)
        .AddJwtCookie(
            JwtCookieDefaults.AuthenticationScheme,
            options =>
            {
                options.JwtCookieName = generalSettings.RuntimeCookieName;
                options.MetadataAddress = generalSettings.OpenIdWellKnownEndpoint;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };

                if (builder.Environment.IsDevelopment())
                {
                    options.RequireHttpsMetadata = false;
                }
            }
        );

    services.AddSingleton(config);
    services.AddHttpClient<IRegister, RegisterWrapper>();
    services.AddHttpClient<IStorage, StorageWrapper>();
    services.AddHttpClient<IProfile, ProfileWrapper>();
    services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
    services.AddTransient<IAccessTokenGenerator, AccessTokenGenerator>();
    services.AddTransient<ISigningCredentialsResolver, SigningCredentialsResolver>();

    services.Configure<PlatformSettings>(config.GetSection("PlatformSettings"));
}

void Configure(IConfiguration config)
{
    string authenticationEndpoint = string.Empty;
    if (Environment.GetEnvironmentVariable("PlatformSettings__ApiAuthenticationEndpoint") != null)
    {
        authenticationEndpoint = Environment.GetEnvironmentVariable("PlatformSettings__ApiAuthenticationEndpoint");
    }
    else
    {
        authenticationEndpoint = config["PlatformSettings:ApiAuthenticationEndpoint"];
    }

    if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
    {
        app.UseDeveloperExceptionPage();

        // Enable higher level of detail in exceptions related to JWT validation
        IdentityModelEventSource.ShowPII = true;
    }
    else
    {
        app.UseExceptionHandler("/receipt/api/v1/error");
    }

    app.UseStaticFiles();
    app.UseStatusCodePages(context =>
    {
        var request = context.HttpContext.Request;
        var response = context.HttpContext.Response;
#if !LOCALTEST
        string url = $"https://platform.{config["GeneralSettings:Hostname"]}{request.Path}{request.QueryString}";
#else
        // localtest: there is no platform.* host and no TLS locally; the browser came in on the public origin, whose
        // Host the proxy keeps, so sign-in sends it back there.
        string url = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";
#endif
        string gotoUrl = WebUtility.UrlEncode(url);

        // you may also check requests path to do this only for specific methods
        // && request.Path.Value.StartsWith("/specificPath")
        if (response.StatusCode == (int)HttpStatusCode.Unauthorized)
        {
            response.Redirect($"{authenticationEndpoint}authentication?goto={gotoUrl}");
        }

        return Task.CompletedTask;
    });

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    app.MapControllerRoute(
        name: "languageRoute",
        pattern: "receipt/api/v1/{controller}/{action=Index}",
        defaults: new { controller = "Language" },
        constraints: new { controller = "Language" }
    );
    app.MapHealthChecks("/health");
}
