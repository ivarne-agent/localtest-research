using System.Buffers;
using System.Runtime.CompilerServices;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Provides extension methods for working with buffers.
/// </summary>
public static class BufferExtensions
{
    /// <param name="source">The source sequence.</param>
    /// <typeparam name="T">The type of the elements in the sequence.</typeparam>
    extension<T>(in ReadOnlySequence<T> source)
        where T : IEquatable<T>?
    {
        /// <summary>
        /// Returns position of first occurrence of item in the <see cref="ReadOnlySequence{T}"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SequencePosition? PositionOfAny(SearchValues<T> values)
        {
            if (source.IsSingleSegment)
            {
                int index = source.First.Span.IndexOfAny(values);
                if (index != -1)
                {
                    return source.GetPosition(index);
                }

                return null;
            }

            return source.PositionOfAnyMultiSegment(values);
        }

        private SequencePosition? PositionOfAnyMultiSegment(SearchValues<T> values)
        {
            SequencePosition position = source.Start;
            SequencePosition result = position;
            while (source.TryGet(ref position, out ReadOnlyMemory<T> memory))
            {
                int index = memory.Span.IndexOfAny(values);
                if (index != -1)
                {
                    return source.GetPosition(index, result);
                }
                else if (position.GetObject() == null)
                {
                    break;
                }

                result = position;
            }

            return null;
        }

        /// <summary>
        /// Determines whether the end of the <see cref="ReadOnlySequence{T}"/> matches any of the specified values.
        /// </summary>
        /// <param name="values">The values to check for at the end of the sequence.</param>
        /// <returns><see langword="true"/> if the end of the sequence matches any of the specified values; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EndsWithAnyOf(SearchValues<T> values)
        {
            if (source.IsSingleSegment)
            {
                return source.First.Span.EndsWithAny(values);
            }

            return source.EndsWithAnyOfMultiSegment(values);
        }

        private bool EndsWithAnyOfMultiSegment(SearchValues<T> values)
        {
            var enumerator = source.GetEnumerator();
            ReadOnlyMemory<T> lastSegment = null;
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                if (current.Length > 0)
                {
                    lastSegment = current;
                }
            }

            return lastSegment.Span.EndsWithAny(values);
        }
    }

    extension<T>(in ReadOnlySpan<T> source)
        where T : IEquatable<T>?
    {
        /// <summary>
        /// Determines whether the end of the <see cref="ReadOnlySpan{T}"/> matches any of the specified values.
        /// </summary>
        /// <param name="values">The values to check for at the end of the span.</param>
        /// <returns><see langword="true"/> if the end of the span matches any of the specified values; otherwise, <see langword="false"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool EndsWithAny(SearchValues<T> values)
        {
            if (source.Length == 0)
            {
                return false;
            }

            var last = source[^1];
            return values.Contains(last);
        }

        /// <summary>
        /// Returns a slice of the <see cref="ReadOnlySpan{T}"/> that is guaranteed to be within the bounds of the span, even if the specified start and length exceed the span's length.
        /// </summary>
        /// <param name="start">The start index of the slice.</param>
        /// <param name="length">The length of the slice.</param>
        /// <returns>A <see cref="ReadOnlySpan{T}"/> representing the slice.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> SafeSlice(int start, int length)
        {
            start = Math.Min(start, source.Length);
            length = Math.Min(length, source.Length - start);

            return source.Slice(start, length);
        }
    }
}
