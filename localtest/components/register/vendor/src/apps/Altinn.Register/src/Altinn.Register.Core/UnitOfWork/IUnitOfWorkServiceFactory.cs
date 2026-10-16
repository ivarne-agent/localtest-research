namespace Altinn.Register.Core.UnitOfWork;

/// <summary>
/// Marker interface for a factory that creates services that participate in a unit of work.
/// </summary>
public interface IUnitOfWorkServiceFactory
{
}

/// <summary>
/// Factory that creates services that participate in a unit of work.
/// </summary>
/// <typeparam name="TService">The service type.</typeparam>
public interface IUnitOfWorkServiceFactory<TService>
    : IUnitOfWorkServiceFactory
    where TService : class
{
    /// <summary>
    /// Creates a service that participates in a unit of work.
    /// </summary>
    /// <param name="services">The <see cref="IServiceProvider"/>.</param>
    /// <returns>A service that participates in a unit of work.</returns>
    TService Create(IServiceProvider services);
}
