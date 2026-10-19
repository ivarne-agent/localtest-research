#nullable enable
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Platform.Events.Localtest.Startup;

/// <summary>
/// Runs the localtest startup scripts against eventsdb as <c>platform_events_admin</c>: <c>prepare.sql</c> (year
/// partitions of the trace log, the localtest delivery table) and, unless <c>Localtest:Events:ResetOnStart</c> is
/// false, <c>reset-on-start.sql</c>, which empties every data table. The production migrations have already run
/// (<c>UseYuniql</c> in <c>Program.Configure</c>, before the host starts), and <see cref="StartingAsync"/> runs before
/// any hosted service's <c>StartAsync</c>, so the production claim loop never sees the previous session's events.
/// </summary>
public sealed class LocaltestDatabaseStartup(
    LocaltestAdminDatabase database,
    IOptions<LocaltestEventsOptions> options,
    ILogger<LocaltestDatabaseStartup> logger) : IHostedLifecycleService
{
    public async Task StartingAsync(CancellationToken cancellationToken)
    {
        await database.RunScript("prepare.sql", cancellationToken);
        if (options.Value.ResetOnStart)
        {
            await database.RunScript("reset-on-start.sql", cancellationToken);
            logger.LogInformation("Localtest: eventsdb emptied on start (Localtest:Events:ResetOnStart)");
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
