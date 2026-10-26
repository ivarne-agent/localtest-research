# Dialogporten: Application Insights to OpenTelemetry

Upstream: [Altinn/dialogporten](https://github.com/Altinn/dialogporten), vendored at `ec333e4b`
(`../../components/dialogporten/vendor/`; WebApi, GraphQL and Service hosts). Reviewed 2026-10-26 (TODO.md task 53).
**Classification: trivial** (follows the rule already, except `http/protobuf` for traces and metrics).

## Upstream today

- **OpenTelemetry first, OTLP when the endpoint is set.** `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs`,
  `AddDialogportenTelemetry`: nothing unless `OTEL_EXPORTER_OTLP_ENDPOINT` is a well-formed URI; then traces (HttpClient
  with Azure and Key Vault filters, Npgsql, Postgres/GraphQL/FusionCache/health filters, the hosts add ASP.NET Core and
  more) and metrics (runtime, HttpClient, the hosts' own), OpenTelemetry 1.17.0. Logs: Serilog, to the OTLP sink
  (`Serilog.Sinks.OpenTelemetry` 4.2.0) instead of the console when the endpoint is set. Name: `OTEL_SERVICE_NAME`, else
  the application name.
- **Azure Monitor only for metrics, only with `APPLICATIONINSIGHTS_CONNECTION_STRING`** (then instead of OTLP for
  metrics). Production presumably ships traces and logs through a collector on the OTLP endpoint (not checked in the infra).
- Deviations from the rule: `OTEL_EXPORTER_OTLP_PROTOCOL` must be `grpc` or `http/protobuf` when the endpoint is set
  (unset throws at start, the specification's default is `http/protobuf`); traces and metrics set `options.Endpoint` in
  code, so with `http/protobuf` they post to the bare endpoint without `/v1/traces` and `/v1/metrics` (only `grpc` works).
  The Serilog sink appends `/v1/logs` itself.

## Upstream change

- `OpenTelemetryExtensions.cs`: do not set `Endpoint` and `Protocol` on the two `AddOtlpExporter` calls (the SDK reads both
  variables and appends the signal path), and default the protocol instead of throwing (or leave the throw; it is a
  stricter reading, not a bug). Risk low; one file.

## Localtest (done 2026-10-26)

`OpenTelemetryExtensions.cs:106-112`, `:134-139`: upstream's `options.Endpoint` and `options.Protocol` under
`#if !LOCALTEST`, nothing in the `#else`. With `grpc` unchanged; with `http/protobuf` traces and metrics now reach
`/v1/traces` and `/v1/metrics`. The throw on an unset protocol stays (the runners set the variable). Build of
`Digdir.Library.Utils.AspNet` with the overrides: 0 errors, no new warnings. A scratch host on OpenTelemetry 1.17.0 with
`AddOtlpExporter(o => { })` posted to `/v1/traces` and `/v1/metrics` of a fake collector over `http/protobuf`. Not run:
the hosts need Postgres and Redis. Proven on the stack (2026-10-26, images `sha-609c646`, `--with everything`): the smoke test passed with its OTLP span check
for `dialogporten-webapi`, and the GraphQL and Service hosts sent traces, metrics and logs under their manifest names
(studioctl sends `grpc`).
