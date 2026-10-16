namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// A service that forms a part of a unit of work.
/// </summary>
public interface IUnitOfWorkParticipant
    : IAsyncDisposable
{
    /// <summary>
    /// Commits the unit of work.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rolls back the unit of work.
    /// </summary>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <remarks>
    /// This is also performed by calling <see cref="IAsyncDisposable.DisposeAsync"/>
    /// if the unit of work has not already been committed or rolled back.
    /// </remarks>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// A service that forms a part of a unit of work.
/// </summary>
/// <typeparam name="TService">The inner service, wrapped with unit of work functionality.</typeparam>
public interface IUnitOfWorkParticipant<TService>
    : IUnitOfWorkParticipant
    where TService : class
{
    /// <summary>
    /// Gets the inner service.
    /// </summary>
    TService Service { get; }
}
