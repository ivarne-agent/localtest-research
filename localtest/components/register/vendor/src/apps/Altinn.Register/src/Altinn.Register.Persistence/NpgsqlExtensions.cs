using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Altinn.Authorization.ModelUtils;
using Altinn.Register.Core.Utils;
using CommunityToolkit.Diagnostics;
using Npgsql;
using NpgsqlTypes;

namespace Altinn.Register.Persistence;

/// <summary>
/// Extension methods for Npgsql.
/// </summary>
internal static class NpgsqlExtensions
{
    /// <summary>
    /// Checks if the exception is a serialization failure.
    /// </summary>
    /// <param name="ex">The <see cref="PostgresException"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="ex"/> is a serialization failure; otherwise <see langword="false"/>.</returns>
    public static bool IsSerializationFailure(this PostgresException ex)
        => string.Equals(ex.SqlState, PostgresErrorCodes.SerializationFailure, StringComparison.Ordinal);

    /// <summary>
    /// Adds a typed parameter to the collection.
    /// </summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="collection">The parameter collection.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <returns>The newly created parameter.</returns>
    public static NpgsqlParameter<T> Add<T>(this NpgsqlParameterCollection collection, string parameterName)
    {
        var parameter = new NpgsqlParameter<T>()
        {
            ParameterName = parameterName,
        };

        collection.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Adds a typed parameter to the collection.
    /// </summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="collection">The parameter collection.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="dbType">The parameter <see cref="NpgsqlDbType"/>.</param>
    /// <returns>The newly created parameter.</returns>
    public static NpgsqlParameter<T> Add<T>(this NpgsqlParameterCollection collection, string parameterName, NpgsqlDbType dbType)
    {
        var parameter = new NpgsqlParameter<T>(parameterName, dbType);

        collection.Add(parameter);
        return parameter;
    }

    /// <summary>
    /// Adds an optional parameter to the specified NpgsqlParameterCollection, including a corresponding parameter that
    /// indicates whether the value is set.
    /// </summary>
    /// <typeparam name="T">The type of the value to add as a parameter. Must be a non-nullable type.</typeparam>
    /// <param name="collection">The parameter collection.</param>
    /// <param name="isSetParameterName">The name of the parameter that will indicate whether the value is set.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="value">A <see cref="FieldValue{T}"/> containing the value to add and a flag indicating whether it is set.</param>
    /// <returns>The newly created parameter.</returns>
    public static NpgsqlParameter AddOptional<T>(this NpgsqlParameterCollection collection, string isSetParameterName, string parameterName, FieldValue<T> value)
        where T : notnull
    {
        AddIsSet(collection, isSetParameterName).Value = value.IsSet;

        return TypeHelper<T>.Instance.AddOptional(collection, parameterName, value);
    }

    /// <summary>
    /// Adds an optional parameter to the specified NpgsqlParameterCollection, including a corresponding parameter that
    /// indicates whether the value is set.
    /// </summary>
    /// <typeparam name="T">The type of the value to add as a parameter. Must be a non-nullable type.</typeparam>
    /// <param name="collection">The parameter collection.</param>
    /// <param name="isSetParameterName">The name of the parameter that will indicate whether the value is set.</param>
    /// <param name="parameterName">The parameter name.</param>
    /// <param name="dbType">The parameter <see cref="NpgsqlDbType"/>.</param>
    /// <param name="value">A <see cref="FieldValue{T}"/> containing the value to add and a flag indicating whether it is set.</param>
    /// <returns>The newly created parameter.</returns>
    public static NpgsqlParameter AddOptional<T>(this NpgsqlParameterCollection collection, string isSetParameterName, string parameterName, NpgsqlDbType dbType, FieldValue<T> value)
        where T : notnull
    {
        AddIsSet(collection, isSetParameterName).Value = value.IsSet;

        return TypeHelper<T>.Instance.AddOptional(collection, parameterName, dbType, value);
    }

    /// <summary>
    /// Adds a "is set" parameter to the collection, used for indicating whether an optional parameter is set or not.
    /// </summary>
    /// <param name="parameters">The parameter collection.</param>
    /// <param name="parameterName">The name of the "is set" parameter.</param>
    /// <returns>The newly created parameter.</returns>
    public static NpgsqlParameter<bool> AddIsSet(this NpgsqlParameterCollection parameters, string parameterName)
        => Add<bool>(parameters, parameterName, NpgsqlDbType.Boolean);

    /// <summary>
    /// Gets a field value or a default value if the field is <see cref="DBNull.Value"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The DB reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="defaultValue">Optional default value, defaults to <see langword="default"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The column value, or <paramref name="defaultValue"/> if the field is <see cref="DBNull.Value"/>.</returns>
    public static Task<T?> GetFieldValueOrDefaultAsync<T>(this NpgsqlDataReader reader, int ordinal, T? defaultValue, CancellationToken cancellationToken = default)
    {
        var isDbNullTask = reader.IsDBNullAsync(ordinal, cancellationToken);
        if (!isDbNullTask.IsCompletedSuccessfully)
        {
            return WaitDbNull(isDbNullTask, reader, ordinal, defaultValue, cancellationToken);
        }

        var isDbNull = isDbNullTask.GetAwaiter().GetResult();
        if (isDbNull)
        {
            return Task.FromResult(defaultValue);
        }

        return reader.GetFieldValueAsync<T>(ordinal, cancellationToken)!;

        async static Task<T?> WaitDbNull(Task<bool> isDbNullTask, NpgsqlDataReader reader, int ordinal, T? defaultValue, CancellationToken cancellationToken)
        {
            var isDbNull = await isDbNullTask;
            return isDbNull ? defaultValue : await reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        }
    }

    /// <summary>
    /// Gets a field value or a default value if the field is <see cref="DBNull.Value"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The DB reader.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The column value, or <see langword="default"/> if the field is <see cref="DBNull.Value"/>.</returns>
    public static Task<T?> GetFieldValueOrDefaultAsync<T>(this NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken = default)
        => GetFieldValueOrDefaultAsync<T>(reader, ordinal, defaultValue: default, cancellationToken);

    /// <summary>
    /// Gets a field value or a default value if the field is <see cref="DBNull.Value"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The DB reader.</param>
    /// <param name="name">The column name.</param>
    /// <param name="defaultValue">Optional default value, defaults to <see langword="default"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The column value, or <paramref name="defaultValue"/> if the field is <see cref="DBNull.Value"/>.</returns>
    public static Task<T?> GetFieldValueOrDefaultAsync<T>(this NpgsqlDataReader reader, string name, T? defaultValue, CancellationToken cancellationToken = default)
        => GetFieldValueOrDefaultAsync(reader, reader.GetOrdinal(name), defaultValue, cancellationToken);

    /// <summary>
    /// Gets a field value or a default value if the field is <see cref="DBNull.Value"/>.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="reader">The DB reader.</param>
    /// <param name="name">The column name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>The column value, or <see langword="default"/> if the field is <see cref="DBNull.Value"/>.</returns>
    public static Task<T?> GetFieldValueOrDefaultAsync<T>(this NpgsqlDataReader reader, string name, CancellationToken cancellationToken = default)
        => GetFieldValueOrDefaultAsync<T>(reader, reader.GetOrdinal(name), cancellationToken);

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    public static ValueTask<FieldValue<T>> GetConditionalFieldValueAsync<T>(this NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken = default)
        where T : notnull
    {
        if (ordinal == -1)
        {
            return ValueTask.FromResult(FieldValue<T>.Unset);
        }

        var isDbNullTask = reader.IsDBNullAsync(ordinal, cancellationToken);
        if (!isDbNullTask.IsCompletedSuccessfully)
        {
            return WaitDbNull(isDbNullTask, reader, ordinal, cancellationToken);
        }

        var isDbNull = isDbNullTask.GetAwaiter().GetResult();
        if (isDbNull)
        {
            return ValueTask.FromResult(FieldValue<T>.Null);
        }

        var fieldValueTask = reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
        if (!fieldValueTask.IsCompletedSuccessfully)
        {
            return AwaitFieldValue(fieldValueTask);
        }

        var fieldValue = fieldValueTask.GetAwaiter().GetResult();
        return ValueTask.FromResult((FieldValue<T>)fieldValue);

        static async ValueTask<FieldValue<T>> WaitDbNull(Task<bool> isDbNullTask, NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken)
        {
            var isDbNull = await isDbNullTask;
            if (isDbNull)
            {
                return FieldValue<T>.Null;
            }

            var fieldValue = await reader.GetFieldValueAsync<T>(ordinal, cancellationToken);
            return (FieldValue<T>)fieldValue;
        }

        static async ValueTask<FieldValue<T>> AwaitFieldValue(Task<T> fieldValueTask)
        {
            var fieldValue = await fieldValueTask;

            return (FieldValue<T>)fieldValue;
        }
    }

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="name">The column name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    public static ValueTask<FieldValue<T>> GetConditionalFieldValueAsync<T>(this NpgsqlDataReader reader, string name, CancellationToken cancellationToken = default)
        where T : notnull
        => GetConditionalFieldValueAsync<T>(reader, reader.GetOrdinal(name), cancellationToken);

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>, parsed from a string using <see cref="IParsable{TSelf}"/>.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    /// <exception cref="FormatException">The database value failed to parse as a <typeparamref name="T"/>.</exception>
    public static ValueTask<FieldValue<T>> GetConditionalParsableFieldValueAsync<T>(this NpgsqlDataReader reader, int ordinal, CancellationToken cancellationToken = default)
        where T : notnull, IParsable<T>
    {
        return GetConditionalFieldValueAsync<string>(reader, ordinal, cancellationToken)
            .Select(
                (reader, ordinal),
                static (value, state) =>
                {
                    if (!T.TryParse(value, provider: CultureInfo.InvariantCulture, out var result))
                    {
                        return ThrowParseError<T>(state.reader, state.ordinal);
                    }

                    return result;
                });
    }

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>, parsed from a string using <see cref="IParsable{TSelf}"/>.
    /// </summary>
    /// <typeparam name="T">The field value type.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="name">The column name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    /// <exception cref="FormatException">The database value failed to parse as a <typeparamref name="T"/>.</exception>
    public static ValueTask<FieldValue<T>> GetConditionalParsableFieldValueAsync<T>(this NpgsqlDataReader reader, string name, CancellationToken cancellationToken = default)
        where T : notnull, IParsable<T>
        => GetConditionalParsableFieldValueAsync<T>(reader, reader.GetOrdinal(name), cancellationToken);

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>, converted from a value using <see cref="IConverter{TSource, TResult}"/>.
    /// </summary>
    /// <typeparam name="TSource">The npgsql supported field value type.</typeparam>
    /// <typeparam name="TConverted">The domain type for the field value.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">The database value failed to convert to a <typeparamref name="TConverted"/>.</exception>
    public static ValueTask<FieldValue<TConverted>> GetConditionalConvertibleFieldValueAsync<TSource, TConverted>(this NpgsqlDataReader reader, int ordinal, IConverter<TSource, TConverted> converter, CancellationToken cancellationToken = default)
        where TSource : notnull
        where TConverted : notnull
    {
        return GetConditionalFieldValueAsync<TSource>(reader, ordinal, cancellationToken)
            .Select(
                (reader, ordinal, converter),
                static (value, state) =>
                {
                    if (!state.converter.TryConvert(value, out var result))
                    {
                        return ThrowConvertError<TConverted>(state.reader, state.ordinal);
                    }

                    return result;
                });
    }

    /// <summary>
    /// Gets a conditional field value as a <see cref="FieldValue{T}"/>, converted from a value using <see cref="IConverter{TSource, TResult}"/>.
    /// </summary>
    /// <typeparam name="TSource">The npgsql supported field value type.</typeparam>
    /// <typeparam name="TConverted">The domain type for the field value.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="name">The column name.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="FieldValue{T}"/>.</returns>
    /// <exception cref="InvalidOperationException">The database value failed to convert to a <typeparamref name="TConverted"/>.</exception>
    public static ValueTask<FieldValue<TConverted>> GetConditionalConvertibleFieldValueAsync<TSource, TConverted>(this NpgsqlDataReader reader, string name, IConverter<TSource, TConverted> converter, CancellationToken cancellationToken = default)
        where TSource : notnull
        where TConverted : notnull
        => GetConditionalConvertibleFieldValueAsync<TSource, TConverted>(reader, reader.GetOrdinal(name), converter, cancellationToken);

    /// <summary>
    /// Gets a field value as a <typeparamref name="TConverted"/>, converted from a value using <see cref="IConverter{TSource, TResult}"/>.
    /// </summary>
    /// <typeparam name="TSource">The npgsql supported field value type.</typeparam>
    /// <typeparam name="TConverted">The domain type for the field value.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <typeparamref name="TConverted"/>.</returns>
    /// <exception cref="InvalidOperationException">The database value failed to convert to a <typeparamref name="TConverted"/>.</exception>
    public static ValueTask<TConverted> GetConvertibleFieldValueAsync<TSource, TConverted>(this NpgsqlDataReader reader, int ordinal, IConverter<TSource, TConverted> converter, CancellationToken cancellationToken = default)
    {
        var valueTask = reader.GetFieldValueAsync<TSource>(ordinal, cancellationToken);
        if (!valueTask.IsCompletedSuccessfully)
        {
            return AwaitValue(valueTask, reader, ordinal, converter);
        }

        var value = valueTask.GetAwaiter().GetResult();
        if (!converter.TryConvert(value, out var result))
        {
            return ThrowConvertError<ValueTask<TConverted>>(reader, ordinal);
        }

        return ValueTask.FromResult(result);

        static async ValueTask<TConverted> AwaitValue(Task<TSource> valueTask, NpgsqlDataReader reader, int ordinal, IConverter<TSource, TConverted> converter)
        {
            var value = await valueTask;

            if (!converter.TryConvert(value, out var result))
            {
                return ThrowConvertError<TConverted>(reader, ordinal);
            }

            return result;
        }
    }

    /// <summary>
    /// Gets a field value as a <typeparamref name="TConverted"/>, converted from a value using <see cref="IConverter{TSource, TResult}"/>.
    /// </summary>
    /// <typeparam name="TSource">The npgsql supported field value type.</typeparam>
    /// <typeparam name="TConverted">The domain type for the field value.</typeparam>
    /// <param name="reader">The <see cref="NpgsqlDataReader"/>.</param>
    /// <param name="name">The column name.</param>
    /// <param name="converter">The converter.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <typeparamref name="TConverted"/>.</returns>
    /// <exception cref="InvalidOperationException">The database value failed to convert to a <typeparamref name="TConverted"/>.</exception>
    public static ValueTask<TConverted> GetConvertibleFieldValueAsync<TSource, TConverted>(this NpgsqlDataReader reader, string name, IConverter<TSource, TConverted> converter, CancellationToken cancellationToken = default)
        => GetConvertibleFieldValueAsync<TSource, TConverted>(reader, reader.GetOrdinal(name), converter, cancellationToken);

    /// <summary>
    /// Selects on a <see cref="ValueTask{T}"/> of <see cref="FieldValue{T}"/>. This is the same as calling
    /// <see cref="FieldValue.Select{TSource, TResult}(FieldValue{TSource}, Func{TSource, TResult})"/> after
    /// awaiting the value.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="source">The source field-value.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The mapped field value.</returns>
    public static ValueTask<FieldValue<TResult>> Select<TSource, TResult>(this ValueTask<FieldValue<TSource>> source, Func<TSource, TResult> selector)
        where TSource : notnull
        where TResult : notnull
    {
        if (source.IsCompletedSuccessfully)
        {
            return ValueTask.FromResult(source.GetAwaiter().GetResult().Select(selector));
        }

        return AwaitSelect(source, selector);

        static async ValueTask<FieldValue<TResult>> AwaitSelect(ValueTask<FieldValue<TSource>> source, Func<TSource, TResult> selector)
        {
            var fieldValue = await source;

            return fieldValue.Select(selector);
        }
    }

    /// <summary>
    /// Selects on a <see cref="ValueTask{T}"/> of <see cref="FieldValue{T}"/>. This is the same as calling
    /// <see cref="FieldValue.Select{TSource, TState, TResult}(FieldValue{TSource}, TState, Func{TSource, TState, TResult})"/> after
    /// awaiting the value.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TState">The state type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="source">The source field-value.</param>
    /// <param name="state">State to pass to the selector.</param>
    /// <param name="selector">The selector.</param>
    /// <returns>The mapped field value.</returns>
    public static ValueTask<FieldValue<TResult>> Select<TSource, TState, TResult>(this ValueTask<FieldValue<TSource>> source, TState state, Func<TSource, TState, TResult> selector)
        where TSource : notnull
        where TResult : notnull
    {
        if (source.IsCompletedSuccessfully)
        {
            return ValueTask.FromResult(source.GetAwaiter().GetResult().Select(state, selector));
        }

        return AwaitSelect(source, state, selector);

        static async ValueTask<FieldValue<TResult>> AwaitSelect(ValueTask<FieldValue<TSource>> source, TState state, Func<TSource, TState, TResult> selector)
        {
            var fieldValue = await source;

            return fieldValue.Select(state, selector);
        }
    }

    [DoesNotReturn]
    private static T ThrowParseError<T>(NpgsqlDataReader reader, int ordinal)
    {
        var columnName = reader.GetName(ordinal);

        if (string.IsNullOrEmpty(columnName))
        {
            columnName = $"column {ordinal}";
        }

        return ThrowHelper.ThrowFormatException<T>($"Failed to parse value of {columnName} as {typeof(T).Name}");
    }

    [DoesNotReturn]
    private static T ThrowConvertError<T>(NpgsqlDataReader reader, int ordinal)
    {
        var columnName = reader.GetName(ordinal);

        if (string.IsNullOrEmpty(columnName))
        {
            columnName = $"column {ordinal}";
        }

        return ThrowHelper.ThrowInvalidOperationException<T>($"Failed to convert value of {columnName} to {typeof(T).Name}");
    }

    private abstract class TypeHelper<T>
        where T : notnull
    {
        public static TypeHelper<T> Instance { get; }
            = (TypeHelper<T>)Activator.CreateInstance(
                typeof(T).IsValueType
                ? typeof(ValueTypeHelper<>).MakeGenericType(typeof(T))
                : typeof(ClassTypeHelper<>).MakeGenericType(typeof(T)))!;

        public abstract NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, FieldValue<T> value);

        public abstract NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, NpgsqlDbType dbType, FieldValue<T> value);
    }

    private sealed class ValueTypeHelper<T>
        : TypeHelper<T>
        where T : struct
    {
        /// <inheritdoc/>
        public override NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, FieldValue<T> value)
        {
            var parameter = collection.Add<T?>(parameterName);
            if (value.HasValue)
            {
                parameter.TypedValue = value.Value;
            }
            else
            {
                parameter.TypedValue = null;
            }

            return parameter;
        }

        /// <inheritdoc/>
        public override NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, NpgsqlDbType dbType, FieldValue<T> value)
        {
            var parameter = collection.Add<T?>(parameterName, dbType);
            if (value.HasValue)
            {
                parameter.TypedValue = value.Value;
            }
            else
            {
                parameter.TypedValue = null;
            }

            return parameter;
        }
    }

    private sealed class ClassTypeHelper<T>
        : TypeHelper<T>
        where T : class
    {
        /// <inheritdoc/>
        public override NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, FieldValue<T> value)
        {
            var parameter = collection.Add<T?>(parameterName);
            parameter.TypedValue = value.HasValue ? value.Value : null;

            return parameter;
        }

        /// <inheritdoc/>
        public override NpgsqlParameter AddOptional(NpgsqlParameterCollection collection, string parameterName, NpgsqlDbType dbType, FieldValue<T> value)
        {
            var parameter = collection.Add<T?>(parameterName, dbType);
            parameter.TypedValue = value.HasValue ? value.Value : null;

            return parameter;
        }
    }
}
