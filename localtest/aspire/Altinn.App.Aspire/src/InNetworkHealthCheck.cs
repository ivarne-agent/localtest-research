using System.Globalization;
using Altinn.App.Aspire.Manifest;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>
/// A container's health check run from inside the stack's network, since only the front door publishes a port: the
/// manifest's HTTP path (a GET answering 200) or <c>tcp</c> (a connection), checked by bash over <c>/dev/tcp</c> in the
/// Postgres container (<c>&lt;runtime&gt; exec</c>), the one image with bash, as studioctl's probe containers do with
/// the same image (<c>internal/cmd/env/localtestv2/health.go</c>). Postgres's own check connects to its network address,
/// which its image's init phase does not listen on, so it passes once the real server is up.
/// <para>
/// A check gates what <c>WaitFor</c>s the container. Once it passed for a container, it stays healthy until the
/// container is replaced (a new container ID): the check is a start gate, as studioctl's, not a liveness probe, so a
/// running stack costs no execs.
/// </para>
/// </summary>
internal sealed class InNetworkHealthCheck(
    HealthTarget target,
    ResourceNotificationService notifications,
    IConfiguration configuration
) : IHealthCheck
{
    /// <summary>The container the probes run in.</summary>
    public const string ProbeContainer = "postgres";

    /// <summary>How many times one check tries, <see cref="IntervalSeconds" /> apart, before it reports unhealthy.</summary>
    public const int Attempts = 10;

    /// <summary>The pause between two tries, in seconds.</summary>
    public const string IntervalSeconds = "0.5";

    /// <summary>How long a try waits for the status line once connected, in seconds.</summary>
    public const int ReadTimeoutSeconds = 5;

    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(60);

    private string? _healthyContainer;

    /// <summary>The target, for the tests.</summary>
    public HealthTarget Target => target;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var container = ContainerRuntime.ContainerId(notifications, target.Name);
        if (container is not null && container == _healthyContainer)
        {
            return HealthCheckResult.Healthy();
        }
        var probe = ContainerRuntime.ContainerId(notifications, ProbeContainer);
        if (probe is null)
        {
            return HealthCheckResult.Unhealthy(
                $"{ProbeContainer}, which runs the health probes, has no container yet."
            );
        }
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(_limit);
        try
        {
            var (exitCode, output) = await ContainerRuntime
                .RunAsync(
                    ContainerRuntime.Cli(configuration),
                    ["exec", probe, "bash", "-c", Script(target)],
                    limit.Token
                )
                .ConfigureAwait(false);
            if (exitCode == 0)
            {
                _healthyContainer = container;
                return HealthCheckResult.Healthy(target.ToString());
            }
            return HealthCheckResult.Unhealthy($"{target} did not answer ({exitCode}{Detail(output)}).");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"{target}: the probe did not finish in {_limit.TotalSeconds:F0} s.");
        }
    }

    /// <summary>
    /// The bash a probe runs: up to <see cref="Attempts" /> tries, exit 0 at the first success. The HTTP check reads
    /// the status line only; the request is HTTP/1.0, so the server closes the connection. studioctl's
    /// <c>probeScript</c>, with a shorter loop: Aspire calls the check again.
    /// </summary>
    public static string Script(HealthTarget target)
    {
        var port = target.Port.ToString(CultureInfo.InvariantCulture);
        var connect = "{ exec 3<>/dev/tcp/" + target.Name + "/" + port + "; } 2>/dev/null";
        var check = "if " + connect + "; then exec 3<&-; exit 0; fi";
        if (!target.Tcp)
        {
            check =
                "if "
                + connect
                + "; then "
                + "printf 'GET "
                + target.Path
                + " HTTP/1.0\\r\\nHost: "
                + target.Name
                + "\\r\\n\\r\\n' >&3; "
                + "IFS= read -r -t "
                + ReadTimeoutSeconds.ToString(CultureInfo.InvariantCulture)
                + " line <&3 || line=; exec 3<&-; "
                + "case \"$line\" in *' 200 '*) exit 0;; esac; fi";
        }
        return "for i in $(seq 1 "
            + Attempts.ToString(CultureInfo.InvariantCulture)
            + "); do "
            + check
            + "; sleep "
            + IntervalSeconds
            + "; done; exit 1";
    }

    private static string Detail(string output) =>
        string.IsNullOrWhiteSpace(output) ? "" : ": " + output.ReplaceLineEndings(" ").Trim();
}
