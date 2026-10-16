using System.Diagnostics;
using Altinn.Authorization.ServiceDefaults;
using Altinn.Authorization.TestUtils.Http;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Time.Testing;

namespace Altinn.Register.TestUtils;

/// <summary>
/// Base class for tests that need a generic host.
/// </summary>
public abstract class HostTestBase
    : ServicesTestBase
{
    private readonly FakeHttpHandlers _httpHandlers = new();
    private readonly FakeTimeProvider _timeProvider = new();

    private IHost? _host;

    /// <summary>
    /// Gets the fake time provider.
    /// </summary>
    protected FakeTimeProvider TimeProvider => _timeProvider;

    /// <summary>
    /// Gets the fake HTTP handlers registered with the host.
    /// </summary>
    protected FakeHttpHandlers HttpHandlers => _httpHandlers;

    /// <summary>
    /// Gets a value indicating whether logging should be disabled.
    /// </summary>
    protected virtual bool DisableLogging => !Debugger.IsAttached;

    /// <summary>
    /// Gets the xUnit test output helper to mirror logs to.
    /// </summary>
    protected virtual ITestOutputHelper? TestOutputHelper => null;

    /// <summary>
    /// Creates and initializes the host.
    /// </summary>
    protected virtual async ValueTask<IHost> InitializeHost()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection([
            new("Logging:LogLevel:Default", "Warning"),
            new("Altinn:IsTest", "true"),
        ]);

        if (DisableLogging)
        {
            configuration.AddInMemoryCollection([
                new(AltinnPreStartLogger.DisableConfigKey, "true"),
            ]);
        }

        await Configure(configuration);

        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
        {
            ApplicationName = "test",
            EnvironmentName = "Development",
            Configuration = configuration,
        });

        builder.AddAltinnServiceDefaults("register");
        if (DisableLogging)
        {
            builder.Logging.ClearProviders();
        }

        if (TestOutputHelper is { } output)
        {
            builder.Services.AddSingleton<ILoggerProvider>(services =>
            {
                var scopeProvider = services.GetService<IExternalScopeProvider>();
                var formatter = services.GetServices<ConsoleFormatter>().FirstOrDefault(f => f.Name == ConsoleFormatterNames.Simple);
                if (formatter is null)
                {
                    ThrowHelper.ThrowInvalidOperationException($"The '{ConsoleFormatterNames.Simple}' console formatter is not registered.");
                }

                return new XunitLoggerProvider(output, scopeProvider, formatter);
            });
        }

        await ConfigureHost(builder);

        return builder.Build();
    }

    /// <inheritdoc/>
    protected override sealed async ValueTask<IServiceProvider> InitializeServiceProvider()
    {
        _host = await InitializeHost();
        await _host.StartAsync();

        return _host.Services;
    }

    /// <summary>
    /// Configures the host before it is built.
    /// </summary>
    protected virtual async ValueTask ConfigureHost(IHostApplicationBuilder builder)
    {
        builder.Services.TryAddSingleton(_timeProvider);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.TryAddSingleton<TimeProvider>(services => services.GetRequiredService<FakeTimeProvider>());
        builder.Services.AddFakeHttpHandlers(_httpHandlers);

        await ConfigureServices(builder.Services);
    }

    /// <summary>
    /// Configures the host configuration.
    /// </summary>
    protected virtual ValueTask Configure(IConfigurationManager configuration)
        => ValueTask.CompletedTask;

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsync()
    {
        if (_host is { } host)
        {
            await host.StopAsync();
            if (host is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                host.Dispose();
            }
        }

        await base.DisposeAsync();
    }

    /// <summary>
    /// Advances the fake clock in real time until disposed.
    /// </summary>
    protected IDisposable UpdateTimerRealtime(TimeSpan interval, TimeSpan stepSize)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(
            GetRequiredService<IHostApplicationLifetime>().ApplicationStopping);
        var ct = cts.Token;

        var systemTime = System.TimeProvider.System;
        var previousTimestamp = systemTime.GetTimestamp();
        var timer = systemTime.CreateTimer(
            _ =>
            {
                var now = systemTime.GetTimestamp();
                var elapsed = systemTime.GetElapsedTime(previousTimestamp, now);
                previousTimestamp = now;

                while (!ct.IsCancellationRequested && elapsed >= interval)
                {
                    TimeProvider.Advance(stepSize);
                    elapsed -= interval;
                }
            },
            state: null,
            dueTime: TimeSpan.Zero,
            period: interval);

        ct.Register(static t => ((ITimer)t!).Dispose(), timer);

        return new DisposeAction(() =>
        {
            cts.Cancel();
            cts.Dispose();
        });
    }

    private sealed class DisposeAction(Action dispose)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
                dispose = null!;
            }
        }
    }

    private sealed class XunitLoggerProvider(
        ITestOutputHelper output,
        IExternalScopeProvider? scopeProvider,
        ConsoleFormatter formatter)
        : ILoggerProvider
    {
        private readonly Lock _lock = new();

        public ILogger CreateLogger(string categoryName)
            => new Logger(categoryName, this, output, scopeProvider, formatter);

        public void Dispose()
        {
        }

        private sealed class Logger(
            string categoryName,
            XunitLoggerProvider provider,
            ITestOutputHelper output,
            IExternalScopeProvider? scopeProvider,
            ConsoleFormatter formatter)
            : ILogger
        {
            [ThreadStatic]
            private static StringWriter? _threadWriter;

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => scopeProvider?.Push(state) ?? NoopDisposable.Instance;

            public bool IsEnabled(LogLevel logLevel)
                => logLevel != LogLevel.None;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> messageFormatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                Guard.IsNotNull(messageFormatter);

                _threadWriter ??= new StringWriter();
                LogEntry<TState> logEntry = new(logLevel, categoryName, eventId, state, exception, messageFormatter);
                formatter.Write(in logEntry, scopeProvider, _threadWriter);

                var builder = _threadWriter.GetStringBuilder();
                if (builder.Length == 0)
                {
                    return;
                }

                string message = builder.ToString();
                builder.Clear();
                if (builder.Capacity > 1024)
                {
                    builder.Capacity = 1024;
                }

                lock (provider._lock)
                {
                    output.WriteLine(message);
                }
            }
        }
    }

    private sealed class NoopDisposable
        : IDisposable
    {
        public static IDisposable Instance { get; } = new NoopDisposable();

        public void Dispose()
        {
        }
    }
}
