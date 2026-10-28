# Register: Application Insights to OpenTelemetry (2026-10-26)

Upstream `Altinn/altinn-register` at `f6b5c603`, `src/apps/Altinn.Register/`.

**Classification: trivial** upstream for the host (it already exports over OTLP when the variable is set); the
family-wide improvement in ServiceDefaults is small. **Localtest: done** (task 53, the name only).

## Telemetry today (all four ServiceDefaults hosts alike)

`AltinnHost.CreateWebApplicationBuilder("register", ...)` (`RegisterHost.cs:68`) runs `AddAltinnServiceDefaults` from
`Altinn.Authorization.ServiceDefaults` 5.5.0 (resolved OpenTelemetry 1.17.0, exporter 1.15.3; source: `Altinn/altinn-authorization-utils`; decompiled, `ConfigureOpenTelemetry`
and `AddOpenTelemetryExporters` identical in 5.5.0 and 6.0.0):

- resource: `AltinnServiceResourceDetector` sets `service.name` `register`, `service.namespace` `altinn`,
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

The host adds `RegisterTelemetry` (source and meter, `RegisterHost.cs:139-140`), the jobs and MassTransit sources
and meters (in-repo `ServiceDefaults.Jobs` / `ServiceDefaults.MassTransit`; MassTransit and jobs are off in localtest,
`Altinn:IsTest`). `Microsoft.ApplicationInsights.AspNetCore` is referenced but not used in code; the `ApplicationInsights`
sections in `appsettings*.json` are log levels for a provider that is never added.

So the host already follows the stack's rule except for the name: OTLP exactly when the variable is set, nothing
otherwise, but `OTEL_SERVICE_NAME` is ignored.

## Upstream change

**Classification: trivial** for this host (nothing in the host itself has to change for "OpenTelemetry first").
Cleanup upstream (trivial): drop the unused `Microsoft.ApplicationInsights.AspNetCore` reference and the `ApplicationInsights` log-level sections.

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
named `register` by its own detector, which is what studioctl and Aspire set as `OTEL_SERVICE_NAME`; `service.instance.id` is
the container's host name and `OTEL_RESOURCE_ATTRIBUTES` is ignored (accepted). The call below and the `fakes/Otlp/`
copy were removed in TODO.md task 61, which re-verified all three signals on the stack. What follows is the record of
task 53.

No vendored file changes. `AddLocaltestRegister` (`fakes/Altinn.Register.Localtest/LocaltestRegisterExtensions.cs`, called from the existing `#if LOCALTEST` site `RegisterHost.cs:249-251`) calls
`LocaltestOtlp.AddLocaltestOtlpResource(services.AddOpenTelemetry(), builder.Configuration)` (`fakes/Altinn.Register.Localtest/Otlp/`, a copy of
`../shared/fakes/Otlp/`): with the variable set it adds the environment detector after ServiceDefaults' resource, so
`OTEL_SERVICE_NAME` / `OTEL_RESOURCE_ATTRIBUTES` win; without it nothing. Not `AddLocaltestOtlp`: `UseOtlpExporter()`
throws next to the signal-specific exporters. Under studioctl the variable is the manifest name, `register`, the same as
the host's own, so the visible difference is only for another runner's name (Aspire, a manual run).
The hook lives in its own project, which referenced only `Altinn.Register.Core`; it now references
`OpenTelemetry.Extensions.Hosting` 1.17.0 and `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.15.3, the versions the host
resolves (unchanged in `obj/project.assets.json`).

Checks (2026-10-26, no containers):
- `dotnet build -c Release` with the overrides (as the Dockerfile): 0 errors, 0 warnings.
- The host as a process (`ASPNETCORE_ENVIRONMENT=Development`, test data from `testdata/testData.json`) against a fake OTLP/HTTP collector on a loopback port, `http/protobuf`: with
  `OTEL_SERVICE_NAME=from-env`, traces, metrics and logs arrive with `service.name` `from-env`; without it, `register`;
  without the endpoint, nothing at the collector or at the SDK defaults 4317/4318.
- `components/shared/Otlp.Tests -p:OtelVersion=1.17.0` and `1.15.3`: 9/9 each.

Still to run: `podman build -t localtest-register:dev localtest/components/register`, `smoke-test.sh http://local.altinn.cloud:8000`
(plus the OTLP span check with `--with monitoring`; skipped in minimal mode and against a plain process).
