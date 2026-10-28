# Resource Registry: upstreaming

- Upstream: [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth), `src/apps/Altinn.ResourceRegistry/`. Vendored
  from [Altinn/altinn-resource-registry](https://github.com/Altinn/altinn-resource-registry) at `a342241` until that
  repository was archived on 2026-09-25 and its history imported into altinn-auth (altinn-auth#4196); retargeted to
  altinn-auth in task 58's follow-up (2026-10-27). altinn-auth `main` has three Resource Registry commits after the pin
  (checked 2026-10-27). **Every item targets altinn-auth.**
- Pin: `3d51810` (main, 2026-10-19), [`../components/resource-registry/vendor/UPSTREAM.md`](../components/resource-registry/vendor/UPSTREAM.md)
- Group: `altinn-auth` (one pin with Authentication, Authorization and Access Management)
- Reminders: none in the metadata. At the next `altinn-auth` group sync: altinn-auth `main` after the pin (`bfa6fdf8`,
  the Maskinporten scope-prefix validation) adds feature management and a Samarbeidsportalen HTTP client to
  `ResourceRegistryHost.cs`; check whether the client needs a localtest setting or fake. The vertical's
  `docker-bake.hcl` and altinn-auth's `containers/` are watched.
- Localtest modifications: 2 directive sites in `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs`,
  the hook `fakes/LocaltestResourceRegistryExtensions.cs` with `fakes/LocaltestResourceRegistryOptions.cs`, the fakes
  `fakes/Applications/`, `fakes/Infrastructure/` (two files), `fakes/Policies/`, `fakes/Seed/` and the shared copy
  `fakes/ServiceDiscovery/`, `postgres-init/`, two build overrides, and the `localtest:` deviations in
  `Dockerfile` plus its own `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/resource-registry/` (sites relative to its `vendor/`). The Resource Registry is an
`AltinnServiceDefaults` host (`Altinn.Authorization.ServiceDefaults` 6.0.0).

## Phase 1: easy fixes

- [ ] **Drop the unused Application Insights references.** Remove `Microsoft.ApplicationInsights.AspNetCore`,
      `Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel` and `Microsoft.Extensions.Logging.ApplicationInsights`
      from `Altinn.ResourceRegistry.csproj` and `src/apps/Altinn.ResourceRegistry/Directory.Packages.props` (still there on altinn-auth's `main`), the
      unused `using static Microsoft.ApplicationInsights.MetricDimensionNames.TelemetryContext;` in
      `Utils/AuthorizationUtil.cs`, and the `ApplicationInsights` log-level sections in `appsettings*.json`.
  - Why: no code uses them; telemetry goes through ServiceDefaults.
  - Size: trivial.
  - Removes: nothing in localtest (cleanup the telemetry review found).
  - Upstream: none found.
  - Detail: [`../reviews/otel/resource-registry.md`](../reviews/otel/resource-registry.md), "Upstream change".

- [ ] **Honour `RequireHttpsMetadata` for the `OidcProviders` metadata.** In `JWTCookieAuthentication`
      (altinn-auth `src/pkgs/JWTCookieAuthentication/`), `JwtCookieHandler.GetOidcConfiguration` builds
      `new HttpDocumentRetriever()` for a token whose issuer matches an `OidcProviders` entry, which requires https
      whatever the options say. Pass `new HttpDocumentRetriever { RequireHttps = Options.RequireHttpsMetadata }`, as
      `JwtCookiePostConfigureOptions` already does for the scheme's own metadata.
  - Why: the host sets `RequireHttpsMetadata = false` in Development, but user tokens from the local Authentication
    component (metadata over http) still fail on the provider path.
  - Size: trivial.
  - Removes: `fakes/Infrastructure/LocaltestOidcMetadata.cs` and its call in the hook (and the copy in
    [`access-management.md`](access-management.md)'s component).
  - Upstream: none found (unchanged on altinn-auth's `main`).
  - Detail: [`../components/resource-registry/README.md`](../components/resource-registry/README.md), "Fakes".

- [ ] **Make the application-list cache duration a setting.** `Altinn.ResourceRegistry.Integration/Clients/ApplicationsClient.cs`
      caches Storage's application list for a fixed 10 minutes (`_cacheEntryOptions`). Read the duration from a setting
      (for example `PlatformSettings:ApplicationListCacheDuration`, default 10 minutes, zero meaning no cache).
  - Why: localtest pushes an app and expects it in `resourcelist` and `resource/app_{org}_{app}` at once; with the cache
    it appears up to ten minutes later.
  - Size: small.
  - Removes: `fakes/Applications/` and its registration in the hook.
  - Upstream: none found.
  - Detail: [`../components/resource-registry/README.md`](../components/resource-registry/README.md), "Differences from
    production".

## Phase 2: structural changes

- [ ] **A policy store without Azure Blob.** Agree on a supported local storage for the two policy containers
      (`resourceregistry` and `metadata`): altinn-auth#4061 plans Azurite with containers created automatically; localtest
      today keeps them as folders on a volume shared with the PDP, which writes app policies to
      `metadata/{org}/{app}/policy.xml`. Whichever is chosen, the PDP and Access Management have to use the same store,
      part of "a supported local configuration without Key Vault, Azure Blob and queues" (see the README's
      ["Across services"](README.md#across-services)).
  - Why: `PolicyRepository` needs Azure Blob with versions and leases; without it no policy can be read or stored.
  - Size: large; needs agreement across the PDP, Access Management and the Resource Registry.
  - Removes: `fakes/Policies/`, its registration in the hook and site `ResourceRegistryHost.cs:192-194` once the other
    hook items are done too.
  - Upstream: [altinn-auth#4061](https://github.com/Altinn/altinn-auth/issues/4061) (Resource Registry in the AppHost:
    Azurite and seed), open.
  - Detail: [`../components/resource-registry/README.md`](../components/resource-registry/README.md), "Fakes".

- [ ] **A supported seed of non-app resources.** altinn-auth#4054 and #4061 plan a canonical test-data corpus whose
      resources and policies are seeded by an init step. Localtest needs that seed to accept its own resources (a
      mounted folder of `resource.json` and `policy.xml`, as `fakes/Seed/` holds) applied through the service layer, so
      the `updated` feed and subject pairs are derived as for `POST resource`.
  - Why: the stack needs Correspondence, Profile and key-role resources that exist in tt02 but not in an empty
    database.
  - Size: medium; needs agreement on the seed format.
  - Removes: `fakes/Seed/` (the seed service and the five resources) and its registration in the hook; the
    `EmbeddedResource` item in `localtest.build.targets`.
  - Upstream: [altinn-auth#4054](https://github.com/Altinn/altinn-auth/issues/4054) and
    [altinn-auth#4061](https://github.com/Altinn/altinn-auth/issues/4061), open.
  - Detail: [`../components/resource-registry/README.md`](../components/resource-registry/README.md), "The seed".

- [ ] **Platform access token keys without Key Vault.** In `Altinn.Common.AccessToken` (`Altinn/altinn-accesstoken`),
      let `PublicSigningKeyProvider` read the `{issuer}-access-token-public-cert` certificates from a configured file or
      folder when no `kvSetting:SecretUri` is set; and have the host bind `Altinn.Common.AccessTokenClient`'s
      `AccessTokenSettings` from configuration, so the outgoing token to Register can be signed with a configured
      certificate. Part of "a supported local configuration without Key Vault" (see the README's
      ["Across services"](README.md#across-services)).
  - Why: without Key Vault no platform access token can be validated, and the generator for the Register access-list
    calls has no signing certificate.
  - Size: small in code; needs agreement with the package owners on the local mode.
  - Removes: `fakes/Infrastructure/LocaltestPublicSigningKeyProvider.cs`, the `Configure<AccessTokenSettings>` call and
    the forced `kvSetting:SecretUri` in the hook.
  - Upstream: [altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136) (open);
    altinn-auth#4052 proposes a local JWKS for platform tokens instead.
  - Detail: [`../components/resource-registry/README.md`](../components/resource-registry/README.md), "Fakes".

- [ ] **Service discovery without the pass-through provider.** The host already gets `AddServiceDiscovery()` from
      `AddAltinnServiceDefaults`; what remains is a supported way to drop the pass-through provider (a ServiceDefaults
      setting), agreed for all four `AltinnServiceDefaults` hosts (see the README's
      ["Across services"](README.md#across-services)).
  - Why: with pass-through a service name without a `services__<name>__http__0` entry goes to DNS and fails late;
    localtest wants it to fail at once.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: `fakes/ServiceDiscovery/` and its call in the hook.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **Publish the image where localtest can pull it.** altinn-auth's CI pushes each app vertical's bake targets to
      `ghcr.io/altinn/altinn-auth/<vertical>-<app>:<sha>`, but `src/apps/Altinn.ResourceRegistry/docker-bake.hcl` is a
      stub (empty default group, an inline `FROM aspnet-runtime`). Give it a real target as Access Management has
      (`containers/Dockerfile.api`), and make the package public as `access-management-api` is (an anonymous pull of
      `resource-registry-api` is refused today).
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets`, the vendored copy, and
    site `ResourceRegistryHost.cs:46-49` (its configuration defaults move to the manifest).
  - Upstream: none found.

## Stays local

- `AddLocaltestConfiguration` and `LocaltestResourceRegistryOptions`: `PostgreSQLSettings` on the shared container,
  `PlatformSettings` by service name and `OidcProviders:altinn` on the public origin: deployment settings, which
  upstream's chart supplies; with a published image they move to the manifest (the site goes with the image item).
- `postgres-init/40-resource-registry.sh`: the database and the roles the migrations grant to, which upstream's
  infrastructure creates. ServiceDefaults' `Altinn:Npgsql:<name>:Create` (Development only, see altinn-auth#4050) might
  replace it; I could not confirm that the Resource Registry host honours it.
- `Dockerfile`: the `/src/vendor/src` layout with the overrides and `fakes/`, and `DirectoryBuildPropsPath` /
  `DirectoryBuildTargetsPath`: the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://+:5100`: deployment settings; with a
  published image they move to the manifest.
- `.dockerignore`: localtest's own (`bin`, `obj`) for the component-folder context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the `NoWarn` for the undocumented fakes and the
  vendored-project test: the mechanism that carries the sites; gone with them.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.

Not a Resource Registry item: the eight monorepo test apps whose `delete` rule puts resource and action matches in one
`AnyOf`, which `PolicyHelper.FlattenXacmlRule` rejects with a 500 on `policy/rules` and `policy/rights`. The review
assigns the fix to the test apps' `policy.xml` in altinn-studio and does not call the validator wrong
([`../reviews/test-apps.md`](../reviews/test-apps.md)).

Coverage: 2 sites, 6 fakes (`Applications/`, `Infrastructure/LocaltestOidcMetadata.cs`,
`Infrastructure/LocaltestPublicSigningKeyProvider.cs`, `Policies/`, `Seed/`, `ServiceDiscovery/`) plus the
hook and `postgres-init/`, 2 build overrides, 2 Dockerfile and 1 `.dockerignore` deviations: all accounted for. `Otlp/`
and its `OTEL_SERVICE_NAME` item went in task 61 ([`../../TODO.md`](../../TODO.md)): the host's own name is the
manifest name, so nothing was left to remove.
