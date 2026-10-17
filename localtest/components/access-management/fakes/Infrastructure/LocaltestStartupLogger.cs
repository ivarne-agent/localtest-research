#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Altinn.AccessManagement.Localtest.Infrastructure;

/// <summary>
/// Logs the effective feature flags and the configuration provider each one came from at startup, so a flag
/// that is shadowed by a later source (JSON, environment) is visible in the container log.
/// </summary>
public sealed class LocaltestStartupLogger(IConfiguration configuration, ILogger<LocaltestStartupLogger> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (configuration is IConfigurationRoot root)
        {
            var lines = root.GetDebugView()
                .Split('\n')
                .Where(l => l.Contains("FeatureManagement", StringComparison.Ordinal) || l.TrimStart().StartsWith("AccessMgmt", StringComparison.Ordinal) || l.TrimStart().StartsWith("RightsDelegationApi", StringComparison.Ordinal))
                .Select(l => l.Trim());
            logger.LogInformation("Localtest: feature flags (value and provider):\n{Flags}", string.Join("\n", lines));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
