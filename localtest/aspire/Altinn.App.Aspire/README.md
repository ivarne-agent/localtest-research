# Altinn.App.Aspire

A .NET Aspire hosting package that runs the localtest platform (the localtest-v2 container stack) as container
resources of an AppHost. It is a thin loader: it embeds studioctl's
[manifest v2](../../studioctl/src/cli/internal/localtestv2/manifest/README.md)
(`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, by relative path, one copy) and starts what
studioctl's `env up -r localtest-v2` starts: the same containers with the same images, environment, `services__*`
variables, volumes and certificate, and the localtest container as the front door on `http://local.altinn.cloud:8000`,
the only published port. `AddAltinnApp` runs an Altinn app project beside it with the environment `studioctl run`
gives an app and registers it with the front door, which routes it and provisions it. Tasks 21 and 27b of
[`TODO.md`](../../../TODO.md).

Status (2026-10-23): task 27b phase 1, code and tests (69). The run under Podman with this version (phase 2) is open;
the runs recorded under [History](#history) were made with the package's earlier YARP front door (tasks 21d to 21f).
The documentation stays in this repository until the package is adopted.

| Path | What |
| --- | --- |
| `src/` | The package (`Altinn.App.Aspire`, net10.0, one dependency: `Aspire.Hosting`) |
| `src/Manifest/` | The manifest reader and its validation, the run specification of each container (`StackSpec`), the app environment (`AppEnvironment`, a port of studioctl's `internal/cmd/app/env.go`) |
| `tests/` | xunit v3: the reader, the golden comparisons with studioctl's snapshots, the application model, the registration against a fake front door |
| [`../samples/app/`](../samples/app) | The sample: `App/`, the `ttd/testapp` fixture completed into an app on app-lib 8.12.15, and `AppHost/apphost.cs`, a single-file AppHost that adds the default stack, the `dialogporten` group and the app |

The build is standalone (its own `Directory.Build.props`, `.editorconfig`, csharpier configuration and `global.json`,
SDK 10.0.401): `dotnet build src`, `dotnet test tests`, `dotnet csharpier check .`. The sample builds with
`dotnet build ../samples/app/AppHost/apphost.cs`; its `Directory.Build.targets` marks the package's project reference
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
| `AltinnLocaltestOptions.DataDirectory` | Default `~/.altinn-app-aspire`: the shared test certificate in `certs/` (world-readable, as studioctl writes it), the apps' secrets in `apps/<org>/<app>/secrets` |
| `AltinnLocaltestOptions.Groups` | The same as `WithGroup` for each |
| `AltinnLocaltestOptions.PostgresHostPort` | Publishes Postgres on this loopback port, for `psql`; default none, as studioctl |
| `AddAltinnApp<TProject>(localtest, name = "app", configure = null)` | Adds an app project (`Projects.App` for `#:project ../App/App.csproj`): [The app](#the-app) |
| `AddAltinnApp(localtest, projectPath, name = "app", configure = null)` | The same by the `.csproj`'s path, for an AppHost without the generated `Projects.*` |
| `AltinnAppOptions.OpenTelemetry` | Default `true`: `AppSettings__UseOpenTelemetry=true`, so the app's telemetry reaches the dashboard |
| `AltinnAppOptions.ProvisionWhenReady` | Default `true`: register the app when it is healthy; `false` leaves it to the `provision` command |

`OnlyComponents` (21d, for diagnosis: a subset such as Postgres alone) is gone: the README documented no use beyond
diagnosis, and the in-network health checks need Postgres anyway.

## How the manifest becomes resources

Per selected container (`default` and the enabled groups' roots, closed over `dependsOn`, in start order), as
studioctl's loader (`internal/cmd/env/localtestv2/containers.go`) builds it:

| Manifest or runner | Aspire | Differs from studioctl |
| --- | --- | --- |
| the key | `AddContainer(key, image, tag)`, persistent (`ContainerLifetime.Persistent`), a child of the parent resource; Aspire's network alias is the resource name | container names `<key>-<hash of the AppHost's path>` (`storage-0de70ea0`), Aspire's network |
| `registry`, `tag` | registry and image split (`WithImageRegistry`), `WithImageSHA256` for a digest (Aspire then drops the tag: `library/redis@sha256:...`); a floating tag (`tt02`, `latest`) `WithImagePullPolicy(Always)` | studioctl keeps a stale local copy when the registry is unreachable |
| `envs` | `WithEnvironment`, literal | none |
| the `services__<name>__http__0=http://<name>:<port>` of every selected HTTP container | the same, read when the AppHost starts, so a group added later is announced to every container | none |
| the certificate | `<data>/certs` read-only at `/certs`, and its file at `/accesstoken/accesstokencredentials.pfx` | no SELinux relabel (not an Aspire option) |
| `volumes` | `WithVolume(name, path)`, the manifest's names: **shared with studioctl** | none |
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
come from the resources' snapshots. The alternatives the brief named (a loopback port per container for the probe, or
healthy-when-running with only the front door's `/health`) publish ports or lose the start order Postgres and
Dialogporten's migrations need; phase 2 measures the start-up cost of this one.

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
  AppHost starts (byte for byte studioctl's), and `AppSettings__AppFrontendAssetBaseUrl` with `WithFrontendDevServer`.
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
front door keeps the route, and the next start registers it again. Since the front door strips `Secure` from an app's
cookies, the 21e keepAlive gap is closed.

| Resource | Command | What |
| --- | --- | --- |
| the app | `provision` | The `PUT` again (after an edit of `testData.json`, the texts or the policy) |
| the app | `deprovision` | After a confirmation, `DELETE localtest/api/apps/{org}/{app}`: the test users leave the Register, the route the front door |
| `altinn-localtest` | `reset` | After a confirmation, `rm --force` of the containers by ID and `volume rm --force` of the manifest's volumes that exist, through the runtime's CLI; then restart the AppHost. The volumes are studioctl's too, so this resets a studioctl stack's data as well |

## Publishing

`.github/workflows/aspire-package.yml` runs on a push to `main` that changes `localtest/aspire/**`, the manifest, the
certificate or the golden files the tests read, and on `workflow_dispatch`: csharpier, build, test, a build of the
sample, `dotnet pack` as `0.1.0-alpha.<run number>`, the `.nupkg` as an artifact, and `dotnet nuget push` to
`https://nuget.pkg.github.com/<owner>/index.json`. A tag `aspire-v<version>` packs that version and attaches the
`.nupkg` to a GitHub release.

## Tests and parity

`dotnet test tests -c Release` (69 tests). Parity with studioctl means both runners build the same thing from the one
manifest; the package's tests compare what it builds with studioctl's golden snapshots (under
`localtest/studioctl/src/cli/`), which studioctl's own tests hold its loaders to:

| Golden file | This package's test |
| --- | --- |
| `internal/cmd/env/localtestv2/testdata/environment.golden.json`: `env up`, and `--with everything` | `GoldenComparisonTests`: the selection and start order; per container, from the built application model, the image reference (default stack), the environment with the `services__*` variables, the mounts, the published port, the extra hosts, the alias, the dependencies; the health targets and the front door's; the groups' selections and the volume names |
| `internal/cmd/app/testdata/app-environment.golden.json` | `GoldenComparisonTests`: every case (native, inherited variables, ingress port 80, container) from the same inputs |
| `internal/appsecrets/testdata/app-codes.golden.json` | `AppTests`: byte-equal, owner-only, not rewritten when unchanged |

Besides: `ManifestTests` (the embedded file; defaults and start order; every validation of the Go loader: an unknown
key, an unknown or self reference, a duplicate, a cycle, an unreached container, an empty registry or tag, a bad
health, port, env key or volume, no `default` group, trailing data), `ModelTests` (the default, `dialogporten`,
`correspondence` and `everything` selections, the `services__*` of every container and of a group added later, only
8000 published, persistence, parent and health checks, the extra hosts, the mounts, images and the `Tag` option, the
waits, the probe script), `AppTests` (the app's environment, endpoint, waits, commands, the metadata error, the port
guard, the reset's commands), `FrontDoorRegistrationTests` (the `PUT` and `DELETE` against an in-process fake front
door, a `502` and an unreachable front door as a degraded app, the dev-server route) and `GroupMethodsTests`.

`.github/workflows/manifest-parity.yml` runs studioctl's Go tests (which fail on a stale snapshot) and these on every
pull request and push to `main` that touches the manifest, the Go loaders or the package. Not covered: behaviour at run
time (the smoke suites, run by hand against either runtime: [`localtest/README.md`](../../README.md#tests)), and what
differs by design (the health mechanism, `--dev`, pull policies, container names).

## History

Recorded with the package's YARP front door (21d to 21f, 2026-10-22), Podman 5.7 rootful, arm64; what still applies:

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

- Everything at run time with this version (task 27b phase 2): the in-network probes and their start-up cost, the
  registration through the localtest container, the dev-server route, the smoke suites with the registration checks.
- An SELinux host (bind mounts without relabelling), rootless Podman, Docker.
