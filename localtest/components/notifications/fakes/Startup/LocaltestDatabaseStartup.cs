#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Notifications.Localtest.Startup;

/// <summary>
/// Runs the localtest startup scripts against notificationsdb as <c>platform_notifications_admin</c>:
/// <c>prepare.sql</c> (the <c>localtest</c> schema with the inbox and capture tables) and, only when
/// <c>Localtest:Notifications:ResetOnStart</c> is true, <c>reset-on-start.sql</c>, which empties every data table. The
/// production migrations have already run (<c>SetUpPostgreSql</c> in <c>Program.cs</c>, before the host starts), and
/// <see cref="StartingAsync"/> runs before any hosted service's <c>StartAsync</c>, so the processing loops never see a
/// half-prepared database.
/// </summary>
public sealed class LocaltestDatabaseStartup(
    LocaltestAdminDatabase database,
    IOptions<LocaltestNotificationsOptions> options,
    ILogger<LocaltestDatabaseStartup> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await database.RunScript("prepare.sql", cancellationToken);
        if (options.Value.ResetOnStart)
        {
            await database.RunScript("reset-on-start.sql", cancellationToken);
            logger.LogInformation("Localtest: notificationsdb emptied on start (Localtest:Notifications:ResetOnStart)");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
