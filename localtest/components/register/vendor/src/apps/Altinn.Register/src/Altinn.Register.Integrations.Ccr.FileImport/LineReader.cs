using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Integrations.Ccr.FileImport;

/// <summary>
/// Provides functionality to read lines from a <see cref="PipeReader"/>.
/// </summary>
internal sealed class LineReader
    : IAsyncDisposable
{
    private static readonly SearchValues<byte> NewlineCharacters
        = SearchValues.Create([(byte)'\n', (byte)'\r']);

    private readonly PipeReader _reader;

    private uint _disposed = 0;
    private SequencePosition? _nextLineStart;
    private ReadOnlyMemory<byte> _current;
    private byte[]? _rentedBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LineReader"/> class.
    /// </summary>
    /// <param name="reader">The <see cref="PipeReader"/> to read from.</param>
    public LineReader(PipeReader reader)
    {
        _reader = reader;
    }

    /// <summary>
    /// Gets the current line read from the input.
    /// </summary>
    public ReadOnlySpan<byte> Line
        => _current.Span;

    /// <summary>
    /// Reads the next line from the input into <see cref="Line"/>.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns><see langword="true"/> if a line was read; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> ReadNext(CancellationToken cancellationToken = default)
    {
        const int MaxLineLength = 4 * 1024;

        if (_nextLineStart is { } toAdvance)
        {
            _reader.AdvanceTo(toAdvance);
            _current = default;
            _nextLineStart = default;
        }

        ReadResult readResult;
        do
        {
            readResult = await _reader.ReadAsync(cancellationToken);

            if (readResult.IsCanceled)
            {
                ThrowHelper.ThrowOperationCanceledException(cancellationToken.IsCancellationRequested ? cancellationToken : default);
            }

            var buffer = readResult.Buffer;
            var maybeEndOfLinePosition = buffer.PositionOfAny(NewlineCharacters);
            if (maybeEndOfLinePosition is { } endOfLinePosition
                && FindStartOfNextLine(in buffer, in endOfLinePosition, out var nextLineStart))
            {
                _nextLineStart = nextLineStart;

                var line = buffer.Slice(0, endOfLinePosition);
                return SetCurrent(line);
            }

            if (readResult.IsCompleted)
            {
                // we've reached the end of the input, and it did not end with a newline.
                if (buffer.Length > 0)
                {
                    var line = buffer;
                    if (line.EndsWithAnyOf(NewlineCharacters))
                    {
                        line = line.Slice(0, line.Length - 1);
                    }

                    _nextLineStart = buffer.End;
                    return SetCurrent(line);
                }
            }
            else
            {
                // there were no line-breaks in the available buffer,
                // so we need to notify the reader that we need more data
                if (buffer.Length > MaxLineLength)
                {
                    // Prevents unbounded buffer growth if a file has a line that's too long to process.
                    ThrowHelper.ThrowInvalidDataException("Line is too long to process.");
                }

                _reader.AdvanceTo(consumed: buffer.Start, examined: buffer.End);
            }
        }
        while (!readResult.IsCompleted);

        _current = default;
        return false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool FindStartOfNextLine(in ReadOnlySequence<byte> buffer, in SequencePosition endOfLinePosition, out SequencePosition nextLineStart)
        {
            SequenceReader<byte> reader = new(buffer.Slice(endOfLinePosition));
            if (reader.Length < 2)
            {
                // we need at least 2 bytes to determine if we have a \r\n sequence
                nextLineStart = default;
                return false;
            }

            if (reader.IsNext("\r\n"u8))
            {
                nextLineStart = buffer.GetPosition(2, endOfLinePosition);
                return true;
            }
            else
            {
                // we know endOfLinePosition points to either \n or \r already
                nextLineStart = buffer.GetPosition(1, endOfLinePosition);
                return true;
            }
        }
    }

    private bool SetCurrent(ReadOnlySequence<byte> line)
    {
        if (line.IsSingleSegment)
        {
            _current = line.First;
        }
        else
        {
            int lineLength = checked((int)line.Length);
            AssertRentedBuffer(ref _rentedBuffer, lineLength);
            line.CopyTo(_rentedBuffer);
            _current = _rentedBuffer.AsMemory(0, lineLength);
        }

        return true;

        static void AssertRentedBuffer(ref byte[]? rentedBuffer, int requiredLength)
        {
            if (rentedBuffer is { Length: var length } && length >= requiredLength)
            {
                return;
            }

            if (rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedBuffer, clearArray: true);
            }

            rentedBuffer = ArrayPool<byte>.Shared.Rent(requiredLength);
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
        {
            if (_rentedBuffer is not null)
            {
                ArrayPool<byte>.Shared.Return(_rentedBuffer, clearArray: true);
                _rentedBuffer = null;
            }

            await _reader.CompleteAsync();
        }
    }
}
