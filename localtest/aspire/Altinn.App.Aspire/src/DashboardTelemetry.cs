using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace Altinn.App.Aspire;

/// <summary>
/// Every localtest container sends its traces, metrics and logs to the Aspire dashboard, as studioctl's runner points
/// them at its collector while the monitoring group runs (the group the package ignores). Aspire's
/// <c>WithOtlpExporter()</c> sets the standard variables a component exports by (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>,
/// on the container's network, <c>OTEL_EXPORTER_OTLP_PROTOCOL</c>, <c>OTEL_EXPORTER_OTLP_HEADERS</c> with the dashboard's
/// API key, <c>OTEL_RESOURCE_ATTRIBUTES</c> and, in Development, short batch delays); the package then sets
/// <see cref="Defaults" /> as studioctl does, and the container's own manifest envs win over both.
/// </summary>
internal static class DashboardTelemetry
{
    /// <summary>The variable that turns a component's export on (the components' rule, task 53).</summary>
    public const string EndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>The service name: the container's manifest name, as studioctl's runner sets it.</summary>
    public const string ServiceNameVariable = "OTEL_SERVICE_NAME";

    /// <summary>The metric export interval in milliseconds.</summary>
    public const string MetricExportIntervalVariable = "OTEL_METRIC_EXPORT_INTERVAL";

    /// <summary>
    /// studioctl's 5 s (Aspire's 1 s in Development would quintuple the metric exports of some twenty containers;
    /// the SDKs' default is 60 s).
    /// </summary>
    public const string MetricExportInterval = "5000";

    /// <summary>Aspire's switch for its container tunnel (<c>KnownConfigNames.EnableContainerTunnel</c>, default on).</summary>
    public const string ContainerTunnelSetting = "ASPIRE_ENABLE_CONTAINER_TUNNEL";

    /// <summary>The dashboard's OTLP endpoints (<c>KnownEndpointNames</c> in Aspire.Hosting, internal).</summary>
    public static readonly string[] OtlpEndpointNames = ["otlp-grpc", "otlp-http"];

    /// <summary>
    /// What the package sets on a container that got the dashboard's endpoint, over Aspire's values: the service name
    /// (Aspire's is a DCP template that renders the resource name, the same name) and studioctl's metric interval.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> Defaults(string container) =>
        [new(ServiceNameVariable, container), new(MetricExportIntervalVariable, MetricExportInterval)];

    /// <summary>
    /// Sets <see cref="Defaults" /> when Aspire's exporter callback, which runs before, set the endpoint: with a
    /// dashboard, or with an OTLP endpoint in the configuration (Aspire falls back to <c>http://localhost:18889</c>).
    /// </summary>
    public static void Apply(string container, IDictionary<string, object> environment)
    {
        if (!environment.ContainsKey(EndpointVariable))
        {
            return;
        }
        foreach (var (key, value) in Defaults(container))
        {
            environment[key] = value;
        }
    }

    /// <summary>
    /// Whether the dashboard's OTLP endpoints must listen on every interface for the containers: with Aspire's container
    /// tunnel off (as on Podman 5.7, where its image does not build), a container reaches the host through the gateway
    /// of its network (<c>host.containers.internal</c>, <c>host.docker.internal</c>), and the dashboard's endpoints are
    /// proxied by DCP on loopback only.
    /// </summary>
    public static bool NeedsHostWideOtlp(IConfiguration configuration) =>
        !configuration.GetValue(ContainerTunnelSetting, defaultValue: true);

    /// <summary>The AppHost's OTLP API key, which Aspire puts in every exporter's <c>OTEL_EXPORTER_OTLP_HEADERS</c>.</summary>
    public const string OtlpApiKeySetting = "AppHost:OtlpApiKey";

    /// <summary>The file in the package's data directory that keeps the key between runs.</summary>
    public const string OtlpApiKeyFileName = "otlp-api-key";

    /// <summary>
    /// Keeps the OTLP API key the same from run to run, so the persistent containers, whose environment carries it, are
    /// reused instead of recreated on every start. Aspire generates a key when none is configured and saves it in the
    /// AppHost's user secrets for that reason, but a file-based AppHost (<c>apphost.cs</c>, Aspire 13.6.2) does not
    /// read them back, so the key changes every run. A key only in memory is that generated one: it is replaced by the
    /// one kept in <paramref name="dataDirectory" /> (written owner-only the first time). A key from any other source
    /// (user secrets read back, the environment, appsettings) is the AppHost's own and is left alone, as is a
    /// dashboard without a key (unsecured).
    /// </summary>
    /// <returns>Whether the kept key is now the AppHost's.</returns>
    public static bool KeepOtlpApiKey(IConfigurationManager configuration, string dataDirectory)
    {
        if (configuration[OtlpApiKeySetting] is null)
        {
            return false;
        }
        var configured = ((IConfigurationRoot)configuration).Providers.Any(p =>
            p is not MemoryConfigurationProvider && p.TryGet(OtlpApiKeySetting, out _)
        );
        if (configured)
        {
            return false;
        }
        var path = Path.Combine(dataDirectory, OtlpApiKeyFileName);
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(dataDirectory);
            var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                create.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            using var writer = new StreamWriter(new FileStream(path, create));
            writer.Write(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)));
        }
        configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { [OtlpApiKeySetting] = File.ReadAllText(path).Trim() }
        );
        return true;
    }

    /// <summary>
    /// Makes the dashboard's OTLP endpoints reachable from the containers when the tunnel is off: unproxied, on
    /// <c>0.0.0.0</c>, so the dashboard's own Kestrel binds the port on every interface, including the bridge DCP
    /// creates for the containers' network after its proxies started (DCP's proxy for <c>0.0.0.0</c> binds the
    /// interfaces that exist when it starts). The containers still get the address Aspire resolves for their network,
    /// the host apps <c>localhost</c>. The OTLP port is then open on the machine's network too, behind the dashboard's
    /// API key unless the dashboard runs unsecured.
    /// </summary>
    internal sealed class HostWideOtlp(IConfiguration configuration) : IDistributedApplicationEventingSubscriber
    {
        public Task SubscribeAsync(
            IDistributedApplicationEventing eventing,
            DistributedApplicationExecutionContext executionContext,
            CancellationToken cancellationToken
        )
        {
            if (executionContext.IsRunMode && NeedsHostWideOtlp(configuration))
            {
                // Registered after Aspire's dashboard subscriber, so this runs after it has set the dashboard's endpoints.
                eventing.Subscribe<BeforeStartEvent>(
                    (e, _) =>
                    {
                        Configure(e.Model.Resources);
                        return Task.CompletedTask;
                    }
                );
            }
            return Task.CompletedTask;
        }

        /// <summary>Turns the dashboard's OTLP endpoints, when there is a dashboard, into host-wide unproxied ones.</summary>
        public static void Configure(IEnumerable<IResource> resources)
        {
            if (
                resources.FirstOrDefault(r =>
                    string.Equals(r.Name, DashboardTraceLinks.DashboardResource, StringComparison.OrdinalIgnoreCase)
                )
                is not { } dashboard
            )
            {
                return;
            }
            foreach (
                var endpoint in dashboard
                    .Annotations.OfType<EndpointAnnotation>()
                    .Where(e => OtlpEndpointNames.Contains(e.Name, StringComparer.Ordinal))
            )
            {
                endpoint.IsProxied = false;
                endpoint.TargetHost = "0.0.0.0";
            }
        }
    }
}
