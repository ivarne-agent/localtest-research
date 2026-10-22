using System.Text.Json;

namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>
/// Reads the artefacts from the app's folder, as Designer pushes them on deploy: <c>config/applicationmetadata.json</c>,
/// <c>config/authorization/policy.xml</c>, every <c>config/texts/resource.{language}.json</c> and
/// <c>wwwroot/testData.json</c>, under the app project folder <c>App/</c>. The Aspire package's case: it knows the
/// project path, and the app does not need to be up. A missing policy, test data or text folder means "none".
/// </summary>
public sealed class DirectoryAppSource : IAppSource
{
    private const string MetadataPath = "config/applicationmetadata.json";

    /// <param name="appRoot">
    /// The app repository (the folder holding <c>App/</c>), or the app project folder <c>App/</c> itself, which is the
    /// folder of the project an Aspire AppHost references.
    /// </param>
    public DirectoryAppSource(string appRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appRoot);
        var full = Path.GetFullPath(appRoot);
        var nested = Path.Combine(full, "App");
        AppDirectory = Directory.Exists(Path.Combine(nested, "config")) ? nested : full;
    }

    /// <summary>The app project folder the files are read from (the one holding <c>config/</c>).</summary>
    public string AppDirectory { get; }

    /// <summary>Whether the folder holds an app, that is its application metadata.</summary>
    public bool HasApp => File.Exists(FilePath(MetadataPath));

    /// <summary>The app's <c>{org}/{app}</c> from the <c>id</c> of its application metadata.</summary>
    /// <exception cref="InvalidOperationException">No metadata, or no valid <c>id</c> in it.</exception>
    public async Task<AppId> ReadAppId(CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadata(cancellationToken);
        try
        {
            using var json = JsonDocument.Parse(metadata);
            if (json.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                return AppId.Parse(id.GetString() ?? "");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            throw new InvalidOperationException($"{FilePath(MetadataPath)} has no valid id: {ex.Message}", ex);
        }
        throw new InvalidOperationException($"{FilePath(MetadataPath)} has no id");
    }

    /// <inheritdoc />
    public async Task<AppArtifacts> Read(AppId appId, CancellationToken cancellationToken)
    {
        var metadata = await ReadMetadata(cancellationToken);
        var policy = await ReadText("config/authorization/policy.xml", cancellationToken);
        var testData = await ReadBytes("wwwroot/testData.json", cancellationToken);
        var texts = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.OrdinalIgnoreCase);
        var textsDirectory = FilePath("config/texts");
        if (Directory.Exists(textsDirectory))
        {
            var files = Directory
                .EnumerateFiles(textsDirectory, "resource.*.json")
                .Order(StringComparer.Ordinal)
                .ToArray();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var language = name["resource.".Length..];
                if (!string.IsNullOrWhiteSpace(language))
                    texts[language] = await File.ReadAllBytesAsync(file, cancellationToken);
            }
        }

        return new AppArtifacts(metadata, policy, testData is null ? (ReadOnlyMemory<byte>?)null : testData, texts);
    }

    /// <summary>The app project folder, as the provisioner logs it.</summary>
    public override string ToString() => AppDirectory;

    private async Task<byte[]> ReadMetadata(CancellationToken cancellationToken) =>
        await ReadBytes(MetadataPath, cancellationToken)
        ?? throw new InvalidOperationException($"{FilePath(MetadataPath)} does not exist");

    private async Task<byte[]?> ReadBytes(string relative, CancellationToken cancellationToken)
    {
        var path = FilePath(relative);
        return File.Exists(path) ? await File.ReadAllBytesAsync(path, cancellationToken) : null;
    }

    private async Task<string?> ReadText(string relative, CancellationToken cancellationToken)
    {
        var path = FilePath(relative);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    private string FilePath(string relative) =>
        Path.Combine(AppDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
}
