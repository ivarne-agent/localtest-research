# studioctl with the `localtest-v2` runtime

A vendored copy of the studioctl source from `Altinn/altinn-studio`, so the `localtest-v2` runtime can be developed
next to the components it starts and upstreamed later. Pin, copied paths, baseline commit, how to produce the upstream
patch and how to sync: [`UPSTREAM.md`](UPSTREAM.md). Design: [`../../planned-components/studioctl-integration.md`](../../planned-components/studioctl-integration.md).

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
export STUDIOCTL_LOCALTEST_V2_SRC=<this checkout>/localtest
build/dev-home/bin/studioctl env up -r localtest-v2 [--dev <component>|all] [--with <group>] [--without <group>] [--latest]
build/dev-home/bin/studioctl env logs
build/dev-home/bin/studioctl env down
```

`--dev` builds the named component (or all) from `localtest/components/<name>` instead of pulling the pinned image;
`--with <group>` starts an optional component group and, transitively, the groups it implies; `--without <group>` leaves
out only the named group. The groups are a table in `groups.go` (`groupTable`): each has a default (all off today) and the
groups it implies.

| Group | Implies | Components |
| --- | --- | --- |
| `dialogporten` | – | Redis, the three Dialogporten hosts and the Dialogporten adapter; `--dev dialogporten` builds the three host images from `components/dialogporten`, `--dev dialogporten-adapter` the adapter from `components/dialogporten-adapter` |
| `receipt` | – | the platform receipt (task 16) |
| `correspondence` | `dialogporten` | none yet (task 7) |
| `everything` | every other group | none of its own |

Leaving out a group that an enabled group implies is an error, because the enabled group cannot run without it:
`--with correspondence --without dialogporten` and `--with everything --without receipt` both fail and say which group
needs which. A group without components is harmless: it is recorded in the state file and adds no containers or routes.
`--latest` follows the published `latest` images. `make dev-install` runs `studioctl self install`, which stops every
environment and app first.

Gotcha: `STUDIOCTL_HOME` must be short. studioctl-server listens on a Unix socket under it, and a socket path over
107 characters fails `env up` with "invalid argument"; a worktree under `/home/agent/code/.worktrees/<task>/localtest/studioctl/src/cli/build/dev-home`
is too long, so point a short symlink (for example `/home/agent/<task>-home`) at `build/dev-home` and use that.

Gotcha: only one `studioctl-server` can own port 8000. A dev-home server started from another checkout must be stopped
first, with `studioctl env down` from that home or by killing its `studioctl-server` process.

## What the localtest-v2 change adds

Paths below are under `src/cli/`.

| Where | What |
| --- | --- |
| `internal/cmd/env/localtestv2/` | The runtime: component specs (`components.go`), the optional component groups (`groups.go`: the group table with defaults and implied groups; a component with a `group` exists only when that group is enabled; the enabled groups are recorded in the state file, so `env status` and `env logs` cover what was started and `env down` and `env reset` always cover every group), start/stop/reset (`env.go`), HTTP health polling (`health.go`), topology (`topology.go`), the embedded test certificate (`certs.go`, `assets/localtest-accesstoken.pfx`) |
| `internal/cmd/env/runtime.go`, `registry/`, `types.go`, `env.go` | `-r <runtime>` with one runtime at a time, defaulting to the last one started |
| `internal/config/images.go` | `ghcr.io/ivarne-agent/localtest-research/localtest-<name>` images pinned by `sha-<commit>` (`tagLocaltestV2`), `--latest` |
| `internal/localtestv2/state.go` | The state file both studioctl and studioctl-server read |
| `internal/cmd/app/{env,run}.go`, `internal/studioctlserver/client.go` | App environment for the real components (platform token certificate), provisioning call on `studioctl run` |
| `studioctl-server/Proxy/` | The front door: routes component prefixes, the PDF and workflow-engine hosts, falls back to the localtest app; keeps `Host`, strips `Secure`. A route with the metadata `rewritePrefix` (`route.rewrite` in `components.go`) is forwarded with its matched prefix replaced by that path and the rest kept (`PathRewrite.cs`): `/dialogporten/graphql[/...]` reaches Dialogporten's GraphQL host as `/graphql[/...]`, as APIM does in production. The longest matching prefix wins, so it needs no ordering against `/dialogporten`, and the Dialogporten adapter's `/storage/dialogporten` wins over Storage's `/storage`. `env up` writes the routes of every component, those of an optional group that is off disabled and with the group in the route metadata (`group`); a request on a disabled route answers `503` with RFC 9457 problem details (`title` "Component not running", `detail` with the `studioctl env up -r localtest-v2 --with <group>` command, `instance` the path, extension members `component` and `group`), and an enabled component that refuses the connection or times out answers `503` "Component not answering" pointing at `studioctl env logs -r localtest-v2 -c <component>` and `env status` (`ComponentUnavailable.cs`); a request whose `Accept` ranks `text/html` above JSON gets the same content as a self-contained HTML page. A path that matches no route still reaches the localtest app |
| `studioctl-server/Provisioning/AppProvisioner.cs` | The deploy step: metadata, texts, policy, `testData.json` pushed to the components with the Studio token, Access Management refresh, then, when the route table has the `dialogporten-webapi` route enabled (the `dialogporten` group is up), Dialogporten's `localtest/sync` (logged as skipped otherwise, retried on the next refresh when it fails or the group comes up later); register `DELETE` on app stop |
| `README.md`, `AGENTS.md`, `CHANGELOG.md`, the bundled skill | Docs |
