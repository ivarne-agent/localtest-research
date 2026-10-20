using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Receipt.Extensions;

/// <summary>
/// Extension methods for host configuration.
/// </summary>
public static class HostingExtensions
{
    /// <summary>
    /// Configures host shutdown to coordinate Kubernetes endpoint drain and ASP.NET Core request drain.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The configured web application builder.</returns>
    public static WebApplicationBuilder UseGracefulShutdown(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsDevelopment())
        {
            return builder;
        }

        var shutdownDelay = TimeSpan.FromSeconds(5);
        var shutdownTimeout = TimeSpan.FromSeconds(20);

        builder.Services.AddSingleton<IHostLifetime>(sp =>
            ActivatorUtilities.CreateInstance<AppHostLifetime>(sp, shutdownDelay)
        );

        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = shutdownTimeout);

        return builder;
    }

    private sealed class AppHostLifetime : IHostLifetime, IDisposable
    {
        private readonly ILogger<AppHostLifetime> _logger;
        private readonly IHostEnvironment _environment;
        private readonly IHostApplicationLifetime _applicationLifetime;
        private readonly TimeSpan _delay;
        private IDisposable[] _disposables;

        public AppHostLifetime(
            ILogger<AppHostLifetime> logger,
            IHostEnvironment environment,
            IHostApplicationLifetime applicationLifetime,
            TimeSpan delay
        )
        {
            _logger = logger;
            _environment = environment;
            _applicationLifetime = applicationLifetime;
            _delay = delay;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task WaitForStartAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(!_environment.IsDevelopment(), "We don't need graceful shutdown in development environments");

            PosixSignalRegistration sigint = null;
            PosixSignalRegistration sigquit = null;
            PosixSignalRegistration sigterm = null;

            try
            {
#pragma warning disable CA2000 // Ownership is transferred to _disposables and disposed in Dispose().
                sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, HandleSignal);
                sigquit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, HandleSignal);
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, HandleSignal);
#pragma warning restore CA2000
                _disposables = new IDisposable[] { sigint, sigquit, sigterm };
            }
            catch
            {
                TryDispose(sigint);
                TryDispose(sigquit);
                TryDispose(sigterm);
                throw;
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            foreach (IDisposable disposable in _disposables ?? Array.Empty<IDisposable>())
            {
                TryDispose(disposable);
            }
        }

        private void HandleSignal(PosixSignalContext ctx)
        {
            _logger.LogInformation("Received shutdown signal: {Signal}, delaying shutdown", ctx.Signal);
            ctx.Cancel = true;

            _ = Task.Delay(_delay)
                .ContinueWith(
                    _ =>
                    {
                        _logger.LogInformation("Starting host shutdown...");
                        _applicationLifetime.StopApplication();
                    },
                    TaskScheduler.Default
                );
        }

        private void TryDispose(IDisposable disposable)
        {
            try
            {
                disposable?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during disposal of {Type}", disposable?.GetType().FullName);
            }
        }
    }
}
