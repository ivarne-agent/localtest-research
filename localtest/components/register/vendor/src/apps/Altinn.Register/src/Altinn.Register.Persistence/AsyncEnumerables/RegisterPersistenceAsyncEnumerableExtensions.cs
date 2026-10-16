namespace Altinn.Register.Persistence.AsyncEnumerables;

/// <summary>
/// Extension methods for <see cref="IAsyncEnumerable{T}"/>.
/// </summary>
internal static class RegisterPersistenceAsyncEnumerableExtensions
{
    /// <summary>
    /// Binds a resource to a <see cref="IAsyncEnumerable{T}"/>, such that it is disposed
    /// when the <paramref name="enumerable"/> is disposed.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="enumerable">The source <see cref="IAsyncEnumerable{T}"/>.</param>
    /// <param name="resource">The resource to dispose with the <paramref name="enumerable"/>.</param>
    /// <returns>
    /// A <see cref="IAsyncEnumerable{T}"/> that yields the items from <paramref name="enumerable"/>,
    /// and disposes <paramref name="resource"/> when it is disposed.
    /// </returns>
    public static IAsyncEnumerable<T> Using<T>(this IAsyncEnumerable<T> enumerable, IAsyncDisposable? resource)
    {
        if (resource is null)
        {
            return enumerable;
        }

        if (enumerable is IAsyncResourceOwner owner)
        {
            owner.Adopt([resource]);
            return enumerable;
        }

        var ret = new ResourceOwningEnumerable<T>(enumerable);
        ret.Adopt([resource]);
        return ret;
    }
}
