using Altinn.Register.TestUtils.Tracing;

namespace Altinn.Register.TestUtils;

/// <summary>
/// Base class for tests.
/// </summary>
public abstract class TestBase
    : IAsyncLifetime
{
    private readonly ActivityCollector _collector;

    protected TestBase()
    {
        _collector = new ActivityCollector();
    }

    protected CancellationToken CancellationToken
        => TestContext.Current.CancellationToken;

    protected virtual ValueTask InitializeAsync()
        => ValueTask.CompletedTask;

    protected virtual ValueTask DisposeAsync()
    {
        _collector.Dispose();

        return ValueTask.CompletedTask;
    }

    ValueTask IAsyncDisposable.DisposeAsync()
        => DisposeAsync();

    ValueTask IAsyncLifetime.InitializeAsync()
        => InitializeAsync();
}
