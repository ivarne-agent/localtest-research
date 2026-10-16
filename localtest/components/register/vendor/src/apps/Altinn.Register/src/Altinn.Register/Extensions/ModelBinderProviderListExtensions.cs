using Altinn.Register.ModelBinding;

using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Altinn.Register.Extensions;

/// <summary>
/// Extension methods for <see cref="IList{T}"/> of <see cref="IModelBinderProvider"/>.
/// </summary>
internal static class ModelBinderProviderListExtensions
{
    /// <summary>
    /// Inserts a <see cref="ISingleton{TSelf}"/> binder provider into the list at the specified index.
    /// </summary>
    /// <typeparam name="T">The binder type.</typeparam>
    /// <param name="list">The list of <see cref="IModelBinderProvider"/>.</param>
    /// <param name="index">The zero-based index at which the binder provider should be inserted.</param>
    /// <returns>The modified list, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="list"/> is null.</exception>
    public static IList<IModelBinderProvider> InsertSingleton<T>(this IList<IModelBinderProvider> list, int index)
        where T : IModelBinderProvider, ISingleton<T>
    {
        ArgumentNullException.ThrowIfNull(list);

        var instance = T.Instance;
        list.Insert(index, instance);
        return list;
    }
}
