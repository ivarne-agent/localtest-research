# Authentication: Application Insights to OpenTelemetry (2026-10-26)

Upstream `Altinn/altinn-auth` at `3d518103`, `src/apps/Altinn.Authentication/`.

**Classification: trivial** upstream for the host (it already exports over OTLP when the variable is set); the
family-wide improvement in ServiceDefaults is small. **Localtest: done** (task 53, the name only).

## Telemetry today (all four ServiceDefaults hosts alike)

`AltinnHost.CreateWebApplicationBuilder("authentication", ...)` (`AuthenticationHost.cs:68`) runs `AddAltinnServiceDefaults` from
`Altinn.Authorization.ServiceDefaults` 6.0.0 (OpenTelemetry 1.19.1, exporter 1.19.1 pinned in the csproj for advisories; source: `Altinn/altinn-authorization-utils`; decompiled, `ConfigureOpenTelemetry`
and `AddOpenTelemetryExporters` identical in 5.5.0 and 6.0.0):

- resource: `AltinnServiceResourceDetector` sets `service.name` `authentication`, `service.namespace` `altinn`,
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

The host adds its own source and meters (`AuthenticationHost.cs:225-230`: `AuthenticationTelemetry`, `AddAssemblyMetrics` for four assemblies) and Npgsql tracing through `AddAltinnPostgresDataSource`. No classic Application Insights code; `Azure.Monitor.OpenTelemetry.AspNetCore` 1.6.0 is referenced directly (a version pin, no direct call).

So the host already follows the stack's rule except for the name: OTLP exactly when the variable is set, nothing
otherwise, but `OTEL_SERVICE_NAME` is ignored.

## Upstream change

**Classification: trivial** for this host (nothing in the host itself has to change for "OpenTelemetry first").
Optional cleanup: the direct `Azure.Monitor.OpenTelemetry.AspNetCore` reference can go once ServiceDefaults pins a fixed version.

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
named `authentication` by its own detector, which is what studioctl and Aspire set as `OTEL_SERVICE_NAME`; `service.instance.id` is
the container's host name and `OTEL_RESOURCE_ATTRIBUTES` is ignored (accepted). The call below and the `fakes/Otlp/`
copy were removed in TODO.md task 61, which re-verified all three signals on the stack. What follows is the record of
task 53.

No vendored file changes. `AddLocaltestAuthentication` (`fakes/LocaltestAuthenticationExtensions.cs`, called from the existing `#if LOCALTEST` site `AuthenticationHost.cs:293-295`) calls
`LocaltestOtlp.AddLocaltestOtlpResource(services.AddOpenTelemetry(), builder.Configuration)` (`fakes/Otlp/`, a copy of
`../shared/fakes/Otlp/`): with the variable set it adds the environment detector after ServiceDefaults' resource, so
`OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` win; without it nothing. Not `AddLocaltestOtlp`: `UseOtlpExporter()`
throws next to the signal-specific exporters. Under studioctl the variable is the manifest name, `authentication`, the same as
the host's own, so the visible difference is only for another runner's name (Aspire, a manual run).

Checks (2026-10-26, no containers):
- `dotnet build -c Release` with the overrides (as the Dockerfile): 0 errors, no warnings in `fakes/`.
- The host as a process (`ASPNETCORE_ENVIRONMENT=Development`) against a fake OTLP/HTTP collector on a loopback port, `http/protobuf`: with
  `OTEL_SERVICE_NAME=from-env`, traces, metrics and logs arrive with `service.name` `from-env`; without it, `authentication`;
  without the endpoint, nothing at the collector or at the SDK defaults 4317/4318.
- `components/shared/Otlp.Tests` at 1.15.3, 1.17.0 and 1.19.1: 9/9 each.

Still to run: `podman build -t localtest-authentication:dev localtest/components/authentication`, `smoke-test.sh` (32 checks, 34 with
`--with dialogporten`, plus the OTLP span check with `--with monitoring`).
