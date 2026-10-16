using System.Diagnostics;

namespace Altinn.Register.Core.Location;

/// <summary>
/// Represents a municipality number.
/// The default value is invalid and should not be used.
/// </summary>
[DebuggerDisplay("{_text}")]
public readonly record struct MunicipalityNumber
    : IEquatable<MunicipalityNumber>
    , IEquatable<uint>
    , IEquatable<string>
    , IFormattable
    , ISpanFormattable
{
    private readonly uint _number;
    private readonly string _text;

    /// <summary>
    /// Initializes a new instance of <see cref="MunicipalityNumber"/> with the specified municipality number.
    /// </summary>
    /// <param name="number">The number.</param>
    public MunicipalityNumber(uint number)
    {
        _number = number;
        _text = number.ToString("D4");
    }

    /// <inheritdoc/>
    public bool Equals(uint other)
        => _number == other;

    /// <inheritdoc/>
    public bool Equals(string? other)
        => _text == other;

    /// <inheritdoc/>
    public override string ToString()
        => _text ?? string.Empty;

    /// <inheritdoc/>
    string IFormattable.ToString(string? format, IFormatProvider? formatProvider)
        => _text;

    /// <inheritdoc/>
    bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (_text.TryCopyTo(destination))
        {
            charsWritten = _text.Length;
            return true;
        }

        charsWritten = 0;
        return false;
    }

    /// <summary>
    /// Defines an implicit conversion from <see cref="MunicipalityNumber"/> to <see cref="uint"/>.
    /// </summary>
    /// <param name="number">The municipality number.</param>
    public static implicit operator uint(MunicipalityNumber number)
        => number._number;

    /// <summary>
    /// Defines an implicit conversion from <see cref="MunicipalityNumber"/> to <see cref="string"/>.
    /// </summary>
    /// <param name="number">The municipality number.</param>
    public static implicit operator string(MunicipalityNumber number)
        => number._text;
}
