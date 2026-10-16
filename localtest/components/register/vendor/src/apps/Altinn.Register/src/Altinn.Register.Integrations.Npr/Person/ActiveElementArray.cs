using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Represents the current elements selected from a historical NPR element array.
/// </summary>
/// <typeparam name="T">The historical element type.</typeparam>
[JsonConverter(typeof(ActiveElementArrayJsonConverter))]
public readonly record struct ActiveElementArray<T>
    where T : notnull, HistoricalElement
{
    private readonly ImmutableValueArray<(uint Index, T Value)> _values;

    private ActiveElementArray(ImmutableValueArray<(uint Index, T Value)> values)
    {
        _values = values;
    }

    /// <inheritdoc cref="ImmutableValueArray{T}.IsDefault"/>
    public bool IsDefault
        => _values.IsDefault;

    /// <inheritdoc cref="ImmutableValueArray{T}.IsDefaultOrEmpty"/>
    public bool IsDefaultOrEmpty
        => _values.IsDefaultOrEmpty;

    /// <inheritdoc cref="ImmutableValueArray{T}.Length"/>
    public int Length
        => _values.Length;

    /// <inheritdoc cref="ImmutableValueArray{T}.GetEnumerator"/>
    public ImmutableArray<(uint Index, T Value)>.Enumerator GetEnumerator()
        => _values.GetEnumerator();

    /// <summary>
    /// Converts an active element array to and from JSON.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverter<ActiveElementArray<T>>
    {
        /// <inheritdoc/>
        public override ActiveElementArray<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ImmutableArray<(uint Index, T Value)>.Builder builder = ImmutableArray.CreateBuilder<(uint, T)>();

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException($"Expected start of array, but got {reader.TokenType}.");
            }

            uint index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                T value = JsonSerializer.Deserialize<T>(ref reader, options)
                    ?? throw new JsonException($"Failed to deserialize element of type {typeof(T)}.");

                if (value.IsCurrent is true)
                {
                    builder.Add((index, value));
                }

                index++;
            }

            return new(builder.DrainToImmutableValueArray());
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ActiveElementArray<T> value, JsonSerializerOptions options)
        {
            ThrowHelper.ThrowNotSupportedException();
        }
    }
}

/// <summary>
/// Creates converters for <see cref="ActiveElementArray{T}"/> instances.
/// </summary>
internal sealed class ActiveElementArrayJsonConverter
    : JsonConverterFactory
{
    /// <inheritdoc/>
    public override bool CanConvert(Type typeToConvert)
        => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ActiveElementArray<>);

    /// <inheritdoc/>
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Debug.Assert(typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ActiveElementArray<>));
        Type elementType = typeToConvert.GetGenericArguments()[0];
        Type converterType = typeof(ActiveElementArray<>.JsonConverter).MakeGenericType(elementType);

        return (JsonConverter?)Activator.CreateInstance(converterType);
    }
}
