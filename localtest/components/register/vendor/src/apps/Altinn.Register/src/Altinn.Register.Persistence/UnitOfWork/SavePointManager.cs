using System.Diagnostics;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.Persistence.Utils;
using CommunityToolkit.Diagnostics;
using Npgsql;

namespace Altinn.Register.Persistence.UnitOfWork;

/// <summary>
/// Helper class for managing in-transaction save points.
/// </summary>
internal sealed class SavePointManager
{
    private readonly AsyncLock _lock = new();
    private readonly IUnitOfWorkHandle _handle;
    private readonly NpgsqlTransaction _transaction;
    private readonly Stack<SavePointInfo> _savePoints = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SavePointManager"/> class.
    /// </summary>
    public SavePointManager(
        NpgsqlTransaction transaction,
        IUnitOfWorkHandle handle)
    {
        _transaction = transaction;
        _handle = handle;
    }

    /// <summary>
    /// Creates a new save point.
    /// </summary>
    /// <param name="name">The save point name.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A save point in the transaction.</returns>
    public Task<ISavePoint> CreateSavePoint(string name, CancellationToken cancellationToken = default)
    {
        _handle.ThrowIfCompleted();
        cancellationToken.ThrowIfCancellationRequested();

        var info = SavePointInfo.Create(name, skipFrames: 1);

        return DoCreateSavePoint(info, cancellationToken);

        async Task<ISavePoint> DoCreateSavePoint(SavePointInfo info, CancellationToken cancellationToken)
        {
            using var handle = await _lock.Acquire(cancellationToken);

            await _transaction.SaveAsync(info.Name, cancellationToken);
            _savePoints.Push(info);

            return new SavePointHandle(this, info);
        }
    }

    private async Task ReleaseAsync(SavePointInfo info, CancellationToken cancellationToken)
    {
        _handle.ThrowIfCompleted();
        cancellationToken.ThrowIfCancellationRequested();

        // Note: lock is intentionally held until the save point is released in the db.
        using var handle = await _lock.Acquire(cancellationToken);
        if (!_savePoints.TryPeek(out var current) || !ReferenceEquals(current, info))
        {
            ThrowHelper.ThrowInvalidOperationException("Save point is not the current save point.");
        }

        _savePoints.Pop();
        await _transaction.ReleaseAsync(info.Name, cancellationToken);
    }

    private async Task RollbackAsync(SavePointInfo info, CancellationToken cancellationToken)
    {
        _handle.ThrowIfCompleted();
        cancellationToken.ThrowIfCancellationRequested();

        // Note: lock is intentionally held until the save point is rolled back in the db.
        using var handle = await _lock.Acquire(cancellationToken);
        if (!_savePoints.TryPeek(out var current) || !ReferenceEquals(current, info))
        {
            ThrowHelper.ThrowInvalidOperationException("Save point is not the current save point.");
        }

        _savePoints.Pop();
        await _transaction.RollbackAsync(info.Name, cancellationToken);
    }

    private sealed class SavePointInfo
    {
        public static SavePointInfo Create(string name, int skipFrames)
            => new(name, new StackTrace(skipFrames: skipFrames + 1));

        private SavePointInfo(string name, StackTrace startLocation)
        {
            Name = name;
            StartLocation = startLocation;
        }

        public string Name { get; }

        public StackTrace StartLocation { get; }
    }

    private sealed class SavePointHandle
        : ISavePoint
    {
        private const int STATE_LIVE = 0;
        private const int STATE_RELEASED = 1;
        private const int STATE_ROLLED_BACK = 2;
        private const int STATE_DISPOSED = -1;

        private readonly SavePointManager _mgr;
        private readonly SavePointInfo _info;

        private int _state = STATE_LIVE;

        public SavePointHandle(SavePointManager mgr, SavePointInfo info)
        {
            _mgr = mgr;
            _info = info;
        }

        public ValueTask DisposeAsync()
        {
            var state = Interlocked.Exchange(ref _state, STATE_DISPOSED);

            if (state == STATE_LIVE)
            {
                return new(_mgr.RollbackAsync(_info, CancellationToken.None));
            }

            return ValueTask.CompletedTask;
        }

        public Task ReleaseAsync(CancellationToken cancellationToken = default)
        {
            var state = Interlocked.CompareExchange(ref _state, STATE_RELEASED, STATE_LIVE);

            return state switch
            {
                STATE_LIVE => _mgr.ReleaseAsync(_info, cancellationToken),
                STATE_RELEASED => ThrowHelper.ThrowInvalidOperationException<Task>("Save point has already been released."),
                STATE_ROLLED_BACK => ThrowHelper.ThrowInvalidOperationException<Task>("Save point has already been rolled back."),

                // STATE_DISPOSED:
                _ => ThrowHelper.ThrowObjectDisposedException<Task>(nameof(SavePointHandle)),
            };
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            var state = Interlocked.CompareExchange(ref _state, STATE_ROLLED_BACK, STATE_LIVE);

            return state switch
            {
                STATE_LIVE => _mgr.RollbackAsync(_info, cancellationToken),
                STATE_RELEASED => ThrowHelper.ThrowInvalidOperationException<Task>("Save point has already been released."),
                STATE_ROLLED_BACK => ThrowHelper.ThrowInvalidOperationException<Task>("Save point has already been rolled back."),

                // STATE_DISPOSED:
                _ => ThrowHelper.ThrowObjectDisposedException<Task>(nameof(SavePointHandle)),
            };
        }
    }
}
