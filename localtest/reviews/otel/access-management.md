# Access Management: Application Insights to OpenTelemetry (2026-10-26)

Upstream `Altinn/altinn-auth` at `3d518103`, `src/apps/Altinn.AccessManagement/`.

**Classification: trivial** upstream for the host (it already exports over OTLP when the variable is set); the
family-wide improvement in ServiceDefaults is small. **Localtest: done** (task 53, the name only).

## Telemetry today (all four ServiceDefaults hosts alike)

`AltinnHost.CreateWebApplicationBuilder("access-management", ...)` (`AccessManagementHost.cs:69`) runs `AddAltinnServiceDefaults` from
`Altinn.Authorization.ServiceDefaults` 5.5.0 (resolved OpenTelemetry 1.19.1, exporter 1.15.3; source: `Altinn/altinn-authorization-utils`; decompiled, `ConfigureOpenTelemetry`
and `AddOpenTelemetryExporters` identical in 5.5.0 and 6.0.0):

- resource: `AltinnServiceResourceDetector` sets `service.name` `access-management`, `service.namespace` `altinn`,
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

Access Management differs in two ways. It passes `DisableApplicationInsights()` (`:69`) and adds its own
`UseAzureMonitor` (`AccessManagementHost.cs:88-108`) only outside Development and only with
`ApplicationInsights:InstrumentationKey`, with two trace processors (`ApiVersionRouteProcessor`, `NpgsqlProcessor` that
drops Npgsql spans under `Telemetry:NpgsqlMinimumDurationMs`); with both that key and the variable set it would export
to both. And it adds its own sources and meters: `AuthorizedPartiesTelemetry`, Core, the lease and pipeline libraries,
Npgsql (`libs/Altinn.Authorization.Host`). Classic Application Insights leftovers: `Microsoft.ApplicationInsights.AspNetCore`
and `Microsoft.Extensions.Logging.ApplicationInsights` are referenced, and `Health/HealthTelemetryFilter.cs`,
`Filters/IdentityTelemetryFilter.cs` and `Configuration/CustomTelemetryInitializer.cs` exist, but nothing registers them
(dead code).

So the host already follows the stack's rule except for the name: OTLP exactly when the variable is set, nothing
otherwise, but `OTEL_SERVICE_NAME` is ignored.

## Upstream change

**Classification: trivial** for this host (nothing in the host itself has to change for "OpenTelemetry first").
Cleanup upstream (small, low risk): delete the three unused classic-SDK classes and the two Application Insights
package references; optionally move the `UseAzureMonitor` block into ServiceDefaults' path (drop
`DisableApplicationInsights()`, keep the two processors as `ConfigureOpenTelemetryTracerProvider`), so the host has one
telemetry setup like its siblings.

For the family as a whole, one **small** change in `Altinn/altinn-authorization-utils` (`AltinnServiceDefaultsExtensions`,
one package release, then a version bump in each host):
1. Make the two exporters independent (`if` OTLP, `if` Azure Monitor) instead of `else if`, so a deployment can send to
   both while moving from Application Insights to a collector; today the variable silently turns Azure Monitor off.
2. Let the standard variables win over the detector: `resource.AddEnvironmentVariableDetector()` after
   `AltinnServiceResourceDetector` (in both `ConfigureResource` calls; the tracer provider adds the detector a second
   time), or have the detector skip `service.name` when `OTEL_SERVICE_NAME` is set.
Risk: low; (1) changes behaviour only where both are configured, (2) only where `OTEL_SERVICE_NAME` is set.

Localtest does not propose either (decided 2026-10-28, TODO.md task 61): neither has a local effect, see
[`../../upstreaming/authorization-utils.md`](../../upstreaming/authorization-utils.md), "Not needed".

## Localtest change (removed in task 61)

**Current state (2026-10-28):** no localtest telemetry code in this host. ServiceDefaults exports with the variable,
named `access-management` by its own detector, which is what studioctl and Aspire set as `OTEL_SERVICE_NAME`; `service.instance.id` is
the container's host name and `OTEL_RESOURCE_ATTRIBUTES` is ignored (accepted). The call below and the `fakes/Otlp/`
copy were removed in TODO.md task 61, which re-verified all three signals on the stack. What follows is the record of
task 53.

No vendored file changes. `AddLocaltestAccessManagement` (`fakes/LocaltestAccessManagementExtensions.cs`, called from the existing `#if LOCALTEST` site `AccessManagementHost.cs:168-170`) calls
`LocaltestOtlp.AddLocaltestOtlpResource(services.AddOpenTelemetry(), builder.Configuration)` (`fakes/Otlp/`, a copy of
`../shared/fakes/Otlp/`): with the variable set it adds the environment detector after ServiceDefaults' resource, so
`OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` win; without it nothing. Not `AddLocaltestOtlp`: `UseOtlpExporter()`
throws next to the signal-specific exporters. Under studioctl the variable is the manifest name, `access-management`, the same as
the host's own, so the visible difference is only for another runner's name (Aspire, a manual run).

Checks (2026-10-26, no containers):
- `dotnet build -c Release` with the overrides (as the Dockerfile): 0 errors, no warnings in `fakes/` (9 upstream).
- Not run as a process: the host needs Postgres at start (Yuniql migrations). The same call is verified in Authentication and Register.

Still to run: `podman build -t localtest-access-management:dev localtest/components/access-management`; the component has no
`smoke-test.sh`, so check a span on the stack with `--with monitoring` (`../../smoke/lib/traces.sh`, service `access-management`)
and run the Authorization and root smoke tests, which go through it.
