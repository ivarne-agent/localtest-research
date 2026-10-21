# Altinn.App.Aspire

A .NET Aspire hosting package that runs the localtest platform (the localtest-v2 component stack) as container
resources of an AppHost, behind a YARP front door on `http://local.altinn.cloud:8000`. It reads the same component
manifest studioctl runs (`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, embedded by
relative path, one copy), so the images, environment, ports, volumes, certificate, dependencies, groups and routes are
studioctl's. Task 21 of [`TODO.md`](../../../TODO.md); this is 21d, the first slice: the platform and its front door,
no app yet.

Status (2026-10-22): **built and unit-tested, not yet run**. Nothing in this folder has started a container: phase 2 of
21d runs the sample under Podman and the smoke tests through the front door (see [Not verified](#not-verified)).

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
ASPIRE_CONTAINER_RUNTIME=podman aspire run      # or dotnet run apphost.cs; Docker is Aspire's default
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
| `containerPort`, `hostPort` | `WithHttpEndpoint(port: hostPort, targetPort: containerPort, name: "http", isProxied: false)` | DCP's default host IP is not set (studioctl publishes on `127.0.0.1`) |
| no `hostPort` (Postgres, Redis) | a proxied TCP endpoint on a random loopback port, for the host-side probe | studioctl publishes nothing |
| `healthPath` | `WithHttpHealthCheck(path)` | Aspire polls from the host; `WaitFor` waits for healthy |
| `containerHealthCheck` | Postgres: an `SSLRequest` handshake; Redis: `PING`, both host-side through the TCP endpoint (`ProtocolHealthCheck`). Dialogporten WebApi: its HTTP `healthPath`, which `WaitFor` uses to keep the other hosts' migrations apart | Aspire has no exec health checks; a new exec-only component fails `AddAltinnLocaltest` until it gets a probe |
| `mounts` `certificateDir` | `WithBindMount(<data>/certs, /certs, isReadOnly: true)` | no SELinux relabel: `:z` is not an Aspire option, so on an SELinux host (Fedora with Podman) the bind may be unreadable ([aspire#3945](https://github.com/microsoft/aspire/issues/3945)); this machine is not SELinux |
| `mounts` `certificateFile` | `WithBindMount(<data>/certs/localtest-accesstoken.pfx, target, isReadOnly: true)` | as above |
| `mounts` `volume` | `WithVolume(name, target)` with the manifest's volume names | none, on purpose: the volumes are **shared with studioctl** (same images, same data layout), so `studioctl env reset -r localtest-v2` also resets the Aspire stack's data |
| `networkAliases` | `WithContainerNetworkAlias(alias)` (skipped when it equals the resource name, as for `postgres` and `redis`) | none |
| `ingressHosts` (default true), `extraHostGatewayNames` | `WithContainerRuntimeArgs("--add-host", "<host>:host-gateway")` for `local.altinn.cloud`, `pdf.`, `workflow-engine.`, `pgadmin.`, `app-frontend.`, `af.local.altinn.cloud` and each extra name; such a component also `WaitForStart(frontdoor)` | none in the model; under Podman not yet run |
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

## Not verified

Phase 2 of 21d runs these; until then they are readings of the API and of DCP's code (13.6.2):

- **The front door on every interface.** DCP sets a container port's host IP from the endpoint's target host
  (`ContainerCreator`: any target host other than `localhost` becomes `HostIP`), so `0.0.0.0` should publish on all
  interfaces, and a proxied endpoint would be DCP's loopback proxy. The first check of phase 2:
  `curl http://local.altinn.cloud:8000/` from inside a component container.
- **`--add-host ...:host-gateway` under rootless Podman** through DCP. Fallback (reported, not switched silently):
  network aliases on the front door's container.
- **The YARP container's destinations**: the clusters name the components' endpoints (`http://register`), which the
  YARP image resolves through Aspire's service discovery variables to their container-network addresses.
- **Persistent containers across AppHost restarts**, including the proxied TCP endpoints of Postgres and Redis.
- **Redis's empty argument** (`--save ""`) through DCP's argument list.
- **Memory** with DCP and the dashboard beside the stack.
