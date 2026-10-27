# Events: upstreaming

- Upstream: [Altinn/altinn-events](https://github.com/Altinn/altinn-events)
- Pin: `1a4a80c` (main, 2026-10-19), [`../components/events/vendor/UPSTREAM.md`](../components/events/vendor/UPSTREAM.md)
- Group: none
- Reminders: none.
- Localtest modifications: 7 directive sites (4 in `src/Events/Program.cs`, 2 in
  `src/Events/Controllers/SubscriptionController.cs`, 1 in `src/Events/Services/OutboundService.cs`), the fakes
  `Auth/`, `Changes/`, `Delivery/`, `Inspection/`, `Queues/`, `Sources/`, `Startup/`, the composition
  `LocaltestEventsExtensions.cs` and `LocaltestEventsOptions.cs`, the shared copies `ServiceDiscovery/` and `Otlp/`,
  two build overrides, the Postgres init script `postgres-init/30-events.sh`, and the `localtest:` deviations in
  `Dockerfile` and `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/events/` (sites relative to its `vendor/`).

Events keeps localtest-only features compiled into the host (webhook capture, change notifications, the inspection
API, the reset on start; see "Stays local"), so even with every item below done the vendored build stays unless those
features find another way in.

## Phase 1: easy fixes

- [ ] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Add `OpenTelemetry.Exporter.OpenTelemetryProtocol`
      (at the host's OpenTelemetry version, 1.19.1) to `Altinn.Platform.Events.csproj` and call `UseOtlpExporter()` in
      `ConfigureServices` when the variable is set, next to `AddAzureMonitorTelemetryExporters`; add the environment
      resource detector after `AddService` so `OTEL_SERVICE_NAME` wins over `platform-events`.
  - Why: without it, Events sends telemetry only to Azure Monitor with `ApplicationInsights:ConnectionString` from Key
    Vault, so a local run exports nothing.
  - Size: trivial.
  - Removes: site `src/Events/Program.cs:170-176`, `fakes/Otlp/`, the OTLP `PackageReference` in
    `localtest.build.targets` and the `CS8321` `NoWarn` in `localtest.build.props`.
  - Upstream: none found. [altinn-events#1149](https://github.com/Altinn/altinn-events/pull/1149) lists telemetry in
    its local dashboard as not done.
  - Detail: [`../reviews/otel/events.md`](../reviews/otel/events.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Allow http subscription endpoints and source filters in Development.** `SubscriptionController.IsValidUri`
      accepts https only, for both `EndPoint` and `SourceFilter`. Also accept http when the host runs in `Development`
      (or behind a setting); the method is static today, so it needs the environment or the setting injected.
  - Why: apps and the test sinks are served over plain http on `local.altinn.cloud:8000`, so no local subscription can
    be created.
  - Size: small.
  - Removes: site `src/Events/Controllers/SubscriptionController.cs:193-198`.
  - Upstream: none found. #1149 uses an https webhook receiver with the development certificate instead.
  - Detail: [`../components/events/README.md`](../components/events/README.md), "Directive sites in `vendor/`", 3.

- [ ] **Create `trace_log` partitions ahead of time.** `events.trace_log` is range-partitioned by year and the
      migrations create `trace_log_y2025` (`v0.46`) and `trace_log_y2026` (`v0.50`) only; `events.create_logs_partition()`
      (monthly) exists but nothing calls it. Add the 2027 partition before 2027-01-01, and preferably create the next
      partition automatically (a scheduled call of the function, or a startup step).
  - Why: without a partition for the current year, saving a webhook response fails. Locally the fake adds this year's
    and next year's; production hits the same wall on 2027-01-01 unless it is handled outside the migrations, which I
    could not confirm.
  - Size: trivial for the 2027 migration; small for the automation.
  - Removes: the partition block of `fakes/Startup/prepare.sql` (the rest of `Startup/` stays local).
  - Upstream: none found for 2027; [altinn-events#758](https://github.com/Altinn/altinn-events/issues/758) (closed) did
    the same for 2026 and asked for automation.
  - Detail: [`../reviews/events.md`](../reviews/events.md), finding 12.

- [ ] **Store `registeredtime` and `trace_log.time` in UTC whatever the server's time zone.** The column defaults
      `(now() at time zone 'utc')` (`Migration/v0.21/01-setup-table.sql`, `v0.44` and `v0.46/01-setup-tables.sql`) turn
      the UTC wall clock into a `timestamp` and back into a `timestamptz` in the session's time zone, which is right
      only on a server in UTC. A migration that sets the defaults to `now()` fixes it.
  - Why: on a Postgres not in UTC every event is stored hours early, and the read API's 30-second delay holds nothing
    back.
  - Size: trivial.
  - Removes: nothing in the component; the shared Postgres's `TZ=UTC` settings in studioctl's manifest would no longer
    be needed for Events (Profile and Dialogporten have the same default).
  - Upstream: none found.
  - Detail: [`../components/events/README.md`](../components/events/README.md), "Postgres".

- [ ] **Ship a Postgres init script.** Upstream's README asks for the `eventsdb` database and the roles
      `platform_events_admin` (superuser) and `platform_events` by hand. Add an idempotent script, like Storage's
      `infra/postgres_init.sql`, that creates them.
  - Why: every local setup repeats those steps; the stack's shared Postgres needs them as an init script.
  - Size: small; development tooling only.
  - Removes: `postgres-init/30-events.sh`, replaced by upstream's script (running it in the shared Postgres image stays
    local, as for Storage).
  - Upstream: none found. #1149 creates the roles in its Aspire app host, not as a reusable script.

## Phase 2: structural changes

- [ ] **Tell a platform component's source from an app's without a separate host.** `OutboundService.GetSourceFilter`
      and `SubscriptionController.IsAppSubscription` treat a source as an app's when its host contains or ends with
      `PlatformSettings:AppsDomain`. Locally apps and platform components share `local.altinn.cloud`. Upstream could
      also exclude sources whose first path segment is a configured platform prefix (a list setting, empty by
      default), which is what our `#else` does.
  - Why: with `AppsDomain` the shared host, Dialogporten's and Correspondence's sources become app sources: every
    dialog event is cut to `.../dialogporten/api`, one consumer's cached decision is reused across apps, and a
    Dialogporten source filter takes the app path without the subscribe scope.
  - Size: small in code; needs the team's agreement, since it adds a local-only distinction to the authorization path.
    The localtest-only alternative, apps on `{org}.apps.local.altinn.cloud`, touches studioctl's routes, app-lib's
    URLs and the Cypress setup (large).
  - Removes: sites `src/Events/Controllers/SubscriptionController.cs:213-221` and
    `src/Events/Services/OutboundService.cs:429-435`, and `fakes/Sources/`.
  - Upstream: none found.
  - Detail: [`../components/events/README.md`](../components/events/README.md), "Report for sites 4 and 5".

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder`, so `PlatformSettings` endpoints such as
      `http://register/register/api/` resolve through `services__<name>__http__0`. Events is not one of the
      `AltinnServiceDefaults` hosts, so it needs the registration; agree on the pattern in the README's
      [Across services](README.md#across-services). Decide whether the webhook client (`WebhookService`) should resolve
      names as well; locally it does.
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `src/Events/Program.cs:59-64`, `fakes/ServiceDiscovery/` and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), "Events".

- [ ] **A supported in-process transport without Service Bus.** Part of the cross-cutting "supported local configuration
      without Key Vault, Azure Blob and queues" ([Across services](README.md#across-services)).
      `AddWolverineServices` configures Service Bus only when it is enabled and otherwise registers the legacy Storage
      Queue publishers; the retry policies are attached only to Service Bus listeners. Offer a local mode: the bus
      publishers on Wolverine's local queues, with the four `*EventHandlerPolicy` policies attached to them.
  - Why: locally there is no Service Bus, so the fakes swap the publishers, attach the policies and force the six
    `WolverineSettings:Enable*` flags off.
  - Size: medium; follows the team's migration off Azure Functions.
  - Removes: the publisher swap, the `ConfigureWolverine` policies and the forced `Enable*` flags in
    `LocaltestEventsExtensions.cs`.
  - Upstream: [altinn-events#1149](https://github.com/Altinn/altinn-events/pull/1149) (open) runs Events locally with
    the Service Bus emulator and no code change; localtest could do the same at the cost of the emulator and its SQL
    Server container. [altinn-events#1076](https://github.com/Altinn/altinn-events/issues/1076) (open) moves the last
    listeners into the API.

- [ ] **Retire the legacy Storage Queue path.** Remove the Storage Queue publishers, `IEventsQueueClient` and the
      `inbound` / `outbound` callback endpoints the Azure Functions use, once the Functions are gone.
  - Why: locally the queue client has nothing to talk to; the fake answers with a clear failure.
  - Size: medium; the team's own plan.
  - Removes: `fakes/Queues/`.
  - Upstream: [altinn-events#1076](https://github.com/Altinn/altinn-events/issues/1076) (open, deletes
    `Events.Functions`); #1149 calls the path "on its way out".

- [ ] **Signing keys without Key Vault.** Part of the cross-cutting local configuration. `PublicSigningKeyProvider` from
      `Altinn.Common.AccessToken` (repository `Altinn/altinn-accesstoken`) requires a Key Vault URI. Let it read the
      public certificates from files when configured, as described in [`storage.md`](storage.md).
  - Why: without a vault the apps' platform access tokens cannot be validated.
  - Size: small in the package, then a package bump.
  - Removes: `fakes/Auth/`.
  - Upstream: [altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136) (open). #1149 uses an
    emulated Key Vault (Lowkey Vault) instead, which needs https and an emulated managed identity.

- [ ] **Publish the image where localtest can pull it.** The GitHub workflows build the image only for the Trivy scan
      (`container-scan.yml`) and push nothing; I could not confirm where the production image is built. Also push it
      to a public registry (for example `ghcr.io/altinn/altinn-events`), tagged by commit.
  - Why: a published image is the precondition for running Events unchanged.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: site `src/Events/Program.cs:69-71`: its defaults (endpoints, `AppsDomain`, cache times, retry delays)
    become environment variables in the manifest. The `Dockerfile`, `.dockerignore`, overrides and vendored copy go
    only if the local features under "Stays local" can be added to the published image from outside; they decorate
    the host's repositories and add a controller, and I could not confirm a way (a hosting startup assembly is one to
    try).
  - Upstream: none found.

## Stays local

- `fakes/Delivery/`: records every webhook POST and keeps deliveries to endpoints outside the stack on the machine.
- `fakes/Changes/` and `fakes/Inspection/`: change notifications and the anonymous `events/api/localtest/` inspection
  API and stream for tests and the GUI.
- `fakes/Startup/` (except the partition block above): the `localtest.delivery` table and the reset on every start.
- `LocaltestEventsExtensions.cs`, `LocaltestEventsOptions.cs` and site `src/Events/Program.cs:76-78`: the hook that
  registers the local features; it shrinks with the transport and signing-key items.
- `Dockerfile`: `COPY vendor/...` paths, the `/src` layout and the `DirectoryBuildPropsPath` /
  `DirectoryBuildTargetsPath` variables: the vendoring layout.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5080` and
  `Logging__LogLevel__Default=Information`: deployment settings, which upstream's chart supplies.
- `.dockerignore`: the component's own (`bin`, `obj`): the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, `CS1591` and the vendored-project test: the
  mechanism that carries the sites.
- `localtest.build.targets`: compiling `fakes/**/*.cs` and embedding `fakes/**/*.sql`: as above.

Coverage: 7 sites, 11 fakes (`Auth/`, `Changes/`, `Delivery/`, `Inspection/`, `Otlp/`, `Queues/`, `ServiceDiscovery/`,
`Sources/`, `Startup/`, `LocaltestEventsExtensions.cs`, `LocaltestEventsOptions.cs`), 2 build overrides, 2 Dockerfile
and 1 `.dockerignore` deviations, 1 Postgres init script: all accounted for.
