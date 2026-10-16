namespace Altinn.Register.Core.Sire;

/// <summary>
/// Client for the SIRE event-feed API (Skatteetaten-registrert selskap hendelser).
/// </summary>
public interface ISireEventClient
{
    /// <summary>
    /// Gets the changes that have occurred in SIRE data starting from the given sequence
    /// number. Pages are yielded in sequence order; iteration stops when the feed returns
    /// an empty page.
    /// </summary>
    /// <param name="fromInclusive">The sequence number from which to start retrieving updates, inclusive.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> of <see cref="SireUpdatePage"/> containing the updates.</returns>
    IAsyncEnumerable<SireUpdatePage> GetUpdates(uint fromInclusive = 1, CancellationToken cancellationToken = default);
}
