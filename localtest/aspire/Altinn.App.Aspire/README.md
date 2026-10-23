# Altinn.App.Aspire

A .NET Aspire hosting package that runs the localtest platform (the localtest-v2 container stack) as container
resources of an AppHost. It is a thin loader: it embeds studioctl's
[manifest v2](../../studioctl/src/cli/internal/localtestv2/manifest/README.md)
(`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, by relative path, one copy) and starts what
studioctl's `env up -r localtest-v2` starts: the same containers with the same images, environment, `services__*`
variables, mounts and certificate (on volumes of its own, `altinn-localtest-<id>`), and the localtest container as the front door on `http://local.altinn.cloud:8000`,
the only published port. `AddAltinnApp` runs an Altinn app project beside it with the environment `studioctl run`
gives an app and registers it with the front door, which routes it and provisions it. Tasks 21 and 27b of
[`TODO.md`](../../../TODO.md). The conventions both runtimes follow (the manifest, the front door, service discovery,
ports and health, the certificate, the volumes, the app environment) are in
[`localtest/CONVENTIONS.md`](../../CONVENTIONS.md); this README describes how the package implements them.

Status (2026-10-23): task 27b done in code and tests (75) and **run under Podman** with the sample (`WithDialogporten()`,
the pin `sha-6d928ad`): only the localtest container publishes a port (8000), the app is registered and provisioned
through it, the smoke suites pass, and restarts reuse the containers; see [Run under Podman](#run-under-podman). Task
30: two apps in one AppHost work, see [Several apps](#several-apps). Task 31: the package's volumes are its own (`altinn-localtest-<id>`), no
longer shared with studioctl's (`localtest-v2-<id>`). Task 37: [minimal mode](#minimal-mode),
`AddAltinnLocaltestMinimal()`, in code, tests and the parity golden; its run under Podman is pending.
The documentation stays in this repository until the package is adopted.

| Path | What |
| --- | --- |
| `src/` | The package (`Altinn.App.Aspire`, net10.0, one dependency: `Aspire.Hosting`) |
| `src/Manifest/` | The manifest reader and its validation, the run specification of each container (`StackSpec`), the app environment (`AppEnvironment`, a port of studioctl's `internal/cmd/app/env.go`) |
| `tests/` | xunit v3: the reader, the golden comparisons with studioctl's snapshots, the application model, the registration against a fake front door |
| [`../samples/app/`](../samples/app) | The sample: `App/`, the `ttd/testapp` fixture completed into an app on app-lib 8.12.15; `App2/`, a copy of it as `ttd/testapp2` that exists only to show two apps in one AppHost; `AppHost/apphost.cs`, a single-file AppHost that adds the default stack, the `dialogporten` group and both apps; and `AppHost/apphost-minimal.cs`, the same apps against [minimal mode](#minimal-mode) |

The build is standalone (its own `Directory.Build.props`, `.editorconfig`, csharpier configuration and `global.json`,
SDK 10.0.401): `dotnet build src`, `dotnet test tests`, `dotnet csharpier check .`. The sample builds with
`dotnet build ../samples/app/AppHost/apphost.cs` (and `apphost-minimal.cs`); its `Directory.Build.targets` marks the package's project reference
as a library (`IsAspireProjectResource=false`), which the `#:project` directive cannot say.

## Use

An app repository gets an `apphost.cs` beside its `App/` folder:

```csharp
#:sdk Aspire.AppHost.Sdk@13.6.2
#:package Altinn.App.Aspire@0.1.0-alpha.<run>
#:project ../App/App.csproj

var builder = DistributedApplication.CreateBuilder(args);

var localtest = builder.AddAltinnLocaltest().WithDialogporten();
builder.AddAltinnApp<Projects.App>(localtest);

await builder.Build().RunAsync();
```

```sh
ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost.cs   # or aspire run
```

Stop `studioctl env down -r localtest-v2` first: both need port 8000, and the package refuses to start while it is
taken, naming the holder. `local.altinn.cloud` must resolve to this machine (`studioctl env hosts add` writes the
entries), because the AppHost registers the app through the front door. Aspire's container tunnel does not build on
Podman 5.7 (`ASPIRE_ENABLE_CONTAINER_TUNNEL=false`); the front door reaches the app through `host.docker.internal`
either way. Where the ASP.NET Core development certificate is not trusted (Linux), the dashboard needs
`ASPIRE_ALLOW_UNSECURED_TRANSPORT=true ASPNETCORE_URLS=http://localhost:18888
ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL=http://localhost:18889 ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL=http://localhost:18887`
(local use only) or `dotnet dev-certs https --trust`, or it shows no resources and no commands.

The package is on this repository owner's GitHub Packages feed (see [Publishing](#publishing)), which needs a
personal access token (classic) with `read:packages` even for a public package; a `NuGet.config` beside the AppHost
maps `Altinn.App.Aspire` to `https://nuget.pkg.github.com/ivarne-agent/index.json` with that token
(`packageSourceCredentials`). Without a token, take the `.nupkg` from a GitHub release (`aspire-v*` tags) or a
workflow run's artifact into a local folder source.

| API | What |
| --- | --- |
| `AddAltinnLocaltest(name = "altinn-localtest", configure = null)` | Adds the `default` group's containers and returns the parent resource, which is `Running` once every container is healthy, so an app can `WaitFor` it |
| `.WithGroup(group)` | Adds a group's root containers and what they depend on; `everything` adds every group. An unknown group, or `default` (always on), throws |
| `.WithDialogporten()`, `.WithReceipt()`, `.WithCorrespondence()`, `.WithArbeidsflate()`, `.WithEverything()` | Typed aliases of `WithGroup`; `GroupMethodsTests` fails when they and the manifest's optional groups differ |
| `.WithFrontendDevServer(port = 8080)` | `studioctl run --dev-frontend`: once the front door is healthy, `PUT localtest/api/routes/frontend-dev-server` with `http://host.docker.internal:<port>`, and every app loads app-frontend from `http://app-frontend.local.altinn.cloud:8000` |
| `AltinnLocaltestOptions.Tag` | Replaces every `sha-` pin (`latest` is studioctl's `--latest`; pulled on every start). Default: the manifest's pins |
| `AltinnLocaltestOptions.DataDirectory` | Default `~/.altinn-app-aspire`: the shared test certificate in `certs/` (world-readable, as studioctl writes it), the apps' secrets in `apps/<org>/<app>/secrets`. The platform's data (Postgres, blobs, policies, the localtest app's routes) is not here but in the package's named volumes, `altinn-localtest-<id>`; studioctl's are its own |
| `AltinnLocaltestOptions.Groups` | The same as `WithGroup` for each |
| `AltinnLocaltestOptions.PostgresHostPort` | Publishes Postgres on this loopback port, for `psql`; default none, as studioctl |
| `AddAltinnApp<TProject>(localtest, name = "app", configure = null)` | Adds an app project (`Projects.App` for `#:project ../App/App.csproj`): [The app](#the-app) |
| `AddAltinnApp(localtest, projectPath, name = "app", configure = null)` | The same by the `.csproj`'s path, for an AppHost without the generated `Projects.*` |
| `AltinnAppOptions.OpenTelemetry` | Default `true`: `AppSettings__UseOpenTelemetry=true`, so the app's telemetry reaches the dashboard |
| `AltinnAppOptions.ProvisionWhenReady` | Default `true`: register the app when it is healthy; `false` leaves it to the `provision` command |
| `AddAltinnLocaltestMinimal(name = "altinn-localtest-minimal", configure = null)` | [Minimal mode](#minimal-mode): four containers, the rest emulated, nothing persisted. Its builder takes `AddAltinnApp` and `WithFrontendDevServer`, no group method |
| `AltinnLocaltestMinimalOptions.Tag`, `.PostgresHostPort` | As in `AltinnLocaltestOptions` |

## How the manifest becomes resources

Per selected container (`default` and the enabled groups' roots, closed over `dependsOn`, in start order), as
studioctl's loader (`internal/cmd/env/localtestv2/containers.go`) builds it:

| Manifest or runner | Aspire | Differs from studioctl |
| --- | --- | --- |
| the key | `AddContainer(key, image, tag)`, persistent (`ContainerLifetime.Persistent`), a child of the parent resource; Aspire's network alias is the resource name | container names `<key>-<hash of the AppHost's path>` (`storage-0de70ea0`), Aspire's network |
| `registry`, `tag` | registry and image split (`WithImageRegistry`), `WithImageSHA256` for a digest (Aspire then drops the tag; no container is digest-pinned since `sha-6d928ad`); a floating tag (`tt02`, `latest`) `WithImagePullPolicy(Always)` | studioctl keeps a stale local copy when the registry is unreachable |
| `envs` | `WithEnvironment`, literal | none |
| the `services__<name>__http__0=http://<name>:<port>` of every selected HTTP container | the same, read when the AppHost starts, so a group added later is announced to every container | none |
| the certificate | `<data>/certs` read-only at `/certs`, and its file at `/accesstoken/accesstokencredentials.pfx` | no SELinux relabel (not an Aspire option) |
| `volumes` | `WithVolume("altinn-localtest-" + id, path)`: the prefix (`StackSpec.VolumePrefix`, the default parent resource's name and a hyphen, whatever the parent is called) and the manifest's volume id | the names: studioctl's are `localtest-v2-<id>`, so the two runtimes' data never mixes |
| the public hosts and `host.docker.internal` | `--add-host <host>:host-gateway` | none |
| `dependsOn` | `WaitFor` (healthy); every HTTP container also `WaitForStart(localtest)` | studioctl waits for its probe |
| `health` | [Health](#health) | |
| the front door | the localtest container's `http` endpoint, host port 8000 onto `port` (8080), unproxied, on every interface (the containers reach it through the host gateway); its check is `/health` through 8000 | none |

Nothing else is published. `--dev-<container>` builds have no counterpart (`Tag`, or a locally built image by hand).

### Health

Aspire has no exec health check and can probe only a published endpoint, and only the front door publishes one. Each
other container's check (`InNetworkHealthCheck`) runs studioctl's probe from inside the network:
`<runtime> exec <postgres container> bash -c '<probe>'`, the bash studioctl's probe containers run
(`internal/cmd/env/localtestv2/health.go`): `GET <health>` over `/dev/tcp/<name>/<port>` answering 200, or a TCP
connection for `tcp`. Postgres checks itself at its network address, which its image's init phase does not listen on.
One check tries 10 times half a second apart; Aspire calls it again (after 1, 2, ... 5 s) until it passes. Once it
passed for a container ID it stays healthy without an exec until the container is replaced: a start gate, as
studioctl's, not a liveness probe. The runtime is `ASPIRE_CONTAINER_RUNTIME` (otherwise `docker`); the container IDs
come from the resources' snapshots. Aspire ignores an image's `HEALTHCHECK` for `WaitFor`, which is why the package
runs the check itself. Measured under Podman ([Run under Podman](#run-under-podman)): every container is
healthy 1 to 6 s after what it waits for, the stack in 22 s on existing volumes and 32 s on empty ones, close to
studioctl's 20 s and 21.6 s.

## Minimal mode

```csharp
var localtest = builder.AddAltinnLocaltestMinimal();
builder.AddAltinnApp<Projects.App>(localtest);
```

The Aspire form of `studioctl env up -r localtest-v2 --minimal` (phase 2 of
[`planned-components/minimal-emulation/README.md`](../../../planned-components/minimal-emulation/README.md); the rule in
[`localtest/CONVENTIONS.md`, "Minimal mode"](../../CONVENTIONS.md#minimal-mode)). The sample:
[`../samples/app/AppHost/apphost-minimal.cs`](../samples/app/AppHost/apphost-minimal.cs), both apps.

**What runs.** The manifest's `minimal` group: `postgres`, `storage`, `workflow-engine` and `localtest`, the front door
on `http://local.altinn.cloud:8000`, under the parent `altinn-localtest-minimal`. Each container is built as in the full
stack ([How the manifest becomes resources](#how-the-manifest-becomes-resources): image, `envs`, certificate, extra
hosts, `WaitFor`, [Health](#health)), with three differences, the same as studioctl's (`minimal.go`), held to its
golden snapshot's `minimal` case byte for byte:

| | Full stack | Minimal mode |
| --- | --- | --- |
| `services__<name>__http__0` | every selected HTTP container | the four, and `http://localtest:8080` for every emulated one |
| a manifest volume | `WithVolume("altinn-localtest-<id>", path)` | `WithContainerRuntimeArgs("--tmpfs", "<path>:mode=1777")`; Storage's blobs `size=1g,mode=1777` |
| the front door's `Proxy__AppRoutesFile` | unset: the default `/data/app-routes.json`, on the volume | empty: the app routes and the dev-server route stay in memory |
| lifetime | `ContainerLifetime.Persistent`, `<name>-<hash>`, reused by the next run | `ContainerLifetime.Session`: removed when the AppHost stops |

**What is emulated.** The localtest container answers for every HTTP container of the `default` group that minimal mode
does not start (`AltinnLocaltestMinimalResource.Emulated`): Register, Profile, Authentication, Authorization (it
permits everything), Access Management, Events, the Resource Registry, Notifications and PDF (one static page)
([`localtest/localtest/Emulation/`](../../localtest/Emulation/README.md)). `GET localtest/api/routes` shows them as
`emulated`, and the Home page shows a banner.

**What is lost.** Authorization (no 403, authentication levels not enforced), the Register's real data paths, Profile's
settings, Authentication's OIDC provider for seeded clients, Events delivery, Notifications' pipeline, PDF content, and
the optional groups (Dialogporten, Arbeidsflate, Correspondence, Receipt need the real components). The table and the
reasons: the spec's section 5. Use `AddAltinnLocaltest()` for those.

**Nothing persists.** tmpfs is memory and goes with the container; a tmpfs over the Postgres image's declared `VOLUME`
also keeps the runtime from creating an anonymous volume there. With session lifetime, stopping the AppHost removes the
four containers, so every start is a clean slate: empty databases, no instances, no blobs, no registered app until each
app is healthy again and registers itself (its `provision` command does it by hand, as after a front-door restart). A
persistent container would keep its tmpfs across runs while it kept running, so the clean slate would depend on whether
it was replaced. The full stack's `altinn-localtest-*` volumes are never mounted: switching back finds them intact.
`--tmpfs` is the same flag in Podman and Docker.

**The API.** `AddAltinnLocaltestMinimal()` returns `IResourceBuilder<AltinnLocaltestMinimalResource>`. It and
`AltinnLocaltestResource` are sealed leaves of `AltinnLocaltestResourceBase`; neither derives from the other, so
`IResourceBuilder<out T>`'s covariance does not carry the group methods (`WithGroup`, `WithDialogporten`,
`WithReceipt`, `WithCorrespondence`, `WithArbeidsflate`, `WithEverything`, all on
`IResourceBuilder<AltinnLocaltestResource>`) to the minimal builder: a call does not compile. What both forms support
takes the base: `AddAltinnApp` (the registration, provisioning and the `provision`/`deprovision` commands, as in the full
stack) and `WithFrontendDevServer<T>` (which returns the builder's own type). `GroupMethodsTests` asserts through
reflection that these are the only public methods that take the minimal builder.

| Option | Full stack | Minimal mode |
| --- | --- | --- |
| `Tag` | replaces the `sha-` pins | the same |
| `PostgresHostPort` | publishes Postgres on a loopback port | the same: `psql` into the run's tmpfs database |
| `DataDirectory` | the certificate and the apps' secrets | not offered: minimal mode keeps no data, and the certificate and secrets stay in the default `~/.altinn-app-aspire`, shared with the full stack |
| `Groups` | optional groups | not offered: there are none |

**Commands.** No `reset`: there is nothing to reset, the AppHost's stop removes everything. The apps' `provision` and
`deprovision` are as in the full stack.

**The port guard** runs as in the full stack and refuses a held port 8000, naming the holder. The full stack accepts
this AppHost's own persistent `localtest-<hash>`, which it reuses; minimal mode's front door is a session container, so
the same persistent container left running by a full-stack run is refused by name, with the command that removes it.

**Under Podman**, the same flags as the full stack ([Use](#use)):
`ASPIRE_CONTAINER_RUNTIME=podman ASPIRE_ENABLE_CONTAINER_TUNNEL=false dotnet run apphost-minimal.cs`, and the
dashboard's variables where the development certificate is not trusted.

## The app

`AddAltinnApp<Projects.App>(localtest)` reads the app id from `App/config/applicationmetadata.json` (an `id` of the form
`org/app`, else an `InvalidOperationException` naming the file) and adds the project with:

- **No launch profile and no Kestrel endpoints of its own**: the template's profile and its port 5005 are studioctl's.
- **One HTTP endpoint** without a proxy on an Aspire-assigned port, on every interface (`0.0.0.0`), and
  `Kestrel__EndPoints__Http__Url` to match, since the template's `appsettings.json` names a Kestrel endpoint. The
  dashboard links `http://local.altinn.cloud:8000/{org}/{app}/` and, in the details, the direct URL.
- **`WaitFor(localtest)`**, `/health`, and the registration's health check below.
- **studioctl run's environment** (`AppEnvironment.Build`, compared with every case of studioctl's
  `app-environment.golden.json`): `ASPNETCORE_ENVIRONMENT=Development`, `STUDIOCTL_APP_RUN=1`, the platform endpoints on
  `http://local.altinn.cloud:8000`, the OpenID well-known endpoint, `GeneralSettings__*`, the certificate
  (`<data>/certs/localtest-accesstoken.pfx`, and the keys folder as a traversal from the app root for app-lib v9's 1.x
  client), the secrets directory `<data>/apps/<org>/<app>/secrets` with the development app codes written before the
  AppHost starts (byte for byte studioctl's), the app frontend's inbox, dialog, profile and parties links into the local
  Arbeidsflate (`PlatformFrontendSettings__Arbeidsflate{Inbox,Dialog,Profile,Parties}Url` on
  `http://af.local.altinn.cloud:8000`, so "back to inbox" on a local host opens the inbox instead of the start page; read
  only by the monorepo backend and frontend, see "What the app runner pushes" in `localtest/README.md`), and
  `AppSettings__AppFrontendAssetBaseUrl` with `WithFrontendDevServer`.
  Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT` to its dashboard; `AppSettings__UseOpenTelemetry=true` unless turned off.

## Registration

When the app resource is healthy (`ResourceReadyEvent`) the package calls the front door as studioctl-server's
discovery does: `PUT http://local.altinn.cloud:8000/localtest/api/apps/{org}/{app}` with
`{ "url": "http://host.docker.internal:<port>" }`. The localtest container routes `/{org}/{app}/` to the app and
provisions it from the app's own API (test users, metadata, texts, policy, the Resource Registry, Access Management,
Dialogporten when its adapter runs; [`localtest/localtest/README.md`](../../localtest/README.md#registering-an-app-localtestapiapps-anonymous)).
The answer is logged on the AppHost's console and in the app's log; a refusal (`502` with the failing step) or an
unreachable front door turns the app's `app_provisioning` check **degraded** (the dashboard's warning; the app keeps
running) and the property `altinn.provisioning` holds the last outcome. Nothing is deregistered when the app stops: the
front door keeps the route, and the next start registers it again. The front door strips `Secure` from an app's
cookies, as under studioctl, so app-lib's `keepAlive` cookie works over http.

| Resource | Command | What |
| --- | --- | --- |
| the app | `provision` | The `PUT` again (after an edit of `testData.json`, the texts or the policy) |
| the app | `deprovision` | After a confirmation, `DELETE localtest/api/apps/{org}/{app}`: the test users leave the Register, the route the front door |
| `altinn-localtest` (the full stack; minimal mode has none) | `reset` | After a confirmation, `rm --force` of the containers by ID and `volume rm --force` of the package's volumes (`altinn-localtest-<id>`) that exist, through the runtime's CLI; then restart the AppHost. studioctl's `localtest-v2-<id>` volumes stay |

## Several apps

One AppHost runs several apps against one platform: one `AddAltinnApp` per app, each with its own resource name, as the
sample does (`AddAltinnApp<Projects.App>(localtest)` and `AddAltinnApp<Projects.App2>(localtest, "app2")`). A second
app under a name already taken is refused by Aspire (`DistributedApplicationException`), and a second resource of an
app id already added by the package (`InvalidOperationException`), since the two would share the route and the secrets
directory.

| Per app | Shared |
| --- | --- |
| The project resource and its rebuilder, the `http` endpoint on its own Aspire-assigned port, the Kestrel URL, the `<name>_provisioning` health check, the registration `PUT localtest/api/apps/{org}/{app}` with its own `host.docker.internal:<port>`, the `provision` and `deprovision` commands (they call only that app's path), the secrets directory `<data>/apps/<org>/<app>/secrets` (keyed on the app id, not the resource name) | The platform and its containers, the front door on 8000, the test certificate `<data>/certs`, the environment otherwise (the same platform endpoints), the Register's store |

**The Register.** Each provisioning posts that app's `testData.json` to `PUT register/api/localtest/apps/{org}/{app}`,
and the Register rebuilds its store from the seed and every registered app's file
([`components/register/README.md`](../../components/register/README.md#app-test-data)): a second app adds to the first
app's users and does not replace them. When every registered app posted test data, the seed is left out (`"source":
"apps"`), with one app as with two. Identical entries in two apps' files are fine; an id reused with a different
definition is skipped (first wins) and listed under `conflicts`. `deprovision` of one app removes only its file, so
users that another registered app also lists stay.

Run (task 30, 2026-10-23, Podman 5.7, arm64, the sample with both apps and `WithDialogporten()`, 18 containers):

- Both apps provisioned 35 s after `dotnet run` (images present, containers created): `GET localtest/api/apps` listed
  `ttd/testapp` on `host.docker.internal:21833` and `ttd/testapp2` on `:21834`, both `succeeded` (1 text, policy, test
  data, Dialogporten `Synced`); through 8000 `/ttd/testapp/api/v1/applicationmetadata` and
  `/ttd/testapp2/api/v1/applicationmetadata` answered 200 with their own ids.
- The Register after both provisionings: both apps with `TestData` (2 persons, 1 organisation each), no conflicts,
  2 users and 3 parties (the two files are identical, so the merged store is one file's users).
- The dashboard's `deprovision` on `app2` (its confirmation names `ttd/testapp2`): `DELETE .../ttd/testapp2` answered
  204; `ttd/testapp` stayed registered and answering 200, `/ttd/testapp2/...` answered 404 ("No app is registered for
  this path"), the Register kept `ttd/testapp` and its 2 users. `provision` on `app2` brought it back (200 through 8000,
  both in the Register again).
- SIGINT to the process group: DCP, the dashboard and both apps gone within 8 s, none to kill; the containers kept
  running, and the front door kept both routes (`/ttd/testapp/...` answered 503 while the apps were down). The restart
  reused the 18 containers (same creation times) and registered both apps again on new ports in 12 s.
- Memory with both apps up: 3.7 GB used of 7.9 GB, 4.3 GB available; the containers about 1.6 GB by `podman stats`, each
  app about 155 MB resident.

## Publishing

`.github/workflows/aspire-package.yml` runs on a push to `main` that changes `localtest/aspire/**`, the manifest, the
certificate or the golden files the tests read, and on `workflow_dispatch`: csharpier, build, test, a build of the
sample, `dotnet pack` as `0.1.0-alpha.<run number>`, the `.nupkg` as an artifact, and `dotnet nuget push` to
`https://nuget.pkg.github.com/<owner>/index.json`. A tag `aspire-v<version>` packs that version and attaches the
`.nupkg` to a GitHub release.

## Tests and parity

`dotnet test tests -c Release` (92 tests). Parity with studioctl means both runners build the same thing from the one
manifest; the package's tests compare what it builds with studioctl's golden snapshots (under
`localtest/studioctl/src/cli/`), which studioctl's own tests hold its loaders to:

| Golden file | This package's test |
| --- | --- |
| `internal/cmd/env/localtestv2/testdata/environment.golden.json`: `env up`, `--with everything` and `--minimal` | `GoldenComparisonTests`: the selection and start order; per container, from the built application model, the image reference (default stack), the environment with the `services__*` variables, the mounts, the published port, the extra hosts, the alias, the dependencies; the health targets and the front door's; the groups' selections and the volume ids. A named volume compares by its id, each runner's prefix stripped (studioctl's from the snapshot's `volumePrefix`); `ThePackageNamesItsVolumesByItsOwnPrefix` holds the package to `altinn-localtest-`. In the `minimal` case a tmpfs compares by its path and options (`--tmpfs path:options` in the runtime arguments), and `MinimalModeEmulatesWhatStudioctlEmulatesAndMountsNoVolume` holds the emulated set to the snapshot's |
| `internal/cmd/app/testdata/app-environment.golden.json` | `GoldenComparisonTests`: every case (native, inherited variables, ingress port 80, container) from the same inputs |
| `internal/appsecrets/testdata/app-codes.golden.json` | `AppTests`: byte-equal, owner-only, not rewritten when unchanged |

Besides: `ManifestTests` (the embedded file; defaults and start order; every validation of the Go loader: an unknown
key, an unknown or self reference, a duplicate, a cycle, an unreached container, an empty registry or tag, a bad
health, port, env key, volume id or volume path, no `default` group, trailing data), `ModelTests` (the default, `dialogporten`,
`correspondence` and `everything` selections, the `services__*` of every container and of a group added later, only
8000 published, persistence, parent and health checks, the extra hosts, the mounts, images and the `Tag` option, the
waits, the probe script), `AppTests` (the app's environment, endpoint, waits, commands, the metadata error, the port
guard, the reset's commands), `FrontDoorRegistrationTests` (the `PUT` and `DELETE` against an in-process fake front
door, a `502` and an unreachable front door as a degraded app, the dev-server route), `GroupMethodsTests` (the typed
group methods against the manifest's groups; through reflection, that the only public methods taking minimal mode's
builder are `AddAltinnApp` and `WithFrontendDevServer`) and `MinimalModeTests` (the minimal sample's model: the four
containers, session lifetime, the tmpfs mounts, the emulated set, the in-memory routes, both apps, no reset command,
the options, the port guard).

`.github/workflows/manifest-parity.yml` runs studioctl's Go tests (which fail on a stale snapshot) and these on every
pull request and push to `main` that touches the manifest, the Go loaders or the package. Not covered: behaviour at run
time (the smoke suites, run by hand against either runtime: [`localtest/README.md`](../../README.md#tests)), and what
differs by design (the health mechanism, `--dev`, pull policies, container names).

## Run under Podman

Task 27b phase 2 (2026-10-22), Podman 5.7 rootful, arm64, the sample AppHost with `WithDialogporten()` (18 containers)
on the pinned images (`sha-6d928ad`), started with the environment of [Use](#use) through a launcher that gives the
`dotnet run` process group the default SIGINT handling (a shell's background job ignores SIGINT, and the AppHost then
ignores Ctrl-C sent with `kill -INT`).

| Run | Platform healthy | App provisioned (from `dotnet run`) |
| --- | --- | --- |
| Containers created, volumes from an earlier studioctl stack (shared until task 31) | 22 s | 33 s |
| Restart, all 18 containers reused (same creation times) | 4 s | 11 s |
| First start after the `reset` command (no volumes; Postgres ran its nine `CREATE DATABASE`) | 32 s | 40 s |

The containers' "healthy after" times on empty volumes: localtest and Redis 3 s, the Register 4, Postgres and pdf3 6,
Storage 9, Profile and the workflow engine 10, the Resource Registry 11, Authentication and the PDP 12, Events 15,
Notifications 16, Access Management 23, Dialogporten's WebApi 29 (its migrations), the adapter 30, Service and GraphQL
32. On the reused stack every container was healthy in 4 s.

- `podman port` lists one published port: `localtest-<hash>: 8080/tcp -> 0.0.0.0:8000`.
- The registration: `PUT .../localtest/api/apps/ttd/testapp answered 200` with `"state":"succeeded"`, one text resource,
  policy, test data and Dialogporten `Synced`; `GET localtest/api/apps` lists it; the app answers through 8000
  (`/ttd/testapp/api/v1/applicationmetadata` 200).
- A group that is off: `/correspondence/...` and `/receipt/` answer `503` problem details "Component not running" naming
  `.WithCorrespondence()`; `af.local.altinn.cloud` 503 too.
- The smoke suites through `localtest/smoke/lib/containers.sh`, after `DELETE localtest/api/apps/ttd/testapp` and the PDP
  refresh: root 26 of 26, authentication 34 of 34, Dialogporten 60 of 60, authorization 31 of 31, the localtest app
  80 of 81 with every check (the one failure is `/app-frontend/` without `FRONTEND_DIST`, as under studioctl). The app
  was registered again afterwards.
- `reset` from the dashboard (confirmed in its dialog) removed the 18 containers and the five volumes.
- SIGINT to the process group: the AppHost exits in about a second, DCP and the app within 12 s more by themselves; no
  process was left to kill. The persistent containers stay, as designed.
- Memory with the 18 containers: about 1.9 GB by `podman stats`, 4.3 GB used of 7.9 GB on the machine.

Task 31 run (2026-10-23, Podman 5.7, arm64, the pins `sha-6d928ad`): `studioctl env reset -r localtest-v2 --yes`
removed the four `localtest-v2-*` volumes that existed; `env up -r localtest-v2` (default stack, 24 s) created
`localtest-v2-{postgres-data,policy-store,storage-blobs,localtest-data}` and no other named volume; the root suite 26 of
26; `env down`. Then the sample AppHost (both apps, `WithDialogporten()`, 18 containers, on empty volumes): both apps
provisioned 40 s after `dotnet run`, beside the four `localtest-v2-*` volumes the four `altinn-localtest-*` ones; the
root suite through `smoke/lib/containers.sh` (after deprovisioning both apps and the PDP refresh) 26 of 26; the
dashboard's `reset` (Playwright) removed the 18 containers and the four `altinn-localtest-*` volumes and left the four
`localtest-v2-*` ones. SIGINT to the process group: the AppHost gone in 3 s, DCP and the apps within 45 s by
themselves; nothing to kill.

Found and fixed: the port guard refused every restart, because the persistent localtest container keeps port 8000
between runs; it now accepts the port when this AppHost's own `localtest-<hash>` container runs.

## History

Runs of the package before task 27b (21d to 21f, 2026-10-22, Podman 5.7 rootful, arm64), when it still carried its
own proxy; the findings that still apply:

- Persistent containers survive Ctrl-C of the AppHost and are reused by the next start (healthy in 3 s), unless their
  configuration changed; a start that creates them on existing volumes took 27 s, a first start on empty volumes after
  `reset` 36 s (the app provisioned at 47 s).
- `--add-host ...:host-gateway` works under Podman; containers reach the public origin through the gateway.
- Memory: the default stack plus `dialogporten` about 2.2 to 3.0 GB by `podman stats`, Aspire about 0.5 GB.
- A browser signed in to the sample app and submitted it to the receipt
  ([`docs/sample-app-form.png`](docs/sample-app-form.png), [`docs/sample-app-receipt.png`](docs/sample-app-receipt.png));
  app-frontend comes from the CDN. pdf3 could not reach the CDN through this sandbox's proxy (an environment limit).
- The smoke suites passed against the sample with the containers found by `localtest/smoke/lib/containers.sh`: root 26,
  authentication 34, Dialogporten 60, authorization 31, the localtest app 64 of 66 with `PROXY_CHECKS=0`.

## Not verified

- Minimal mode under Podman (task 37 phase 2): the tmpfs mounts, the session lifetime, the clean slate on a restart,
  the smoke suites.
- `WithFrontendDevServer` against a running Vite server. (The `provision` and `deprovision` commands were run from the
  dashboard in task 30, on the second app; see [Several apps](#several-apps).)
- An SELinux host (bind mounts without relabelling), rootless Podman, Docker.
