# Receipt: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-receipt](https://github.com/Altinn/altinn-receipt) (`../../components/receipt/vendor/`).
Reviewed 2026-10-26 (TODO.md task 53). **Classification: trivial.**

## Upstream today

- **Already OpenTelemetry first.** `src/backend/Altinn.Receipt/Program.cs`, `ConfigureServices`: OpenTelemetry 1.19.1.
  Traces: ASP.NET Core, HttpClient, `RequestFilterProcessor`, `AlwaysOnSampler` in Development. Metrics: ASP.NET Core,
  Kestrel, `System.Net.Http`. Logs: `ILoggingBuilder.AddOpenTelemetry`. Resource: `service.name` `platform-receipt`
  through `AddAttributes`.
- **Azure Monitor only with a connection string** read from Key Vault (`ConnectToKeyVaultAndSetApplicationInsights`,
  only with `kvSetting:SecretUri`): `AddAzureMonitorTelemetryExporters` (`Azure.Monitor.OpenTelemetry.Exporter` 1.10.0).
- No OTLP, no classic SDK, no custom meters.

## Upstream change

The OTLP package 1.19.1 and `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, after the Azure Monitor
block; optionally the environment detector after `AddAttributes`. Risk low. Files: `Program.cs`, the csproj.

## Localtest (done 2026-10-26)

Directive site 4, `Program.cs:190-199`: the Azure Monitor block under `#if !LOCALTEST`, `#else`
`LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`); OTLP package 1.19.1 in `localtest.build.targets`; `CS8321` silenced
in `localtest.build.props`. Build: 0 errors, 0 warnings. Upstream tests with the overrides: 19 of 19 pass.

Run without Docker against a fake OTLP/HTTP collector (a Python HTTP server; `http/protobuf`):

| Variables | Received |
| --- | --- |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `/v1/traces` (`GET receipt/api/v1/users/current`; `/health` is dropped by `RequestFilterProcessor`), `/v1/metrics`, `/v1/logs`, all `platform-receipt` |
| the same and `OTEL_SERVICE_NAME=otlp-check-receipt` | all three named `otlp-check-receipt` |
| none | nothing; no exporter line in the log |
| only `OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` | nothing; no exporter line in the log |

Seen: with the variable, the exporter's own `HttpClient` logs every export at Information on the console
(`System.Net.Http.HttpClient.OtlpMetricExporter.*`), because the host logs `System.Net.Http` at Information. None of those
lines showed up in the exported logs (the SDK suppresses telemetry from its own export), so it is console noise only; a log filter for `System.Net.Http.HttpClient.Otlp` in the shared
helper would remove it for every component. On the stack (2026-10-26, task 53's stack proof) the containers get `grpc`, and
over `grpc` the exporters log nothing; the same image with `http/protobuf` logged 40 such lines in 5 idle minutes. The
helper now filters `System.Net.Http.HttpClient.Otlp` below Warning (in the next images build). Proven on the stack (2026-10-26, images `sha-609c646`, `--with everything`): the smoke test passed with its OTLP span check.
