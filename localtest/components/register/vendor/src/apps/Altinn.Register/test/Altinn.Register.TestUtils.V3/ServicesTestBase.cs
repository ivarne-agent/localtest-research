using Microsoft.Extensions.DependencyInjection;

namespace Altinn.Register.TestUtils;

/// <summary>
/// Base class for tests that need services.
/// </summary>
public abstract class ServicesTestBase
    : TestBase
{
    private IServiceProvider? _serviceProvider;
    private AsyncServiceScope? _serviceScope;

    /// <summary>
    /// Gets the scoped service provider.
    /// </summary>
    public IServiceProvider Services => _serviceScope!.Value.ServiceProvider;

    /// <inheritdoc cref="ServiceProviderServiceExtensions.GetRequiredService{T}(IServiceProvider)"/>
    public T GetRequiredService<T>()
        where T : notnull
        => Services.GetRequiredService<T>();

    /// <summary>
    /// Initializes the root service provider for the test.
    /// </summary>
    protected abstract ValueTask<IServiceProvider> InitializeServiceProvider();

    /// <summary>
    /// Configures services before the provider is built.
    /// </summary>
    protected virtual ValueTask ConfigureServices(IServiceCollection services)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    protected override async ValueTask InitializeAsync()
    {
        _serviceProvider = await InitializeServiceProvider();
        _serviceScope = _serviceProvider.CreateAsyncScope();

        await base.InitializeAsync();
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsync()
    {
        if (_serviceScope is { } scope)
        {
            await scope.DisposeAsync();
        }

        if (_serviceProvider is IAsyncDisposable asyncProvider)
        {
            await asyncProvider.DisposeAsync();
        }
        else if (_serviceProvider is IDisposable provider)
        {
            provider.Dispose();
        }

        await base.DisposeAsync();
    }
}
