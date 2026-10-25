# studioctl with the `localtest-v2` runtime

A vendored copy of the studioctl source from `Altinn/altinn-studio`, so the `localtest-v2` runtime can be developed
next to the components it starts and upstreamed later. Pin, copied paths, baseline commit, how to produce the upstream
patch and how to sync: [`UPSTREAM.md`](UPSTREAM.md). The stack's conventions (manifest, front door, service
discovery, ports, certificate, volumes, the app environment): [`../CONVENTIONS.md`](../CONVENTIONS.md). Original design
(a dated record): [`../../planned-components/studioctl-integration.md`](../../planned-components/studioctl-integration.md).

## Layout

The monorepo layout is kept, so relative paths resolve as upstream.

| Path | What |
| --- | --- |
| `src/cli` | studioctl (Go), `studioctl-server` (C#), `studioctl-server-tests`, `common/dotnet`; the Makefile and `.gitignore` live here (`build/`, `bin/`, `obj/` are ignored) |
| `src/Runtime/devenv` | Go module `altinn.studio/devenv`, replaced into `src/cli/go.mod` |
| `src/Runtime/common/dotnet/Altinn.Studio.Runtime.Common` | `HostBridge` and `EnvTopology` sources compiled into `studioctl-server` |
| `src/Runtime/localtest/infra` | Files staged by `make dev-install` |

## Dev loop

```sh
cd localtest/studioctl/src/cli
make dev-install                            # builds build/dev-home/bin/{studioctl,studioctl-server}
export STUDIOCTL_HOME=$PWD/build/dev-home
build/dev-home/bin/studioctl env up -r localtest-v2 [--with <group>] [--without <group>] [--dev-<container>[=<path>]] [--latest] [--minimal] [--host <name>] [--port <n>]
build/dev-home/bin/studioctl env logs [-c <container>]
build/dev-home/bin/studioctl env down        # env reset also deletes the manifest's volumes
```

- `--with <group>` starts an optional group (`dialogporten`, `receipt`, `correspondence`, `arbeidsflate`, or
  `everything`): its root containers and what they depend on. `--without <group>` leaves out a group's own roots; a
  container another enabled group depends on still starts. The groups are in the manifest; `everything` is a constant.
- `--dev-<container>[=<path>]` builds the container's image from a Dockerfile, or a
  directory holding `Dockerfile`, default the current directory, tags it `localtest-v2-dev-<container>` and runs it
  instead of the pinned image. A Dockerfile inside the current directory builds with the current directory as context
  (`cd localtest && … --dev-postgres=postgres/Dockerfile`), one elsewhere with its own directory. Example:
  `cd localtest/components/storage && studioctl env up -r localtest-v2 --dev-storage`.
- `--latest` runs the research images at `latest` instead of their `sha-` pin.
- `--host <name>` and `--port <n>` move the public origin (default `http://local.altinn.cloud:8000`, the manifest's
  `public` section): the front door's published port, every `{{origin}}` value, the containers' host-gateway names
  (`<name>` and its `pdf.`, `workflow-engine.`, `app-frontend.`, `af.` subdomains). `--port 0` picks a free port. The
  state file records both; `env status` prints `Origin:`, and `env down`, `run`, `app env` and studioctl-server use the
  recorded origin. A plain `env up` keeps it; `env up` with other values stops the environment and starts it at the new
  origin. A custom host must resolve to loopback (the user's own `/etc/hosts` line; `env hosts add` writes the default
  names only): `env up` refuses with the line to add otherwise.
- `env logs -c <container>` shows the container's log also when it has stopped (the shared
  `internal/cmd/env/stack/log_streamer.go`). `make dev-install` runs `studioctl self install`, which stops every
  environment and app first.

Gotcha: `STUDIOCTL_HOME` must be short. studioctl-server listens on a Unix socket under it, and a socket path over
107 characters fails `env up` with "invalid argument"; a worktree under `/home/agent/code/.worktrees/<task>/localtest/studioctl/src/cli/build/dev-home`
is too long, so point a short symlink (for example `/home/agent/<task>-home`) at `build/dev-home` and use that.

Gotcha: only one runtime runs at a time. `env up` refuses to start one while the other has a running container
(`envtypes.EnsureNoOtherActive`), whatever its port, and an omitted `-r` means the runtime that is running.

## The manifest

[`src/cli/internal/localtestv2/manifest/manifest.json`](src/cli/internal/localtestv2/manifest/manifest.json) (format:
its [README](src/cli/internal/localtestv2/manifest/README.md)) lists the public origin's defaults (`public`), the containers (registry, tag, `envs` with the origin's placeholders,
`dependsOn`, named `volumes`, optional `port` and `health`) and the groups (root containers). Everything else is the
loader's: network, container names, aliases, host mappings, the certificate mounts, the `services__<name>__http__0`
variables, the one published port, health, dev builds. studioctl embeds the file. Its test validates it; the golden
snapshot `internal/cmd/env/localtestv2/testdata/environment.golden.json` pins what the loader builds from it
(`STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/` regenerates it). The pin moves only with
`localtest/tools/bump-tag.sh sha-<commit>`. CI: [`.github/workflows/manifest-parity.yml`](../../.github/workflows/manifest-parity.yml).

To add a container: add it to `manifest.json` and to a group's roots or another container's `dependsOn`, its image to
`.github/workflows/images.yml`, its database init script to `localtest/postgres/Dockerfile`; then run `go test ./...`
under `src/cli` and regenerate the snapshot. No Go change is needed.

## Not mergeable yet

The code under `src/` is written for the monorepo: no task numbers and no relative links out of `src/`. What still
names this repository, each by absolute URL or image reference, and must change before
[altinn-studio#21394](https://github.com/Altinn/altinn-studio/pull/21394) can merge:

| Where (under `src/cli/`) | What | Before merging |
| --- | --- | --- |
| `internal/localtestv2/manifest/manifest.json`, its README's example, `internal/cmd/env/localtestv2/testdata/environment.golden.json` | The research images, `ghcr.io/ivarne-agent/localtest-research/localtest-*` | Images built and published from the monorepo |
| `internal/localtestv2/manifest/manifest_internal_test.go` (`redisImage`, `TestTheResearchImagesShareOnePin`), `internal/cmd/env/localtestv2/loader_internal_test.go` (`--latest`) | The same registry prefix in test code | Follows the images |
| `internal/localtestv2/manifest/README.md` ("Tags", "The certificate", "Changing it", the Postgres and Redis images), `AGENTS.md` | Links to `bump-tag.sh`, the certificate's README, the Postgres and Redis Dockerfiles, the Aspire package and `manifest-parity.yml` | Follows the images' sources and tooling |
| `internal/cmd/env/localtestv2/doc.go`, `certs.go` | Where the components are built; the test key pair's origin | Follows the images' sources |
| `internal/appsecrets/appcodes_golden_test.go`, `internal/cmd/app/env_golden_internal_test.go` | The Aspire package that compares against these goldens | The Aspire package's home |
| `README.md` ("localtest-v2") | The research repository as the components' source, `--latest`, a `--dev-<container>` example | Follows the images' sources |

The conventions the code depends on (manifest, tags, service names, port and health, the certificate, volumes) are
stated in `src/cli/internal/localtestv2/manifest/README.md`; [`../CONVENTIONS.md`](../CONVENTIONS.md) points there for
those.

## What the localtest-v2 change adds

Paths below are under `src/cli/`.

| Where | What |
| --- | --- |
| `internal/localtestv2/manifest/` | The manifest (`manifest.json`, `README.md`) and its reader and validation (`manifest.go`): known keys, references, no cycles, every container reached by a group, values in range; the groups' transitive selection in start order. `origin.go`: the `public` section, the five placeholders (`{{origin}}`, `{{host}}`, `{{port}}`, `{{origin:<sub>}}`, `{{host:<sub>}}`), their validation and resolution for a host and port |
| `internal/cmd/env/localtestv2/` | The runtime. `containers.go`: one container per selected manifest entry, in dependency order, alias its name, the public hosts and `host.docker.internal` on the host gateway, the certificate at `/certs`, its `envs` then `services__*` for every running HTTP container, the public port (8000, `--port`) published from `localtest` only (all interfaces, so the containers reach it through the gateway). `groups.go`: `--with`/`--without`/`everything`. `dev.go`: `--dev-<container>` paths. `apps.go`: every teardown removes the container-mode apps (label `altinn.studio/localtest-v2=app`) before the network. `health.go`: each container's health (HTTP path or TCP) checked from a short-lived probe container on the stack's network (the manifest's Postgres image, bash `/dev/tcp`), created through the container client and gating what depends on it; then the front door's `/health` through the public origin. `origin.go`: `--host`/`--port`, the origin a plain `env up` keeps, `--port 0`'s free port, a custom host's resolution check. `env.go`: up, down, reset (the manifest's volumes), logs, status, all on the shared `internal/cmd/env/stack` (apply, destroy, status snapshot, logs), with nothing imported from the localtest runtime's package. `certs.go`: the embedded test certificate |
| `../Runtime/devenv/pkg/resource` | `ContainerLifecycleOptions.ReadinessProbe`: a check the executor runs once a container is ready and before its dependents start. `executor/container/running.go`: `GraphRunning`, whether a container with a graph's ownership label runs, which `stack.Active` (every runtime's `Env.Active`) and `localtestv2.Current` share |
| `internal/cmd/env/localtestv2/runtime.go` | The runtime's own `env up` flags (`--with`, `--without`, `--latest`, `--minimal`, `--host`, `--port`, `--dev-<container>[=<path>]` for every manifest container) and their help, bound through the `envtypes.Runtime` interface; `UpOptions.DevComponents` maps container to path. `env up` parses `-r` first and then only the chosen runtime's flags, so another runtime's flag is refused by name |
| `internal/cmd/env/active.go`, `registry/` | `-r <runtime>` with one runtime at a time: `ResolveRuntime` defaults to the runtime with a running container (each runtime's `Env.Active`, from its devenv graph label), else localtest |
| `internal/cmd/env/stack/log_streamer.go`, `internal/cmd/env/status.go` | The log streamer shows a stopped container's log; the status payload carries localtest-v2's origin, mode and emulated containers |
| `internal/envtopology/` | The `arbeidsflate` host `af.local.altinn.cloud` (`env hosts add`); `NewLocalAt`, the public URLs at another host |
| `internal/localtestv2/state.go` | The state file: the configuration the stack was started with (certificate directory, public host and port, enabled groups, `--latest`, `--minimal`), never the evidence that it runs. `Current` is the one answer outside the runtime: the recorded configuration, and whether the stack runs, which is a running container with the runtime's graph label (`GraphID`, as `Env.Active` checks); without a state file it does not ask the container runtime, and a file without running containers (a reboot, a `docker rm`) is `Stale` and counts as down. `Environment.Topology` gives `run`, `app env` and the dev-server route the recorded origin while the stack runs. Inside the runtime the file is read as the started configuration: `env up` on a running environment converges it when the groups, the origin or `--latest` differ |
| `internal/cmd/app/{env,config,run}.go` | The app environment (runner code, pinned by `testdata/app-environment.golden.json`; `studioctl app env` prints it) with the certificate variables and the Arbeidsflate links (`PlatformFrontendSettings__Arbeidsflate*Url`) while localtest-v2 runs (`localtestv2.Current`, through the service's container client); the app then listens on `0.0.0.0`, because the front door reaches it as `host.docker.internal:<port>`. `run --mode container` (`onStackNetwork`): the container on the stack's network `localtest-v2_network` under the alias `<org>-<app>`, the public host names on the host gateway, the certificate at `/certs`, Kestrel on `0.0.0.0:5005`, its loopback port for discovery and the label `altinn.studio/front-door-url`; refused at a custom origin |
| `internal/localtestv2/frontdoor/` | The client of the front door's API: `GET localtest/api/apps`, waiting for an app's registration, `PUT`/`DELETE localtest/api/routes/frontend-dev-server` |
| `internal/cmd/run_frontdoor.go`, `run.go` | `studioctl run` goes on without localtest-v2 when the stack's containers are gone, and stops before it builds the app when they run but its front door does not answer, with the fact and `env up -r localtest-v2` / `env down -r localtest-v2` (`runTopology`); otherwise, while the stack runs, it waits until the front door lists the app at `http://host.docker.internal:<port>` (a container: at its alias on the stack's network) with a provisioning that succeeded since the run started (and says so when the front door does not answer); with `--dev-frontend` it routes `app-frontend.<public host>` to `http://host.docker.internal:8080` for the run's length, in process and container mode |
| `internal/studioctlserver/client.go` | While localtest-v2 runs (`localtestv2.Current`, a container check whenever there is a state file), studioctl-server runs with `Localtest__RegisterApps=true`, the front door at the recorded port and without the host bridge; every start and reconcile decides it afresh, so a stale state file starts the server as without localtest-v2. `env up` restarts it once the containers are healthy, `env down` reconciles it |
| `studioctl-server/Discovery/FrontDoorRegistration.cs`, `AppRegistry.cs`, `ServiceCollectionExtensions.cs`, `Studioctl/Endpoints.cs` | Discovery registers each app it finds (a `studioctl run`'s or one started from an IDE) with `PUT {Localtest:Url}localtest/api/apps/{org}/{app}` and the address `http://host.docker.internal:<port>` (a container that discovery reports with a `frontDoorUrl`, from its `altinn.studio/front-door-url` label: that address; `Container/ContainerDiscovery.cs`), logs the provisioning result, retries a failure on the next refresh, registers again an app that restarted (another process or container) or that the front door's `GET localtest/api/apps` no longer lists, and `DELETE`s an app it registered that is gone. These calls run beside discovery, at most one per app, so a slow front door delays only that app, and an app's start completes only after its own registration has an outcome; the status endpoint reports `localtest.registerApps`. The front door, the 503 pages and the provisioning are the localtest image's (`../../localtest/README.md`) |
| `studioctl-server-tests/`, `*_test.go` | Tests of the above |
| `README.md`, `AGENTS.md`, `CHANGELOG.md`, the bundled skill | Docs |
