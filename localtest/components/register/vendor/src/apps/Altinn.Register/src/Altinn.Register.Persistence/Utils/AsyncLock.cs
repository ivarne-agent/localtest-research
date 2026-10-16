using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Persistence.Utils;

/// <summary>
/// An asynchronous lock.
/// </summary>
/// <remarks>
/// Lock is not reentrant.
/// </remarks>
internal sealed class AsyncLock()
    : AsyncConcurrencyLimiter(1)
{
}

/// <summary>
/// An asynchronous concurrency limiter.
/// </summary>
internal class AsyncConcurrencyLimiter
    : IDisposable
{
    private readonly SemaphoreSlim _semaphoreSlim;
    private readonly ConcurrentDictionary<uint, LockNotReleasedException> _active = new();
    private uint _nextId;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncConcurrencyLimiter" /> class.
    /// </summary>
    /// <param name="maxConcurrency">The maximum number of concurrent operations.</param>
    public AsyncConcurrencyLimiter(int maxConcurrency)
    {
        Guard.IsGreaterThanOrEqualTo(maxConcurrency, 1);

        _semaphoreSlim = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the resources used by the <see cref="AsyncConcurrencyLimiter" />.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> if disposing, <see langword="false"/> if called from a finalizer.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _semaphoreSlim.Dispose();

            var exceptions = _active.Values.ToList();
            switch (exceptions.Count)
            {
                case 1:
                    ExceptionDispatchInfo.Throw(exceptions[0]);
                    break;

                case > 1:
                    throw new AggregateException(exceptions);
            }
        }
    }

    /// <summary>
    /// Acquires a lock to access the resource thread-safe.
    /// </summary>
    /// <returns>An <see cref="IDisposable" /> that releases the lock on <see cref="IDisposable.Dispose" />.</returns>
    public async Task<IDisposable> Acquire(CancellationToken cancellationToken = default)
    {
        var exn = new LockNotReleasedException();
        ExceptionDispatchInfo.SetCurrentStackTrace(exn); // populate stacktrace without throwing

        var ticketId = Interlocked.Increment(ref _nextId);
        await _semaphoreSlim.WaitAsync(cancellationToken);
        _active[ticketId] = exn;

        return new Ticket(this, ticketId);
    }

    private void Release(uint ticketId)
    {
        if (!_active.TryRemove(ticketId, out _))
        {
            throw new InvalidOperationException("Invalid ticket ID.");
        }

        _semaphoreSlim.Release();
    }

    /// <summary>
    /// A lock to synchronize threads.
    /// </summary>
    private sealed class Ticket : IDisposable
    {
        private AsyncConcurrencyLimiter? _owner;
        private readonly uint _ticketId;

        /// <summary>
        /// Initializes a new instance of the <see cref="Ticket" /> class.
        /// </summary>
        /// <param name="owner">The owner of this ticket.</param>
        /// <param name="ticketId">The ID of the ticket.</param>
        public Ticket(AsyncConcurrencyLimiter owner, uint ticketId)
        {
            _owner = owner;
            _ticketId = ticketId;
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Interlocked.Exchange(ref _owner, null)?.Release(_ticketId);
        }
    }

    private sealed class LockNotReleasedException
        : InvalidOperationException
    {
        public LockNotReleasedException()
            : base("Lock not released.")
        {
        }
    }
}
