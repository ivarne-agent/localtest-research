using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Altinn.Authorization.ModelUtils;

namespace Altinn.Register.Core.Utils;

// TODO: Move this to modelutils and build out properly

/// <summary>
/// A set of values that have value semantics. The set is sorted.
/// </summary>
/// <remarks>As of right now, most set operations are unsupported</remarks>
/// <typeparam name="T">The item type.</typeparam>
[DebuggerDisplay("{_inner,nq}")]
public readonly struct ImmutableValueSet<T>
    : IReadOnlySet<T>
    where T : notnull
{
    /// <summary>
    /// An empty (initialized) instance of <see cref="ImmutableValueSet{T}"/>.
    /// </summary>
    public static readonly ImmutableValueSet<T> Empty = new(ImmutableValueArray<T>.Empty, Comparer<T>.Default);

    /// <summary>
    /// Creates a new instance of <see cref="ImmutableValueSet{T}"/> with the specified items.
    /// </summary>
    /// <param name="items">The items. Duplicates are removed.</param>
    /// <param name="comparer">The comparer used to sort the items.</param>
    /// <returns>A new instance of <see cref="ImmutableValueSet{T}"/> containing the specified items.</returns>
    public static ImmutableValueSet<T> Create(
        IEnumerable<T> items,
        IComparer<T>? comparer = null)
    {
        ImmutableArray<T>.Builder builder;
        if (items.TryGetNonEnumeratedCount(out int count))
        {
            builder = ImmutableArray.CreateBuilder<T>(count);
        }
        else
        {
            builder = ImmutableArray.CreateBuilder<T>();
        }

        comparer ??= Comparer<T>.Default;
        foreach (var item in items)
        {
            if (!Contains(builder, item, comparer))
            {
                builder.Add(item);
            }
        }

        builder.Sort(comparer);
        return CreateUnchecked(builder.DrainToImmutableValueArray(), comparer);
    }

    /// <summary>
    /// Creates a new instance of <see cref="ImmutableValueSet{T}"/> with the specified values.
    /// </summary>
    /// <remarks>The input array is not validated</remarks>
    /// <param name="inner">The array values to include in the set.</param>
    /// <param name="comparer">The equality comparer to use for set operations.</param>
    /// <returns>A new instance of <see cref="ImmutableValueSet{T}"/> containing the specified values.</returns>
    internal static ImmutableValueSet<T> CreateUnchecked(
        ImmutableValueArray<T> inner,
        IComparer<T>? comparer = null)
        => new(inner, comparer ?? Comparer<T>.Default);

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    private readonly ImmutableValueArray<T> _inner;
    private readonly IComparer<T> _comparer;

    private ImmutableValueSet(
        ImmutableValueArray<T> inner,
        IComparer<T> comparer)
    {
        _inner = inner;
        _comparer = comparer;
    }

    /// <summary>
    /// Gets a value indicating whether this collection is empty.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsEmpty => _inner.IsEmpty;

    /// <summary>
    /// Gets the number of elements in the array.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public int Length => _inner.Length;

    /// <summary>
    /// Gets a value indicating whether this struct was initialized without an actual array instance.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsDefault => _inner.IsDefault;

    /// <summary>
    /// Gets a value indicating whether this struct is empty or uninitialized.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public bool IsDefaultOrEmpty => _inner.IsDefaultOrEmpty;

    /// <inheritdoc/>
    public bool Contains(T item)
        => _inner.AsSpan().BinarySearch(item, _comparer) >= 0;

    /// <summary>
    /// Returns an enumerator for the contents of the array.
    /// </summary>
    /// <returns>An enumerator.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ImmutableArray<T>.Enumerator GetEnumerator()
        => _inner.GetEnumerator();

    /// <inheritdoc/>
    int IReadOnlyCollection<T>.Count
        => Length;

    /// <inheritdoc/>
    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => ((IEnumerable<T>)_inner).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_inner).GetEnumerator();

    /// <inheritdoc/>
    bool IReadOnlySet<T>.IsProperSubsetOf(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    bool IReadOnlySet<T>.IsProperSupersetOf(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    bool IReadOnlySet<T>.IsSubsetOf(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    bool IReadOnlySet<T>.IsSupersetOf(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    bool IReadOnlySet<T>.Overlaps(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    bool IReadOnlySet<T>.SetEquals(IEnumerable<T> other)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Returns a new <see cref="ImmutableValueSet{T}"/> that is the union of this set and another set.
    /// The resulting set will contain all unique elements from both sets.
    /// The resulting set is sorted using this set's comparer, matching the ordering invariant of <see cref="ImmutableValueSet{T}"/>.
    /// </summary>
    /// <param name="other">The other set to union with.</param>
    /// <returns>A new <see cref="ImmutableValueSet{T}"/> containing all unique elements from both sets.</returns>
    public ImmutableValueSet<T> UnionWith(ImmutableValueSet<T> other)
    {
        ImmutableArray<T>.Builder builder = ImmutableArray.CreateBuilder<T>(Length + other.Length);
        foreach (var item in this)
        {
            // no need to check for duplicates within the same set since duplicates are not allowed
            builder.Add(item);
        }

        foreach (var item in other)
        {
            if (!Contains(builder, item, _comparer))
            {
                builder.Add(item);
            }
        }

        builder.Sort(_comparer);
        return CreateUnchecked(builder.DrainToImmutableValueArray(), _comparer);
    }

    private static bool Contains(ImmutableArray<T>.Builder builder, T item, IComparer<T> comparer)
    {
        for (int i = 0; i < builder.Count; i++)
        {
            if (comparer.Compare(builder[i], item) == 0)
            {
                return true;
            }
        }

        return false;
    }
}
