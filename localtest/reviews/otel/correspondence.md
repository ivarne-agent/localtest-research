# Correspondence: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-correspondence](https://github.com/Altinn/altinn-correspondence), vendored at `6dcf5619`
(`../../components/correspondence/vendor/`). Reviewed 2026-10-26 (TODO.md task 53). **Classification: trivial.**

## Upstream today

- **Already OpenTelemetry first.** `src/Altinn.Correspondence.Integrations/OpenTelemetry/DependencyInjection.cs`,
  `ConfigureOpenTelemetry(GeneralSettings)`, called from `src/Altinn.Correspondence.API/Program.cs` after
  `ConfigureServices`. OpenTelemetry 1.18.0. Traces: the Hangfire source, ASP.NET Core with a filter (OPTIONS and
  `TelemetryFilterHelper.ShouldExcludeRequest`), HttpClient with `HttpClientActivityEnricher`, `RequestFilterProcessor`.
  Metrics: the ASP.NET Core, Kestrel, `System.Net.Http` and Hangfire meters and the Npgsql metrics (no Npgsql tracing).
  Logs: `WithLogging()`. Resource: `service.name` `altinn-correspondence` (`AddAttributes`).
- **Azure Monitor only with a connection string:** the three Azure Monitor exporters (`Azure.Monitor.OpenTelemetry.Exporter`
  1.9.0) when `GeneralSettings:ApplicationInsightsConnectionString` is set.
- Dead references: `Microsoft.ApplicationInsights` 3.1.2 (API and Integrations) and `Azure.Monitor.OpenTelemetry.AspNetCore`
  1.6.0 (API) are referenced but no code uses them (no `TelemetryClient`, no `UseAzureMonitor`). No OTLP.

## Upstream change

- `DependencyInjection.cs`: take `IConfiguration` (or read the variable through the builder) and call `UseOtlpExporter()`
  when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; package `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.18.0 in the
  Integrations project. Optionally the environment detector after `AddAttributes`.
- Cleanup worth proposing with it: drop the two unused Application Insights packages.
- Existing telemetry: OpenTelemetry already (filters, enricher, processor, meters). Risk low. Files: `DependencyInjection.cs`,
  `Program.cs` (the extra argument), two csproj files.

## Localtest (done 2026-10-26)

The fakes compile into the API project only, so the site is in `src/Altinn.Correspondence.API/Program.cs:66-71`: a pure
`#if LOCALTEST` addition right after `ConfigureOpenTelemetry(generalSettings)`, calling `LocaltestOtlp.AddLocaltestOtlp`
(`fakes/Otlp/`) on the same providers. Production's call stays and adds no Azure Monitor exporter (the fakes force the
connection string empty). The OTLP package at the host's 1.18.0 in `localtest.build.targets` (restored: every
OpenTelemetry package at 1.18.0). Build: 0 errors, the same 24 upstream warnings. Not run: Postgres and Redis needed.
Proven on the stack (2026-10-26, images `sha-609c646`, `--with everything`): the smoke test passed with its OTLP span check.
