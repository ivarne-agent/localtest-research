using System.Diagnostics;
using CommunityToolkit.Diagnostics;

namespace Altinn.Authorization.ServiceDefaults.Jobs;

/// <summary>
/// A helper class for disposing resources.
/// </summary>
internal sealed class DisposeHelper
{
    private const byte STATE_ALIVE = default;
    private const byte STATE_DISPOSING = 1;
    private const byte STATE_DISPOSED = 2;

    private readonly string _objectName;
    private readonly TaskCompletionSource _disposedCompletionSource = new();

    private byte _state;

    /// <summary>
    /// Initializes a new instance of the <see cref="DisposeHelper"/> class.
    /// </summary>
    /// <param name="objectName">The name of the object that can be disposed.</param>
    public DisposeHelper(string objectName)
    {
        _objectName = objectName;
    }

    /// <summary>
    /// Gets a value indicating whether the object has been disposed.
    /// </summary>
    public bool IsDisposed
        => Volatile.Read(ref _state) != STATE_ALIVE;

    /// <summary>
    /// Ensures that the object has not been disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">Thrown if the object has been disposed.</exception>
    public void EnsureNotDisposed()
    {
        if (IsDisposed)
        {
            ThrowHelper.ThrowObjectDisposedException(_objectName);
        }
    }

    /// <summary>
    /// Handles the disposal logic of an object.
    /// </summary>
    /// <typeparam name="T">A state type. Typically the owner object.</typeparam>
    /// <param name="state">The state.</param>
    /// <param name="disposeAction">The action to run for disposal.</param>
    public ValueTask DisposeAsync<T>(T state, Func<T, ValueTask> disposeAction)
    {
        Guard.IsNotNull(disposeAction);

        var prevState = Interlocked.CompareExchange(ref _state, STATE_DISPOSING, STATE_ALIVE);
        if (prevState == STATE_DISPOSED)
        {
            // Already disposed
            return ValueTask.CompletedTask;
        }

        if (prevState == STATE_DISPOSING)
        {
            // Other task is disposing concurrently
            return new ValueTask(_disposedCompletionSource.Task);
        }

        // We are the disposing task
        Debug.Assert(prevState == STATE_ALIVE);
        return DisposeAsyncCore(this, state, disposeAction);

        static async ValueTask DisposeAsyncCore(DisposeHelper self, T state, Func<T, ValueTask> disposeAction)
        {
            try
            {
                await disposeAction(state);
            }
            catch (Exception e)
            {
                self._disposedCompletionSource.TrySetException(e);
                throw;
            }
            finally
            {
                if (self._disposedCompletionSource.TrySetResult())
                {
                    Interlocked.Exchange(ref self._state, STATE_DISPOSED);
                }
            }
        }
    }
}
