using System.Collections.Immutable;

namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// A factory for creating participants in a unit of work.
/// </summary>
public interface IUnitOfWorkParticipantFactory
{
    /// <summary>
    /// Gets the public service types that the factory creates.
    /// </summary>
    ImmutableArray<Type> ServiceTypes { get; }

    /// <summary>
    /// Factory method for creating a participant in a unit of work.
    /// </summary>
    /// <param name="handle">A <see cref="IUnitOfWorkHandle"/>.</param>
    /// <param name="serviceProvider">A <see cref="IServiceProvider"/>.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="IUnitOfWorkParticipant"/>.</returns>
    ValueTask<IUnitOfWorkParticipant> Create(IUnitOfWorkHandle handle, IServiceProvider serviceProvider, CancellationToken cancellationToken = default);
}
