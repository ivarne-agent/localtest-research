using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using Altinn.Register.Contracts.JsonConverters;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <summary>
/// A text that is translated into multiple languages.
/// </summary>
[DebuggerDisplay("Count = {Count}")]
[DebuggerTypeProxy(typeof(TranslatedText.DebuggerProxy))]
[JsonConverter(typeof(TranslatedTextJsonConverter))]
public sealed partial class TranslatedText
    : IReadOnlyDictionary<LangCode, string>
    , IDictionary<string, string> // for writing to db
    , IEquatable<TranslatedText>
    , IEqualityOperators<TranslatedText, TranslatedText, bool>
{
    /// <summary>
    /// Creates a new <see cref="Builder"/> instance.
    /// </summary>
    /// <returns>A new <see cref="Builder"/>.</returns>
    public static Builder CreateBuilder()
        => Builder.Create();

    // the three required languages
    private readonly string _en;
    private readonly string _nb;
    private readonly string _nn;

    // any additional languages
    private readonly ImmutableArray<KeyValuePair<LangCode, string>> _additional;

    /// <summary>
    /// Gets additional translations beyond the three required ones.
    /// </summary>
    internal ImmutableArray<KeyValuePair<LangCode, string>> Additional => _additional;

    // Note: additional must be sorted
    private TranslatedText(string en, string nb, string nn, ImmutableArray<KeyValuePair<LangCode, string>> additional)
    {
        Guard.IsNotDefault(additional);
        Guard.IsNotNull(en);
        Guard.IsNotNull(nb);
        Guard.IsNotNull(nn);

        _en = en;
        _nb = nb;
        _nn = nn;
        _additional = additional;
    }

    /// <inheritdoc/>
    public int Count => 3 + _additional.Length;

    /// <summary>
    /// Gets the english translation.
    /// </summary>
    public string En => _en;

    /// <summary>
    /// Gets the norwegian bokmål translation.
    /// </summary>
    public string Nb => _nb;

    /// <summary>
    /// Gets the norwegian nynorsk translation.
    /// </summary>
    public string Nn => _nn;

    /// <inheritdoc/>
    IEnumerable<LangCode> IReadOnlyDictionary<LangCode, string>.Keys
    {
        get
        {
            yield return LangCode.En;
            yield return LangCode.Nb;
            yield return LangCode.Nn;

            foreach (var kvp in _additional)
            {
                yield return kvp.Key;
            }
        }
    }

    /// <inheritdoc/>
    IEnumerable<string> IReadOnlyDictionary<LangCode, string>.Values
    {
        get
        {
            yield return _en;
            yield return _nb;
            yield return _nn;

            foreach (var kvp in _additional)
            {
                yield return kvp.Value;
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<LangCode, string>> GetEnumerator()
    {
        yield return new KeyValuePair<LangCode, string>(LangCode.En, _en);
        yield return new KeyValuePair<LangCode, string>(LangCode.Nb, _nb);
        yield return new KeyValuePair<LangCode, string>(LangCode.Nn, _nn);

        foreach (var kvp in _additional)
        {
            yield return kvp;
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable<KeyValuePair<LangCode, string>>)this).GetEnumerator();

    /// <inheritdoc/>
    bool IReadOnlyDictionary<LangCode, string>.ContainsKey(LangCode langCode)
        => TryGetValue(langCode, out _);

    /// <inheritdoc/>
    public bool TryGetValue(LangCode key, [MaybeNullWhen(false)] out string value)
    {
        return key.Code switch
        {
            LangCode.EN_CODE => TryGetKnownValue(_en, out value),
            LangCode.NB_CODE => TryGetKnownValue(_nb, out value),
            LangCode.NN_CODE => TryGetKnownValue(_nn, out value),
            _ => TryGetAdditionalValue(_additional, key, out value),
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool TryGetKnownValue(string field, [MaybeNullWhen(false)] out string value)
        {
            value = field;
            return true;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static bool TryGetAdditionalValue(ImmutableArray<KeyValuePair<LangCode, string>> additional, LangCode key, [MaybeNullWhen(false)] out string value)
        {
            foreach (var kvp in additional)
            {
                if (kvp.Key == key)
                {
                    value = kvp.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }
    }

    /// <inheritdoc/>
    public string this[LangCode key]
    {
        get
        {
            if (!TryGetValue(key, out var value))
            {
                ThrowKeyNotFound(key);
            }

            return value;
        }
    }

    #region IDictionary<string, string>

    /// <inheritdoc/>
    string IDictionary<string, string>.this[string key]
    {
        get => this[LangCode.FromCode(key)];
        set => ThrowHelper.ThrowNotSupportedException();
    }

    /// <inheritdoc/>
    ICollection<string> IDictionary<string, string>.Keys
        => ((IEnumerable<KeyValuePair<string, string>>)this).Select(static kvp => kvp.Key).ToArray();

    /// <inheritdoc/>
    ICollection<string> IDictionary<string, string>.Values
        => ((IEnumerable<KeyValuePair<string, string>>)this).Select(static kvp => kvp.Value).ToArray();

    /// <inheritdoc/>
    bool IDictionary<string, string>.ContainsKey(string key)
        => TryGetValue(LangCode.FromCode(key), out _);

    /// <inheritdoc/>
    void IDictionary<string, string>.Add(string key, string value)
        => ThrowHelper.ThrowNotSupportedException();

    /// <inheritdoc/>
    bool IDictionary<string, string>.Remove(string key)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    bool IDictionary<string, string>.TryGetValue(string key, [MaybeNullWhen(false)] out string value)
        => TryGetValue(LangCode.FromCode(key), out value);

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<string, string>>.IsReadOnly => true;

    /// <inheritdoc/>
    void ICollection<KeyValuePair<string, string>>.Add(KeyValuePair<string, string> item)
        => ThrowHelper.ThrowNotSupportedException();

    /// <inheritdoc/>
    void ICollection<KeyValuePair<string, string>>.Clear()
        => ThrowHelper.ThrowNotSupportedException();

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<string, string>>.Contains(KeyValuePair<string, string> item)
        => TryGetValue(LangCode.FromCode(item.Key), out var value) && value == item.Value;

    /// <inheritdoc/>
    void ICollection<KeyValuePair<string, string>>.CopyTo(KeyValuePair<string, string>[] array, int arrayIndex)
    {
        Guard.IsNotNull(array);
        Guard.IsGreaterThanOrEqualTo(arrayIndex, 0);
        Guard.HasSizeGreaterThanOrEqualTo(array, arrayIndex + Count);

        foreach (var kv in (IEnumerable<KeyValuePair<string, string>>)this)
        {
            array[arrayIndex++] = kv;
        }
    }

    /// <inheritdoc/>
    bool ICollection<KeyValuePair<string, string>>.Remove(KeyValuePair<string, string> item)
        => ThrowHelper.ThrowNotSupportedException<bool>();

    /// <inheritdoc/>
    IEnumerator<KeyValuePair<string, string>> IEnumerable<KeyValuePair<string, string>>.GetEnumerator()
    {
        foreach (var kvp in this)
        {
            yield return new KeyValuePair<string, string>(kvp.Key.Code, kvp.Value);
        }
    }

    #endregion

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        HashCode hash = default;

        hash.Add(_en, StringComparer.Ordinal);
        hash.Add(_nb, StringComparer.Ordinal);
        hash.Add(_nn, StringComparer.Ordinal);

        if (!_additional.IsDefault)
        {
            hash.Add(_additional.Length);
            foreach (var item in _additional)
            {
                hash.Add(item.Key);
                hash.Add(item.Value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        return obj is TranslatedText other && Equals(other);
    }

    /// <inheritdoc/>
    public bool Equals([NotNullWhen(true)] TranslatedText? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null)
        {
            return false;
        }

        if (!string.Equals(_en, other._en, StringComparison.Ordinal)
            || !string.Equals(_nb, other._nb, StringComparison.Ordinal)
            || !string.Equals(_nn, other._nn, StringComparison.Ordinal))
        {
            return false;
        }

        if (_additional.Length != other._additional.Length)
        {
            return false;
        }

        for (int i = 0; i < _additional.Length; i++)
        {
            if (_additional[i].Key != other._additional[i].Key
                || !string.Equals(_additional[i].Value, other._additional[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public static bool operator ==(TranslatedText? left, TranslatedText? right)
        => ReferenceEquals(left, right) || (left is not null && left.Equals(right));

    /// <inheritdoc/>
    public static bool operator !=(TranslatedText? left, TranslatedText? right)
        => !(left == right);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowKeyNotFound(LangCode key)
    {
        throw new KeyNotFoundException($"The langcode '{key}' was not found in the TranslatedText.");
    }

    private sealed class DebuggerProxy(IReadOnlyDictionary<LangCode, string> dictionary)
    {
        /// <summary>
        /// The dictionary to show to the debugger.
        /// </summary>
        private readonly IReadOnlyDictionary<LangCode, string> _dictionary = dictionary;

        /// <summary>
        /// The contents of the dictionary, cached into an array.
        /// </summary>
        private Item[]? _cachedContents;

        /// <summary>
        /// Gets the contents of the dictionary for display in the debugger.
        /// </summary>
        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public Item[] Contents => _cachedContents
            ??= _dictionary.Select(static kv => new Item(kv)).ToArray();

        /// <summary>
        /// Defines a key/value pair for displaying an item of a dictionary by a debugger.
        /// </summary>
        [DebuggerDisplay("{Value}", Name = "[{Key}]")]
        public readonly struct Item(KeyValuePair<LangCode, string> kvp)
        {
            [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
            public LangCode Key { get; } = kvp.Key;

            [DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
            public string Value { get; } = kvp.Value;
        }
    }
}
