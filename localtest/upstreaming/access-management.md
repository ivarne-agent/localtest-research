# Access Management: upstreaming

- Upstream: [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth), `src/apps/Altinn.AccessManagement/` (with
  `src/libs/` Api.Contracts, Host and Integration, `src/pkgs/` ABAC and PEP)
- Pin: `3d51810` (main, 2026-10-19),
  [`../components/access-management/vendor/UPSTREAM.md`](../components/access-management/vendor/UPSTREAM.md); none of
  the 33 upstream commits since touches the directive sites (checked 2026-10-27)
- Group: `altinn-auth`, one pin shared with [Authentication](authentication.md) and [Authorization](authorization.md);
  the three move together.
- Reminders: none.
- Host: an `AltinnServiceDefaults` host (`AltinnHost.CreateWebApplicationBuilder("access-management", ...)`,
  `Altinn.Authorization.ServiceDefaults` 5.5.0), so service discovery and OTLP export come from the shared library; its
  changes are in [`authorization-utils.md`](authorization-utils.md).
- Localtest modifications: 7 directive sites under `src/apps/Altinn.AccessManagement/src/` (`Program.cs`, two in
  `AccessManagementHost.cs`, two in `Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs`, and task
  68's `Altinn.AccessManagement.Core/Services/ConsentService.cs` and
  `Altinn.AccessManagement.Api.ServiceOwner/Controllers/RequestController.cs`; task 63's two in the lib's
  `TokenGenerator.cs` went in task 66), the
  composition `fakes/LocaltestAccessManagementExtensions.cs` with `fakes/LocaltestAccessManagementOptions.cs`, the
  policy store `fakes/Policies/`, the token, key and metadata stand-ins
  `fakes/Infrastructure/`, the seed refresh `fakes/Sync/` and `fakes/Controllers/`, the shared copy
  `fakes/ServiceDiscovery/`, two build overrides, the Postgres init script
  `postgres-init/45-access-management.sh`, and the `localtest:` deviations in `Dockerfile` and `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/access-management/` (sites relative to `vendor/src/apps/Altinn.AccessManagement/src/`).

Upstream plans its own local setup: [altinn-auth#4063](https://github.com/Altinn/altinn-auth/issues/4063) (Access
Management in an Aspire app host: init and sync, Valkey, Azurite) under the epic
[altinn-auth#4048](https://github.com/Altinn/altinn-auth/issues/4048), with the configuration contract in
[altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050).

## Phase 1: easy fixes

- [ ] **The full URL of the test tool's platform access token endpoint, as a setting.** In
      `src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/`, `TokenGenerator.TestTool`
      (the token source without Key Vault) always calls `{Endpoint}/api/GetPlatformAccessToken`, an absolute path that
      drops any path in `Endpoint`. Add an optional `Platform:Token:TestTool:PlatformAccessTokenUrl`, the full URL, used as
      is when set (the `env`, `ttl` and `app` parameters appended; validation: `Endpoint` or it). Task:
      [`../../TODO.md` task 66](../../TODO.md#66-a-test-tool-token-generator-mock-in-the-front-door) (the owner's
      decision, 2026-10-29: "I would like to upstream an environment variable that have the full path"; it supersedes
      task 64's local signing source).
  - Why: the front door's mock of the test tool answers under `/mockporten/`
    (`http://localtest:8080/mockporten/api/GetPlatformAccessToken`), which the lib cannot reach today, so the front door
    also answers at the root `/api/GetPlatformAccessToken` for it; the manifest already sets the variable
    (`Platform__Token__TestTool__PlatformAccessTokenUrl`), which the lib at the pin ignores.
  - Size: small (one option in two option classes, one branch in `CreatePlatformAccessToken`, the validation, 11 unit
    tests, a README paragraph).
  - Removes: no site (task 66 removed the two `TokenGenerator.cs` blocks; production's `TestTool` already calls the mock
    at the front door's root); the front door's temporary root route `/api/GetPlatformAccessToken` and the root use of
    `Platform__Token__TestTool__Endpoint`, so the mock has one route.
  - Upstream: prepared, not opened: the branch `feat/test-tool-platform-access-token-url` (local clone
    `/home/agent/code/altinn-auth`) and its description in
    `/home/agent/code/.handover/altinn-auth-upstream/test-tool-platform-access-token-url.md`, pushed to the fork
    (`ivarne-agent/altinn-auth`, branch `feat/test-tool-platform-access-token-url`, commit `826df92`); to be opened:
    https://github.com/Altinn/altinn-auth/compare/main...ivarne-agent:altinn-auth:feat/test-tool-platform-access-token-url?expand=1.
    Related
    [altinn-auth#4052](https://github.com/Altinn/altinn-auth/issues/4052) (Mockporten serves the same API). The lib is
    compiled into Access Management only in this repository (`Altinn.AccessManagement` and `Altinn.AccessMgmt.Core`
    reference it). Authorization's `TokenGeneratorSettings:Url` is already a full URL.

- [ ] **The Access Management GUI's and the platform's base URLs as settings.** `ConsentService.GetConsentViewUri`
      writes `https://am.ui.{GeneralSettings:Hostname}/accessmanagement/ui/consent/request?id=…` and
      `RequestController.BuildLinks` (the service-owner delegation requests) `https://am.ui.{Hostname}/…/requests/resource`
      and `https://platform.{Hostname}/accessmanagement/api/v1/…/status`. Two settings with the whole base URL
      (`GeneralSettings:AccessManagementUiBaseUrl`, `GeneralSettings:PlatformBaseUrl`, defaulting to today's values)
      keep production as it is.
  - Why: the stack serves the GUI on `http://am.ui.local.altinn.cloud:8000` and the platform on the main host, plain http
    with a port (TODO.md task 68).
  - Size: small.
  - Removes: the two sites (`Altinn.AccessManagement.Core/Services/ConsentService.cs`,
    `Altinn.AccessManagement.Api.ServiceOwner/Controllers/RequestController.cs`), and the fakes'
    `GeneralSettings:Hostname` with the port goes back to the host (`fakes/LocaltestAccessManagementExtensions.cs`).
  - Upstream: none found.

- [ ] **A factory client for the Resource Registry.** `ResourceRegistryClient` creates its own `new HttpClient()` in a
      field initializer, so it bypasses `IHttpClientFactory` and everything ServiceDefaults adds to factory clients
      (service discovery, the standard resilience handler). Register it as a typed client
      (`AddHttpClient<IResourceRegistryClient, ResourceRegistryClient>()` in
      `Altinn.AccessManagement.Integration/Extensions/IntegrationDependencyInjectionExtensions.cs:37`, today a singleton)
      or take `IHttpClientFactory` in the constructor.
  - Why: the client cannot reach `http://resource-registry/` by service name, and a hand-made `HttpClient` per
    singleton never sees DNS changes.
  - Size: small.
  - Removes: sites `ResourceRegistryClient.cs:25-30` and `ResourceRegistryClient.cs:42-51`.
  - Upstream: none found; still `new()` on `main`.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), section
    on clients outside the factory, row 1.

- [ ] **The JWT cookie handler honours `RequireHttpsMetadata` for `OidcProviders` metadata.** In
      `src/pkgs/JWTCookieAuthentication/src/Altinn.Common.Authentication/JwtCookie/JwtCookieHandler.cs`,
      `GetOidcConfiguration` builds `new HttpDocumentRetriever()` (`RequireHttps` true) for a token whose issuer
      matches a configured provider, while the scheme's own metadata uses
      `new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata }`. Use the option there too, release the
      package (today 4.0.4) and bump it here.
  - Why: the localtest Authentication publishes its metadata over plain http; in Development the scheme's own metadata
    works, the per-issuer path does not.
  - Size: small (one line, a package release, a bump in each host).
  - Removes: `fakes/Infrastructure/LocaltestOidcMetadata.cs` and its call (the folder goes with the Azure item below);
    the same fake in [`resource-registry.md`](resource-registry.md).
  - Upstream: none found; unchanged on `main`.
  - Detail: [`../components/access-management/README.md`](../components/access-management/README.md), "Status".

## Phase 2: structural changes

- [ ] **Service discovery without the pass-through for service names.** `AddAltinnServiceDefaults` already registers
      `AddServiceDiscovery()` and the factory clients' handler; what remains is a supported way to turn off the
      pass-through provider for single-label names, in the shared library (see the README's
      ["Across services"](README.md#across-services)).
  - Why: with the pass-through a service whose group is off is a DNS lookup and a connection error instead of an
    immediate "no endpoint".
  - Size: small in code; needs agreement on the option, since production relies on the pass-through for DNS names.
  - Removes: `fakes/ServiceDiscovery/` and the `AddLocaltestServiceEndpointProviders` call in the hook.
  - Upstream: none found; see [`authorization-utils.md`](authorization-utils.md).
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **A supported "init, then serve" mode.** Production runs `Init()` (migrations, static data, the Register import)
      in a separate `RunInitOnly` job; the only mode that inits and then serves is `RunIntegrationTests`, and it migrates
      on the app connection (`AccessManagementHost.cs:128` picks `SourceType.Migration` only for `RunInitOnly`). Name and
      document a mode for local runs that inits and then serves, migrating on the migration connection. Localtest then
      runs its seed refresh as a hosted service (the roles and database already come from its `postgres-init` script).
  - Why: one container has to migrate and serve; the app connection needs superuser rights today, which hides a grant
    an upstream migration forgets.
  - Size: medium; the mode needs agreement, the rest is localtest work.
  - Removes: site `Program.cs:19-35`, and the superuser grant to `platform_authorization` in
    `postgres-init/45-access-management.sh`.
  - Upstream: related [altinn-auth#4063](https://github.com/Altinn/altinn-auth/issues/4063) (an init step in the app
    host).
  - Detail: [`../reviews/access-management.md`](../reviews/access-management.md), findings 10 and 14 and "Differences
    from production".

- [ ] **A supported local configuration without Azure Blob, Key Vault and the test-tool token generator.** Production
      needs three Azure Blob containers for policies (with versions and leases), Key Vault certificates for incoming
      platform access tokens, and either Key Vault or the internet test-tool generator for outgoing ones. Agree on a
      local mode: a file policy store (shared with the PDP and the Resource Registry), the certificate from a file for
      every issuer, and the production `IAccessTokenGenerator` signing with a local certificate. The README's
      ["Across services"](README.md#across-services) has the cross-service proposal.
  - Why: none of these exists locally, and localtest does not run Azurite or call the test tool on the internet.
  - Size: large; the policy store is shared by three hosts.
  - Removes: `fakes/Policies/`, `fakes/Infrastructure/` (`LocaltestPublicSigningKeyProvider`, with the metadata item
    above `LocaltestOidcMetadata`; `LocaltestStartupLogger`, diagnostics only, moves into the hook; the outgoing tokens
    are the first item's), the forced `kvSetting:SecretUri` and `Lease:StorageAccount:BlobEndpoint` values, and the `/policy-store` half of the `Dockerfile` setup deviation.
  - Upstream: related [altinn-auth#4063](https://github.com/Altinn/altinn-auth/issues/4063) (Azurite),
    [altinn-auth#4052](https://github.com/Altinn/altinn-auth/issues/4052) (Mockporten as the token generator); the
    Azurite choice needs raising there.
  - Related: [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280) (open, by another contributor,
    2026-10-28) reads the issuers' public certificate from a file (`AccessTokenSettings:PublicSigningCertificatePath`)
    when no Key Vault is configured; released and bumped, it replaces `LocaltestPublicSigningKeyProvider`. It removes
    nothing yet. This host binds only `kvSetting`, not `AccessTokenSettings`, so binding that section is the
    prerequisite (one of five hosts, see the README's ["Across services"](README.md#across-services)).
  - Detail: [`../reviews/access-management.md`](../reviews/access-management.md), "Fakes and what they replace".

- [x] **Publish the image where localtest can pull it.** Already the case at the pin: `docker-bake.hcl` builds
      `access-management-api` and `-ffb`, and CI pushes `ghcr.io/altinn/altinn-auth/access-management-api:<sha>`, which
      pulls anonymously (checked 2026-10-27).
  - Why: once the items above are merged, upstream's image runs locally unchanged.
  - Size: none left upstream.
  - Removes: once the other items land, `Dockerfile`, `.dockerignore`, `localtest.build.props`,
    `localtest.build.targets` and the vendored copy.
  - Upstream: nothing needed.

## Stays local

- Site `AccessManagementHost.cs:70-73`, `AddLocaltestConfiguration`, and the configuration defaults in
  `fakes/LocaltestAccessManagementExtensions.cs` with `fakes/LocaltestAccessManagementOptions.cs`: connection strings,
  service names, the issuer, feature flags with the periodic syncs and the outbox off: deployment settings; once they
  come from the manifest the early hook goes.
- Site `AccessManagementHost.cs:168-170`, `AddLocaltestAccessManagement`: the hook for the test-data refresh below; it
  stays while that does.
- `fakes/Sync/` and `fakes/Controllers/`: the seed refresh (the Register import rerun, then the seed's own roles,
  Altinn 2 codes and access packages from the Register's test users) and `POST accessmanagement/api/localtest/refresh`,
  the app runner's hook: test data with no production analogue (upstream's corpus,
  [altinn-auth#4054](https://github.com/Altinn/altinn-auth/issues/4054), may replace part of it later).
- `Dockerfile`: `COPY vendor/...` and the `/src` layout in the builder stage, and the publish path under `vendor/` in
  the `Dockerfile.api` stage: the vendoring layout.
- `Dockerfile`: `chmod 1777 /rootfs/tmp` in the setup stage: buildah applies the staged directory's mode, so the app
  user could not write `/tmp`; I could not confirm whether BuildKit, which upstream releases with, needs it. Gone with
  a pulled image.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5080` and
  `Localtest__AccessManagement__PolicyStorePath`: deployment settings.
- `.dockerignore`: build and test output kept out of the context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the documentation `NoWarn` and the
  vendored-project test: the mechanism that carries the sites.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.
- `postgres-init/45-access-management.sh`: the roles and `authorizationdb` on the shared Postgres, with the passwords
  from the manifest: users and databases are set up by localtest (TODO.md task 60), as production's infrastructure does
  for its own.

Coverage: 7 sites, 7 fakes (`LocaltestAccessManagementExtensions.cs`, `LocaltestAccessManagementOptions.cs`,
`Policies/`, `Infrastructure/`, `Sync/`, `Controllers/`, `ServiceDiscovery/`), 2 build overrides,
4 Dockerfile and 1 `.dockerignore` deviations, 1 Postgres init script: all accounted for. `Otlp/` and its
`OTEL_SERVICE_NAME` item went in task 61 ([`../../TODO.md`](../../TODO.md)): the host's own name is the manifest name,
so nothing was left to remove.
