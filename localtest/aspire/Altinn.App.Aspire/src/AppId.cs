using System.Text.Json;

namespace Altinn.App.Aspire;

/// <summary>An app's id, <c>org/app</c>.</summary>
/// <param name="Org">The owner.</param>
/// <param name="App">The app's name.</param>
internal sealed record AppId(string Org, string App)
{
    /// <inheritdoc />
    public override string ToString() => $"{Org}/{App}";

    /// <summary>The application metadata's path in an app's project folder.</summary>
    public static string MetadataPath(string projectDirectory) =>
        Path.Combine(projectDirectory, "config", "applicationmetadata.json");

    /// <summary>Reads the <c>id</c> of the project folder's application metadata.</summary>
    /// <exception cref="InvalidOperationException">The file is missing, does not parse, or has no <c>org/app</c> id.</exception>
    public static AppId Read(string projectDirectory)
    {
        var path = MetadataPath(projectDirectory);
        string? id;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            id =
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("id", out var value)
                && value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"{path} cannot be read: {e.Message}", e);
        }
        var parts = id?.Split('/') ?? [];
        if (parts is not [{ Length: > 0 } org, { Length: > 0 } app])
        {
            throw new InvalidOperationException($"{path} has no \"id\" of the form org/app (it has \"{id}\").");
        }
        return new AppId(org, app);
    }
}
