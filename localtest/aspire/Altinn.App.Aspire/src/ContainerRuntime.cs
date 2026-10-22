using System.Diagnostics;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire;

/// <summary>The container runtime's CLI, which the health probes and the reset command run.</summary>
internal static class ContainerRuntime
{
    /// <summary>The snapshot property that holds a container's ID.</summary>
    public const string ContainerIdProperty = "container.id";

    /// <summary>The container runtime's CLI: Aspire's <c>ASPIRE_CONTAINER_RUNTIME</c>, otherwise docker.</summary>
    public static string Cli(IConfiguration configuration)
    {
        if (configuration["ASPIRE_CONTAINER_RUNTIME"] is { Length: > 0 } runtime)
        {
            return runtime;
        }
        return configuration["DOTNET_ASPIRE_CONTAINER_RUNTIME"] is { Length: > 0 } legacy ? legacy : "docker";
    }

    /// <summary>The ID of a resource's running container, from its snapshot; null before it has one.</summary>
    public static string? ContainerId(ResourceNotificationService notifications, string resourceName) =>
        notifications.TryGetCurrentState(resourceName, out var state)
        && state.Snapshot.Properties.FirstOrDefault(p => p.Name == ContainerIdProperty)?.Value
            is string { Length: > 0 } id
            ? id
            : null;

    /// <summary>Runs the CLI; returns its exit code and its output (stderr first when it failed).</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(
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
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var errors = await stderr.ConfigureAwait(false);
            return (process.ExitCode, process.ExitCode == 0 ? output : (errors + output).Trim());
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }
}
