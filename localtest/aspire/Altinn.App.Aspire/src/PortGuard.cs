using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Altinn.App.Aspire;

/// <summary>
/// Refuses to start the platform when its front door's port is taken: by studioctl-server (a studioctl
/// <c>localtest-v2</c> stack), by another AppHost's front door, or by anything else. Without the check the front door
/// would fail later with a less helpful error, or a request would reach the other stack.
/// </summary>
internal static class PortGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException" /> naming the holder when <paramref name="port" /> cannot be bound
    /// on every interface or answers on loopback.
    /// </summary>
    public static void EnsureFree(int port)
    {
        if (IsFree(port))
        {
            return;
        }
        var holder = Holder(port);
        throw new InvalidOperationException(
            $"Port {port} is in use{(holder is null ? "" : $" by {holder}")}: another localtest stack or AppHost is running "
                + "(studioctl-server for `studioctl env up -r localtest-v2`, or another AppHost's front door). "
                + "Stop it first (`studioctl env down -r localtest-v2`, or stop the other AppHost and remove its front door "
                + "container); the localtest platform needs the port for its front door on http://local.altinn.cloud:"
                + port.ToString(CultureInfo.InvariantCulture)
                + "."
        );
    }

    /// <summary>Whether the port can be bound on every interface and nothing answers on loopback.</summary>
    public static bool IsFree(int port)
    {
        try
        {
            using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            listener.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (SocketException)
        {
            return false;
        }
        // A container runtime may forward the port without a listening socket on the host.
        try
        {
            using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            var connect = client.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port));
            return !connect.Wait(TimeSpan.FromMilliseconds(500)) || !client.Connected;
        }
        catch (AggregateException e) when (e.InnerException is SocketException)
        {
            return true;
        }
    }

    /// <summary>
    /// The process listening on the port, from <c>/proc/net/tcp</c> and the processes' socket descriptors (Linux);
    /// null when it cannot be told (another platform, another user's process, a runtime's forwarding).
    /// </summary>
    public static string? Holder(int port)
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }
        try
        {
            var inodes = ListeningInodes(port);
            if (inodes.Count == 0)
            {
                return null;
            }
            foreach (var process in System.IO.Directory.EnumerateDirectories("/proc"))
            {
                var pid = Path.GetFileName(process);
                if (!int.TryParse(pid, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }
                if (OwnsSocket(process, inodes))
                {
                    var name = ReadOrNull(Path.Combine(process, "comm"))?.Trim();
                    return $"{name ?? "a process"} (pid {pid})";
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    private static HashSet<string> ListeningInodes(int port)
    {
        const string listen = "0A";
        var hexPort = port.ToString("X4", CultureInfo.InvariantCulture);
        var inodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var table in new[] { "/proc/net/tcp", "/proc/net/tcp6" })
        {
            var lines = ReadOrNull(table)?.Split('\n').Skip(1) ?? [];
            foreach (var line in lines)
            {
                var fields = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (
                    fields.Length > 9
                    && fields[3] == listen
                    && fields[1].EndsWith(":" + hexPort, StringComparison.OrdinalIgnoreCase)
                )
                {
                    inodes.Add(fields[9]);
                }
            }
        }
        return inodes;
    }

    private static bool OwnsSocket(string process, HashSet<string> inodes)
    {
        try
        {
            foreach (var descriptor in System.IO.Directory.EnumerateFileSystemEntries(Path.Combine(process, "fd")))
            {
                var target = new FileInfo(descriptor).LinkTarget;
                if (
                    target is not null
                    && target.StartsWith("socket:[", StringComparison.Ordinal)
                    && inodes.Contains(target["socket:[".Length..^1])
                )
                {
                    return true;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        return false;
    }

    private static string? ReadOrNull(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
