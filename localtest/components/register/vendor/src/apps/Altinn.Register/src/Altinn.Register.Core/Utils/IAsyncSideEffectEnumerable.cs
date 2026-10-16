using System.Runtime.CompilerServices;

namespace Altinn.Register.Core.Utils;

/// <summary>
/// Represents an async enumerable that can be executed just for its side effects.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public interface IAsyncSideEffectEnumerable<out T>
    : IAsyncEnumerable<T>
{
    /// <summary>Gets an awaiter used to await this <see cref="IAsyncSideEffectEnumerable{T}"/>.</summary>
    /// <returns>An awaiter instance.</returns>
    TaskAwaiter GetAwaiter();
}
