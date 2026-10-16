using Altinn.Register.Core.Utils;
using Altinn.Register.Tests.Utils;

namespace Altinn.Register.Tests.UnitTests;

public class AsyncEnumerableExtensionsTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void Merge_Throws_IfNullSource()
    {
        IAsyncEnumerable<string> enumerable = null!;

        Assert.Throws<ArgumentNullException>(() => enumerable.Merge(AsyncEnumerable.Empty<string>()));
    }

    [Fact]
    public void Merge_Throws_IfNullSources()
    {
        IAsyncEnumerable<string> enumerable = new AsyncList<string>();

        Assert.Throws<ArgumentNullException>(() => enumerable.Merge([null!]));
    }

    [Fact]
    public async Task Merge_Merges_Sources_Fairly()
    {
        IAsyncEnumerable<string> enumerable = new AsyncList<string>(yieldBeforeItems: false) { "a", "b", "c" };

        var result = await enumerable
            .Merge(new AsyncList<string>(yieldBeforeItems: false) { "d", "e", "f" })
            .Merge([new AsyncList<string>(yieldBeforeItems: false) { "g", "h", "i" }, new AsyncList<string>(yieldBeforeItems: false) { "j", "k", "l" }])
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(12);
        result[0].ShouldBe("a");
        result[1].ShouldBe("d");
        result[2].ShouldBe("g");
        result[3].ShouldBe("j");
        result[4].ShouldBe("b");
        result[5].ShouldBe("e");
        result[6].ShouldBe("h");
        result[7].ShouldBe("k");
        result[8].ShouldBe("c");
        result[9].ShouldBe("f");
        result[10].ShouldBe("i");
        result[11].ShouldBe("l");
    }

    [Fact]
    public async Task Merge_IsFair_WithSyncAsync_Combination()
    {
        var infiniteSequence = Enumerable.Range(0, int.MaxValue).ToAsyncEnumerable();
        var yielding = new AsyncList<int>(yieldBeforeItems: true) { -1 };

        var merged = infiniteSequence.Merge(yielding);
        await foreach (var item in merged)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), CancellationToken);

            if (item == -1)
            {
                // success
                break;
            }

            if (item > 10)
            {
                throw new InvalidOperationException("Should have yielded -1 before 10");
            }
        }
    }

    [Fact]
    public async Task Merge_Merges_AsyncSources()
    {
        IAsyncEnumerable<string> enumerable = new AsyncList<string>(yieldBeforeItems: true) { "a", "b", "c" };

        var result = await enumerable
            .Merge(new AsyncList<string>(yieldBeforeItems: true) { "d", "e", "f" })
            .Merge([new AsyncList<string>(yieldBeforeItems: true) { "g", "h", "i" }, new AsyncList<string>(yieldBeforeItems: true) { "j", "k", "l" }])
            .ToListAsync(CancellationToken);

        result.Count.ShouldBe(12);
    }

    [Fact]
    public async Task Merge_Propagates_Cancellation()
    {
        using var cts = new CancellationTokenSource();

        var infiniteSequence = Enumerable.Range(0, int.MaxValue).ToAsyncEnumerable().Yielding(CancellationToken);
        var cancelableSequence = new CancellableEnumerable<int>(cts.Token);
        var merged = infiniteSequence.Merge(cancelableSequence);

        await using var enumerator = merged.GetAsyncEnumerator(CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(0, enumerator.Current);
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current);

        using var resetEvent = new ManualResetEvent(false);
        cts.Token.Register(() => resetEvent.Set());

        cts.Cancel();
        var ex = await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            resetEvent.WaitOne();

            await enumerator.MoveNextAsync();
            throw new InvalidOperationException("Should have cancelled");
        });

        Assert.Equal(cts.Token, ex.CancellationToken);
    }

    [Fact]
    public async Task Merge_Empty()
    {
        var seq = AsyncEnumerableExtensions.Merge<string>([]);
        var result = await seq.ToListAsync(CancellationToken);
        Assert.Empty(result);
    }

    [Fact]
    public async Task Merge_Single()
    {
        var seq = AsyncEnumerableExtensions.Merge([AsyncEnumerable.Range(0, 10)]);
        var result = await seq.ToListAsync(CancellationToken);
        Assert.Equal(10, result.Count);
    }

    [Fact]
    public async Task Merge_Remains_Completed_After_Terminal_False()
    {
        var enumerator = AsyncEnumerableExtensions.Merge([AsyncEnumerable.Empty<int>(), AsyncEnumerable.Empty<int>()])
            .GetAsyncEnumerator(CancellationToken);

        (await enumerator.MoveNextAsync()).ShouldBeFalse();
        (await enumerator.MoveNextAsync()).ShouldBeFalse();

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task Merge_Does_Not_Create_Source_Enumerators_Until_Polled()
    {
        var first = new TrackingEnumerable<int>();
        var second = new TrackingEnumerable<int>();

        var enumerator = first.Merge(second).GetAsyncEnumerator(CancellationToken);

        first.GetAsyncEnumeratorCalls.ShouldBe(0);
        second.GetAsyncEnumeratorCalls.ShouldBe(0);

        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task Merge_Disposes_Already_Created_Sources_When_Later_Source_Creation_Fails()
    {
        var first = new TrackingEnumerable<int>();
        var second = new ThrowingGetAsyncEnumeratorEnumerable<int>(new InvalidOperationException("boom"));
        var enumerator = first.Merge(second).GetAsyncEnumerator(CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await enumerator.MoveNextAsync());

        ex.Message.ShouldBe("boom");
        first.GetAsyncEnumeratorCalls.ShouldBe(1);
        first.DisposeCalls.ShouldBe(1);
        second.GetAsyncEnumeratorCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Merge_DisposeAsync_Attempts_All_Sources_And_Aggregates_Exceptions()
    {
        var first = new ThrowingDisposeEnumerable<int>(new InvalidOperationException("first"));
        var second = new ThrowingDisposeEnumerable<int>(new InvalidOperationException("second"));

        var enumerator = first.Merge(second).GetAsyncEnumerator(CancellationToken);
        (await enumerator.MoveNextAsync()).ShouldBeFalse();

        var ex = await Assert.ThrowsAsync<AggregateException>(async () => await enumerator.DisposeAsync());

        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
        ex.InnerExceptions.Count.ShouldBe(2);
        ex.InnerExceptions[0].Message.ShouldBe("first");
        ex.InnerExceptions[1].Message.ShouldBe("second");

        await enumerator.DisposeAsync();
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Merge_Disposes_Created_Sources_When_Awaiter_GetResult_Throws()
    {
        var first = new ControlledEnumerable<int>();
        var second = new ControlledEnumerable<int>();
        var enumerator = first.Merge(second).GetAsyncEnumerator(CancellationToken);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();

        first.GetAsyncEnumeratorCalls.ShouldBe(1);
        second.GetAsyncEnumeratorCalls.ShouldBe(1);

        second.Fail(new InvalidOperationException("boom"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () => await moveNextTask);

        ex.Message.ShouldBe("boom");
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);

        await enumerator.DisposeAsync();
        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
    }

    [Fact]
    public async Task Merge_DisposeAsync_Disposes_Pending_Sources()
    {
        var first = new ControlledEnumerable<int>();
        var second = new ControlledEnumerable<int>();
        var enumerator = first.Merge(second).GetAsyncEnumerator(CancellationToken);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();

        first.GetAsyncEnumeratorCalls.ShouldBe(1);
        second.GetAsyncEnumeratorCalls.ShouldBe(1);

        await enumerator.DisposeAsync();

        first.DisposeCalls.ShouldBe(1);
        second.DisposeCalls.ShouldBe(1);
        moveNextTask.IsCompleted.ShouldBeFalse();
    }

    private sealed class CancellableEnumerable<T>
        : IAsyncEnumerable<T>
    {
        private readonly CancellationToken _token;

        public CancellableEnumerable(CancellationToken token)
        {
            _token = token;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            return new Enumerator(_token);
        }

        private class Enumerator : IAsyncEnumerator<T>
        {
            private readonly TaskCompletionSource<bool> _tcs;

            public Enumerator(CancellationToken ct)
            {
                _tcs = new();
                if (ct.IsCancellationRequested)
                {
                    _tcs.TrySetCanceled(ct);
                }
                else
                {
                    ct.Register(() => _tcs.SetCanceled(ct), useSynchronizationContext: false);
                }
            }

            public T Current => throw new InvalidOperationException();

            public ValueTask DisposeAsync()
            {
                _tcs.TrySetCanceled();

                return ValueTask.CompletedTask;
            }

            public ValueTask<bool> MoveNextAsync()
            {
                return new(_tcs.Task);
            }
        }
    }

    private sealed class ThrowingDisposeEnumerable<T>(Exception exception)
        : IAsyncEnumerable<T>
    {
        private int _disposeCalls;

        public int DisposeCalls => _disposeCalls;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator(this, exception);

        private sealed class Enumerator(ThrowingDisposeEnumerable<T> owner, Exception exception)
            : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCalls);
                return ValueTask.FromException(exception);
            }
        }
    }

    private sealed class TrackingEnumerable<T> : IAsyncEnumerable<T>
    {
        private int _getAsyncEnumeratorCalls;
        private int _disposeCalls;

        public int GetAsyncEnumeratorCalls => _getAsyncEnumeratorCalls;

        public int DisposeCalls => _disposeCalls;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getAsyncEnumeratorCalls);
            return new Enumerator(this);
        }

        private sealed class Enumerator(TrackingEnumerable<T> owner) : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => ValueTask.FromResult(false);

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCalls);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class ThrowingGetAsyncEnumeratorEnumerable<T>(Exception exception) : IAsyncEnumerable<T>
    {
        private int _getAsyncEnumeratorCalls;

        public int GetAsyncEnumeratorCalls => _getAsyncEnumeratorCalls;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getAsyncEnumeratorCalls);
            throw exception;
        }
    }

    private sealed class ControlledEnumerable<T> : IAsyncEnumerable<T>
    {
        private readonly TaskCompletionSource<bool> _moveNext = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _getAsyncEnumeratorCalls;
        private int _disposeCalls;

        public int GetAsyncEnumeratorCalls => _getAsyncEnumeratorCalls;

        public int DisposeCalls => _disposeCalls;

        public void Fail(Exception exception) => _moveNext.TrySetException(exception);

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _getAsyncEnumeratorCalls);
            return new Enumerator(this);
        }

        private sealed class Enumerator(ControlledEnumerable<T> owner) : IAsyncEnumerator<T>
        {
            public T Current => default!;

            public ValueTask<bool> MoveNextAsync() => new(owner._moveNext.Task);

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner._disposeCalls);
                return ValueTask.CompletedTask;
            }
        }
    }
}
