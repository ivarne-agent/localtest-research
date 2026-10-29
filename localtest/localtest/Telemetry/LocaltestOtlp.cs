#nullable enable
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Localtest.Shared.Otlp;

/// <summary>
/// The stack's telemetry rule (TODO.md task 53): a component sends traces, metrics and logs over OTLP exactly when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, with the protocol from <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> and the name from
/// <c>OTEL_SERVICE_NAME</c>, and sends nothing otherwise. The front door's helper, and the reference for the rule the
/// components inline in the <c>#else</c> branch where production configures Azure Monitor or Application Insights
/// (task 63); <c>components/shared/Otlp.Tests</c> tests this copy.
/// </summary>
/// <remarks>
/// <para>Uses <c>UseOtlpExporter()</c>, which turns on all three signals, reads the standard variables from the host's
/// configuration (the environment) and appends <c>/v1/traces</c>, <c>/v1/metrics</c> and <c>/v1/logs</c> to the endpoint
/// for <c>http/protobuf</c>. It can be called once per service collection and not next to a signal-specific
/// <c>AddOtlpExporter</c>, so it is not for a host that already exports OTLP itself (Dialogporten, the workflow engine,
/// the <c>Altinn.Authorization.ServiceDefaults</c> hosts).</para>
/// <para>Most components hard-code <c>service.name</c> in their resource, which overrides <c>OTEL_SERVICE_NAME</c>
/// (a later resource wins). With the variable set this adds the environment-variable detector again, after the
/// component's own resource, so <c>OTEL_SERVICE_NAME</c> and <c>OTEL_RESOURCE_ATTRIBUTES</c> win when they are set and
/// the component's name stays when they are not. Call it after the component's own <c>ConfigureResource</c>.</para>
/// </remarks>
public static class LocaltestOtlp
{
    /// <summary>The standard variable whose presence turns exporting on.</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// OTLP for all signals when <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set in <paramref name="configuration"/>; nothing
    /// otherwise.
    /// </summary>
    public static IOpenTelemetryBuilder AddLocaltestOtlp(this IOpenTelemetryBuilder builder, IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return builder;
        }

        builder.AddLocaltestOtlpResource(configuration);
        builder.UseOtlpExporter();
        // The exporters' IHttpClientFactory clients log every export at Information (http/protobuf); keep warnings.
        builder.Services.AddLogging(logging => logging.AddFilter("System.Net.Http.HttpClient.Otlp", LogLevel.Warning));
        return builder;
    }

    /// <summary>
    /// Only the resource half of <see cref="AddLocaltestOtlp"/>: with <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set,
    /// <c>OTEL_SERVICE_NAME</c> and <c>OTEL_RESOURCE_ATTRIBUTES</c> win over the host's own resource. Call it after the
    /// host's own resource configuration. Nothing otherwise. Used by <see cref="AddLocaltestOtlp"/>; its one consumer
    /// outside it, the <c>Altinn.Authorization.ServiceDefaults</c> hosts, went in task 61 (they honour the variables
    /// themselves).
    /// </summary>
    public static IOpenTelemetryBuilder AddLocaltestOtlpResource(this IOpenTelemetryBuilder builder, IConfiguration configuration)
    {
        if (IsEnabled(configuration))
        {
            builder.ConfigureResource(resource => resource.AddEnvironmentVariableDetector());
        }

        return builder;
    }

    /// <summary>
    /// Whether <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set (not blank). A per-signal endpoint
    /// (<c>OTEL_EXPORTER_OTLP_TRACES_ENDPOINT</c> and the others) alone does not count: <c>UseOtlpExporter()</c> always
    /// turns on all three signals, and a signal without an endpoint would go to the SDK's default, <c>localhost:4317</c>.
    /// With the general endpoint set, a per-signal one still overrides it for its signal.
    /// </summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[EndpointVariable]);
}
