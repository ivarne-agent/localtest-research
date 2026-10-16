using System.Text.RegularExpressions;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.Jobs;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.StorageQueues;
using Altinn.Common.AccessToken;
using Altinn.Common.AccessToken.Configuration;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Common.PEP.Authorization;
using Altinn.Register.ApiDescriptions;
using Altinn.Register.Authorization;
using Altinn.Register.Ccr;
using Altinn.Register.Cleanup;
using Altinn.Register.Clients;
using Altinn.Register.Clients.Interfaces;
using Altinn.Register.Configuration;
using Altinn.Register.Conventions;
using Altinn.Register.Core;
using Altinn.Register.Core.A2;
using Altinn.Register.Core.A2.SblProfile;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Altinn.Register.Core.PartyImport.A2;
using Altinn.Register.Core.Sire;
using Altinn.Register.Core.Utils;
using Altinn.Register.Extensions;
using Altinn.Register.Integrations.Sire;
using Altinn.Register.Model.Extensions;
using Altinn.Register.ModelBinding;
using Altinn.Register.PartyImport;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.PartyImport.Ccr;
using Altinn.Register.PartyImport.Npr;
using Altinn.Register.PartyImport.Sire;
using Altinn.Register.PartyImport.SystemUser;
using Altinn.Register.Services;
using Altinn.Register.Services.Implementation;
using Altinn.Register.Services.Interfaces;
using Altinn.Register.Utils;
using AltinnCore.Authentication.JwtCookie;
using Asp.Versioning.ApiExplorer;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Altinn.Register;

/// <summary>
/// Configures the register host.
/// </summary>
internal static partial class RegisterHost
{
    /// <summary>
    /// Configures the register host.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    public static WebApplication Create(string[] args)
    {
        var builder = AltinnHost.CreateWebApplicationBuilder("register", args);
        var services = builder.Services;
        var config = builder.Configuration;
        var descriptor = builder.GetAltinnServiceDescriptor();

        services.AddMemoryCache();
        services.AddHybridCache();

        services
            .AddControllers()
            .AddControllersAsServices()
            .AddMvcOptions(opt =>
            {
                opt.ModelBinderProviders.InsertSingleton<PartyComponentOptionModelBinder>(0);
                opt.ModelBinderProviders.InsertSingleton<PartyFieldIncludesModelBinder>(0);
            })
            .AddJsonOptions(opt =>
            {
                opt.JsonSerializerOptions.WriteIndented = descriptor.IsLocalDev;
            });

        services.AddSingleton<ConditionalControllerConvention>();
        services.AddOptions<MvcOptions>()
            .Configure((MvcOptions opts, IServiceProvider services) =>
            {
                opts.Conventions.Add(services.GetRequiredService<ConditionalControllerConvention>());
            });

        services
            .AddApiVersioning(opt =>
            {
                opt.ReportApiVersions = true;
            })
            .AddMvc()
            .AddApiExplorer(opt =>
            {
                // add the versioned api explorer, which also adds IApiVersionDescriptionProvider service
                // note: the specified format code will format the version as "'v'major[.minor][-status]"
                opt.GroupNameFormat = "'v'VVV";

                // note: this option is only necessary when versioning by url segment. the SubstitutionFormat
                // can also be used to control the format of the API version in route templates
                opt.SubstituteApiVersionInUrl = true;
            });

        services.Configure<GeneralSettings>(config.GetSection("GeneralSettings"));
        services.Configure<KeyVaultSettings>(config.GetSection("kvSetting"));
        services.Configure<AccessTokenSettings>(config.GetSection("AccessTokenSettings"));
        services.Configure<PlatformSettings>(config.GetSection("PlatformSettings"));
        services.Configure<PersonLookupSettings>(config.GetSection("PersonLookupSettings"));
        services.AddRateLimitPolicy(PersonLookupService.FailedAttemptsRateLimitPolicyName);
        services.AddOptions<Controllers.V2.PartyController.Settings>().ValidateDataAnnotations();

        services.AddOptions<A2PartyImportSettings>()
            .Configure((A2PartyImportSettings settings, IConfiguration config) =>
            {
                var ep = config.GetSection("Platform:SblBridge:Endpoint");
                if (!string.IsNullOrEmpty(ep.Value))
                {
                    settings.BridgeApiEndpoint = ep.Get<Uri>();
                }
            })
            .ValidateDataAnnotations();

        services.AddSingleton<IAuthorizationHandler, AccessTokenHandler>();
        services.AddScoped<IAuthorizationHandler, ScopeAccessHandler>();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddSingleton<IPublicSigningKeyProvider, PublicSigningKeyProvider>();

        services.AddScoped<IPersonLookup, PersonLookupService>();

        services.ConfigureOpenTelemetryTracerProvider((builder) => builder.AddSource(RegisterTelemetry.Name));
        services.ConfigureOpenTelemetryMeterProvider((builder) => builder.AddMeter(RegisterTelemetry.Name));

        services.AddAuthentication(JwtCookieDefaults.AuthenticationScheme)
          .AddJwtCookie(JwtCookieDefaults.AuthenticationScheme, options =>
          {
              GeneralSettings generalSettings = config.GetSection("GeneralSettings").Get<GeneralSettings>()
                  ?? throw new InvalidOperationException("Missing GeneralSettings configuration.");
              options.JwtCookieName = generalSettings.JwtCookieName;
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
          });

        services.AddAuthorizationBuilder()
            .AddPolicy("PlatformAccess", policy =>
                policy.Requirements.Add(new AccessTokenRequirement()))
            .AddPolicy("AuthorizationLevel2", policy =>
                policy.RequireClaim(AltinnCore.Authentication.Constants.AltinnCoreClaimTypes.AuthenticationLevel, "2", "3", "4"))
            .AddPolicy("InternalOrPlatformAccess", policy =>
                policy.Requirements.Add(new InternalScopeOrAccessTokenRequirement("altinn:register/partylookup.admin")))
            .AddPolicy("Debug", policy =>
            {
                // Note: this scope does not actually exist, and can only be generated using the test token generator.
                policy.Requirements.Add(new ScopeAccessRequirement("altinn:register/debug.internal"));
            });

        services.AddRegisterCoreServices();

        services.AddCcrFileProcessor();
        services.AddCcrXmlProcessor();
        services.AddCcrFileImportServices();

        services.AddHttpClient<IOrganizationClient, OrganizationClient>();
        services.AddHttpClient<IPersonClient, PersonClient>();
        services.AddHttpClient<IV1PartyService, PartiesClient>();
        services.AddHttpClient<IAuthorizationClient, AuthorizationClient>();

        services.AddHttpClient<IA2PartyImportService, A2PartyImportService>()
            .ConfigureBaseAddressFromOptions(static (A2PartyImportSettings settings) => settings.BridgeApiEndpoint!)
            .ReplaceResilienceHandler(static c =>
            {
                // Do not retry in the IA2PartyImportService, it's handled by MassTransit
                c.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
            });

        services.AddHttpClient<SystemUserImportService>()
            .ConfigureBaseAddress("https+http://altinn-authentication/authentication/")
            .AddPlatformAccessTokenHandler();

        services.AddHttpClient<TempWorkarounds.AccessManagementClient>()
            .ConfigureBaseAddress("https+http://altinn-access-management/accessmanagement/")
            .AddPlatformAccessTokenHandler();

        services.AddHttpClient<ISblProfileBridgeClient, SblProfileBridgeClient>()
            .ConfigureBaseAddressFromOptions(static (A2PartyImportSettings settings) => settings.BridgeApiEndpoint!)
            .ReplaceResilienceHandler(static c =>
            {
                // Iteration-1 proxy: the caller (altinn-authentication) is synchronous;
                // failures should surface immediately as 502 BadGateway, not get retried.
                c.Retry.ShouldHandle = static _ => ValueTask.FromResult(false);
            });

        services.AddNprClient()
            .ConfigureBaseAddress("https://folkeregisteret/")
            .AddMaskinPortenHandler("register-freg");

        services.AddHttpClient<ISireClient, SireClient>()
            .ConfigureBaseAddress("https://sire/")
            .AddMaskinPortenHandler("register-freg"); // we reuse the maskinporten client from freg

        services.AddHttpClient<ISireEventClient, SireEventClient>()
            .ConfigureBaseAddress("https://sire-events/")
            .AddMaskinPortenHandler("register-freg");

        // Lets operators tune the SIRE feed's antall page-size from config without code
        // changes. Unset → SIRE picks its server-side default.
        services.AddOptions<SireEventClientOptions>()
            .BindConfiguration("Altinn:register:PartyImport:Sire:Client");

        services.TryAddPlatformTokenProvider();
        if (!descriptor.IsLocalDev && !descriptor.IsTest)
        {
            services.AddSingleton<IAccessTokenGenerator, AccessTokenGenerator>();
            services.AddTransient<ISigningCredentialsResolver, SigningCredentialsResolver>();
        }

        services.AddUnitOfWorkManager();

        if (config.GetValue<bool>("Altinn:Npgsql:register:Enable"))
        {
            builder.AddRegisterPersistence();
            builder.Services.AddLeaseManager();
        }
        else
        {
            services.AddSingleton<IImportJobTracker>(s => throw new InvalidOperationException("Npgsql is not enabled"));
        }

        var initOnly = config.GetValue<bool>("Altinn:RunInitOnly");
        var isTest = config.GetValue<bool>("Altinn:IsTest");
        var mtEnabled = config.GetValue<bool>("Altinn:MassTransit:register:Enable");
        if (!initOnly && !isTest)
        {
            const string A2PartyImportJobTag = "a2-import";
            Func<IServiceProvider, CancellationToken, ValueTask> waitForBus = static (s, ct) => new ValueTask(s.GetRequiredService<IBusLifetime>().WaitForBus(ct));

            if (mtEnabled)
            {
                builder.AddAltinnMassTransit(
                    configureMassTransit: (cfg) =>
                    {
                        cfg.AddConsumers(typeof(RegisterHost).Assembly);
                    });

                builder.Services.AddScoped<SagaManager>();
            }

            var maxDbSizeInGib = config.GetValue("Altinn:register:PartyImport:A2:MaxDbSizeInGib", 20UL);
            builder.AddDatabaseSizeJobCondition(maxSize: ByteSize.FromGibibytes(maxDbSizeInGib), jobTags: [A2PartyImportJobTag]);
            builder.Services.AddSingleton<JobCleanupHelper>();

            services.AddRecurringJob<A2PartyImportJob>(settings =>
            {
                settings.Tags.Add(A2PartyImportJobTag);
                settings.LeaseName = A2PartyImportJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(1);
                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:A2:Enable");
            });

            services.AddRecurringJob<A2ProfileImportJob>(settings =>
            {
                settings.Tags.Add(A2PartyImportJobTag);
                settings.LeaseName = A2ProfileImportJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(1);
                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:A2:Profiles:Enable")
                    .WithRequireImportJobFinished(A2PartyImportJob.JobName, threshold: 5_000);
            });

            services.AddRecurringJob<SystemUserImportJob>(settings =>
            {
                settings.Tags.Add(A2PartyImportJobTag);
                settings.LeaseName = SystemUserImportJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(1);
                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:SystemUsers:Enable")
                    .WithRequireImportJobFinished(A2PartyImportJob.JobName, threshold: 5_000);
            });

            services.AddRecurringJob<NprImportJob>(settings =>
            {
                settings.Tags.Add(A2PartyImportJobTag);
                settings.LeaseName = NprImportJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(10);
                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:Npr:Enable");
            });

            services.AddRecurringJob<SireImportJob>(settings =>
            {
                settings.Tags.Add(A2PartyImportJobTag);
                settings.LeaseName = SireImportJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(10);
                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:Sire:Listen");
            });

            services.AddOptions<SagaStateCleanupSettings>()
                .ValidateDataAnnotations()
                .ValidateOnStart()
                .BindConfiguration("Altinn:register:Saga:Cleanup");

            services.AddRecurringJob<SagaStateCleanupJob>(settings =>
            {
                settings.LeaseName = SagaStateCleanupJob.JobName;
                settings.Interval = TimeSpan.FromMinutes(15);
            });

            services.AddRecurringJob<CcrImportJob>(settings =>
            {
                settings.LeaseName = CcrImportJob.JobName;
                settings.Interval = descriptor.Environment.IsAT
                    ? TimeSpan.FromMinutes(1)
                    : TimeSpan.FromMinutes(15);

                settings.WaitForReady = waitForBus;
                settings.Enabled = JobEnabledBuilder.Default
                    .WithRequireConfigurationValueEnabled("Altinn:register:PartyImport:Ccr:Enable");
            });

            services.TryAddSingleton<ICcrUpdateFederator, CcrUpdateFederator>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<StorageQueueSettings>, ConfigureQueueSettingsForCcrFederation>());
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CcrUpdateFederationSettings>, ConfigureUpdateFederationSettingsFromConfiguration>());
            services.AddStorageQueueSenderFactory();
        }
        else if (initOnly && mtEnabled)
        {
            builder.AddAltinnMassTransitMigrations();
        }

        services.AddOpenApiExampleProvider();
        services.AddSwaggerFilterAttributeSupport();
        services.AddUrnSwaggerSupport();
        services.AddSwaggerAutoXmlDoc();
        services.AddAuthorizationModelUtilsSwaggerSupport();
        services.AddOptions<SwaggerGenOptions>()
            .Configure((SwaggerGenOptions opt, IApiVersionDescriptionProvider provider) =>
            {
                // add a swagger document for each discovered API version
                foreach (var desc in provider.ApiVersionDescriptions)
                {
                    opt.SwaggerDoc(desc.GroupName, new OpenApiInfo { Title = "Altinn Platform Register", Version = desc.ApiVersion.ToString() });
                }
            });

        services.AddSwaggerGen(c =>
        {
            var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            var xmlFile = Path.ChangeExtension(assemblyPath, ".xml");
            if (File.Exists(xmlFile))
            {
                c.IncludeXmlComments(xmlFile);
            }

            c.EnableAnnotations();
            c.SupportNonNullableReferenceTypes();
            c.SchemaFilter<PartyComponentOptionSchemaFilter>();
            c.SchemaFilter<PartyFieldIncludesSchemaFilter>();
            c.SchemaFilter<TranslatedTextSchemaFilter>();

            var originalIdSelector = c.SchemaGeneratorOptions.SchemaIdSelector;
            c.SchemaGeneratorOptions.SchemaIdSelector = (Type t) =>
            {
                if (!t.IsNested)
                {
                    var orig = originalIdSelector(t);

                    if (t.Assembly == typeof(Contracts.PartyType).Assembly)
                    {
                        if (t.Namespace is string typeNamespace
                            && GetVersionedNamespaceRegex().Match(typeNamespace) is { Success: true, Groups: var groups })
                        {
                            orig = $"Contracts.{groups[1].ValueSpan}.{orig}";
                        }
                        else
                        {
                            orig = $"Contracts.{orig}";
                        }
                    }

                    return orig;
                }

                var chain = new List<string>();
                do
                {
                    chain.Add(originalIdSelector(t));
                    t = t.DeclaringType!;
                }
                while (t != null);

                chain.Reverse();
                return string.Join(".", chain);
            };
        });

        if (initOnly)
        {
            builder.Host.UseDefaultServiceProvider((context, options) =>
            {
                options.ValidateScopes = false;
                options.ValidateOnBuild = false;
            });
        }

        return builder.Build();
    }

    [GeneratedRegex(@"\.(?<version>V\d+)(?:\.|$)", RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture)]
    private static partial Regex GetVersionedNamespaceRegex();
}
