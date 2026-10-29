# Dialogporten adapter: upstreaming

- Upstream: [Altinn/altinn-dialogporten-adapter](https://github.com/Altinn/altinn-dialogporten-adapter)
- Pin: `86364b8` (main, 2026-10-19),
  [`../components/dialogporten-adapter/vendor/UPSTREAM.md`](../components/dialogporten-adapter/vendor/UPSTREAM.md)
- Group: none
- Reminders: a new required setting goes into `fakes/` `LocaltestAdapterExtensions.AddLocaltestConfiguration`.
- Localtest modifications: 10 directive sites (four hooks in `Program.cs`, three blocks in
  `Common/Extensions/ServiceCollectionExtensions.cs`, one in `Settings.cs`, two in `StorageDialogportenDataMerger.cs`),
  the fakes `LocaltestAdapterExtensions.cs`, `LocaltestAdapterOptions.cs`, `LocaltestAdapterTelemetry.cs`,
  `LocaltestLinks.cs` and the shared copies `Maskinporten/` and `ServiceDiscovery/` (the OTLP helper was inlined into
  `LocaltestAdapterTelemetry.cs` in task 63), three build overrides
  (`localtest.build.props`, `localtest.build.targets`, `.editorconfig`), and the `localtest:` deviations in
  `Dockerfile` and `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/dialogporten-adapter/` (sites relative to its `vendor/src/Altinn.DialogportenAdapter.WebApi/`). Upstream
`main` has two commits after the pin (`948e4c7`, `f1a3251`); they change `StorageDialogportenDataMerger.cs` around the
two self-link sites (the `SelfLinks.Platform` lines are at `:382` and `:409` upstream) but not the sites themselves.

Upstream's own local mode is the Azure Service Bus emulator (`docker-compose-service-bus.yml`: the emulator and MSSQL,
both `linux/amd64` only, about 2 GB) and an optional `LocalDevelopment` section that mocks the Dialogporten API. The
adapter plan rejected the emulator for localtest ("no Azure dependencies", the memory), so the transport is a phase 2
item below.

## Phase 1: easy fixes

- [ ] **Telemetry without Application Insights, OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** In
      `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs`, build the OpenTelemetry
      pipeline unconditionally (the resource, `HealthCheckFilterProcessor`, ASP.NET Core and HttpClient instrumentation
      for traces and metrics), add Azure Monitor only when the connection string is set (instead of
      `ApplicationInsights:Enabled`), and call `UseOtlpExporter()` when the variable is set (package
      `OpenTelemetry.Exporter.OpenTelemetryProtocol`).
  - Why: without Application Insights the adapter has no telemetry pipeline at all, and `ConfigureTelemetry` throws
    without a connection string, so a local run exports nothing.
  - Size: small (one method restructured; the distro's defaults become explicit).
  - Removes: site `Common/Extensions/ServiceCollectionExtensions.cs:56-65`, `fakes/LocaltestAdapterTelemetry.cs`,
    the OTLP `PackageReference` in `localtest.build.targets` (the OTLP rule, inlined in the telemetry fake since task 63).
  - Upstream: none found.
  - Detail: [`../reviews/otel/dialogporten-adapter.md`](../reviews/otel/dialogporten-adapter.md), with the 5 % sampler
    question and the health filter's `Recorded` flag. Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [x] **Configurable Maskinporten and token-exchange endpoints in `Altinn.ApiClients.Maskinporten`.** Merged as
      [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92) (2026-10-26)
      and released in 10.2.0 (2026-10-26): `MaskinportenSettings` has `TokenEndpoint`, `AssertionAudience` and
      `TokenExchangeEndpoint`, applied when tokens are requested through a client definition.
  - Why: the package hard-coded `https://*.maskinporten.no` and the exchange hosts.
  - Size: small (done).
  - Removes: nothing on its own; the consumer bump below removes the fake.
  - Upstream: [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92),
    merged.
  - Detail: [`../reviews/2026-10-24-maskinporten-pr92.md`](../reviews/2026-10-24-maskinporten-pr92.md).

- [ ] **Bump `Altinn.ApiClients.Maskinporten` to 10.2.0.**
      `src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj` references 10.1.0 (also on
      upstream `main`). Then set `DialogportenAdapter:Maskinporten:TokenEndpoint`, `AssertionAudience` and
      `TokenExchangeEndpoint` in localtest's manifest, and a throwaway JWK as `EncodedJwk` instead of today's
      `{"kty":"RSA"}` placeholder (the package signs the assertion). Every client of the adapter (Storage, Register,
      Dialogporten) registers through `AddMaskinportenHttpMessageHandler<SettingsJwkClientDefinition>` with
      `DialogportenAdapter:Maskinporten`, so the new keys bind without code changes.
  - Why: without the bump the package can only call the real Maskinporten, so localtest replaces `IMaskinportenService`.
  - Size: trivial.
  - Removes: `fakes/Maskinporten/`, its `AddLocaltestMaskinporten` call in `fakes/LocaltestAdapterExtensions.cs` and the
    `fakes/Maskinporten` section of `.editorconfig`.
  - Upstream: none found (no open Renovate PR for 10.2.0). The localtest token endpoint reads the assertion's payload
    only, not its header or signature (`../localtest/Maskinporten/MaskinportenApi.cs`, `TryReadAssertion`), so a signed
    assertion passes.
  - Detail: [`../components/shared/README.md`](../components/shared/README.md), "`IMaskinportenService` fake"; TODO
    task 47 in [`../../TODO.md`](../../TODO.md) (#92, released in 10.2.0 on 2026-10-26; it superseded task 15).

- [ ] **Allow OpenID metadata over http in Development.** In `ServiceCollectionExtensions.cs`, the `AddJwtBearer` for
      `JwtBearerDefaults.AuthenticationScheme` sets `MetadataAddress` from `Authentication:JwtBearerWellKnown`; also set
      `options.RequireHttpsMetadata = !environment.IsDevelopment()` (or from a setting).
  - Why: the stack's Authentication publishes its metadata over plain http, which the bearer scheme refuses.
  - Size: trivial.
  - Removes: the `Configure<JwtBearerOptions>` in `fakes/LocaltestAdapterExtensions.cs`; with the Maskinporten bump, the
    whole `AddLocaltestAdapter` and its site `Program.cs:55-57`.
  - Upstream: none found.
  - Detail: [`../components/dialogporten-adapter/README.md`](../components/dialogporten-adapter/README.md), "What is
    faked, and why".

- [ ] **Configurable app and platform origins for the links in dialogs.** `Settings.cs`, `AltinnPlatformSettings`:
      `GetAppUriForOrg` builds `{scheme}://{org}.apps.{host}/{appId}` and `GetPlatformUri` `{scheme}://platform.{host}`
      from `Altinn:BaseUri`, dropping the port. Add optional settings (for example `Altinn:PlatformUri` and an apps
      template such as `Altinn:AppsUriTemplate` with `{org}`), defaulting to the current expressions.
  - Why: localtest serves apps and platform on one origin with a port, `http://local.altinn.cloud:8000`, so every go-to,
    read-API, receipt and attachment link would point at sub-domains that do not resolve.
  - Size: small.
  - Removes: site `Settings.cs:43-50`.
  - Upstream: none found.
  - Detail: `UPSTREAM.md`, the `Settings.cs` row.

- [ ] **Build attachment URLs on the platform origin.** `Features/Command/Sync/StorageDialogportenDataMerger.cs`,
      `CreateAttachmentDto` and `CreateTransmissionAttachmentDto`, use `element.SelfLinks.Platform` as Storage wrote it.
      Take its path and query and put them on `GetPlatformUri()`, which the adapter already uses for every other
      platform link.
  - Why: Storage's Interface package writes `https://platform.{Hostname}/storage/...` into every self link, which
    neither resolves nor serves TLS locally.
  - Size: small.
  - Removes: sites `Features/Command/Sync/StorageDialogportenDataMerger.cs:366-371` and `:398-403`, and
    `fakes/LocaltestLinks.cs`.
  - Upstream: none found. The other way to remove these sites is Storage's self-link base, a phase 2 item in
    [`storage.md`](storage.md); either one is enough for the adapter. This item needs the previous one, since it relies
    on `GetPlatformUri()` giving the local origin.
  - Detail: `UPSTREAM.md`, the `StorageDialogportenDataMerger.cs` row.

## Phase 2: structural changes

- [ ] **An in-process transport for local runs.** In `ServiceCollectionExtensions.cs`, `AddInfrastructure`, make the
      Azure Service Bus bootstrap (`ConfigureAdapterDefaults`, which throws on an empty connection string) and the two
      queue listeners conditional on a setting (for example `WolverineSettings:UseLocalQueue`, Development only): then
      the three non-transport defaults, one sequential local queue under `ContractConstants.AdapterQueueName` and the
      publish rule for `SyncInstanceCommand`. Add a Development-only endpoint that publishes a posted
      `SyncInstanceCommand` to that queue, so Storage's outbox can hand commands over HTTP and the production handler with
      its retry and error policies processes them.
  - Why: localtest has no Service Bus; the emulator is upstream's local mode but needs two `linux/amd64` containers and
    about 2 GB. Posting to the production `syncDialog` instead would bypass the queue's policies.
  - Size: medium; a design decision for the team (a development endpoint in a production service).
  - Removes: sites `Common/Extensions/ServiceCollectionExtensions.cs:69-81` and `:185-197`, `Program.cs:62-64`, the
    enqueue endpoint `MapLocaltestRoutes` in `fakes/LocaltestAdapterExtensions.cs` and the placeholder
    `WolverineSettings:ServiceBusConnectionString` default.
  - Upstream: none found.
  - Detail: [`../../planned-components/dialogporten/dialogporten-adapter.md`](../../planned-components/dialogporten/dialogporten-adapter.md),
    options (a) to (c); TODO task 10 in [`../../TODO.md`](../../TODO.md). Storage's side is in [`storage.md`](storage.md).

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder` in `Program.cs`, so `InternalStorageEndpoint`, `InternalRegisterEndpoint` and
      `Dialogporten:BaseUri` such as `http://storage/` resolve through `services__<name>__http__0` (pattern in the
      README's ["Across services"](README.md#across-services)). Upstream already separates the internal endpoints from
      the public base, so no setting needs splitting.
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `Program.cs:31-36`, `fakes/ServiceDiscovery/`, the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets` and the `fakes/ServiceDiscovery` section of `.editorconfig`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), the
    adapter table.

- [ ] **Publish the image where localtest can pull it, and configure it from the manifest.** No workflow in the
      repository pushes an image (`.github/workflows/` only builds one for the Trivy scan); I could not confirm where the
      deployed image is built. Push it to a public registry (for example `ghcr.io/altinn/altinn-dialogporten-adapter`),
      tagged by commit. Localtest then moves the defaults of `AddLocaltestConfiguration` (the public and internal
      endpoints, the subscription key, the feature flag, the JWT well-known URL, the Maskinporten settings, the log
      level) and `ASPNETCORE_ENVIRONMENT`/`ASPNETCORE_URLS` into its manifest.
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: site `Program.cs:48-50`, the rest of `fakes/LocaltestAdapterExtensions.cs`, `fakes/LocaltestAdapterOptions.cs`,
    `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets`, `.editorconfig` and the vendored
    copy itself.
  - Upstream: none found.

## Stays local

- `Dockerfile`: `COPY` of the overrides, `.editorconfig` and `fakes/`, `DirectoryBuildPropsPath` and
  `DirectoryBuildTargetsPath`, and the `vendor/` prefix with the `/src/vendor` working directory: the vendoring layout;
  gone with the vendored copy.
- `Dockerfile`: `dotnet build -o /app/out` instead of `-o out`: follows from the layout, so the final stage copies the
  same path as upstream's.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://+:5011`: deployment settings, which
  upstream's deployment supplies; with a published image they move to the manifest.
- `.dockerignore`: keeps host `bin/`, `obj/` and the component's own files out of the build context: the vendoring
  layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false` and the vendored-project test: the mechanism that
  carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.
- `.editorconfig`: analyzer exceptions for the shared copies only; each section goes with its copy (items above).

Coverage: 10 sites, 6 fakes (`LocaltestAdapterExtensions.cs`, `LocaltestAdapterOptions.cs`, `LocaltestAdapterTelemetry.cs`,
`LocaltestLinks.cs`, `Maskinporten/`, `ServiceDiscovery/`), 3 build overrides, 3 Dockerfile and 1
`.dockerignore` deviations: all accounted for.
