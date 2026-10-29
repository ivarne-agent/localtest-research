# Profile: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-profile](https://github.com/Altinn/altinn-profile) (`../../components/profile/vendor/`).
Reviewed 2026-10-26 (TODO.md task 53). **Classification: trivial.**

## Upstream today

- **Already OpenTelemetry first.** `src/Altinn.Profile/Program.cs`, `ConfigureServices`: OpenTelemetry 1.19.1. Traces:
  the service's source (`Telemetry.AppName`), ASP.NET Core, HttpClient, EF Core (`OpenTelemetry.Instrumentation.EntityFrameworkCore`
  1.11.0-beta.2), `RequestFilterProcessor`, `AlwaysOnSampler` in Development. Metrics: ASP.NET Core, Kestrel,
  `System.Net.Http` and the service's meter (`Telemetry`, several counters). Logs: `ILoggingBuilder.AddOpenTelemetry`.
  Resource: `AddService("platform-profile", ...)`.
- **Azure Monitor only with a connection string:** `AddAzureMonitorTelemetryExporters` (static local function) returns
  without `ApplicationInsights:ConnectionString`; otherwise the three exporters (`Azure.Monitor.OpenTelemetry.Exporter`
  1.9.0). `OpenTelemetry.Exporter.Console` referenced, unused.
- No OTLP, no classic SDK.

## Upstream change

Same as Events: the OTLP package 1.19.1, `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, optionally the
environment detector after `AddService`. Existing telemetry is OpenTelemetry already. Risk low. Files: `Program.cs`, the
csproj.

## Localtest (done 2026-10-26)

Superseded 2026-10-29 (task 63's follow-up, TODO.md section 63): the `fakes/Otlp/` copy is gone and the site inlines
the rule (the endpoint check, `UseOtlpExporter()` after the environment detector, the `HttpClient.Otlp` filter). What
follows is the record of 2026-10-26.

Directive site 4, `src/Altinn.Profile/Program.cs:152-158`: `#if !LOCALTEST` production's call, `#else`
`LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`); OTLP package 1.19.1 in `localtest.build.targets`; `CS8321` silenced.
The fakes no longer force `ApplicationInsights:ConnectionString` empty. Build: 0 errors, 0 warnings. Not run: Postgres
needed. Proven on the stack (2026-10-26, images `sha-609c646`, `--with everything`): the smoke test passed with its OTLP span check.
