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
build/dev-home/bin/studioctl env up -r localtest-v2 [--with <group>] [--without <group>] [--dev-<container>[=<path>]] [--latest]
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
- `env logs -c <container>` shows the container's log also when it has stopped (the shared
  `internal/cmd/env/localtest/log_streamer.go`). `make dev-install` runs `studioctl self install`, which stops every
  environment and app first.

Gotcha: `STUDIOCTL_HOME` must be short. studioctl-server listens on a Unix socket under it, and a socket path over
107 characters fails `env up` with "invalid argument"; a worktree under `/home/agent/code/.worktrees/<task>/localtest/studioctl/src/cli/build/dev-home`
is too long, so point a short symlink (for example `/home/agent/<task>-home`) at `build/dev-home` and use that.

Gotcha: only one runtime can own port 8000: the localtest runtime's container, or the localtest-v2 `localtest`
container.

## The manifest

[`src/cli/internal/localtestv2/manifest/manifest.json`](src/cli/internal/localtestv2/manifest/manifest.json) (format:
its [README](src/cli/internal/localtestv2/manifest/README.md)) lists the containers (registry, tag, literal `envs`,
`dependsOn`, named `volumes`, optional `port` and `health`) and the groups (root containers). Everything else is the
loader's: network, container names, aliases, host mappings, the certificate mounts, the `services__<name>__http__0`
variables, the one published port, health, dev builds. studioctl embeds the file. Its test validates it; the golden
snapshot `internal/cmd/env/localtestv2/testdata/environment.golden.json` pins what the loader builds from it
(`STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/` regenerates it). The pin moves only with
`localtest/tools/bump-tag.sh sha-<commit>`. CI: [`.github/workflows/manifest-parity.yml`](../../.github/workflows/manifest-parity.yml).

To add a container: add it to `manifest.json` and to a group's roots or another container's `dependsOn`, its image to
`.github/workflows/images.yml`, its database init script to `localtest/postgres/Dockerfile`; then run `go test ./...`
under `src/cli` and regenerate the snapshot. No Go change is needed.

## What the localtest-v2 change adds

Paths below are under `src/cli/`.

| Where | What |
| --- | --- |
| `internal/localtestv2/manifest/` | The manifest (`manifest.json`, `README.md`) and its reader and validation (`manifest.go`): known keys, references, no cycles, every container reached by a group, values in range; the groups' transitive selection in start order |
| `internal/cmd/env/localtestv2/` | The runtime. `containers.go`: one container per selected manifest entry, in dependency order, alias its name, the public hosts and `host.docker.internal` on the host gateway, the certificate at `/certs`, its `envs` then `services__*` for every running HTTP container, port 8000 published from `localtest` only (all interfaces, so the containers reach it through the gateway). `groups.go`: `--with`/`--without`/`everything`. `dev.go`: `--dev-<container>` paths. `health.go`: each container's health (HTTP path or TCP) checked from a short-lived probe container on the stack's network (the manifest's Postgres image, bash `/dev/tcp`), created through the container client and gating what depends on it; then the front door's `/health` through port 8000. `env.go`: up, down, reset (the manifest's volumes), logs, status. `certs.go`: the embedded test certificate |
| `../Runtime/devenv/pkg/resource` | `ContainerLifecycleOptions.ReadinessProbe`: a check the executor runs once a container is ready and before its dependents start |
| `internal/cmd/env.go`, `internal/cmd/env/types.go` | The flags (`--with`, `--without`, `--latest`, `--dev-<container>[=<path>]` for every manifest container); `UpOptions.DevComponents` maps container to path; `env up` refuses the flags the chosen runtime does not have |
| `internal/cmd/env/runtime.go`, `registry/` | `-r <runtime>` with one runtime at a time, defaulting to the last one started |
| `internal/cmd/env/localtest/env.go`, `log_streamer.go` | The localtest runtime refuses to start while localtest-v2 holds the port; the log streamer shows a stopped container's log |
| `internal/envtopology/` | The `arbeidsflate` host `af.local.altinn.cloud` (`env hosts add`, the containers' host mappings) |
| `internal/localtestv2/state.go` | The state file (certificate directory, enabled groups, `--latest`) studioctl and studioctl-server read; `env up` on a running environment converges it when the groups or `--latest` differ |
| `internal/cmd/app/{env,config,run}.go` | The app environment (runner code, pinned by `testdata/app-environment.golden.json`; `studioctl app env` prints it) with the certificate variables and the Arbeidsflate links (`PlatformFrontendSettings__Arbeidsflate*Url`) while localtest-v2 is up; the app then listens on `0.0.0.0`, because the front door reaches it as `host.docker.internal:<port>`. `run --mode container` is refused while localtest-v2 is up (the app container would join the localtest runtime's network) |
| `internal/localtestv2/frontdoor/` | The client of the front door's API: `GET localtest/api/apps`, waiting for an app's registration, `PUT`/`DELETE localtest/api/routes/frontend-dev-server` |
| `internal/cmd/run_frontdoor.go`, `run.go` | `studioctl run` waits until the front door lists the app at `http://host.docker.internal:<port>` with a provisioning that succeeded since the run started (and says so when the front door does not answer); with `--dev-frontend` it routes `app-frontend.local.altinn.cloud` to `http://host.docker.internal:8080` for the run's length |
| `internal/studioctlserver/client.go` | While localtest-v2 is up, studioctl-server runs with `Localtest__RegisterApps=true` and without the host bridge; `env up`/`down` restart it when that changes |
| `studioctl-server/Discovery/FrontDoorRegistration.cs`, `AppRegistry.cs`, `ServiceCollectionExtensions.cs`, `Studioctl/Endpoints.cs` | Discovery registers each app it finds (a `studioctl run`'s or one started from an IDE) with `PUT {Localtest:Url}localtest/api/apps/{org}/{app}` and the address `http://host.docker.internal:<port>`, logs the provisioning result, retries a failure on the next refresh, registers again an app that restarted (another process or container) or that the front door's `GET localtest/api/apps` no longer lists, and `DELETE`s an app it registered that is gone; the status endpoint reports `localtest.registerApps`. The front door, the 503 pages and the provisioning are the localtest image's (`../../localtest/README.md`) |
| `studioctl-server-tests/`, `*_test.go` | Tests of the above |
| `README.md`, `AGENTS.md`, `CHANGELOG.md`, the bundled skill | Docs |
