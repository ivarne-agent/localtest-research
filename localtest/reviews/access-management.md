# Access Management review (2026-10-20)

> **Superseded (2026-10-21):** compose was replaced by studioctl's `localtest-v2` runtime on 2026-10-20; finding 14's
> start order is now studioctl's: Postgres, the Register, the Resource Registry and, since task 14, the profile
> component, whose production Profile answers Access Management's `internal/user` calls. The one-time
> `drop database authorizationdb` in the README concerns only Postgres volumes from before task 5b.

> **Status (2026-10-20):** all findings handled. Fixed: 1 (the Dockerfile mirrors the release image's bake files; `/tmp` reset to 1777 because buildah applies the staged mode), 2 (no HEALTHCHECK), 3 (sync instructions; re-pinned to `3d518103`), 4 (the dead Application Insights override dropped; the comment says what the remaining two do), 5, 6, 7 and 8 (stale comments and docs), 9 (the Resource Registry stand-in throws `HttpRequestException` for an unknown resource, as production's client does), 11 (refreshes run one at a time), 12 (the service-owner numbers' convention stated), 14 (compose starts it after Storage). Documented in the README's known gaps: 10 (superuser roles) and 13 (the Altinn 2 bridge answers 404). Not changed, as decided: the database is not reset. Verified through the PDP and apps: authorization smoke 30/30, root smoke 23/23, Cypress signing-test 4/4.

> **Follow-up (task 5b, 2026-10-20):** the three `ResourceRegistry/` stand-ins (findings 9 and 12 and the largest row of the fakes table) are deleted. The production `ResourceRegistryClient` and `AltinnResourceRegistryClient` call the Resource Registry component (`components/resource-registry/`); the refresh runs the production `ResourceSyncService` against its `updated` feed. Consent templates are fetched by the production client from GitHub, no longer stubbed to null.

## Summary

`localtest/components/access-management` runs production Access Management from `Altinn/altinn-auth` at pin `392f9578`. Apart from the two `LOCALTEST` directive files, the vendored tree matches upstream byte for byte, and the three directive sites follow the vendoring rule. Upstream `main` is 6 commits ahead and changes 99 files under the vendored paths. One of those files is `AccessManagementHost.cs`, which holds directive sites, though the change is far from them. The commits also move the instance-delegation controller (the one apps call for signing) into a new `Api.Apps` project and move the CI base images to chiseled. The fakes are small and well targeted: file policy store, certificate-backed token handling, a Resource Registry stand-in derived from Storage, and a refresh hook. The component Dockerfile is the main gap. It is the only vendored component that does not mirror upstream yet: tags are unpinned, the container runs as root, `tzdata`/`krb5-libs` are missing, there are no `localtest:` markers, and it keeps a dead `HEALTHCHECK`. A few comments and docs are also stale.

## Vendored code

- Pin: `392f9578d925b947331a1e90fb609c031654d3d2` (`vendor/UPSTREAM.md`). Baseline commit: `05c82c1`, 3 349 files.
- Fidelity check: I ran `git archive 392f957` of every path listed in `UPSTREAM.md` (`.editorconfig`, `global.json`, `src/Directory.Build.{props,targets}`, `src/Directory.Packages.props`, `src/Altinn.ruleset`, `src/Stylecop.json`, `src/apps/Altinn.AccessManagement`, `src/libs/Altinn.Authorization.{Api.Contracts,Host,Integration}`, `src/pkgs/Altinn.Authorization.{ABAC,PEP}`, plus the `Directory.*`/`README.md` files in `src/{apps,libs,pkgs}`) into an empty folder. Then `diff -rq --exclude=bin --exclude=obj --exclude=UPSTREAM.md` against `vendor/`.
  - Result: only `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Program.cs` and `.../AccessManagementHost.cs` differ. No files are missing or extra.
  - `git diff 05c82c1 HEAD -- vendor` shows the same two files (+15 lines, no deletions). The baseline is unmodified.
  - The working tree holds untracked `bin/`/`obj/` folders under `vendor/src/libs/...`. They are ignored by `localtest/.gitignore` and `.dockerignore`, so they are harmless.
- Directive sites (all under `vendor/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/`):

| # | Where | Kind | What it does | Rule |
| --- | --- | --- | --- | --- |
| 1 | `Program.cs:19-35` | `#if !LOCALTEST` / `#else` | Production `RunInitOnly`/`RunIntegrationTests` selection kept unchanged. `#else` runs `LocaltestDatabase.EnsureAsync`, then the production `Init()` (EF migrations, static data, Register import), then `LocaltestRefreshService.RunAtStartup`, on every start. | OK (production first, unchanged) |
| 2 | `AccessManagementHost.cs:70-73` | `#if LOCALTEST` addition | `AddLocaltestConfiguration(builder)` right after `AltinnHost.CreateWebApplicationBuilder`: config defaults before the eager reads | OK |
| 3 | `AccessManagementHost.cs:168-170` | `#if LOCALTEST` addition | `AddLocaltestAccessManagement(builder)` right before `builder.Build()`: DI replacements | OK |

- The component `README.md` description of site 1 leaves out the `LocaltestRefreshService.RunAtStartup` call.
- `components/authorization/vendor` holds a second, identical copy of `src/libs/Altinn.Authorization.Api.Contracts` at the same pin (checked with `diff -rq`). The two components must be re-pinned together.

## Upstream drift

`gh api repos/Altinn/altinn-auth/compare/392f9578...main`: status ahead by 6 commits (all 2026-10-19); 135 files changed, 99 of them under the vendored paths.

| Commit | What | Relevance to localtest |
| --- | --- | --- |
| `8509a47b` (#4414) | Moves `AppsInstanceDelegationController` into a new `src/Altinn.AccessManagement.Api.Apps` project (with its own `Program.cs`/host). Host csproj gains a `ProjectReference`. Many DTOs move into `Api.Contracts` (`AccessManagement/Apps/...`). Several Core/host models are removed. | High for the sync. This is the endpoint apps call for signing delegations. The new project sits inside the copied path, so a re-sync picks it up. The authorization component's copy of `Api.Contracts` changes too. |
| `452a8fee` (#4418) | Bankruptcy-estate (konkursbo) main admin on ER import: new controller/service, `RoleSyncService`, `AssignmentService`, `RoleConstants`, `PackageConstants`, `StaticDataIngest`, `RemoveObsoletePackages`. Also edits `AccessManagementHost.cs`, adding 4 authorization policies around line 401. | Touches a directive file, but far from the directive blocks, so a three-way merge should apply cleanly (not verified). Static data and role sync change what the refresh sees. |
| `3d518103` (#4657) | The PDP request reads the enduser party parameter case-insensitively (`DecisionHelper`) | Small behaviour fix |
| `5968cd8a` (#4585) | Root `containers/Dockerfile.aspnet-{builder,runtime}` move to `sdk:10.0-noble` / `aspnet:10.0-noble-chiseled-extra`, copying the Kerberos libraries in by hand | Not in the vendored paths. Changes what the CI image (bake) looks like. |
| `afb4624d`, `15203f7c` | Renovate config, agent skills | None |

## Dockerfile

- Upstream has two build definitions:
  - `vendor/src/apps/Altinn.AccessManagement/Dockerfile`, the app-folder Dockerfile. It is used only by the manually dispatched `publish-flux-artifacts.yml` (image `ghcr.io/altinn/altinn-auth/accessmanagement`, which the `manifests/pre-deploy` db-migration job references).
  - `docker-bake.hcl` (targets `access-management-api`/`-ffb`), which uses `containers/Dockerfile.api` with the root `containers/Dockerfile.aspnet-builder`/`-runtime` as named contexts. CI (`ci.yml`, `repoctl vertical containers build`) and the release image tagging (`deploy-release-app.yml`) go through bake.
  - The root `containers/` files are not vendored. I did not verify which image name the deployment manifest (`access-management:{{GIT_SHA}}`) resolves to.
  - At the pin, both definitions have the same content: pinned digests `sdk:10.0-alpine@sha256:3cc3bb…` / `aspnet:10.0-alpine@sha256:f62a27…`, `apk add tzdata krb5-libs`, user `dotnet` 1000:3000, `USER dotnet`, `/tmp/logtelemetry`. Bake's runtime sets `ASPNETCORE_URLS=http://+:8080`; the app Dockerfile has `EXPOSE 5110` and nothing else.
- The component `Dockerfile` (21 lines) does **not** mirror upstream:
  - `FROM …:10.0-alpine` with no digest (lines 3, 13).
  - No `apk add tzdata krb5-libs`. The running container logs `Error loading shared library libgssapi_krb5.so.2` at startup.
  - No user creation or `USER`, so it runs as root: `podman inspect` shows an empty `User`, and DataProtection writes to `/root/.aspnet`.
  - No `/tmp/logtelemetry`.
  - No header that says it mirrors upstream, and no `localtest:` markers.
  - `HEALTHCHECK` on line 20 is dead (`podman inspect` shows a nil `Healthcheck` on the image and the container), and compose defines no `healthcheck:` for this service.
  - `COPY . .` copies the whole component folder (fine, matches register). The env block (`ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS=:5080`, `PolicyStorePath`) is the legitimate localtest deviation.

## Fakes and what they replace

| File (`fakes/`) | Replaces | Could it be production code instead? |
| --- | --- | --- |
| `LocaltestAccessManagementExtensions.cs` | Deployment configuration (connection strings, platform endpoints, OIDC issuer, feature flags) and the DI replacement hook | Configuration only. It could live in compose env vars instead, but code defaults are fine. |
| `LocaltestAccessManagementOptions.cs` | – (localtest settings) | – |
| `Database/LocaltestDatabase.cs` | Operations-created Postgres roles and database (`platform_authorization_admin`/`platform_authorization`, `authorizationdb`, `hstore`) | It could be a compose init script like storage's and events'. Both roles are made `SUPERUSER` because, without `RunInitOnly`, EF migrations run on the app connection. |
| `Policies/LocaltestPolicyFactory.cs`, `Policies/LocaltestFilePolicyRepository.cs` | `IPolicyFactory`/`PolicyRepository` over three Azure Blob accounts (`delegationpolicies`, `metadata`, `resourceregistry`), with blob versioning and leases | Only with Azurite, which the project rules out. Path resolution copies production `PolicyFactory`. Missing blobs return an empty stream as production does. The lease is non-exclusive; a process-wide write lock serialises writes. |
| `Infrastructure/LocaltestTokenGenerator.cs` | `ITokenGenerator` (test tool or Key Vault) for outgoing platform tokens | Already wraps production `IAccessTokenGenerator`. Only the certificate source differs. |
| `Infrastructure/LocaltestPublicSigningKeyProvider.cs` | `IPublicSigningKeyProvider` (Key Vault) | Same pattern as the other components. Production `AccessTokenHandler` validates for real. |
| `Infrastructure/LocaltestOidcMetadata.cs` | – (pre-registers an http-allowing metadata manager in production `ConfigurationMangerUtil` for http `OidcProviders`) | Needed because the localtest issuer is plain http. A transport-only change. |
| `Infrastructure/LocaltestStartupLogger.cs` | – (logs effective feature flags and their providers) | Diagnostics only |
| `Sync/LocaltestRefreshService.cs` | Periodic Register sync, plus data production gets from Altinn 2/ER (own-party roles, Altinn 2 codes, access packages from `testData.json`) | Partly production: reruns `RegisterHostedService` and `ResourceSyncService` unchanged, then writes through `IAssignmentService`. The seed-specific part has no production analogue. |
| `Controllers/LocaltestRefreshController.cs` | – (anonymous `POST accessmanagement/api/localtest/refresh`, the app runner's hook) | – |

Not faked, handled by configuration: lease (no blob endpoint → production `NoopLeaseService`), outbox (handler and reaper flags off), periodic Register and Resource Registry sync (off), Application Insights (off in Development and through `DisableApplicationInsights`). There is no MassTransit or Service Bus in this host. SBL bridge clients (`AltinnRolesClient`, `PartiesClient`, `DelegationRequestProxy`) are neither faked nor routed: `http://local.altinn.cloud:8000/sblbridge/…` returns 404.

## Differences from production

| Area | Production | Localtest | What an app developer sees |
| --- | --- | --- | --- |
| Init/migrations | Separate `RunInitOnly` job with the admin connection; the app does not re-init | Every start: ensure db/roles, EF migrate, static ingest, Register import, refresh | Slower start. Data is kept across restarts on the `postgres-data` volume and is not reset. |
| DB privileges | Split admin/app roles | Both roles are superusers | Privilege or grant bugs are hidden |
| Parties and roles | Continuous Register sync plus Altinn 2/ER | Register import at start and on refresh; roles, Altinn 2 codes and packages from `testData.json` | Changes appear only after `refresh`. Unknown codes (for example `ECKEYROLE`) are skipped with a log line. |
| Resources | Resource Registry (all resource types, subject index, rights, consent templates) | The Resource Registry component through the production clients: app resources with a pushed policy plus its seeded non-app resources, read from the `updated` feed from the epoch on every refresh (the localtest lease keeps no `since`) | Resources and role/package links appear after the refresh that follows an app push. Apps without a policy have no subject pairs and so no `dbo.resource` row. Provider rows are the real service-owner list; `ttd` has no organisation number there. |
| Delegation policies | Azure Blob with versions and leases | Files in the `policy-store` volume, shared with the PDP | Same version semantics. No exclusive lease. |
| Platform tokens | Key Vault certificates | Shared test certificate | None |
| User tokens | HTTPS OIDC metadata | Plain-http metadata from the localtest authentication | None |
| Altinn 2/SBL bridge | Real | 404 | Any feature that consults Altinn 2 roles or key roles through SBL bridge fails or returns empty (not verified which endpoints) |
| Outbox notifications | On | Off | No notifications on delegation changes |
| Environment | `ASPNETCORE_ENVIRONMENT=Production`, `ALTINN_ENVIRONMENT` set | `Development`; ServiceDefaults environment `UNKNOWN` | Swagger UI, developer exception page, `ShowPII`, verbose EF SQL logging. `appsettings.Development.json` feature flags (`UseNewQueryRepo`, `UsingConnectionQueryOnly`, `RightsDelegationApi`), which may differ from the App Configuration flags production uses (not verified). |
| Image | Pinned alpine (chiseled on `main`), non-root, with tzdata and krb5 | Unpinned alpine, root, no tzdata or krb5 | Not visible to apps |

## Findings

1. **The Dockerfile does not mirror upstream.**
   - Where: `localtest/components/access-management/Dockerfile:3,13,15-21`.
   - What: unpinned base images, no `tzdata krb5-libs`, no `dotnet` user or `USER`, no `/tmp/logtelemetry`, no `localtest:` markers. The `libgssapi_krb5.so.2` error in the startup log comes from the missing `krb5-libs`.
   - Why it matters: it breaks the project rule that storage, register, authentication and workflow-engine already follow. It also runs as root.
   - Fix: rewrite it from `vendor/src/apps/Altinn.AccessManagement/Dockerfile` line for line (or from `containers/Dockerfile.aspnet-*`), and say in a header which upstream file is mirrored and that bake is CI's path.
   - Severity: medium.
2. **Dead `HEALTHCHECK`.**
   - Where: `Dockerfile:20`.
   - What: podman's OCI images drop the instruction, and `podman inspect` confirms a nil healthcheck.
   - Fix: remove it, and add a compose `healthcheck:` if one is wanted (as for postgres). Use the same wording as storage/register.
   - Severity: low.
3. **`UPSTREAM.md` has no sync instructions.**
   - Where: `vendor/UPSTREAM.md`.
   - What: unlike `storage/vendor/UPSTREAM.md` there is no "Syncing to a newer upstream commit" section. That section would cover: the path list for `git diff`, the three-way merge of `Program.cs`/`AccessManagementHost.cs`, the `diff -rq` check, the Dockerfile carry-over, and a lockstep re-pin with `components/authorization` (shared `Api.Contracts`).
   - Why it matters: upstream already moved the signing endpoint (#4414) and touched a directive file (#4418).
   - Fix: add the section.
   - Severity: medium.
4. **Partly dead "forced" overrides.**
   - Where: `fakes/LocaltestAccessManagementExtensions.cs:94-100`.
   - What: they are added after `AltinnHost.CreateWebApplicationBuilder`, so `kvSetting:SecretUri=""` does not influence the ServiceDefaults Key Vault decision, which was already taken (the log shows "Missing keyvault settings"). It does still steer `ConfigureLibsIntegrations` (`AccessManagementHost.cs:199`) to the test-tool path. `ApplicationInsights:InstrumentationKey` is read only inside `!IsDevelopment()` (`AccessManagementHost.cs:88-90`), so it is dead. `Lease:StorageAccount:BlobEndpoint` is effective. No dead `Altinn:LocalDev`/`Altinn:IsTest` defaults are set; the service reports `Environment: UNKNOWN` and uses the non-LocalDev paths.
   - Fix: keep only the keys that matter (or say exactly what each one guards against) and drop the "Cannot be anything else" claim.
   - Severity: low.
5. **Stale code comment.**
   - Where: `fakes/LocaltestAccessManagementExtensions.cs:20`.
   - What: it calls this "The one localtest hook … right before `Build()`", but there are two hooks, and `AddLocaltestConfiguration` runs right after builder creation.
   - Severity: low.
6. **Stale compose comment.**
   - Where: `localtest/docker-compose.yml:102-103`.
   - What: "Step 1 … not yet routed through the proxy, so port 5080 is published for verification" is no longer true: `/accessmanagement/` is routed (`localtest/Proxy/PlatformRoutes.cs:39`).
   - Severity: low.
7. **Stale plan docs.**
   - Where: `planned-components/access-management/README.md:18` and `:36`.
   - What: line 18 says the database is "created by the existing init script", but `LocaltestDatabase` creates it. Line 36 says "Analysis … (in progress)" while the plan is marked complete.
   - Severity: low.
8. **Incomplete README directive description.**
   - Where: `components/access-management/README.md:16-19`.
   - What: site 1 omits `LocaltestRefreshService.RunAtStartup`. The paths are relative to `vendor/src/apps/Altinn.AccessManagement/` without saying so.
   - Severity: low.
9. **The Resource Registry stand-in's `GetResource` returns `null` for unknown ids.**
   - Where: `fakes/ResourceRegistry/LocaltestResourceRegistryClient.cs:268-269`.
   - What: production `ResourceRegistryClient.GetResource` throws `HttpRequestException` on non-200. Callers written for the throw may get a `NullReferenceException` or treat the resource as found-but-empty (not verified which callers).
   - Fix: throw as production does.
   - Severity: low to medium.
10. **Superuser roles hide privilege bugs.**
    - Where: `fakes/Database/LocaltestDatabase.cs:32-39`.
    - What: both production roles get `SUPERUSER`.
    - Why it matters: it is needed because the app connection runs EF migrations, but a missing grant in an upstream migration would pass locally.
    - Fix: run migrations with the admin role only, or accept the risk and say so in the README.
    - Severity: low.
11. **The refresh has no concurrency guard.**
    - Where: `fakes/Sync/LocaltestRefreshService.cs:47`, `fakes/Controllers/LocaltestRefreshController.cs:166`.
    - What: two concurrent `POST …/refresh` calls (two app runners) run the import, resource sync and prune in parallel. Leases are no-ops, so nothing serialises them.
    - Fix: a static `SemaphoreSlim`.
    - Severity: low.
12. **Misleading service-owner comment.**
    - Where: `fakes/ResourceRegistry/LocaltestAppResources.cs:127-131`.
    - What: the table maps `ttd` to `991825827` (Digdir's number). That matches the app backend convention (see altinn-studio `operatorcontext.go:108-115`), but the comment claims the numbers come from `altinn-orgs.json`, where `ttd` has no number. The Register seed's Testdepartementet is `405003309`.
    - Fix: state the convention.
    - Severity: low.
13. **SBL bridge calls are silent 404s.**
    - Where: `LocaltestAccessManagementExtensions.cs:68,72`.
    - What: the plan's decision was "stub to empty", but no stub exists and the README does not mention it.
    - Fix: document it, or add empty stubs for `IAltinnRolesClient`/`IPartiesClient` if any app-relevant endpoint hits them.
    - Severity: low (not verified which endpoints are affected).
14. **No dependency on Storage at start.**
    - Where: `docker-compose.yml:116-120`.
    - What: the startup refresh builds app resources from Storage, but compose depends only on postgres and register. If Storage is not up, resources are empty until the next refresh. The code handles this with a warning.
    - Severity: low.

## Verification done

- Read `AGENTS.md`, `planned-components/mvp-vendoring-model.md`, `planned-components/access-management/README.md`, the component `README.md`, `vendor/UPSTREAM.md`, `Dockerfile`, `.dockerignore`, `localtest.build.props/.targets`, every file in `fakes/`, the vendored `Program.cs`, `AccessManagementHost.cs`, `appsettings*.json`, `PolicyFactory.cs`, `PolicyRepository.cs`, `ResourceRegistryClient.GetResource`, lease registration, and `Integration.Platform` defaults.
- Fidelity: `git archive 392f957 <paths> | tar -x` into a scratch dir under the scratchpad, then `diff -rq --exclude=bin --exclude=obj --exclude=UPSTREAM.md`. Only the 2 directive files differ. `git diff 05c82c1 HEAD -- vendor` shows the same 2 files. `git ls-files vendor` has 3 349 files and none under `bin/`/`obj/`.
- Drift: `gh api repos/Altinn/altinn-auth/compare/392f9578…main` (commits, file list, patches for `AccessManagementHost.cs`, csproj, root `containers/Dockerfile.*`, `Api.Apps/Program.cs`).
- Upstream build path: read `.github/workflows/publish-flux-artifacts.yml`, `ci.yml`, `deploy-release-app.yml` and `.github/actions/deploy-apps/README.md` in the reference clone, plus `docker-bake.hcl`, `containers/Dockerfile.*` and `manifests/*` image references.
- Runtime, read only:
  - `podman ps`.
  - `podman logs localtest_access-management_1`: saw `Environment: UNKNOWN`, the missing Key Vault, the `libgssapi_krb5.so.2` error, "No migrations were applied", Yuniql up to date, the refresh summary (`unknown role codes: ECKEYROLE`), the DataProtection keys in `/root`, `Hosting environment: Development`, and the feature flags coming from `MemoryConfigurationProvider`.
  - `podman inspect` of the container and image: empty `User`, nil `Healthcheck`, the env vars.
- GETs:
  - `/accessmanagement/api/v1/meta/info/accesspackages/export` returned 200.
  - `/sblbridge/…` returned 404.
  - `/register/api/localtest/test-users` returned the seed role codes.
- Not verified:
  - Whether the #4418 three-way merge into `AccessManagementHost.cs` is conflict-free.
  - Which image name the production deployment actually runs.
  - Production feature-flag values (App Configuration).
  - Which endpoints reach the SBL bridge clients.
  - Callers' behaviour on the `null` from `GetResource`.
  - Behaviour on a fresh `postgres-data` volume (the plan says it was verified; I did not reproduce it).
  - Nothing was built or run.
