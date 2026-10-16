using MassTransit.Testing;

namespace Altinn.Register.TestUtils.MassTransit;

/// <summary>
/// Extension methods for <see cref="IAsyncElementList{TElement}"/>.
/// </summary>
public static class AsyncElementListExtensions
{
    public static async IAsyncEnumerable<T> SelectExisting<T>(
        this IAsyncElementList<T> list,
        FilterDelegate<T> filter)
        where T : class, IAsyncListElement
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await foreach (var item in list.SelectAsync(filter, cts.Token))
        {
            yield return item;
        }
    }
}
