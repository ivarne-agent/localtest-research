#nullable enable
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Altinn.Platform.Authorization.Localtest;

/// <summary>
/// The PDP's telemetry in localtest (TODO.md task 53), called from the <c>#else</c> branch in <c>Program.cs</c> where
/// production sets up the classic Application Insights SDK and its metrics-only Azure Monitor pipeline. Traces, metrics
/// and logs go over OTLP exactly when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set; otherwise
/// nothing is registered, as before.
/// </summary>
/// <remarks>
/// The host has no OpenTelemetry pipeline of its own, so this builds one from the packages that
/// <c>Altinn.Authorization.ServiceDefaults</c> brings in transitively (1.15.x): ASP.NET Core and HttpClient
/// instrumentation for traces and metrics, and the production <c>DecisionTelemetry</c> meter, which <c>Program.cs</c>
/// registers on whatever meter provider exists. The OpenTelemetry equivalents of production's Application Insights
/// additions: <c>service.name</c> <c>platform-authorization</c> (the role name <c>CustomTelemetryInitializer</c> sets;
/// <c>OTEL_SERVICE_NAME</c> wins when set) and no spans for <c>/health</c> (<c>HealthTelemetryFilter</c>).
/// <c>IdentityTelemetryFilter</c>'s user, party and organisation properties are left out.
/// </remarks>
public static class LocaltestAuthorizationTelemetry
{
    public const string ServiceName = "platform-authorization";

    public static IServiceCollection AddLocaltestTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            return services;
        }

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !IsHealth(context.Request.Path))
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            // The environment detector after the resource above lets OTEL_SERVICE_NAME win (TODO.md task 53).
            .ConfigureResource(resource => resource.AddEnvironmentVariableDetector())
            .UseOtlpExporter();

        // The exporter's HttpClient logs every export at Information; keep warnings.
        services.AddLogging(logging => logging.AddFilter("System.Net.Http.HttpClient.Otlp", LogLevel.Warning));

        return services;
    }

    private static bool IsHealth(PathString path) => path.StartsWithSegments("/health");
}
