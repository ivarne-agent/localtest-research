using Altinn.Common.AccessTokenClient.Services;
using Altinn.Register.Core.Ccr;
using Altinn.Register.Core.ExternalRoles;
using Altinn.Register.Core.Location;
using Altinn.Register.Core.RateLimiting;
using Altinn.Register.Tests.IntegrationTests.Utils;
using Altinn.Register.Tests.Mocks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Altinn.Register.Tests.TestingControllers.Utils;

public abstract class BaseControllerTests
    : IClassFixture<WebApplicationFixture>
    , IAsyncLifetime
{
    private readonly WebApplicationFixture _webApplicationFixture;

    private WebApplicationFactory<Program>? _webApp;
    private IServiceProvider? _services;
    private AsyncServiceScope _scope;

    protected CancellationToken CancellationToken
        => TestContext.Current.CancellationToken;

    protected BaseControllerTests(WebApplicationFixture webApplicationFixture)
    {
        _webApplicationFixture = webApplicationFixture;
    }

    protected virtual HttpClient CreateClient()
        => _webApp!.CreateClient();

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
        services.AddSingleton<IAccessTokenGenerator, TestAccessTokenGenerator>();
        services.AddSingleton<IRateLimitProvider, MockRateLimitProvider>();
        services.TryAddSingleton<IExternalRoleDefinitionPersistence, MockExternalRoleDefinitionPersistence>();
        services.AddSingleton<ILocationLookupProvider, MockLocationLookupProvider>();
        services.AddScoped<ICcrUpdateFederator, NoOpCcrUpdateFederator>();
    }

    protected virtual void ConfigureTestConfiguration(IConfigurationBuilder builder)
    {
    }

    protected virtual ValueTask Initialize(IServiceProvider services)
    {
        return ValueTask.CompletedTask;
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await DisposeAsync();
        if (_scope is { } scope)
        {
            await scope.DisposeAsync();
        }

        if (_services is IAsyncDisposable iad)
        {
            await iad.DisposeAsync();
        }
        else if (_services is IDisposable id)
        {
            id.Dispose();
        }

        if (_webApp is { } webApp)
        {
            await webApp.DisposeAsync();
        }
    }

    async ValueTask IAsyncLifetime.InitializeAsync()
    {
        _webApp = _webApplicationFixture.CreateServer(
            configureConfiguration: config =>
            {
                ConfigureTestConfiguration(config);
            },
            configureServices: services =>
            {
                ConfigureTestServices(services);
            });

        _services = _webApp.Services;
        _scope = _services.CreateAsyncScope();

        await Initialize(_services);
    }

    protected virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
