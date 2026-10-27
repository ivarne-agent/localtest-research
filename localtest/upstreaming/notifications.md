# Notifications: upstreaming

- Upstream: [Altinn/altinn-notifications](https://github.com/Altinn/altinn-notifications)
- Pin: `a4aa959` (main, 2026-10-20),
  [`../components/notifications/vendor/UPSTREAM.md`](../components/notifications/vendor/UPSTREAM.md); upstream `main` is
  12 commits ahead (`8512ecc`, checked 2026-10-27)
- Group: none
- Reminders: new Yuniql migrations (`Migration/` folders) run only on a fresh volume: `env reset -r localtest-v2 --yes`
  before the smoke test.
- Localtest modifications: 4 directive sites in `components/api/src/Altinn.Notifications/Program.cs`, the composition
  `fakes/LocaltestNotificationsExtensions.cs`, the transport replacement `fakes/Messaging/` with `fakes/Capture/`,
  `fakes/Startup/`, `fakes/Inspection/` and `fakes/LocaltestNotificationsOptions.cs`, the signing keys `fakes/Auth/`, the
  shared copies `fakes/Maskinporten/`, `fakes/ServiceDiscovery/` and `fakes/Otlp/`, two build overrides, the
  `localtest:` deviations in `Dockerfile` and `.dockerignore`, and `postgres-init/60-notifications.sh`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/notifications/` (sites relative to its `vendor/`).

Upstream is building its own local setup: an Aspire app host that runs the API, Email and SMS hosts with the Service
Bus emulator ([altinn-notifications#1930](https://github.com/Altinn/altinn-notifications/pull/1930), merged after our
pin), and a stack of open pull requests that add a token issuer, an ACS e-mail mock, WireMock stand-ins for Profile,
Register, Authorization and Maskinporten, and a control panel with inbox and phone tabs
([altinn-notifications#1963](https://github.com/Altinn/altinn-notifications/pull/1963) to
[#1978](https://github.com/Altinn/altinn-notifications/pull/1978), [#1990](https://github.com/Altinn/altinn-notifications/pull/1990)).
Several items below can reuse that work.

## Phase 1: easy fixes

- [x] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Merged upstream in
      [altinn-notifications#1930](https://github.com/Altinn/altinn-notifications/pull/1930) (2026-10-22, after our pin):
      `AddAzureMonitorTelemetryExporters` calls `UseOtlpExporter()` when the variable is set and
      `OtelSettings:Endpoint` is not. Block to remove at next sync, together with the next item.
  - Why: at the pin, Notifications exports only traces, and only with its own `OtelSettings:Endpoint` (endpoint set in
    code, so no `/v1/traces` for `http/protobuf`), plus Azure Monitor with a connection string.
  - Size: small (done).
  - Removes: site `Program.cs:191-198`, `fakes/Otlp/` and the `CS8321` `NoWarn` in `localtest.build.props` (the host
    already references the OTLP package). Without the next item, the stack's spans are named `platform-notifications`
    and `smoke-test.sh`'s OTLP check, which expects `notifications`, fails.
  - Upstream: [altinn-notifications#1930](https://github.com/Altinn/altinn-notifications/pull/1930), from issue
    [altinn-notifications#1925](https://github.com/Altinn/altinn-notifications/issues/1925).
  - Detail: [`../reviews/otel/notifications.md`](../reviews/otel/notifications.md).

- [ ] **Let `OTEL_SERVICE_NAME` win over the hard-coded service name.** In `ConfigureServices`, add the environment
      resource detector (`AddEnvironmentVariableDetector()`) after `AddAttributes(attributes)`, which sets `service.name`
      to `platform-notifications`; optionally the same in the Email and SMS hosts.
  - Why: a later resource wins over the SDK's environment detector, so the name the stack gives each container is
    ignored.
  - Size: trivial.
  - Removes: nothing on its own; it lets the item above remove site `Program.cs:191-198` without changing the stack's
    service names.
  - Upstream: none found.
  - Detail: [`../reviews/otel/README.md`](../reviews/otel/README.md) (the `service.name` and sampler notes to carry into
    the PR).

- [ ] **Bump `Altinn.ApiClients.Maskinporten` to 10.2.0.** `Directory.Packages.props` pins 10.1.0 on upstream `main`.
      Version 10.2.0 adds `TokenEndpoint`, `AssertionAudience` and `TokenExchangeEndpoint` to `MaskinportenSettings`,
      applied when a token is requested through a client definition, so the condition check's
      `SendConditionClient:MaskinportenSettings` can point at localtest's Maskinporten endpoint. Localtest then sets the
      three settings in its manifest, with a real test JWK instead of today's placeholder `EncodedJwk` (`e30=`);
      Notifications takes Digdir's raw Maskinporten token, so `TokenExchangeEndpoint` stays unset.
  - Why: 10.1.0 hard-codes the `maskinporten.no` hosts and `MaskinportenSettings` cannot override them, so localtest
    replaces the package's `IMaskinportenService`.
  - Size: trivial (Renovate may do it).
  - Removes: `fakes/Maskinporten/` and the `AddLocaltestMaskinporten` call in `LocaltestNotificationsExtensions.cs`.
  - Upstream: the package change is merged,
    [Altinn/altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92), released
    as v10.2.0 (2026-10-26); the consumer bump is in the open
    [altinn-notifications#1965](https://github.com/Altinn/altinn-notifications/pull/1965), which points the condition
    client at a Maskinporten mock the same way. Localtest's token endpoint reads only the assertion's payload, not its
    header or signature (`localtest/localtest/Maskinporten/MaskinportenApi.cs`), so the package's signed assertion
    should pass; I could not confirm it, nothing has run it.
  - Detail: [`../components/shared/README.md`](../components/shared/README.md), "`IMaskinportenService` fake".

- [ ] **Find the Yuniql migrations in the image in every environment.** `SetUpPostgreSql`
      (`Altinn.Notifications.Persistence/Extensions/WebApplicationExtensions.cs`) resolves `MigrationScriptPath` against
      the parent of the working directory in Development (made for `dotnet run` from the project folder, with
      `appsettings.Development.json`'s `Altinn.Notifications.Persistence/Migration`) and against the working directory
      otherwise. Fall back to the working directory when the Development path does not exist, or resolve against
      `AppContext.BaseDirectory` where `Migration/` is copied in the image.
  - Why: the image copies the migrations to `/app/Migration`; run with `ASPNETCORE_ENVIRONMENT=Development`, which the
    JWT cookie handler needs to read the OpenID metadata over http, it looks for them under `/`.
  - Size: trivial.
  - Removes: `PostgreSQLSettings__MigrationScriptPath=app/Migration` in the `Dockerfile`'s settings block (otherwise it
    moves to the manifest with the other settings).
  - Upstream: none found.

## Phase 2: structural changes

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`,
      in `Directory.Packages.props`) after `WebApplication.CreateBuilder`, so `PlatformSettings` endpoints such as
      `http://profile/profile/api/v1/` resolve through `services__<name>__http__0`. Agree with the team on the pattern
      the four `AltinnServiceDefaults` hosts already use (see the README's
      ["Across services"](README.md#across-services)).
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `Program.cs:40-45`, `fakes/ServiceDiscovery/` and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **Validate platform access tokens without Key Vault.** `PublicSigningKeyProvider` in `Altinn.Common.AccessToken`
      ([Altinn/altinn-accesstoken](https://github.com/Altinn/altinn-accesstoken)) reads each issuer's public certificate
      from Key Vault (`kvSetting:SecretUri`, secret `{issuer}-access-token-public-cert`) and builds a `SecretClient` in
      its constructor. Give the library, or Notifications' registration, a supported local source: for example a
      certificate file named by a setting (localtest's is `AccessTokenSettings:AccessTokenSigningCertificateFullPath`,
      the same certificate the production generator signs with), used when no Key Vault is configured.
  - Why: locally there is no Key Vault, so every platform access token from the apps and the other components would be
    rejected.
  - Size: small in code; the shape (library or each service) needs agreement, and it is part of "a supported local
    configuration without Key Vault" in the README's ["Across services"](README.md#across-services).
  - Removes: `fakes/Auth/` (the provider and its `RemoveAll`/`AddSingleton` in `LocaltestNotificationsExtensions.cs`).
  - Upstream: open issue [Altinn/altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136)
    "Improve ability to test locally, so Applications using PublicSigningKeyProvider without a valid URI can start".
    Profile has a copy of the same fake ([`profile.md`](profile.md)). Upstream's Aspire stack sidesteps it with
    enterprise tokens from its own issuer
    ([altinn-notifications#1963](https://github.com/Altinn/altinn-notifications/pull/1963)).
  - Detail: [`../components/notifications/README.md`](../components/notifications/README.md), "What is replaced".

- [ ] **A supported local transport without Service Bus.** `AddWolverineServices` throws without
      `WolverineSettings:ServiceBusConnectionString` and registers hosted services that connect at start, so localtest
      replaces the call: the three publishers (`IEmailCommandPublisher`, `IComposedEmailCommandPublisher`,
      `ISendSmsPublisher`) write to a capture, an in-process responder applies the send result and the delivery report
      through `UpdateSendStatus` as the production handlers do, and the Email and SMS hosts' instant endpoints are served
      on the API host. Agree with the team on one of two ways. Either a broker-less mode in the API host: without a
      connection string, hand the commands to a configurable HTTP sink and take results and delivery reports back over
      HTTP (or route them through Wolverine's local queues to the existing handlers), with localtest's inbox as that
      sink. Or localtest runs upstream's own local stack: the Service Bus emulator (with SQL Server), the Email and SMS
      hosts and the ACS and LinkMobility mocks, whose mirror posts (`MirrorUrl`,
      [altinn-notifications#1977](https://github.com/Altinn/altinn-notifications/pull/1977),
      [#1978](https://github.com/Altinn/altinn-notifications/pull/1978)) feed localtest's inbox; that is four or more
      containers more, which the plan rejected for memory.
  - Why: there is no Service Bus, Email or SMS host locally, and testers need to see every message sent.
  - Size: large; a design decision for the team.
  - Removes: site `Program.cs:242-246`, `fakes/Messaging/`, `fakes/Capture/`, `fakes/Inspection/`, `fakes/Startup/`
    (with `prepare.sql` and `reset-on-start.sql`), `fakes/LocaltestNotificationsOptions.cs`, the `EmbeddedResource` of
    `fakes/**/*.sql` in `localtest.build.targets`, and the instant endpoints `localhost:5090` in the configuration. The
    inbox and its `notifications/api/localtest/` API stay localtest's, but as a service of their own outside this image.
  - Upstream: none found for a broker-less mode; the Aspire stack above is upstream's current direction.
  - Detail: [`../../planned-components/notifications/notifications.md`](../../planned-components/notifications/notifications.md),
    options A and B; [`../components/notifications/README.md`](../components/notifications/README.md), "Delivery:
    everything ends `Delivered`".

- [ ] **Publish the image where localtest can pull it.** Upstream's GitHub workflows build the API image only to scan
      it (`.github/workflows/container-scan.yml`); I could not confirm where the deployed image is built and pushed.
      Push the same image to a public registry (for example `ghcr.io/altinn/altinn-notifications`), tagged by commit.
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the vendored copy
    itself.
  - Upstream: none found.

## Stays local

- Site `Program.cs:51-53` and `fakes/LocaltestNotificationsExtensions.cs`: localtest's composition. The configuration
  half sets deployment settings (the public origin for `GeneralSettings:BaseUri` and the OpenID metadata, service
  endpoints by name, one task and a 1 s idle delay per process loop, the SMS window 0 to 24, Dialogporten off, and
  `kvSetting:SecretUri` forced empty, which only guards: Key Vault needs all four `kvSetting` values); with a published
  image they move to the manifest. The rest is `AddLocaltestMessaging`, the replacement in the transport item; the file
  goes when those items are done.
- `Dockerfile`: the `/app/vendor` layout with `fakes/`, the overrides and `DirectoryBuildPropsPath` /
  `DirectoryBuildTargetsPath`: the vendoring layout; gone with the vendored copy.
- `Dockerfile`: the root build files and every `COPY` from `vendor/`: as above.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5090` and
  `Logging__LogLevel__Default=Information`: deployment settings, which upstream's chart supplies; with a published image
  they move to the manifest. Development already lets the JWT cookie handler read the OpenID metadata over http.
- `.dockerignore`: only `**/bin` and `**/obj`, for the component folder as build context: the vendoring layout.
- `localtest.build.props`: the import of upstream's `Directory.Build.props`, `LOCALTEST`, `TreatWarningsAsErrors=false`,
  `CS1591` and the vendored-project test: the mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: the import of upstream's `Directory.Build.targets` and compiling `fakes/**/*.cs` into the
  host: as above.
- `postgres-init/60-notifications.sh`: the database and the two roles on the shared Postgres: deployment, which
  production's infrastructure provisions.

Coverage: 4 sites, 10 fakes (`LocaltestNotificationsExtensions.cs`, `LocaltestNotificationsOptions.cs`, `Auth/`,
`Capture/`, `Inspection/`, `Maskinporten/`, `Messaging/`, `Startup/`, `ServiceDiscovery/`, `Otlp/`), 2 build overrides, 4
Dockerfile and 1 `.dockerignore` deviations, the Postgres init script: all accounted for.
