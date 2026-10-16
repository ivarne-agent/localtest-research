using System.Text.Json;

namespace Altinn.Register.Contracts.JsonConverters;

/// <summary>
/// <see cref="JsonConverter"/> for <see cref="OrganizationIdentifier"/>.
/// </summary>
public sealed class OrganizationIdentifierJsonConverter
    : JsonConverter<OrganizationIdentifier>
{
    /// <inheritdoc/>
    public override OrganizationIdentifier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (!OrganizationIdentifier.TryParse(str, null, out var result))
        {
            throw new JsonException("Invalid org. number");
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, OrganizationIdentifier value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
