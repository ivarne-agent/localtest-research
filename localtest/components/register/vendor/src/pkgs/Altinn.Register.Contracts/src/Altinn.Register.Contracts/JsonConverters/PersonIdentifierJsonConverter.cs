using System.Text.Json;

namespace Altinn.Register.Contracts.JsonConverters;

/// <summary>
/// <see cref="JsonConverter"/> for <see cref="PersonIdentifier"/>.
/// </summary>
public sealed class PersonIdentifierJsonConverter
    : JsonConverter<PersonIdentifier>
{
    /// <inheritdoc/>
    public override PersonIdentifier? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (!PersonIdentifier.TryParse(str, null, out var result))
        {
            throw new JsonException("Invalid SSN");
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PersonIdentifier value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}
