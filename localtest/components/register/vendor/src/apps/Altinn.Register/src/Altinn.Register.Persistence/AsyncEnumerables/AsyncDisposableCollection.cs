using System.Collections.Immutable;

namespace Altinn.Register.Persistence.AsyncEnumerables;

/// <summary>
/// A utility class for collecting <see cref="IAsyncDisposable"/>
/// then disposing all of them.
/// </summary>
internal class AsyncDisposableCollection
    : IAsyncResourceOwner
{
    private ImmutableQueue<IAsyncDisposable> _resources
        = ImmutableQueue<IAsyncDisposable>.Empty;

    /// <inheritdoc/>
    public void Adopt(ReadOnlySpan<IAsyncDisposable> resources)
    {
        if (resources.Length == 0)
        {
            return;
        }

        if (resources.Length == 1)
        {
            ImmutableInterlocked.Enqueue(ref _resources, resources[0]);
            return;
        }

        AdoptMany(ref _resources, resources);

        static void AdoptMany(ref ImmutableQueue<IAsyncDisposable> location, ReadOnlySpan<IAsyncDisposable> newValues)
        {
            bool successful;
            ImmutableQueue<IAsyncDisposable> oldValue = Volatile.Read(ref location);
            do
            {
                var newValue = oldValue;
                foreach (var value in newValues)
                {
                    newValue = newValue.Enqueue(value);
                }

                var interlockedResult = Interlocked.CompareExchange(ref location, newValue, oldValue);
                successful = ReferenceEquals(oldValue, interlockedResult);
                oldValue = interlockedResult; // we already have a volatile read that we can reuse for the next loop
            }
            while (!successful);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        var resources = Interlocked.Exchange(ref _resources, ImmutableQueue<IAsyncDisposable>.Empty);

        return DisposeAsync(resources);

        static ValueTask DisposeAsync(ImmutableQueue<IAsyncDisposable> resources)
        {
            while (!resources.IsEmpty)
            {
                resources = resources.Dequeue(out var resource);
                var task = resource.DisposeAsync();
                if (!task.IsCompletedSuccessfully)
                {
                    return AwaitAndDisposeAsync(task, resources);
                }
            }

            return ValueTask.CompletedTask;
        }

        static async ValueTask AwaitAndDisposeAsync(ValueTask task, ImmutableQueue<IAsyncDisposable> resources)
        {
            await task;

            while (!resources.IsEmpty)
            {
                resources = resources.Dequeue(out var resource);
                await resource.DisposeAsync();
            }
        }
    }
}
