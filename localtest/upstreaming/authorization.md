# Authorization (PDP): upstreaming

- Upstream: [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth), `src/apps/Altinn.Authorization/` (with
  `src/libs/Altinn.Authorization.Api.Contracts/` and `src/pkgs/` ABAC and PEP)
- Pin: `3d51810` (main, 2026-10-19),
  [`../components/authorization/vendor/UPSTREAM.md`](../components/authorization/vendor/UPSTREAM.md); upstream has
  moved the release build to `docker-bake.hcl` and `containers/Dockerfile.api` since (2026-10-27), and none of the 33
  commits since touches the directive sites (checked 2026-10-27)
- Group: `altinn-auth`, one pin shared with [Authentication](authentication.md) and
  [Access Management](access-management.md); the three move together.
- Reminders: none.
- Host: a plain `WebApplication.CreateBuilder` host, not an `AltinnServiceDefaults` one: it references
  `Altinn.Authorization.ServiceDefaults` 5.5.0 but never calls it (only its transitive packages are used), and its
  telemetry is the classic Application Insights SDK.
- Localtest modifications: 3 directive sites in `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs` and
  one each in `Services/Implementation/OedRoleAssignmentWrapper.cs` and `PolicyRetrievalPoint.cs` (task 63), the
  composition `fakes/LocaltestAuthorizationExtensions.cs` with `fakes/LocaltestAuthorizationOptions.cs`, the telemetry
  `fakes/LocaltestAuthorizationTelemetry.cs`, the policy store `fakes/Policies/`, the Azure and Key Vault stand-ins
  `fakes/Infrastructure/`, the test API `fakes/Controllers/`, the shared copies
  `fakes/ServiceDiscovery/` (the OTLP helper was inlined into the telemetry fake in task 63), two build overrides, and the `localtest:` deviations in `Dockerfile` and
  `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/authorization/` (sites relative to its `vendor/`).

Upstream plans to move the host to ServiceDefaults and into a local Aspire app host:
[altinn-auth#4064](https://github.com/Altinn/altinn-auth/issues/4064) (under the epic
[altinn-auth#4048](https://github.com/Altinn/altinn-auth/issues/4048)), with Azurite for the policies and the event
queue. That would cover the telemetry and service-discovery items below, but Azurite is not what localtest runs.

## Phase 1: easy fixes

- [ ] **One OpenTelemetry pipeline instead of the classic Application Insights SDK.** In `ConfigureServices`, replace
      the Application Insights block (`AddApplicationInsightsTelemetry`, its two telemetry processors and initializer,
      and the metrics-only Azure Monitor pipeline) and the Application Insights branch of `ConfigureLogging` with
      `AddOpenTelemetry()`: `service.name` `platform-authorization`, traces (ASP.NET Core without `/health`, HttpClient),
      metrics (ASP.NET Core, HttpClient, runtime, `DecisionTelemetry`), logs; `UseAzureMonitor` when the connection
      string is set and `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT` is set. Delete `HealthTelemetryFilter`,
      `IdentityTelemetryFilter` (its tags become `EnrichWithHttpRequest`) and `CustomTelemetryInitializer`, and drop
      `Microsoft.ApplicationInsights.AspNetCore`.
  - Why: the PDP ignores `OTEL_EXPORTER_OTLP_ENDPOINT`; without the Key Vault key it has no OpenTelemetry provider at
    all, so not even the decision meter goes anywhere.
  - Size: small (one host file, three classes deleted, one package swapped); the Application Insights queries and
    sampling rate need a look by the team.
  - Removes: site `Program.cs:278-304` and `fakes/LocaltestAuthorizationTelemetry.cs` (with the OTLP rule inlined in it
    since task 63).
  - Upstream: none found for the change itself; moving the host to ServiceDefaults
    ([altinn-auth#4064](https://github.com/Altinn/altinn-auth/issues/4064)) would bring the same and is large.
  - Detail: [`../reviews/otel/authorization.md`](../reviews/otel/authorization.md), option 1.

- [ ] **Drop the cached app policy when it is written.** `Services/Implementation/PolicyAdministrationPoint.cs`,
      `WritePolicyAsync`, stores a new app policy, but `PolicyRetrievalPoint` keeps serving the old document until
      `GeneralSettings:PolicyCacheTimeout` (whole minutes, 10 by default) expires. Remove the policy path's cache entry
      after a successful write.
  - Why: the app runner pushes a policy and the next decision should use it; locally the shortest timeout is still a
    minute.
  - Size: small.
  - Removes: the directive site in `PolicyRetrievalPoint.GetPolicyAsync(org, app)` (it replaced
    `fakes/Policies/LocaltestPolicyRetrievalPoint.cs` in task 63).
  - Upstream: none found. With several replicas only the one that took the write sees it at once; I could not confirm
    how many production runs, but nothing gets worse.
  - Detail: [`../components/authorization/README.md`](../components/authorization/README.md), "Fakes".

## Phase 2: structural changes

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` after `WebApplication.CreateBuilder` (the
      package already comes in through ServiceDefaults 5.5.0), with the pattern the four `AltinnServiceDefaults` hosts
      use; or get it from the move to ServiceDefaults
      ([altinn-auth#4064](https://github.com/Altinn/altinn-auth/issues/4064)), after which the pass-through question in
      [`authorization-utils.md`](authorization-utils.md) applies. See the README's
      ["Across services"](README.md#across-services).
  - Why: the stack injects `services__<name>__http__0` per container; without service discovery every URL has to be a
    resolvable host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `Program.cs:53-58` and `fakes/ServiceDiscovery/`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **A supported local configuration without Azure Blob, the audit queue and Key Vault.** Production needs Azure Blob
      for `metadata` and `delegationpolicies`, an Azure Storage queue for the audit log, Key Vault certificates for
      incoming platform access tokens, and the test-tool token generator in Development. Agree on a local mode: a file
      policy store (shared with Access Management and the Resource Registry, which write the same layout), the audit
      log off without a queue, the certificate from a file for every issuer, and the production generator signing with
      a local certificate. The README's ["Across services"](README.md#across-services) has the cross-service proposal.
  - Why: none of the three Azure services exists locally, and localtest does not run Azurite.
  - Size: large; the policy store is shared by three hosts.
  - Removes: `fakes/Policies/`, `fakes/Infrastructure/` (`LocaltestPublicSigningKeyProvider`; the audit queue's no-op
    went in task 63, the forced-off audit log leaves the production client uncalled), the generator and `AccessTokenSettings` replacements in the hook, and the
    `/policy-store` deviation in `Dockerfile`.
  - Upstream: related [altinn-auth#4064](https://github.com/Altinn/altinn-auth/issues/4064), which proposes Azurite
    instead; the difference needs raising there.
  - Related: [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280) (open, by another contributor,
    2026-10-28) reads the issuers' public certificate from a file (`AccessTokenSettings:PublicSigningCertificatePath`)
    when no Key Vault is configured; released and bumped, it replaces `LocaltestPublicSigningKeyProvider`. It removes
    nothing yet. This host binds only `kvSetting`, not `AccessTokenSettings`, so binding that section is the
    prerequisite (one of five hosts, see the README's ["Across services"](README.md#across-services)).
  - Detail: [`../reviews/authorization.md`](../reviews/authorization.md), "Fakes and what they replace".

- [x] **Publish the image where localtest can pull it.** [altinn-auth#4739](https://github.com/Altinn/altinn-auth/pull/4739)
      (merged 2026-10-27) and `6228127` give `docker-bake.hcl` a default target over
      `src/apps/Altinn.Authorization/containers/Dockerfile.api`, so CI pushes `ghcr.io/altinn/altinn-auth/authorization-api:<sha>`;
      it pulls anonymously (checked 2026-10-27).
  - Why: once the items above are merged, upstream's image runs locally unchanged.
  - Size: none left upstream.
  - Removes: at the next sync, `Dockerfile` follows `containers/Dockerfile.api` instead of the classic one; once the
    other items land, `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the
    vendored copy.
  - Upstream: [altinn-auth#4739](https://github.com/Altinn/altinn-auth/pull/4739).

## Stays local

- Site `Program.cs:67-71`, the hook `AddLocaltestAuthorization`: it carries the test-only fakes below; it stays while
  they do.
- `fakes/LocaltestAuthorizationExtensions.cs` and `fakes/LocaltestAuthorizationOptions.cs`, configuration defaults:
  service names, the public origin for the JWT cookie metadata and SBL links, one-minute caches, production's feature
  flags with the audit log off: deployment settings; with the environment they need no `JwtCookieOptions` re-apply.
- The directive site in `OedRoleAssignmentWrapper.GetOedRoleAssignments` (task 63, in place of `fakes/Pip/`): an empty
  answer instead of the external Digitalt dødsbo API reached through Maskinporten.
- `fakes/Controllers/`: the anonymous `localtest/decision` and `localtest/refresh` endpoints for tests and debugging.
- `Dockerfile`: `COPY vendor/...` and the `/src` layout with `DirectoryBuildPropsPath` and `DirectoryBuildTargetsPath`:
  the vendoring layout.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `Logging__LogLevel__Default=Information`,
  `ASPNETCORE_URLS=http://+:5070` and `Localtest__Authorization__PolicyStorePath`: deployment settings; Development is
  what lets the JWT cookie handler read the OpenID metadata over http, so nothing upstream is needed for it.
- `.dockerignore`: build output and the Dockerfile kept out of the context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the documentation `NoWarn` and the
  vendored-project test: the mechanism that carries the sites.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.

Coverage: 5 sites, 7 fakes (`LocaltestAuthorizationExtensions.cs`, `LocaltestAuthorizationOptions.cs`,
`LocaltestAuthorizationTelemetry.cs`, `Policies/`, `Infrastructure/`, `Controllers/`, `ServiceDiscovery/`), 2 build overrides, 3 Dockerfile and 1 `.dockerignore` deviations: all accounted for.
