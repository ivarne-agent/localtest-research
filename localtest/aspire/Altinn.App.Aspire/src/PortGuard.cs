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
    /// on every interface or answers on loopback, unless <paramref name="heldByThisAppHost" /> says the holder is this
    /// AppHost's own front door: the localtest container is persistent, so it keeps the port between AppHost runs.
    /// </summary>
    public static void EnsureFree(int port, Func<bool>? heldByThisAppHost = null) =>
        EnsureFree(port, "local.altinn.cloud", heldByThisAppHost);

    /// <summary>
    /// As <see cref="EnsureFree(int, Func{bool})" />, naming the public origin at <paramref name="host" /> in the
    /// message.
    /// </summary>
    public static void EnsureFree(int port, string host, Func<bool>? heldByThisAppHost = null)
    {
        if (IsFree(port) || heldByThisAppHost?.Invoke() == true)
        {
            return;
        }
        var holder = Holder(port);
        throw new InvalidOperationException(
            $"Port {port} is in use{(holder is null ? "" : $" by {holder}")}: another localtest stack or AppHost is running "
                + "(studioctl-server for `studioctl env up -r localtest-v2`, or another AppHost's front door). "
                + "Stop it first (`studioctl env down -r localtest-v2`, or stop the other AppHost and remove its front door "
                + $"container); the localtest platform needs the port for its front door on http://{host}:"
                + port.ToString(CultureInfo.InvariantCulture)
                + "."
        );
    }

    /// <summary>
    /// The check before a start, given this AppHost's persistent front door (<c>localtest-&lt;hash&gt;</c>) when it
    /// runs. The full stack's front door is persistent, so its own container holding the port is accepted: the run
    /// reuses it. Minimal mode's front door is a session container, which an AppHost's stop removes; this AppHost's
    /// persistent one, left running by a run of the full stack, holds the port that minimal mode needs, and is named.
    /// </summary>
    /// <param name="port">The front door's port.</param>
    /// <param name="host">The public host, for the message.</param>
    /// <param name="persistentFrontDoor">Whether the starting stack's front door is persistent (the full stack).</param>
    /// <param name="ownFrontDoor">This AppHost's persistent front door's container when it runs, else null.</param>
    /// <param name="cli">The container runtime's CLI, for the remedy.</param>
    public static void EnsureFree(int port, string host, bool persistentFrontDoor, string? ownFrontDoor, string cli)
    {
        if (persistentFrontDoor)
        {
            EnsureFree(port, host, () => ownFrontDoor is not null);
            return;
        }
        if (ownFrontDoor is not null && !IsFree(port))
        {
            throw new InvalidOperationException(
                $"Port {port.ToString(CultureInfo.InvariantCulture)} is in use by {ownFrontDoor}, this AppHost's "
                    + "persistent front door from a run of the full stack (AddAltinnLocaltest); minimal mode starts its "
                    + $"own. Remove it first (`{cli} rm --force {ownFrontDoor}`; the full stack's volumes stay)."
            );
        }
        EnsureFree(port, host);
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
                    return $"{ProcessName(process) ?? "a process"} (pid {pid})";
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return null;
    }

    /// <summary>
    /// The program's name from its command line (the assembly for <c>dotnet app.dll</c>), else the kernel's
    /// 15-character <c>comm</c>.
    /// </summary>
    private static string? ProcessName(string process)
    {
        var arguments = ReadOrNull(Path.Combine(process, "cmdline"))
            ?.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        if (arguments is { Length: > 0 })
        {
            var program = Path.GetFileName(arguments[0]);
            return program == "dotnet" && arguments.Length > 1
                ? Path.GetFileNameWithoutExtension(arguments[1])
                : program;
        }
        return ReadOrNull(Path.Combine(process, "comm"))?.Trim();
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
