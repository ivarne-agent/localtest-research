using System.Text.Json;

namespace Altinn.Register.Contracts.JsonConverters;

/// <summary>
/// <see cref="JsonConverter"/> for <see cref="TranslatedText"/>.
/// </summary>
public sealed class TranslatedTextJsonConverter
    : JsonConverter<TranslatedText>
{
    /// <inheritdoc/>
    public override TranslatedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected {nameof(TranslatedText)} object.");
        }

        var builder = TranslatedText.CreateBuilder();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            var langCode = LangCodeJsonConverter.ReadAsPropertyName(ref reader);

            if (!reader.Read())
            {
                throw new JsonException($"Expected value for property '{langCode}'.");
            }

            var value = reader.GetString();
            if (value is null)
            {
                throw new JsonException($"Expected value for property '{langCode}' to be a string, but got null.");
            }

            // overwrite as per normal json rules, latest key wins
            builder[langCode] = value;
        }

        if (!builder.TryToImmutable(out var result))
        {
            throw new JsonException($"Invalid {nameof(TranslatedText)} object.");
        }

        return result;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, TranslatedText value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        LangCodeJsonConverter.WriteAsPropertyName(writer, LangCode.En);
        writer.WriteStringValue(value.En);

        LangCodeJsonConverter.WriteAsPropertyName(writer, LangCode.Nb);
        writer.WriteStringValue(value.Nb);

        LangCodeJsonConverter.WriteAsPropertyName(writer, LangCode.Nn);
        writer.WriteStringValue(value.Nn);

        foreach (var kvp in value.Additional)
        {
            LangCodeJsonConverter.WriteAsPropertyName(writer, kvp.Key);
            writer.WriteStringValue(kvp.Value);
        }

        writer.WriteEndObject();
    }
}
