using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using Altinn.Authorization.ModelUtils;
using CommunityToolkit.Diagnostics;

namespace Altinn.Register.Core.Sire;

/// <summary>
/// Represents a page of SIRE updates. The updates are ordered by their sequence number.
/// </summary>
public sealed record SireUpdatePage
    : IReadOnlyList<SireUpdate>
{
    private readonly ImmutableValueArray<SireUpdate> _updates;

    /// <summary>
    /// Initializes a new instance of the <see cref="SireUpdatePage"/> class with the specified updates.
    /// </summary>
    /// <param name="updates">The updates in the page. Must not be a default array.</param>
    public SireUpdatePage(ImmutableValueArray<SireUpdate> updates)
    {
        if (updates.IsDefault)
        {
            ThrowHelper.ThrowArgumentException(nameof(updates), "Updates must be provided.");
        }

        Debug.Assert(IsSorted(updates));
        _updates = updates;
    }

    /// <inheritdoc/>
    public SireUpdate this[int index]
        => _updates[index];

    /// <inheritdoc/>
    public int Count
        => _updates.Length;

    private static bool IsSorted(ImmutableValueArray<SireUpdate> updates)
    {
        var prev = uint.MinValue;
        foreach (var update in updates)
        {
            if (update.SequenceNumber <= prev)
            {
                return false;
            }

            prev = update.SequenceNumber;
        }

        return true;
    }

    /// <inheritdoc cref="IEnumerable{T}.GetEnumerator"/>
    public ImmutableArray<SireUpdate>.Enumerator GetEnumerator()
        => _updates.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<SireUpdate> IEnumerable<SireUpdate>.GetEnumerator()
        => ((IEnumerable<SireUpdate>)_updates).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)_updates).GetEnumerator();
}
