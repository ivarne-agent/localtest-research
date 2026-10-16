using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Tests.Utils;

/// <summary>
/// A <see cref="PipeReader"/> that simulates a pathological network situation where data arrives in
/// very small segments (down to a single byte) at a time.
/// </summary>
/// <remarks>
/// Changes here very easily leads to infinite looping, so be careful when modifying this class.
/// </remarks>
internal sealed class SegmentBySegmentPipeReader
    : PipeReader
{
    private ReadOnlySequence<byte> _ready;
    private ReadOnlySequence<byte> _pending;
    private bool _isReaderCompleted;
    private bool _readMore = true;

    private int _cancelNext;

    public SegmentBySegmentPipeReader(ReadOnlySequence<byte> sequence)
    {
        _pending = sequence;
        _ready = _pending.Slice(0, 0);
    }

    public override void AdvanceTo(SequencePosition consumed)
    {
        AdvanceTo(consumed, consumed);
    }

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        ThrowIfCompleted();

        if (examined.Equals(_ready.End))
        {
            _readMore = true;
        }

        _ready = _ready.Slice(consumed);
        _pending = _pending.Slice(consumed);
    }

    /// <inheritdoc />
    public override void CancelPendingRead()
    {
        Interlocked.Exchange(ref _cancelNext, 1);
    }

    /// <inheritdoc />
    public override void Complete(Exception? exception = null)
    {
        if (_isReaderCompleted)
        {
            return;
        }

        _isReaderCompleted = true;
        _pending = ReadOnlySequence<byte>.Empty;
        _ready = ReadOnlySequence<byte>.Empty;
    }

    /// <inheritdoc />
    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_readMore)
        {
            var read = false;
            var position = _ready.End;
            while (_pending.TryGet(ref position, out var memory, advance: true))
            {
                if (memory.Length > 0)
                {
                    read = true;
                    _ready = _pending.Slice(0, position);
                    break;
                }
            }

            if (!read)
            {
                _ready = _pending;
            }

            // prevents stack-overflow
            await Task.Yield();
            _readMore = false;
        }

        if (TryRead(out ReadResult result))
        {
            return result;
        }

        Debug.Assert(_pending.End.Equals(_ready.End));
        result = new ReadResult(ReadOnlySequence<byte>.Empty, isCanceled: false, isCompleted: true);
        return result;
    }

    /// <inheritdoc />
    public override bool TryRead(out ReadResult result)
    {
        ThrowIfCompleted();

        bool isCancellationRequested = Interlocked.Exchange(ref _cancelNext, 0) == 1;
        if (isCancellationRequested || (_ready.Length > 0 && !_readMore))
        {
            result = new ReadResult(_ready, isCancellationRequested, isCompleted: _pending.End.Equals(_ready.End));
            return true;
        }

        result = default;
        return false;
    }

    private void ThrowIfCompleted()
    {
        if (_isReaderCompleted)
        {
            ThrowHelper.ThrowInvalidOperationException("Reading is not allowed after reader was completed.");
        }
    }
}
