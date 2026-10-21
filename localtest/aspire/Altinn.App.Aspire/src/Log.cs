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
}
