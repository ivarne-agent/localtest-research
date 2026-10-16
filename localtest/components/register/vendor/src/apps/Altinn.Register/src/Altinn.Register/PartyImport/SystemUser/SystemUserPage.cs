using System.Collections;
using System.Collections.Immutable;

namespace Altinn.Register.PartyImport.SystemUser;

/// <summary>
/// A page of system users from the System User Stream API.
/// </summary>
internal sealed class SystemUserPage(
    ImmutableArray<SystemUserItem> items,
    string? nextUrl,
    ulong sequenceMax)
    : IReadOnlyList<SystemUserItem>
{
    /// <inheritdoc/>
    public SystemUserItem this[int index]
        => items[index];

    /// <summary>
    /// Gets the URL to fetch the next page, or <c>null</c> if there are no more pages.
    /// </summary>
    public string? NextUrl
        => nextUrl;

    /// <summary>
    /// Gets the maximum sequence number of the system users.
    /// </summary>
    public ulong SequenceMax
        => sequenceMax;

    /// <inheritdoc/>
    public int Count
        => items.Length;

    /// <summary>
    /// Returns an enumerator that iterates through the page.
    /// </summary>
    /// <returns>An enumerator.</returns>
    public ImmutableArray<SystemUserItem>.Enumerator GetEnumerator()
        => items.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator<SystemUserItem> IEnumerable<SystemUserItem>.GetEnumerator()
        => ((IEnumerable<SystemUserItem>)items).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator()
        => ((IEnumerable)items).GetEnumerator();
}
