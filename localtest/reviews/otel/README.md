# Application Insights to OpenTelemetry, per service (task 53)

The user's request (2026-10-26): see how Application Insights can be replaced with OpenTelemetry in every service, and
do it where it is simple. The stack's rule (TODO.md task 53): a service exports traces, metrics and logs over OTLP
exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (protocol and service name from the standard variables; the
per-signal endpoint variables do not turn exporting on), and sends nothing otherwise.

Each file has two halves: the **upstream view** (how the vendored production code sends telemetry today, and what an
upstream change to "OpenTelemetry first, Azure Monitor only when its connection string is set, OTLP when the endpoint is
set" would take, classified trivial / small / large) and the **localtest change** (done where trivial or small, through
`../../components/shared/fakes/Otlp/`, otherwise a plan).

| Service | Review | Upstream | Localtest |
| --- | --- | --- | --- |
| Authorization (PDP) | [`authorization.md`](authorization.md) | small: classic Application Insights SDK (logs, requests, two telemetry processors, an initializer) plus a metrics-only Azure Monitor pipeline; replace with one OpenTelemetry pipeline in `Program.cs` (moving to ServiceDefaults would be large) | done: `#if !LOCALTEST`/`#else` builds traces, metrics and logs with `AddLocaltestOtlp`, `service.name` `platform-authorization` |
| Authentication | [`authentication.md`](authentication.md) | trivial: ServiceDefaults 6.0.0 already exports over OTLP when the variable is set | done: `AddLocaltestOtlpResource` in the fakes hook, so `OTEL_SERVICE_NAME` counts |
| Access Management | [`access-management.md`](access-management.md) | trivial for the rule; small cleanup (dead classic-SDK classes, its own `UseAzureMonitor` block outside ServiceDefaults) | done: as Authentication |
| Register | [`register.md`](register.md) | trivial: ServiceDefaults 5.5.0; drop an unused Application Insights reference | done: as Authentication (two package references in the fakes project) |
| Resource Registry | [`resource-registry.md`](resource-registry.md) | trivial: ServiceDefaults 6.0.0; drop three unused Application Insights references | done: as Authentication |

Across the four ServiceDefaults hosts, one small upstream change in `Altinn/altinn-authorization-utils` would help all of
them: make the OTLP and Azure Monitor exporters independent (today OTLP turns Azure Monitor off) and let
`OTEL_SERVICE_NAME` win over `AltinnServiceResourceDetector`.
