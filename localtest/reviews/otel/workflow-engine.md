# Workflow engine: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-studio](https://github.com/Altinn/altinn-studio) `src/Runtime/workflow-engine`, vendored at
`447e53eb` (`../../components/workflow-engine/vendor/`). Reviewed 2026-10-26 (TODO.md task 53). **Classification:
small, upstream; in review as [altinn-studio#21515](https://github.com/Altinn/altinn-studio/pull/21515).**

## Upstream today (the vendored pin)

- **OpenTelemetry only** (`WorkflowEngine.Telemetry/Extensions/ServiceCollectionExtensions.cs`): traces (own source,
  HttpClient, ASP.NET Core, optional EF Core/Npgsql), metrics (views, runtime, HttpClient, ASP.NET Core) and logs, with
  three signal-specific `AddOtlpExporter` calls, whenever `EngineSettings:EnableTelemetry` is true (the default). No
  Application Insights.
- Without the variable it exports to the SDK default `localhost:4317`, so the manifest sets
  `EngineSettings__EnableTelemetry=false`. `service.name` `WorkflowEngine` via `AddService`; `main` already lets
  `OTEL_SERVICE_NAME` win.

## Upstream change

altinn-studio#21515 exports exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set and removes `EnableTelemetry` (TODO.md,
index row 53, has its verification). Nothing to change in localtest: no `#if LOCALTEST`. After it merges: re-vendor,
rebuild the image, drop `EngineSettings__EnableTelemetry` from the manifest.
