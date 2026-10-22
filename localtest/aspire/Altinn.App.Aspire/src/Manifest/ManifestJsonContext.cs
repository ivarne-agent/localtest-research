using System.Text.Json.Serialization;

namespace Altinn.App.Aspire.Manifest;

/// <summary>Source-generated JSON for the manifest. Unknown keys are refused, as the Go loader refuses them.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true
)]
[JsonSerializable(typeof(IReadOnlyList<ManifestGroup>))]
[JsonSerializable(typeof(ManifestContainer))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;
