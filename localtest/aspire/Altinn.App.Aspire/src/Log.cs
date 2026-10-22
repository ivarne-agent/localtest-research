using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

internal static partial class Log
{
    [LoggerMessage(LogLevel.Error, "A localtest component did not become healthy.")]
    public static partial void LogLocaltestFailed(this ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "{Component} is healthy after {Seconds:F0} s")]
    public static partial void LogComponentHealthy(this ILogger logger, string component, double seconds);

    [LoggerMessage(LogLevel.Information, "{Name}: every component and the front door are healthy after {Seconds:F0} s")]
    public static partial void LogLocaltestHealthy(this ILogger logger, string name, double seconds);

    [LoggerMessage(LogLevel.Information, "{AppId}: {Operation}: {Message}")]
    public static partial void LogProvisioned(this ILogger logger, string appId, string operation, string message);

    [LoggerMessage(
        LogLevel.Warning,
        "{AppId}: {Operation} failed at {Step} ({Url}); the app keeps running, and the provision command runs it again"
    )]
    public static partial void LogProvisioningFailed(
        this ILogger logger,
        Exception exception,
        string appId,
        string operation,
        string step,
        string url
    );

    [LoggerMessage(LogLevel.Information, "{Command}: {Arguments}")]
    public static partial void LogRuntimeCommand(this ILogger logger, string command, string arguments);
}
