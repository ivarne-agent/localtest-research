# Altinn.App.Aspire

A .NET Aspire hosting package that runs the localtest platform (the localtest-v2 component stack) as container
resources of an AppHost, behind a YARP front door on `http://local.altinn.cloud:8000`. It reads the same component
manifest studioctl runs (`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, embedded by
relative path, one copy), so the images, environment, ports, volumes, certificate, dependencies, groups and routes are
studioctl's. `AddAltinnApp` runs an Altinn app project beside it with the environment `studioctl run` gives an app
(the manifest's `app` section), its route on the front door and its provisioning on the platform. Task 21 of
[`TODO.md`](../../../TODO.md): 21d (the platform and its front door) and 21e (the app).

Status (2026-10-22): the platform **runs under Podman** (21d): the default stack plus `dialogporten` passes the root
(26), authentication (34) and Dialogporten (60) smoke tests through its front door, and survives AppHost restarts (see
[Run under Podman](#run-under-podman)). The app side (21e) is **unit-tested, not run yet**: the sample app under Podman
is phase 2 of 21e.

| Path | What |
| --- | --- |
| `src/` | The package (`Altinn.App.Aspire`, net10.0). It also compiles the shared provisioning library's sources ([`Altinn.Studio.Localtest.Provisioning`](../../studioctl/src/Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning/README.md)), so the NuGet is one assembly with no unpublished dependency |
| `src/Manifest/` | The C# manifest reader: the model (System.Text.Json, source-generated, unknown keys refused), the placeholders, group closure, images and routes |
| `tests/` | xunit v3: the reader against studioctl's golden files, the schema, the application model the sample builds, the front door's routes through an in-process YARP, the app side |
| [`../samples/app/`](../samples/app) | The sample: `App/`, the `ttd/testapp` fixture completed into an app, and `AppHost/apphost.cs`, a single-file AppHost that adds the default stack, the `dialogporten` group and the app |

The build is standalone like the provisioning library's (its `Directory.Build.props`, `.editorconfig`, csharpier
configuration and `global.json`, SDK 10.0.401): `dotnet build src`, `dotnet test tests`, `dotnet csharpier check .`.
The sample builds with `dotnet build ../samples/app/AppHost/apphost.cs`; its `Directory.Build.targets` marks the
package's project reference as a library (`IsAspireProjectResource=false`), which the `#:project` directive cannot say,
and leaves the app's a project resource, so the SDK generates `Projects.App`.

## Use

An app repository gets an `apphost.cs` beside its `App/` folder:

```csharp
#:sdk Aspire.AppHost.Sdk@13.6.2
#:package Altinn.App.Aspire@0.1.0-alpha.<run>
#:project ../App/App.csproj

var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltest().WithGroup("dialogporten");
builder.AddAltinnApp<Projects.App>(localtest);

await builder.Build().RunAsync();
```

```sh
ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost.cs   # or aspire run
```

The sample in this repository (`../samples/app/AppHost/apphost.cs`) references the package by `#:project` instead of
`#:package`. Stop `studioctl env down -r localtest-v2` first: both listen on port 8000 and on the components' host
ports, and the package refuses to start while port 8000 is taken. `local.altinn.cloud` must resolve to this machine
(`studioctl env hosts add` writes the entries), because the AppHost provisions the app through the front door.

### Getting the package

The package is published to this repository owner's feed on GitHub Packages (see [Publishing](#publishing)). GitHub
Packages needs a token even for public packages: a personal access token (classic) with `read:packages`. A
`NuGet.config` beside the AppHost:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="localtest-research" value="https://nuget.pkg.github.com/ivarne-agent/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="localtest-research">
      <package pattern="Altinn.App.Aspire" />
    </packageSource>
  </packageSourceMapping>
  <packageSourceCredentials>
    <localtest-research>
      <add key="Username" value="%GITHUB_USER%" />
      <add key="ClearTextPassword" value="%GITHUB_PACKAGES_TOKEN%" />
    </localtest-research>
  </packageSourceCredentials>
</configuration>
```

with `GITHUB_USER` and `GITHUB_PACKAGES_TOKEN` (the `read:packages` token) in the environment. Without a token, take
the `.nupkg` from a GitHub release (`aspire-v*` tags) or a workflow run's artifact into a local folder source
(`<add key="local" value="./packages" />`). The versions are `0.1.0-alpha.<run number>` of the workflow; pin one in
`#:package Altinn.App.Aspire@<version>`.

| API | What |
| --- | --- |
| `AddAltinnLocaltest(name = "localtest", configure = null)` | Adds the always-on components and the front door, returns the parent resource. It is `Running` once every component and the front door is healthy, so an app can `WaitFor` it |
| `.WithGroup(group)` | Adds an optional group's components and those of the groups it implies (`correspondence` and `arbeidsflate` bring `dialogporten`, `everything` brings all) |
| `AltinnLocaltestOptions.Tag` | The research images' tag. Default: `STUDIOCTL_LOCALTEST_V2_TAG`, otherwise the manifest's pin; `latest` is pulled on every start. A per-image `STUDIOCTL_IMAGE_LOCALTEST_V2_<NAME>` (and `STUDIOCTL_IMAGE_PDF3`, `STUDIOCTL_IMAGE_WORKFLOW_ENGINE`) still wins, as with studioctl |
| `AltinnLocaltestOptions.DataDirectory` | Default `~/.altinn-app-aspire`; the shared test certificate is written to `certs/` there (world-readable, as studioctl does) when the AppHost starts |
| `AltinnLocaltestOptions.Groups` | The same as `WithGroup` for each |
| `AltinnLocaltestResource.Groups`, `.PlatformUri`, `.CertificateDirectory`, `.DataDirectory` | For an AppHost's own resources |
| `AddAltinnApp<TProject>(localtest, name = "app", configure = null)` | Adds an app project (`TProject` is the SDK's `Projects.App` for `#:project ../App/App.csproj`, which works in a single-file AppHost) run against the platform: [The app](#the-app) |
| `AddAltinnApp(localtest, projectPath, name = "app", configure = null)` | The same by the path of the app's `.csproj` (absolute, or relative to the AppHost's folder), for an AppHost without the generated `Projects.*` |
| `AltinnAppOptions.OpenTelemetry` | Default `true`: `AppSettings__UseOpenTelemetry=true`, so the app's traces, metrics and logs reach the dashboard through the OTLP endpoint Aspire gives every project |
| `AltinnAppOptions.ProvisionWhenReady` | Default `true`: provision when the app is healthy; `false` leaves it to the `provision` command |

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
| `env reset` | the localtest resource's `reset` command ([Commands](#commands)) | the AppHost is restarted by hand afterwards |
| `env logs`, `env status` | none; the dashboard shows logs and state | none |
| studioctl-server's port check | a `BeforeStartEvent` check of port 8000 ([Port 8000](#port-8000)) | none |

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
| `local.altinn.cloud`, `localhost`, `127.0.0.1` | `/ttd/testapp/{**catch-all}` | -1 | `app` (`AddAltinnApp`) | |

An app's route (`/{org}/{app}/{**catch-all}`, order -1) comes after every component prefix and before the localtest
app's fall-through. studioctl-server sorts an app's prefix among the component prefixes by length; the two agree
unless an org is named like a component prefix (`/storage/...`).

**Known gap, accepted (TODO task 21, 21d decisions):** the stock YARP image has no cookie transform, so it cannot strip
`Secure` from a `Set-Cookie`. The components no longer set `Secure` cookies over http (21b), but app-lib's `keepAlive`
cookie does: an app behind this front door (21e) loses the ten-minute token refresh over http until app-lib takes
`Secure` from the request (task 22).

## The app

`AddAltinnApp<Projects.App>(localtest)` reads the app id from `App/config/applicationmetadata.json` in the project's
folder (`IProjectMetadata.ProjectPath`) when the model is built; without the file, or without an `id` of the form
`org/app`, it throws an `InvalidOperationException` naming the file. It then adds the project with:

- **No launch profile and no Kestrel endpoints of its own** (`ExcludeLaunchProfile`, `ExcludeKestrelEndpoints`): the
  template's profile and its `appsettings.json` endpoint (port 5005) are studioctl's.
- **One HTTP endpoint `http` without a proxy**, on a port Aspire assigns (the same for the endpoint and the app), with
  the target host `0.0.0.0`, so the app listens on every interface (`ASPNETCORE_URLS` follows the target host). The
  template's `appsettings.json` names a Kestrel endpoint, which wins over `ASPNETCORE_URLS`, so
  `Kestrel__EndPoints__Http__Url=http://0.0.0.0:<port>` is set as well, as studioctl sets its own Kestrel URL. The
  dashboard links the front-door URL (`http://local.altinn.cloud:8000/{org}/{app}/`) and, in the details, the direct
  one.
- **The route** `/{org}/{app}/{**catch-all}` on the front door, original Host kept. The cluster's destination is
  `http://host.docker.internal:<port>` and the front door gets `--add-host host.docker.internal:host-gateway`: the
  container tunnel does not build on Podman 5.7 (21d), so the front door reaches the app on the host through the
  runtime's gateway, as the components reach the front door. The destination is written by the package rather than
  taken from the project's endpoint, which Aspire would resolve for a container to its own container host name
  (`host.containers.internal` under Podman, without the tunnel).
- **`WaitFor(localtest)`**: the app starts once every component and the front door are healthy.
- **Health**: `/health` (app-lib's) and the provisioning check below.
- **The environment** of the manifest's `app` section
  ([`manifest/README.md`](../../studioctl/src/cli/internal/localtestv2/manifest/README.md#the-app-section)), expanded
  with the same resolver as the components:

| Variable | Value in the sample |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` |
| `STUDIOCTL_APP_RUN` | `1` (app-lib v9 then skips its own import of `studioctl app env`) |
| `AppSettings__OpenIdWellKnownEndpoint` | `http://local.altinn.cloud:8000/authentication/api/v1/openid/` |
| `GeneralSettings__ExternalAppBaseUrl`, `GeneralSettings__HostName` | `http://local.altinn.cloud:8000/{org}/{app}/`, `local.altinn.cloud` |
| `PlatformSettings__Api*Endpoint` (eleven) | `http://local.altinn.cloud:8000/<component>/api/v1/`; PDF `http://pdf.local.altinn.cloud:8000/pdf`, the workflow engine `http://workflow-engine.local.altinn.cloud:8000/api/v1/` |
| `AccessTokenSettings__AccessTokenSigningCertificateFullPath` | `<data>/certs/localtest-accesstoken.pfx` on the host (`{{appCertPath}}`) |
| `AccessTokenSettings__AccessTokenSigningKeysFolder` | the certificate directory as a traversal from the app root, the parent of the app's working directory `App/` (`{{appCertKeysFolder}}`, what app-lib v9's 1.x client resolves) |
| `AccessTokenSettings__AccessTokenSigningCertificateFileName` | `localtest-accesstoken.pfx` |
| `RUNTIME_APP_SECRETS_DIR` | `<data>/apps/<org>/<app>/secrets`, owner-only, with `app-codes.json` (the development app codes) written before the AppHost starts, byte for byte what studioctl writes |
| `RUNTIME_APP_SECRETS_APPCODES_FILENAME`, `RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME` | `app-codes.json`, `maskinporten-settings.json` (a Maskinporten client is the developer's to put there) |
| `AppSettings__UseOpenTelemetry` | `true` unless `AltinnAppOptions.OpenTelemetry` is `false` |
| `Kestrel__EndPoints__Http__Url` | `http://0.0.0.0:<port>` (the package's, not the manifest's) |

`<data>` is `AltinnLocaltestOptions.DataDirectory` (`~/.altinn-app-aspire`). Aspire adds `OTEL_EXPORTER_OTLP_ENDPOINT`
(its dashboard) and its own service variables; the frontend asset base URL (`--dev-frontend`) is not supported, so the
app loads app-frontend from the CDN as the template does.

**The keepAlive gap.** The stock YARP image cannot strip `Secure` from a `Set-Cookie`, and app-lib's `keepAlive`
sets the runtime cookie `Secure` in every environment, so over http the browser drops the refreshed cookie and the app
loses its ten-minute token refresh: sign in again when the token expires. studioctl-server strips the flag on app
routes; here it waits for app-lib to set it from the request (task 22).

## Provisioning

When the app resource is healthy (`ResourceReadyEvent`), the package runs the shared library's
`LocaltestProvisioner.Provision` with a `DirectoryAppSource` on the project folder: the app does not serve its own
files, they are read from disk (`App/wwwroot/testData.json`, `App/config/applicationmetadata.json`, the texts and the
policy). `PlatformUri` is the front door (`http://local.altinn.cloud:8000/`), so the AppHost process must resolve
`local.altinn.cloud` to this machine; the Studio token comes from the localtest app (`/Home/GetTestStudioToken`, the
library's default); Dialogporten's sync (step 5) runs when the AppHost added the `dialogporten` group. The steps and
their endpoints: [`localtest/README.md`](../../README.md#what-the-app-runner-pushes).

A failure does not fail the app's start. It is logged in the app's console in the dashboard and on the AppHost's
console with its step and URL (`provision failed at step 2, Storage's application metadata and texts
(http://local.altinn.cloud:8000/storage/api/v1/applications?appId=ttd/testapp): ...`), the app's health check
`app_provisioning` turns **degraded** with that message (the dashboard's warning state; the app keeps running), and the
property `altinn.provisioning` holds the last outcome. The `provision` command runs it again; a success clears the
warning. The step is told by the last request the provisioner sent (a delegating handler on its `HttpClient`), so the
library is unchanged.

Nothing is deprovisioned when the app or the AppHost stops: every step is idempotent, the next start provisions again,
and in between the Register keeps the last run's test users as Storage and the PDP keep the app, as a deployed app
stays registered. The `deprovision` command removes the test users (`DELETE register/api/localtest/apps/{org}/{app}`
and an Access Management refresh); Storage and the PDP keep the app.

## Commands

| Resource | Command | What |
| --- | --- | --- |
| the app | `provision` | Provisions the app again from its files (after an edit of `testData.json`, the texts or the policy) |
| the app | `deprovision` | Removes the app's test users from the Register, after a confirmation |
| `localtest` | `reset` | After a confirmation: removes the persistent component containers (`rm --force` by their container IDs) and the manifest's five named volumes that exist, through the container runtime's CLI (`ASPIRE_CONTAINER_RUNTIME`, otherwise `docker`). Then restart the AppHost, which creates them afresh. The volumes are studioctl's too, so this also resets a studioctl `localtest-v2` stack's data, as `studioctl env reset -r localtest-v2` resets the Aspire stack's |

## Port 8000

When the AppHost starts (`BeforeStartEvent`, run mode), the package binds port 8000 on every interface and connects to
it on loopback. When either says it is taken, the AppHost refuses to start with the holder, read from `/proc/net/tcp`
and the processes' sockets on Linux (`Port 8000 is in use by studioctl-server (pid 1234): another localtest stack or
AppHost is running ...`), or without it when it cannot be told (another user's process, a runtime's forwarding,
another platform).

## Publishing

`.github/workflows/aspire-package.yml` runs on a push to `main` that changes `localtest/aspire/**`, the manifest, the
golden files the tests read or the provisioning library, and on `workflow_dispatch`: csharpier, build, test, a build of
the sample AppHost and app, `dotnet pack` as `0.1.0-alpha.<run number>`, the `.nupkg` as a workflow artifact, and
`dotnet nuget push` to `https://nuget.pkg.github.com/<owner>/index.json` with the workflow's `GITHUB_TOKEN`
(`packages: write`). A tag `aspire-v<version>` packs that version and attaches the `.nupkg` to a GitHub release, the
token-free way to get it. nuget.org under Altinn's prefix is for adoption, not this repository.

## Tests

`dotnet test tests -c Release` (76 tests):

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
- **App** (`AppTests`): the sample app's environment has every variable of the manifest's `app` section expanded (no
  placeholder left) with the package's certificate and secrets paths; the app section expands to studioctl's snapshot
  (`internal/cmd/app/testdata/app-environment.golden.json`, the `native-localtest-v2` case, less the runner's own
  variables) and the keys folder is computed as studioctl computes it; the app codes are the bytes of studioctl's
  `internal/appsecrets/testdata/app-codes.golden.json`, owner-only, and not rewritten when unchanged; the endpoint
  (unproxied, `0.0.0.0`, no fixed port) and the Kestrel URL; the route, its order and the cluster's
  `host.docker.internal` destination with the front door's `--add-host`; the waits, health checks and commands; the
  error for a project without metadata; a provisioning against a refused port is a degraded health check naming the
  step and URL; the port guard names the holding process; the reset's runtime commands.
- **Routing** (`FrontDoorRoutingTests`): those YARP routes loaded into an in-process YARP (`Yarp.ReverseProxy` 2.3.0;
  the image is `2.3-preview`) in front of an echo server: longest prefix, the bare prefix, segment boundaries, case,
  the rewrite with the rest of the path and the query, the fall-through for `/receipt` and `/correspondence`, the
  `localhost` and `127.0.0.1` fall-back, the `pdf.` and `workflow-engine.` hosts, the app's `/ttd/testapp` route (and
  `/ttd/testappx`, `/ttd/other` falling through), the original Host, and YARP's 404 for `af.local.altinn.cloud`.

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

- The whole app side (21e phase 2): the app on its Aspire-assigned port on `0.0.0.0`, the front door reaching it
  through `host.docker.internal` under Podman, the provisioning against the running platform, the degraded state in
  the dashboard, the commands, the port refusal with a studioctl stack up, the workflow's run.
- An SELinux host (bind mounts without relabelling), rootless Podman (this machine runs Podman rootful behind its
  socket), Docker.
- A first start on empty volumes (the volumes are studioctl's and held data, so the init scripts did not run).
