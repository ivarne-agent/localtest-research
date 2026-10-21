#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Altinn.Profile.Integrations.Extensions;
using Altinn.Profile.Integrations.Persistence;
using Altinn.Profile.Localtest.Register;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Altinn.Profile.Localtest.Startup;

/// <summary>
/// What production does before the host serves: the database migrations, which production runs in a Kubernetes
/// pre-upgrade hook (<c>dotnet Altinn.Profile.dll --run-db-migrations</c>, with the admin connection string), run here
/// with the same code and connection before anything else starts. Then one seed run (<see cref="LocaltestSync"/>), so
/// the test users have their contact details and settings on the first login; when the Register does not answer in
/// time, the recurring jobs seed within a minute instead. <see cref="StartingAsync"/> runs before any hosted service's
/// <c>StartAsync</c> (the recurring jobs, Kestrel), so nothing reads the database before it is migrated.
/// </summary>
public sealed class LocaltestDatabaseStartup(
    IConfiguration configuration,
    LocaltestSync sync,
    LocaltestRegisterTestData register,
    IOptions<LocaltestProfileOptions> options,
    ILogger<LocaltestDatabaseStartup> logger) : IHostedLifecycleService
{
    private const int ConnectAttempts = 30;

    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        var settings = configuration.GetSection("PostgreSqlSettings").Get<PostgreSqlSettings>() ?? new PostgreSqlSettings();
        if (!settings.EnableDBConnection)
        {
            logger.LogWarning("Localtest: PostgreSqlSettings:EnableDBConnection is false; no migrations and no seed");
            return;
        }

        await Migrate(cancellationToken);

        var wait = TimeSpan.FromSeconds(Math.Max(0, options.Value.StartupSyncWaitSeconds));
        var deadline = DateTime.UtcNow + wait;
        while (!await register.IsAvailable(cancellationToken))
        {
            if (DateTime.UtcNow > deadline)
            {
                logger.LogWarning("Localtest: the Register did not answer within {Wait}; the recurring jobs will seed", wait);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        var steps = await sync.RunAll(cancellationToken);
        logger.LogInformation("Localtest: start-up seed {Steps}", string.Join(", ", steps.Select(s => s.Step + "=" + s.Outcome)));
    }

    private async Task Migrate(CancellationToken cancellationToken)
    {
        var dbOptions = new DbContextOptionsBuilder<ProfileDbContext>()
            .UseNpgsql(configuration.GetAdminDatabaseConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var context = new ProfileDbContext(dbOptions);
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count > 0)
                {
                    logger.LogInformation("Localtest: applying {Count} migrations: {Migrations}", pending.Count, string.Join(", ", pending));
                    await context.Database.MigrateAsync(cancellationToken);
                }
                else
                {
                    logger.LogInformation("Localtest: profiledb is up to date");
                }

                return;
            }
            catch (Exception ex) when (Find<PostgresException>(ex) is { SqlState: PostgresErrorCodes.InvalidCatalogName })
            {
                // A Postgres volume created before profile joined the stack: its init scripts never ran 80-profile.sh.
                logger.LogCritical(
                    "Localtest: database profiledb does not exist. Run components/profile/postgres-init/80-profile.sh against the shared Postgres (see the component README) or reset the environment");
                throw;
            }
            catch (Exception ex) when (attempt < ConnectAttempts && Find<NpgsqlException>(ex) is { IsTransient: true })
            {
                logger.LogInformation("Localtest: Postgres not ready ({Message}); attempt {Attempt}", ex.Message, attempt);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
    }

    private static T? Find<T>(Exception? ex)
        where T : Exception
    {
        for (; ex is not null; ex = ex.InnerException)
        {
            if (ex is T match)
            {
                return match;
            }
        }

        return null;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
