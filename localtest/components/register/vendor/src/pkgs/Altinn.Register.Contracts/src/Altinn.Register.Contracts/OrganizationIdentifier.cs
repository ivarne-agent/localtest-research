using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Altinn.Register.Contracts.JsonConverters;
using Altinn.Swashbuckle.Examples;
using Altinn.Swashbuckle.Filters;

namespace Altinn.Register.Contracts;

/// <summary>
/// A organization number (a string of 9 digits).
/// </summary>
[SwaggerString(Format = "org-no", Pattern = "^[0-9]{9}$")]
[JsonConverter(typeof(OrganizationIdentifierJsonConverter))]
public sealed class OrganizationIdentifier
    : IParsable<OrganizationIdentifier>
    , ISpanParsable<OrganizationIdentifier>
    , IFormattable
    , ISpanFormattable
    , IExampleDataProvider<OrganizationIdentifier>
    , IEquatable<OrganizationIdentifier>
    , IEquatable<string>
    , IEqualityOperators<OrganizationIdentifier, OrganizationIdentifier, bool>
    , IEqualityOperators<OrganizationIdentifier, string, bool>
{
    private const int LENGTH = 9;
    private static readonly SearchValues<char> NUMBERS = SearchValues.Create('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

    private readonly string _value;

    private OrganizationIdentifier(string value)
    {
        _value = value;
    }

    /// <inheritdoc/>
    public static IEnumerable<OrganizationIdentifier>? GetExamples(ExampleDataOptions options)
    {
        yield return Parse("123456785");
        yield return Parse("987654325");
    }

    /// <inheritdoc cref="IParsable{TSelf}.Parse(string, IFormatProvider?)"/>
    public static OrganizationIdentifier Parse(string s)
        => Parse(s, provider: null);

    /// <inheritdoc/>
    public static OrganizationIdentifier Parse(string s, IFormatProvider? provider)
        => TryParse(s, provider, out var result)
        ? result
        : throw new FormatException("Invalid organization number");

    /// <inheritdoc cref="ISpanParsable{TSelf}.Parse(ReadOnlySpan{char}, IFormatProvider?)"/>
    public static OrganizationIdentifier Parse(ReadOnlySpan<char> s)
        => Parse(s, provider: null);

    /// <inheritdoc/>
    public static OrganizationIdentifier Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
        => TryParse(s, provider, out var result)
        ? result
        : throw new FormatException("Invalid organization number");

    /// <inheritdoc/>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out OrganizationIdentifier result)
        => TryParse(s.AsSpan(), s, out result);

    /// <inheritdoc/>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out OrganizationIdentifier result)
        => TryParse(s, original: null, out result);

    private static bool TryParse(ReadOnlySpan<char> s, string? original, [MaybeNullWhen(false)] out OrganizationIdentifier result)
    {
        if (s.Length != LENGTH)
        {
            result = null;
            return false;
        }

        if (s.ContainsAnyExcept(NUMBERS))
        {
            result = null;
            return false;
        }

        if (!IsValidOrganizationIdentifier(s))
        {
            result = null;
            return false;
        }

        result = new OrganizationIdentifier(original ?? new string(s));
        return true;

        static bool IsValidOrganizationIdentifier(ReadOnlySpan<char> s)
        {
            ReadOnlySpan<ushort> chars = MemoryMarshal.Cast<char, ushort>(s);
            Vector128<ushort> weights = Vector128.Create((ushort)3, 2, 7, 6, 5, 4, 3, 2);

            Vector128<ushort> zeroDigit = Vector128.Create('0', '0', '0', '0', '0', '0', '0', '0');
            Vector128<ushort> charsVec = Vector128.Create(chars);

            var sum = Vector128.Sum((charsVec - zeroDigit) * weights);

            var ctrlDigit = (11 - (sum % 11)) % 11;

            var currentDigit = chars[8] - '0';
            return currentDigit == ctrlDigit;
        }
    }

    /// <inheritdoc/>
    public override string ToString()
        => _value;

    /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
    public string ToString(string? format)
        => _value;

    /// <inheritdoc/>
    public string ToString(string? format, IFormatProvider? formatProvider)
        => _value;

    /// <inheritdoc/>
    public bool Equals(OrganizationIdentifier? other)
        => ReferenceEquals(this, other) || (other is not null && _value == other._value);

    /// <inheritdoc/>
    public bool Equals(string? other)
        => other is not null && _value == other;

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj switch
        {
            OrganizationIdentifier other => Equals(other),
            string other => Equals(other),
            _ => false,
        };

    /// <inheritdoc/>
    public override int GetHashCode()
        => _value.GetHashCode(StringComparison.Ordinal);

    /// <inheritdoc/>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (destination.Length < _value.Length)
        {
            charsWritten = 0;
            return false;
        }

        _value.AsSpan().CopyTo(destination);
        charsWritten = _value.Length;
        return true;
    }

    /// <inheritdoc/>
    public static bool operator ==(OrganizationIdentifier? left, OrganizationIdentifier? right)
        => ReferenceEquals(left, right) || (left?.Equals(right) ?? right is null);

    /// <inheritdoc/>
    public static bool operator !=(OrganizationIdentifier? left, OrganizationIdentifier? right)
        => !(left == right);

    /// <inheritdoc/>
    public static bool operator ==(OrganizationIdentifier? left, string? right)
        => left?.Equals(right) ?? right is null;

    /// <inheritdoc/>
    public static bool operator !=(OrganizationIdentifier? left, string? right)
        => !(left == right);

    /// <inheritdoc cref="IEqualityOperators{TSelf, TOther, TResult}.op_Equality"/>
    public static bool operator ==(string? left, OrganizationIdentifier? right)
        => right?.Equals(left) ?? left is null;

    /// <inheritdoc cref="IEqualityOperators{TSelf, TOther, TResult}.op_Inequality"/>
    public static bool operator !=(string? left, OrganizationIdentifier? right)
        => !(left == right);
}
