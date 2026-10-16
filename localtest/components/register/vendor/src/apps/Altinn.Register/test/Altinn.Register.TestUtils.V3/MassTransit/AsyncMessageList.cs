using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MassTransit.Testing;

namespace Altinn.Register.TestUtils.MassTransit;

[ExcludeFromCodeCoverage]
public abstract class AsyncMessageList<T, TOuter>
    : IAsyncEnumerable<T>
    where TOuter : class, IAsyncListElement
{
    /// <inheritdoc/>
    public abstract IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets items already completed (does not wait for new items).
    /// </summary>
    public abstract IAsyncEnumerable<T> Completed { get; }

    /// <summary>
    /// Gets the outer enumerable.
    /// </summary>
    public abstract IAsyncEnumerable<TOuter> Outer { get; }
}

/// <summary>
/// Represents a wrapped <see cref="IAsyncEnumerable{T}"/>, with the possibility of getting at the outer enumerable.
/// </summary>
/// <typeparam name="T">The inner item type.</typeparam>
/// <typeparam name="TOuter">The outer item type.</typeparam>
[ExcludeFromCodeCoverage]
internal sealed class AsyncMessageList<T, TOuter, TBase>
    : AsyncMessageList<T, TOuter>
    where TOuter : class, IAsyncListElement, TBase
    where TBase : class, IAsyncListElement
{
    private readonly ITestHarness _harness;
    private readonly Predicate<IReceivedMessage> _faultFilter;
    private readonly IAsyncElementList<TBase> _source;
    private readonly FilterDelegate<TBase> _filter;
    private readonly Func<TOuter, T> _selector;
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="AsyncUnwrappedEnumerable{T, TOuter}"/> class.
    /// </summary>
    /// <param name="source">The source <see cref="IAsyncElementList{T}"/>.</param>
    /// <param name="selector">The selector that unwraps <typeparamref name="TOuter"/>s to <typeparamref name="T"/>s.</param>
    public AsyncMessageList(
        ITestHarness harness,
        Predicate<IReceivedMessage> faultFilter,
        IAsyncElementList<TBase> source,
        FilterDelegate<TBase> filter,
        Func<TOuter, T> selector,
        CancellationToken cancellationToken)
    {
        _harness = harness;
        _faultFilter = faultFilter;
        _source = source;
        _filter = filter;
        _selector = selector;
        _cancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public override IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => GetOuterEnumerator(throwOnFaults: true, cancellationToken).Select(_selector).GetAsyncEnumerator(cancellationToken);

    /// <inheritdoc/>
    public override IAsyncEnumerable<T> Completed
        => GetCompleted();

    /// <inheritdoc/>
    public override IAsyncEnumerable<TOuter> Outer => GetOuterEnumerator(throwOnFaults: true, default);

    private IAsyncEnumerable<TOuter> GetOuterEnumerator(bool throwOnFaults, CancellationToken cancellationToken)
        => throwOnFaults
        ? GetOuterEnumeratorThrowOnFaults(cancellationToken)
        : GetOuterEnumeratorIgnoreFaults(cancellationToken);

    private async IAsyncEnumerable<T> GetCompleted([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // first, check if there are any existing faults
        var fault = await _harness.Consumed.SelectExisting(m => m.Exception is not null && _faultFilter(m)).Select(m => m.Exception!).ToListAsync(cancellationToken);
        if (fault is { Count: > 0 })
        {
            throw new AggregateException(fault);
        }

        await foreach (var item in _source.SelectExisting(_filter).OfType<TOuter>().Select(_selector))
        {
            yield return item;
        }
    }

    private async IAsyncEnumerable<TOuter> GetOuterEnumeratorThrowOnFaults([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

        var faults = _harness.Consumed.SelectAsync(m => m.Exception is not null && _faultFilter(m), cts.Token);
        var exnLock = new Lock();
        Exception? exn = null;

        var faultsConsumerTask = Task.Run(
            async () =>
            {
                await foreach (var fault in faults)
                {
                    if (fault.Exception is { } faultExn)
                    {
                        lock (exnLock)
                        {
                            exn = faultExn;
                        }

                        await cts.CancelAsync();
                        break;
                    }
                }
            },
            cts.Token);

        {
            await using var messages = _source.SelectAsync(_filter, _cancellationToken).OfType<TOuter>().GetAsyncEnumerator(cts.Token);
            while (!cts.IsCancellationRequested)
            {
                bool hasNext;
                try
                {
                    hasNext = await messages.MoveNextAsync();
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (!hasNext)
                {
                    break;
                }

                yield return messages.Current;
            }
        }

        await cts.CancelAsync();
        await faultsConsumerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        lock (exnLock)
        {
            if (exn is not null)
            {
                throw new AggregateException(exn);
            }
        }
    }

    private async IAsyncEnumerable<TOuter> GetOuterEnumeratorIgnoreFaults([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, cancellationToken);

        await foreach (TOuter outer in _source.SelectAsync(_filter, cts.Token).OfType<TOuter>())
        {
            yield return outer;
        }
    }
}
