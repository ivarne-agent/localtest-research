using Altinn.Register.Core.ImportJobs;
using Altinn.Register.Core.Parties;
using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// Extension methods for unit of work.
/// </summary>
public static class UnitOfWorkExtensions
{
    /// <summary>
    /// Gets a <see cref="IPartyPersistence"/> registered with the unit of work.
    /// </summary>
    /// <param name="uow">The unit of work.</param>
    /// <returns>A <see cref="IPartyPersistence"/>.</returns>
    public static IPartyPersistence GetPartyPersistence(this IUnitOfWork uow)
        => uow.GetRequiredService<IPartyPersistence>();

    /// <summary>
    /// Gets a <see cref="IPartyExternalRolePersistence"/> registered with the unit of work.
    /// </summary>
    /// <param name="uow">The unit of work.</param>
    /// <returns>A <see cref="IPartyExternalRolePersistence"/>.</returns>
    public static IPartyExternalRolePersistence GetPartyExternalRolePersistence(this IUnitOfWork uow)
        => uow.GetRequiredService<IPartyExternalRolePersistence>();

    /// <summary>
    /// Gets a <see cref="IImportJobStatePersistence"/> registered with the unit of work.
    /// </summary>
    /// <param name="uow">The unit of work.</param>
    /// <returns>A <see cref="IImportJobStatePersistence"/>.</returns>
    public static IImportJobStatePersistence GetImportJobStatePersistence(this IUnitOfWork uow)
        => uow.GetRequiredService<IImportJobStatePersistence>();
}
