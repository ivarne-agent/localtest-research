using Microsoft.Extensions.Logging;

namespace Altinn.App.Aspire;

internal static partial class Log
{
    [LoggerMessage(LogLevel.Error, "A localtest component did not become healthy.")]
    public static partial void LogLocaltestFailed(this ILogger logger, Exception exception);
}
