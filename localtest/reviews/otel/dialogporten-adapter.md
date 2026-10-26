# Dialogporten adapter: Application Insights to OpenTelemetry

Upstream: [Altinn/altinn-dialogporten-adapter](https://github.com/Altinn/altinn-dialogporten-adapter), vendored at
`86364b87` (`../../components/dialogporten-adapter/vendor/`). Reviewed 2026-10-26 (TODO.md task 53).
**Classification: small.**

## Upstream today

- **Azure Monitor first, OpenTelemetry only with it.** `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs`:
  `AddInfrastructure` calls `ConfigureTelemetry` only when `ApplicationInsights:Enabled`, and `ConfigureTelemetry` throws
  without `ApplicationInsights:ConnectionString`. It registers `HealthCheckFilterProcessor`, `service.name`
  `platform-dialogporten-adapter` and the Azure Monitor distro, `UseAzureMonitor` (`Azure.Monitor.OpenTelemetry.AspNetCore`
  1.6.0, OpenTelemetry 1.15.3): the distro brings ASP.NET Core and HttpClient instrumentation, the logs and metrics, 5 %
  trace sampling (`SamplingRatio = 0.05`), no live metrics, offline storage in `/tmp/logtelemetry`.
- Without Application Insights the service has no telemetry pipeline at all. No custom meters or sources. No OTLP.
- `HealthCheckFilterProcessor` sets `IsAllDataRequested = false` in `OnEnd`. An OTLP exporter exports the `/health` span
  anyway (seen locally); whether Azure Monitor honours it was not checked.

## Upstream change

- Build the pipeline unconditionally: `AddOpenTelemetry()` with the resource, the health filter, ASP.NET Core and
  HttpClient instrumentation for traces and metrics (`OpenTelemetry.Instrumentation.AspNetCore`/`.Http`).
- Azure Monitor only with the connection string: `ApplicationInsights:Enabled` becomes "connection string set". Either keep
  the distro (`UseAzureMonitor` in that branch; check that its instrumentation does not double the explicit one) or, like
  the other Altinn services, switch to `Azure.Monitor.OpenTelemetry.Exporter` with the 5 % sampler set explicitly.
- `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`OpenTelemetry.Exporter.OpenTelemetryProtocol`).
- The 5 % sampler is provider-wide, so OTLP would see the same 5 % in production; decide whether that is wanted.
- If the health filter is meant to drop spans for every exporter, it should also clear `ActivityTraceFlags.Recorded` (the
  OpenTelemetry export processor checks that flag, not `IsAllDataRequested`) or use the ASP.NET Core instrumentation's
  `Filter` (checked in the decompiled OpenTelemetry 1.15.3: `BatchActivityExportProcessor.OnEnd` exports when `Recorded`).
- Risk: medium-low (a restructure of one method, and the distro's defaults become explicit). Files:
  `ServiceCollectionExtensions.cs`, `Settings.cs` (the `Enabled` flag), the csproj.

## Localtest (done 2026-10-26)

`ServiceCollectionExtensions.cs:56-65`: production's `if (Enabled) ConfigureTelemetry` under `#if !LOCALTEST`, `#else`
`fakes/LocaltestAdapterTelemetry.cs`: with the variable, the same pipeline without Azure Monitor (filter, name, ASP.NET
Core and HttpClient traces and metrics) plus `LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`, which adds the logs);
nothing otherwise. The OTLP package 1.15.3 in `localtest.build.targets`; `.editorconfig` silences CA1716 for the copy.
The forced `ApplicationInsights:Enabled=false` in the fakes is gone. Build: 0 errors, 0 warnings.

Run without Docker against a fake OTLP/HTTP collector: with the variable, `/v1/traces` (`GET /health`), `/v1/metrics`
and `/v1/logs` arrived as `platform-dialogporten-adapter`; without it nothing arrived and nothing was logged about
exporting. Owed: the image build, `bump-tag.sh` and a smoke-test run with `--with monitoring` (its OTLP span check is added, not run).
