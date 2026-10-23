using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.App.Aspire;

/// <summary>
/// The localtest resource's <c>reset</c> command, <c>studioctl env reset</c> for the Aspire stack: removes the
/// persistent containers and the package's volumes (<see cref="StackSpec.VolumePrefix" /> and the manifest's volume
/// ids) through the container runtime's CLI, so the next AppHost start creates the databases, blobs and policies
/// afresh. studioctl's volumes have their own prefix and are left alone.
/// </summary>
internal static class LocaltestReset
{
    /// <summary>The command's name.</summary>
    public const string CommandName = "reset";

    /// <summary>The confirmation the dashboard asks for before it runs the command.</summary>
    public const string Confirmation =
        "Remove the localtest containers and their volumes (the Postgres data, blobs, policies and the localtest app's data)? "
        + "Restart the AppHost afterwards.";

    /// <summary>The command that lists the volumes, one name per line.</summary>
    public static IReadOnlyList<string> ListVolumes { get; } = ["volume", "ls", "--quiet"];

    /// <summary>
    /// The runtime commands the reset runs, in order: remove the containers (a volume in use by one cannot go), then
    /// those of the package's volumes that exist (Podman refuses to remove one that does not).
    /// </summary>
    /// <param name="containerIds">The running stack's container ids.</param>
    /// <param name="volumeIds">The manifest's volume ids.</param>
    /// <param name="existingVolumes">The volumes the runtime lists.</param>
    public static IReadOnlyList<IReadOnlyList<string>> Commands(
        IReadOnlyCollection<string> containerIds,
        IEnumerable<string> volumeIds,
        IReadOnlySet<string> existingVolumes
    )
    {
        var commands = new List<IReadOnlyList<string>>();
        if (containerIds.Count > 0)
        {
            commands.Add(["rm", "--force", .. containerIds]);
        }
        var volumes = volumeIds.Select(StackSpec.VolumeName).Where(existingVolumes.Contains).ToList();
        if (volumes.Count > 0)
        {
            commands.Add(["volume", "rm", "--force", .. volumes]);
        }
        return commands;
    }

    public static async Task<ExecuteCommandResult> ExecuteAsync(
        ExecuteCommandContext context,
        AltinnLocaltestResource localtest
    )
    {
        var services = context.Services;
        var notifications = services.GetRequiredService<ResourceNotificationService>();
        var logger = services.GetRequiredService<ResourceLoggerService>().GetLogger(localtest);
        var ids = new List<string>();
        foreach (var name in localtest.Stack.Containers.Keys)
        {
            if (ContainerRuntime.ContainerId(notifications, name) is { } id)
            {
                ids.Add(id);
            }
        }
        var cli = ContainerRuntime.Cli(services.GetRequiredService<IConfiguration>());
        var (listed, volumeList) = await ContainerRuntime
            .RunAsync(cli, ListVolumes, context.CancellationToken)
            .ConfigureAwait(false);
        if (listed != 0)
        {
            return CommandResults.Failure($"{cli} {string.Join(' ', ListVolumes)} exited with {listed}: {volumeList}");
        }
        var existing = volumeList
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var arguments in Commands(ids, localtest.Stack.Manifest.VolumeIds, existing))
        {
            logger.LogRuntimeCommand(cli, string.Join(' ', arguments));
            var (exitCode, output) = await ContainerRuntime
                .RunAsync(cli, arguments, context.CancellationToken)
                .ConfigureAwait(false);
            if (exitCode != 0)
            {
                return CommandResults.Failure($"{cli} {string.Join(' ', arguments)} exited with {exitCode}: {output}");
            }
        }
        return CommandResults.Success();
    }
}
