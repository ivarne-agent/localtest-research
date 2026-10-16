using System.Diagnostics;
using System.Net;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.ServiceDefaults.HttpClient.MaskinPorten;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.ServiceDefaults.MassTransit.Testing;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Common.AccessToken.Services;
using Altinn.Common.AccessTokenClient.Services;
using Altinn.Register.Configuration;
using Altinn.Register.Controllers.V2;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.Cryptography;
using Altinn.Register.IntegrationTests.Fakes;
using Altinn.Register.IntegrationTests.TestServices;
using Altinn.Register.IntegrationTests.Tracing;
using Altinn.Register.PartyImport.A2;
using Altinn.Register.PartyImport.SystemUser;
using Altinn.Register.TestUtils;
using Altinn.Register.TestUtils.Database;
using Altinn.Register.TestUtils.TestData;
using AltinnCore.Authentication.JwtCookie;
using CommunityToolkit.Diagnostics;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Altinn.Register.IntegrationTests.Fixtures;

public sealed class WebApplicationFixture
    : IAsyncLifetime
{
    ValueTask IAsyncDisposable.DisposeAsync()
        => ValueTask.CompletedTask;

    ValueTask IAsyncLifetime.InitializeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public async Task<TestWebApplication> CreateServer(
        Action<IConfigurationBuilder>? configureConfiguration = null,
        Action<IServiceCollection>? configureServices = null)
    {
        using var rootActivity = IntegrationTestsActivities.Source.StartActivity(ActivityKind.Internal, name: $"create web-app server");

        var ctx = TestContext.Current;
        var dbFixture = await ctx.GetRequiredFixture<PostgresServerFixture>();
        var templateDb = await dbFixture.GetOrCreateTemplateDatabase(
            "integration-tests-template-db",
            async (db, ct) =>
            {
                await using var factory = new WebApplicationFactory().WithWebHostBuilder(builder =>
                {
                    builder.ConfigureServices(services =>
                    {
                        services.AddSingleton(db);
                    });

                    var settings = new ConfigurationBuilder();

                    settings.AddInMemoryCollection([
                        /* Init only */
                        new("Altinn:RunInitOnly", "true"), // this may need to be removed

                        /* DB */
                        new("Altinn:Npgsql:register:Enable", "true"),
                        new("Altinn:Npgsql:register:Migrate:Enabled", "true"),

                        new("Altinn:Npgsql:register:ConnectionString", db.AppConnectionString),
                        new("Altinn:Npgsql:register:Migrate:ConnectionString", db.MigratorConnectionString),
                        new("Altinn:Npgsql:register:Seed:ConnectionString", db.SeederConnectionString),
                    ]);
                    builder.UseConfiguration(settings.Build());
                });

                {
                    using var startActivity = IntegrationTestsActivities.Source.StartActivity(ActivityKind.Internal, name: $"start web-app server for template db");
                    factory.CreateClient();
                }
            },
            ctx.CancellationToken);

        var db = await dbFixture.CreateDatabase(templateDb, ctx.CancellationToken);

        var factory = new WebApplicationFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(db);
                services.AddOptions<PartyController.Settings>()
                    .Configure(s =>
                    {
                        s.PartyStreamPageSize = 10;
                        s.RoleAssignmentsStreamPageSize = 10;
                    });
            });

            var settings = new ConfigurationBuilder();

            settings.AddInMemoryCollection([
                /* DB */
                new("Altinn:Npgsql:register:Enable", "true"),
                new("Altinn:Npgsql:register:Migrate:Enabled", "false"),

                new("Altinn:Npgsql:register:ConnectionString", db.AppConnectionString),
                new("Altinn:Npgsql:register:Migrate:ConnectionString", db.MigratorConnectionString),
                new("Altinn:Npgsql:register:Seed:ConnectionString", db.SeederConnectionString),

                /* Party import */
                new("Altinn:register:PartyImport:Npr:Guardianships:Enable", "true"),

                // feature flags
                new("Altinn:register:Party:CreatePartyId", "true"),
            ]);

            // Apply test-provided configuration last, so tests can override the defaults above.
            if (configureConfiguration is not null)
            {
                configureConfiguration(settings);
            }

            builder.UseConfiguration(settings.Build());

            if (configureServices is not null)
            {
                builder.ConfigureTestServices(configureServices);
            }
        });

        {
            using var startActivity = IntegrationTestsActivities.Source.StartActivity(ActivityKind.Internal, name: $"start web-app server");
            factory.CreateClient();
        }

        return new(factory, db);
    }

    private class WebApplicationFactory
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(WebHostDefaults.ServerUrlsKey, "http://register.test");

            var settings = new ConfigurationBuilder()
                .AddInMemoryCollection([
                    new(AltinnPreStartLogger.DisableConfigKey, "true"),
                    new("Logging:LogLevel:Microsoft.AspNetCore.Authentication", "Trace"),
                ])
                .Build();

            builder.UseConfiguration(settings);
            builder.ConfigureLogging(builder =>
            {
                builder.ClearProviders();
                if (TestContext.Current.TestOutputHelper is { } output)
                {
                    builder.Services.AddSingleton<ILoggerProvider>(s =>
                    {
                        var config = s.GetRequiredService<IConfiguration>();
                        if (!config.GetValue<bool>("Altinn:Test:Logging:Enable", defaultValue: false))
                        {
                            return NullLoggerProvider.Instance;
                        }

                        var scopeProvider = s.GetService<IExternalScopeProvider>();
                        var formatter = s.GetServices<ConsoleFormatter>().FirstOrDefault(f => f.Name == ConsoleFormatterNames.Simple);
                        if (formatter is null)
                        {
                            ThrowHelper.ThrowInvalidOperationException($"The '{ConsoleFormatterNames.Simple}' console formatter is not registered.");
                        }

                        return new XunitLoggerProvider(output, scopeProvider, formatter);
                    });
                }
            });

            builder.ConfigureServices(services =>
            {
                services.AddSingleton<FakeTimeProvider>();
                services.AddSingleton<RegisterTestDataGenerator>();
                services.AddSingleton<TimeProvider>(s => s.GetRequiredService<FakeTimeProvider>());
                services.AddSingleton<IPublicSigningKeyProvider, TestPublicSigningKeyProvider>();
                services.AddSingleton<IAccessTokenGenerator, TestAccessTokenGenerator>();
                services.AddSingleton<TestCertificateService>();
                services.AddSingleton<TestOpenIdConnectConfigurationManager>();
                services.AddSingleton<TestJwtService>();
                services.AddSingleton<IPostConfigureOptions<JwtCookieOptions>, ConfigureTestJwtOptions>();
                services.ConfigureHttpClientDefaults(builder =>
                {
                    builder.ConfigureAdditionalHttpMessageHandlers((handlers, _) =>
                    {
                        handlers.Add(new TracingHandler([FakeHttpEndpoint.BasePath[..^1]]));
                    });
                });
                services.AddFakeHttpHandlers();
                services.AddOptions<GeneralSettings>()
                    .PostConfigure(s => s.BridgeApiEndpoint = FakeHttpEndpoint.HttpsUri.ToString());
                services.AddOptions<A2PartyImportSettings>()
                    .PostConfigure(s => s.BridgeApiEndpoint = FakeHttpEndpoint.HttpsUri);
                services.AddHttpClient<TempWorkarounds.AccessManagementClient>()
                    .ConfigureBaseAddress(FakeHttpEndpoint.HttpsUri);
                services.AddHttpClient<SystemUserImportService>().ConfigureBaseAddress(FakeHttpEndpoint.HttpsUri);
                services.AddNprClient().ConfigureBaseAddress(FakeHttpEndpoint.HttpsUri);
                services.AddSingleton<IMaskinPortenClient, FakeMaskinPortenClient>();

                services.AddOptions<CcrServiceSettings>()
                    .Configure(s =>
                    {
                        var userName = "test-user";
                        var password = "test-password";
                        var client = new CcrClientIdentitySettings
                        {
                            PasswordHash = PasswordHash.Create(userName, password),
                            AllowedSourceNetworks = [new IPNetwork(TestWebApplication.RemoteTestIpAddress, 128)],
                        };

                        s.Clients[userName] = client;
                    });

                AltinnServiceDefaultsMassTransitTestingExtensions.AddAltinnMassTransitTestHarness(
                    services,
                    configureMassTransit: (cfg) =>
                    {
                        cfg.AddConsumers(typeof(RegisterHost).Assembly);
                    });

                services.Configure<AltinnMassTransitOptions>(o => o.ActivityPropagation = "Child");
                services.AddScoped<SagaManager>();
            });

            base.ConfigureWebHost(builder);
        }
    }

    private sealed class ConfigureTestJwtOptions(TestOpenIdConnectConfigurationManager manager)
        : IPostConfigureOptions<JwtCookieOptions>
    {
        public void PostConfigure(string? name, JwtCookieOptions options)
        {
            options.ConfigurationManager = manager;
        }
    }

    private sealed class XunitLoggerProvider
        : ILoggerProvider
    {
        private readonly ITestOutputHelper _output;
        private readonly IExternalScopeProvider? _scopeProvider;
        private readonly ConsoleFormatter _formatter;
        private readonly Lock _lock = new();

        public XunitLoggerProvider(ITestOutputHelper output, IExternalScopeProvider? scopeProvider, ConsoleFormatter formatter)
        {
            _output = output;
            _scopeProvider = scopeProvider;
            _formatter = formatter;
        }

        public ILogger CreateLogger(string categoryName)
            => new Logger(categoryName, this);

        public void Dispose()
        {
        }

        private sealed class Logger
            : ILogger
        {
            private readonly XunitLoggerProvider _provider;
            private readonly string _categoryName;

            public Logger(string categoryName, XunitLoggerProvider provider)
            {
                _provider = provider;
                _categoryName = categoryName;
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => _provider._scopeProvider?.Push(state) ?? NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel)
                => logLevel != LogLevel.None;

            [ThreadStatic]
            private static StringWriter? _tStaticStringWriter;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Guard.IsNotNull(formatter);

                _tStaticStringWriter ??= new StringWriter();
                LogEntry<TState> logEntry = new(logLevel, _categoryName, eventId, state, exception, formatter);
                _provider._formatter.Write(in logEntry, _provider._scopeProvider, _tStaticStringWriter);

                var sb = _tStaticStringWriter.GetStringBuilder();
                if (sb.Length == 0)
                {
                    return;
                }

                string logString = sb.ToString();
                sb.Clear();
                if (sb.Capacity > 1024)
                {
                    sb.Capacity = 1024;
                }

                lock (_provider._lock)
                {
                    _provider._output.WriteLine(logString);
                }
            }
        }

        private sealed class NullScope
            : IDisposable
        {
            public static NullScope Instance { get; } = new NullScope();

            private NullScope()
            {
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }
    }
}
