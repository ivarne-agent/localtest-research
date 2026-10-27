using System.Globalization;
using System.Security.Cryptography;
using Altinn.App.Aspire.Manifest;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire;

/// <summary>
/// The host's CA bundle, as studioctl's runner passes it on (<c>internal/cmd/env/localtestv2/cabundle.go</c>): behind a
/// TLS-intercepting proxy the developer names the proxy's CA in <c>STUDIO_CA_BUNDLE</c>, a PEM file, and the containers
/// the manifest marks with <c>caBundle</c> (pdf3) mount a copy of it read-only at
/// <see cref="LocaltestManifest.CaBundlePath" />, with the variable pointing there. The copy lies in the data directory
/// under its hash (<c>ca-bundle/&lt;16 hex digits&gt;.pem</c>), readable by the images' users, so a changed bundle
/// changes the persistent containers' mounts and Aspire recreates them.
/// </summary>
internal sealed class HostCaBundle
{
    /// <summary>How many hex digits of the bundle's SHA-256 name the copy.</summary>
    private const int SumLength = 16;

    private readonly byte[] _content;

    private HostCaBundle(string source, byte[] content)
    {
        Source = source;
        _content = content;
        Sum = Convert.ToHexStringLower(SHA256.HashData(content));
    }

    /// <summary>The host path <c>STUDIO_CA_BUNDLE</c> names.</summary>
    public string Source { get; }

    /// <summary>The SHA-256 of the content, hex.</summary>
    public string Sum { get; }

    /// <summary>The copy's file name: the first digits of <see cref="Sum" />.</summary>
    public string FileName => Sum[..SumLength] + ".pem";

    /// <summary>The folder of the copy in <paramref name="dataDirectory" />.</summary>
    public static string Directory(string dataDirectory) => Path.Combine(dataDirectory, "ca-bundle");

    /// <summary>
    /// Reads the file <c>STUDIO_CA_BUNDLE</c> names in the AppHost's configuration (its environment). Unset, or naming
    /// no file, is none; a file that cannot be read is none with a warning for the AppHost's log.
    /// </summary>
    public static (HostCaBundle? Bundle, string? Warning) Read(IConfiguration configuration)
    {
        if (configuration[LocaltestManifest.CaBundleVariable] is not { Length: > 0 } source)
        {
            return (null, null);
        }
        try
        {
            return (new HostCaBundle(source, File.ReadAllBytes(source)), null);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return (null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (
                null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{LocaltestManifest.CaBundleVariable}={source}: {e.Message}; the containers start without the CA bundle"
                )
            );
        }
    }

    /// <summary>
    /// Makes the copy's folder in <paramref name="dataDirectory" /> hold exactly the copy of <paramref name="bundle" />,
    /// world-readable; with none it removes the folder.
    /// </summary>
    public static void Ensure(string dataDirectory, HostCaBundle? bundle)
    {
        var directory = Directory(dataDirectory);
        if (bundle is null)
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, recursive: true);
            }
            return;
        }
        System.IO.Directory.CreateDirectory(directory);
        foreach (var other in System.IO.Directory.EnumerateFileSystemEntries(directory))
        {
            if (Path.GetFileName(other) == bundle.FileName)
            {
                continue;
            }
            if (System.IO.Directory.Exists(other))
            {
                System.IO.Directory.Delete(other, recursive: true);
            }
            else
            {
                File.Delete(other);
            }
        }
        var path = Path.Combine(directory, bundle.FileName);
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(bundle._content))
        {
            File.WriteAllBytes(path, bundle._content);
        }
        if (!OperatingSystem.IsWindows())
        {
            // World-readable on purpose: the containers run as their image users.
            const UnixFileMode readable =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            File.SetUnixFileMode(path, readable);
            File.SetUnixFileMode(
                directory,
                readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute
            );
        }
    }
}
