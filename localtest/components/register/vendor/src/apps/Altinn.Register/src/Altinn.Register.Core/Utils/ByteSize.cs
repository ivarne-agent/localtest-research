using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Utility class for working with byte sizes.
/// </summary>
[ExcludeFromCodeCoverage]
public readonly record struct ByteSize
    : IEquatable<ByteSize>
    , INumber<ByteSize>
    , IMultiplyOperators<ByteSize, ulong, ByteSize>
    , IDivisionOperators<ByteSize, ulong, ByteSize>
{
    /// <summary>
    /// Zero byte size (0 bytes).
    /// </summary>
    public static readonly ByteSize Zero = default;

    /// <summary>
    /// One byte in bytes.
    /// </summary>
    public static readonly ByteSize Byte = new(1UL);

    /// <summary>
    /// One kibibyte in bytes (1024 bytes).
    /// </summary>
    public static readonly ByteSize Kibibyte = Byte * 1024UL;

    /// <summary>
    /// One mebibyte in bytes (1024 kibibytes).
    /// </summary>
    public static readonly ByteSize Mebibyte = Kibibyte * 1024UL;

    /// <summary>
    /// One gibibyte in bytes (1024 mebibytes).
    /// </summary>
    public static readonly ByteSize Gibibyte = Mebibyte * 1024UL;

    /// <summary>
    /// One tebibyte in bytes (1024 gibibytes).
    /// </summary>
    public static readonly ByteSize Tebibyte = Gibibyte * 1024UL;

    /// <summary>
    /// Creates a new <see cref="ByteSize"/> instance from the specified value in bytes.
    /// </summary>
    /// <param name="bytes">The value in bytes.</param>
    /// <returns>A <see cref="ByteSize"/> representing the specified value in bytes.</returns>
    public static ByteSize FromBytes(ulong bytes) => new(bytes);

    /// <summary>
    /// Creates a new <see cref="ByteSize"/> instance from the specified value in kibibytes.
    /// </summary>
    /// <param name="kibibytes">The value in kibibytes.</param>
    /// <returns>A <see cref="ByteSize"/> representing the specified value in kibibytes.</returns>
    public static ByteSize FromKibibytes(ulong kibibytes) => Kibibyte * kibibytes;

    /// <summary>
    /// Creates a new <see cref="ByteSize"/> instance from the specified value in mebibytes.
    /// </summary>
    /// <param name="kibibytes">The value in mebibytes.</param>
    /// <returns>A <see cref="ByteSize"/> representing the specified value in mebibytes.</returns>
    public static ByteSize FromMebibytes(ulong kibibytes) => Mebibyte * kibibytes;

    /// <summary>
    /// Creates a new <see cref="ByteSize"/> instance from the specified value in gibibytes.
    /// </summary>
    /// <param name="gibibytes">The value in gibibytes.</param>
    /// <remarks>A <see cref="ByteSize"/> representing the specified value in gibibytes.</remarks>
    public static ByteSize FromGibibytes(ulong gibibytes) => Gibibyte * gibibytes;

    /// <summary>
    /// Creates a new <see cref="ByteSize"/> instance from the specified value in tebibytes.
    /// </summary>
    /// <param name="tebibytes">The value in tebibytes.</param>
    /// <returns>A <see cref="ByteSize"/> representing the specified value in tebibytes.</returns>
    public static ByteSize FromTebibytes(ulong tebibytes) => Tebibyte * tebibytes;

    private readonly ulong _value;

    private ByteSize(ulong value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the value of this <see cref="ByteSize"/> in bytes.
    /// </summary>
    public ulong Bytes => _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromChecked<TOther>(TOther value, out ByteSize result)
        where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = new(actualValue);
            return true;
        }
        else if (Inner(value, out ulong convertedValue))
        {
            result = new(convertedValue);
            return true;
        }
        else
        {
            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T2.TryConvertFromChecked(value, out result!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromSaturating<TOther>(TOther value, out ByteSize result)
        where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = new(actualValue);
            return true;
        }
        else if (Inner(value, out ulong convertedValue))
        {
            result = new(convertedValue);
            return true;
        }
        else
        {
            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T2.TryConvertFromSaturating(value, out result!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryConvertFromTruncating<TOther>(TOther value, out ByteSize result)
        where TOther : INumberBase<TOther>
    {
        if (typeof(TOther) == typeof(ulong))
        {
            ulong actualValue = (ulong)(object)value;
            result = new(actualValue);
            return true;
        }
        else if (Inner(value, out ulong convertedValue))
        {
            result = new(convertedValue);
            return true;
        }
        else
        {
            result = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T2.TryConvertFromTruncating(value, out result!);
    }

    #region Public operators

    /// <inheritdoc cref="IAdditionOperators{TSelf, TOther, TResult}.op_Addition(TSelf, TOther)" />
    public static ByteSize operator +(ByteSize left, ByteSize right) => new(left._value + right._value);

    /// <inheritdoc cref="ISubtractionOperators{TSelf, TOther, TResult}.op_Subtraction(TSelf, TOther)" />
    public static ByteSize operator -(ByteSize left, ByteSize right) => new(left._value - right._value);

    /// <inheritdoc cref="IMultiplyOperators{TSelf, TOther, TResult}.op_Multiply(TSelf, TOther)" />
    public static ByteSize operator *(ByteSize left, ulong right) => new(left._value * right);

    /// <inheritdoc cref="IDivisionOperators{TSelf, TOther, TResult}.op_Division(TSelf, TOther)" />
    public static ByteSize operator /(ByteSize left, ulong right) => new(left._value / right);

    /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_LessThan(TSelf, TOther)" />
    public static bool operator <(ByteSize left, ByteSize right) => left._value < right._value;

    /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_GreaterThan(TSelf, TOther)" />
    public static bool operator >(ByteSize left, ByteSize right) => left._value > right._value;

    /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_LessThanOrEqual(TSelf, TOther)" />
    public static bool operator <=(ByteSize left, ByteSize right) => left._value <= right._value;

    /// <inheritdoc cref="IComparisonOperators{TSelf, TOther, TResult}.op_GreaterThanOrEqual(TSelf, TOther)" />
    public static bool operator >=(ByteSize left, ByteSize right) => left._value >= right._value;

    #endregion

    #region INumber<TSelf> implementation

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.One => Byte;

    /// <inheritdoc/>
    static int INumberBase<ByteSize>.Radix => 2;

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.Zero => default;

    /// <inheritdoc/>
    static ByteSize IAdditiveIdentity<ByteSize, ByteSize>.AdditiveIdentity => default;

    /// <inheritdoc/>
    static ByteSize IMultiplicativeIdentity<ByteSize, ByteSize>.MultiplicativeIdentity => Byte;

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.Abs(ByteSize value) => value;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsCanonical(ByteSize value) => true;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsComplexNumber(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsEvenInteger(ByteSize value) => ulong.IsEvenInteger(value._value);

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsFinite(ByteSize value) => true;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsImaginaryNumber(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsInfinity(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsInteger(ByteSize value) => true;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsNaN(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsNegative(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsNegativeInfinity(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsNormal(ByteSize value) => value._value != 0;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsOddInteger(ByteSize value) => ulong.IsOddInteger(value._value);

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsPositive(ByteSize value) => true;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsPositiveInfinity(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsRealNumber(ByteSize value) => true;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsSubnormal(ByteSize value) => false;

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.IsZero(ByteSize value) => value._value == 0;

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.MaxMagnitude(ByteSize x, ByteSize y) => new(ulong.Max(x._value, y._value));

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.MaxMagnitudeNumber(ByteSize x, ByteSize y) => new(ulong.Max(x._value, y._value));

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.MinMagnitude(ByteSize x, ByteSize y) => new(ulong.Min(x._value, y._value));

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.MinMagnitudeNumber(ByteSize x, ByteSize y) => new(ulong.Min(x._value, y._value));

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertFromChecked<TOther>(TOther value, out ByteSize result)
        => TryConvertFromChecked(value, out result);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertFromSaturating<TOther>(TOther value, out ByteSize result)
        => TryConvertFromSaturating(value, out result);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertFromTruncating<TOther>(TOther value, out ByteSize result)
        => TryConvertFromTruncating(value, out result);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertToChecked<TOther>(ByteSize value, out TOther result)
    {
        if (typeof(TOther) == typeof(ulong))
        {
            result = (TOther)(object)value._value;
            return true;
        }
        else
        {
            return Inner(value._value, out result!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T1.TryConvertToChecked(value, out result!);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertToSaturating<TOther>(ByteSize value, out TOther result)
    {
        if (typeof(TOther) == typeof(ulong))
        {
            result = (TOther)(object)value._value;
            return true;
        }
        else
        {
            return Inner(value._value, out result!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T1.TryConvertToSaturating(value, out result!);
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static bool INumberBase<ByteSize>.TryConvertToTruncating<TOther>(ByteSize value, out TOther result)
    {
        if (typeof(TOther) == typeof(ulong))
        {
            result = (TOther)(object)value._value;
            return true;
        }
        else
        {
            return Inner(value._value, out result!);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool Inner<T1, T2>(T1 value, out T2 result)
            where T2 : INumberBase<T2>
            where T1 : INumberBase<T1>
            => T1.TryConvertToTruncating(value, out result!);
    }

    /// <inheritdoc/>
    int IComparable.CompareTo(object? obj)
    {
        if (obj == null)
        {
            return 1;
        }

        if (obj is ByteSize other)
        {
            return _value.CompareTo(other._value);
        }

        return ThrowHelper.ThrowArgumentException<int>(nameof(obj), $"Object must be of type {nameof(ByteSize)}.");
    }

    /// <inheritdoc/>
    int IComparable<ByteSize>.CompareTo(ByteSize other) => _value.CompareTo(other._value);

    /// <inheritdoc/>
    static ByteSize IUnaryPlusOperators<ByteSize, ByteSize>.operator +(ByteSize value) => value;

    /// <inheritdoc/>
    static ByteSize IAdditionOperators<ByteSize, ByteSize, ByteSize>.operator +(ByteSize left, ByteSize right) => left + right;

    /// <inheritdoc/>
    static ByteSize IUnaryNegationOperators<ByteSize, ByteSize>.operator -(ByteSize value)
    {
        return new(Inner(value._value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static T Inner<T>(T value)
            where T : INumberBase<T>
            => -value;
    }

    /// <inheritdoc/>
    static ByteSize ISubtractionOperators<ByteSize, ByteSize, ByteSize>.operator -(ByteSize left, ByteSize right) => left - right;

    /// <inheritdoc/>
    static ByteSize IIncrementOperators<ByteSize>.operator ++(ByteSize value) => value + Byte;

    /// <inheritdoc/>
    static ByteSize IDecrementOperators<ByteSize>.operator --(ByteSize value) => value - Byte;

    /// <inheritdoc/>
    static ByteSize IMultiplyOperators<ByteSize, ByteSize, ByteSize>.operator *(ByteSize left, ByteSize right) => new(left._value * right._value);

    /// <inheritdoc/>
    static ByteSize IMultiplyOperators<ByteSize, ulong, ByteSize>.operator *(ByteSize left, ulong right) => left * right;

    /// <inheritdoc/>
    static ByteSize IDivisionOperators<ByteSize, ByteSize, ByteSize>.operator /(ByteSize left, ByteSize right) => new(left._value / right._value);

    /// <inheritdoc/>
    static ByteSize IDivisionOperators<ByteSize, ulong, ByteSize>.operator /(ByteSize left, ulong right) => left / right;

    /// <inheritdoc/>
    static ByteSize IModulusOperators<ByteSize, ByteSize, ByteSize>.operator %(ByteSize left, ByteSize right) => new(left._value % right._value);

    /// <inheritdoc/>
    static bool IComparisonOperators<ByteSize, ByteSize, bool>.operator <(ByteSize left, ByteSize right) => left < right;

    /// <inheritdoc/>
    static bool IComparisonOperators<ByteSize, ByteSize, bool>.operator >(ByteSize left, ByteSize right) => left > right;

    /// <inheritdoc/>
    static bool IComparisonOperators<ByteSize, ByteSize, bool>.operator <=(ByteSize left, ByteSize right) => left <= right;

    /// <inheritdoc/>
    static bool IComparisonOperators<ByteSize, ByteSize, bool>.operator >=(ByteSize left, ByteSize right) => left >= right;

    #endregion

    #region Parsing

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.Parse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static ByteSize INumberBase<ByteSize>.Parse(string s, NumberStyles style, IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static ByteSize ISpanParsable<ByteSize>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static ByteSize IParsable<ByteSize>.Parse(string s, IFormatProvider? provider)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.TryParse(ReadOnlySpan<char> s, NumberStyles style, IFormatProvider? provider, out ByteSize result)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static bool INumberBase<ByteSize>.TryParse(string? s, NumberStyles style, IFormatProvider? provider, out ByteSize result)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static bool ISpanParsable<ByteSize>.TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ByteSize result)
    {
        throw new NotSupportedException();
    }

    /// <inheritdoc/>
    static bool IParsable<ByteSize>.TryParse(string? s, IFormatProvider? provider, out ByteSize result)
    {
        throw new NotSupportedException();
    }

    #endregion

    #region Formatting

    /// <inheritdoc/>
    public override string ToString()
        => ToString(null, CultureInfo.CurrentCulture);

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        // TODO: Care about format?
        string unit = "B";
        double value = _value;

        if (this >= Tebibyte)
        {
            unit = "TiB";
            value /= Tebibyte._value;
        }
        else if (this >= Gibibyte)
        {
            unit = "GiB";
            value /= Gibibyte._value;
        }
        else if (this >= Mebibyte)
        {
            unit = "MiB";
            value /= Mebibyte._value;
        }
        else if (this >= Kibibyte)
        {
            unit = "KiB";
            value /= Kibibyte._value;
        }

        return string.Create(formatProvider, $"{value:0.##} {unit}");
    }

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        // TODO: Care about format?
        string unit = "B";
        double value = _value;

        if (this >= Tebibyte)
        {
            unit = "TiB";
            value /= Tebibyte._value;
        }
        else if (this >= Gibibyte)
        {
            unit = "GiB";
            value /= Gibibyte._value;
        }
        else if (this >= Mebibyte)
        {
            unit = "MiB";
            value /= Mebibyte._value;
        }
        else if (this >= Kibibyte)
        {
            unit = "KiB";
            value /= Kibibyte._value;
        }

        if (!value.TryFormat(destination, out charsWritten, "0.##", provider))
        {
            return false;
        }

        if (charsWritten + unit.Length + 1 > destination.Length)
        {
            charsWritten = 0;
            return false;
        }

        destination[charsWritten++] = ' ';
        unit.AsSpan().CopyTo(destination.Slice(charsWritten));
        charsWritten += unit.Length;
        return true;
    }

    #endregion
}
