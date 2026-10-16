namespace Altinn.Register.Core.Utils;

/// <summary>
/// Extension methods for <see cref="IEnumerable{T}"/>
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// Returns an empty enumerable if the source is null.
    /// </summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <returns><paramref name="source"/>, or an empty enumerable.</returns>
    public static IEnumerable<T> OrEmpty<T>(this IEnumerable<T>? source)
    {
        return source ?? [];
    }
}
