using System.Text.Json;
using Altinn.Authorization.ServiceDefaults.MassTransit;
using Altinn.Authorization.TestUtils.Http;
using Altinn.Register.Core.UnitOfWork;
using Altinn.Register.IntegrationTests.Fixtures;
using Altinn.Register.TestUtils;
using MassTransit.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Altinn.Register.IntegrationTests;

public abstract class IntegrationTestBase
    : TestBase
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    private TestWebApplication? _webApp;
    private AsyncServiceScope? _scope;
    private HttpClient? _client;
    private FakeHttpHandlers? _fakeHttpHandlers;
    private FakeTimeProvider? _timeProvider;
    private ITestHarness? _testHarness;
    private ICommandSender? _commandSender;

    protected JsonSerializerOptions JsonOptions
        => _jsonOptions;

    protected IServiceProvider Services
        => _scope!.Value.ServiceProvider;

    protected HttpClient HttpClient
        => _client!;

    protected Uri BaseUrl
        => _client!.BaseAddress!;

    protected FakeHttpHandlers FakeHttpHandlers
        => _fakeHttpHandlers!;

    protected FakeTimeProvider TimeProvider
        => _timeProvider!;

    protected ICommandSender CommandSender
        => _commandSender!;

    protected ITestHarness TestHarness
        => _testHarness!;

    protected IConfigurationManager Configuration
        => (IConfigurationManager)GetRequiredService<IConfiguration>();

    protected T GetRequiredService<T>()
        where T : notnull
        => Services.GetRequiredService<T>();

    protected async Task<T> Setup<T>(Func<IUnitOfWork, CancellationToken, Task<T>> setup)
    {
        var ct = CancellationToken;

        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        await using var uow = await uowManager.CreateAsync(activityName: $"setup {TestContext.Current.Test!.TestDisplayName}", cancellationToken: ct);
        var result = await setup(uow, ct);
        await uow.CommitAsync(ct);

        return result;
    }

    protected Task Setup(Func<IUnitOfWork, CancellationToken, Task> setup)
        => Setup<object?>(async (uow, ct) =>
        {
            await setup(uow, ct);
            return null;
        });

    protected async Task<T> Check<T>(Func<IUnitOfWork, CancellationToken, Task<T>> check)
    {
        var ct = CancellationToken;

        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        await using var uow = await uowManager.CreateAsync(activityName: $"check {TestContext.Current.Test!.TestDisplayName}", cancellationToken: ct);
        var result = await check(uow, ct);
        await uow.RollbackAsync(ct);

        return result;
    }

    protected Task Check(Func<IUnitOfWork, CancellationToken, Task> check)
        => Check<object?>(async (uow, ct) =>
        {
            await check(uow, ct);
            return null;
        });

    /// <summary>
    /// Allows derived tests to add or override configuration in the test host. Called while the
    /// host is being built.
    /// </summary>
    /// <param name="configuration">The <see cref="IConfigurationBuilder"/>.</param>
    protected virtual void ConfigureConfiguration(IConfigurationBuilder configuration)
    {
    }

    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        _webApp = await TestWebApplication.Create(ConfigureConfiguration);
        _scope = _webApp.Services.CreateAsyncScope();
        _client = _webApp.CreateClient();
        _timeProvider = _webApp.Services.GetRequiredService<FakeTimeProvider>();
        _fakeHttpHandlers = _webApp.Services.GetRequiredService<FakeHttpHandlers>();
        _testHarness = _webApp.Services.GetRequiredService<ITestHarness>();
        _commandSender = _scope.Value.ServiceProvider.GetRequiredService<ICommandSender>();
    }

    protected override async ValueTask DisposeAsync()
    {
        if (_fakeHttpHandlers is IDisposable fakehttpHandlers)
        {
            fakehttpHandlers.Dispose();
        }

        if (_client is { } client)
        {
            client.Dispose();
        }

        if (_scope is { } scope)
        {
            await scope.DisposeAsync();
        }

        if (_webApp is IAsyncDisposable webApp)
        {
            await webApp.DisposeAsync();
        }

        await base.DisposeAsync();
    }
}
