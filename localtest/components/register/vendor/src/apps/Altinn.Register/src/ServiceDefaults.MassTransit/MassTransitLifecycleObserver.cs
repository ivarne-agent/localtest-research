using System.Diagnostics.CodeAnalysis;
using MassTransit;
using Microsoft.Extensions.Logging;

namespace Altinn.Authorization.ServiceDefaults.MassTransit;

/// <summary>
/// A lifecycle observer for MassTransit that implements <see cref="IBusLifetime"/>
/// to allow other services to wait for the bus to be ready.
/// </summary>
[ExcludeFromCodeCoverage]
internal sealed partial class MassTransitLifecycleObserver
    : IBusObserver
    , IBusLifetime
{
    private readonly ILogger<MassTransitLifecycleObserver> _logger;
    private readonly TaskCompletionSource<BusReady> _tcs = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MassTransitLifecycleObserver"/> class.
    /// </summary>
    public MassTransitLifecycleObserver(
        ILogger<MassTransitLifecycleObserver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    void IBusObserver.CreateFaulted(Exception exception)
    {
        _tcs.TrySetException(exception);
        Log.BusCreationFaulted(_logger, exception);
    }

    /// <inheritdoc/>
    void IBusObserver.PostCreate(IBus bus)
    {
        Log.BusPostCreate(_logger);
    }

    /// <inheritdoc/>
    Task IBusObserver.PostStart(IBus bus, Task<BusReady> busReady)
    {
        busReady.TryContinueWith(_tcs);
        Log.BusPostStart(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IBusObserver.PostStop(IBus bus)
    {
        Log.BusPostStop(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IBusObserver.PreStart(IBus bus)
    {
        Log.BusPreStart(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IBusObserver.PreStop(IBus bus)
    {
        Log.BusPreStop(_logger);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IBusObserver.StartFaulted(IBus bus, Exception exception)
    {
        _tcs.TrySetException(exception);
        Log.BusStartFaulted(_logger, exception);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task IBusObserver.StopFaulted(IBus bus, Exception exception)
    {
        _tcs.TrySetException(exception);
        Log.BusStopFaulted(_logger, exception);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    Task<BusReady> IBusLifetime.WaitForBus(CancellationToken cancellationToken)
        => _tcs.Task.WaitAsync(cancellationToken);

    private static partial class Log
    {
        [LoggerMessage(0, LogLevel.Debug, "Bus creation faulted.")]
        public static partial void BusCreationFaulted(ILogger logger, Exception exception);

        [LoggerMessage(1, LogLevel.Debug, "Bus post create.")]
        public static partial void BusPostCreate(ILogger logger);

        [LoggerMessage(2, LogLevel.Debug, "Bus post start.")]
        public static partial void BusPostStart(ILogger logger);

        [LoggerMessage(3, LogLevel.Debug, "Bus post stop.")]
        public static partial void BusPostStop(ILogger logger);

        [LoggerMessage(4, LogLevel.Debug, "Bus pre start.")]
        public static partial void BusPreStart(ILogger logger);

        [LoggerMessage(5, LogLevel.Debug, "Bus pre stop.")]
        public static partial void BusPreStop(ILogger logger);

        [LoggerMessage(6, LogLevel.Debug, "Bus start faulted.")]
        public static partial void BusStartFaulted(ILogger logger, Exception exception);

        [LoggerMessage(7, LogLevel.Debug, "Bus stop faulted.")]
        public static partial void BusStopFaulted(ILogger logger, Exception exception);
    }
}
