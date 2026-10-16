using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Altinn.Register.Contracts.JsonConverters;

/// <summary>
/// <see cref="JsonConverter"/> for <see cref="LangCode"/>.
/// </summary>
public sealed class LangCodeJsonConverter
    : JsonConverter<LangCode>
{
    /// <inheritdoc cref="JsonConverter{T}.Read(ref Utf8JsonReader, Type, JsonSerializerOptions)"/>
    public static LangCode? Read(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException($"Expected a string, but got {reader.TokenType}.");
        }

        if (reader.ValueIsEscaped || reader.HasValueSequence)
        {
            var str = reader.GetString();
            if (str is null)
            {
                return null;
            }

            return LangCode.FromCode(str);
        }

        return LangCode.FromCode(reader.ValueSpan);
    }

    /// <inheritdoc cref="JsonConverter{T}.ReadAsPropertyName(ref Utf8JsonReader, Type, JsonSerializerOptions)"/>
    public static LangCode ReadAsPropertyName(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is not JsonTokenType.PropertyName)
        {
            throw new JsonException($"Expected a string, but got {reader.TokenType}.");
        }

        if (reader.ValueIsEscaped || reader.HasValueSequence)
        {
            var str = reader.GetString();
            if (str is null)
            {
                throw new JsonException($"Expected a property name, but read null");
            }

            return LangCode.FromCode(str);
        }

        return LangCode.FromCode(reader.ValueSpan);
    }

    /// <inheritdoc cref="JsonConverter{T}.Write(Utf8JsonWriter, T, JsonSerializerOptions)"/>
    internal static void Write(Utf8JsonWriter writer, LangCode value)
    {
        writer.WriteStringValue(value.Utf8);
    }

    /// <inheritdoc cref="JsonConverter{T}.WriteAsPropertyName(Utf8JsonWriter, T, JsonSerializerOptions)"/>
    internal static void WriteAsPropertyName(Utf8JsonWriter writer, LangCode value)
    {
        writer.WritePropertyName(value.Utf8);
    }

    /// <inheritdoc/>
    public override LangCode? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Read(ref reader);

    /// <inheritdoc/>
    public override LangCode ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => ReadAsPropertyName(ref reader);

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, LangCode value, JsonSerializerOptions options)
        => Write(writer, value);

    /// <inheritdoc/>
    public override void WriteAsPropertyName(Utf8JsonWriter writer, [DisallowNull] LangCode value, JsonSerializerOptions options)
        => WriteAsPropertyName(writer, value);
}
