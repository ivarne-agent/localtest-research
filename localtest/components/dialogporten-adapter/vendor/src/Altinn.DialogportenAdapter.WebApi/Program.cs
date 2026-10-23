using System.Globalization;
using Altinn.DialogportenAdapter.WebApi;
using Altinn.DialogportenAdapter.WebApi.Common;
using Altinn.DialogportenAdapter.WebApi.Common.Extensions;

// Workaround issue in Wolverine on nb_NB causing intermittent 400 Bad Request errors due to
// attempts to auto-provision queues with a unicode negative sign
var safeCulture = (CultureInfo)CultureInfo.CurrentCulture.Clone();
safeCulture.NumberFormat.NegativeSign = "-";
CultureInfo.DefaultThreadCurrentCulture = safeCulture;
CultureInfo.CurrentCulture = safeCulture;

using var loggerFactory = CreateBootstrapLoggerFactory();
var bootstrapLogger = loggerFactory.CreateLogger<Program>();

try
{
    BuildAndRun(args);
}
catch (Exception e)
{
    LogApplicationTerminatedUnexpectedly(bootstrapLogger, e);
    throw;
}

return;

static void BuildAndRun(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
#if LOCALTEST
    // localtest: resolve other components by name (http://<service>/...) from services__<service>__http__0; a name
    // without an entry fails at once, a real host (a dot, an IP, localhost) is used as it is (no pass-through;
    // fakes/ServiceDiscovery, planned-components/service-discovery.md).
    global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services);
#endif

    builder.Logging
        .ClearProviders()
        .AddConsole();

    builder.Configuration
        .AddCoreClusterSettings()
        .AddAzureKeyVault()
        .AddLocalDevelopmentSettings(builder.Environment)
        .AddUserSecrets<DialogportenAdapterSettings>();

#if LOCALTEST
    Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.AddLocaltestConfiguration(builder.Configuration);
#endif

    builder.Services
        .ConfigureDialogportenAdapterServices(builder.Configuration, builder.Environment, new Clock())
        .ReplaceLocalDevelopmentResources(builder.Configuration, builder.Environment);
#if LOCALTEST
    Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.AddLocaltestAdapter(builder);
#endif

    using var app = builder
        .Build()
        .RegisterRoutes();
#if LOCALTEST
    Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.MapLocaltestRoutes(app);
#endif

    app.Run();
}

ILoggerFactory CreateBootstrapLoggerFactory() => LoggerFactory.Create(builder => builder
    .SetMinimumLevel(LogLevel.Warning)
    .AddSimpleConsole(options =>
    {
        options.IncludeScopes = true;
        options.UseUtcTimestamp = true;
        options.SingleLine = true;
        options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
    }));

partial class Program
{
    [LoggerMessage(LogLevel.Critical, "Application terminated unexpectedly")]
    static partial void LogApplicationTerminatedUnexpectedly(ILogger<Program> logger, Exception exception);
}
