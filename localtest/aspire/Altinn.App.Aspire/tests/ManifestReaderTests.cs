using System.Text.Json;
using Altinn.App.Aspire.Manifest;

namespace Altinn.App.Aspire.Tests;

public sealed class ManifestReaderTests
{
    private const string SchemaId = "https://altinn.studio/schemas/localtest-v2/manifest.schema.json";

    [Fact]
    public void TheEmbeddedSchemaIsTheVersionThisReaderKnows()
    {
        using var schema = JsonDocument.Parse(ManifestLoader.ReadResource(ManifestLoader.SchemaResource));
        var root = schema.RootElement;

        Assert.Equal(SchemaId, root.GetProperty("$id").GetString());
        Assert.Equal(
            ManifestLoader.SupportedVersion,
            root.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32()
        );
        Assert.Equal(ManifestLoader.SupportedVersion, ManifestLoader.Default.Version);
    }

    /// <summary>
    /// Every key the schema defines anywhere is a property of the reader's model, so a field added to the schema (and
    /// the manifest) fails here until the reader knows it; the reader refuses unknown keys when it parses.
    /// </summary>
    [Fact]
    public void TheReaderKnowsEveryKeyTheSchemaDefines()
    {
        using var schema = JsonDocument.Parse(ManifestLoader.ReadResource(ManifestLoader.SchemaResource));
        var schemaKeys = new SortedSet<string>(StringComparer.Ordinal);
        CollectPropertyNames(schema.RootElement, schemaKeys);

        var modelKeys = ManifestJsonContext
            .Default.GetType()
            .Assembly.GetTypes()
            .Where(t =>
                t.Namespace == typeof(LocaltestManifest).Namespace
                    && t.Name.StartsWith("Manifest", StringComparison.Ordinal)
                || t == typeof(LocaltestManifest)
            )
            .Where(t => !t.IsAbstract || !t.IsSealed)
            .Select(t => ManifestJsonContext.Default.Options.TryGetTypeInfo(t, out var info) ? info : null)
            .Where(info => info is not null)
            .SelectMany(info => info!.Properties.Select(p => p.Name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(schemaKeys);
        Assert.Equal([], schemaKeys.Where(key => !modelKeys.Contains(key)).ToArray());
    }

    [Fact]
    public void AnUnknownKeyIsRefused()
    {
        var json = JsonSerializer.Serialize(
            JsonDocument.Parse(ManifestLoader.ReadResource(ManifestLoader.ManifestResource)).RootElement
        );
        var withUnknown = json.Replace("\"version\":1", "\"version\":1,\"surprise\":true", StringComparison.Ordinal);

        Assert.NotEqual(json, withUnknown);
        Assert.Throws<InvalidDataException>(() =>
            ManifestLoader.Parse(System.Text.Encoding.UTF8.GetBytes(withUnknown))
        );
    }

    [Fact]
    public void AnotherVersionIsRefused()
    {
        var json = JsonSerializer
            .Serialize(JsonDocument.Parse(ManifestLoader.ReadResource(ManifestLoader.ManifestResource)).RootElement)
            .Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() =>
            ManifestLoader.Parse(System.Text.Encoding.UTF8.GetBytes(json))
        );
        Assert.Contains("version 2", error.Message, StringComparison.Ordinal);
    }

    private static void CollectPropertyNames(JsonElement element, ISet<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Name == "properties" && property.Value.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var key in property.Value.EnumerateObject())
                        {
                            names.Add(key.Name);
                        }
                    }
                    CollectPropertyNames(property.Value, names);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPropertyNames(item, names);
                }
                break;
        }
    }
}
