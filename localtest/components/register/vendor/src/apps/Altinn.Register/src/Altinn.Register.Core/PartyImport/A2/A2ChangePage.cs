using System.Collections;
using System.Collections.Immutable;

namespace Altinn.Register.Core.PartyImport.A2;

/// <summary>
/// Base class for a page of changes from Altinn 2.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
public abstract class A2ChangePage<T>(ImmutableArray<T> items, uint lastKnownChangeId)
    : IReadOnlyList<T>
    where T : A2Change
{
    /// <inheritdoc/>
    public T this[int index]
        => items[index];

    /// <summary>
    /// Gets the <see cref="A2Change.ChangeId"/> of the last known change at the time this <see cref="A2ChangePage{T}"/> was fetched.
    /// </summary>
    public uint LastKnownChangeId
        => lastKnownChangeId;

    /// <inheritdoc/>
    public int Count
        => items.Length;

    /// <summary>
    /// Returns an enumerator that iterates through the page.
    /// </summary>
    /// <returns>An enumerator.</returns>
    public ImmutableArray<T>.Enumerator GetEnumerator()
        => items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => ((IEnumerable<T>)items).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)items).GetEnumerator();
}
