using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Extensions for working with buffers.
/// </summary>
public static class BufferExtensions
{
    /// <param name="reader">The <see cref="PipeReader"/> to read from.</param>
    extension(PipeReader reader)
    {
        /// <summary>
        /// Asynchronously copies data from the <see cref="PipeReader"/> to the specified <see cref="IBufferWriter{T}"/>.
        /// </summary>
        /// <param name="destination">The <see cref="IBufferWriter{T}"/> to write to.</param>
        /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while waiting for data.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous copy operation.</returns>
        public async Task CopyToAsync(IBufferWriter<byte> destination, CancellationToken cancellationToken = default)
        {
            Guard.IsNotNull(reader);
            Guard.IsNotNull(destination);

            while (true)
            {
                ReadResult result = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;
                SequencePosition position = buffer.Start;
                SequencePosition consumed = position;

                try
                {
                    if (result.IsCanceled)
                    {
                        ThrowHelper.ThrowOperationCanceledException("Read cancelled");
                    }

                    while (buffer.TryGet(ref position, out ReadOnlyMemory<byte> memory))
                    {
                        if (memory.IsEmpty)
                        {
                            // advance tracking only (to account for any boundary scenarios)
                            consumed = position;
                        }
                        else
                        {
                            // write and advance
                            destination.Write(memory.Span);
                            consumed = position;
                        }
                    }

                    // The while loop completed successfully, so we've consumed the entire buffer.
                    consumed = buffer.End;

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    // Advance even if WriteAsync throws so the PipeReader is not left in the
                    // currently reading state
                    reader.AdvanceTo(consumed);
                }
            }
        }
    }

    extension(Base64Url)
    {
        /// <inheritdoc cref="Base64Url.EncodeToString(ReadOnlySpan{byte})"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string EncodeToString(ReadOnlySequence<byte> source)
        {
            if (source.IsSingleSegment)
            {
                return Base64Url.EncodeToString(source.First.Span);
            }

            return EncodeToStringSlow(source);

            [MethodImpl(MethodImplOptions.NoInlining)]
            static string EncodeToStringSlow(ReadOnlySequence<byte> source)
            {
                var length = checked((int)source.Length);
                var buffer = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    source.CopyTo(buffer);
                    return Base64Url.EncodeToString(buffer.AsSpan(0, length));
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
                }
            }
        }
    }
}
