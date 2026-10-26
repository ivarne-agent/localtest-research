using Altinn.DialogportenAdapter.WebApi.Common.Health;
using Localtest.Shared.Otlp;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Altinn.DialogportenAdapter.WebApi.Localtest;

/// <summary>
/// The adapter's telemetry in localtest, called from the <c>#else</c> of production's <c>ConfigureTelemetry</c> in
/// <c>Common/Extensions/ServiceCollectionExtensions.cs</c> (TODO.md task 53). Production has a pipeline only with
/// Application Insights, and <c>UseAzureMonitor</c> brings the instrumentation itself. Here the same pipeline (the
/// health-check filter, the name, ASP.NET Core and HttpClient traces and metrics) is built exactly when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set and exports over OTLP from the standard variables; nothing is registered
/// otherwise.
/// </summary>
internal static class LocaltestAdapterTelemetry
{
    public static IServiceCollection AddLocaltestAdapterTelemetry(IServiceCollection services, IConfiguration configuration)
    {
        if (!LocaltestOtlp.IsEnabled(configuration))
        {
            return services;
        }

        services
            .AddTransient<HealthCheckFilterProcessor>()
            .ConfigureOpenTelemetryTracerProvider((sp, builder) =>
                builder.AddProcessor(sp.GetRequiredService<HealthCheckFilterProcessor>()))
            .AddOpenTelemetry()
            .ConfigureResource(x => x.AddAttributes([
                new("service.name", "platform-dialogporten-adapter")
            ]))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .AddLocaltestOtlp(configuration);

        return services;
    }
}
