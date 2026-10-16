namespace Altinn.Register.Persistence.UnitOfWork;

/// <summary>
/// Represents a save point in a transaction.
/// </summary>
internal interface ISavePoint
    : IAsyncDisposable
{
    /// <summary>
    /// Rolls the transaction back to before the save point was created.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    Task RollbackAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the save point. This effectively commits it to the transaction.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    Task ReleaseAsync(CancellationToken cancellationToken = default);
}
