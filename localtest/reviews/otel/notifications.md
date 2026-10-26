# Notifications: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-notifications](https://github.com/Altinn/altinn-notifications), vendored at `a4aa959a`
(`../../components/notifications/vendor/`, the API component and `components/shared`). Reviewed 2026-10-26 (TODO.md
task 53). **Classification: small** (a setting of its own is replaced).

## Upstream today

- **Already OpenTelemetry first.** `components/api/src/Altinn.Notifications/Program.cs`, `ConfigureServices`:
  OpenTelemetry 1.19.1 (central package management). Traces: ASP.NET Core, HttpClient, Npgsql, the `Wolverine` source,
  a background-job source, `RequestFilterProcessor`, `WolverineActivityNameProcessor` (renames Wolverine spans),
  `AlwaysOnSampler` in Development. Metrics: ASP.NET Core, Kestrel, `System.Net.Http`, `DeliveryReportMetrics`. Logs:
  `ILoggingBuilder.AddOpenTelemetry`. Resource: `service.name` `platform-notifications` (`AddAttributes`).
- `AddAzureMonitorTelemetryExporters` does two things:
  - **its own OTLP switch:** with `OtelSettings:Endpoint`, a traces-only `AddOtlpExporter` with the endpoint set in code
    (so no `/v1/traces` for `http/protobuf`, and no metrics or logs);
  - **Azure Monitor** for all three signals with `ApplicationInsights:ConnectionString`, unless
    `ApplicationInsights:Disable` (`Azure.Monitor.OpenTelemetry.Exporter` 1.10.0).
- No classic SDK. Nothing in the vendored tree sets `OtelSettings` (no compose file or appsettings entry).
- Not checked: the email and SMS services of the same repository, which are not vendored.

## Upstream change

- Replace the `OtelSettings:Endpoint` block with `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (the package
  is already referenced). Both cannot stay: `UseOtlpExporter` throws next to a signal-specific `AddOtlpExporter`. Anyone
  who sets `OtelSettings__Endpoint` today switches to the standard variable and gets metrics and logs as well.
- Optionally the environment detector after `AddAttributes`.
- Existing telemetry: OpenTelemetry already. Risk low; the behaviour change for `OtelSettings__Endpoint` users is the only
  break. Files: `Program.cs`. Apply the same to the email and SMS hosts if they share the pattern.

## Localtest (done 2026-10-26)

Directive site 4, `Program.cs:192-199`: `AddAzureMonitorTelemetryExporters` under `#if !LOCALTEST`, `#else`
`LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`). No package change. `CS8321` silenced in `localtest.build.props`. The
fakes no longer force `OtelSettings:Endpoint` and `ApplicationInsights:ConnectionString` empty (nothing reads them under
`LOCALTEST`). Build: 0 errors, 0 warnings. Not run: Postgres needed. Owed: the image build, `bump-tag.sh` and a smoke-test run with `--with monitoring` (its OTLP span check is added, not run).
