# Resource Registry: Application Insights to OpenTelemetry (2026-10-26)

Upstream `Altinn/altinn-resource-registry` at `a3422419`, `src/apps/Altinn.ResourceRegistry/src/`.

**Classification: trivial** upstream for the host (it already exports over OTLP when the variable is set); the
family-wide improvement in ServiceDefaults is small. **Localtest: done** (task 53, the name only).

## Telemetry today (all four ServiceDefaults hosts alike)

`AltinnHost.CreateWebApplicationBuilder("resource-registry", ...)` (`ResourceRegistryHost.cs:45`) runs `AddAltinnServiceDefaults` from
`Altinn.Authorization.ServiceDefaults` 6.0.0 (resolved OpenTelemetry 1.17.0, exporter 1.17.0; source: `Altinn/altinn-authorization-utils`; decompiled, `ConfigureOpenTelemetry`
and `AddOpenTelemetryExporters` identical in 5.5.0 and 6.0.0):

- resource: `AltinnServiceResourceDetector` sets `service.name` `resource-registry`, `service.namespace` `altinn`,
  `service.instance.id` and the Kubernetes attributes; registered after the SDK's environment detector, so it overrides
  `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES`;
- logs: `WithLogging` (formatted message, scopes);
- metrics: runtime, ASP.NET Core, Kestrel and `System.Net.Http` meters;
- traces: ASP.NET Core (health and alive paths filtered, enriched from the request), HttpClient, a tail-sampling
  processor (`ErrorTailSampler`) and the sampler from `Altinn:Telemetry:Sampling`;
- exporters: **`if OTEL_EXPORTER_OTLP_ENDPOINT` is set**, `AddOtlpExporter` per signal (endpoint, protocol, headers from
  the standard variables; `/v1/<signal>` appended for `http/protobuf`); **`else if`** Application Insights is enabled
  (the default) and `ConnectionStrings:ApplicationInsights`, `ApplicationInsights:ConnectionString` or
  `ApplicationInsights:InstrumentationKey` is set, `UseAzureMonitor`; otherwise nothing.

The host adds Npgsql tracing through `AddAltinnPostgresDataSource` and nothing of its own. Classic Application
Insights leftovers: `Microsoft.ApplicationInsights.AspNetCore`, `...WindowsServer.TelemetryChannel` and
`Microsoft.Extensions.Logging.ApplicationInsights` are referenced; the only code use is an unused
`using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;` in `Utils/AuthorizationUtil.cs`; the
`ApplicationInsights` sections in `appsettings*.json` are log levels for a provider that is never added.

So the host already follows the stack's rule except for the name: OTLP exactly when the variable is set, nothing
otherwise, but `OTEL_SERVICE_NAME` is ignored.

## Upstream change

**Classification: trivial** for this host (nothing in the host itself has to change for "OpenTelemetry first").
Cleanup upstream (trivial): drop the three Application Insights package references, the stray `using static` and the log-level sections.

For the family as a whole, one **small** change in `Altinn/altinn-authorization-utils` (`AltinnServiceDefaultsExtensions`,
one package release, then a version bump in each host):
1. Make the two exporters independent (`if` OTLP, `if` Azure Monitor) instead of `else if`, so a deployment can send to
   both while moving from Application Insights to a collector; today the variable silently turns Azure Monitor off.
2. Let the standard variables win over the detector: `resource.AddEnvironmentVariableDetector()` after
   `AltinnServiceResourceDetector` (in both `ConfigureResource` calls; the tracer provider adds the detector a second
   time), or have the detector skip `service.name` when `OTEL_SERVICE_NAME` is set.
Risk: low; (1) changes behaviour only where both are configured, (2) only where `OTEL_SERVICE_NAME` is set.

## Localtest change (done)

No vendored file changes. `AddLocaltestResourceRegistry` (`fakes/LocaltestResourceRegistryExtensions.cs`, called from the existing `#if LOCALTEST` site `ResourceRegistryHost.cs:191-193`) calls
`LocaltestOtlp.AddLocaltestOtlpResource(services.AddOpenTelemetry(), builder.Configuration)` (`fakes/Otlp/`, a copy of
`../shared/fakes/Otlp/`): with the variable set it adds the environment detector after ServiceDefaults' resource, so
`OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` win; without it nothing. Not `AddLocaltestOtlp`: `UseOtlpExporter()`
throws next to the signal-specific exporters. Under studioctl the variable is the manifest name, `resource-registry`, the same as
the host's own, so the visible difference is only for another runner's name (Aspire, a manual run).

Checks (2026-10-26, no containers):
- `dotnet build -c Release` with the overrides (as the Dockerfile): 0 errors, no warnings in `fakes/` (10 upstream).
- Not run as a process: the host needs Postgres at start (Yuniql migrations). The same call is verified in Authentication and Register.
- `components/shared/Otlp.Tests -p:OtelVersion=1.17.0`: 9/9.

Still to run: `podman build -t localtest-resource-registry:dev localtest/components/resource-registry`, `smoke-test.sh` (plus the
OTLP span check with `--with monitoring`).
