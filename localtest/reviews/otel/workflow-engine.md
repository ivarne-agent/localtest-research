# Workflow engine: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-studio](https://github.com/Altinn/altinn-studio) `src/Runtime/workflow-engine`, vendored at
`447e53eb` (`components/workflow-engine/vendor/`, removed 2026-10-27 in TODO.md task 58: the stack runs the monorepo's
image). Reviewed 2026-10-26 (TODO.md task 53). **Classification:
small, upstream; done in [altinn-studio#21515](https://github.com/Altinn/altinn-studio/pull/21515) (merged).**

## Upstream today (the vendored pin)

- **OpenTelemetry only** (`WorkflowEngine.Telemetry/Extensions/ServiceCollectionExtensions.cs`): traces (own source,
  HttpClient, ASP.NET Core, optional EF Core/Npgsql), metrics (views, runtime, HttpClient, ASP.NET Core) and logs, with
  three signal-specific `AddOtlpExporter` calls, whenever `EngineSettings:EnableTelemetry` is true (the default). No
  Application Insights.
- Without the variable it exports to the SDK default `localhost:4317`, so the manifest sets
  `EngineSettings__EnableTelemetry=false`. `service.name` `WorkflowEngine` via `AddService`; `main` already lets
  `OTEL_SERVICE_NAME` win.

## Upstream change (done)

altinn-studio#21515, merged as `884b8ed755`, exports exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set and removes
`EnableTelemetry`; the stack runs the `tt02` image that has it, and the manifest's `EngineSettings__EnableTelemetry` is
gone (commit `f8f4148f`, TODO.md task 52). Nothing to change in localtest: no `#if LOCALTEST`. The vendored source above
is older than the image and should be re-vendored at the next sync.
