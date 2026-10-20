# Options: integrating the new localtest into studioctl

Status: decision material, 2026-10-20. [`studioctl-integration.md`](studioctl-integration.md) is a single-path plan;
this note lists the alternatives at each decision point, with the trade-offs, so the path can be chosen deliberately.
Evidence: `src/cli` and `src/Runtime/localtest` in altinn-studio `89b1d7bc70` (no changes since the `447e53eb`
analysed in [`../localtest-components/studioctl-and-app-contract.md`](../localtest-components/studioctl-and-app-contract.md)),
and the stack in [`../localtest/`](../localtest/).

Facts that shape every option:

- studioctl has no compose file. Containers are `devenv` resources applied through the Docker Engine API (Podman via
  its compatibility socket); `env status`, `env logs`, `env reset` and `doctor` all work from that manifest.
- Apps started by `studioctl run` bind Kestrel to `127.0.0.1:0`. The proxy in the localtest container reaches them
  through studioctl-server's WebSocket host bridge, not through `host.docker.internal`, which on Linux cannot reach
  loopback-bound ports. `run-app.sh` binds apps to `0.0.0.0:<port>` for that reason.
- Today's localtest pulls each app's metadata, texts, policy and `testData.json` from the running app. The
  production components need them pushed (the four calls `run-app.sh` makes).
- The localtest image follows `main` while a developer's studioctl is older: new container names, ports or settings
  must not break an older studioctl, and the old image must keep being published while old studioctl versions are
  supported.

## 1. How studioctl starts the stack

| Option | What | For | Against |
| --- | --- | --- | --- |
| **1a. One manifest entry per component** (the plan) | A `register<Name>Components` file per container, as pdf3 and the workflow engine have | Status, logs, reset, dev-mode builds and image following work per component with no new mechanism; the executor handles ordering | The most code: ~8 component files, ~8 image specs, 4 volumes, a migration, CI image cache entries |
| 1b. One stack image | The localtest app and the six components in one image under a process supervisor; Postgres stays a separate container (the workflow engine's today) | Smallest studioctl change: swap the image, rename a few settings, add the shared Postgres | A multi-GB image rebuilt for any change, one log stream, one health, no per-component dev rebuild or pin; against the grain of the devenv model |
| 1c. Shell out to `compose` | Embed `docker-compose.yml`, run `podman compose`/`docker compose` | Reuses the file that is tested today | A new runtime dependency (compose plugin or podman-compose), and status/logs/reset would need a second code path; the executor talks to the engine API, not to compose |
| 1d. Pod spec (`podman kube play`) | Generate a Kubernetes pod YAML for the stack | One unit, shared network namespace, kind-compatible later | Not supported by the devenv executor; Docker has no equivalent |

1a is the only one that fits the existing tooling without a second mechanism. 1b is the fallback if integration
effort is the constraint, at the cost of everything studioctl does per container.

## 2. Coexistence with today's localtest

| Option | What | For | Against |
| --- | --- | --- | --- |
| 2a. Flag inside the `localtest` runtime (the plan) | `env up --localtest v2` or `STUDIOCTL_LOCALTEST=v2` selects the component set under the same graph id | Least plumbing; `env down` + `env up` switches | Same data dir and graph, so the two cannot be installed side by side; the flag leaks into status, logs, reset and doctor |
| 2b. A second runtime (`-r platform`) | A new `env.Env` implementation in `registry.Envs`, own graph id and data dir | Clean isolation, both runnable in CI, the flag already exists on every `env` command | `cmd/env.go` hard-codes `localtest` in six switches and `Status` is not on the interface; the app environment assumes one ingress; more plumbing before any component runs |
| 2c. Hard cutover in one release | Replace the component set, ship a migration that wipes the old data | Shortest total path; the new images have new names, so an old studioctl keeps pulling the old image | No fallback for a developer whose setup breaks; the Cypress and integration suites must be green on the new stack first |

2b is the better shape if the two stacks will coexist for more than one release; 2a if the window is short. Either way
the new images need distinct names (`runtime-localtest-<name>`), so the old `runtime-localtest:latest` can keep
publishing until the old studioctl window closes.

## 3. How the proxy learns about running apps

| Option | What | For | Against |
| --- | --- | --- | --- |
| **3a. Port the bound topology and the host bridge** (the plan) | The AOT app reads `result.json` and tunnels `host` destinations over studioctl-server's WebSocket | Apps keep binding to loopback with a random port; the frontend dev server, host workflow engine and Grafana routes keep working; process and container mode stay symmetric | `Altinn.Studio.Runtime.Common` must become AOT-compatible (source-generated JSON); the bridge client is the largest port |
| 3b. studioctl-server calls the route API | `PUT localtest/api/proxy/apps/{org}/{app}` with `http://host.docker.internal:<port>` | Nothing to port; one route API for studioctl and `run-app.sh` | Apps must listen on `0.0.0.0`, which `studioctl run` deliberately avoids; `--random-host-port` and the frontend dev server need the same treatment; container-mode apps need a different destination (their network name) |
| 3c. `result.json` without the bridge | The AOT app reads the topology file; `host` destinations go to `host.docker.internal` | No server changes, a small JSON reader instead of the bridge | The same loopback problem as 3b |

The loopback binding decides this: 3b and 3c require changing how `studioctl run` starts apps. 3a keeps the app
contract unchanged.

## 4. Who pushes an app's artefacts to the components

| Option | What | For | Against |
| --- | --- | --- | --- |
| 4a. studioctl-server (the plan) | On discovery: fetch from the app, get the Studio token, push to Register, Storage, Authorization, Access Management | Sits next to the existing readiness probe and app registry | C# logic that knows every component's localtest endpoint lives in the CLI's server, not with the stack; `run-app.sh` and other runners keep their own copy |
| **4b. The localtest app** | On a new route (from `result.json` or the route API) the localtest app fetches from the app as today's `LocalAppHttp` does and pushes | One implementation for studioctl, `run-app.sh` and anything else; it ships with the stack, so it follows the components' API, not the studioctl release; visible in the inspection endpoints | The front door grows a deployer role; the push needs the app to be reachable, so it runs after the route appears and the readiness probe waits for it |
| 4c. The CLI, from disk | `studioctl run` reads `App/config/*` and `testData.json` and pushes before the app starts | No dependency on the app being up; Register has the seed before the first request | Go code duplicating the C# `run-app.sh` logic; container mode and process mode need the same; a change in the app's config needs a rerun |

Re-push trigger, orthogonal to the choice: on app restart only, a file watch under `App/config/`, or an explicit
`studioctl app sync`. The old localtest answered live, which none of these fully replaces.

## 5. Readiness

| Option | What | For | Against |
| --- | --- | --- | --- |
| 5a. Aggregated health in the localtest app (the plan) | `GET localtest/api/health` fans out to every component; only the `localtest` container has a health check | One check; the AOT binary can probe itself | The executor reports the stack ready late (every component up) or not at all if one is optional |
| **5b. Host-side wait in studioctl** | After `executor.Apply`, studioctl polls `http://127.0.0.1:8000/<svc>/health` per component through the proxy | No image changes, matches what `doctor` already does, per-component messages | Not visible to the executor's `WaitForReady`; a second readiness mechanism |
| 5c. Per-container health checks | An exec probe per image | Native | Storage and Access Management run on chiseled images (no shell, no curl); Podman drops Dockerfile `HEALTHCHECK` for OCI images, so the probe has to be in the spec and in the image |

## 6. Where the code and images live

| Option | What | For | Against |
| --- | --- | --- | --- |
| 6a. Monorepo `src/Runtime/localtest-v2` + ghcr images (the plan) | Workflow per component path filter publishes `ghcr.io/altinn/altinn-studio/runtime-localtest-<name>` | Dev-mode builds from the checkout, one PR changes studioctl and the stack together, the house rule | Seven vendored trees with their own build chains and analyzers in the monorepo's CI; AOT needs an amd64/arm64 matrix |
| 6b. Separate repository (`Altinn/localtest`, grown from this research repo) | Only images cross into studioctl | Keeps the vendored copies and their re-pin tooling out of the monorepo; the compose file stays the dev loop | Breaks the "change studioctl in the same PR" rule for container-spec changes; no dev-mode build from the monorepo |
| 6c. Owning teams publish localtest images | Each platform repo builds a `localtest`-flavoured image | The fakes live with the code they fake | Storage, Register and Events publish no public image today; out of our control |

## 7. Smaller decisions

- **Postgres:** one shared container (absorbing `localtest-workflow-engine-db`) or one per component. The shared one
  halves the containers; it couples the components' migrations to one major version (studioctl runs 18.3, the stack
  ran 16). Init scripts: baked into a `runtime-localtest-postgres` image (nothing for studioctl to ship) or mounted
  from the data dir (studioctl already does this for the workflow engine).
- **Certificate:** generate per installation on first `env up` (studioctl owns it, mounted into the app and every
  component) or ship the test key pair in the images (zero setup, same key on every machine, fine for localhost).
- **Topology listing:** add the components to `topology.yaml` so `studioctl env status` shows them (touches the four
  closed switch sites per component) or keep the platform prefixes inside the localtest app's route table and treat
  the stack as one unit, with per-container status from the manifest only.
- **Hosts file:** nothing changes as long as every component sits behind a path prefix on `local.altinn.cloud`;
  a per-component hostname would need every developer to rerun `env hosts add`.
- **Contracts to keep** are listed in the plan (section 5); they do not change with these options.

## A coherent pick

1a with distinct image names, 2b if coexistence lasts more than one release (2a otherwise), 3a, 4b, 5b for
`env up` plus 5a for `doctor`, 6a, shared Postgres with baked init scripts, generated certificate. The two places this
differs from the plan are 4 (the push logic belongs with the stack, not in studioctl-server) and 5 (readiness in
studioctl rather than in the images).
