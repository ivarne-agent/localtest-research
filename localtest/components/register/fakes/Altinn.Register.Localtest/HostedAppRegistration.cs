using Altinn.Register.Localtest.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Altinn.Register.Localtest;

/// <summary>
/// Registers the apps listed in <c>Localtest:Register:Apps</c> once the host has started. Runs in the background so
/// an app that is not up yet does not delay startup; it stays registered and is fetched again on the next refresh.
/// </summary>
internal sealed class HostedAppRegistration(RegisterStoreProvider provider, IOptions<LocaltestRegisterOptions> options, ILogger<HostedAppRegistration> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var apps = (options.Value.Apps ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(a => a.Split('/', 2))
            .ToList();

        foreach (var parts in apps)
        {
            if (parts.Length != 2)
            {
                logger.LogWarning("Ignoring Localtest:Register:Apps entry '{Entry}': expected org/app", string.Join('/', parts));
                continue;
            }

            try
            {
                var state = await provider.Register(parts[0], parts[1], stoppingToken);
                logger.LogInformation("Registered app {Org}/{App} at startup ({Users} users)", parts[0], parts[1], state.Users);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Registering app {Org}/{App} at startup failed", parts[0], parts[1]);
            }
        }
    }
}
