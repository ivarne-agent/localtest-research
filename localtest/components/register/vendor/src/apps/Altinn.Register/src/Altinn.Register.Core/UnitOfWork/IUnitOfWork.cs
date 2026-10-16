using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// Represents an atomic unit of work.
/// </summary>
public interface IUnitOfWork
    : IAsyncDisposable
    , IUnitOfWorkHandle
    , IServiceProvider
    , ISupportRequiredService
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
/// A handle to a unit of work. Can be used to check the status of the unit of work.
/// </summary>
public interface IUnitOfWorkHandle
{
    /// <summary>
    /// Gets the status of the unit of work.
    /// </summary>
    UnitOfWorkStatus Status { get; }

    /// <summary>
    /// Throws an exception if the unit of work has been completed.
    /// </summary>
    void ThrowIfCompleted()
    {
        switch (Status)
        {
            case UnitOfWorkStatus.Committed:
            case UnitOfWorkStatus.RolledBack:
                ThrowHelper.ThrowInvalidOperationException("The unit of work has been completed.");
                break;

            case UnitOfWorkStatus.Disposed:
                ThrowHelper.ThrowObjectDisposedException(nameof(IUnitOfWork), "The unit of work has been disposed.");
                break;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the unit of work has been completed.
    /// </summary>
    bool IsCompleted => Status != UnitOfWorkStatus.Active;

    /// <summary>
    /// Gets a <see cref="CancellationToken"/> that is associated with the unit of work.
    /// </summary>
    /// <remarks>
    /// This cancellation token is linked to the unit of work's lifetime.
    /// </remarks>
    CancellationToken Token { get; }
}
