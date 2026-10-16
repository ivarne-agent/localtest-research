namespace Altinn.Register.Model.Extensions;

/// <summary>
/// Extension methods for <see cref="Span{T}"/>.
/// </summary>
public static class SpanExtensions
{
    /// <summary>
    /// Splits a read-only span of characters into substrings based on a specified separator character.
    /// </summary>
    /// <param name="span">The read-only span of characters to split.</param>
    /// <param name="separator">The character used to separate the span into substrings.</param>
    /// <param name="options">Optional string split options that determine whether to trim entries and/or remove empty entries.</param>
    /// <returns>An enumerable that iterates over the substrings of the span.</returns>
    public static StringSplitEnumerable Split(this ReadOnlySpan<char> span, char separator, StringSplitOptions options = StringSplitOptions.None) => new(span, separator, options);

    /// <summary>
    /// Provides an enumerable that iterates over segments of a read-only span of characters, split by a specified separator.
    /// </summary>
    public readonly ref struct StringSplitEnumerable
    {
        private readonly ReadOnlySpan<char> _span;
        private readonly StringSplitOptions _options;
        private readonly char _separator;

        /// <summary>
        /// Constructs a new instance of <see cref="StringSplitEnumerable"/>.
        /// </summary>
        /// <param name="span">The span to split.</param>
        /// <param name="separator">The separator.</param>
        /// <param name="options">The split options.</param>
        public StringSplitEnumerable(ReadOnlySpan<char> span, char separator, StringSplitOptions options)
        {
            _span = span;
            _separator = separator;
            _options = options;
        }

        /// <inheritdoc cref="IEnumerable{T}.GetEnumerator()"/>
        public Enumerator GetEnumerator() => new(_span, _separator, _options);

        /// <summary>
        /// Provides an enumerator that iterates over segments of a read-only span of characters, split by a specified separator.
        /// </summary>
        public ref struct Enumerator
        {
            // Note: this bool is intentionally negated so that default(Enumerator) produces no elements.
            private bool _notEnded;
            private ReadOnlySpan<char> _current;
            private ReadOnlySpan<char> _rest;

            private readonly char _separator;
            private readonly StringSplitOptions _options;

            /// <summary>
            /// Constructs a new instance of <see cref="Enumerator"/>.
            /// </summary>
            /// <param name="span">The span to split.</param>
            /// <param name="separator">The separator.</param>
            /// <param name="options">The split options.</param>
            public Enumerator(ReadOnlySpan<char> span, char separator, StringSplitOptions options)
            {
                _rest = span;
                _separator = separator;
                _options = options;
                _notEnded = true;
                _current = default;
            }

            /// <summary>
            /// Advances the enumerator to the next element of the span split by the separator.
            /// </summary>
            /// <returns>
            /// <c>true</c> if the enumerator was successfully advanced to the next element;
            /// <c>false</c> if the enumerator has passed the end of the span.
            /// </returns>
            public bool MoveNext()
            {
                while (_notEnded)
                {
                    var index = _rest.IndexOf(_separator);
                    if (index < 0)
                    {
                        _current = _rest;
                        _rest = default;
                        _notEnded = false;
                    }
                    else
                    {
                        _current = _rest[..index];
                        _rest = _rest[(index + 1)..];
                    }

                    if (_options.HasFlag(StringSplitOptions.TrimEntries))
                    {
                        _current = _current.Trim();
                    }

                    if (_options.HasFlag(StringSplitOptions.RemoveEmptyEntries) && _current.IsEmpty)
                    {
                        continue;
                    }

                    return true;
                }

                _current = default;
                return false;
            }

            /// <inheritdoc cref="IEnumerator{T}.Current"/>
            public readonly ReadOnlySpan<char> Current => _current;
        }
    }
}
