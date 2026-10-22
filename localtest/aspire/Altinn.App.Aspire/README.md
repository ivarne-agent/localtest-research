# Altinn.App.Aspire

A .NET Aspire hosting package that runs the localtest platform (the localtest-v2 component stack) as container
resources of an AppHost, behind a YARP front door on `http://local.altinn.cloud:8000`. It reads the same component
manifest studioctl runs (`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, embedded by
relative path, one copy), so the images, environment, ports, volumes, certificate, dependencies, groups and routes are
studioctl's. Task 21 of [`TODO.md`](../../../TODO.md); this is 21d, the first slice: the platform and its front door,
no app yet.

Status (2026-10-22): **runs under Podman**. The sample (default stack plus `dialogporten`) passes the root (26),
authentication (34) and Dialogporten (60) smoke tests through its front door, and survives AppHost restarts (see
[Run under Podman](#run-under-podman)).

| Path | What |
| --- | --- |
| `src/` | The package (`Altinn.App.Aspire`, net10.0, `0.1.0-alpha.1`, not published yet). It also compiles the shared provisioning library's sources ([`Altinn.Studio.Localtest.Provisioning`](../../studioctl/src/Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning/README.md), unused until 21e), so the NuGet is one assembly |
| `src/Manifest/` | The C# manifest reader: the model (System.Text.Json, source-generated, unknown keys refused), the placeholders, group closure, images and routes |
| `tests/` | xunit v3: the reader against studioctl's golden file, the schema, the application model the sample builds, the front door's routes through an in-process YARP |
| `samples/AppHost/` | A single-file AppHost (`apphost.cs`) that adds the default stack and the `dialogporten` group |

The build is standalone like the provisioning library's (its `Directory.Build.props`, `.editorconfig`, csharpier
configuration and `global.json`, SDK 10.0.401): `dotnet build src`, `dotnet test tests`, `dotnet csharpier check .`.
The sample builds with `dotnet build samples/AppHost/apphost.cs`; its `Directory.Build.targets` marks the package
reference as a library (`IsAspireProjectResource=false`), which the `#:project` directive cannot say.

## Use

```csharp
#:sdk Aspire.AppHost.Sdk@13.6.2
#:package Altinn.App.Aspire@0.1.0-alpha.1

var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltest().WithGroup("dialogporten");
// 21e: builder.AddAltinnApp<Projects.App>(localtest);

await builder.Build().RunAsync();
```

```sh
cd samples/AppHost
ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost.cs   # or aspire run
```

Stop `studioctl env down -r localtest-v2` first: both listen on port 8000 and on the components' host ports.

| API | What |
| --- | --- |
| `AddAltinnLocaltest(name = "localtest", configure = null)` | Adds the always-on components and the front door, returns the parent resource. It is `Running` once every component and the front door is healthy, so an app can `WaitFor` it |
| `.WithGroup(group)` | Adds an optional group's components and those of the groups it implies (`correspondence` and `arbeidsflate` bring `dialogporten`, `everything` brings all) |
| `AltinnLocaltestOptions.Tag` | The research images' tag. Default: `STUDIOCTL_LOCALTEST_V2_TAG`, otherwise the manifest's pin; `latest` is pulled on every start. A per-image `STUDIOCTL_IMAGE_LOCALTEST_V2_<NAME>` (and `STUDIOCTL_IMAGE_PDF3`, `STUDIOCTL_IMAGE_WORKFLOW_ENGINE`) still wins, as with studioctl |
| `AltinnLocaltestOptions.DataDirectory` | Default `~/.altinn-app-aspire`; the shared test certificate is written to `certs/` there (world-readable, as studioctl does) when the AppHost starts |
| `AltinnLocaltestOptions.Groups` | The same as `WithGroup` for each |
| `AltinnLocaltestResource.Groups`, `.PlatformUri`, `.CertificateDirectory` | For 21e and for an AppHost's own resources |

The groups: `dialogporten` (Redis, the WebApi, GraphQL and Service hosts, the adapter), `receipt`, `correspondence`,
`arbeidsflate`, `everything`; see [`localtest/README.md`](../../README.md#run-it).

Resources: the parent `localtest`, the front door `frontdoor`, and one container per component named after the
manifest component, children of the parent in the dashboard. The localtest app's container is `localtest-app`, because
Aspire resource names are unique and the parent is `localtest`; it keeps `localtest` as a network alias. Containers are
**persistent** (`ContainerLifetime.Persistent`): they outlive the AppHost and are reused by the next run unless their
configuration changed. The front door has the session's lifetime (21e adds the app's route, which lives with the
session).

## How the manifest becomes resources

| Manifest | Aspire | Deviation from studioctl |
| --- | --- | --- |
| component `name` | `AddContainer(name, image, tag)`; the container is reachable on Aspire's network by that name | Aspire's network and container names (`localtest-v2_network`, `localtest-v2-<name>` are studioctl's); `localtest` is `localtest-app` |
| `image.research` | `repositoryPrefix + name : tag`, split into `WithImageRegistry("ghcr.io")` and the image | none |
| `image.thirdParty` (Redis) | registry, image, `WithImageSHA256(digest)` | Aspire drops the tag when a digest is set: `library/redis@sha256:...`, the same image |
| `image.core` (pdf3, workflow engine), floating tags | `WithImagePullPolicy(Always)` | studioctl keeps the local copy when the registry is unreachable; `Always` fails instead |
| `environment` | `WithEnvironment(key, value)` per variable, placeholders resolved as the Go loader does | none |
| `command` | `WithArgs(...)` (the image's CMD) | none |
| `containerPort`, `hostPort` | `WithHttpEndpoint(port: hostPort, targetPort: containerPort, name: "http", isProxied: false)` | none: DCP publishes on `127.0.0.1` when no host IP is set, as studioctl does |
| no `hostPort` (Postgres, Redis) | an unproxied TCP endpoint on a fixed loopback port, `PostgresHostPort` (15432) and `RedisHostPort` (16379), for the host-side probe | studioctl publishes nothing. A proxied endpoint (random port) gave the persistent container a new port on every run, which recreated it on every AppHost start |
| `healthPath` | `WithHttpHealthCheck(path)` | Aspire polls from the host; `WaitFor` waits for healthy |
| `containerHealthCheck` | Postgres: an `SSLRequest` handshake; Redis: `PING`, both host-side through the TCP endpoint (`ProtocolHealthCheck`). Dialogporten WebApi: its HTTP `healthPath`, which `WaitFor` uses to keep the other hosts' migrations apart | Aspire has no exec health checks; a new exec-only component fails `AddAltinnLocaltest` until it gets a probe |
| `mounts` `certificateDir` | `WithBindMount(<data>/certs, /certs, isReadOnly: true)` | no SELinux relabel: `:z` is not an Aspire option, so on an SELinux host (Fedora with Podman) the bind may be unreadable ([aspire#3945](https://github.com/microsoft/aspire/issues/3945)); this machine is not SELinux |
| `mounts` `certificateFile` | `WithBindMount(<data>/certs/localtest-accesstoken.pfx, target, isReadOnly: true)` | as above |
| `mounts` `volume` | `WithVolume(name, target)` with the manifest's volume names | none, on purpose: the volumes are **shared with studioctl** (same images, same data layout), so `studioctl env reset -r localtest-v2` also resets the Aspire stack's data |
| `networkAliases` | `WithContainerNetworkAlias(alias)` (skipped when it equals the resource name, as for `postgres` and `redis`) | none |
| `ingressHosts` (default true), `extraHostGatewayNames` | `WithContainerRuntimeArgs("--add-host", "<host>:host-gateway")` for `local.altinn.cloud`, `pdf.`, `workflow-engine.`, `pgadmin.`, `app-frontend.`, `af.local.altinn.cloud` and each extra name; such a component also `WaitForStart(frontdoor)` | none (works under Podman 5.7) |
| `dependsOn` | `WaitFor(dependency)` (healthy) | studioctl waits for the runtime's ready state; Aspire for the health check |
| `hostUser` | refused (`NotSupportedException`) | no component uses it today (studioctl's `RuntimeUser`/`UsernsMode` apply only to such components) |
| `group` | added by `WithGroup` with the group closure; components of groups that are off are **not added** | the 503 problem details for a group that is off are deferred: its prefix falls through to the localtest app's 404, and its own host (`af.local.altinn.cloud`) gets YARP's 404 |
| `routes` | YARP routes, below | the localhost fall-back below; no cookie rewriting |
| `frontDoor.healthPath` | `WithHttpHealthCheck` on the front door | none |
| `frontDoor.hostRoutes` (the Vite dev server on host port 8080) | **out of scope** | `--dev-frontend` has no route |
| `postgres.initScripts` | nothing: they are baked into the Postgres image | none |
| `dev` builds | nothing | no `--dev`; use `STUDIOCTL_IMAGE_LOCALTEST_V2_<NAME>` with a locally built image |
| `env reset`, `env logs`, `env status` | none; the dashboard shows logs and state; `podman volume rm` resets | no reset command yet (21e) |

## The front door

`AddYarp("frontdoor").WithHostPort(8000)`, with the `http` endpoint unproxied and its target host `0.0.0.0`, so DCP
publishes the container's port on every interface: the components reach it as `local.altinn.cloud:8000` through the
host gateway, which a loopback-only port would not answer. One YARP cluster per routed component (its `http`
endpoint), and per manifest route:

- path `<prefix>/{**catch-all}` (`/{**catch-all}` for a whole host), which also matches the bare prefix (`/storage`)
  and stops at segment boundaries (`/storagex` falls through), as studioctl-server's `StartsWithSegments` does;
- `WithMatchHosts(host)`, and for routes on `local.altinn.cloud` also `localhost` and `127.0.0.1` (studioctl-server
  matches any other host against the public origin's host; the front door's own health check uses it);
- `WithOrder(-prefix.Length)`, `0` for a whole host, so the longest prefix wins and the localtest app's empty prefix is
  the fall-through;
- `WithTransformUseOriginalHostHeader()`;
- with a `rewrite`: `WithTransformPathRemovePrefix(prefix)` then `WithTransformPathPrefix(rewrite)`, so
  `/dialogporten/graphql/stream` reaches the GraphQL host as `/graphql/stream`.

The sample's routes (every one keeps the original Host):

| Host | Path | Order | Component | Transform |
| --- | --- | --- | --- | --- |
| `local.altinn.cloud`, `localhost`, `127.0.0.1` | `/dialogporten/graphql/{**catch-all}` | -21 | `dialogporten-graphql` | `/dialogporten/graphql` → `/graphql` |
| same | `/storage/dialogporten/{**catch-all}` | -21 | `dialogporten-adapter` | |
| same | `/resourceregistry/{**catch-all}` | -17 | `resource-registry` | |
| same | `/accessmanagement/{**catch-all}` | -17 | `access-management` | |
| same | `/authentication/{**catch-all}` | -15 | `authentication` | |
| same | `/authorization/{**catch-all}` | -14 | `authorization` | |
| same | `/notifications/{**catch-all}` | -14 | `notifications` | |
| same | `/dialogporten/{**catch-all}` | -13 | `dialogporten-webapi` | |
| same | `/register/{**catch-all}` | -9 | `register` | |
| same | `/profile/{**catch-all}` | -8 | `profile` | |
| same | `/storage/{**catch-all}` | -8 | `storage` | |
| same | `/events/{**catch-all}` | -7 | `events` | |
| same | `/{**catch-all}` | 0 | `localtest-app` | |
| `pdf.local.altinn.cloud` | `/{**catch-all}` | 0 | `pdf3` | |
| `workflow-engine.local.altinn.cloud` | `/{**catch-all}` | 0 | `workflow-engine` | |

**Known gap, accepted (TODO task 21, 21d decisions):** the stock YARP image has no cookie transform, so it cannot strip
`Secure` from a `Set-Cookie`. The components no longer set `Secure` cookies over http (21b), but app-lib's `keepAlive`
cookie does: an app behind this front door (21e) loses the ten-minute token refresh over http until app-lib takes
`Secure` from the request (task 22).

## Tests

`dotnet test tests -c Release` (51 tests):

- **Golden comparison** (`GoldenComparisonTests`): the resolved components against studioctl's snapshot
  `internal/cmd/env/localtestv2/testdata/environment.golden.json` (read by relative path), for the default stack and
  for `everything`: component names and start order, image references (default; the `everything` snapshot is
  `--dev all`, so its built images are skipped), environment, host and container ports, mounts (type, source, target,
  read-only), network aliases, extra hosts, dependencies, command, the container health check's resolved test, the
  health targets (component, port and path, plus the front door's), the routes (host, prefix, rewrite; enabled ones
  added, the disabled ones those of the groups that are off), the group implications and the volume names. Excluded,
  being studioctl's own: dev builds, SELinux relabelling, the runtime user and user namespace (asserted empty: no
  component uses `hostUser`), the network resource, pull policies, the state file and the front door's host route.
- **Schema** (`ManifestReaderTests`): the embedded schema's `$id` and `version` const are the ones the reader knows,
  every key the schema defines is a property of the reader's model, and an unknown key or another version is refused.
- **Resolver** (`ResolverTests`): placeholders, bad placeholders, group closure, the tag and override variables, the
  digest-pinned Redis.
- **Model** (`ModelTests`): the sample's application model, built without running it: resource names (no `receipt`,
  `correspondence` or `arbeidsflate`), persistence and parent, endpoints and health checks, images, the `--add-host`
  arguments, environment, mounts, aliases, commands, waits, the front door's port and the YARP routes and transforms
  (read from the resource by reflection: the integration has no public read API).
- **Routing** (`FrontDoorRoutingTests`): those YARP routes loaded into an in-process YARP (`Yarp.ReverseProxy` 2.3.0;
  the image is `2.3-preview`) in front of an echo server: longest prefix, the bare prefix, segment boundaries, case,
  the rewrite with the rest of the path and the query, the fall-through for `/receipt` and `/correspondence`, the
  `localhost` and `127.0.0.1` fall-back, the `pdf.` and `workflow-engine.` hosts, the original Host, and YARP's 404 for
  `af.local.altinn.cloud`.

## Run under Podman

Phase 2 of 21d (2026-10-22), on this machine: Podman 5.7.0 driven through its socket (rootful, netavark), arm64,
`dotnet run apphost.cs` with SDK 10.0.401 (no `aspire` CLI), DCP and the dashboard from the 13.6.2 linux-arm64 NuGet
packages.

- **`ASPIRE_ENABLE_CONTAINER_TUNNEL=false` is needed here.** With the tunnel on (Aspire's default since 13.3) DCP
  builds its tunnel image and Podman refuses the generated Dockerfile (`parsing main Dockerfile: ... bufio.Scanner:
  token too long`); the front door then never starts, because it exports telemetry to the dashboard on the host, which
  goes through the tunnel. Without the tunnel the front door's telemetry has no route to the dashboard (no traces from
  it); nothing else changes. 21e planned the app route through that tunnel; its fallback (`host.docker.internal` with
  the app on `0.0.0.0`) is the one that applies on Podman 5.7.
- **Port 8000 on every interface works**: the front door's port is `0.0.0.0:8000->5000`; the components' fixed ports
  are on `127.0.0.1`. **`--add-host ...:host-gateway` works**: a component's `/etc/hosts` maps the ingress hosts to the
  network's gateway (`10.89.2.1`), and `http://local.altinn.cloud:8000/Home/Localtest/Version` answered from inside
  Storage's network namespace. The component images have no shell or curl, so the request ran in a throwaway container
  sharing that namespace: `podman run --rm --network container:<storage container> --entrypoint sh
  docker.io/library/redis:7.2-alpine -c 'wget -qO- http://local.altinn.cloud:8000/Home/Localtest/Version'`. The
  network-alias fallback was not needed.
- **The YARP container reaches the components** by their resource names (the smoke tests pass through it).
- **Container names** are `<resource>-<suffix>`, the suffix a hash of the AppHost's path (`storage-0de70ea0` for the
  sample), so two AppHosts give two sets of containers on the same host ports: run one at a time, and remove the other's
  containers (`podman rm -f`) first. Set `STORAGE_CONTAINER` for the root smoke test's log check.
- **Persistent containers survive** Ctrl-C of the AppHost (the front door, session-scoped, goes); the next start reuses
  all of them and is healthy in 3 s, against 27 s for a start that creates them on data volumes that already exist.
  A changed configuration recreates only the containers it changed. Redis's empty argument (`--save ""`) passes
  through.
- **Memory**: the stack with `dialogporten` (18 containers and the front door) is about 2.2 GB by `podman stats`;
  Aspire itself about 0.5 GB proportional set size (the AppHost 140 MB, the dashboard 125 MB, DCP's API server and
  controllers 115 MB, one 26 MB DCP monitor per process), plus 150 MB for the `dotnet run` host.

## Not verified

- An SELinux host (bind mounts without relabelling), rootless Podman (this machine runs Podman rootful behind its
  socket), Docker.
- A first start on empty volumes (the volumes are studioctl's and held data, so the init scripts did not run).
