# Events: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-events](https://github.com/Altinn/altinn-events) (`../../components/events/vendor/`).
Reviewed 2026-10-26 (TODO.md task 53). **Classification: trivial.**

## Upstream today

- **Already OpenTelemetry first.** `src/Events/Program.cs`, `ConfigureServices`: one OpenTelemetry pipeline (1.19.1).
  Traces: the service's own sources (`TelemetryClient.AppName`, `Altinn.Platform.Events.RegisteredEventsProcessingService`),
  ASP.NET Core, HttpClient, Npgsql, `RequestFilterProcessor`, `AlwaysOnSampler` in Development. Metrics: ASP.NET Core,
  Kestrel, `System.Net.Http` and the service's meter. Logs: `ILoggingBuilder.AddOpenTelemetry`. Resource:
  `AddService("platform-events", serviceInstanceId: machine name)`.
- `TelemetryClient` is the service's own class (meter and counters), not the Application Insights SDK's.
- **Azure Monitor only with a connection string.** `AddAzureMonitorTelemetryExporters` returns early without
  `ApplicationInsights:ConnectionString` (from Key Vault in production); otherwise the three Azure Monitor exporters
  (`Azure.Monitor.OpenTelemetry.Exporter` 1.9.0, `SamplingRatio = 1.0`, `TracesPerSecond = null`).
- No OTLP, no classic SDK, no `TelemetryInitializer`.

## Upstream change

- Package `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1; in `ConfigureServices` next to
  `AddAzureMonitorTelemetryExporters`: `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.
- Optional: the environment detector after `AddService`, so `OTEL_SERVICE_NAME` wins.
- Existing telemetry: all OpenTelemetry already. Risk low; the Azure Monitor trace exporter's sampler applies to OTLP too
  when both are on. Files: `Program.cs`, the csproj.

## Localtest (done 2026-10-26)

`src/Events/Program.cs:170-176`: `AddAzureMonitorTelemetryExporters` under `#if !LOCALTEST`, `#else`
`LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`); the OTLP package 1.19.1 in `localtest.build.targets`; `CS8321` silenced
in `localtest.build.props` (the unused local function). The fakes no longer force `ApplicationInsights:ConnectionString`
empty (nothing reads it under `LOCALTEST`). Build: 0 errors, 0 warnings. Not run: the host needs Postgres. Proven on the stack (2026-10-26, images `sha-609c646`, `--with everything`): the smoke test passed with its OTLP span check.
