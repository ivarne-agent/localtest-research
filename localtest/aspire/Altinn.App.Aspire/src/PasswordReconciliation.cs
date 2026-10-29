using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire;

/// <summary>
/// Sets the database passwords in the Postgres container once it is healthy and before its dependants start, as
/// studioctl's runner does (<c>internal/cmd/env/localtestv2/passwords.go</c>): <c>&lt;runtime&gt; exec -i</c> with the
/// manifest's <see cref="LocaltestManifest.ReconcileCommand" /> and its <see cref="LocaltestManifest.ReconcileScript" />
/// on standard input. The passwords come from the container's environment; neither the arguments nor the script hold
/// one. Part of Postgres's health check (<see cref="InNetworkHealthCheck" />), so a <c>WaitFor</c> on Postgres waits
/// for it too, and it runs once per container.
/// </summary>
internal sealed class PasswordReconciliation(
    LocaltestManifest manifest,
    Func<IReadOnlyDictionary<string, string>?> passwords
)
{
    /// <summary>The <c>exec</c> arguments for the container.</summary>
    public IReadOnlyList<string> Arguments(string container) => ["exec", "-i", container, .. manifest.ReconcileCommand];

    /// <summary>The script on standard input.</summary>
    public string Script => manifest.ReconcileScript;

    /// <summary>Runs it in the container; null when it succeeded, otherwise the failure with every password masked.</summary>
    public async Task<string?> RunAsync(string cli, string container, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await ContainerRuntime
            .RunAsync(cli, Arguments(container), cancellationToken, Script)
            .ConfigureAwait(false);
        return exitCode == 0
            ? null
            : $"setting the database passwords failed ({exitCode}): {Masked(output.ReplaceLineEndings(" ").Trim())}";
    }

    /// <summary>The text with every password replaced.</summary>
    public string Masked(string text)
    {
        foreach (var password in passwords()?.Values ?? [])
        {
            if (password.Length > 0)
            {
                text = text.Replace(password, "***", StringComparison.Ordinal);
            }
        }
        return text;
    }
}
