using System.Diagnostics.CodeAnalysis;
using MassTransit.Internals;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// Extension methods for <see cref="Task{TResult}"/> and <see cref="Task"/>.
/// </summary>
[ExcludeFromCodeCoverage]
internal static class TaskExtensions
{
    /// <summary>
    /// Attempts to transition the <see cref="TaskCompletionSource{TResult}"/> into the same completion state as the <paramref name="task"/>
    /// when the <paramref name="task"/> completes.
    /// </summary>
    /// <typeparam name="TResult">The return type of the <see cref="Task{TResult}"/>.</typeparam>
    /// <param name="task">The task to copy the completion state from.</param>
    /// <param name="source">The <see cref="TaskCompletionSource{TResult}"/> to copy the completion state to.</param>
    /// <returns>A new continuation <see cref="Task{TResult}"/>.</returns>
    public static Task<TResult> TryContinueWith<TResult>(this Task<TResult> task, TaskCompletionSource<TResult> source)
    {
        if (task.IsCompleted)
        {
            source.TrySetFromTask(task);
            return task;
        }
        else
        {
            return task.ContinueWith(
                static (task, sourceObj) =>
                {
                    ((TaskCompletionSource<TResult>)sourceObj!).TrySetFromTask(task);
                    return task.Result;
                },
                source,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
