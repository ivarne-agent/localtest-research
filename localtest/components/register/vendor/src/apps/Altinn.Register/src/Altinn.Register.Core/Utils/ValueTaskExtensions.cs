namespace Altinn.Register.Core.Utils;

/// <summary>
/// Extension methods for <see cref="ValueTask"/> and <see cref="ValueTask{T}"/>.
/// </summary>
public static class ValueTaskExtensions
{
    /// <summary>
    /// Gets a <see cref="ValueTask"/> that will complete when this <see cref="ValueTask"/> completes or when the specified timeout expires.
    /// </summary>
    /// <param name="task">The <see cref="ValueTask"/> to wait for.</param>
    /// <param name="timeout">The timeout after which the <see cref="ValueTask"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/>.</param>
    /// <returns>The <see cref="ValueTask"/> representing the asynchronous wait. It may or may not be the same instance as the current instance.</returns>
    public static ValueTask WaitAsync(this ValueTask task, TimeSpan timeout, TimeProvider timeProvider)
    {
        if (task.IsCompleted)
        {
            return task;
        }

        return new(task.AsTask().WaitAsync(timeout, timeProvider));
    }

    /// <summary>
    /// Gets a <see cref="ValueTask{T}"/> that will complete when this <see cref="ValueTask{T}"/> completes or when the specified timeout expires.
    /// </summary>
    /// <param name="task">The <see cref="ValueTask{T}"/> to wait for.</param>
    /// <param name="timeout">The timeout after which the <see cref="ValueTask{T}"/> should be faulted with a <see cref="TimeoutException"/> if it hasn't otherwise completed.</param>
    /// <param name="timeProvider">The <see cref="TimeProvider"/>.</param>
    /// <returns>The <see cref="ValueTask"/> representing the asynchronous wait. It may or may not be the same instance as the current instance.</returns>
    public static ValueTask<T> WaitAsync<T>(this ValueTask<T> task, TimeSpan timeout, TimeProvider timeProvider)
    {
        if (task.IsCompleted)
        {
            return task;
        }

        return new(task.AsTask().WaitAsync(timeout, timeProvider));
    }
}
