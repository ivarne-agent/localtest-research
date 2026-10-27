using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Altinn.App.Aspire.Manifest;
using Microsoft.Extensions.Configuration;

namespace Altinn.App.Aspire;

/// <summary>
/// The host's CA bundle, as studioctl passes it on (<c>internal/cmd/env/stack/cabundle.go</c> on devenv's
/// <c>pkg/cabundle</c>): behind a TLS-intercepting proxy the developer names the proxy's CA in <c>STUDIO_CA_BUNDLE</c>,
/// a PEM file, and the containers the manifest marks with <c>caBundle</c> (pdf3) mount a copy of its usable
/// certificates read-only at <see cref="LocaltestManifest.CaBundlePath" />, with the variable pointing there. The copy
/// lies in the data directory under the digest of what it holds (<c>ca-bundle/ca-bundle-&lt;16 hex digits&gt;.pem</c>),
/// readable by the images' users, so a changed bundle changes the persistent containers' mounts and Aspire recreates
/// them.
/// </summary>
internal sealed class HostCaBundle
{
    /// <summary>How many hex digits of the digest name the copy.</summary>
    private const int DigestLength = 16;

    /// <summary>How many skipped certificates one warning lists, as studioctl.</summary>
    private const int MaxSkippedListed = 3;

    private const string CertificateLabel = "CERTIFICATE";

    private const string StartsWithout = "the containers start without the CA bundle";

    private static readonly byte[] _pemBegin = "-----BEGIN "u8.ToArray();
    private static readonly byte[] _pemBeginCertificate = "-----BEGIN CERTIFICATE-----"u8.ToArray();

    private readonly byte[] _content;

    private HostCaBundle(byte[] content)
    {
        _content = content;
        Digest = Convert.ToHexStringLower(SHA256.HashData(content));
    }

    /// <summary>The SHA-256 of the usable certificates, hex.</summary>
    public string Digest { get; }

    /// <summary>The copy's file name: studioctl's, <c>ca-bundle-</c> and the first digits of <see cref="Digest" />.</summary>
    public string FileName => "ca-bundle-" + Digest[..DigestLength] + ".pem";

    /// <summary>The folder of the copy in <paramref name="dataDirectory" />.</summary>
    public static string Directory(string dataDirectory) => Path.Combine(dataDirectory, "ca-bundle");

    /// <summary>
    /// Reads the file <c>STUDIO_CA_BUNDLE</c> names in the AppHost's configuration (its environment) and keeps its usable
    /// certificates: the <c>CERTIFICATE</c> blocks that decode to an X.509 certificate, re-encoded as PEM without the
    /// text between them. Unset is none. A certificate that is not usable is left out, with a warning naming it by line;
    /// a variable naming no readable file, or a file without a usable certificate, is none with a warning. The warning
    /// is for the AppHost's log.
    /// </summary>
    public static (HostCaBundle? Bundle, string? Warning) Read(IConfiguration configuration)
    {
        if (configuration[LocaltestManifest.CaBundleVariable] is not { Length: > 0 } source)
        {
            return (null, null);
        }
        var path = Path.GetFullPath(source);
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, Format($"{LocaltestManifest.CaBundleVariable}={path}: {e.Message}; {StartsWithout}"));
        }
        var (certificates, skipped) = UsableCertificates(data);
        if (certificates.Length == 0)
        {
            var why = skipped.Count == 0 ? "no PEM CERTIFICATE block" : $"none usable; {skipped[0]}";
            return (
                null,
                Format(
                    $"{LocaltestManifest.CaBundleVariable}={path} holds no usable certificate: {why}; {StartsWithout}"
                )
            );
        }
        var bundle = new HostCaBundle(certificates);
        if (skipped.Count == 0)
        {
            return (bundle, null);
        }
        var listed = string.Join("; ", skipped.Take(MaxSkippedListed));
        var more = skipped.Count > MaxSkippedListed ? Format($"; and {skipped.Count - MaxSkippedListed} more") : "";
        var noun = skipped.Count == 1 ? "certificate" : "certificates";
        return (
            bundle,
            Format(
                $"{LocaltestManifest.CaBundleVariable} ({path}): skipping {skipped.Count} unusable {noun}, the containers trust the rest: {listed}{more}"
            )
        );
    }

    /// <summary>
    /// The <c>CERTIFICATE</c> blocks of <paramref name="data" /> that decode to an X.509 certificate, re-encoded as PEM,
    /// and why each other one was skipped, by line. Each block ends where the next begins, so a malformed block never
    /// swallows the one after it. Other PEM blocks, such as keys, and the text around the blocks are left out without
    /// a reason.
    /// </summary>
    private static (byte[] Certificates, List<string> Skipped) UsableCertificates(byte[] data)
    {
        var certificates = new StringBuilder();
        var skipped = new List<string>();
        var span = data.AsSpan();
        var offset = 0;
        while (true)
        {
            var found = span[offset..].IndexOf(_pemBegin);
            if (found < 0)
            {
                return (Encoding.ASCII.GetBytes(certificates.ToString()), skipped);
            }
            var start = offset + found;
            var next = span[(start + _pemBegin.Length)..].IndexOf(_pemBegin);
            var end = next < 0 ? data.Length : start + _pemBegin.Length + next;
            offset = end;
            var chunk = span[start..end];
            if (!chunk.StartsWith(_pemBeginCertificate))
            {
                continue;
            }
            var line = 1 + span[..start].Count((byte)'\n');
            var text = Encoding.ASCII.GetString(chunk);
            if (
                !PemEncoding.TryFind(text, out var fields)
                || fields.Location.Start.Value != 0
                || text[fields.Label] != CertificateLabel
            )
            {
                skipped.Add(Format($"line {line}: malformed PEM block"));
                continue;
            }
            var der = Convert.FromBase64String(text[fields.Base64Data]);
            try
            {
                using var certificate = X509CertificateLoader.LoadCertificate(der);
            }
            catch (CryptographicException e)
            {
                skipped.Add(Format($"line {line}: {e.Message}"));
                continue;
            }
            certificates.Append(PemEncoding.Write(CertificateLabel, der)).Append('\n');
        }
    }

    private static string Format(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

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
