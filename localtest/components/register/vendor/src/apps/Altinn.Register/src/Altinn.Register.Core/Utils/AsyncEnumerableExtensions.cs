using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Extension methods for <see cref="IAsyncEnumerable{T}"/>
/// </summary>
public static class AsyncEnumerableExtensions
{
    /// <summary>
    /// Merges the specified <see cref="IAsyncEnumerable{T}"/> instances into a single <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements of the <paramref name="sources"/>.</typeparam>
    /// <param name="sources">The <see cref="IAsyncEnumerable{T}"/>s to merge.</param>
    /// <returns>A merged <see cref="IAsyncEnumerable{T}"/>.</returns>
    public static IAsyncEnumerable<T> Merge<T>(ReadOnlySpan<IAsyncEnumerable<T>> sources)
        => sources switch
        {
            [] => AsyncEnumerable.Empty<T>(),
            [var first] => first,
            [MergedAsyncEnumerable<T> first, .. var rest] => first.MergeWith(rest),
            _ => MergedAsyncEnumerable<T>.Create(sources),
        };

    /// <summary>
    /// Merges the specified <see cref="IAsyncEnumerable{T}"/> instances into a single <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequences.</typeparam>
    /// <param name="self">The first enumerable.</param>
    /// <param name="rest">The remaining enumerables.</param>
    /// <returns>The merged sequence.</returns>
    public static IAsyncEnumerable<T> Merge<T>(this IAsyncEnumerable<T> self, ReadOnlySpan<IAsyncEnumerable<T>> rest)
        => Merge([self, .. rest]);

    /// <summary>
    /// Merges the specified <see cref="IAsyncEnumerable{T}"/> instances into a single <see cref="IAsyncEnumerable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequences.</typeparam>
    /// <param name="self">The first enumerable.</param>
    /// <param name="other">The second enumerable.</param>
    /// <returns>The merged sequence.</returns>
    public static IAsyncEnumerable<T> Merge<T>(this IAsyncEnumerable<T> self, IAsyncEnumerable<T> other)
        => Merge([self, other]);

    /// <summary>
    /// Writes the elements of the <see cref="IAsyncEnumerable{T}"/> to the specified <see cref="ChannelWriter{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of items in the <paramref name="source"/>.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="writer">The channel writer.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task WriteToAsync<T>(this IAsyncEnumerable<T> source, ChannelWriter<T> writer, CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken))
        {
            if (!await writer.WaitToWriteAsync(cancellationToken))
            {
                return;
            }

            await writer.WriteAsync(item, cancellationToken);
        }
    }

    /// <summary>
    /// Wraps exceptions thrown by the source enumerable in a new exception.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="wrap">The exception wrapper.</param>
    /// <param name="cancellationToken">Cancellation token (used to filter out <see cref="OperationCanceledException"/>s).</param>
    /// <returns>The source enumerable, but with any exceptions optionally wrapped by <paramref name="wrap"/>.</returns>
    public static IAsyncEnumerable<T> WrapExceptions<T>(
        this IAsyncEnumerable<T> source,
        Func<Exception, Exception?> wrap,
        CancellationToken cancellationToken)
    {
        Guard.IsNotNull(source);
        Guard.IsNotNull(wrap);

        return new WrapExceptionsAsyncEnumerable<T>(source, wrap, cancellationToken);
    }

    /// <summary>
    /// Wraps exceptions thrown by the source enumerable in a new exception.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="wrap">The exception wrapper.</param>
    /// <param name="cancellationToken">Cancellation token (used to filter out <see cref="OperationCanceledException"/>s).</param>
    /// <returns>The source enumerable, but with any exceptions optionally wrapped by <paramref name="wrap"/>.</returns>
    public static IAsyncSideEffectEnumerable<T> WrapExceptions<T>(
        this IAsyncSideEffectEnumerable<T> source,
        Func<Exception, Exception?> wrap,
        CancellationToken cancellationToken)
    {
        Guard.IsNotNull(source);
        Guard.IsNotNull(wrap);

        return new WrapExceptionsAsyncSideEffectEnumerable<T>(source, wrap, cancellationToken);
    }

    private class WrapExceptionsAsyncEnumerable<T>
        : IAsyncEnumerable<T>
    {
        private readonly IAsyncEnumerable<T> _source;
        private readonly Func<Exception, Exception?> _wrap;
        private readonly CancellationToken _cancellationToken;

        public WrapExceptionsAsyncEnumerable(
            IAsyncEnumerable<T> source,
            Func<Exception, Exception?> wrap,
            CancellationToken cancellationToken)
        {
            _source = source;
            _wrap = wrap;
            _cancellationToken = cancellationToken;
        }

        protected Exception? WrapException(Exception source)
            => _wrap(source);

        protected bool ShouldRethrowAsIs(Exception exception)
            => exception is OperationCanceledException ex && ex.CancellationToken == _cancellationToken;

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        {
            try
            {
                return new Enumerator(_source.GetAsyncEnumerator(cancellationToken), _wrap, _cancellationToken);
            }
            catch (Exception ex)
            {
                if (ShouldRethrowAsIs(ex))
                {
                    throw;
                }

                if (WrapException(ex) is Exception wrapped)
                {
                    throw wrapped;
                }

                throw;
            }
        }

        private sealed class Enumerator
            : IAsyncEnumerator<T>
        {
            private readonly IAsyncEnumerator<T> _source;
            private readonly Func<Exception, Exception?> _wrap;
            private readonly CancellationToken _cancellationToken;

            public Enumerator(
                IAsyncEnumerator<T> source,
                Func<Exception, Exception?> wrap,
                CancellationToken cancellationToken)
            {
                _source = source;
                _wrap = wrap;
                _cancellationToken = cancellationToken;
            }

            private Exception? WrapException(Exception source)
                => _wrap(source);

            private bool ShouldRethrowAsIs(Exception exception)
                => exception is OperationCanceledException ex && ex.CancellationToken == _cancellationToken;

            public T Current => _source.Current;

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _source.DisposeAsync();
                }
                catch (Exception ex)
                {
                    if (ShouldRethrowAsIs(ex))
                    {
                        throw;
                    }

                    if (WrapException(ex) is Exception wrapped)
                    {
                        throw wrapped;
                    }

                    throw;
                }
            }

            public async ValueTask<bool> MoveNextAsync()
            {
                try
                {
                    return await _source.MoveNextAsync();
                }
                catch (Exception ex)
                {
                    if (ShouldRethrowAsIs(ex))
                    {
                        throw;
                    }

                    if (WrapException(ex) is Exception wrapped)
                    {
                        throw wrapped;
                    }

                    throw;
                }
            }
        }
    }

    private sealed class WrapExceptionsAsyncSideEffectEnumerable<T>
        : WrapExceptionsAsyncEnumerable<T>
        , IAsyncSideEffectEnumerable<T>
    {
        private readonly IAsyncSideEffectEnumerable<T> _source;

        public WrapExceptionsAsyncSideEffectEnumerable(
            IAsyncSideEffectEnumerable<T> source,
            Func<Exception, Exception?> wrap,
            CancellationToken cancellationToken)
            : base(source, wrap, cancellationToken)
        {
            _source = source;
        }

        public TaskAwaiter GetAwaiter()
            => Run().GetAwaiter();

        private async Task Run()
        {
            try
            {
                await _source;
            }
            catch (Exception ex)
            {
                if (ShouldRethrowAsIs(ex))
                {
                    throw;
                }

                if (WrapException(ex) is Exception wrapped)
                {
                    throw wrapped;
                }

                throw;
            }
        }
    }

    private sealed class MergedAsyncEnumerable<T>
        : IAsyncEnumerable<T>
    {
        private readonly ImmutableArray<IAsyncEnumerable<T>> _sources;

        private MergedAsyncEnumerable(ImmutableArray<IAsyncEnumerable<T>> sources)
        {
            _sources = sources;
        }

        public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            => new Enumerator(_sources, cancellationToken);

        public MergedAsyncEnumerable<T> MergeWith(ReadOnlySpan<IAsyncEnumerable<T>> sources)
            => Create([.. _sources, .. sources]);

        public static MergedAsyncEnumerable<T> Create(ReadOnlySpan<IAsyncEnumerable<T>> sources)
        {
            var length = sources.Length;
            foreach (var source in sources)
            {
                if (source is null)
                {
                    throw new ArgumentNullException(nameof(sources));
                }

                if (source is MergedAsyncEnumerable<T> merged)
                {
                    length += merged._sources.Length - 1;
                }
            }

            var builder = ImmutableArray.CreateBuilder<IAsyncEnumerable<T>>(length);
            foreach (var source in sources)
            {
                if (source is MergedAsyncEnumerable<T> merged)
                {
                    builder.AddRange(merged._sources);
                }
                else
                {
                    builder.Add(source);
                }
            }

            return new MergedAsyncEnumerable<T>(builder.DrainToImmutable());
        }

        private sealed class Enumerator
            : IAsyncEnumerator<T>
            , IValueTaskSource<bool>
        {
            private readonly Lock _lock = new();
            private readonly CancellationToken _cancellationToken;
            private readonly ImmutableArray<IAsyncEnumerable<T>> _enumerables;
            private readonly SourceState[] _sources;
            private ManualResetValueTaskSourceCore<bool> _tcs;
            private bool _disposed;
            private bool _waiting;
            private int _index;
            private T _current = default!;

            public Enumerator(ImmutableArray<IAsyncEnumerable<T>> sources, CancellationToken cancellationToken)
            {
                _cancellationToken = cancellationToken;
                _enumerables = sources;
                _tcs = default;
                _tcs.RunContinuationsAsynchronously = true;
                _sources = new SourceState[sources.Length];
            }

            public T Current => _current;

            public async ValueTask DisposeAsync()
            {
                await DisposeSourcesAsync(DetachSourcesForDispose());
            }

            public ValueTask<bool> MoveNextAsync()
            {
                _cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    lock (_lock)
                    {
                        Debug.Assert(!_waiting);
                        bool anyPending = false;

                        for (var offset = 0; offset < _sources.Length; offset++)
                        {
                            var i = (_index + offset) % _sources.Length;
                            ref var source = ref _sources[i];

                            switch (source.Status)
                            {
                                case SourceState.STATUS_INITIAL:
                                    source.Source ??= _enumerables[i].GetAsyncEnumerator(_cancellationToken);

                                    var awaiter = source.Source.MoveNextAsync().ConfigureAwait(false).GetAwaiter();
                                    if (awaiter.IsCompleted)
                                    {
                                        if (awaiter.GetResult())
                                        {
                                            _current = source.Source.Current;
                                            _index = (i + 1) % _sources.Length;
                                            return new ValueTask<bool>(true);
                                        }

                                        source.Status = SourceState.STATUS_DONE;
                                    }
                                    else
                                    {
                                        source.Status = SourceState.STATUS_PENDING;
                                        source.Awaiter = awaiter;
                                        var callback = source.Callback ??= CreateCallback(this, i);
                                        awaiter.UnsafeOnCompleted(callback);
                                        anyPending = true;
                                    }

                                    break;

                                case SourceState.STATUS_PENDING:
                                    anyPending = true;
                                    break;

                                case SourceState.STATUS_READY:
                                    Debug.Assert(source.Awaiter.IsCompleted);
                                    if (source.Awaiter.GetResult())
                                    {
                                        Debug.Assert(source.Source is not null);
                                        _current = source.Source.Current;
                                        _index = (i + 1) % _sources.Length;
                                        source.Status = SourceState.STATUS_INITIAL;
                                        return new ValueTask<bool>(true);
                                    }

                                    source.Status = SourceState.STATUS_DONE;
                                    break;
                            }
                        }

                        if (anyPending)
                        {
                            _tcs.Reset();
                            _waiting = true;
                            return new ValueTask<bool>(this, _tcs.Version);
                        }
                    }
                }
                catch (Exception ex)
                {
                    return DisposeOnErrorAsync(ex);
                }

                return new ValueTask<bool>(false);
            }

            bool IValueTaskSource<bool>.GetResult(short token)
            {
                lock (_lock)
                {
                    return _tcs.GetResult(token);
                }
            }

            ValueTaskSourceStatus IValueTaskSource<bool>.GetStatus(short token)
            {
                lock (_lock)
                {
                    return _tcs.GetStatus(token);
                }
            }

            void IValueTaskSource<bool>.OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            {
                lock (_lock)
                {
                    _tcs.OnCompleted(continuation, state, token, flags);
                }
            }

            private void OnSourceReady(int index)
            {
                IAsyncEnumerator<T>?[]? sourcesToDispose = null;
                Exception? error = null;

                lock (_lock)
                {
                    ref var source = ref _sources[index];
                    if (source.Status == SourceState.STATUS_DONE)
                    {
                        // This can happen if the source completes during dispose, after we've done DetachSourcesForDispose
                        return;
                    }

                    Debug.Assert(source.Status == SourceState.STATUS_PENDING);
                    source.Status = SourceState.STATUS_READY;

                    if (!_waiting)
                    {
                        return;
                    }

                    bool moveNextResult = false;
                    try
                    {
                        moveNextResult = source.Awaiter.GetResult();
                        source.Status = SourceState.STATUS_INITIAL;
                    }
                    catch (Exception e)
                    {
                        sourcesToDispose = DetachSourcesForDisposeNoLock(index);
                        error = e;
                        goto CompleteException;
                    }

                    if (moveNextResult)
                    {
                        Debug.Assert(source.Source is not null);
                        _current = source.Source.Current;
                        _index = (index + 1) % _sources.Length;
                        _waiting = false;
                        _tcs.SetResult(true);
                        return;
                    }

                    source.Status = SourceState.STATUS_DONE;
                    if (_sources.All(static s => s.Status == SourceState.STATUS_DONE))
                    {
                        _index = (index + 1) % _sources.Length;
                        _waiting = false;
                        _tcs.SetResult(false);
                    }
                }

            CompleteException:
                if (error is not null)
                {
                    Debug.Assert(sourcesToDispose is not null);
                    _ = CompleteExceptionAfterDisposeAsync(sourcesToDispose, error);
                }
            }

            private static Action CreateCallback(Enumerator enumerator, int index)
                => () => enumerator.OnSourceReady(index);

            private async ValueTask<bool> DisposeOnErrorAsync(Exception exception)
            {
                try
                {
                    await DisposeSourcesAsync(DetachSourcesForDispose());
                }
                catch (Exception disposeException)
                {
                    if (disposeException is AggregateException aggregateException)
                    {
                        throw new AggregateException([exception, .. aggregateException.InnerExceptions]);
                    }

                    throw new AggregateException(exception, disposeException);
                }

                ExceptionDispatchInfo.Capture(exception).Throw();
                throw null!;
            }

            private async Task CompleteExceptionAfterDisposeAsync(IAsyncEnumerator<T>?[] sourcesToDispose, Exception exception)
            {
                Exception error = exception;

                try
                {
                    await DisposeSourcesAsync(sourcesToDispose);
                }
                catch (Exception disposeException)
                {
                    error = disposeException is AggregateException aggregateException
                        ? new AggregateException([exception, .. aggregateException.InnerExceptions])
                        : new AggregateException(exception, disposeException);
                }

                lock (_lock)
                {
                    _tcs.SetException(error);
                }
            }

            private IAsyncEnumerator<T>?[] DetachSourcesForDispose()
            {
                lock (_lock)
                {
                    return DetachSourcesForDisposeNoLock();
                }
            }

            private IAsyncEnumerator<T>?[] DetachSourcesForDisposeNoLock(int? completedIndex = null)
            {
                if (_disposed)
                {
                    return [];
                }

                _disposed = true;
                _waiting = false;

                if (completedIndex is int index)
                {
                    _index = (index + 1) % _sources.Length;
                }

                var sourcesToDispose = new IAsyncEnumerator<T>?[_sources.Length];
                for (var i = 0; i < _sources.Length; i++)
                {
                    ref var source = ref _sources[i];
                    sourcesToDispose[i] = source.Source;
                    source.Source = null;
                    source.Status = SourceState.STATUS_DONE;
                    source.Callback = null;
                    source.Awaiter = default;
                }

                return sourcesToDispose;
            }

            private static async ValueTask DisposeSourcesAsync(IAsyncEnumerator<T>?[] sourcesToDispose)
            {
                List<Exception>? exceptions = null;

                for (var i = 0; i < sourcesToDispose.Length; i++)
                {
                    if (sourcesToDispose[i] is not { } source)
                    {
                        continue;
                    }

                    try
                    {
                        await source.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        exceptions ??= [];
                        exceptions.Add(ex);
                    }
                }

                if (exceptions is not null)
                {
                    throw new AggregateException(exceptions);
                }
            }

            private struct SourceState
            {
                public const byte STATUS_INITIAL = 0;
                public const byte STATUS_PENDING = 1;
                public const byte STATUS_READY = 2;
                public const byte STATUS_DONE = 3;

                public IAsyncEnumerator<T>? Source;
                public byte Status;
                public Action? Callback;
                public ConfiguredValueTaskAwaitable<bool>.ConfiguredValueTaskAwaiter Awaiter;
            }
        }
    }
}
