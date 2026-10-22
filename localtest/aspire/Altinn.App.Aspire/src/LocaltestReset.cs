using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.App.Aspire;

/// <summary>
/// The localtest resource's <c>reset</c> command, <c>studioctl env reset</c> for the Aspire stack: removes the
/// persistent component containers and the manifest's named volumes through the container runtime's CLI, so the next
/// AppHost start creates the databases, blobs and policies afresh. The volumes are studioctl's too, so this also
/// resets a studioctl <c>localtest-v2</c> stack's data.
/// </summary>
internal static class LocaltestReset
{
    /// <summary>The command's name.</summary>
    public const string CommandName = "reset";

    /// <summary>The snapshot property that holds a container's ID.</summary>
    private const string ContainerIdProperty = "container.id";

    /// <summary>The confirmation the dashboard asks for before it runs the command.</summary>
    public const string Confirmation =
        "Remove the localtest containers and their volumes (the Postgres data, blobs, policies and the localtest app's data, "
        + "shared with studioctl's localtest-v2 stack)? Restart the AppHost afterwards.";

    /// <summary>The container runtime's CLI: Aspire's <c>ASPIRE_CONTAINER_RUNTIME</c>, otherwise docker.</summary>
    public static string RuntimeCli(IConfiguration configuration)
    {
        if (configuration["ASPIRE_CONTAINER_RUNTIME"] is { Length: > 0 } runtime)
        {
            return runtime;
        }
        return configuration["DOTNET_ASPIRE_CONTAINER_RUNTIME"] is { Length: > 0 } legacy ? legacy : "docker";
    }

    /// <summary>The command that lists the volumes, one name per line.</summary>
    public static IReadOnlyList<string> ListVolumes { get; } = ["volume", "ls", "--quiet"];

    /// <summary>
    /// The runtime commands the reset runs, in order: remove the containers (a volume in use by one cannot go), then
    /// those of the manifest's volumes that exist (Podman refuses to remove one that does not).
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Commands(
        IReadOnlyCollection<string> containerIds,
        IEnumerable<string> manifestVolumes,
        IReadOnlySet<string> existingVolumes
    )
    {
        var commands = new List<IReadOnlyList<string>>();
        if (containerIds.Count > 0)
        {
            commands.Add(["rm", "--force", .. containerIds]);
        }
        var volumes = manifestVolumes.Where(existingVolumes.Contains).ToList();
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
        foreach (var child in localtest.Stack.Components.Values.Select(c => c.Resource))
        {
            if (
                notifications.TryGetCurrentState(child.Name, out var state)
                && state.Snapshot.Properties.FirstOrDefault(p => p.Name == ContainerIdProperty)?.Value
                    is string { Length: > 0 } id
            )
            {
                ids.Add(id);
            }
        }
        var cli = RuntimeCli(services.GetRequiredService<IConfiguration>());
        var (listed, volumeList) = await RunAsync(cli, ListVolumes, context.CancellationToken).ConfigureAwait(false);
        if (listed != 0)
        {
            return CommandResults.Failure($"{cli} {string.Join(' ', ListVolumes)} exited with {listed}: {volumeList}");
        }
        var existing = volumeList
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
        var manifestVolumes = localtest.Stack.Resolver.Manifest.Volumes.Select(v => v.Name);
        foreach (var arguments in Commands(ids, manifestVolumes, existing))
        {
            logger.LogRuntimeCommand(cli, string.Join(' ', arguments));
            var (exitCode, output) = await RunAsync(cli, arguments, context.CancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                return CommandResults.Failure($"{cli} {string.Join(' ', arguments)} exited with {exitCode}: {output}");
            }
        }
        return CommandResults.Success();
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string cli,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var start = new ProcessStartInfo(cli)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{cli} did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await stdout.ConfigureAwait(false);
        var errors = await stderr.ConfigureAwait(false);
        return (process.ExitCode, process.ExitCode == 0 ? output : (errors + output).Trim());
    }
}
