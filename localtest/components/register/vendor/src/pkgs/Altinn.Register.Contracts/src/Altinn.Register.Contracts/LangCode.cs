using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Altinn.Register.Contracts.JsonConverters;
using Altinn.Swashbuckle.Filters;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// Represents a language code.
/// </summary>
[DebuggerDisplay("{Code}")]
[JsonConverter(typeof(LangCodeJsonConverter))]
[SwaggerString]
public sealed class LangCode
    : IEquatable<LangCode>
    , IComparable<LangCode>
    , IEqualityOperators<LangCode, LangCode, bool>
    , IComparisonOperators<LangCode, LangCode, bool>
{
    /// <summary>
    /// The language code for English.
    /// </summary>
    internal const string EN_CODE = "en";

    /// <summary>
    /// The language code for Norwegian Bokmål.
    /// </summary>
    internal const string NB_CODE = "nb";

    /// <summary>
    /// The language code for Norwegian Nynorsk.
    /// </summary>
    internal const string NN_CODE = "nn";

    /// <summary>
    /// Gets the language code for English.
    /// </summary>
    public static readonly LangCode En = new LangCode(EN_CODE, "en"u8);

    /// <summary>
    /// Gets the language code for Norwegian Bokmål.
    /// </summary>
    public static readonly LangCode Nb = new LangCode(NB_CODE, "nb"u8);

    /// <summary>
    /// Gets the language code for Norwegian Nynorsk.
    /// </summary>
    public static readonly LangCode Nn = new LangCode(NN_CODE, "nn"u8);

    private static readonly ConcurrentDictionary<string, LangCode> _codes
        = new ConcurrentDictionary<string, LangCode>(
            comparer: StringComparer.OrdinalIgnoreCase,
            collection: [
                new(En.Code, En),
                new(Nb.Code, Nb),
                new(Nn.Code, Nn),
            ]);

    /// <summary>
    /// Gets a language code from a string.
    /// </summary>
    /// <param name="code">The language code as a string.</param>
    /// <returns>A <see cref="LangCode"/>.</returns>
    public static LangCode FromCode(string code)
    {
        Guard.IsNotNull(code);

        return code switch
        {
            // normal cases
            "en" => En,
            "nb" => Nb,
            "nn" => Nn,

            // rest
            _ => Normalized(code),
        };

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LangCode Normalized(string code)
        {
            code = code.ToLowerInvariant().Normalize();

            Guard.IsNotEmpty(code);
            Guard.HasSizeEqualTo(code, 2);

            return code switch
            {
                // normal cases
                "en" => En,
                "nb" => Nb,
                "nn" => Nn,

                // rest
                _ => GetOrCreateCached(code),
            };
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static LangCode GetOrCreateCached(string code)
            => _codes.GetOrAdd(code, static c => new(c, Encoding.UTF8.GetBytes(c)));
    }

    /// <summary>
    /// Gets a language code from a UTF-8 encoded string.
    /// </summary>
    /// <param name="utf8Bytes">The UTF-8 bytes.</param>
    /// <returns>A <see cref="LangCode"/>.</returns>
    public static LangCode FromCode(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.Length == 2)
        {
            if (utf8Bytes.SequenceEqual("en"u8))
            {
                return En;
            }

            if (utf8Bytes.SequenceEqual("nb"u8))
            {
                return Nb;
            }

            if (utf8Bytes.SequenceEqual("nn"u8))
            {
                return Nn;
            }
        }

        var str = Encoding.UTF8.GetString(utf8Bytes);
        return FromCode(str);
    }

    private readonly string _code;
    private readonly ImmutableArray<byte> _utf8;

    /// <summary>
    /// Gets the language code as a string.
    /// </summary>
    public string Code => _code;

    /// <summary>
    /// Gets the language code as UTF-8 bytes.
    /// </summary>
    internal ReadOnlySpan<byte> Utf8 => _utf8.AsSpan();

    private LangCode(string code, ReadOnlySpan<byte> utf8)
    {
        Guard.IsNotNull(code);

        _code = code;
        _utf8 = [.. utf8];
    }

    /// <inheritdoc/>
    public override string ToString()
        => _code;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // this does not need to be OrdinalIgnoreCase, as we normalize the code before calling the constructor
        return string.GetHashCode(_code, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        // no two instances of LangCode should have the same code, the global dictionary ensures this
        return ReferenceEquals(this, obj);
    }

    /// <inheritdoc/>
    public bool Equals(LangCode? other)
    {
        // no two instances of LangCode should have the same code, the global dictionary ensures this
        return ReferenceEquals(this, other);
    }

    /// <inheritdoc/>
    public int CompareTo(LangCode? other)
    {
        return ReferenceEquals(this, other) ? 0
            : string.Compare(_code, other?._code, StringComparison.Ordinal);
    }

    /// <inheritdoc/>
    public static bool operator ==(LangCode? left, LangCode? right)
    {
        // no two instances of LangCode should have the same code, the global dictionary ensures this
        return ReferenceEquals(left, right);
    }

    /// <inheritdoc/>
    public static bool operator !=(LangCode? left, LangCode? right)
    {
        // no two instances of LangCode should have the same code, the global dictionary ensures this
        return !ReferenceEquals(left, right);
    }

    /// <inheritdoc/>
    public static bool operator >(LangCode left, LangCode right)
    {
        return left.CompareTo(right) > 0;
    }

    /// <inheritdoc/>
    public static bool operator >=(LangCode left, LangCode right)
    {
        return left.CompareTo(right) >= 0;
    }

    /// <inheritdoc/>
    public static bool operator <(LangCode left, LangCode right)
    {
        return left.CompareTo(right) < 0;
    }

    /// <inheritdoc/>
    public static bool operator <=(LangCode left, LangCode right)
    {
        return left.CompareTo(right) <= 0;
    }
}
