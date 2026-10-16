using System.Runtime.CompilerServices;

namespace Altinn.Register.Tests.Utils;

internal static class AsyncEnumerableTestExtensions
{
    public static async IAsyncEnumerable<T> Yielding<T>(this IAsyncEnumerable<T> source, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = source.GetAsyncEnumerator(cancellationToken);
        await Task.Yield();

        while (await enumerator.MoveNextAsync())
        {
            yield return enumerator.Current;
            await Task.Yield();
        }

        // before the dispose
        await Task.Yield();
    }
}
