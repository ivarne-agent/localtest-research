# Profile: upstreaming

- Upstream: [Altinn/altinn-profile](https://github.com/Altinn/altinn-profile)
- Pin: `d0cd92d` (main, 2026-10-22), [`../components/profile/vendor/UPSTREAM.md`](../components/profile/vendor/UPSTREAM.md);
  upstream `main` has not moved since (checked 2026-10-27)
- Group: none
- Reminders: none.
- Localtest modifications: 4 directive sites in `src/Altinn.Profile/Program.cs`, the composition
  `fakes/LocaltestProfileExtensions.cs` with `fakes/LocaltestProfileOptions.cs`, the stand-ins `fakes/Auth/`,
  `fakes/Krr/` and `fakes/Brreg/`, the seed and start-up `fakes/Register/`, `fakes/Seed/` and `fakes/Startup/`, the
  inspection API `fakes/Inspection/`, the shared copy `fakes/ServiceDiscovery/` (the OTLP helper's copy was inlined into site 4 in task 63), two build
  overrides, the `localtest:` deviations in `Dockerfile` and `.dockerignore`, and `postgres-init/80-profile.sh`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/profile/` (sites relative to its `vendor/`).

Upstream is building its own local setup: the open pull request
[altinn-profile#1073](https://github.com/Altinn/altinn-profile/pull/1073) adds an Aspire app host (issue
[altinn-profile#214](https://github.com/Altinn/altinn-profile/issues/214)), runs the EF migrations as a separate step
and adds OTLP. It mirrors the one Notifications merged; see [`notifications.md`](notifications.md).

## Phase 1: easy fixes

- [ ] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Add `OpenTelemetry.Exporter.OpenTelemetryProtocol`
      (at the host's OpenTelemetry version, 1.20.0) to `Altinn.Profile.csproj` and call `UseOtlpExporter()` in
      `ConfigureServices` when the variable is set, next to the Azure Monitor block.
  - Why: without it, Profile sends telemetry only to Azure Monitor with a connection string, so a local run exports
    nothing.
  - Size: trivial.
  - Removes: the inlined OTLP rule in site `Program.cs:152-165` (the copy of `fakes/Otlp/` until task 63), the OTLP
    `PackageReference` in `localtest.build.targets` and the `CS8321` `NoWarn` in `localtest.build.props`; the site becomes an `#if LOCALTEST` addition of
    `AddEnvironmentVariableDetector()` after `AddService`, so the stack's spans keep the name `profile` that
    `smoke-test.sh` checks (see "Not needed").
  - Upstream: open pull request [altinn-profile#1073](https://github.com/Altinn/altinn-profile/pull/1073) adds the
    package at 1.20.0 and `UseOtlpExporter()` inside `AddAzureMonitorTelemetryExporters` when the variable is set. It
    does not add the environment detector, which localtest keeps as its own line (see "Not needed").
  - Detail: [`../reviews/otel/profile.md`](../reviews/otel/profile.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Apply the migrations at start-up when `PostgreSqlSettings:MigrateOnStartup` is true.** `Program.cs` runs the
      same `Migrate()` as `--run-db-migrations`, with the admin connection string, before the application starts; the
      setting defaults to `false`, so deployments (the pre-upgrade hook) are unchanged.
  - Why: Profile migrates only in `--run-db-migrations` mode, so a container configured by environment variables alone
    cannot start on an empty database without a one-shot migration step first. The stack's rule is that a service
    migrates at start (TODO.md task 62), as Dialogporten, Correspondence, Storage, Events and Notifications do.
  - Size: small.
  - Removes: the `Migrate()` step in `fakes/Startup/LocaltestDatabaseStartup.cs` (the manifest sets
    `PostgreSqlSettings__MigrateOnStartup=true` instead); the start-up seed in the same file stays (see "Stays local").
  - Upstream: open pull request [altinn-profile#1076](https://github.com/Altinn/altinn-profile/pull/1076) (2026-10-29,
    from the fork branch `feature/migrate-on-startup-in-development`).
  - Detail: `/home/agent/code/.handover/altinn-profile-upstream/migrate-on-startup.md` (the PR's description).

- [ ] **Bump `Altinn.ApiClients.Maskinporten` to 10.2.0.** `src/Altinn.Profile.Core/Altinn.Profile.Core.csproj`
      references 10.1.0 on upstream `main`. Version 10.2.0 adds `TokenEndpoint`, `AssertionAudience` and
      `TokenExchangeEndpoint` to `MaskinportenSettings`, applied when a token is requested through a client definition,
      so the KRR client's `ContactAndReservationSettings:MaskinportenSettings` can point at localtest's Maskinporten
      endpoint.
  - Why: 10.1.0 hard-codes the `maskinporten.no` hosts, so the production KRR client cannot get a token locally.
  - Size: trivial (Renovate may do it).
  - Removes: nothing on its own; Profile has no `fakes/Maskinporten/` copy because its KRR and brreg clients are replaced
    whole. It is the prerequisite for the KRR item in phase 2.
  - Upstream: the package change is merged,
    [Altinn/altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92), released
    as v10.2.0 (2026-10-26); the consumer bump in Profile: none found. Localtest's token endpoint reads only the
    assertion's payload, not its header or signature (`localtest/localtest/Maskinporten/MaskinportenApi.cs`), so the
    package's signed assertion should pass; I could not confirm it, nothing has run it.
  - Detail: [`../components/shared/README.md`](../components/shared/README.md), "`IMaskinportenService` fake".

## Phase 2: structural changes

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder`, so `RegisterSettings:ApiRegisterEndpoint`,
      `PlatformSettings:ApiAuthorizationEndpoint` and `NotificationsSettings:ApiNotificationsEndpoint` such as
      `http://register/register/api/` resolve through `services__<name>__http__0`. Agree with the team on the pattern
      the four `AltinnServiceDefaults` hosts already use (see the README's
      ["Across services"](README.md#across-services)).
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `Program.cs:54-59`, `fakes/ServiceDiscovery/` and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **Validate platform access tokens without Key Vault.** `PublicSigningKeyProvider` in `Altinn.Common.AccessToken`
      ([Altinn/altinn-accesstoken](https://github.com/Altinn/altinn-accesstoken)) reads each issuer's public certificate
      from Key Vault (`kvSetting:SecretUri`, secret `{issuer}-access-token-public-cert`) and builds a `SecretClient` in
      its constructor. Give the library, or Profile's registration, a supported local source: for example a certificate
      file named by a setting (localtest's is `AccessTokenSettings:AccessTokenSigningCertificateFullPath`, the same
      certificate the production generator signs with), used when no Key Vault is configured.
  - Why: locally there is no Key Vault, so every platform access token from the apps and the other components would be
    rejected.
  - Size: small in code; the shape (library or each service) needs agreement, and it is part of "a supported local
    configuration without Key Vault" in the README's ["Across services"](README.md#across-services).
  - Removes: `fakes/Auth/` (the provider and its `RemoveAll`/`AddSingleton` in `LocaltestProfileExtensions.cs`).
  - Upstream: open issue [Altinn/altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136)
    "Improve ability to test locally, so Applications using PublicSigningKeyProvider without a valid URI can start".
    The same fake is in Notifications ([`notifications.md`](notifications.md)), from which Profile's is copied.
  - Related: [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280) (open, by another contributor,
    2026-10-28) reads the issuers' public certificate from a file (`AccessTokenSettings:PublicSigningCertificatePath`)
    when no Key Vault is configured; released and bumped, it replaces `LocaltestPublicSigningKeyProvider`. It removes
    nothing yet. This host binds `AccessTokenSettings`, so the setting would take effect.
  - Detail: [`../components/profile/README.md`](../components/profile/README.md), "What is replaced".

- [ ] **Reach KRR and brreg through HTTP stand-ins instead of in-process fakes.** Upstream needs little beyond the
      Maskinporten bump in phase 1: `ContactAndReservationSettings:ChangesLogEndpoint` and
      `OrganizationNotificationAddressSettings:ChangesLogEndpoint` and `UpdateEndpoint` are settings already, and
      `IsValidUrl` accepts http. Agree with the team that a stand-in serving KRR's `hentEndringer` and brreg's
      `kof-sync` and `kof-reception` APIs is the supported local way (upstream's Aspire work in Notifications uses
      WireMock stand-ins for platform services the same way,
      [altinn-notifications#1965](https://github.com/Altinn/altinn-notifications/pull/1965)), and ask how the brreg
      client authenticates: its `MaskinportenSettings` is bound, but at the pin nothing registers a Maskinporten handler
      or an `HttpClient` for `OrganizationNotificationAddressHttpClient` (I could not confirm how production supplies
      them). On our side the two stand-ins move to the Register component, which owns the test data and already serves
      a KRR stand-in (`register/api/localtest/krr/...`).
  - Why: the fakes replace `IContactRegisterHttpClient`, `IOrganizationNotificationAddressUpdateClient` and
    `IOrganizationNotificationAddressSyncClient` inside the host, so a published image cannot run without them compiled
    in.
  - Size: small upstream; medium on our side (the change logs with change numbers, the brreg echo that accepts written
    addresses, both now in `fakes/`).
  - Removes: `fakes/Krr/` and `fakes/Brreg/` (with their registrations in `LocaltestProfileExtensions.cs`).
  - Upstream: none found.
  - Detail: [`../components/profile/README.md`](../components/profile/README.md), "The KRR stand-in as a change log" and
    "The brreg stand-in"; [`../../planned-components/profile/profile.md`](../../planned-components/profile/profile.md).

- [ ] **Publish the image where localtest can pull it.** Upstream's GitHub workflows build the image only to scan it
      (`.github/workflows/container-scan.yml`, tagged `altinn-profile:<sha>`, not pushed); I could not confirm where the
      deployed image is built and pushed. Push the same image to a public registry (for example
      `ghcr.io/altinn/altinn-profile`), tagged by commit.
  - Why: once the items above are merged and the seed has moved out of the host (below), upstream's image runs locally
    unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the vendored copy
    itself.
  - Upstream: none found.

## Not needed

- **Let `OTEL_SERVICE_NAME` win over the hard-coded service name** (the environment detector after `AddService`):
  localtest gets the same with an `#if LOCALTEST` line once the OTLP item is in, and the hard-coded `platform-profile`
  is production's name, so nothing local depends on an upstream change. Aspire's dashboard links no container's
  telemetry to its resource either way (TODO.md section 61). Decided 2026-10-29; the branch `otel-service-name` on the
  fork `ivarne-agent/altinn-profile` (on altinn-profile#1073) stays unopened, and no comment goes to #1073.

## Stays local

- Site `Program.cs:152-158` once the OTLP item is in (the lines it would then have): the environment detector after `AddService`, so the stack's
  `OTEL_SERVICE_NAME` wins (see "Not needed").

- Sites `Program.cs:64-66` and `Program.cs:243-245`, `fakes/LocaltestProfileExtensions.cs` and
  `fakes/LocaltestProfileOptions.cs`: localtest's composition. The configuration half sets deployment settings (the
  OpenID endpoint on the public origin, service endpoints by name, both sync jobs every minute, the 10 s Register id
  cache, `kvSetting:SecretUri` forced empty although upstream never sets it); with a published image they move to the
  manifest. The service half registers the replacements of the items above and the seed; both hooks go when those are
  done and the seed has moved out (next line).
- `fakes/Register/`, `fakes/Seed/`, `fakes/Startup/` and `fakes/Inspection/`: localtest's test-data seed (settings once
  per test user, the Register's localtest API), the migrations at start and `profile/api/localtest/`: test-only. To run
  a published image they move out of the host: the migrations through upstream's own start-up migration once
  [altinn-profile#1076](https://github.com/Altinn/altinn-profile/pull/1076) is in (the phase 1 item above; a service
  migrates at start, so no one-shot `--run-db-migrations` container), the settings seed through Profile's API, the
  inspection reading Postgres.
- `Dockerfile`: the `/app/vendor` layout with `fakes/`, the overrides and `DirectoryBuildPropsPath` /
  `DirectoryBuildTargetsPath`, and the `COPY vendor/...` paths: the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5030` and
  `Logging__LogLevel__Default=Information`: deployment settings, which upstream's chart (and its `docker-compose.yml`
  for the URL) supplies; with a published image they move to the manifest. Development already lets the JWT cookie
  handler read the OpenID metadata over http, so nothing upstream is needed for it.
- `.dockerignore`: only `**/bin` and `**/obj`, for the component folder as build context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, `CS1591` and the vendored-project test: the
  mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.
- `postgres-init/80-profile.sh`: the database and the two roles on the shared Postgres: deployment, which production's
  infrastructure provisions.

Coverage: 4 sites, 10 fakes (`LocaltestProfileExtensions.cs`, `LocaltestProfileOptions.cs`, `Auth/`, `Krr/`, `Brreg/`,
`Register/`, `Seed/`, `Startup/`, `Inspection/`, `ServiceDiscovery/`), 2 build overrides, 3 Dockerfile and 1
`.dockerignore` deviations, the Postgres init script: all accounted for.
