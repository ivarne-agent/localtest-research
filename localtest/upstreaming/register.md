# Register: upstreaming

- Upstream: [Altinn/altinn-register](https://github.com/Altinn/altinn-register) today; it moves into
  [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth) (`src/apps/Altinn.Register/`, a skeleton there so far).
- **Blocked on [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056)** ("Flytt altinn-register inn i
  altinn-auth", an issue, open on 2026-10-27). Its plan includes a freeze window in which open pull requests are merged
  or closed before the import. Target the structural items (phase 2) at altinn-auth after the move: their upstream
  issues already live there. The two phase 1 items are small enough for altinn-register before the freeze; otherwise
  they go to altinn-auth too. The OTLP change targets `Altinn/altinn-authorization-utils` and the signing keys
  `Altinn/altinn-accesstoken` in either case.
- Pin: `f6b5c60` (main, 2026-10-13), [`../components/register/vendor/UPSTREAM.md`](../components/register/vendor/UPSTREAM.md);
  upstream `main` has not moved since (checked 2026-10-27).
- Group: none
- Reminders: check altinn-auth#4056 and its linked pull requests at every sync; once the move has landed, retarget with
  `sync register --repository Altinn/altinn-auth --paths ... --group altinn-auth`.
- Localtest modifications: 1 directive site in `src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs`, the
  fakes project `fakes/Altinn.Register.Localtest/` (the hook `LocaltestRegisterExtensions.cs` with its options and
  project file, and the folders `Auth/`, `Controllers/`, `Data/`, `Otlp/`, `Persistence/`, `ServiceDiscovery/`,
  `TestData/`), the seed `testdata/`, two build overrides, and the `localtest:` deviations in `Dockerfile` plus its own
  `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/register/` (sites relative to its `vendor/`, fakes relative to `fakes/Altinn.Register.Localtest/`).
Register is an `AltinnServiceDefaults` host (`Altinn.Authorization.ServiceDefaults` 5.5.0).

## Phase 1: easy fixes

- [ ] **Let `OTEL_SERVICE_NAME` win over the host's name.** Nothing in Register itself: take the
      `Altinn.Authorization.ServiceDefaults` release that adds the environment resource detector after
      `AltinnServiceResourceDetector` (and makes OTLP and Azure Monitor independent), and bump the version in
      `Directory.Packages.props`.
  - Why: ServiceDefaults already exports over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, but its detector
    sets `service.name` `register` after the SDK's environment detector, so `OTEL_SERVICE_NAME` and
    `OTEL_RESOURCE_ATTRIBUTES` are ignored.
  - Size: trivial here (a version bump); the library change is small, see [`authorization-utils.md`](authorization-utils.md).
  - Removes: `Otlp/`, the `AddLocaltestOtlpResource` call in the hook and the `OpenTelemetry.Extensions.Hosting` and
    `OpenTelemetry.Exporter.OpenTelemetryProtocol` `PackageReference`s (and their `follow` entries) in
    `Altinn.Register.Localtest.csproj`.
  - Upstream: none found.
  - Detail: [`../reviews/otel/register.md`](../reviews/otel/register.md).

- [ ] **Drop the unused Application Insights reference.** Remove `Microsoft.ApplicationInsights.AspNetCore` from
      `Altinn.Register.csproj` and `Directory.Packages.props`, and the `Logging:ApplicationInsights` sections from
      `appsettings.json`, `appsettings.Development.json` and `appsettings.Staging.json`.
  - Why: no code uses it; the log-level sections configure a provider that is never added. Telemetry goes through
    ServiceDefaults.
  - Size: trivial.
  - Removes: nothing in localtest (cleanup the telemetry review found).
  - Upstream: none found.
  - Detail: [`../reviews/otel/register.md`](../reviews/otel/register.md), "Upstream change".

## Phase 2: structural changes

- [ ] **A supported local mode for the host.** Agree with the team on the LocalDev configuration contract
      (altinn-auth#4050) and make Register follow it from the environment alone: MassTransit, the import jobs, leases
      and CCR federation off, the v1 endpoints answering from the database, and the A2 and CCR request handlers'
      dependencies (`ICommandSender`, `ICcrUpdateFederator`, `ILocationLookupProvider`) either registered as
      not-available placeholders or the handlers left out when their features are off, so startup validation passes.
      In the same mode, outgoing platform access tokens must not come from the remote test token generator
      (`TryAddPlatformTokenProvider` picks it when `IsLocalDev`); a token signed with a configured certificate, or a
      local generator as altinn-auth#4052 proposes, works locally.
  - Why: localtest gets this today by inserting `Altinn:IsTest=true` and `Altinn:register:ApiSource:Default=DB` as
    lowest-priority configuration after `AltinnHost` has read its settings, so ServiceDefaults runs in environment
    `UNKNOWN` with the descriptor's `IsTest` off. Setting `IsTest` from the environment would also reach the
    descriptor, and `LOCAL` would fetch platform tokens over the internet. I could not confirm what else the
    descriptor's `IsTest` changes in ServiceDefaults.
  - Size: medium; needs the contract decided first.
  - Removes: the configuration defaults (`Altinn:IsTest`, `Altinn:register:ApiSource:Default`) and the three throwing
    registrations in the hook.
  - Upstream: [altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050) (ADR: LocalDev configuration
    contract) and [altinn-auth#4060](https://github.com/Altinn/altinn-auth/issues/4060) (Register in the AppHost), both
    open.
  - Detail: [`../reviews/register.md`](../reviews/register.md), finding 1;
    [`../components/register/README.md`](../components/register/README.md).

- [ ] **Run on Postgres with a supported test-data seed.** Replace the in-memory persistence with upstream's own
      Postgres persistence plus a seed localtest can supply: upstream already seeds embedded SQL through
      `SeedFromFileProvider` (`Altinn.Register.Persistence/TestData/[register.party]/*.sql`, applied when
      `Altinn:Npgsql:register:Seed:Enabled`), and altinn-auth#4054 plans a canonical test-data corpus with generated SQL.
      Localtest needs two things on top: a seed read from a mounted folder instead of the assembly, so its own
      `testData.json` (converted to that format) can be the source, and a supported way to add and replace parties
      while running, for the test data each app registers (I could not confirm that Register has a write path for
      persons and organisations outside its import jobs).
  - Why: Register needs Postgres and a populated party table; the localtest stack starts from `testData.json` plus the
    apps' test data and rebuilds the register when an app is registered.
  - Size: large; needs agreement on the seed format and the write path.
  - Removes: `Persistence/` (party and external-role persistence, the rate limiter, the store and its provider, the
    static role definitions), `Data/` (the embedded role-definition snapshot), their registrations in the hook and site
    `RegisterHost.cs:249-251` once the other hook items are done too.
  - Upstream: [altinn-auth#4054](https://github.com/Altinn/altinn-auth/issues/4054) (canonical test-data corpus and
    seeding) and [altinn-auth#4060](https://github.com/Altinn/altinn-auth/issues/4060), open.
  - Detail: [`../../planned-components/register/README.md`](../../planned-components/register/README.md),
    [`../../planned-components/register/production-register-analysis.md`](../../planned-components/register/production-register-analysis.md).

- [ ] **Platform access token keys without Key Vault.** In `Altinn.Common.AccessToken` (`Altinn/altinn-accesstoken`),
      let `PublicSigningKeyProvider` read the `{issuer}-access-token-public-cert` certificates from a configured file
      or folder when no `kvSetting:SecretUri` is set, instead of building a `SecretClient` for an empty URI. Part of
      "a supported local configuration without Key Vault" (see the README's
      ["Across services"](README.md#across-services)); the same fake exists in eight components.
  - Why: without Key Vault no platform access token can be validated; localtest validates every token against the
    shared test certificate.
  - Size: small in code; needs agreement with the owners of the package on the local mode.
  - Removes: `Auth/` and its registration in the hook.
  - Upstream: [altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136) (open: start without a
    valid Key Vault URI); altinn-auth#4052 proposes a local JWKS for platform tokens instead.
  - Detail: [`../components/register/README.md`](../components/register/README.md), "Run locally without Docker".

- [ ] **Service discovery without the pass-through provider.** Register already gets `AddServiceDiscovery()` from
      `AddAltinnServiceDefaults`; what remains is a supported way to drop the pass-through provider (a ServiceDefaults
      setting), agreed for all four `AltinnServiceDefaults` hosts (see the README's
      ["Across services"](README.md#across-services)).
  - Why: with pass-through a service name without a `services__<name>__http__0` entry goes to DNS and fails late;
    localtest wants it to fail at once.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: `ServiceDiscovery/`, its call in the hook and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` (and `follow` entry) in `Altinn.Register.Localtest.csproj`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **Publish the image where localtest can pull it.** altinn-register has no image workflow (it deploys through
      Azure DevOps, per altinn-auth#4056). In altinn-auth the CI pushes each app vertical's bake targets to
      `ghcr.io/altinn/altinn-auth/<vertical>-<app>:<sha>`, but `src/apps/Altinn.Register/docker-bake.hcl` is still a
      stub (empty default group, an inline `FROM aspnet-runtime`). After the move, give it a real target as
      Access Management has (`containers/Dockerfile.api`), and make the package public as
      `access-management-api` is (an anonymous pull of `register-api` is refused today).
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code after the move; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets`, the fakes project file
    and the vendored copy itself.
  - Upstream: [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056) (the move).

## Stays local

- `Controllers/`: the localtest API under `register/api/localtest/` (`test-users`, `organizations`, the app runner's
  `apps` registration and the KRR stand-in for `kontaktregisteret.no`): inspection endpoints and a stand-in for an
  external register. With a published image they move out of the Register into a localtest-owned service.
- `TestData/`: the `testData.json` contract and merge rule shared with the localtest app; localtest's test-data model.
- `testdata/`: the built-in seed (persons and organisations with synthetic identifiers): localtest's test data.
- The hook's `GeneralSettings:JwtCookieName` and `AccessTokenSettings:AccessTokenHeaderId` defaults and
  `LocaltestRegisterOptions` (`Localtest:Register:TestDataPath`): deployment settings; with a published image they move
  to the manifest or go with the in-memory store.
- `Dockerfile`: the build context is the component folder with `vendor/` as upstream's context, and
  `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `COPY testdata /testdata`: the seed above, baked in; with a published image it is mounted.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5020` and
  `Localtest__Register__TestDataPath`: deployment settings, which upstream's chart supplies; with a published image they
  move to the manifest.
- `.dockerignore`: localtest's own (`bin/`, `obj/`, `.vs/`, `Dockerfile`) for the component-folder context; upstream's
  stays in `vendor/`: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false` and the vendored-project test: the mechanism that
  carries the site; gone with it.
- `localtest.build.targets`: the `ProjectReference` to the fakes project: as above.

Coverage: 1 site, 7 fakes (`Auth/`, `Controllers/`, `Data/`, `Otlp/`, `Persistence/`, `ServiceDiscovery/`, `TestData/`)
plus the hook, 2 build overrides, 3 Dockerfile and 1 `.dockerignore` deviations: all accounted for.
