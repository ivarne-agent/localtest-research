using System.Text.Json.Serialization;

namespace Altinn.Studio.Localtest.Provisioning;

/// <summary>The app's <c>api/v1/applicationlanguages</c> answer, one entry per text resource.</summary>
internal sealed record ApplicationLanguage(string Language);

/// <summary>Source-generated, so the library stays trimming- and AOT-safe.</summary>
[JsonSourceGenerationOptions(System.Text.Json.JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(List<ApplicationLanguage>))]
internal sealed partial class ProvisioningJsonContext : JsonSerializerContext;
