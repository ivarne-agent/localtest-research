using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire;

/// <summary>
/// The shared test certificate every app and component signs platform access tokens with: the file studioctl embeds
/// (<c>internal/cmd/env/localtestv2/assets/localtest-accesstoken.pfx</c>), a public test key pair.
/// </summary>
internal static class LocaltestCertificate
{
    /// <summary>The embedded certificate's resource name.</summary>
    public const string Resource = "Altinn.App.Aspire.localtest-accesstoken.pfx";

    /// <summary>
    /// Writes the certificate into <paramref name="directory" /> unless it is there already, as studioctl's
    /// <c>ensureCertificate</c> does; world-readable on purpose, because the containers run as their image users.
    /// </summary>
    public static string Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Topology.CertificateFileName);
        var certificate = Read();
        if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(certificate))
        {
            File.WriteAllBytes(path, certificate);
        }
        if (!OperatingSystem.IsWindows())
        {
            const UnixFileMode readable =
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
            File.SetUnixFileMode(path, readable);
            File.SetUnixFileMode(
                directory,
                readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute
            );
        }
        return path;
    }

    /// <summary>The certificate's bytes.</summary>
    public static byte[] Read()
    {
        using var stream =
            typeof(LocaltestCertificate).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"The package has no embedded resource {Resource}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
