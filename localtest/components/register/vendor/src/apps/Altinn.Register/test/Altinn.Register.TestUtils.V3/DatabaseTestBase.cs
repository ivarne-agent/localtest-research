using Altinn.Authorization.ServiceDefaults;
using Altinn.Register.TestUtils.Database;
using Altinn.Register.TestUtils.TestData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Register.TestUtils;

/// <summary>
/// Base class for tests that need a PostgreSQL database.
/// </summary>
public abstract class DatabaseTestBase
    : HostTestBase
{
    private PostgresDatabase? _db;
    private PostgresServerFixture? _serverFixture;
    private PostgresServerFixture.TemplateDb? _template;

    /// <summary>
    /// Gets whether database seed data should be enabled.
    /// </summary>
    protected virtual bool SeedData => true;

    /// <summary>
    /// Gets the per-test database.
    /// </summary>
    protected PostgresDatabase Database => _db!;

    /// <summary>
    /// Gets the register test data generator.
    /// </summary>
    protected RegisterTestDataGenerator TestDataGenerator => GetRequiredService<RegisterTestDataGenerator>();

    /// <summary>
    /// Creates another uniquely named database from the same template as the per-test database.
    /// </summary>
    protected Task<PostgresDatabase> CreateDatabaseFromTemplate(CancellationToken cancellationToken = default)
        => _serverFixture!.CreateDatabase(_template, cancellationToken);

    /// <summary>
    /// Gets the application configuration.
    /// </summary>
    protected IConfigurationManager Configuration
        => (IConfigurationManager)GetRequiredService<IConfiguration>();

    /// <inheritdoc/>
    protected override async ValueTask ConfigureHost(IHostApplicationBuilder builder)
    {
        var testContext = TestContext.Current;
        _serverFixture = await testContext.GetRequiredFixture<PostgresServerFixture>();
        _template = await _serverFixture.GetOrCreateTemplateDatabase(
            "persistence-tests-template",
            InitializeTemplateDatabase,
            testContext.CancellationToken);
        _db = await _serverFixture.CreateDatabase(_template, testContext.CancellationToken);

        await base.ConfigureHost(builder);

        builder.Configuration.AddInMemoryCollection([
            new("Altinn:Npgsql:register:ConnectionString", _db.ConnectionString),
            new("Altinn:Npgsql:register:Migrate:ConnectionString", _db.MigratorConnectionString),
            new("Altinn:Npgsql:register:Seed:ConnectionString", _db.SeederConnectionString),
            new("Altinn:Npgsql:register:Seed:Enabled", SeedData ? "true" : "false"),

            // feature flags
            new("Altinn:register:Party:CreatePartyId", "true"),
        ]);

        builder.AddRegisterPersistence();
        builder.Services.AddSingleton<RegisterTestDataGenerator>();
    }

    private async Task InitializeTemplateDatabase(PostgresDatabase db, CancellationToken cancellationToken)
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection([
            new("Logging:LogLevel:Default", "Warning"),
            new("Altinn:IsTest", "true"),
            new("Altinn:Npgsql:register:ConnectionString", db.ConnectionString),
            new("Altinn:Npgsql:register:Migrate:ConnectionString", db.MigratorConnectionString),
            new("Altinn:Npgsql:register:Seed:ConnectionString", db.SeederConnectionString),
            new("Altinn:Npgsql:register:Seed:Enabled", "false"),
        ]);

        if (DisableLogging)
        {
            configuration.AddInMemoryCollection([
                new(AltinnPreStartLogger.DisableConfigKey, "true"),
            ]);
        }

        await Configure(configuration);

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "test-template",
            EnvironmentName = "Development",
            Configuration = configuration,
        });

        builder.AddAltinnServiceDefaults("register");
        if (DisableLogging)
        {
            builder.Logging.ClearProviders();
        }

        await base.ConfigureHost(builder);

        builder.AddRegisterPersistence();
        builder.Services.AddSingleton<RegisterTestDataGenerator>();

        using var host = builder.Build();
        await host.StartAsync(cancellationToken);
        await host.StopAsync(cancellationToken);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (_db is { } db)
        {
            await ((IAsyncDisposable)db).DisposeAsync();
        }
    }
}
