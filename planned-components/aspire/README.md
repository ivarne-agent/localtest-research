# Research: an Aspire host for the components

## Status (2026-10-22)

- **Research done** as task 20 (2026-10-21); the owner chose the hosting package (task 21 in [`TODO.md`](../../TODO.md)),
  with the stock `Aspire.Hosting.Yarp` as the front door (the 21d decisions), not the localtest app this report
  recommends in section 3.
- **21d merged** (2026-10-22): [`localtest/aspire/Altinn.App.Aspire/`](../../localtest/aspire/Altinn.App.Aspire/README.md)
  has the C# manifest reader (compared with studioctl's golden file), `AddAltinnLocaltest()` with the default stack and
  `WithGroup(...)`, and the YARP front door with the manifest's routes. Run under Podman 5.7: the root, authentication
  and Dialogporten smoke tests pass through its front door, and persistent containers survive AppHost restarts.
  Aspire's container tunnel does not build on this Podman and is switched off (`ASPIRE_ENABLE_CONTAINER_TUNNEL=false`),
  so an app on the host is reached through `host.docker.internal` and the host gateway.
- **21e done** (2026-10-22, branch `task/aspire-app`): the app's environment is the manifest's `app` section,
  which `studioctl run` loads (its output pinned by a golden file) and the package reads;
  `AddAltinnApp<Projects.App>(localtest)` adds the app project with that environment, one endpoint on an Aspire-assigned
  port on every interface, the `/{org}/{app}` front-door route, provisioning when the app is healthy (a failure is a
  degraded health check, not a failed start), and the `provision`, `deprovision` and `reset` commands. The package
  refuses to start while port 8000 is taken. The sample is `localtest/aspire/samples/app/` (the `ttd/testapp` fixture
  as an app on app-lib 8.12.15, and its single-file AppHost), and `.github/workflows/aspire-package.yml` publishes the
  package to GitHub Packages. Phase 2 ran the sample under Podman: the app through the front door, provisioned,
  signed in to and submitted in a browser, the commands from the dashboard, a first start on empty volumes after `reset`,
  and the root and authentication smoke tests; the workflow runs after the merge.
- **HTTPS researched** (2026-10-22, task 23): [`https.md`](https.md). Aspire 13.6 can terminate TLS on the YARP front
  door with a package-made certificate and give every component the CA without image changes (run in a scratch AppHost
  under Podman); the dev certificate cannot carry the stack's host names. Recommendation: termination at the front
  door, a localtest CA in the package's data directory, Aspire first behind an opt-in, studioctl after; 4.5 to 6 days,
  plus 2 to 3 for studioctl.
- **21f** (2026-10-22, branch `task/aspire-parity`): `.github/workflows/manifest-parity.yml` runs studioctl's Go tests
  (the golden snapshots are the stale-golden check), the package's and the provisioning library's tests and a JSON Schema
  validation of the manifest on every change to the manifest, the Go loaders, the package or the library; the smoke
  tests find containers under either runtime (`localtest/smoke/lib/containers.sh`); `localtest/README.md` describes the
  two runtimes and how to run the smoke tests against an Aspire-hosted stack. The documentation stays in this repository
  until adoption (nothing in altinn-studio-docs).
- **Task 21 is done** except what task 24 (the HTTPS mode task 23 proposed) and task 22
  (app-lib honours an explicit localtest environment and sets `keepAlive`'s `Secure` from the request) decide.
- **Task 26 phase 1** (2026-10-22, branch `task/front-door`): the localtest image is the shared front door the owner
  chose on 2026-10-22, which reverses the 21d choice of `Aspire.Hosting.Yarp`: YARP routes by service name through
  service discovery (no pass-through), the 503 problem details for a service without an address (with the
  `.With<Group>()`/`.WithGroup(...)` hint), the app-route `Secure` stripping, and one `PUT localtest/api/apps/{org}/{app}`
  with the app's URL that routes and provisions it ([`localtest/localtest/README.md`](../../localtest/localtest/README.md#proxy)).
  The package is unchanged until task 27, which drops its YARP container, its route model and its compiled-in
  provisioning and publishes 8000 from the localtest container; `AddAltinnApp` then calls the registration on the app's
  ready event.
- No statement in another plan is superseded by this one.

Research date 2026-10-21 (Europe/Oslo), task 20 of [`../../TODO.md`](../../TODO.md), written against this repository
at `2c2d3aa` (image pin `sha-3143730`), `Altinn/altinn-studio` `origin/main` at `3ccccf7867` and Aspire 13.6.2. The
question: can the stack's components run as container resources of a .NET Aspire AppHost next to an Altinn app
project, so an app developer gets the platform from `dotnet run` on the AppHost instead of `studioctl env up`, and is
it worth doing? One report in this folder.

| Report | What it covers | Effort |
| --- | --- | --- |
| [`aspire.md`](aspire.md) | What `env up` and studioctl-server do, the mapping table (20 components to Aspire resources) and each need's Aspire feature, five front-door candidates, four provisioning triggers, the app's environment and app-lib's studioctl import, the hybrid that needs no code, fit (audience, duplication, shared definitions, memory, Podman, dashboard), recommendation, effort, risks, open questions | Hybrid: 0.5 to 1 day. Generated hosting package: 6 to 9 days |
| [`https.md`](https.md) | Task 23: https on `local.altinn.cloud:8000` for the Aspire-hosted stack: Aspire 13.6's certificate and CA-trust APIs, the certificate (a localtest CA versus the dev certificate), trust in the browser and in every image, termination and `X-Forwarded-Proto`, what https changes, studioctl parity, experiments | Aspire: 4.5 to 6 days; studioctl parity: 2 to 3 more |

## Headline findings

- **The containers map one to one.** Every component is an `AddContainer` with `WithImageRegistry`/`WithImageTag`
  (or `WithImageSHA256`), `WithEnvironment`, `WithBindMount` for the certificate, `WithVolume` for the five named
  volumes, `WithHttpEndpoint(..., isProxied: false)` for the fixed host ports, `WithHttpHealthCheck` and `WaitFor` for
  the order; Postgres and Redis can use `AddPostgres`/`AddRedis` with the pinned images swapped in. A scratch AppHost
  with that slice of the stack **compiles** against Aspire 13.6.2. Small gaps: no exec health checks (both have
  host-side replacements), no volume ownership set-up, no `env reset`.
- **The front door is the hard part, and Aspire's YARP cannot do it.** `Aspire.Hosting.Yarp` routes by host and
  prefix, keeps `Host` and rewrites paths, but has no cookie transform and runs no custom code, so it cannot strip
  `Secure` from production Authentication's cookies (sign-in breaks on `http://`) and cannot answer the 503 for a group
  that is off. The closest fit is the **localtest app's own proxy** (strips `Secure`, keeps `Host`, takes app routes by
  API, upstreams by configuration), given the ingress host names as network aliases so that containers and browser both
  reach it as `local.altinn.cloud:8000`; it lacks six routes, the GraphQL rewrite and the 503s. studioctl-server cannot
  run as a container: its discovery reads the host's port listeners and its control API is a Unix socket.
- **Provisioning fits Aspire's eventing.** The five steps are plain HTTP against the public origin. An
  `OnResourceReady` hook on the app project can run them, and dependents of the app wait for the hook; a `WithCommand`
  gives a "Provision again" button. That makes a fourth copy of the logic unless `AppProvisioner` becomes a shared
  library.
- **The app needs studioctl's environment, and app-lib already fetches it.** In Development, app-lib v9 runs
  `studioctl app env --json` when `STUDIOCTL_APP_RUN` is unset and puts the result *over* the environment, so an
  AppHost must set that variable or let studioctl supply the configuration. The template's launch-profile
  `applicationUrl` plus its Kestrel endpoint may also stop `AddProject` from starting the app unchanged.
- **A hybrid works with no new code, by reading.** An AppHost that runs only the app (plus the developer's own
  resources) next to `studioctl env up -r localtest-v2` gets studioctl's environment through app-lib's import and is
  found, provisioned and routed by studioctl-server's process discovery (the assembly `Altinn.Application` matches),
  exactly as an IDE run is. Not run.
- **No one in the ecosystem uses Aspire today.** Neither the monorepo nor app-lib references Aspire anywhere; docs,
  template and agent skill all say studioctl. A full Aspire host would duplicate studioctl's component table, pins,
  certificate, provisioner, app environment and route table, and every component task would change two places.

## Recommendation

**Do not build a second runtime.** Verify and document the hybrid (an AppHost running the app next to `studioctl env
up`), 0.5 to 1 day. Build an Aspire hosting package only if the product owner wants Aspire as a supported way in, and
then as a thin layer **generated from a JSON manifest that studioctl emits from `components.go`**, with the localtest
app's proxy extended as the front door and the provisioner extracted into a shared library, 6 to 9 days. Never a
hand-maintained AppHost beside studioctl. Separately and cheaply: try the standalone Aspire dashboard beside `studioctl
env up` for traces (an input to task 8). Details, effort and risks in section 7 of the report; eight open questions in
section 7.4.

## Not verified

Nothing was run: no AppHost, DCP, dashboard, container, image pull or Podman call; the hybrid is read from the code.
The scratch AppHost (in the session scratchpad, not committed) compiled against Aspire 13.6.2; compiling proves the
APIs exist, not how DCP behaves with rootless Podman (`--add-host`, network aliases, persistent containers, SELinux
bind mounts), whether `WaitFor` starts an explicit-start dependency, or whether a `net10.0` AppHost references the
template's `net11.0` app cleanly. The YARP integration's lack of a cookie transform is read from its public API. DCP
and dashboard memory and start time were not measured. Whether the vendored components export OTLP, and whether
missing `RUNTIME_APP_SECRETS_*` variables stop an app, were not checked. Details in section 8 of the report.

## Proposed brief

**Goal.** Make the stack usable from a .NET Aspire AppHost without a second runtime: first prove and document that an
AppHost running an Altinn app works next to `studioctl env up -r localtest-v2`; then, only if the product owner
answers question 1 with yes, a hosting package generated from studioctl's component table.

**Step 20a (0.5 to 1 day, needs the stack).** A sample single-project AppHost (`Aspire.AppHost.Sdk/13.6.2`) in
`localtest/samples/aspire-apphost/` referencing `ttd/frontend-test` (or the smoke fixture), with the app's launch
profile and Kestrel endpoints excluded and no `STUDIOCTL_APP_RUN`. With `studioctl env up -r localtest-v2` running:
`dotnet run` on the AppHost; check that the app imports studioctl's environment, that studioctl-server discovers,
provisions and routes it (`studioctl apps`, its log), sign-in through `/Home`, an instance created, and the root smoke
test. Record the result, the launch-profile clash and the OTLP switch (`AppSettings__UseOpenTelemetry=true`) in the
sample's README and in `localtest/README.md`, "Run it". Note for altinn-studio-docs if it works.

**Step 20b (6 to 9 days, only after question 1).** In order, each its own commit:

1. `studioctl env manifest -r localtest-v2 --json` in the vendored studioctl, emitting `components.go`'s table (image
   with pin, env, mounts, ports, deps, group and implied groups, health path, routes, rewrite), with a Go test and a
   committed copy checked in CI.
2. The localtest app's proxy brought level with studioctl-server's front door: the missing prefixes, the GraphQL
   rewrite, longest-prefix matching, 503 problem details for groups marked off by configuration; `smoke-test.sh`
   extended.
3. `AppProvisioner` extracted into a library that studioctl-server and the package both use; studioctl-server's tests
   unchanged.
4. The hosting package (`AddAltinnLocaltest()`, `WithGroups`, `WithAltinnLocaltest()` on the app): resources from the
   manifest, persistent lifetime, the localtest container aliased as the ingress hosts, the certificate written by the
   package, groups as explicit-start resources, the app environment with `STUDIOCTL_APP_RUN=1` and the secrets
   directory, route `PUT`, provisioning on `ResourceReadyEvent`, dashboard commands (provision again, Events reset,
   Dialogporten cache clear).
5. The sample AppHost switched to the package; root smoke test and the platform regression through it.

**Open.** Questions 1 to 8 in section 7.4 of [`aspire.md`](aspire.md): Aspire as a supported way in; documenting the
hybrid; the template's URL sources; the front door outside studioctl; where the provisioner lives; a studioctl
manifest; the standalone dashboard and OTLP; Aspire testing for service owners.

**Verification.** 20a: the sample's run as above, recorded with the commit. 20b: the root, Authentication and
Resource Registry smoke tests and the platform regression (44 tests) pass through the AppHost with the default stack,
the Dialogporten smoke test with the `dialogporten` group started from the dashboard, and the manifest check fails CI
when `components.go` changes without the committed JSON. One image build at a time; no run beside a Cypress run.
