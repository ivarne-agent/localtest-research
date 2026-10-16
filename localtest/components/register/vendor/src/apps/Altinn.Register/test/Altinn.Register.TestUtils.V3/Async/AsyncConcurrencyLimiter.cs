using System.Diagnostics;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.TestUtils.Async;

internal class AsyncConcurrencyLimiter
    : IDisposable
{
    private readonly SemaphoreSlim _semaphoreSlim;

    public AsyncConcurrencyLimiter(int maxConcurrency)
    {
        Guard.IsGreaterThanOrEqualTo(maxConcurrency, 1);

        _semaphoreSlim = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    public void Dispose()
    {
        _semaphoreSlim.Dispose();
    }

    /// <summary>
    /// Acquires a lock to access the resource thread-safe.
    /// </summary>
    /// <returns>An <see cref="IDisposable" /> that releases the lock on <see cref="IDisposable.Dispose" />.</returns>
    public async Task<IDisposable> Acquire()
    {
        await _semaphoreSlim.WaitAsync();
        return new Ticket(_semaphoreSlim, new StackTrace());
    }

    /// <summary>
    /// A lock to synchronize threads.
    /// </summary>
    private sealed class Ticket : IDisposable
    {
        private readonly StackTrace _stackTrace;
        private SemaphoreSlim? _semaphoreSlim;

        /// <summary>
        /// Initializes a new instance of the <see cref="Ticket" /> class.
        /// </summary>
        /// <param name="semaphoreSlim">The semaphore slim to synchronize threads.</param>
        /// <param name="stackTrace">The stack trace where the lock was created.</param>
        public Ticket(SemaphoreSlim semaphoreSlim, StackTrace stackTrace)
        {
            _stackTrace = stackTrace;
            _semaphoreSlim = semaphoreSlim;
        }

        ~Ticket()
        {
            if (_semaphoreSlim != null)
            {
                ThrowHelper.ThrowInvalidOperationException($"Lock not released: {_stackTrace}");
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Interlocked.Exchange(ref _semaphoreSlim, null)?.Release();
        }
    }
}
