# Plan: the new localtest in studioctl

Status: superseded in part, 2026-10-20. The integration was built differently on several points (studioctl branch
`feat/studioctl-localtest-v2`, commit `43be33f73e`; the research repo's image workflow in `84514a3`). The choices are
argued in [`studioctl-integration-options.md`](studioctl-integration-options.md). The source now lives in
[`localtest/studioctl/`](../localtest/studioctl/) and the runtime is developed there until it is upstreamed. What was built:

- **A separate runtime**, `studioctl env up -r localtest-v2`, next to the classic localtest, instead of a flag that
  swaps the classic one. Images are pinned from `ghcr.io/ivarne-agent/localtest-research/localtest-<name>` (`--latest`
  follows main, `--dev <name>|all` builds from a research checkout).
- **The front door is studioctl-server**, not the localtest app (section 3.2 below is superseded). It matches the bound
  topology (component prefixes on `local.altinn.cloud`, the PDF and workflow-engine hosts, falling back to the localtest
  app), keeps `Host`, strips `Secure`, and reaches apps directly instead of through the host bridge. The localtest app
  keeps the fake ID-porten, Maskinporten, `/Home` and the app-frontend files; its own proxy is unused there.
- **Provisioning in studioctl-server** as section 3.4 proposed (metadata, languages and texts, policy, `testData.json`
  from the app over HTTP, the Studio token from `/Home/GetTestStudioToken`, the Access Management refresh, the register
  `DELETE` when an app goes away).
- **Health by HTTP polling** of each component's `/health` from studioctl, not a container health check.
- **Images built in the research repo** for now (`.github/workflows/images.yml`), including a `localtest-postgres`
  image with the init scripts; the pdf3 and workflow-engine images are the monorepo's own.
- **The certificate** is embedded in studioctl and mounted at `/certs`, not generated per installation.
- **No compose file** in the research repo any more.

Sections 1 (where the code lands), 4 (the components need nothing studioctl-specific), 5 (contracts) and 6 (rollout)
still describe open work.

## What moves

The stack in [`../localtest/`](../localtest/) replaces today's single localtest container:

| Container (studioctl name) | Source | Replaces in today's localtest |
| --- | --- | --- |
| `localtest` | The localtest app (`localtest/localtest/`): front door on `local.altinn.cloud:8000`, the proxy, the fake ID-porten and Maskinporten, the `/` and `/Home` contract | The ASP.NET localtest (MVC controllers, `ProxyMiddleware`, fake login) |
| `localtest-register` | Production Register, in-memory snapshot, also answers Profile | `register/`, `profile/` controllers, `TestDataService` |
| `localtest-authentication` | Production Authentication, sessions in memory | `authentication/` controllers, token minting |
| `localtest-authorization` | Production PDP, file policy store | In-process PDP, `PolicyRetrievalPoint` from the app |
| `localtest-access-management` | Production Access Management on Postgres | `accessmanagement/` controllers |
| `localtest-storage` | Production Storage on Postgres, file blobs | `storage/` controllers, `AltinnPlatformLocal` files |
| `localtest-events` | Production Events on Postgres, webhook capture | `events/` controllers |
| `localtest-postgres` | `postgres` (one server, a database per component) | The workflow engine's own Postgres container |
| `localtest-pdf3`, `localtest-workflow-engine` | Unchanged | — |

Notifications and Correspondence stay out of scope; their prefixes keep answering 404, as Correspondence does today.

## 1. Where the code lives in the monorepo

- `src/Runtime/localtest/` holds the new stack, laid out as in this research repo: the localtest app at the root of
  it (or in `app/`), one folder per component under `components/<name>/` with `vendor/`, `fakes/`, the two
  MSBuild override files, the Dockerfile and `UPSTREAM.md`. The certificate, compose file and scripts become
  studioctl's (sections 3 and 4); the compose file can stay as a developer convenience and a CI reference.
- Today's localtest has to keep working during the rollout (section 6), so the new stack lands next to it first:
  `src/Runtime/localtest-v2/`, renamed over `src/Runtime/localtest/` when the old one is deleted. Its git history
  (the `git subtree` from app-localtest) stays reachable.
- **Vendoring.** The monorepo brings external repositories in with non-squashed `git subtree` of whole repositories.
  The components vendor *parts* of repositories (Register: two folders and root build files; Access Management: six
  folders, `containers/` and build files), which `git subtree` cannot take without `git subtree split` per folder
  in a clone of each upstream. Keep the copy-plus-`UPSTREAM.md` model with `tools/repin-vendor.py` (three-way merges
  of the `LOCALTEST` files, reports conflicts), and add a CI check that re-runs `git archive <pin> <paths>` and
  `diff -rq` against `vendor/` so the "unmodified except directives" rule is enforced.
- Root `Makefile`: a `repin-<component>` target per component (or one per upstream repository: the three
  altinn-auth components re-pin together) next to the existing subtree targets.

## 2. Images

- One image per component and one for the localtest app, built by a GitHub workflow on changes under
  `src/Runtime/localtest/**` (per component path filter), pushed as `ghcr.io/altinn/altinn-studio/localtest-<name>`
  with the commit SHA and `latest` from `main`, as today's localtest image follows `main`.
- Build contexts are the component folders, exactly as the Dockerfiles here expect; each Dockerfile mirrors its
  upstream's (pinned digests, non-root user) with `localtest:` deviations.
- The localtest app is a Native AOT build for the build machine's architecture. The workflow needs a matrix
  (`linux/amd64`, `linux/arm64`) and a manifest list, because developers run Apple silicon. The .NET components are
  multi-arch through their base images.
- [`.github/actions/setup-localtest-images`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/.github/actions/setup-localtest-images/action.yaml):
  add every new image to the cache key's source list and to `docker image save`.

## 3. studioctl (`src/cli`) changes

### 3.1 Images and containers

- [`internal/config/images.go`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/config/images.go):
  one `CoreImages` field and default `ImageSpec` per new image (`Floating: true`, like localtest), with
  `STUDIOCTL_IMAGE_*` overrides. Rename `WorkflowEngineDb` to a shared Postgres spec (`postgres:18.3` today; the
  research stack ran 16, so run the components' migrations on 18 once before switching).
- [`internal/cmd/env/localtest/components/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/components):
  one `register<Name>Components` file per component on the pdf3/workflow-engine pattern: image (pulled, or in dev
  mode `localDevImage` with `ContextPath` = the component folder), `ContainerSpec` (name, environment, volumes,
  dependencies, `ExtraHosts`), and no published ports. The environment is today's
  [`docker-compose.yml`](../localtest/docker-compose.yml) per service: the platform endpoint, Postgres connection
  strings, `AccessTokenSettings__AccessTokenSigningCertificateFullPath`, the policy-store path. Events also gets
  `host.docker.internal:host-gateway`, so webhooks to the developer's machine arrive.
- `core.go`: the `localtest` container runs the localtest app image. It keeps the network aliases
  (`local.altinn.cloud`, `pdf.local.altinn.cloud`, `workflow-engine.local.altinn.cloud`, …), host port 8000, and the
  compatibility port 5101 (`dotnet run` with app-lib's `PlatformSettings` defaults). The new proxy routes
  platform paths on *any* host, so `localhost:5101` and the studioctl-server probe on `127.0.0.1:8000` reach the real
  components (the old matcher needed `Host: local.altinn.cloud`).
- **Postgres.** One `localtest-postgres` with a named volume and init scripts that create each component's
  database and roles (storage's `postgres_init.sql`, events', access management's, the workflow engine's). The init
  scripts live in the component folders and are bind-mounted from the studioctl data dir, or baked into a small
  `localtest-postgres` image so studioctl does not have to ship files.
- **Volumes.** `localtest-postgres-data`, `localtest-storage-blobs` (uid 1654), `localtest-policy-store`
  (uid 1000, shared by the PDP and Access Management), `localtest-data` (the localtest app). `env reset` deletes all of
  them ([`env.go:685-720`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/env.go#L685-L720)),
  and a migration (like `003_reset_localtest_data.go`) removes the old `AltinnPlatformLocal` directory and the
  workflow engine's separate database volume.
- **Readiness.** The executor waits for a container `HealthCheck`, which runs inside the container. The component
  images are chiseled or have no HTTP client, and podman builds OCI images, which drop Dockerfile `HEALTHCHECK`s.
  Proposal: the localtest app gets `GET /localtest/api/health`, which checks every upstream's `/health`; only the
  `localtest` container gets a health check (the AOT binary can probe itself: `Localtest --health`), and it reports
  ready when the stack is. The components start in dependency order (`Dependencies`) without health checks.

### 3.2 Routing and the topology

- The localtest app's proxy replaces localtest's `ProxyMiddleware`, and it must read the same inputs: the bound
  topology (`result.json`, mounted at `/studioctl/topology/result.json`) for app routes, the PDF and
  workflow-engine hosts, Grafana and the frontend dev server; and the **host bridge** for apps that run as host
  processes (studioctl-server's websocket to `/internal/host-bridge`). Today the localtest app takes app routes
  through `PUT localtest/api/proxy/apps/{org}/{app}` and reaches host apps at `host.docker.internal:<port>`; that
  API stays for scripts, but studioctl does not use it.
- Port `EnvRouteProxy`, `HostRouteProxy` and `HostBridgeClient` from today's localtest into the proxy feature, and
  reuse `BoundTopologyIndex` and `HostBridgeProtocol` from `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common`.
  That library serialises with reflection (`BoundTopologyIndexAccessor`, `HostBridgeProtocol`): give it a
  source-generated `JsonSerializerContext` and `IsAotCompatible`, so the AOT app can reference it. Old localtest
  keeps working with the same library.
- Platform prefixes (`/register/`, `/storage/`, …) stay in the localtest app's own route table; studioctl passes the
  upstream addresses as `Proxy__Upstreams__<name>=http://localtest-<name>:<port>`. The topology's `platform`
  component keeps its empty destination. Adding the components to `topology.yaml` is not needed (it would touch the
  four closed switch sites for nothing); revisit only if `studioctl env` should show or toggle them.
- Features of today's proxy the new one does not have yet: the frontend version override (`FrontendVersionOverride`
  on proxied HTML and the `frontendVersion` cookie) and cookie-domain rewriting. Port them with the proxy, or decide
  they go.

### 3.3 What `studioctl run` adds for an app

[`internal/cmd/app/env.go`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/app/env.go)
already sets every `PlatformSettings__Api*Endpoint` as `run-app.sh` does. Missing:

- **Platform access tokens.** The components validate the tokens apps sign (Storage's `PlatformAccess`, Register's,
  Profile's), which today's localtest never did. The app needs the signing certificate:
  `AccessTokenSettings__AccessTokenSigningCertificateFullPath` for `Altinn.Common.AccessTokenClient` 3.x, and for the
  v9 backend's 1.x the folder and file name (`AccessTokenSigningKeysFolder` is concatenated to the parent of the
  working directory, see `run-app.sh`). In container mode the certificate is mounted.
- **The certificate itself.** The research repo commits a test key pair (`localtest/certs/`). studioctl should instead
  generate one per installation in its data dir on first `env up`, and mount it into the app, the localtest app and every component that signs or
  verifies platform tokens (Register, Authentication, Authorization, Access Management, Storage, Events).

### 3.4 studioctl-server: provisioning an app (the deploy step)

Today's localtest *pulls* an app's metadata, texts, policy and `testData.json` from the running app
(`LocalAppHttp`). The production components do not; Designer pushes on deploy. The research repo's
[`run-app.sh`](../localtest/run-app.sh) does that push; in studioctl it belongs in studioctl-server, which already
discovers apps and probes them
([`AppRegistry`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/studioctl-server/Discovery/AppRegistry.cs)).
On a new or restarted app:

1. Read from the app over HTTP, as today's localtest does: `api/v1/applicationmetadata?checkOrgApp=false`,
   `api/v1/applicationlanguages` then `api/v1/texts/{lang}` per language, `api/v1/meta/authorizationpolicy`,
   `testData.json`.
2. Get Designer's credential from the localtest app (`GET /Home/GetTestStudioToken`).
3. Push: `PUT register/api/localtest/apps/{org}/{app}` (test data), `POST`/`PUT storage/api/v1/applications…` and
   `…/texts…`, `POST authorization/api/v1/policies?org&app`, `POST accessmanagement/api/localtest/refresh`.
4. Then the existing readiness probe (`LocaltestStorageProbe`, `GET storage/api/v1/applications/{org}/{app}`) passes,
   because Storage now has the app.

On app stop: `DELETE register/api/localtest/apps/{org}/{app}`, so the test users of a stopped app disappear as they
do today. A change to metadata, texts or policy needs a re-push: on app restart, or on a file watch in
`studioctl run --watch`, or a `studioctl app sync` command.

### 3.5 Docs and agent material

`src/cli/README.md` and the bundled skill
([`resources/agent/skills/app-development/SKILL.md`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/resources/agent/skills/app-development/SKILL.md))
describe localtest's endpoints and data folder; the AGENTS rule requires keeping them current. The docs site's local
testing pages (`altinn-studio-docs`) need the same update.

## 4. Changes in the components and the localtest app

- Each component already takes its settings from the environment; nothing studioctl-specific is needed beyond the
  upstream URLs, the certificate path and the database connection strings.
- The localtest app: the bound-topology and host-bridge work above, `GET /localtest/api/health`, and a `--health`
  switch for the container health check.
- `run-app.sh`, `stop-apps.sh` and `smoke-test.sh` stay as developer tools for running the stack without studioctl;
  the smoke tests become CI checks against `studioctl env up` in dev mode.

## 5. Contracts to keep (from today's localtest)

| Contract | New stack |
| --- | --- |
| `GET /Home/Localtest/Version` (app-lib's `LocaltestValidation`, ≥ 6) | The localtest app answers `8` |
| `/`, `/Home/LogInTestUser`, `/Home/GetTest*Token` (Cypress, scripts) | The localtest app's Home feature, same contract |
| `{storage authority}/health` (integration tests' connectivity check, snapshot `Healthy`) | The localtest app's `/health` answers `ok`, not `Healthy`: align, or update the snapshot |
| Storage self links `https://platform.local.altinn.cloud/…` (58 integration-test snapshots) | Production Storage builds `https://platform.{GeneralSettings:Hostname}`; with `Hostname=local.altinn.cloud` they match (paging `next` links differ by design) |
| Test users and party ids (`12345.512345`, `1337.500000`, …) | The register seed keeps them |
| StorageExplorer, `/LocalPlatformStorage`, Tenor and debug-user pages | Not in the new stack; Storage data is in Postgres (pgAdmin is already a studioctl tool) and the blob volume |

## 6. Rollout

Today's localtest image follows `main` while developers run older studioctl versions, and the cli AGENTS rule is to
keep the previous spelling working for at least one studioctl release. The new stack changes the container set, so:

1. Land the stack in `src/Runtime/localtest-v2/` with its images, and a studioctl that runs it behind a flag
   (`STUDIOCTL_LOCALTEST=v2` or `env up --localtest v2`). Default stays the old localtest.
2. Run the app-frontend Cypress suite and the app-lib integration tests (`StudioctlEnvironment`) against both in CI.
3. Flip the default in a studioctl release, with a migration that resets local data (the old file storage cannot be
   carried over).
4. After the supported studioctl window, delete the old localtest and its image, and move `localtest-v2` into place.

## Open questions

- The certificate: generate per installation (proposed) or ship a test certificate in the images.
- Frontend version override and cookie-domain rewriting: port or drop.
- Re-push on change: watch, restart, or an explicit `studioctl app sync`.
- One Postgres image with baked-in init scripts, or scripts mounted by studioctl.
- Whether `studioctl env` should list the components (topology entries) or treat the stack as one unit.
