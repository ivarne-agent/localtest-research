# Authorization (PDP): Application Insights to OpenTelemetry (2026-10-26)

Upstream `Altinn/altinn-auth` at `3d518103`, `src/apps/Altinn.Authorization/` (paths below are under
`localtest/components/authorization/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/`).

**Classification: small** upstream (one host file, three classes deleted, two packages swapped; medium risk only for
the Application Insights dashboards and queries that read the classic telemetry). **Localtest: done** (task 53).

## Telemetry today

The PDP is a plain `WebApplication.CreateBuilder` host. It references `Altinn.Authorization.ServiceDefaults` 5.5.0 but
never calls `AddAltinnServiceDefaults`, so none of the ServiceDefaults OpenTelemetry pipeline applies. Everything is
the **classic Application Insights SDK** (`Microsoft.ApplicationInsights.AspNetCore` 2.23.0), behind one condition: the
instrumentation key read from Key Vault (`ConnectToKeyVaultAndSetApplicationInsights`, `Program.cs:157-194`, only with
all four `kvSetting` values; the secret `ApplicationInsights--InstrumentationKey` becomes `InstrumentationKey=...`).

| Signal | Today | Where |
| --- | --- | --- |
| Logs | `logging.ClearProviders()`, then `AddApplicationInsights` (Warning, `Program` at Trace) when the key was read, otherwise the console | `Program.cs:93-120` |
| Requests, dependencies (traces) | `AddApplicationInsightsTelemetry` with a `ServerTelemetryChannel` (`/tmp/logtelemetry`), adaptive sampling by default | `Program.cs:278-287` |
| Filtering | `HealthTelemetryFilter` (drops requests ending in `/health/`), `IdentityTelemetryFilter` (adds `partyId`, `authLevel`, `userId`, `orgNumber` to `authorization/api/` requests from the user's claims) | `Health/`, `Filters/` |
| Enrichment | `CustomTelemetryInitializer`: cloud role name `platform-authorization` | `Configuration/CustomTelemetryInitializer.cs` |
| Metrics | A metrics-only OpenTelemetry pipeline with `AddAzureMonitorMetricExporter` (comment: traces and logs stay on the classic SDK to avoid duplicate ingestion); the `DecisionTelemetry` meter is added with `ConfigureOpenTelemetryMeterProvider`, outside the condition | `Program.cs:293-301` |

No OTLP anywhere; `OTEL_EXPORTER_OTLP_ENDPOINT` is ignored. Without the Key Vault key there is no OpenTelemetry provider
at all, so the decision meter goes nowhere.

## Upstream change: OpenTelemetry first

Target: one OpenTelemetry pipeline (traces, metrics, logs), Azure Monitor exporters when the connection string is set,
OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (both when both are).

Two ways:

1. **Within `Program.cs` (small, proposed).** Replace `Program.cs:278-298` and the Application Insights branch of
   `ConfigureLogging` with:
   - `services.AddOpenTelemetry().ConfigureResource(r => r.AddService("platform-authorization"))` with
     `WithTracing` (ASP.NET Core with `Filter` excluding `/health`, HttpClient) and `WithMetrics` (ASP.NET Core,
     HttpClient, runtime, `DecisionTelemetry.MeterName`) and `WithLogging` (formatted messages; the current
     `Warning` / `Program`-at-`Trace` levels as `AddFilter<OpenTelemetryLoggerProvider>`).
   - `if (!string.IsNullOrEmpty(applicationInsightsConnectionString)) builder.UseAzureMonitor(o => o.ConnectionString = ...)`
     (`Azure.Monitor.OpenTelemetry.AspNetCore`, already in the graph at 1.5.0 through ServiceDefaults), or the three
     `AddAzureMonitor*Exporter` calls if the distro's own instrumentation is not wanted.
   - `if (!string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"])) otel.UseOtlpExporter()` (cannot be
     combined with signal-specific `AddOtlpExporter`; fine here, there is none). `UseAzureMonitor` and
     `UseOtlpExporter` can coexist.
   - Keep the console logger when neither is set (today's behaviour).
   - Delete `HealthTelemetryFilter`, `IdentityTelemetryFilter`, `CustomTelemetryInitializer`; drop
     `Microsoft.ApplicationInsights.AspNetCore` from the csproj and `Microsoft.Extensions.Logging.ApplicationInsights`
     use; the Key Vault read stays (it only produces the connection string).
2. **Move the host to `AltinnHost.CreateWebApplicationBuilder("authorization", ...)`** like Authentication, Access
   Management, Register and the Resource Registry (**large**): it brings the telemetry for free but also ServiceDefaults'
   configuration sources, health endpoints, problem details and middleware, and replaces the hand-written Key Vault
   setup. Worth doing for consistency, but not as a telemetry change.

Equivalents for what exists today:

| Classic SDK | OpenTelemetry |
| --- | --- |
| `HealthTelemetryFilter` | ASP.NET Core instrumentation `Filter` (as ServiceDefaults does) |
| `IdentityTelemetryFilter` | `EnrichWithHttpRequest` / `EnrichWithHttpResponse` on the ASP.NET Core instrumentation, setting the same tags from `HttpContext.User` (in Azure Monitor they land in `customDimensions` as today) |
| `CustomTelemetryInitializer` (role name) | `service.name` in the resource (Azure Monitor maps it to `cloud_RoleName`) |
| Adaptive sampling (classic default) | The distro's sampler (`SamplingRatio`, or rate-limited `TracesPerSecond` in newer versions); a decision on the rate is needed |
| `ServerTelemetryChannel` disk buffer | The Azure Monitor exporter's offline storage (on by default) |
| `ApplicationInsightsLoggerProvider` filters | `AddFilter<OpenTelemetryLoggerProvider>` |

Risk: Application Insights queries, alerts and workbooks that rely on the classic shape (custom dimension names,
`cloud_RoleName`, sampling) change; the decision metrics stay as they are. Tests: nothing under `test/` references
the three classes or Application Insights (grep, 2026-10-26).

## Localtest change (done)

Superseded 2026-10-29 (task 63's follow-up, TODO.md section 63): `AddLocaltestOtlp` and the `fakes/Otlp/` copy are gone;
`LocaltestAuthorizationTelemetry` inlines the rule (`UseOtlpExporter()` after the environment detector, the
`HttpClient.Otlp` filter). What follows is the record of 2026-10-26.

`Program.cs:278-304`: `#if !LOCALTEST` around the Application Insights block, `#else`
`LocaltestAuthorizationTelemetry.AddLocaltestTelemetry(services, config)` (`fakes/LocaltestAuthorizationTelemetry.cs`).
With `OTEL_EXPORTER_OTLP_ENDPOINT` set it builds the pipeline of option 1 without Azure Monitor: traces (ASP.NET Core
without `/health`, HttpClient), metrics (ASP.NET Core, HttpClient, and the `DecisionTelemetry` meter from production's
own registration), `service.name` `platform-authorization`, then `AddLocaltestOtlp` (logs too; `OTEL_SERVICE_NAME`
wins). Without the variable it registers nothing, as before. Packages: none added; the 1.15.x OpenTelemetry packages
come transitively through ServiceDefaults 5.5.0 (`OpenTelemetry` 1.15.3, ASP.NET Core instrumentation 1.15.2, HttpClient
1.15.1). `IdentityTelemetryFilter`'s properties are not carried over.

Checks (2026-10-26, no containers):
- `dotnet build -c Release` with the overrides (`DirectoryBuildPropsPath`/`DirectoryBuildTargetsPath`, as the Dockerfile):
  0 errors, 1 warning (pre-existing).
- The host as a process (`ASPNETCORE_ENVIRONMENT=Development`) against a fake OTLP/HTTP collector on a loopback port,
  `http/protobuf`: `/v1/traces` (the API request's span, none for `/health`), `/v1/metrics`, `/v1/logs`, with
  `service.name` `platform-authorization`, or `authorization` with `OTEL_SERVICE_NAME=authorization`. Without the
  variable: nothing at the collector or at the SDK defaults 4317/4318, no export errors in the log.
- `components/shared/Otlp.Tests -p:OtelVersion=1.15.3`: 9/9.

Still to run: `podman build -t localtest-authorization:dev localtest/components/authorization`, then `smoke-test.sh`
(31 checks, plus the new OTLP check with `--with monitoring`: the PDP's server span with `service.name` `authorization`
in VictoriaTraces), then `bump-tag.sh`.
