# Aspire host for the components: mapping, front door, provisioning, fit

**Superseded 2026-10-23 by task 27:** the package is now a thin loader of manifest v2 with the localtest container as its front door: no `Aspire.Hosting.Yarp` front door, no compiled-in provisioning, no probe ports and no image override variables, so the front-door and provisioning sections are history; see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

Research date 2026-10-21 (Europe/Oslo), task 20 of [`../../TODO.md`](../../TODO.md). Written against this repository
at `2c2d3aa` (the vendored studioctl's `localtestv2/` runtime, image pin `tagLocaltestV2 = "sha-3143730"` in
`internal/config/images.go`), `Altinn/altinn-studio` `origin/main` at `3ccccf7867` (app-lib v9 and the app template
under `src/App/`), and Aspire **13.6.2** (`Aspire.AppHost.Sdk`, `Aspire.Hosting`, `Aspire.Hosting.Yarp`,
`Aspire.Hosting.PostgreSQL`, `Aspire.Hosting.Redis`, `Aspire.Hosting.Docker`, `Aspire.Hosting.Testing`: all 13.6.2 on
nuget.org that day; `Aspire.Hosting.Kubernetes` only as `13.6.2-preview.1`).

The question: can the stack's components run as container resources of an Aspire AppHost next to an Altinn app
project, so an app developer gets the platform from `dotnet run` on the AppHost instead of `studioctl env up`, and is
it worth doing.

Method: the studioctl runtime source read (`components.go`, `groups.go`, `topology.go`, `health.go`, `env.go`,
`certs.go`, `studioctl-server/Proxy/`, `Provisioning/AppProvisioner.cs`, `Discovery/`, `internal/cmd/app/`), app-lib's
local-run code in the monorepo read, and a **scratch AppHost** (net10.0, `Aspire.AppHost.Sdk/13.6.2`, a stand-in
`App` project) that models a representative slice of the stack: the research Postgres image, Redis pinned by digest,
five components from a table, the certificate bind mount, the ingress `--add-host` entries, group gating with
`WithExplicitStart`, `AddDockerfile` for `--dev`, the YARP front door with path and host transforms, the app project
with its environment, a `ResourceReadyEvent` subscriber and a `WithCommand` for provisioning. It **compiles** (`dotnet
build`, 0 errors). It was **not run**: no container was started, no Podman call, no DCP or dashboard process (see
section 8). API statements below come from that build and from the packages' XML documentation; behaviour statements
that need a running AppHost are marked as such.

## 1. What studioctl does today, in Aspire's terms

`studioctl env up -r localtest-v2` does six things (`env.go`, `Up`):

1. Writes the embedded test certificate to its data directory (`certs.go`), world-readable.
2. Writes the **route table** (`topology.go`, `boundTopologyConfig`): every component's host and prefix, an optional
   group's routes written *disabled* with the group in the metadata, plus the frontend dev server's host.
3. Writes the state file (certificate directory, proxy ports, enabled groups) that studioctl-server and `studioctl run`
   read.
4. Starts **studioctl-server** on the host in proxy mode: the front door on `local.altinn.cloud:8000`, bound on every
   interface because the containers call it back through the runtime's host gateway.
5. Applies the **devenv resource graph** (`components.go`, `resources`): one network `localtest-v2_network`, then per
   component a pulled (or, with `--dev`, built) image and a container with env, mounts, extra hosts, aliases,
   dependencies and an optional container health check; the executor orders by `DependsOn` and waits for readiness.
6. **Polls** every component's health path on its loopback host port, then the front door
   (`/Home/Localtest/Version`), for up to three minutes (`health.go`).

Then, for each app, studioctl-server **discovers** it (registered by `studioctl run`, or found by process name
`Altinn.App*`/`Altinn.Application*` among the host's port listeners, which is how an IDE-started app is found),
**provisions** it (`AppProvisioner.cs`: steps 1 to 5 below, fetching the artefacts from the running app over HTTP) and
**routes** `/{org}/{app}` to it. `studioctl run` gives the app its environment (`internal/cmd/app/env.go`), and app-lib
v9 pulls the same environment itself when studioctl did not start it (`StudioctlLocalConfiguration`, below).

One topology fact shapes everything that follows: **the components do not call each other by container name.** Apart
from Postgres (alias `postgres`) and Redis (alias `redis`), every component reaches every other through the public
origin `http://local.altinn.cloud:8000/`, which each container resolves to the host gateway (`--add-host
local.altinn.cloud:host-gateway` and the other ingress hosts) and which studioctl-server answers on the host. So an
Aspire host does not need Aspire's service discovery or `WithReference` between components at all; it needs **one
front door that the containers and the browser both reach as `local.altinn.cloud:8000`**, and the routes behind it.

## 2. Mapping: each component as an Aspire resource

Images are `ghcr.io/ivarne-agent/localtest-research/localtest-<name>:sha-3143730` unless noted; every one maps to
`AddContainer(name, "ivarne-agent/localtest-research/localtest-" + name, tag).WithImageRegistry("ghcr.io")`. "Port" is
host:container as studioctl publishes it on `127.0.0.1`; in Aspire `WithHttpEndpoint(port: host, targetPort:
container, isProxied: false)`. "Cert" is the shared certificate read-only at `/certs` (`WithBindMount(certDir,
"/certs", isReadOnly: true)` plus `AccessTokenSettings__AccessTokenSigningCertificateFullPath`); "cert at /accesstoken"
is the second read-only bind of the same file at `/accesstoken/accesstokencredentials.pfx`. "Platform" is the
`...PlatformEndpoint` variable set to `http://local.altinn.cloud:8000/`. Dependencies are `WaitFor(...)`; health is
`WithHttpHealthCheck(path)`.

| Component | Group | Port | Environment beyond cert and platform | Mounts | Waits for | Aspire notes |
| --- | --- | --- | --- | --- | --- | --- |
| `postgres` (`localtest-postgres`, Postgres 18 with every init script baked in) | – | none | `POSTGRES_USER`/`PASSWORD` `postgres`, `TZ=UTC`; command `postgres -c shared_preload_libraries=pg_stat_statements -c timezone=UTC -c log_timezone=UTC` | volume `localtest-v2-postgres-data` at `/var/lib/postgresql` | – | Either `AddContainer` + `WithArgs` + `WithVolume` + `WithContainerNetworkAlias("postgres")`, or `AddPostgres(name, user, password).WithImageRegistry(...).WithImage(...)` with `WithDataVolume`, which brings a host-side Npgsql health check. Aspire has no exec health check, so studioctl's `pg_isready` probe becomes that Npgsql check (or a registered TCP check). Init scripts need nothing: they are in the image |
| `register` | – | 8020:5020 | `GeneralSettings__OpenIdWellKnownEndpoint`, `PlatformSettings__ApiAuthorizationEndpoint` | cert | – | Plain |
| `profile` | – | 8036:5030 | `PostgreSqlSettings__AdminConnectionString`, `__ConnectionString` (`Host=postgres;...;Password={0}` — the component's fake fills the password) | cert | postgres, register | Connection strings stay literal; Aspire's `WithReference(db)` connection-string injection is not used, because the components read their own keys and the `{0}` convention |
| `authentication` | – | 8021:5101 | `GeneralSettings__PlatformEndpoint` | cert | register, profile | Plain |
| `authorization` (PDP) | – | 8022:5070 | – | cert, volume `localtest-v2-policy-store` at `/policy-store` | register, profile | Shared named volume: `WithVolume("localtest-v2-policy-store", "/policy-store")` on three resources |
| `storage` | – | 8024:5010 | Postgres connection strings (`storagedb`) | cert, volume `localtest-v2-storage-blobs` at `/data/blobs` | postgres | Plain |
| `access-management` | – | 8023:5080 | – | cert, policy store | postgres, register, resource-registry, profile | Plain |
| `events` | – | 8025:5080 | Postgres (`eventsdb`) | cert, cert at /accesstoken | postgres, register, authorization | Also `--add-host host.docker.internal:host-gateway` for webhooks to the developer's machine |
| `resource-registry` | – | 8027:5100 | Postgres (`resourceregistrydb`) | cert, policy store | postgres, storage | Plain |
| `notifications` | – | 8029:5090 | Postgres (`notificationsdb`) | cert, cert at /accesstoken | postgres, register, profile, authorization, authentication | Plain |
| `localtest` (the localtest app) | – | 8026:8000 | `Localtest__AccessTokenCertificatePath`, `Proxy__Upstreams__register`/`__authentication` = public origin | cert, volume `localtest-v2-localtest-data` at `/data` | – | The candidate front door in an Aspire host (section 3) |
| `pdf3` (`ghcr.io/altinn/altinn-studio/runtime-pdf3-worker`, floating tt02 tag) | – | 5300:5031 | `TZ`, `PDF3_ENVIRONMENT=localtest`, `PDF3_QUEUE_SIZE`, `PDF3_LOCALTEST_PUBLIC_BASE_URL` | – | – | Health `/health/ready`; a floating tag maps to `WithImagePullPolicy(ImagePullPolicy.Always)` |
| `workflow-engine` (`runtime-workflow-engine-app`, floating) | – | 8028:9090 | `ConnectionStrings__WorkflowEngine` (`Host=postgres`), `AppCommandSettings__CommandEndpoint` (public origin + app callback path), telemetry off | – | postgres | Health `/api/v1/health/ready`. With Aspire its telemetry could be switched **on** (`WithOtlpExporter()`) |
| `redis` (`docker.io/library/redis:7.2-alpine@sha256:29e8…`) | dialogporten | none | command `redis-server --save "" --appendonly no` | – | – | `AddRedis("redis").WithImageTag("7.2-alpine").WithImageSHA256("29e8…")` (compiles; Aspire's own Redis health check) or `AddContainer` + alias `redis` |
| `dialogporten-webapi` | dialogporten | 8030:8080 | `Localtest__Dialogporten__PlatformEndpoint` | – | postgres, redis, register, authentication, authorization, access-management, events, resource-registry | Health `/health/readiness`. studioctl uses a **container** health check here (bash `/dev/tcp`) so the other two hosts start after its migrations; `WithHttpHealthCheck` + `WaitFor` gives the same ordering from the host side |
| `dialogporten-graphql` | dialogporten | 8031:8080 | as webapi | – | dialogporten-webapi | Route `/dialogporten/graphql` rewritten to `/graphql` (section 3) |
| `dialogporten-service` | dialogporten | 8032:8080 | as webapi | – | dialogporten-webapi | No route |
| `dialogporten-adapter` | dialogporten | 8035:5011 | `Localtest__Adapter__PlatformEndpoint` | – | storage, register, authentication, dialogporten-webapi | Route `/storage/dialogporten` must win over `/storage` (longest prefix) |
| `receipt` | receipt | 8034:5060 | `GeneralSettings__OpenIdWellKnownEndpoint`, `__Hostname`, five `PlatformSettings__Api*Endpoint` | cert at /accesstoken only | register, profile, authentication, storage | Plain |
| `correspondence` | correspondence (implies dialogporten) | 8033:2525 | `DatabaseOptions__ConnectionString` (`correspondence` db), `GeneralSettings__RedisConnectionString=redis:6379,defaultDatabase=1` | volume `localtest-v2-correspondence-blobs` at `/mnt/storage` | postgres, register, profile, authorization, resource-registry, notifications, events, redis, dialogporten-webapi | Plain |

Every non-database component also gets `--add-host <h>:host-gateway` for `local.altinn.cloud`,
`pdf.local.altinn.cloud`, `workflow-engine.local.altinn.cloud` and the app-frontend host
(`WithContainerRuntimeArgs("--add-host", ...)`, compiles), unless the front door is moved onto the container network
(section 3.2), in which case a network alias replaces them.

### 2.1 The needs, feature by feature

| studioctl need | Aspire feature | Fit |
| --- | --- | --- |
| Pinned images from ghcr, `sha-<commit>` tag | `AddContainer` + `WithImageRegistry` + `WithImageTag`; digest with `WithImageSHA256` | Full |
| `--latest`, `STUDIOCTL_LOCALTEST_V2_TAG`, `STUDIOCTL_IMAGE_LOCALTEST_V2_*` | AppHost configuration (`builder.Configuration["Localtest:Tag"]`, environment variables, `appsettings.json`, user secrets) or `AddParameter` | Full, by convention rather than flags |
| Environment per component | `WithEnvironment` (15 overloads, including callbacks and endpoint references) | Full |
| Certificate bind mount | `WithBindMount(source, target, isReadOnly: true)`; the AppHost must write the file first (it is embedded in studioctl; an AppHost package would embed it too) | Full; SELinux relabelling (studioctl's `RelabelBinds`) would need `WithContainerRuntimeArgs` or a `:z` on the source, **not verified** |
| Named volumes, shared between containers | `WithVolume(name, target)` with the same name on several resources | Full |
| Volume ownership (blobs uid 1654, policy store uid 1000, set up "on first use" by the runtime) | Nothing built in; the images would have to create the directories with the right owner, or an init container (`AddContainer` + `WaitForCompletion`) chowns | Gap, small |
| Network and aliases (`postgres`, `redis`) | DCP creates a bridge network on which containers find each other by resource name (`aspire-session-network-<suffix>`, or `aspire-persistent-network-<suffix>` for persistent containers, names read from the 13.6.2 assembly); `WithContainerNetworkAlias(alias)` adds a DNS name | Full; the network's name is Aspire's, not `localtest-v2_network`, and no API to choose it was found |
| `--add-host ...:host-gateway` | `WithContainerRuntimeArgs("--add-host", "local.altinn.cloud:host-gateway")` | Full in the API (passed to the runtime as given, per the API's documentation); not run here, so **not verified** with Podman |
| Dependency order | `WaitFor(resource)` (waits for healthy), `WaitForCompletion` for one-shot jobs | Full, and finer than studioctl's: Aspire waits on the dependency's **health**, where devenv waits for `WaitForReady` |
| Health | `WithHttpHealthCheck(path)` per HTTP component; `AddPostgres`/`AddRedis` bring their own; custom `IHealthCheck` via `WithHealthCheck(key)` | Full for HTTP; no exec probes (studioctl uses two: `pg_isready` and the Dialogporten bash probe), both replaceable from the host side |
| The final "proxy answers" check | `WithHttpHealthCheck("/Home/Localtest/Version")` on the front-door resource; the app `WaitFor`s it | Full |
| Optional groups | No groups (and no official pattern beyond these). Options: (a) AppHost configuration read at build time (`Localtest:Groups=dialogporten,receipt`), resources not added; (b) every resource added, those of a disabled group with `WithExplicitStart()` so the dashboard shows them **stopped with a Start button**; (c) separate AppHost projects or launch profiles per group set | (b) is the closest to studioctl's 503 story and gives a one-click "start the group" that studioctl lacks; the implied-group closure (`correspondence` → `dialogporten`) has to be computed in the AppHost, as `ResolveGroups` does; a started-by-hand group does not cascade its implied groups (**not verified** whether `WaitFor` on an explicitly-started resource starts it) |
| `env down` | Stopping the AppHost stops the session-lifetime containers | Full |
| `env reset` (delete five volumes) | No command. Volumes outlive the AppHost; reset is `podman volume rm`, or a `WithCommand` on the Postgres resource that stops and removes them through the runtime's CLI | Gap, small |
| Keep the stack between app restarts | `WithLifetime(ContainerLifetime.Persistent)` (the docs now prefer `WithPersistentLifetime()`): containers are not removed when the AppHost stops, are reused on the next run, and are recreated when a hash of their configuration (image, environment, mounts, ports) changes ([resource lifetimes](https://aspire.dev/app-host/resource-lifetimes/)) | Full, and important: without it every AppHost restart (every change to the app) recreates 13 to 20 containers. Persistent containers' endpoints stay **proxied** by default, so a fixed host port is unreachable once the AppHost stops unless the endpoint is `isProxied: false`, which this design uses anyway |
| `--dev <component>` local image build | `AddDockerfile(name, contextPath, dockerfilePath)` (one per Dialogporten host: `Dockerfile.webapi` and so on), or `WithDockerfile` on the existing resource | Full in the API; builds go through Podman's builder on this machine, so the one-build-at-a-time rule still applies and Aspire builds every changed `AddDockerfile` at start (**not verified** whether in parallel) |
| `env logs -c <component>` | The dashboard's console log per resource; `aspire logs` | Full while the AppHost runs |
| `env status` | The dashboard's resource list (state, health, endpoints, env); `aspire ps`, `aspire describe` | Full |
| Rootless Podman user handling (`RuntimeUser`, `UsernsMode`) | Nothing; `WithContainerRuntimeArgs("--userns", ...)` if needed | Not needed today: every localtest-v2 component has `useDefaultUser = true` |

## 3. The front door

studioctl-server's proxy does six things the stack depends on: (1) routes by host and longest path prefix, with the
dedicated `pdf.` and `workflow-engine.` hosts and a fall-through to the localtest app; (2) keeps the browser's `Host`;
(3) strips `Secure` from every `Set-Cookie`; (4) rewrites `/dialogporten/graphql` to `/graphql`; (5) answers `503`
problem details for a group that is off or a component that does not answer; (6) adds `/{org}/{app}` for each
discovered app. It is also reachable **from the containers** through the host gateway, because it listens on every
interface.

### 3.1 Candidates

| Candidate | (1) routes | (2) Host | (3) Secure | (4) rewrite | (5) 503 | (6) app routes | Verdict |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **A. `Aspire.Hosting.Yarp`** (`AddYarp`, the container image `mcr.microsoft.com/dotnet/nightly/yarp:2.3-preview`) | Yes: `AddRoute(path, endpoint)`, `WithMatchHosts`, `WithOrder` | Yes: `WithTransformUseOriginalHostHeader()` | **No**: the configuration transforms can add, set or remove a response header (`WithTransformResponseHeader`, `...Remove`), not rewrite one; there is no cookie transform, and the image runs no custom code | Yes: `WithTransformPathRemovePrefix`, `WithTransformPathSet` | **No** (no static responses; a disabled group's route can only be omitted, which falls through to the localtest app's 404) | Only routes known when the AppHost builds the model | Not sufficient on its own. Losing (3) breaks every sign-in: production Authentication sets `Secure` cookies and the browser drops them on `http://` |
| **B. The localtest app's own proxy** (`localtest/localtest/Proxy/`) | Yes, by host and prefix, upstreams from `Proxy__Upstreams__<name>` | Yes | **Yes** | No (no rewrite today) | No | Yes: `PUT localtest/api/proxy/apps/{org}/{app}` with an upstream, persisted on `/data` | The closest fit. It was the front door until studioctl took over (`run-app.sh`, the compose era, 419 Cypress tests on 2026-10-20), and its upstreams are configuration, which is what an AppHost writes. Missing: routes for `resourceregistry`, `notifications`, `dialogporten` (+ the GraphQL rewrite), `storage/dialogporten`, `receipt`, `correspondence`; the 503 problem details; it is Native AOT, so the additions are hand-written C#, not configuration |
| **C. A YARP project in the AppHost's solution** (`AddProject<FrontDoor>()` with `Yarp.ReverseProxy` and studioctl-server's `PlatformTransformer`, `PathRewrite` and `ComponentUnavailable` copied or shared) | Yes | Yes | Yes | Yes | Yes | From the AppHost model, or an API like B's | Full fidelity, but a third copy of the front door (studioctl-server, the localtest app, this) |
| **D. studioctl-server as a container** | Yes | Yes | Yes | Yes | Yes | **No**: discovery reads the host's port listeners and the Unix-socket API is the CLI's control channel; in a container it sees neither | Wrong shape: it is a host daemon by design |
| **E. studioctl-server itself, on the host** (the AppHost does not replace the front door at all) | Yes | Yes | Yes | Yes | Yes | Yes, by process discovery | This is the hybrid of section 5.3: the AppHost runs the app, studioctl runs the platform |

### 3.2 How the containers reach it

With studioctl the containers call `local.altinn.cloud:8000` on the host through `host-gateway`, and studioctl-server
binds `0.0.0.0` for that. With an Aspire host there is a cleaner option for B: give the localtest container the
**network aliases** `local.altinn.cloud`, `pdf.local.altinn.cloud` and `workflow-engine.local.altinn.cloud`
(`WithContainerNetworkAlias`, one call per alias) and publish its container port 8000 on host port 8000. The localtest
image already listens on 8000 (`ASPNETCORE_HTTP_PORTS=8000` in its Dockerfile), so `http://local.altinn.cloud:8000/`
means the same front door from inside the network (by alias) and from the browser (by the published port); no
`--add-host` is needed for the platform, and the proxy's upstreams become container addresses (`Proxy__Upstreams__storage
= http://storage:5010`, which are the defaults the localtest app already ships with). Only the route to the **app on the
host** needs the host gateway: `PUT localtest/api/proxy/apps/ttd/app {"upstream":"http://host.docker.internal:5005"}`,
and the app must listen on `0.0.0.0` (as `run-app.sh` does), because on Linux the gateway cannot reach a loopback-bound
port. Aspire offers an alternative for that one hop: its **container tunnel** (on by default since 13.3) lets a
container reach a project's endpoint through a tunnel container on the network, named `aspire.dev.internal`, when the
container is given the project's endpoint with `WithReference` ([container
networking](https://aspire.dev/fundamentals/container-networking/)). The front door could take the app's upstream from
that reference instead of `host.docker.internal`, and the app could stay on loopback; the route `PUT` would then carry
the tunnel address, which the AppHost knows only at run time. **Not verified**, and an open issue
([aspire#13311](https://github.com/microsoft/aspire/issues/13311)) reports wrong container-side URLs for endpoints bound
to `0.0.0.0`.

### 3.3 What is lost without studioctl-server

- **Provisioning on discovery** (section 4) and **re-provisioning when the app comes back on another port**.
- **Process discovery**: an app started outside the AppHost (a second app from another checkout, an IDE run) is not
  found; it must register its own route (B's `PUT`), as `run-app.sh` did.
- **The route table as a file** that `studioctl env status`, the provisioner (Dialogporten sync only when the group is
  up) and the 503 page share. In an AppHost the model itself is that table, so it is replaced, not lost.
- **The Unix-socket control API** (`studioctl run` registering, `apps`, `ps`, `stop`): the dashboard and `aspire` CLI
  cover start, stop and logs for resources of *this* AppHost only.
- **`--dev-frontend` routing**: studioctl routes the frontend dev server's host; in an AppHost the dev server would be
  another resource (`AddExecutable` or a JavaScript hosting integration) and the app's
  `AppSettings__AppFrontendAssetBaseUrl` would point at its endpoint.
- **The task 13 gaps are inverted**: the localtest app redirects a prefix without its trailing slash and can serve
  `/app-frontend/`, both of which studioctl-server cannot today.

## 4. Provisioning

The provisioner's steps for one app (`AppProvisioner.Provision`): fetch metadata, languages, texts, policy and
`testData.json` **from the running app** over HTTP; get the Studio token (`GET /Home/GetTestStudioToken`); then
1 `PUT register/api/localtest/apps/{org}/{app}`; 2 Storage application metadata and texts (create or replace); 3 the
policy to the PDP with Designer's placeholder replacement; 3b the Resource Registry subject reload; 4 `POST
accessmanagement/api/localtest/refresh`; 5 `POST dialogporten/api/v1/localtest/sync` when the group is up. On app stop:
the Register `DELETE` and another refresh. All of it is plain HTTP against the public origin; nothing needs studioctl.

| Trigger in an Aspire host | How | For | Against |
| --- | --- | --- | --- |
| **A hook in the AppHost** | `builder.Eventing.Subscribe<ResourceReadyEvent>(app.Resource, ...)` or `app.OnResourceReady(...)` (both compile), running the five steps; plus `WithCommand("provision", ...)` for a "Provision again" button in the dashboard (compiles; `CommandResults.Success()`; also runnable as `aspire resource app provision`) | Runs exactly when the app is healthy, and **holds back everything that `WaitFor`s the app until the handler returns** (`ResourceReadyEvent` handlers are awaited before dependents start, per [ResourceNotificationService](https://source.dot.net/Aspire.Hosting/ApplicationModel/ResourceNotificationService.cs.html)), so a test resource can wait for a provisioned app; the dashboard shows the command; no new container | The C# lives in the AppHost (or a hosting package), a fourth home for the logic beside studioctl-server, `run-app.sh` and the options doc's 4b; app stop (`DELETE`) has no clean event when the AppHost itself stops |
| A one-shot container | `AddContainer("provision", ...)` with `WaitFor(app)` and `WaitForCompletion` for nothing | Same image for every runner | Needs an image for ~200 lines of HTTP calls; reaches the app through the host gateway |
| The localtest app on route registration | The options doc's **4b**: when an app route is `PUT`, the localtest app fetches and pushes | One implementation for every runner (AppHost, `run-app.sh`, anything else), ships with the stack, follows the components' API | studioctl-server already does it; moving it means changing studioctl too, or two copies |
| app-lib at startup | The app pushes its own artefacts when it detects localtest (`RuntimeEnvironment.IsLocaltestPlatform`) | No runner needed at all | Production code doing a deploy step, with Designer's token, in every app's startup; the app is the wrong layer (it never deploys itself in production). Not recommended |

The least duplication is to **extract `AppProvisioner` into a small .NET library** (it depends only on
`IHttpClientFactory`, a logger and "is Dialogporten up") used by studioctl-server and by an Aspire hosting package.
Without that, the AppHost hook is a copy.

## 5. The app side

### 5.1 Adding the app

`builder.AddProject<Projects.App>("app")` needs a `ProjectReference` from the AppHost to `App/App.csproj` (compiles
with a stand-in `net10.0` web project; the template's project is `Microsoft.NET.Sdk.Web`, `net11.0` for v9, assembly
`Altinn.Application`; a C# AppHost needs the .NET 10 SDK, and whether a `net10.0` AppHost references a `net11.0` app
project cleanly was **not verified**, an AppHost on `net11.0` would sidestep it). Aspire builds the project and runs it
as a DCP executable with its launch profile, injecting `ASPNETCORE_URLS` (rewritten to a random internal port when the
endpoint is proxied), `DOTNET_LAUNCH_PROFILE`, `OTEL_EXPORTER_OTLP_ENDPOINT` and `services__*`
([project resources](https://aspire.dev/integrations/dotnet/project-resources/)). The template carries two URL sources:
`Properties/launchSettings.json` sets `applicationUrl: http://local.altinn.cloud:8000` (the front door's own address)
and `appsettings.json` sets `Kestrel:Endpoints:Http:Url: http://*:5005`. Aspire's documentation says an AppHost throws
when both a launch-profile `applicationUrl` and Kestrel endpoint configuration are active, so the template app as it
is would likely not start under `AddProject` without
`AddProject<Projects.App>("app", o => { o.ExcludeLaunchProfile = true; o.ExcludeKestrelEndpoints = true; })` (compiles)
or a template change. **Not verified by running.** The environment it needs is exactly what `studioctl run` sets (`internal/cmd/app/env.go`,
`addRunDefaults`):

- `ASPNETCORE_ENVIRONMENT=Development`, a Kestrel URL (with B as the front door: `http://0.0.0.0:5005`,
  `WithHttpEndpoint(port: 5005, isProxied: false)`).
- `GeneralSettings__HostName=local.altinn.cloud`, `GeneralSettings__ExternalAppBaseUrl`,
  `AppSettings__OpenIdWellKnownEndpoint`, and the eleven `PlatformSettings__Api*Endpoint` values (Storage, Register,
  Profile, Authentication, Authorization, Events, Notifications, Correspondence, Access Management on the public
  origin; Pdf2 and the workflow engine on their hosts).
- The certificate: `AccessTokenSettings__AccessTokenSigningCertificateFullPath`, `...KeysFolder` (a relative
  traversal from the app root, for the 1.x client), `...FileName`.
- `RUNTIME_APP_SECRETS_DIR`, `RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME`, `RUNTIME_APP_SECRETS_APPCODES_FILENAME`, with
  a directory holding the development callback codes (`appsecrets.WriteDevelopmentAppCodes`) and, when stored, the
  Maskinporten client. The AppHost would have to create these, or ask studioctl.
- **`STUDIOCTL_APP_RUN=1`**, or app-lib does something unexpected: in Development, when that variable is unset,
  app-lib v9 runs `studioctl app env --json --project ...` at startup and adds the result as configuration *after* the
  environment sources (`StudioctlLocalConfiguration`, called from `WebHostBuilderExtensions`), so a developer with
  studioctl installed would get studioctl's values over the AppHost's. Without studioctl on the machine it is a
  silent no-op.

`LocaltestValidation` (Development only) calls `{platform}/Home/Localtest/Version` and stops the app below version 6
or on 404; the localtest app answers it, so nothing changes. OpenTelemetry in app-lib is opt-in
(`AppSettings__UseOpenTelemetry=true`); with it set the app's traces, metrics and logs reach the dashboard through the
`OTEL_EXPORTER_OTLP_ENDPOINT` Aspire injects. studioctl sets that variable but not the switch.

### 5.2 `studioctl run`'s other duties

| Duty | In an AppHost |
| --- | --- |
| Build, then run, wait for readiness | Aspire's project resource |
| Register with studioctl-server, route `/{org}/{app}` | The route `PUT` to the localtest app (front door B), from the same hook as provisioning |
| Provisioning | Section 4 |
| Secrets directory, development app codes, `studioctl app maskinporten set` | Not available; the AppHost writes the codes file, Maskinporten clients stay a studioctl feature |
| `--dev-frontend` (Vite dev server, `AppFrontendAssetBaseUrl`) | A frontend resource and one variable; the dev server needs `Access-Control-Allow-Origin` as today |
| Container mode (`-m container`), `--random-host-port`, `--image-tag` | Not covered; an AppHost runs the project natively |
| Test data | Provisioning step 1 reads `testData.json` from the running app; nothing extra |

### 5.3 The hybrid that works without new code

An AppHost that contains **only the app** (and whatever the developer's own solution adds: a database, a mock of an
external API) already runs against `studioctl env up -r localtest-v2`, by the same two mechanisms that make an IDE run
work: app-lib imports `studioctl app env --json` (because the AppHost does not set `STUDIOCTL_APP_RUN`), and
studioctl-server finds the process by name (`Altinn.Application` matches its `IsStudioAppName`) and provisions and
routes it. Reading the code, nothing in this path depends on who launched `dotnet`. **Not verified by running.** One
open point: Aspire puts a proxy in front of a project's endpoints by default; studioctl-server discovers the app's own
listener (the process's port), so the app should keep its Kestrel URL from studioctl's environment (`isProxied: false`
or no Aspire endpoint at all), and the launch-profile and Kestrel clash of section 5.1 applies here too.

## 6. Fit

**Who would use it.** App developers who already use Aspire for their own app's dependencies (service owners with
custom backends, a database or an external API mocked in the same solution), in Rider or Visual Studio, where the
AppHost is the start project and debugging the app is F5. That is a minority of Altinn app developers today: neither
the app template nor app-lib nor the monorepo references Aspire anywhere (no `Aspire.Hosting`, AppHost or
ServiceDefaults project in `altinn-studio` or `app-lib-dotnet`). Studio's guidance, the docs and the bundled agent
skill all say `studioctl`. CI: `Aspire.Hosting.Testing` (`DistributedApplicationTestingBuilder`) can start an AppHost
from an xUnit test and wait for resources, which is a plausible way to run app integration tests against the stack;
the monorepo's own CI would use studioctl (task 13), so this would only be for service owners' pipelines.

**What it duplicates from studioctl.** The component table (20 components, their env, mounts, deps, groups), the
image pins, the certificate, the provisioner, the app environment, the front door's routes and the group closure. Only
the container executor, health polling and log streaming are genuinely replaced by Aspire's. Everything a component
task changes on "the studioctl side" (see `TODO.md`'s rules paragraph: container spec, image pin, proxy prefix,
provisioning calls, health) would have to change in two places.

**Sharing the definitions.** Two directions:

- **Go is the source** (recommended if anything is built): `components.go` already holds every fact in one struct
  (`component`: image, env, mounts, ports, deps, group, health path, routes, rewrite). A `studioctl env manifest -r
  localtest-v2 --json` (or a generated JSON file committed next to it, checked in CI) emits it; an Aspire hosting
  package reads that file and creates the resources and the front door's upstreams. The pin moves in one place.
- Aspire as the source: Aspire's publishers could produce a compose file from an AppHost
  (`AddDockerComposeEnvironment` from `Aspire.Hosting.Docker` 13.6.2, then `aspire publish` writes
  `aspire-output/docker-compose.yaml`; the `publish` command is still marked preview, and whether runtime arguments and
  aliases survive into the file is not documented), but studioctl does not run compose (options doc, 1c: it talks to the engine API), so
  this direction would make studioctl consume a format it deliberately dropped. Not recommended.

**Memory and start time.** The stack is the same containers either way (4.4 GB used of 7.8 GB with every group up,
`localtest/README.md`). Aspire adds the DCP orchestrator, the dashboard and, per proxied endpoint, DCP's proxies; their
size was **not measured** (the orchestration and dashboard packages are 172 MB and 126 MB on disk for linux-arm64).
Start time is dominated by the components' own start; the difference is the restart loop: with session lifetime every
AppHost restart recreates the stack (Postgres migrations, Access Management's import, Dialogporten's migrations), so
persistent lifetime is mandatory for the developer loop, and then the AppHost's restart cost is the app's own build.
studioctl separates the two by design (`env up` once, `run` many times).

**Podman.** Podman is a "supported alternative" to Docker ([prerequisites](https://aspire.dev/get-started/prerequisites/)),
selected with `ASPIRE_CONTAINER_RUNTIME=podman` (the older `DOTNET_ASPIRE_CONTAINER_RUNTIME` is still read); the
13.6.2 assembly also carries runtime detection and Podman builds for `AddDockerfile`. Known problems: bind mounts under
SELinux ([aspire#3945](https://github.com/microsoft/aspire/issues/3945), open; studioctl relabels binds for exactly
that reason); earlier container-to-host networking issues on Podman, including YARP to projects, are closed
([aspire#11449](https://github.com/microsoft/aspire/issues/11449), [aspire#6724](https://github.com/microsoft/aspire/issues/6724)).
What was not verified: rootless Podman with `host-gateway`, the network aliases, and persistent containers across
AppHost runs on this machine. studioctl's devenv already has Podman-specific handling (`RelabelBinds`, user namespace)
that the AppHost would have to rediscover.

**The dashboard (task 8 overlap).** The dashboard gives, for free: resource list with state and health, console logs per
container, environment variables, endpoints, start/stop/restart, custom commands (a "Provision again", a "Reset
Events", a "Clear Dialogporten caches" button are each a `WithCommand` calling an existing `api/localtest/`
endpoint), and traces, metrics and structured logs for whatever exports OTLP: the app with `UseOpenTelemetry`, the
workflow engine with telemetry on, and possibly the components (several vendored hosts call `AddOpenTelemetry`;
whether their OTLP exporters follow `OTEL_EXPORTER_OTLP_ENDPOINT` locally was not checked). It overlaps with task 8 in
the *operational* view (what runs, logs, health), not in the *domain* view task 8 plans (test users, events and
deliveries, notification inboxes, dialogs, instances). The standalone dashboard (`mcr.microsoft.com/aspire/dashboard`, UI 18888, OTLP 18889/18890) could also sit beside
`studioctl env up` for traces, metrics and structured logs without any AppHost; it has no resource list, console logs
or commands ([standalone dashboard](https://aspire.dev/dashboard/standalone/)). That would be the cheapest way to get
Aspire's trace view for the stack and is worth a separate look.

## 7. Recommendation, effort, risks

### 7.1 Recommendation

**Do not build a second runtime now. Document the hybrid, and keep the door open for a thin generated layer.**

1. **Now (0.5 to 1 day):** verify and document the hybrid of section 5.3: an AppHost with the app project (and the
   developer's own resources) next to `studioctl env up -r localtest-v2`. It needs no new code if the run confirms
   the reading: app-lib imports studioctl's environment, studioctl-server discovers, provisions and routes the app.
   This serves the Aspire user with nothing to maintain, and it is what the platform team can support.
2. **Only if the product owner wants Aspire as a first-class way in (6 to 9 days):** a hosting package,
   `builder.AddAltinnLocaltest(o => o.WithGroups("dialogporten"))` plus `.WithAltinnLocaltest()` on the app project,
   generated from a JSON manifest that studioctl emits from `components.go`, with the localtest app as the front door
   (front door B, extended) and the provisioner extracted into a shared library. Not a hand-written parallel
   definition.
3. **Not:** a hand-maintained Aspire AppHost beside studioctl. Every component task would have to change it too, and
   it would drift the first time a pin moves.

Why not more: the stack's hard parts are not container orchestration (which Aspire does well) but the front door
(Secure cookies, host routing, rewrite, 503), provisioning, the certificate and the app environment, all of which
studioctl owns and Aspire's integrations do not provide. Aspire's YARP integration cannot strip `Secure`, so the
smallest working Aspire host already needs custom front-door code. And no one in the Altinn app ecosystem uses Aspire
today, so the audience is speculative.

### 7.2 Effort (option 2, if chosen)

| Piece | Days |
| --- | --- |
| `studioctl env manifest --json` from `components.go` (images with pins, env, mounts, ports, deps, groups, health, routes) and a CI check that the committed JSON matches | 1 |
| The localtest app's proxy brought level with studioctl-server: the six missing prefixes, the GraphQL rewrite, longest-prefix ordering, the 503 problem details for groups the configuration marks off; its smoke test extended | 1.5 to 2 |
| `AppProvisioner` extracted into a library shared by studioctl-server and the package | 1 |
| The hosting package: resources from the manifest, groups with `WithExplicitStart`, persistent lifetime, aliases, certificate file, app extension (environment, secrets directory, route `PUT`, provisioning hook, dashboard commands) | 2 to 3 |
| A sample AppHost against `ttd/frontend-test` or the smoke fixture, smoke test and platform regression through it, docs | 1 to 1.5 |
| Total | 6.5 to 8.5, round to 6 to 9 |

### 7.3 Risks

- **Two front doors to keep equal** (studioctl-server and the localtest app), unless task 13 moves studioctl to the
  localtest app as front door, which reverses a decision already built.
- **Podman specifics**: `host-gateway`, aliases, persistent containers and SELinux relabelling under DCP are unverified
  on this machine; studioctl's devenv solved them once already.
- **Aspire's pace**: 13.x moves quickly (the YARP integration's image is a `nightly` tag, `2.3-preview`; the
  Kubernetes publisher is preview). A hosting package pinned to one Aspire version needs upkeep each release.
- **app-lib's studioctl import** silently overrides an AppHost's environment for a developer who has studioctl
  installed unless `STUDIOCTL_APP_RUN` is set; easy to get wrong, hard to diagnose.
- **Restart cost** without persistent lifetime (every app change restarts the stack).
- **Support surface**: a second way to run the platform doubles "works on studioctl, not on Aspire" reports.
- **The template app may not start under `AddProject` unchanged** (launch-profile `applicationUrl` plus Kestrel
  endpoints in `appsettings.json`, section 5.1); every Aspire user meets this first, hybrid or not.

### 7.4 Open questions for the product owner

1. Is Aspire a way in that Altinn Studio wants to offer app developers at all (docs, template, support), or is
   studioctl the one supported runtime? Everything beyond the hybrid depends on this.
2. Should the hybrid (an AppHost running the app next to `studioctl env up`) be documented in altinn-studio-docs, with a
   sample AppHost, once a run confirms it?
3. Should the app template change so an AppHost can add it without options (one URL source instead of a launch-profile
   `applicationUrl` plus Kestrel endpoints)?
4. If a hosting package is wanted: is the localtest app the front door outside studioctl (extend its proxy to
   studioctl-server's route table and 503s), or does the package carry its own YARP project?
5. Should `AppProvisioner` move out of studioctl-server into a library (or into the localtest app, the options doc's
   4b), so that every runner provisions the same way?
6. Should studioctl emit its component table as JSON (`studioctl env manifest`), as the single source for any other
   runner, Aspire or CI?
7. Is the standalone Aspire dashboard beside `studioctl env up` worth trying for traces, and should the components'
   OTLP export be switched on locally for it (an input to task 8)?
8. Is running app integration tests through `Aspire.Hosting.Testing` against the stack something service owners have
   asked for?

### 7.5 What a generated AppHost would look like

The shape the scratch AppHost compiled, reduced to the developer's view, if option 2 were built (package and method
names are proposals):

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltest()          // 13 default components from studioctl's manifest,
    .WithGroups("dialogporten");                      // persistent; other groups added with WithExplicitStart

builder.AddProject<Projects.App>("app", o => { o.ExcludeLaunchProfile = true; o.ExcludeKestrelEndpoints = true; })
    .WithAltinnLocaltest(localtest);                  // studioctl's app environment, STUDIOCTL_APP_RUN=1,
                                                      // route PUT, provisioning on ResourceReadyEvent,
                                                      // "Provision again" command
builder.Build().Run();
```

Inside, each component is the `AddContainer(...).WithImageRegistry("ghcr.io").WithHttpEndpoint(port, targetPort,
isProxied: false).WithHttpHealthCheck(path).WithBindMount(certDir, "/certs", true)...WaitFor(...)` chain of section 2,
and the front door is the localtest container with the ingress host names as network aliases (section 3.2).

## 8. Not verified

- Nothing was run. No AppHost, DCP, dashboard or container was started, no image pulled, no Podman call; the hybrid
  of section 5.3 is read from the code, not observed.
- The scratch AppHost compiled against Aspire 13.6.2 (APIs used: `AddContainer`, `WithImageRegistry`,
  `WithImageSHA256`, `WithImageTag`, `WithArgs`, `WithVolume`, `WithBindMount`, `WithContainerNetworkAlias`,
  `WithEndpoint`/`WithHttpEndpoint` with `isProxied: false`, `WithHttpHealthCheck`, `WithContainerRuntimeArgs`,
  `WithLifetime(ContainerLifetime.Persistent)`, `WithExplicitStart`, `WaitFor`, `AddPostgres` with a swapped image and
  `WithDataVolume`, `AddRedis`, `AddParameter`, `AddDockerfile` with a named Dockerfile, `AddYarp`, `WithHostPort`,
  `WithConfiguration`, `AddRoute`, `WithTransformUseOriginalHostHeader`, `WithTransformPathRemovePrefix`,
  `WithMatchHosts`, `AddProject`, `Eventing.Subscribe<ResourceReadyEvent>`, `OnResourceReady`, `WithCommand`, `ProjectResourceOptions.ExcludeLaunchProfile`/`ExcludeKestrelEndpoints`). That
  they compile says nothing about runtime behaviour: whether DCP passes `--add-host` to Podman, how aliases resolve,
  whether `WaitFor` starts an explicit-start dependency, what a persistent container does when its spec changes.
- The YARP integration's lack of a cookie transform is read from its public API (13.6.2 XML documentation); a
  configuration-file route (`IYarpJsonConfigGeneratorBuilder.WithConfigFile`) uses the same YARP configuration
  transforms, which have no cookie rewrite either, but no YARP image extension point was looked for beyond that.
- DCP and dashboard memory, and start-time differences, were not measured.
- Whether the vendored components export OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set was not checked.
- Whether a missing `RUNTIME_APP_SECRETS_*` stops an app at startup or only logs was not checked.
- Aspire behaviour is quoted from aspire.dev, source.dot.net and GitHub issues as read on 2026-10-21, not observed.
  The network names come from strings in the 13.6.2 assembly. DCP's memory and start overhead have no published
  figures.
