using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

internal static partial class Log
{
    [LoggerMessage(LogLevel.Error, "A localtest container did not become healthy.")]
    public static partial void LogLocaltestFailed(this ILogger logger, Exception exception);

    [LoggerMessage(LogLevel.Information, "{Container} is healthy after {Seconds:F0} s")]
    public static partial void LogContainerHealthy(this ILogger logger, string container, double seconds);

    [LoggerMessage(LogLevel.Information, "{Name}: every container is healthy after {Seconds:F0} s")]
    public static partial void LogLocaltestHealthy(this ILogger logger, string name, double seconds);

    [LoggerMessage(LogLevel.Information, "{AppId}: {Operation}: {Answer}")]
    public static partial void LogProvisioned(this ILogger logger, string appId, string operation, string answer);

    [LoggerMessage(
        LogLevel.Warning,
        "{AppId}: {Operation}: {Answer}; the app keeps running, and the provision command registers it again"
    )]
    public static partial void LogProvisioningRefused(
        this ILogger logger,
        string appId,
        string operation,
        string answer
    );

    [LoggerMessage(
        LogLevel.Warning,
        "{AppId}: {Operation} failed ({Url}); the app keeps running, and the provision command registers it again"
    )]
    public static partial void LogProvisioningFailed(
        this ILogger logger,
        Exception exception,
        string appId,
        string operation,
        string url
    );

    [LoggerMessage(LogLevel.Information, "frontend dev server: {Answer}")]
    public static partial void LogDevServerRoute(this ILogger logger, string answer);

    [LoggerMessage(LogLevel.Warning, "frontend dev server: the route could not be registered ({Url})")]
    public static partial void LogDevServerRouteFailed(this ILogger logger, Exception exception, string url);

    [LoggerMessage(LogLevel.Warning, "{Warning}")]
    public static partial void LogCaBundleWarning(this ILogger logger, string warning);

    [LoggerMessage(LogLevel.Information, "{Command}: {Arguments}")]
    public static partial void LogRuntimeCommand(this ILogger logger, string command, string arguments);
}
