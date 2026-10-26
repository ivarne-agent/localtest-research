namespace Altinn.App.Aspire.Manifest;

/// <summary>
/// The files that ship with the manifest: every file in the folders beside <c>manifest.json</c>, at its path relative
/// to the manifest's directory, slash-separated (<c>monitoring/otel-collector.yaml</c>). A container's <c>files</c>
/// names one of them, or a folder holding some, as the source of a read-only mount. The files at the top level (the
/// manifest itself, its README and studioctl's code) are not among them. The package embeds them all (the project file's
/// <c>EmbeddedResource</c> of every folder beside the manifest), as studioctl does (<c>manifest.go</c>'s
/// <c>bundle</c>).
/// </summary>
internal sealed class ManifestFiles
{
    /// <summary>The prefix of an embedded file's resource name, before its path relative to the manifest.</summary>
    public const string ResourcePrefix = "Altinn.App.Aspire.manifest-files/";

    private static readonly Lazy<ManifestFiles> _embedded = new(() =>
    {
        var assembly = typeof(ManifestFiles).Assembly;
        return new ManifestFiles(
            assembly
                .GetManifestResourceNames()
                .Where(r => r.StartsWith(ResourcePrefix, StringComparison.Ordinal))
                .ToDictionary(
                    // MSBuild's RecursiveDir has the build machine's separator.
                    r => r[ResourcePrefix.Length..].Replace('\\', '/'),
                    r =>
                        (Func<byte[]>)(
                            () =>
                            {
                                using var stream =
                                    assembly.GetManifestResourceStream(r)
                                    ?? throw new InvalidOperationException(
                                        $"The package has no embedded resource {r}."
                                    );
                                using var copy = new MemoryStream();
                                stream.CopyTo(copy);
                                return copy.ToArray();
                            }
                        ),
                    StringComparer.Ordinal
                )
        );
    });

    private readonly Dictionary<string, Func<byte[]>> _files;

    private ManifestFiles(Dictionary<string, Func<byte[]>> files)
    {
        _files = files
            .Where(kv => kv.Key.Contains('/', StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Names = [.. _files.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>The files the package embeds.</summary>
    public static ManifestFiles Embedded => _embedded.Value;

    /// <summary>No files.</summary>
    public static ManifestFiles None { get; } = new([]);

    /// <summary>
    /// Files by their path relative to the manifest's directory; those at the top level are left out, as they are when
    /// they ship.
    /// </summary>
    public static ManifestFiles From(IReadOnlyDictionary<string, byte[]> files) =>
        new(files.ToDictionary(kv => kv.Key, kv => (Func<byte[]>)(() => kv.Value), StringComparer.Ordinal));

    /// <summary>The files' paths, in ordinal order.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>A file's content.</summary>
    public byte[] Read(string name) =>
        _files.TryGetValue(name, out var read)
            ? read()
            : throw new FileNotFoundException($"No file {name} ships with the manifest.", name);

    /// <summary>Whether <paramref name="source" /> is one of the files or a folder holding one.</summary>
    public bool Ships(string source) =>
        Names.Any(name => name == source || name.StartsWith(source + "/", StringComparison.Ordinal));

    /// <summary>
    /// Writes the files into <paramref name="directory" /> at their paths, as studioctl's <c>ensureFiles</c> does: a
    /// file whose content is there already is left alone and a changed one is rewritten in place, so a running
    /// container's mount keeps pointing at it; what does not ship is removed. World-readable on purpose, because the
    /// containers run as their image users.
    /// </summary>
    public void Ensure(string directory)
    {
        Directory.CreateDirectory(directory);
        var root = Path.GetFullPath(directory);
        RemoveUnshipped(root);
        MakeReadable(root, directory: true);
        foreach (var name in Names)
        {
            var path = Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
            var folder = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(folder);
            for (var dir = folder; dir.Length > root.Length; dir = Path.GetDirectoryName(dir)!)
            {
                MakeReadable(dir, directory: true);
            }
            var content = Read(name);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
            {
                // Truncates and writes the same file, which a bind mount of it keeps seeing.
                using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                stream.Write(content);
            }
            MakeReadable(path, directory: false);
        }
    }

    private void RemoveUnshipped(string root)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            for (var dir = name; dir.Contains('/', StringComparison.Ordinal); )
            {
                dir = dir[..dir.LastIndexOf('/')];
                keep.Add(dir);
            }
        }
        var shipped = Names.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
        {
            Remove(root, entry, keep, shipped);
        }
    }

    private static void Remove(string root, FileSystemInfo entry, HashSet<string> keep, HashSet<string> shipped)
    {
        var name = Path.GetRelativePath(root, entry.FullName).Replace(Path.DirectorySeparatorChar, '/');
        if (entry is DirectoryInfo dir && entry.LinkTarget is null)
        {
            if (!keep.Contains(name))
            {
                dir.Delete(recursive: true);
                return;
            }
            foreach (var child in dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly))
            {
                Remove(root, child, keep, shipped);
            }
        }
        else if (!shipped.Contains(name) || entry.LinkTarget is not null)
        {
            entry.Delete();
        }
    }

    private static void MakeReadable(string path, bool directory)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        const UnixFileMode readable =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
        File.SetUnixFileMode(
            path,
            directory
                ? readable | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute
                : readable
        );
    }
}
