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
| Authentication | [`authentication.md`](authentication.md) | trivial: ServiceDefaults 6.0.0 already exports over OTLP when the variable is set | nothing needed: exports under its own name, the manifest name (`AddLocaltestOtlpResource` from task 53 removed in task 61) |
| Access Management | [`access-management.md`](access-management.md) | trivial for the rule; small cleanup (dead classic-SDK classes, its own `UseAzureMonitor` block outside ServiceDefaults) | nothing needed: as Authentication |
| Register | [`register.md`](register.md) | trivial: ServiceDefaults 5.5.0; drop an unused Application Insights reference | nothing needed: as Authentication (the two package references in the fakes project went too) |
| Resource Registry | [`resource-registry.md`](resource-registry.md) | trivial: ServiceDefaults 6.0.0; drop three unused Application Insights references | nothing needed: as Authentication |
| Storage | [`storage.md`](storage.md) | trivial: OpenTelemetry pipeline already, Azure Monitor exporters only with the Key Vault connection string; add the OTLP package and `UseOtlpExporter()` when the variable is set | done: `#if !LOCALTEST`/`#else AddLocaltestOtlp` around the Azure Monitor block |
| Events | [`events.md`](events.md) | trivial: as Storage | done: as Storage |
| Profile | [`profile.md`](profile.md) | trivial: as Storage | done: as Storage |
| Receipt | [`receipt.md`](receipt.md) | trivial: as Storage | done: as Storage; run as a process against a fake collector |
| Correspondence | [`correspondence.md`](correspondence.md) | trivial: as Storage, in `Integrations/OpenTelemetry/DependencyInjection.cs`; drop two unused Application Insights packages | done: a pure `#if LOCALTEST` `AddLocaltestOtlp` after `ConfigureOpenTelemetry` (the fakes compile into the API project only) |
| Notifications | [`notifications.md`](notifications.md) | small: replace its own traces-only `OtelSettings:Endpoint` with `UseOtlpExporter()` on the standard variable | done: `#if !LOCALTEST`/`#else AddLocaltestOtlp`; `OtelSettings` dropped from the fakes |
| Dialogporten adapter | [`dialogporten-adapter.md`](dialogporten-adapter.md) | small: telemetry only through the `UseAzureMonitor` distro when Application Insights is enabled; build the pipeline always, Azure Monitor with the connection string, OTLP with the variable; its health filter does not drop spans | done: the `#else` builds the same pipeline (`fakes/LocaltestAdapterTelemetry.cs`) when the variable is set; run as a process |
| Dialogporten (WebApi, GraphQL, Service) | [`dialogporten.md`](dialogporten.md) | trivial: OTLP from the variable already; traces and metrics set the endpoint in code, so `http/protobuf` misses `/v1/<signal>` | done: `#else` leaves endpoint and protocol to the SDK |
| Arbeidsflate BFF | [`arbeidsflate.md`](arbeidsflate.md) | small: OTLP when the variable is set, but gRPC exporters whatever the protocol says | nothing needed: studioctl sends every container `grpc` to the collector's 4317 |
| Workflow engine | [`workflow-engine.md`](workflow-engine.md) | small, upstream: done in altinn-studio#21515 (merged; exports exactly when the variable is set, `EnableTelemetry` removed) | nothing in localtest: the `tt02` image has it, the manifest switch is gone; no longer vendored (task 58) |

pdf3 (not in the table: its image comes from altinn-studio) exports traces, metrics and logs over OTLP when the variable
is set, but only log records at WARN and above, since the pipeline in Altinn's environments drops everything below WARN.
Localtest keeps that on purpose (decided 2026-10-27): pdf3's request spans already record each render step with its
timings and link to the app's span, so its INFO records would only repeat them. A configurable level
(`PDF3_OTEL_LOG_LEVEL`) was built and dropped. Its WARN and ERROR records carry the request's trace ID.

Across the four ServiceDefaults hosts, one small upstream change in `Altinn/altinn-authorization-utils` was proposed:
make the OTLP and Azure Monitor exporters independent (today OTLP turns Azure Monitor off) and let `OTEL_SERVICE_NAME`
win over `AltinnServiceResourceDetector`. Neither has a local effect (each host's own name is the manifest name), so
since task 61 (2026-10-28) localtest neither proposes it nor carries the `AddLocaltestOtlpResource` workaround.

Across the Azure Monitor services (Storage, Events, Profile, Receipt, Correspondence, Notifications): they are OpenTelemetry
first already, so the upstream change is one package and one `UseOtlpExporter()` call each. Two things to carry into those
pull requests: `AddAzureMonitorTraceExporter` sets the provider's sampler, so OTLP sees Azure Monitor's sample when both
are on; and a hard-coded `service.name` hides `OTEL_SERVICE_NAME` unless the environment detector is added after it.
