# studioctl topology, vendoring practice and the app ↔ platform contract

Strand: how `studioctl env up` composes containers, how localtest routes to them, what apps expect from
"the platform", and how external code has been vendored into the monorepo so far.

Evidence base: `Altinn/altinn-studio` at `447e53eb97` (2026-10-16), `Altinn/app-lib-dotnet` at `4740bb28`
(2026-10-16). Paths are relative to `/home/agent/code/altinn-studio` unless prefixed.

## Executive summary

- **Adding a container is cheap and well-trodden.** pdf3 and workflow-engine are the templates. A new
  component means: an `ImageSpec` in `src/cli/internal/config/images.go`, a `register*Components` file in
  `src/cli/internal/cmd/env/localtest/components/` (image + `ContainerSpec` + optional health check/named
  volume + `RuntimeBinding`), a new `ComponentID` wired into **four closed Go switch/struct sites** in
  `src/cli/internal/envtopology/`, a `topology.yaml` entry, reset handling for any volume, and the CI
  image cache list. No localtest code change is needed for routing: localtest's `ProxyMiddleware` is
  generic and driven by the generated `result.json`.
- **Apps always go through localtest.** `studioctl run`/`app run` point every `PlatformSettings__Api*Endpoint`
  except PDF and workflow-engine at `http://local.altinn.cloud:8000/<service>/api/v1/`, which in both
  process and container mode lands on the localtest container. A per-service path prefix under
  `local.altinn.cloud` (e.g. `/storage`) can therefore be routed to a real container without touching
  app-lib or the app's configuration.
- **The `platform` component is not a route.** Its binding has an empty destination, so studioctl leaves it
  out of `result.json`, and anything that matches no route falls through to localtest's own MVC controllers.
  A split is purely additive: add more specific prefix routes and everything else stays in localtest.
- **Main constraints on a split:**
  1. The matcher works on exact host plus path prefix. Requests addressed to `localhost:5101` (the
     `PlatformSettings` defaults used by plain `dotnet run`, which the `TODO` port mapping keeps alive) or
     to `127.0.0.1:8000` (the studioctl-server storage readiness probe) never match a
     `local.altinn.cloud` route, so they keep hitting localtest's built-in copy.
  2. The full path is forwarded unchanged, with no prefix stripping, and the `Host` header stays
     `local.altinn.cloud`.
  3. A disabled route returns 404 instead of falling through.
  4. The localtest image follows `main` while a developer's studioctl is older, so localtest must keep its
     built-in implementation until every supported studioctl routes the prefix away.
- **The app has its own localtest contract.** `LocaltestValidation` probes `<authority of
  ApiStorageEndpoint>/Home/Localtest/Version` and shuts the app down on a 404. The storage endpoint
  therefore must keep pointing at localtest, which another argument for proxying through localtest rather
  than pointing apps straight at a real service. Localtest's storage, authorization (PRP) and texts read
  from the running app (`LocalAppHttp`), and studioctl-server's readiness probe depends on that.
- **Vendoring practice is non-squashed `git subtree`.** The root `Makefile` drives it for
  app-localtest, app-frontend-react, app-lib-dotnet, fileanalyzers, codelists and the test apps; the
  2026-06-04 merge `1166612623` is one `git subtree pull --prefix=src/Runtime/localtest`. Platform service
  code has never been subtree'd. It is consumed only as NuGet contracts (`Altinn.Platform.Storage.Interface`,
  `Altinn.Platform.Models`, `Altinn.Common.PEP`, `Altinn.Authorization.ABAC`), and localtest has *manually
  ported* altinn-storage PRs (e.g. "Sync altinn-storage PR 995 (#211)").
- **Verdict (mechanism cost only):** the cheapest real containers are *leaf* services that the app calls
  through one `PlatformSettings` prefix and that localtest does not use internally: **notifications**,
  **events** and **correspondence** (which localtest lacks entirely). **Storage** and **authorization**
  cost the most: localtest uses them in-process (PDP inside storage, the StorageExplorer and
  `/LocalPlatformStorage` browsing, app-sourced application metadata and policies, the studioctl-server
  probe, the version probe derived from the storage URL).

---

## 1. Adding a containerized component to `studioctl env up`

### 1.1 Composition pipeline

`Env.Up` (`src/cli/internal/cmd/env/localtest/env.go:103-149`):

1. `envtopology.NewLocal("8000")` loads the **embedded** `topology.yaml` (`src/cli/internal/envtopology/load.go`,
   `//go:embed`, `decoder.KnownFields(true)`). It is compiled into studioctl, not shipped in the image.
2. `components.NewManifest(opts)` (`components/manifest.go:25-40`) calls the `register*Components` functions
   in order: network → topology bindings → core (localtest) → pdf → workflow-engine → pgadmin → monitoring.
   Each adds `resource.Resource`s (image + container) and `envtopology.RuntimeBinding`s.
3. `envtypes.EnsureBoundTopology` (`src/cli/internal/cmd/env/preconditions.go:13-28`) writes
   `~/.altinn-studio/.../generated/topology/env.json` from the bindings and starts studioctl-server. The
   server's `BoundTopologyConfigReconciler` (`src/cli/studioctl-server/Topology/BoundTopologyConfigReconciler.cs`)
   merges `env.json` with discovered apps (process/container discovery) into `result.json`. Localtest
   reads that file from the read-only bind mount `/studioctl/topology/result.json`
   (`BoundTopologyOptions__ConfigPath`).
4. `manifest.Prepare` runs host-side preparations (create dirs, pgpass), then the devenv executor applies the
   resource graph (`src/Runtime/devenv/pkg/resource`). Containers have `DependsOn` refs and
   `WaitForReady: true`, so the executor waits until a container with a `HealthCheck` reports healthy
   (`devenv/pkg/resource/executor/container/runtime.go:228`, `helpers.go:53`). Podman ignores health status
   when no healthcheck is configured (`devenv/pkg/container/podman/podman.go:553`).

### 1.2 Checklist for a new component (pdf3/workflow-engine as templates)

| Piece | Where | Notes / evidence |
|---|---|---|
| Image reference | `src/cli/internal/config/images.go` | Add a field to `CoreImages`, a default `ImageSpec` (`Floating: true` re-pulls on every `env up` via `PullAlwaysAllowStale`; pinned images use `PullIfNotPresent`, see `components/image_mode.go:47-53`) and an override in `imageOverrides()` (`STUDIOCTL_IMAGE_*`). The AGENTS.md rule: "Image references live in the CLI, not in a config file". localtest follows `:latest` (main); pdf3/workflow-engine follow `:tt02`, moved by `.github/workflows/deploy-runtime-{pdf3,workflow-engine-app}.yaml` ring-tag jobs; postgres/pgadmin/monitoring are pinned. |
| Dev build | same component file | `if ctx.ImageMode == DevMode && ctx.DevConfig != nil` → `localDevImage(ctx.PrebuiltDevImages, &resource.BuiltImage{ContextPath, Dockerfile, Tag: "<name>:dev"})` (`core.go:25-41`, `pdf.go:29-46`). DevMode = `STUDIOCTL_INTERNAL_DEV=true` inside a monorepo with `src/Runtime/localtest/Dockerfile` (`env.go:906-931`). A service whose source is *not* in the monorepo has no dev build, so it would be pulled in both modes (like postgres). |
| Container name | `components/containers.go` | Constant, e.g. `ContainerPDF3 = "localtest-pdf3"`. The name is also the DNS name on `altinntestlocal_network` (`network.go`). |
| `ContainerSpec` | `components/resources.go:20-32`, `newContainerSpec(...)` | Fields: `Name, Ports, Environment, Volumes, NetworkAliases, Dependencies, Command, HealthCheck, ExtraHosts, UseDefaultUser`. Set `UseDefaultUser = true` when there are no host-writable bind mounts (pdf3, workflow-engine). |
| Dependencies | `Dependencies: []string{ContainerLocaltest, ...}` | Becomes `DependsOn` refs (`resources.go:258-268`). workflow-engine depends on its DB and localtest. |
| Health check | `spec.HealthCheck = &types.HealthCheck{Test, Interval, Timeout, Retries, StartPeriod}` | Workflow-engine uses a bash `/dev/tcp` probe because the aspnet base image ships no curl/wget (`workflow_engine.go:33-38`); postgres uses `pg_isready`. |
| Named volume | `newNamedVolume(name, path)` | `WorkflowEngineDbVolume = "localtest-workflow-engine-db-data"`. Bind mounts go under the studioctl data dir (`Paths.DataDir`/`InfraDir`). |
| Reset | `env.go:685-720` `deletePersistedData` + `removeWorkflowEngineDbVolume` | `env reset` explicitly deletes `AltinnPlatformLocal` and the workflow-engine volume. A new volume must be added here, and a forced wipe on upgrade needs a migration like `src/cli/internal/migrations/003_reset_localtest_data.go` (calls every `Resetter` in `registry.Envs`) or `007_reset_workflow_engine_data.go`. |
| Topology component | `src/cli/internal/envtopology/topology.yaml` + **4 Go sites** | `components.go` (`ComponentID` const + `switch` in `Component()`), `model.go` (`componentsDefinition` struct field with yaml tag), `validate.go` (map in `validateDefinition`). The list is closed: unknown yaml keys fail because of `KnownFields(true)`. Kinds: `app, platform, telemetry, runtime-service, monitoring, tool, frontend-dev`; `platform`/`runtime-service` accept `host` + optional `pathPrefix`, no `port`. |
| RuntimeBinding | `manifest.addBinding(envtopology.RuntimeBinding{ComponentID, Destination{Location: "env", Kind: "http", URL: "http://<container>:<port>"}, Enabled})` | `pdf.go:16-27`. `Location: "host"` sends the request to a host port through the host bridge (frontend dev server, `--dev-workflow-engine`). |
| Hostnames | `envtopology/topology.go` `LocaltestIngressHosts()` (network aliases on the localtest container, `core.go:73`) and `HostFileHostnames()` (`studioctl env hosts add`, `cmd/env.go:876-891`) | Both are hard-coded lists. **`env up` does not edit the hosts file**, so a new *hostname* (e.g. `storage.local.altinn.cloud`) needs every developer to rerun `env hosts add`. A **path prefix on `local.altinn.cloud` needs no hosts change.** |
| Status/logs | `EnabledContainerNames`, `log_streamer.go`, `status.go` | Derived from the manifest, so no extra work. |
| CI | `.github/actions/setup-localtest-images/action.yml` | The cache key hashes a fixed list of source dirs, and `docker image save` names the images explicitly (`localtest:dev localtest-pdf3:dev localtest-workflow-engine:dev <db image>`). A new image must be added, or CI jobs that require a cache hit will pull it every run. |
| Bundled skill/docs | `src/cli/resources/agent/skills/app-development/SKILL.md`, `src/cli/README.md` | AGENTS.md requires keeping these current. |

### 1.3 How localtest proxies

- `ProxyMiddleware` is the **first** middleware (`src/Runtime/localtest/src/Startup.cs`, before `/health`, static files,
  routing, auth). It calls `BoundTopologyIndex.MatchRequest(Request.Host.Host, path)`
  (`src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common/EnvTopology/BoundTopologyIndex.cs`).
- Match result:
  - no match → `_nextMiddleware` (localtest's own controllers);
  - route `Enabled == false` → **404, no fallthrough**;
  - `Destination.Kind != "http"` → fall through;
  - `Location == "env"` → `EnvRouteProxy.Proxy(context, url)`;
  - `Location == "host"` → `HostRouteProxy`, which goes over the host bridge.
- `EnvRouteProxy` (`src/Filters/EnvRouteProxy.cs`) is a YARP `IHttpForwarder` with a static
  `SocketsHttpHandler` (5 s connect timeout, no redirects). `HttpTransformer.Default` forwards the **full
  original path and query**, with no prefix stripping. It forces `Host` to the browser-visible host, rewrites
  cookie domains (`CookieDomainRewriter`) and applies `FrontendVersionOverride` to HTML responses. Errors
  return a 502 HTML page.
- Host bridge: studioctl-server (`src/cli/studioctl-server/HostBridge/HostBridgeConnector.cs`) opens a
  websocket to localtest's `/internal/host-bridge` (`HostBridgeDefaults.EndpointPath`). Localtest's
  `src/HostBridge/HostBridgeClient.cs` multiplexes proxied requests as frames
  (`Altinn.Studio.Runtime.Common/HostBridge/HostBridgeProtocol.cs`) to host-side targets: apps run as
  processes, the Vite dev server and a host workflow engine. **Env containers never use it**; they are
  reached directly on the docker network.

### 1.4 Matcher semantics (`BoundTopologyIndex`)

- Routes are grouped by exact host (case-insensitive; `Request.Host.Host` drops the port). Within a host:
  prefix routes come before host-only routes, then **longest prefix wins**.
- A prefix matches if `path == prefix` or `path` starts with `prefix + "/"`; a prefix ending in `/` uses a
  plain `StartsWith`. Matching is case-insensitive.
- Several routes with the same host+prefix are round-robined (`RouteGroup.Next`), which is used for app replicas.
- App routes are generated by studioctl-server from `appRouteTemplate` `/{org}/{app}` on `local.altinn.cloud`.
- Go side: `Local.BoundTopologyConfig` (`bound_topology.go:157-180`) **skips bindings whose
  `Destination.Kind == ""`** (`HasRoute()`) and skips `app`. Each component has a single `pathPrefix`.

### 1.5 The `platform` component

`registerTopologyComponents` (`components/topology.go:23-31`) binds `ComponentPlatform` with an empty
destination, so it is **not emitted into `result.json`**. Requests to `local.altinn.cloud/<anything not
claimed by /grafana or /{org}/{app}>` match no route and fall through to localtest's MVC controllers
(`storage/api/v1/...`, `register/api/v1/...` and so on). The component exists only so that
`PlatformAPIBaseURL()`/`PublicBaseURL(ComponentPlatform)` can build `http://local.altinn.cloud:8000`
for the app environment.

### 1.6 How the app reaches the platform

- `src/cli/internal/cmd/app/env.go:82-96` (`addRunDefaults`, using `setDefault`, so the caller's env wins).
  `endpoints.platform = topology.PublicBaseURL(platform)` = `http://local.altinn.cloud:8000`
  (`config.go:15-21`). PDF = `http://pdf.local.altinn.cloud:8000/pdf`, workflow engine =
  `http://workflow-engine.local.altinn.cloud:8000/api/v1/`. Both are also proxied by localtest, through
  host-only routes on their own hostnames.
- Process mode (`run.go:143`): the hosts file maps `local.altinn.cloud` to 127.0.0.1, and host port 8000 is
  published to localtest:5101.
- Container mode (`run.go:213-286`): the app joins `altinntestlocal_network`, where `local.altinn.cloud`,
  `pdf.local...` and the other ingress hostnames are **network aliases of the localtest container**, and
  localtest also listens on 8000 inside the container (`localtestListenURLs`, `core.go:43-48`).
- **Conclusion:** with `studioctl run`, every platform call goes through localtest. A real service
  container is reached only via a localtest route. Real service containers can call back to
  localtest-retained services (register, authentication/OIDC, authorization) at
  `http://local.altinn.cloud:5101|8000` thanks to the same aliases.

### 1.7 Compatibility constraints

- `TODO` in `core.go:61-67`: localtest also publishes host `5101:5101` because the `PlatformSettings`
  defaults (`http://localhost:5101/...`) are what plain `dotnet run --project App` uses. pdf3 publishes
  `5300:5031` for the same reason (`pdf.go:52`). Requests with `Host: localhost` **do not match any
  `local.altinn.cloud` route**, so in a split they would keep hitting localtest's built-in copies. The
  split would be inconsistent for `dotnet run` users unless routes are added for the `localhost` host too.
  The v9 default `ApiWorkflowEngineEndpoint = http://localhost:9090/api/v1/` has no host port mapping
  except in `--dev-workflow-engine` mode. I could not confirm whether that is intentional.
- `src/cli/AGENTS.md` ("Container images") and `src/Runtime/localtest/AGENTS.md`: the localtest image
  follows `latest` from main with no ring, while a developer's studioctl is older than the image it pulls.
  "Keep the previous spelling working for at least one studioctl release, and change studioctl in the
  same pull request." For a split this means a staged rollout:
  1. Ship studioctl with the new component and prefix route. Localtest still implements the service, so
     older studioctl versions fall through to it.
  2. Only after the supported studioctl window has passed, remove the copy from localtest.
- `env up` returns early on a converged environment, so new images and components arrive on
  `env down` + `env up`.

---

## 2. App ↔ platform contract

### 2.1 `PlatformSettings` defaults

`src/App/backend/src/Altinn.App.Core/Configuration/PlatformSettings.cs` (v9) and
`/home/agent/code/app-lib-dotnet/src/Altinn.App.Core/Configuration/PlatformSettings.cs` (v8) are identical
except that v9 adds `ApiWorkflowEngineEndpoint`.

| Property | Default | studioctl `run` override |
|---|---|---|
| ApiStorageEndpoint | `http://localhost:5101/storage/api/v1/` | `http://local.altinn.cloud:8000/storage/api/v1/` |
| ApiRegisterEndpoint | `…:5101/register/api/v1/` | `…:8000/register/api/v1/` |
| ApiProfileEndpoint | `…:5101/profile/api/v1/` | `…:8000/profile/api/v1/` |
| ApiAuthenticationEndpoint | `…:5101/authentication/api/v1/` | `…:8000/authentication/api/v1/` |
| ApiAuthorizationEndpoint | `…:5101/authorization/api/v1/` | `…:8000/authorization/api/v1/` |
| ApiEventsEndpoint | `…:5101/events/api/v1/` | `…:8000/events/api/v1/` |
| ApiPdf2Endpoint | `http://localhost:5300/pdf` | `http://pdf.local.altinn.cloud:8000/pdf` |
| ApiNotificationEndpoint | `…:5101/notifications/api/v1/` | `…:8000/notifications/api/v1/` |
| ApiCorrespondenceEndpoint | `…:5101/correspondence/api/v1/` (`// TODO: which port for localtest?`) | `…:8000/correspondence/api/v1/` |
| ApiAccessManagementEndpoint | `…:5101/accessmanagement/api/v1/` | `…:8000/accessmanagement/api/v1/` |
| ApiWorkflowEngineEndpoint (v9 only) | `http://localhost:9090/api/v1/` | `http://workflow-engine.local.altinn.cloud:8000/api/v1/` |
| Altinn3LibraryApiEndpoint | `https://studioprodsc.blob.core.windows.net/...` | not overridden (external) |
| AppSettings:OpenIdWellKnownEndpoint | n/a | `…:8000/authentication/api/v1/openid/` |

There is **no** `ApiResourceRegistryEndpoint` in app-lib (v8 or v9); resource registry is consumed only
inside localtest (by its PDP/authorization). There is no Maskinporten endpoint on `PlatformSettings`;
`MaskinportenClient` uses `ApiAuthenticationEndpoint + /exchange/maskinporten`.

### 2.2 Service table

| Service | Prefix | App-lib clients (v9; v8 adds the ones marked †) | Localtest implements? |
|---|---|---|---|
| Storage | `/storage/api/v1/` | `ApplicationClient`, `DataClient`, `InstanceClient`, `InstanceEventClient`, `ProcessClient`, `SignClient`, `RuntimeEnvironment` (base URL), `FiksArkivInstanceClient`; v8 also `InstanceLockClient`†, `TextClient`†, `EformidlingStatusCheckEventHandler(2)`† | **Yes**: applications, texts, instances (+lock, mutations, process, events, complete, sign, readstatus/substatus/presentationtexts/datavalues), data (+lock, filescanstatus) |
| Register | `/register/api/v1/` | `AltinnPartyClient`, `PersonClient`, `RegisterERClient`, `RegisterClient` | **Partly**: `parties/{id}`, `parties/lookup`, `parties/partylist`, `persons`, `organizations/{orgNr}`. **Missing:** `apps/parties/query`, `access-management/parties/query` (used by `GetPartyIdByUrn`/`GetPartyUuidByUrn`) |
| Profile | `/profile/api/v1/` | `ProfileClient` | **Partly**: `users/{id}`, `users/current`. **Missing:** `users/byuuid/{uuid}`, `users` (by SSN) |
| Authentication | `/authentication/api/v1/` | `AuthenticationClient`, `MaskinportenClient`, `HomeController` (redirect) | **Yes**: `refresh`, `authentication`, `orgToken`, `appToken`, `openid/.well-known/openid-configuration(+/jwks)`. **Missing:** `exchange/maskinporten` |
| Authorization | `/authorization/api/v1/` | `AuthorizationClient` (+ `Altinn.Common.PEP`) | **Yes**: `decision`, `parties`, `parties/{id}/validate`, `roles` |
| Access management | `/accessmanagement/api/v1/` | `AuthorizationClient` (`enduser/authorizedparties`), `UrlHelper` (`app/delegations/...`) | **Yes**: `enduser/authorizedparties`, `v1/app/delegations/...`, `v1/app/delegationrevoke/...` |
| Events | `/events/api/v1/` | `EventsClient`; v8 also `EventsSubscriptionClient`† | **Yes**: `app`, `events`, `subscriptions` (POST) |
| Notifications | `/notifications/api/v1/` | `EmailNotificationClient` (`orders/email`), `SmsNotificationClient` (`orders/sms`), `NotificationOrderClient` (`future/orders`), `NotificationCancelClient` (`orders/{id}/cancel`) | **Partly**: `orders/email`, `orders/sms` only (`src/Notifications/API`). **Missing:** `future/orders`, cancel |
| Correspondence | `/correspondence/api/v1/` | `CorrespondenceClient` | **No** |
| Resource registry | `/resourceregistry/api/v1/` | none (not in `PlatformSettings`) | Yes (`resource`, `resource/{id}/policy`, `Search`); used internally |
| PDF | `pdf.local.altinn.cloud/pdf` | `PdfGeneratorClient` | Separate container (`localtest-pdf3`) |
| Workflow engine | `workflow-engine.local.altinn.cloud/api/v1/` | `WorkflowEngineClient` (v9) | Separate container + postgres |
| Maskinporten | external | `MaskinportenClient` | No |

The "missing" rows come from comparing client path strings with localtest `[Route]`/`[Http*]` attributes;
whether those code paths run in typical local flows is not confirmed.

---

## 3. Localtest-specific contract that a replacement must preserve

| Feature | Evidence | Implication for a split |
|---|---|---|
| **Version probe** | `Altinn.App.Core/Internal/LocaltestValidation.cs`. Active when `GeneralSettings.HostName` is `local.altinn.cloud` or `altinn3local.no` and `DisableLocaltestValidation` is false. GETs `{GetPlatformBaseUrl()}/Home/Localtest/Version`, where `GetPlatformBaseUrl()` is **the authority of `ApiStorageEndpoint`** (`RuntimeEnvironment.cs:38-39`). Requires version ≥ 6 (v9) / ≥ 5 (v8); localtest returns `LocalTestApi.Version = 6` (`src/Constants/LocalTestApi.cs`, `HomeController.cs:124`). 404 → "Shutting down..". | Pointing `ApiStorageEndpoint` straight at a real Storage container would kill the app. Keep storage behind localtest's ingress, or bump/redirect the probe. |
| **App-sourced metadata** | `Services/LocalApp/Implementation/LocalAppHttp.cs` fetches `/{org}/{app}/api/v1/applicationmetadata?checkOrgApp=false`, `/api/v1/meta/authorizationpolicy`, `/api/v1/texts/{lang}` and `testData.json` from the running app. These feed `ApplicationRepository`, `TextRepository`, `PolicyRetrievalPoint`, `TestDataService`. | Real Storage/Authorization read applications and policies from their own stores. A replacement needs a seeding step (on app discovery) or a shim. |
| **studioctl-server readiness probe** | `src/cli/studioctl-server/Discovery/LocaltestStorageProbe.cs` GETs `http://127.0.0.1:8000/storage/api/v1/applications/{org}/{app}` and requires `id == appId` before `studioctl run` reports the app ready (`AppRegistry.cs:~350`). | The `127.0.0.1` host does not match `local.altinn.cloud` routes, so this probe keeps hitting localtest's own storage. It also depends on the app-sourced metadata above. |
| **testData.json** | `LocalAppHttp.GetTestDataWithMetadata` merges `/{appId}/testData.json` from every reachable app with the baked-in `/testdata`. | Stays with register/login (which stay in localtest). |
| **Frontend version override** | `Controllers/FrontendVersionController.cs` (`/Home/FrontendVersion/Index`, sets the `frontendVersion` cookie). `Filters/FrontendVersionOverride.cs` rewrites `altinn-app-frontend.(js\|css)` URLs in proxied HTML (`EnvRouteProxy`/`HostRouteProxy` transformers). | Localtest-only; unaffected by a split (applies to app HTML). |
| **Home pages / fake login** | `HomeController` (`/`, `/Home/Index`, `/Home/LogInTestUser` used by Cypress `support/auth.ts:206`, `/Home/GetTest*Token`, `/Home/Tokens`), `TestAuthenticationController` (`/Home/auth/*`), `TenorUsersController`, `DebugUsersController` (`/Home/DebugUsers/LocalTestUsers{,Raw,RoundTrip}`, `ShowTenorUsers`), `InfraController` (grafana link via `TryGetComponentRoute("grafana")`). | Stays in localtest. Instantiation from the home page goes through `ILocalApp.Instantiate` → the app → storage, so it works with whichever storage the app talks to. |
| **StorageExplorer / LocalPlatformStorage** | `StorageExplorerController` → view with an iframe on `/LocalPlatformStorage/`; `Startup.cs` serves `LocalTestingStorageBasePath` (`/AltinnPlatformLocal`, bind-mounted from the studioctl data dir) with `UseStaticFiles` + `UseDirectoryBrowser`. | Lost or needs replacing if storage moves to a DB-backed container (e.g. pgAdmin, which is already in the stack). `env reset` deletes this directory. |
| **Integration tests** | `src/App/backend/test/Altinn.App.Integration.Tests/_fixture/StudioctlEnvironment.cs` always runs `studioctl env up --detach` with `STUDIOCTL_INTERNAL_DEV=true` (dev images from the checkout), apps via `studioctl run --mode process --detach --random-host-port --json`. `BasicAppTests.AppConnectivity_Localtest` calls `/{org}/{app}/api/testing/connectivity/localtest` (`_testapps/_shared/TestingApis.cs:96`), which GETs `<authority of ApiStorageEndpoint>/health`. Snapshot: `Url: http://local.altinn.cloud:8000/health, ResponseContent: Healthy`. 58 snapshot files contain storage self-links `https://platform.local.altinn.cloud/storage/api/v1/instances/...` (localtest builds them as `https://platform.{GeneralSettings.Hostname}` in `Controllers/Storage/InstancesController.cs:1215`). | A real Storage must be configured to produce identical self-links and response shapes, or ~60 snapshots change. `/health` must stay on localtest. |
| **App-frontend e2e** | `src/App/frontend/test/e2e/config/localtest.json`: `baseUrl http://local.altinn.cloud:8000`, fixed users/partyIds (`12345.512345`, `1337.500000`, …) from the baked-in testdata. CI: `.github/workflows/app-frontend-cypress.yml:447` (`--env environment=localtest`). | Stays valid as long as register, authentication and login stay in localtest and party IDs are stable. |

---

## 4. Vendoring and subtree practice in the monorepo

- **Mechanism:** root `Makefile` (`SUBTREES := localtest|src/Runtime/localtest|https://github.com/Altinn/app-localtest.git|main`,
  plus frontend, backend, fileanalyzers, codelists, and `TEST_APPS` under `src/test/apps`). `pull-subtree`
  runs `git subtree pull --prefix=<prefix> <url> <branch>`, **without `--squash`**, on a
  `chore/subtree-sync-<x>` branch; `sync-all` creates a single PR.
- **Initial adds** (`git log --grep=git-subtree-dir`): `src/Runtime/localtest/` (e7f38bd6a7, 2025-09-23,
  `git-subtree-split: d9c4e618…`), `src/App/frontend/` (2062d9d2ca), `src/App/backend/` (46aae14363),
  `src/App/fileanalyzers/` (d5b1e819af), `src/App/codelists/` (98d81e1070), `src/AI/mcp/` (568ec2e2c6, 2025-11-18).
- **`1166612623` (2026-06-04, Ivar Nesje, "Merge commit '49e2ba8e…'")** has parents `3ee2766153` (monorepo main)
  and `49e2ba8e6e` ("Flip default nullable behaviour (#212)"). `git ls-tree 49e2ba8e6e` shows the
  **app-localtest root layout** (`Dockerfile`, `LocalTest.sln`, `docker-compose.yml`, `.github`, …), while the
  merge diff lands in `src/Runtime/localtest/**` (281 files). That is the signature of a plain `git subtree pull
  --prefix=src/Runtime/localtest`; no filter-repo is involved, and the full upstream history is in the monorepo.
  Later syncs: `76e6b98fef` (2026-07-24) and `78a465ed05` "chore(localtest) subtree sync localtest (#20203)".
  App-lib-dotnet is still synced the same way (`573e2c71d9`, `2da4d0983d` "record the app-lib-dotnet sync up to #1873").
  `/home/agent/code/.reference/app-localtest` is a shallow clone (1 commit), so I could not cross-check the
  upstream SHAs there.
- **Platform service code has never been subtree'd.** Copies were **hand-ported**, as the app-localtest
  history (now in the monorepo) shows: `dea3e4e1fb` "Sync altinn-storage PR 995 (#211)" (refactor of
  `ProcessAuthorizer`), `055f3c9b8e` "Ports ProcessDataCleanupService from Storage", `c792d8fbde`
  "Support excludeConfirmedBy in Instances query API in Storage".
- **NuGet contracts** (no central pin for localtest):
  - `src/App/backend/Directory.Packages.props`: `Altinn.Common.AccessTokenClient 1.1.5`, `Altinn.Common.PEP 4.2.3`,
    `Altinn.Platform.Models 1.6.1`, `Altinn.Platform.Storage.Interface 5.0.0-rc.2`.
  - `src/Designer/backend/Directory.Packages.props`: `Altinn.Common.AccessToken 5.1.13`, `AccessTokenClient 3.2.12`,
    `Altinn.Platform.Storage.Interface 5.0.0-rc.1`, `Altinn.Authorization.ABAC 0.1.1`.
  - `src/Runtime/localtest/src/LocalTest.csproj` (inline `Version=`, not central): `Altinn.Authorization.ABAC 0.1.1`,
    `Altinn.Common.PEP 4.2.3`, `Altinn.Platform.Models 1.6.1`, `Altinn.Platform.Storage.Interface 5.0.0-rc.1`
    (**one RC behind app-lib v9's rc.2**).
  - No repo-root `Directory.Packages.props`; each area pins on its own.

---

## 5. Verdict from the studioctl side

### 5.1 Mechanism cost per candidate

Every path-prefix component costs about the same in studioctl (§1.2). The difference is how much
*localtest-internal* coupling has to be cut and how many app-facing contracts hang off the prefix.

| Candidate | Extra studioctl/localtest cost beyond the generic checklist | Rank |
|---|---|---|
| **Correspondence** | None to remove (localtest has no implementation). The app calls only `ApiCorrespondenceEndpoint`. Adds capability instead of replacing it. | Cheapest (additive) |
| **Notifications** | Localtest has a small stub (`orders/email`, `orders/sms`) and nothing else in localtest calls it. A real container would also cover `future/orders` and cancel. | Cheapest replacement |
| **Events** | Leaf for the app (`EventsClient`). No other localtest component reads it (not confirmed exhaustively). | Cheap |
| Resource registry | Not called by apps. Consumed in-process by localtest authorization (`IResourceRegistry` in `Startup.cs:128`), so it only helps if authorization moves too. | Low value alone |
| Profile | Localtest test users are joined across register, profile and testData. The fake login and DebugUsers depend on them. | Coupled to register (stays) |
| Authorization / access management | Localtest storage authorizes in-process (`StorageAccessHandler`, `Services/Storage/Implementation/AuthorizationService.cs`, `PolicyRetrievalPoint` pulling the policy from the app). Needs a policy-seeding path. | Expensive |
| **Storage** | Version probe authority, `/health` connectivity test, studioctl-server readiness probe at `127.0.0.1`, StorageExplorer / `LocalPlatformStorage`, app-sourced `applications`/`texts`, ~60 self-link snapshots, in-process PDP. | Most expensive |

### 5.2 What a "platform split" would look like

Keep `platform` as the catch-all (empty destination, falls through to localtest) and add one component per
moved service, all on `local.altinn.cloud` with a path prefix:

```yaml
components:
  platform:            # catch-all: not emitted, falls through to localtest MVC
    kind: platform
    host: local.altinn.cloud
  notifications:
    kind: platform
    host: local.altinn.cloud
    pathPrefix: /notifications
  events:
    kind: platform
    host: local.altinn.cloud
    pathPrefix: /events
  correspondence:
    kind: platform
    host: local.altinn.cloud
    pathPrefix: /correspondence
```

with bindings such as `{ComponentID: "notifications", Destination: {Location: "env", Kind: "http",
URL: "http://localtest-notifications:5090"}, Enabled: true}`. The app's `PlatformSettings__ApiNotificationEndpoint`
does not change (`http://local.altinn.cloud:8000/notifications/api/v1/`). Localtest forwards
`/notifications/api/v1/...` verbatim, which works because the Altinn platform services serve under their own
service prefix. (I believe they do, for example `storage/api/v1`, but I did not verify this against each service's source here.)

Required changes and caveats:

1. **Closed component set.** Each new ID touches `components.go`, `model.go`, `validate.go` and `topology.yaml`.
   If several services move, refactor `componentsDefinition` into a map, or allow `pathPrefixes: []` on one
   component, instead of growing four switch statements.
2. **Disabled ≠ absent.** For an optional component (behind a flag), emit `Destination.Kind == ""` when it
   is off (like `platform`), not `Enabled: false`. A disabled route returns 404 and would *shadow*
   localtest's built-in implementation.
3. **Host-exact matching.** Routes only apply to `Host: local.altinn.cloud`. `dotnet run` apps (`localhost:5101`),
   the studioctl-server probe (`127.0.0.1:8000`) and anything else using IP/localhost bypass the split. Fix
   by emitting duplicate routes for `localhost`/`127.0.0.1`, or by adding host aliases to the matcher.
4. **No path rewrite.** The target must accept the full public path and the `local.altinn.cloud` `Host` header.
   There is no per-route transform support in `EnvRouteProxy` today.
5. **Prefix collision with apps.** App routes `/{org}/{app}` share the host. Longest prefix wins, so
   `/events/api` would be taken by an app whose org is `events` and app is `api`. This is negligible but
   should be noted.
6. **Rollout order.** Because of the version skew (§1.7), the localtest image must keep each moved
   implementation until no supported studioctl lacks the route. The fall-through design makes this safe:
   old studioctl → no route → built-in.
7. **Callbacks.** Real containers reach retained services (register, OIDC/JWKS at
   `/authentication/api/v1/openid/...`, authorization) via the `local.altinn.cloud` network alias to localtest.
   No new networking is needed.
8. A per-service **hostname** (e.g. `storage.local.altinn.cloud`) is possible (kind `runtime-service`, like
   pdf), but it requires an app `PlatformSettings` change in `cmd/app/env.go`, additions to
   `LocaltestIngressHosts`/`HostFileHostnames`, and every developer rerunning `studioctl env hosts add`.
   The path prefix avoids all three.

### 5.3 Not confirmed

- Which tags/registries the real platform services publish (ghcr vs ACR, a `tt02`-style moving tag), and
  therefore whether `Floating` tracking like pdf3 is possible. This is out of this strand; see
  `images-and-repos.md`.
- Whether app-lib code paths that hit the missing localtest endpoints (register `*/parties/query`, profile
  `users/byuuid`, notifications `future/orders`) are exercised in normal local flows.
- Whether the missing host port mapping for the v9 `ApiWorkflowEngineEndpoint` default (`localhost:9090`) is
  intended.
