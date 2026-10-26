#nullable enable
using Microsoft.Extensions.Configuration;
using OpenTelemetry;
using OpenTelemetry.Resources;

namespace Localtest.Shared.Otlp;

/// <summary>
/// The stack's telemetry rule (TODO.md task 53): a component sends traces, metrics and logs over OTLP exactly when
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> is set, with the protocol from <c>OTEL_EXPORTER_OTLP_PROTOCOL</c> and the name from
/// <c>OTEL_SERVICE_NAME</c>, and sends nothing otherwise. A component calls this on the OpenTelemetry builder it already
/// has, from the <c>#else</c> branch where production configures Azure Monitor or Application Insights.
/// </summary>
/// <remarks>
/// <para>Uses <c>UseOtlpExporter()</c>, which turns on all three signals, reads the standard variables from the host's
/// configuration (the environment) and appends <c>/v1/traces</c>, <c>/v1/metrics</c> and <c>/v1/logs</c> to the endpoint
/// for <c>http/protobuf</c>. It can be called once per service collection and not next to a signal-specific
/// <c>AddOtlpExporter</c>, so it is not for a host that already exports OTLP itself (the
/// <c>Altinn.Authorization.ServiceDefaults</c> hosts, Dialogporten, the workflow engine).</para>
/// <para>Most components hard-code <c>service.name</c> in their resource, which overrides <c>OTEL_SERVICE_NAME</c>
/// (a later resource wins). With the variable set this adds the environment-variable detector again, after the
/// component's own resource, so <c>OTEL_SERVICE_NAME</c> and <c>OTEL_RESOURCE_ATTRIBUTES</c> win when they are set and
/// the component's name stays when they are not. Call it after the component's own <c>ConfigureResource</c>. A host that
/// exports by itself gets only this part from <see cref="AddLocaltestOtlpResource"/>.</para>
/// </remarks>
public static class LocaltestOtlp
{
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
        return builder;
    }

    /// <summary>
    /// Only the resource half of <see cref="AddLocaltestOtlp"/>, for a host that already exports OTLP itself when the
    /// variable is set but hard-codes <c>service.name</c> (the <c>Altinn.Authorization.ServiceDefaults</c> hosts): with
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> set, <c>OTEL_SERVICE_NAME</c> and <c>OTEL_RESOURCE_ATTRIBUTES</c> win over the
    /// host's own resource. Call it after the host's own resource configuration. Nothing otherwise.
    /// </summary>
    public static IOpenTelemetryBuilder AddLocaltestOtlpResource(this IOpenTelemetryBuilder builder, IConfiguration configuration)
    {
        if (IsEnabled(configuration))
        {
            builder.ConfigureResource(resource => resource.AddEnvironmentVariableDetector());
        }

        return builder;
    }

    public static bool IsEnabled(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[EndpointVariable]);
}
