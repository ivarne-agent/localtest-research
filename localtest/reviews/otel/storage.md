# Storage: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-storage](https://github.com/Altinn/altinn-storage), vendored at `9e72a9f1`
(`../../components/storage/vendor/`). Reviewed 2026-10-26 (TODO.md task 53). **Classification: trivial.**

## Upstream today

- **Already OpenTelemetry first.** `src/Storage/Program.cs`, `ConfigureServices`: one OpenTelemetry SDK pipeline
  (1.19.1), always on. Traces: ASP.NET Core, HttpClient, Npgsql (`Npgsql.OpenTelemetry`), `RequestFilterProcessor`
  (drops health and similar requests, tags the user, party and system user from the claims), `AlwaysOnSampler` in
  Development. Metrics: ASP.NET Core, the Kestrel and `System.Net.Http` meters, the service's own meter
  (`Telemetry/Metrics.cs`) and `AspNetCoreMetricsEnricher`. Logs: `ILoggingBuilder.AddOpenTelemetry` with formatted
  messages and scopes. Resource: `service.name` `platform-storage` through `AddAttributes`.
- **Azure Monitor only with a connection string.** `AddAzureMonitorTelemetryExporters` adds the three Azure Monitor
  exporters (`Azure.Monitor.OpenTelemetry.Exporter` 1.10.0) only when `ConnectToKeyVaultAndSetApplicationInsights`
  read `ApplicationInsights--ConnectionString` from Key Vault. The trace exporter sets `SamplingRatio = 1.0`,
  `TracesPerSecond = null`.
- No classic Application Insights SDK, no `TelemetryInitializer`, no `ITelemetryProcessor`. `OpenTelemetry.Exporter.Console`
  is referenced but not used.
- No OTLP.

## Upstream change for "OTLP when the endpoint is set"

- Package: `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1.
- `Program.cs`, after the Azure Monitor block in `ConfigureServices`:
  `if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"])) services.AddOpenTelemetry().UseOtlpExporter();`
  `UseOtlpExporter` runs next to the Azure Monitor exporters (it only conflicts with a signal-specific `AddOtlpExporter`).
- Optional: `.AddEnvironmentVariableDetector()` after `AddAttributes` in `ConfigureResource`, so `OTEL_SERVICE_NAME`
  wins over the hard-coded name (today it is ignored; the localtest helper does this when the variable is set).
- Existing telemetry needs nothing: all of it is OpenTelemetry already.
- Risk: low. One thing to know: `AddAzureMonitorTraceExporter` sets the tracer provider's sampler, so with both
  exporters on, OTLP gets Azure Monitor's sample (here everything). Files: `Program.cs`, the csproj.

## Localtest (done 2026-10-26)

`Program.cs:249-258`: the Azure Monitor block under `#if !LOCALTEST`, `#else`
`LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(), config)` (`fakes/Otlp/`, a copy of
`components/shared/fakes/Otlp/`); the OTLP package 1.19.1 in `localtest.build.targets`; `CS8321` (the now unused local
function) was already silenced in `localtest.build.props`. Build with the overrides: 0 errors, no new warnings. Not run:
the host needs Postgres. Owed: the image build, `bump-tag.sh`, the smoke check.
