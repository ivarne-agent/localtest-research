using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Contracts;

/// <content>
/// Contains the builder for <see cref="TranslatedText"/>.
/// </content>
public partial class TranslatedText
{
    /// <summary>
    /// Builder for <see cref="TranslatedText"/>.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    [DebuggerTypeProxy(typeof(TranslatedText.DebuggerProxy))]
    public sealed class Builder
        : IDictionary<LangCode, string>
        , IReadOnlyDictionary<LangCode, string>
    {
        private ImmutableArray<KeyValuePair<LangCode, string>>.Builder? _additional = null;

        private string? _en;
        private string? _nb;
        private string? _nn;

        private Builder()
        {
        }

        /// <summary>
        /// Creates a new <see cref="Builder"/>.
        /// </summary>
        /// <returns>The newly created builder.</returns>
        internal static Builder Create()
            => new Builder();

        #region IDictionary<TKey, TValue> Properties

        /// <inheritdoc/>
        public int Count
            => (_en is not null ? 1 : 0)
            + (_nb is not null ? 1 : 0)
            + (_nn is not null ? 1 : 0)
            + (_additional?.Count ?? 0);

        /// <inheritdoc/>
        bool ICollection<KeyValuePair<LangCode, string>>.IsReadOnly
            => false;

        /// <inheritdoc/>
        public IEnumerable<LangCode> Keys
        {
            get
            {
                if (_en is not null)
                {
                    yield return LangCode.En;
                }

                if (_nb is not null)
                {
                    yield return LangCode.Nb;
                }

                if (_nn is not null)
                {
                    yield return LangCode.Nn;
                }

                if (_additional is not null)
                {
                    foreach (var (lang, _) in _additional)
                    {
                        yield return lang;
                    }
                }
            }
        }

        /// <inheritdoc/>
        ICollection<LangCode> IDictionary<LangCode, string>.Keys
            => Keys.ToArray();

        /// <inheritdoc/>
        public IEnumerable<string> Values
        {
            get
            {
                if (_en is not null)
                {
                    yield return _en;
                }

                if (_nb is not null)
                {
                    yield return _nb;
                }

                if (_nn is not null)
                {
                    yield return _nn;
                }

                if (_additional is not null)
                {
                    foreach (var (_, text) in _additional)
                    {
                        yield return text;
                    }
                }
            }
        }

        /// <inheritdoc/>
        ICollection<string> IDictionary<LangCode, string>.Values
            => Values.ToArray();

        #endregion

        #region IDictionary<TKey, TValue> Members

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

            set
            {
                SetValue(key, value, overwrite: true);
            }
        }

        /// <inheritdoc/>
        public void Add(LangCode key, string value)
        {
            if (!SetValue(key, value, overwrite: false))
            {
                ThrowHelper.ThrowArgumentException("An element with the same key already exists in the dictionary.");
            }
        }

        /// <inheritdoc/>
        void ICollection<KeyValuePair<LangCode, string>>.Add(KeyValuePair<LangCode, string> item)
            => Add(item.Key, item.Value);

        /// <inheritdoc/>
        public bool ContainsKey(LangCode key)
        {
            return key.Code switch
            {
                LangCode.EN_CODE => _en is not null,
                LangCode.NB_CODE => _nb is not null,
                LangCode.NN_CODE => _nn is not null,
                _ when _additional is null => false,
                _ => _additional.Any(kvp => kvp.Key == key),
            };
        }

        /// <inheritdoc/>
        public bool Remove(LangCode key)
        {
            return key.Code switch
            {
                LangCode.EN_CODE => RemoveKnownValue(ref _en),
                LangCode.NB_CODE => RemoveKnownValue(ref _nb),
                LangCode.NN_CODE => RemoveKnownValue(ref _nn),
                _ when _additional is null => false,
                _ => RemoveAdditionalValue(ref _additional, key),
            };

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static bool RemoveKnownValue(ref string? field)
            {
                if (field is not null)
                {
                    field = null;
                    return true;
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static bool RemoveAdditionalValue(ref ImmutableArray<KeyValuePair<LangCode, string>>.Builder additional, LangCode key)
            {
                for (var i = 0; i < additional.Count; i++)
                {
                    if (additional[i].Key == key)
                    {
                        SwapRemoveAt(additional, i);
                        return true;
                    }
                }

                return false;
            }
        }

        /// <inheritdoc/>
        bool ICollection<KeyValuePair<LangCode, string>>.Remove(KeyValuePair<LangCode, string> item)
        {
            if (TryGetValue(item.Key, out var value) && value == item.Value)
            {
                var result = Remove(item.Key);
                Debug.Assert(result, "We already checked that the value existed");

                return result;
            }

            return false;
        }

        /// <inheritdoc/>
        bool ICollection<KeyValuePair<LangCode, string>>.Contains(KeyValuePair<LangCode, string> item)
        {
            return TryGetValue(item.Key, out var value) && value == item.Value;
        }

        /// <inheritdoc/>
        void ICollection<KeyValuePair<LangCode, string>>.Clear()
        {
            _en = null;
            _nb = null;
            _nn = null;
            _additional = null;
        }

        /// <inheritdoc/>
        void ICollection<KeyValuePair<LangCode, string>>.CopyTo(KeyValuePair<LangCode, string>[] array, int arrayIndex)
        {
            Guard.IsNotNull(array);
            Guard.IsGreaterThanOrEqualTo(arrayIndex, 0);
            Guard.HasSizeGreaterThanOrEqualTo(array, arrayIndex + Count);

            foreach (var kv in this)
            {
                array[arrayIndex++] = kv;
            }
        }

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
            static bool TryGetKnownValue(string? field, [MaybeNullWhen(false)] out string value)
            {
                if (field is not null)
                {
                    value = field;
                    return true;
                }

                value = null;
                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static bool TryGetAdditionalValue(ImmutableArray<KeyValuePair<LangCode, string>>.Builder? additional, LangCode key, [MaybeNullWhen(false)] out string value)
            {
                if (additional is not null)
                {
                    for (var i = 0; i < additional.Count; i++)
                    {
                        if (additional[i].Key == key)
                        {
                            value = additional[i].Value;
                            return true;
                        }
                    }
                }

                value = null;
                return false;
            }
        }

        /// <inheritdoc/>
        public IEnumerator<KeyValuePair<LangCode, string>> GetEnumerator()
        {
            if (_en is not null)
            {
                yield return new(LangCode.En, _en);
            }

            if (_nb is not null)
            {
                yield return new(LangCode.Nb, _nb);
            }

            if (_nn is not null)
            {
                yield return new(LangCode.Nn, _nn);
            }

            if (_additional is not null)
            {
                foreach (var kvp in _additional)
                {
                    yield return kvp;
                }
            }
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        #endregion

        /// <summary>
        /// Creates a new <see cref="TranslatedText"/> from the current state of the builder.
        /// </summary>
        /// <returns>The newly created and immutable <see cref="TranslatedText"/>.</returns>
        public TranslatedText ToImmutable()
        {
            if (_en is null)
            {
                ThrowHelper.ThrowInvalidOperationException($"Missing required language: {LangCode.En}");
            }

            if (_nb is null)
            {
                ThrowHelper.ThrowInvalidOperationException($"Missing required language: {LangCode.Nb}");
            }

            if (_nn is null)
            {
                ThrowHelper.ThrowInvalidOperationException($"Missing required language: {LangCode.Nn}");
            }

            ImmutableArray<KeyValuePair<LangCode, string>> additional;
            if (_additional is not null)
            {
                _additional.Sort(static (a, b) => a.Key.CompareTo(b.Key));
                additional = _additional.ToImmutable();
            }
            else
            {
                additional = [];
            }

            return new TranslatedText(_en, _nb, _nn, additional);
        }

        /// <summary>
        /// Tries to create a new <see cref="TranslatedText"/> from the current state of the builder.
        /// </summary>
        /// <param name="result">The created <see cref="TranslatedText"/> if the builder is in a valid state, otherwise <see langword="null"/>.</param>
        /// <returns><see langword="true"/> if a <see cref="TranslatedText"/> was successfully created; otherwise, <see langword="false"/>.</returns>
        public bool TryToImmutable([MaybeNullWhen(false)] out TranslatedText result)
        {
            if (_en is null || _nb is null || _nn is null)
            {
                result = null;
                return false;
            }

            result = ToImmutable();
            return true;
        }

        /// <returns>
        /// <see langword="true"/> if the value was set; otherwise <see langword="false"/>.
        /// </returns>
        private bool SetValue(LangCode key, string value, bool overwrite)
        {
            return key.Code switch
            {
                LangCode.EN_CODE => SetKnownValue(ref _en, value, overwrite),
                LangCode.NB_CODE => SetKnownValue(ref _nb, value, overwrite),
                LangCode.NN_CODE => SetKnownValue(ref _nn, value, overwrite),
                _ => SetAdditionalValue(ref _additional, key, value, overwrite),
            };

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            static bool SetKnownValue(ref string? field, string value, bool overwrite)
            {
                if (overwrite || field is null)
                {
                    field = value;
                    return true;
                }

                return false;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            static bool SetAdditionalValue(ref ImmutableArray<KeyValuePair<LangCode, string>>.Builder? additional, LangCode key, string value, bool overwrite)
            {
                additional ??= ImmutableArray.CreateBuilder<KeyValuePair<LangCode, string>>();

                for (var i = 0; i < additional.Count; i++)
                {
                    if (additional[i].Key == key)
                    {
                        if (overwrite)
                        {
                            additional[i] = new(key, value);
                            return true;
                        }

                        return false;
                    }
                }

                additional.Add(new(key, value));
                return true;
            }
        }
    }

    /// <summary>
    /// Removes the element at the specified index by swapping the last element with the element to remove and then removing the last element.
    /// </summary>
    /// <remarks>
    /// This modifies the order of the elements in the list.
    /// </remarks>
    /// <typeparam name="T">The type of the items in the list.</typeparam>
    /// <param name="list">The list to remove an item from.</param>
    /// <param name="index">The index of the item to remove.</param>
    private static void SwapRemoveAt<T>(IList<T> list, int index)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, list.Count);

        list[index] = list[^1];
        list.RemoveAt(list.Count - 1);
    }
}
