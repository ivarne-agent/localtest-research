using Altinn.Register.Core.UnitOfWork;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/>.
/// </summary>
public static class UnitOfWorkServiceCollectionExtensions
{
    /// <summary>
    /// Registers a unit of work service with the service collection.
    /// </summary>
    /// <typeparam name="TService">The unit of work service type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddUnitOfWorkService<TService>(this IServiceCollection services)
        where TService : class
    {
        services.AddUnitOfWorkManager();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUnitOfWorkServiceFactory, UnitOfWorkServiceFactory<TService>>());

        return services;
    }

    /// <summary>
    /// Registers a unit of work service with the service collection.
    /// </summary>
    /// <typeparam name="TService">The unit of work service type.</typeparam>
    /// <typeparam name="TImplementation">The implementation of the service type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddUnitOfWorkService<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        services.AddUnitOfWorkManager();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUnitOfWorkServiceFactory, UnitOfWorkServiceFactory<TService, TImplementation>>());

        return services;
    }

    /// <summary>
    /// Registers a unit of work service with the service collection.
    /// </summary>
    /// <typeparam name="TService">The unit of work service type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="factory">The service factory.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddUnitOfWorkService<TService>(this IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
    {
        services.AddUnitOfWorkManager();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUnitOfWorkServiceFactory>(new UnitOfWorkServiceFactory<TService>(factory)));

        return services;
    }

    /// <summary>
    /// Registers a unit of work participant with the service collection.
    /// </summary>
    /// <typeparam name="TFactory">The factory for the unit of work participant.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddUnitOfWorkParticipant<TFactory>(this IServiceCollection services)
        where TFactory : class, IUnitOfWorkParticipantFactory
    {
        services.AddUnitOfWorkManager();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUnitOfWorkParticipantFactory, TFactory>());

        return services;
    }

    /// <summary>
    /// Registers the unit of work manager with the service collection.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <returns><paramref name="services"/>.</returns>
    public static IServiceCollection AddUnitOfWorkManager(this IServiceCollection services)
    {
        services.TryAddSingleton<UnitOfWorkManager.Impl>();
        services.TryAddScoped<IUnitOfWorkManager, UnitOfWorkManager>();

        return services;
    }

    private sealed class UnitOfWorkServiceFactory<TService>
        : IUnitOfWorkServiceFactory<TService>
        where TService : class
    {
        private static readonly ObjectFactory<TService> _objectFactory
            = ActivatorUtilities.CreateFactory<TService>([]);

        private static readonly Func<IServiceProvider, TService> _defaultFactory
            = static (s) => _objectFactory(s, []);

        private readonly Func<IServiceProvider, TService> _factory;

        public UnitOfWorkServiceFactory(Func<IServiceProvider, TService> factory)
        {
            _factory = factory;
        }

        public UnitOfWorkServiceFactory()
            : this(_defaultFactory)
        {
        }

        public TService Create(IServiceProvider services)
            => _factory(services);
    }

    private sealed class UnitOfWorkServiceFactory<TService, TImplementation>
        : IUnitOfWorkServiceFactory<TService>
        where TService : class
        where TImplementation : class, TService
    {
        private static readonly ObjectFactory<TImplementation> _factory
            = ActivatorUtilities.CreateFactory<TImplementation>([]);

        public TService Create(IServiceProvider services)
            => _factory(services, []);
    }
}
