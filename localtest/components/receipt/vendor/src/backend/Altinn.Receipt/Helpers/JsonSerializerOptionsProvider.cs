using System.Text.Json;
using System.Text.Json.Serialization;

namespace Altinn.Platform.Receipt.Helpers;

/// <summary>
/// Provider class for JsonSerializerOptions
/// </summary>
public static class JsonSerializerOptionsProvider
{
    /// <summary>
    /// Standard serializer options
    /// </summary>
    public static JsonSerializerOptions Options { get; } =
        new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,

            Converters = { new JsonStringEnumConverter() },
        };
}
