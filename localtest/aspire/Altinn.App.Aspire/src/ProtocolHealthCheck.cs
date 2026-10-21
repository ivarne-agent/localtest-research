using System.Net.Sockets;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Altinn.App.Aspire;

/// <summary>
/// A host-side health check for a component whose manifest health check runs in the container (Aspire has no exec
/// health checks): a protocol handshake over the component's TCP endpoint. A bare TCP connect would not do, because
/// DCP's proxy accepts the connection whether or not the container listens.
/// </summary>
internal sealed class ProtocolHealthCheck(EndpointReference endpoint, ProtocolHealthCheck.Protocol protocol)
    : IHealthCheck
{
    /// <summary>The handshakes.</summary>
    public enum Protocol
    {
        /// <summary>
        /// Postgres's <c>SSLRequest</c>: a server that accepts connections answers one byte, <c>S</c> or <c>N</c>. The
        /// image's init phase listens on the Unix socket only, so the check passes once the real server is up, as
        /// <c>pg_isready -h 127.0.0.1</c> does.
        /// </summary>
        Postgres,

        /// <summary>Redis's <c>PING</c>, answered <c>+PONG</c>, as <c>redis-cli ping</c>.</summary>
        Redis,
    }

    private static readonly byte[] _postgresSslRequest = [0, 0, 0, 8, 0x04, 0xD2, 0x16, 0x2F];
    private static readonly byte[] _redisPing = "PING\r\n"u8.ToArray();

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            var (request, expected) = protocol switch
            {
                Protocol.Postgres => (_postgresSslRequest, "SN"),
                _ => (_redisPing, "+"),
            };
            await stream.WriteAsync(request, timeout.Token).ConfigureAwait(false);
            var answer = new byte[1];
            var read = await stream.ReadAsync(answer, timeout.Token).ConfigureAwait(false);
            return read == 1 && expected.Contains((char)answer[0], StringComparison.Ordinal)
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{protocol} did not answer the handshake.");
        }
        catch (Exception e) when (e is SocketException or IOException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"{protocol} is not reachable: {e.Message}");
        }
    }
}
