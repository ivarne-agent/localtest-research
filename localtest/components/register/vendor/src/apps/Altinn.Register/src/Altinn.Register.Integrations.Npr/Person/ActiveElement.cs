using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Integrations.Npr.Person;

/// <summary>
/// Helpers for <see cref="ActiveElement{T}"/>
/// </summary>
public static class ActiveElement
{
    /// <summary>
    /// Represents the presence of a current element in the historical array, along with its index.
    /// </summary>
    /// <param name="Value">The item value.</param>
    /// <param name="Index">The zero-based index of the item in the original historical array.</param>
    public readonly record struct Item<T>(T Value, uint Index);

    /// <summary>
    /// Represents the presence of multiple active elements in the historical array.
    /// </summary>
    /// <param name="Indices">The indices of the active elements.</param>
    public readonly record struct Multiple(ImmutableArray<uint> Indices);

    /// <summary>
    /// Creates converters for <see cref="ActiveElement{T}"/> instances.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ActiveElement<>);

        /// <inheritdoc/>
        public override System.Text.Json.Serialization.JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            Debug.Assert(typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(ActiveElement<>));
            Type elementType = typeToConvert.GetGenericArguments()[0];
            Type converterType = typeof(ActiveElement<>.JsonConverter).MakeGenericType(elementType);

            return (System.Text.Json.Serialization.JsonConverter?)Activator.CreateInstance(converterType);
        }
    }
}

/// <summary>
/// Represents the current element selected from a historical NPR element array.
/// </summary>
/// <remarks>
/// An <see cref="ActiveElement{T}"/> is *either*
/// <list type="bullet">
///   <item>Missing (no active elements were present in the array)</item>
///   <item>A single active element (including the index of that element in the original array)</item>
///   <item>A set of indices when multiple active elements were present in the original array, which is considered an error</item>
/// </list>
/// </remarks>
/// <typeparam name="T">The historical element type.</typeparam>
[JsonConverter(typeof(ActiveElement.JsonConverter))]
public readonly record struct ActiveElement<T>
    where T : notnull, HistoricalElement
{
    private readonly object? _value;
    private readonly uint _index;

    private ActiveElement(ActiveElement.Item<T> value)
    {
        _value = value.Value;
        _index = value.Index;
    }

    private ActiveElement(ActiveElement.Multiple value)
    {
        _value = value.Indices;
        _index = default;
    }

    /// <summary>
    /// The value of the active-element union. Prefer using the TryGetValue methods instead.
    /// </summary>
    /// <remarks>
    /// A value here might be an error-value. This implements the C# union contract.
    /// </remarks>
    public object? Value
    {
        get
        {
            if (TryGetValue(out ActiveElement.Item<T> item))
            {
                return item;
            }

            if (TryGetValue(out ActiveElement.Multiple multiple))
            {
                return multiple;
            }

            return null;
        }
    }

    /// <summary>
    /// Gets whether the current <see cref="ActiveElement{T}"/> has a value or not.
    /// </summary>
    /// <remarks>
    /// A value here might be an error-value. This implements the C# union contract.
    /// </remarks>
    public bool HasValue
        => _value is not null;

    /// <summary>
    /// Attempts to get the active element value, if present and valid.
    /// </summary>
    /// <param name="value">The active element value and its index in the original array, if present and valid.</param>
    /// <returns>True if a single active element was found and returned; false otherwise.</returns>
    /// <remarks>
    /// If multiple active elements were found in the original array, this method will return false and the out parameter will be default.
    /// This implements the C# union contract.
    /// </remarks>
    public bool TryGetValue(out ActiveElement.Item<T> value)
    {
        if (_value is T typedValue)
        {
            value = new(typedValue, _index);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Attempts to get the indices of multiple active elements, if present.
    /// </summary>
    /// <param name="value">The indices of the active elements, if multiple were found.</param>
    /// <returns>True if multiple active elements were found and their indices returned; false otherwise.</returns>
    /// <remarks>
    /// If exactly one active element was found in the original array, this method will return false and the out parameter will be default.
    /// This implements the C# union contract.
    /// </remarks>
    public bool TryGetValue(out ActiveElement.Multiple value)
    {
        if (_value is ImmutableArray<uint> indices)
        {
            value = new(indices);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Converts an active element to and from JSON.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverter<ActiveElement<T>>
    {
        /// <inheritdoc/>
        public override ActiveElement<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            T? result = null;
            List<uint> indices = new(1);

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
                    indices.Add(index);
                    result = value;
                }

                index++;
            }

            switch (indices.Count)
            {
                // No active elements were found in the array.
                case 0:
                    return default;

                // A single active element was found in the array.
                case 1:
                    Debug.Assert(result is not null);
                    return new(new ActiveElement.Item<T>(result, indices[0]));

                // Multiple active elements were found in the array, which is considered an error.
                default:
                    return new(new ActiveElement.Multiple([.. indices]));
            }
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, ActiveElement<T> value, JsonSerializerOptions options)
        {
            ThrowHelper.ThrowNotSupportedException();
        }
    }
}
