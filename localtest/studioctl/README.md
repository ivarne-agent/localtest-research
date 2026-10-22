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
| `src/Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning` | The app provisioning steps as a library (`src/`, `tests/`, [README](src/Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning/README.md)), a project reference of `studioctl-server` and, later, of the Aspire hosting package; in `studioctl.slnx` |
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

`env logs` streams the running containers; `env logs -c <component>` shows the named component's log even when its
container has stopped (changed 2026-10-21 in the shared `internal/cmd/env/localtest/log_streamer.go`, which also serves
the localtest runtime), so the front door's "Component not answering" hint works for a container that exited.

`--dev` builds the named component (or all) from `localtest/components/<name>` instead of pulling the pinned image;
`--with <group>` starts an optional component group and, transitively, the groups it implies; `--without <group>` leaves
out only the named group. The groups are defined in the component manifest (below): each has a default (all off today)
and the groups it implies; `groups.go` resolves the flags against them.

| Group | Implies | Components |
| --- | --- | --- |
| `dialogporten` | – | Redis, the three Dialogporten hosts (host ports 8030 to 8032, routes `/dialogporten` and `/dialogporten/graphql`) and the Dialogporten adapter (host port 8035, route `/storage/dialogporten`); `--dev dialogporten` builds the three host images from `components/dialogporten`, `--dev dialogporten-adapter` the adapter from `components/dialogporten-adapter` |
| `receipt` | – | the platform receipt (`components/receipt`, host port 8034, route `/receipt`) |
| `correspondence` | `dialogporten` | Correspondence (host port 8033, route `/correspondence`, volume `localtest-v2-correspondence-blobs`, Dialogporten's Redis database 1); `--dev correspondence` builds it from `components/correspondence` |
| `arbeidsflate` | `dialogporten` | Arbeidsflate, the Altinn inbox: the SPA's nginx (host port 8037) on its own ingress host `af.local.altinn.cloud` (the whole host is one route; the topology's `arbeidsflate` component, so `env hosts add` writes it and every container maps it to the host gateway) and the BFF behind it (host port 8038, network alias `arbeidsflate-bff`, no route; database `arbeidsflate`, Dialogporten's Redis database 2); `--dev arbeidsflate` builds both images from `components/arbeidsflate` (`Dockerfile.frontend`, `Dockerfile.bff`) |
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

## The component manifest

Every container of the stack is described in one file, the component manifest
[`src/cli/internal/localtestv2/manifest/manifest.json`](src/cli/internal/localtestv2/manifest/manifest.json), with a
JSON Schema (2020-12) beside it and a [README](src/cli/internal/localtestv2/manifest/README.md) with the placeholder
vocabulary and the rules for adding a component or a group. It holds each component's image, dev build, ports, health
path and container health check, environment, command, mounts, network aliases, dependencies (the start order), group
and front-door routes; the optional groups; the image pin, the override variable rule and the third-party and core
images; the network, container prefix, volumes, shared certificate and Postgres init scripts; and the front door's
routes to the host (the frontend dev server). It is the only copy: studioctl embeds it (`go:embed`), and other
runtimes, such as an Aspire AppHost, can read the same file.

It lives under `internal/localtestv2/` rather than next to the runtime in `internal/cmd/env/localtestv2/`, because
`internal/config/images.go` reads the image pin from it and `internal/config` must not import from `internal/cmd`.

What studioctl does with it:

- `manifest.Parse` decodes it (unknown fields refused) and checks what the schema cannot: unique names, references to
  components, groups, volumes and images, no dependency cycle, a component depends only on components that run whenever
  it does, and every placeholder is known. `manifest.MustDefault` parses the embedded file once.
- `internal/cmd/env/localtestv2/components.go` is a loader: it turns each manifest component into the runtime's
  container spec, resolving images against the image configuration (pin, `--latest`, overrides), placeholders against
  the topology, and the certificate mounts against the host's certificate directory. `groups.go` takes the group table,
  `topology.go` the front door's routes and `health.go` the front door's health path from it; `env reset` deletes its
  volumes.
- `studioctl run` and `studioctl app env` take the app's environment from the manifest's `app` section
  (`internal/cmd/app/env.go` is its loader): the platform endpoints, the host name and base URL, the OpenID well-known
  endpoint, `STUDIOCTL_APP_RUN`, the secrets variables and, while `localtest-v2` is up, the certificate variables with
  the runner's `{{appCertPath}}` and `{{appCertKeysFolder}}`. The Kestrel URL, `--dev-frontend`'s asset base URL, the
  OpenTelemetry endpoint and the keys directory stay studioctl's own. The Aspire package
  (`localtest/aspire/Altinn.App.Aspire`) reads the same section; `internal/cmd/app/testdata/app-environment.golden.json`
  pins the result (taken before the section existed, so the move changed no variable) and
  `internal/appsecrets/testdata/app-codes.golden.json` the development app codes both runners write.
- `internal/config/images.go` takes the research images, the pin (`images.tag`), the tag variable
  (`STUDIOCTL_LOCALTEST_V2_TAG`) and the per-image override variables (`STUDIOCTL_IMAGE_LOCALTEST_V2_<NAME>`, the image
  name upper-cased with `-` as `_`) from it.
- Tests: the manifest validates against its schema (a small in-test validator that refuses keywords it does not know),
  every schema node has a title and a description, `Parse` refuses broken manifests, the Postgres init scripts match
  what `localtest/postgres/Dockerfile` copies and every dev build's Dockerfile exists (both skipped outside this
  repository), and `testdata/environment.golden.json` pins the loader's output (the resource graph, route table and
  health targets of the default `env up` and of `--with everything --dev all`). The golden file was generated while a
  transition test showed the loader building exactly what the hand-written component table it replaced built, for 2268
  option combinations; the commit that removed the table also removed that test.
- CI: [`.github/workflows/manifest-parity.yml`](../../.github/workflows/manifest-parity.yml) runs `go build ./...` and
  `go test ./...` here on every pull request that touches the manifest, the loaders, the provisioning library or the
  Aspire package, so a manifest change without a regenerated golden file fails it; the same workflow runs the Aspire
  package's tests, which compare its C# reader with these golden files, the provisioning library's tests and
  `python3 -m jsonschema` on the manifest.

To check the manifest by hand with a full JSON Schema validator:

```sh
cd src/cli/internal/localtestv2/manifest
python3 -m venv /tmp/venv && /tmp/venv/bin/pip install jsonschema
/tmp/venv/bin/python -m jsonschema -i manifest.json manifest.schema.json   # silent when valid
```

To add a component: add it to `manifest.json` (the manifest's README says what each field needs), its image to
`.github/workflows/images.yml` and, if it has a database, its init script to `localtest/postgres/Dockerfile` and
`postgres.initScripts`; run `go test ./...` under `src/cli`, review the diff of the golden file the failing test reports
and regenerate it with `STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/`. No Go change is needed unless
it needs something the manifest cannot express yet, such as a new placeholder or a new topology host.

## What the localtest-v2 change adds

Paths below are under `src/cli/`.

| Where | What |
| --- | --- |
| `internal/localtestv2/manifest/` | The component manifest (`manifest.json`, `manifest.schema.json`, `README.md`) and its parser and checks (`manifest.go`, `check.go`, `placeholders.go`, `app.go` for the `app` section) |
| `internal/cmd/env/localtestv2/` | The runtime: the manifest loader (`components.go`), the optional component groups (`groups.go`: the manifest's group table with defaults and implied groups; a component with a `group` exists only when that group is enabled; the enabled groups are recorded in the state file, so `env status` and `env logs` cover what was started and `env down` and `env reset` always cover every group), start/stop/reset (`env.go`), HTTP health polling (`health.go`), topology (`topology.go`), the embedded test certificate (`certs.go`, `assets/localtest-accesstoken.pfx`) |
| `internal/cmd/env/runtime.go`, `registry/`, `types.go`, `env.go` | `-r <runtime>` with one runtime at a time, defaulting to the last one started |
| `internal/cmd/env/localtest/env.go`, `log_streamer.go` | The localtest runtime refuses to start while localtest-v2 holds the port; the log streamer is shared with localtest-v2 and, for `env logs -c <component>`, shows a stopped container's log |
| `internal/envtopology/` (`topology.yaml`, `model.go`, `validate.go`, `components.go`, `topology.go`) | The `arbeidsflate` component, host `af.local.altinn.cloud`: one of `LocaltestIngressHosts` (mapped to the host gateway in every container, which Authentication's server-side front-channel logout needs) and of `HostFileHostnames` (`env hosts add`); `ArbeidsflateHostName()`. The localtest runtime's containers get the mapping too, which is harmless |
| `internal/config/images.go` | `ghcr.io/ivarne-agent/localtest-research/localtest-<name>` images pinned by `sha-<commit>` (the manifest's `images.tag`), `--latest`, the override variables by rule |
| `internal/localtestv2/state.go` | The state file both studioctl and studioctl-server read |
| `internal/cmd/app/{env,run}.go`, `internal/studioctlserver/client.go`, `internal/localtestv2/topology.go` | App environment for the real components from the manifest's `app` section (platform token certificate), provisioning call on `studioctl run` |
| `studioctl-server/Proxy/` | The front door: routes component prefixes, the PDF and workflow-engine hosts, falls back to the localtest app; keeps `Host`. A platform component's `Set-Cookie` passes unchanged (the components set no `Secure` cookie over plain http, task 21b, `../reviews/cookies.md`); only on an app's route (component `app`) is `Secure` still removed, because app-lib's `keepAlive` sets the runtime cookie `Secure` in every environment (`PlatformTransformer.cs`). A route with the metadata `rewritePrefix` (a route's `rewrite` in the manifest) is forwarded with its matched prefix replaced by that path and the rest kept (`PathRewrite.cs`): `/dialogporten/graphql[/...]` reaches Dialogporten's GraphQL host as `/graphql[/...]`, as APIM does in production. The longest matching prefix wins, so it needs no ordering against `/dialogporten`, and the Dialogporten adapter's `/storage/dialogporten` wins over Storage's `/storage`. `env up` writes the routes of every component, those of an optional group that is off disabled and with the group in the route metadata (`group`); a request on a disabled route answers `503` with RFC 9457 problem details (`title` "Component not running", `detail` with the `studioctl env up -r localtest-v2 --with <group>` command, `instance` the path, extension members `component` and `group`), and an enabled component that refuses the connection or times out answers `503` "Component not answering" pointing at `studioctl env logs -r localtest-v2 -c <component>` and `env status` (`ComponentUnavailable.cs`); a request whose `Accept` ranks `text/html` above JSON gets the same content as a self-contained HTML page. A path that matches no route still reaches the localtest app |
| `studioctl-server/Provisioning/AppProvisioner.cs`, `Discovery/AppRegistry.cs`, `../Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning/` | The deploy step, run for each discovered app before its readiness check. The steps live in the shared library (`LocaltestProvisioner` with a `RunningAppSource` reading the app's own API): `testData.json` to the Register, metadata and texts to Storage and the policy to the PDP with the Studio token, the Resource Registry's subject reload for the policy, Access Management refresh, then, when the route table has the `dialogporten-webapi` route enabled (the `dialogporten` group is up), Dialogporten's `localtest/sync` (logged as skipped otherwise, retried on the next refresh when it fails or the group comes up later); register `DELETE` on app stop. `AppProvisioner` keeps the reconcile loop over the discovered apps and the route-table check |
| `studioctl-server/Program.cs`, `Studioctl/Endpoints.cs`, `studioctl-server.csproj`, `studioctl.slnx`, `Directory.Packages.props`, `src/Runtime/common/.../EnvTopology/BoundTopologyMetadataKeys.cs` | Wiring: the proxy (YARP 2.3.0) and the provisioner registered, the provisioning library referenced and in the solution, the proxy's status in the server's status endpoint, the route metadata keys `group` and `rewritePrefix` |
| `studioctl-server-tests/`, `*_test.go` | Tests of the above |
| `README.md`, `AGENTS.md`, `CHANGELOG.md`, the bundled skill | Docs |
