using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Parties.Records;

/// <summary>
/// Represents an aggregate of field values with historical records.
/// </summary>
/// <typeparam name="T">The value type.</typeparam>
[JsonConverter(typeof(PartyHistoricalAggregate.JsonConverter))]
public sealed record PartyHistoricalAggregate<T>
    where T : notnull
{
    /// <summary>
    /// Gets an empty aggregate with no active value and no historical values.
    /// </summary>
    public static readonly PartyHistoricalAggregate<T> Empty
        = new(hasActiveValue: false, []);

    /// <summary>
    /// Creates a new instance of the <see cref="PartyHistoricalAggregate{T}"/> class.
    /// </summary>
    /// <param name="values">The values of the aggregate.</param>
    /// <param name="hasActiveValue">Indicates whether there is an active value.</param>
    /// <returns>A new instance of <see cref="PartyHistoricalAggregate{T}"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="values"/> is empty and <paramref name="hasActiveValue"/> is <see langword="true"/>.</exception>
    public static PartyHistoricalAggregate<T> Create(IEnumerable<T> values, bool hasActiveValue)
        => Create(values.ToImmutableValueArray(), hasActiveValue);

    /// <summary>
    /// Creates a new instance of the <see cref="PartyHistoricalAggregate{T}"/> class.
    /// </summary>
    /// <param name="values">The values of the aggregate.</param>
    /// <param name="hasActiveValue">Indicates whether there is an active value.</param>
    /// <returns>A new instance of <see cref="PartyHistoricalAggregate{T}"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="values"/> is empty and <paramref name="hasActiveValue"/> is <see langword="true"/>.</exception>
    [OverloadResolutionPriority(2)]
    public static PartyHistoricalAggregate<T> Create(ReadOnlySpan<T> values, bool hasActiveValue)
        => Create(values.ToImmutableValueArray(), hasActiveValue);

    /// <summary>
    /// Creates a new instance of the <see cref="PartyHistoricalAggregate{T}"/> class.
    /// </summary>
    /// <param name="values">The values of the aggregate.</param>
    /// <param name="hasActiveValue">Indicates whether there is an active value.</param>
    /// <returns>A new instance of <see cref="PartyHistoricalAggregate{T}"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="values"/> is empty and <paramref name="hasActiveValue"/> is <see langword="true"/>.</exception>
    [OverloadResolutionPriority(1)]
    public static PartyHistoricalAggregate<T> Create(ImmutableValueArray<T> values, bool hasActiveValue)
    {
        if (hasActiveValue && values.Length == 0)
        {
            ThrowHelper.ThrowArgumentException(nameof(values), "Values cannot be empty when there is an active value.");
        }

        return new(hasActiveValue, values);
    }

    /// <summary>
    /// Creates a new instance of the <see cref="PartyHistoricalAggregate{T}"/> class with a single current active value.
    /// </summary>
    /// <param name="currentValue">The current active value.</param>
    /// <returns>A new instance of <see cref="PartyHistoricalAggregate{T}"/> with the specified current value.</returns>
    public static PartyHistoricalAggregate<T> CreateCurrent(T currentValue)
        => new(hasActiveValue: true, [currentValue]);

    private readonly bool _hasActiveValue;
    private readonly ImmutableValueArray<T> _values;

    /// <summary>
    /// Gets the current active value of the aggregate, or <see langword="null"/> if there is no active value.
    /// </summary>
    public FieldValue<T> CurrentValue
        => _hasActiveValue
            ? _values[0]
            : FieldValue.Null;

    /// <summary>
    /// Gets the values of the aggregate, including the current active value (if any).
    /// </summary>
    public ImmutableValueArray<T> Values
        => _values;

    private PartyHistoricalAggregate(bool hasActiveValue, ImmutableValueArray<T> values)
    {
        if (values.IsDefault)
        {
            ThrowHelper.ThrowArgumentException(nameof(values), "Values cannot be default.");
        }

        if (hasActiveValue && values.Length == 0)
        {
            ThrowHelper.ThrowArgumentException(nameof(values), "Values cannot be empty when there is an active value.");
        }

        _hasActiveValue = hasActiveValue;
        _values = values;
    }

    /// <summary>
    /// Json converter for <see cref="PartyHistoricalAggregate{T}"/>.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverter<PartyHistoricalAggregate<T>>
    {
        /// <inheritdoc/>
        public override PartyHistoricalAggregate<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var model = JsonSerializer.Deserialize<JsonModel>(ref reader, options);
            if (model is null)
            {
                return null;
            }

            return new PartyHistoricalAggregate<T>(model.HasActiveValue, model.Values.ToImmutableValueArray());
        }

        /// <inheritdoc/>
        public override void Write(Utf8JsonWriter writer, PartyHistoricalAggregate<T> value, JsonSerializerOptions options)
        {
            var model = new JsonModel
            {
                HasActiveValue = value._hasActiveValue,
                Values = value._values.ToImmutableArray(),
            };

            JsonSerializer.Serialize(writer, model, options);
        }

        private sealed class JsonModel
        {
            public bool HasActiveValue { get; set; }

            public ImmutableArray<T> Values { get; set; }
        }
    }
}

/// <summary>
/// Helpers for <see cref="PartyHistoricalAggregate{T}"/>.
/// </summary>
public static class PartyHistoricalAggregate
{
    /// <summary>
    /// Json converter for <see cref="PartyHistoricalAggregate{T}"/>.
    /// </summary>
    internal sealed class JsonConverter
        : JsonConverterFactory
    {
        /// <inheritdoc/>
        public override bool CanConvert(Type typeToConvert)
            => typeToConvert.IsGenericType
                && typeToConvert.GetGenericTypeDefinition() == typeof(PartyHistoricalAggregate<>);

        /// <inheritdoc/>
        public override System.Text.Json.Serialization.JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var valueType = typeToConvert.GetGenericArguments()[0];
            var converterType = typeof(PartyHistoricalAggregate<>.JsonConverter).MakeGenericType(valueType);
            return (System.Text.Json.Serialization.JsonConverter?)Activator.CreateInstance(converterType);
        }
    }

    extension<T>(PartyHistoricalAggregate<T> aggregate)
        where T : notnull
    {
        /// <summary>
        /// Gets a value indicating whether the aggregate has a current active value.
        /// </summary>
        public bool HasCurrentValue
            => aggregate.CurrentValue.HasValue;

        /// <summary>
        /// Gets a value indicating whether the aggregate has no values.
        /// </summary>
        public bool IsEmpty
            => aggregate.Values.IsEmpty;

        /// <summary>
        /// Gets a value indicating whether the aggregate has historical values.
        /// </summary>
        public bool HasHistoricalValues
            => aggregate.Values.Length > (aggregate.HasCurrentValue ? 1 : 0);
    }

    extension<T>(FieldValue<PartyHistoricalAggregate<T>> fieldValue)
        where T : notnull
    {
        /// <summary>
        /// Gets the current active value of the aggregate from the field value.
        /// </summary>
        public FieldValue<T> CurrentValue
            => fieldValue.SelectFieldValue(static aggregate => aggregate.CurrentValue);

        /// <summary>
        /// Gets the values of the aggregate from the field value.
        /// </summary>
        public FieldValue<ImmutableValueArray<T>> Values
            => fieldValue.Select(static aggregate => aggregate.Values);
    }
}
