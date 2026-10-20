# Workflow engine and PDF review (2026-10-20)

> **Status (2026-10-20):** handled. Fixed: 1 (compose builds the vendored engine; `build-pdf3.sh` builds pdf3 from an altinn-studio checkout), 2, 3 and 4 (the Dockerfile mirrors upstream's line for line with `localtest:` markers, `.dockerignore` repeats upstream's exclusion of `Directory.*.props`, no HEALTHCHECK), 5 (the init script is idempotent), 6 (the README's hosts line has the PDF and workflow-engine names), 7 (sync instructions, the complete not-copied list), 8 (telemetry off, there is no collector), 10 (`build-pdf3.sh` labels the image with the commit and sets pdf3's service version), 12 (port 8000 bound to 127.0.0.1). Not changed: 9 (state is not reset, as decided) and 11 (specific to this sandbox's TLS interception). Verified: the engine is healthy on its non-root image; Cypress process-transition-test 19/19 and service-task 3/3 through it.

## Summary

- The vendored copy in `localtest/components/workflow-engine/vendor/` is byte-identical to Altinn/altinn-studio at
  `447e53eb` for every file it contains. Omitted files match what `vendor/UPSTREAM.md` says was left out, apart from a
  few it does not list. There are no `LOCALTEST` directive sites, and nothing in `vendor/` has changed since the
  baseline commit `ee72c62`.
- There is no upstream drift: no commit on `origin/main` (`89b1d7bc70`, 2026-10-19) after the pin touches the
  vendored paths.
- The most important finding: `localtest/docker-compose.yml` has no `build:` for `workflow-engine`, and none for
  `pdf3`. The compose comment says studioctl builds the image, but the component README says compose builds it. On a
  fresh machine, `docker compose up --build` does not build the vendored engine. The image running now was built from
  the component's Dockerfile, but only by hand.
- The Dockerfile follows upstream closely, but none of its deviations carry the `localtest:` marker the vendoring
  rule requires. Its `HEALTHCHECK` has no effect: the running image's healthcheck config is `null`. The build also
  differs in a way nobody documented. Upstream's `Dockerfile.dockerignore` removes `Directory.Build.props` from the
  build context, so production images are built without it. The localtest build imports it on purpose, which adds
  analyzers, `GenerateDocumentationFile` and the CSharpier package.
- pdf3 runs a prebuilt `localhost/localtest-pdf3:dev`. Its environment matches studioctl's `pdfEnv` exactly. Nothing
  records which commit the image was built from. Its logs show it contains pdf3 changes from 2026-10-14, so it is
  newer than the clone's HEAD at build time (`70300ade45`). The exact source commit could not be found.

## Workflow engine

### Vendored code

| Check | Result |
| --- | --- |
| Pin | `447e53eb9797d4fffe63d0c61d05a1f692b4ec48` (2026-10-16 20:43 +0200, on `main`, an ancestor of `origin/main`) |
| `diff -rq -x bin -x obj` of `git archive 447e53eb src/Runtime/workflow-engine-app src/Runtime/workflow-engine src/common/dotnet/Altinn.Studio.Common` against `vendor/src` | No file differs and nothing exists only in `vendor/`. Only in upstream: engine `.docker/`, `.gitignore`, `.k6/`, `Dockerfile`, `Dockerfile.dockerignore`, `Makefile`, `WorkflowEngine.slnx`, `docker-compose.yaml`, `docs/`, `tests/`; app `.gitignore`, `.k6/`, `Makefile`, `WorkflowEngine.App.slnx`, `infra/`, `tests/` |
| Directive sites | `grep -rn LOCALTEST vendor` finds only the text in `UPSTREAM.md`. There are no code sites, which matches the README |
| History | Baseline `ee72c62` adds 268 files. `git diff ee72c62 HEAD -- .../vendor` is empty, and the working tree is clean |
| `UPSTREAM.md` claims | Verified: there is no `Directory.*.props` or `global.json` in `src/`, `src/Runtime/` or `src/common/`, all 3 copied `.editorconfig` files are `root = true`, and there is no `Directory.Build.targets` on the path |

### Upstream drift

`git log 447e53eb..origin/main` on the three vendored paths returns 0 commits, out of 18 commits on main since the
pin. The last commits that touch them are older than the pin (#21043, #21179, #21164).

### Dockerfile (`localtest/components/workflow-engine/Dockerfile` compared with `vendor/src/Runtime/workflow-engine-app/Dockerfile`)

| Upstream | Localtest | Marked `localtest:`? |
| --- | --- | --- |
| `FROM aspnet:10.0@sha256:d5633778…` base, `USER $APP_UID`, `WORKDIR /app`, `ENV ASPNETCORE_HTTP_PORTS=9090`, `EXPOSE 9090` | identical | n/a |
| `FROM sdk:10.0@sha256:e70cdb7f…`, `ARG BUILD_CONFIGURATION=Release` | identical | n/a |
| `ARG BUILD_VERSION=dev` | `ARG BUILD_VERSION=localtest` (line 12) | no |
| `WORKDIR /build` | `WORKDIR /src` (line 13) | no |
| 3 `COPY` of `common/…`, `Runtime/workflow-engine/`, `Runtime/workflow-engine-app/` from context `src/` | `COPY localtest.build.props localtest.build.targets ./` and `COPY vendor/ vendor/` from the component folder (lines 16-17) | explained in a comment, not marked |
| `RUN find … bin*/obj* -prune -exec rm -rf` | dropped. Instead, `.dockerignore` excludes `**/bin/` and `**/obj/` but not upstream's `bin*`/`obj*` variants such as literal `bin\Debug` | no |
| context filtered by `Dockerfile.dockerignore`, which removes `Directory.*.props` except `Directory.Packages.props`, plus README, docs, tests and `.csharpier*` | the whole `vendor/` is copied, and `localtest.build.props` imports the vendored `Directory.Build.props` | not mentioned anywhere (Finding 3) |
| — | `ENV DirectoryBuildPropsPath/DirectoryBuildTargetsPath` (lines 20-21) | explained in a comment |
| `WORKDIR …/WorkflowEngine.App`, `restore`, `publish … /p:UseAppHost=false /p:CSharpier_Bypass=true /p:InformationalVersion=$BUILD_VERSION` | identical apart from the path prefix | n/a |
| final stage `COPY --from=build`, `ENTRYPOINT` | identical | n/a |
| — | `ENV ASPNETCORE_ENVIRONMENT=Development` (line 29) | no |
| — | `HEALTHCHECK …/dev/tcp…` (line 32). Podman (OCI) drops it, and `podman image inspect` shows `Healthcheck: null` | no |

### What localtest changes or replaces

| Area | Localtest | Source |
| --- | --- | --- |
| Code | nothing: no directives, no fakes; `LOCALTEST` is defined but unused | `localtest.build.props` |
| Build | imports the vendored `Directory.Build.props` (analyzers, `GenerateDocumentationFile`, `AnalysisMode=All`, `NuGetAudit`), forces `CSharpier_Bypass` | `localtest.build.props:11-18` |
| Environment | `ASPNETCORE_ENVIRONMENT=Development` (image default and compose) | `Dockerfile:29`, `docker-compose.yml:197` |
| Database | shared `postgres:16-alpine`, database `workflow_engine` created by an init script, superuser `postgres/postgres`, `pg_stat_statements` preloaded (the engine code never references it) | `postgres-init/20-workflow-engine.sh`, `docker-compose.yml:131,133,199` |
| Database tuning | none. Upstream and studioctl apply `postgres-init.sql` (`ALTER SYSTEM` memory, WAL and logging settings) | upstream `src/Runtime/localtest/infra/workflow-engine/postgres-init.sql` |
| Callbacks | `AppCommandSettings__CommandEndpoint=http://local.altinn.cloud:8000/{Org}/{App}/instances/…/workflow-engine-callbacks/` through the localtest proxy (10.200.0.10 through `extra_hosts`) | `docker-compose.yml:200` |
| Inbound | the proxy routes `workflow-engine.local.altinn.cloud:8000` to `workflow-engine:9090`, and `run-app.sh:47` points apps at it | `localtest/Proxy/PlatformRoutes.cs:20,27` |
| Same as studioctl | the 4 environment variables are exactly studioctl's `workflowEngineEnv` (`src/cli/.../components/workflow_engine.go`), except that studioctl uses its own dedicated Postgres container | upstream |

### Differences from production (`vendor/…/workflow-engine-app` upstream `infra/kustomize/base`, `tt02`)

| Setting | Production | Localtest |
| --- | --- | --- |
| Environment | from the `runtime-environment` ConfigMap (tt02, prod, …), which loads `appsettings.<env>.json` and `/app/secrets/secrets.json` | `Development`, which loads `appsettings.Development.json` and an optional `appsettings.Local.json` |
| Database auth | Azure workload identity (`enableAzureWorkloadIdentity: !isLocalEnvironment`), no password, `Ssl Mode=VerifyFull;GSS Encryption Mode=Disable`, one database per service owner and environment | password `postgres` superuser, no SSL, no `GSS Encryption Mode=Disable` (hence the `libgssapi_krb5.so.2` error line at startup), one database for all orgs |
| Development-only code paths | — | EF sensitive-data logging, SQL parameters on traces (`emitQueryParameters`), startup `pg_terminate_backend` of other connections to `workflow_engine` only, dashboard served from disk when present, forwarded headers trusted from any IP |
| Telemetry | OTLP to `otel-router.runtime-obs:4317`, `OTEL_SERVICE_NAME` | the OTLP exporter is still registered (`EnableTelemetry` defaults to true), with no endpoint, so it defaults to `localhost:4317` and exports go nowhere |
| Inbound access | NetworkPolicy allows only app pods in `default` on 9090. There is no app-level auth | no app-level auth, published through the proxy on host port `8000` (all interfaces): API, Swagger UI and dashboard |
| Callback target | `http://{Org}-{App}-deployment.default.svc.cluster.local/…` | through the proxy to the app on the developer's machine |
| Runtime hardening | 2 replicas, read-only root filesystem, dropped capabilities, linkerd, probes on `/api/v1/health/live` and `/ready`, memory limit 3Gi | one container, no probes (`HEALTHCHECK` is dropped), no limits |
| Image | CI `docker build` (BuildKit, honours `Dockerfile.dockerignore`), version = short SHA, `org.opencontainers.image.revision` label | local podman build, version `localtest`, no revision label |

Internet dependencies: none at runtime. I checked the code and the logs show no outbound calls. The build needs MCR
images and NuGet.

## PDF (pdf3)

- **Build:** compose has no `build:`. It uses the image `localhost/localtest-pdf3:dev` and says it is built by
  studioctl dev mode from `src/Runtime/pdf3/Dockerfile.worker` with context `src/Runtime/pdf3`, which matches
  `src/cli/.../components/pdf.go`. Nothing in `localtest/README.md` says how to build it. `Run it`
  (`docker compose up --build`) assumes the image already exists. Nothing is vendored, and the
  `components/pdf/` folder from `mvp-vendoring-model.md` does not exist.
- **Configuration:** `TZ`, `PDF3_ENVIRONMENT=localtest`, `PDF3_QUEUE_SIZE=3` and
  `PDF3_LOCALTEST_PUBLIC_BASE_URL=http://local.altinn.cloud:8000` are identical to studioctl's `pdfEnv`. There is no
  host port, where studioctl publishes 5300. The proxy routes `pdf.local.altinn.cloud` to `pdf3:5031`, and
  `run-app.sh:43` sets `PlatformSettings__ApiPdf2Endpoint=http://pdf.local.altinn.cloud:8000/pdf`.
- **Documentation:** a comment at `docker-compose.yml:179-180`, a mention in the service list at `README.md:20`, and the
  proxy README. No README row describes what pdf3 is or how to get the image.
- **Provenance:**
  - Not recorded anywhere: no commit, no labels apart from `io.buildah.version`, and the service version is the
    `dev` default.
  - The image `61f7a47e22f3` was created 2026-10-16 18:33 UTC. At that time the altinn-studio clone was at
    `70300ade45` according to the reflog.
  - The worker logs show per-render browser contexts and a long-lived browser. That behaviour comes from #21059 and
    #21089, both committed 2026-10-14, after `70300ade45`. So the image was built from some other tree; I could not
    determine which commit.
  - Since then, 3 worker-relevant commits have landed on main: #21313, #21315 and #21317, all telemetry or relay
    changes.
  - The base images are digest-pinned in upstream's `Dockerfile.worker`.
- **Runtime dependencies:** rendered pages load `https://altinncdn.no/fonts/...` and the logo. In this sandbox those
  fail with `ERR_CERT_AUTHORITY_INVALID` because the container's Chromium does not trust the mediated CA. The
  entrypoint can import `STUDIO_CA_BUNDLE`, but compose neither sets it nor mounts a bundle. The build downloads
  `ttf-mscorefonts-installer`.

## Findings

1. **Compose never builds the vendored engine (or pdf3).** `localtest/docker-compose.yml:190-203` has no `build:`,
   and its comment says the image comes from upstream's Dockerfile through studioctl. Meanwhile
   `components/workflow-engine/README.md:38-58` shows a service with `build: ./components/workflow-engine` and says
   "Only `build:` is new". `git log -S` confirms that the line never existed in compose. Why it matters:
   - `docker compose up --build` (`README.md:50`) silently uses whatever image carries that tag.
   - On a fresh machine, compose fails to pull `localhost/...`.
   - A studioctl run retags the image with the unmodified upstream build.

   The vendored component is therefore not part of the documented run path. Fix: add `build: ./components/workflow-engine`
   and correct the comment. For pdf3, either add `build:` with `context: ${ALTINN_STUDIO:-…}/src/Runtime/pdf3` and
   `dockerfile: Dockerfile.worker`, or document the build command in `README.md`. **High.**
2. **Dockerfile deviations are not marked `localtest:`.** `components/workflow-engine/Dockerfile` lines 12, 13,
   16-17, 20-21, 29 and 32, plus the dropped prune `RUN`. The rule in `mvp-vendoring-model.md` ("every difference is
   a line marked `localtest:`"), which storage and register follow, is not applied here. The upstream comparison is
   not in `UPSTREAM.md` either. Why it matters: a re-pin has to rediscover each deviation by hand. Fix: mark every
   difference the way `storage/Dockerfile` does, and add a re-pin step. **Medium.**
3. **The localtest build differs from the upstream image build because of upstream's `Dockerfile.dockerignore`.**
   Upstream's ignore file removes every `Directory.Build.props` from the image build context, so the production
   image is built without CSharpier.MsBuild, the Sonar and Nullable analyzers, `GenerateDocumentationFile`,
   `AnalysisMode=All` and `NuGetAudit`. `localtest.build.props:4,11` imports these deliberately ("what MSBuild itself
   would have found"), and the component's `.dockerignore` does not mirror upstream's. The comment at
   `localtest.build.props:16-17` is also inaccurate: upstream's `CSharpier_Bypass` is a no-op in the image build.
   Why it matters: the binaries are not built the way production builds them. Likely effects are XML documentation
   files and OpenAPI descriptions that differ, and a slower build that is more prone to warnings. I did not verify
   these at the binary level. The README (`README.md:12`, "same restore/publish") overstates the parity. Fix: copy
   upstream's ignore rules, either as `Dockerfile.dockerignore` next to the component Dockerfile or by porting them
   into `.dockerignore`, while keeping `localtest.build.props/targets`. Document the choice either way. **Medium.**
4. **The `HEALTHCHECK` is dead code.** `Dockerfile:32`; `podman image inspect` shows `Healthcheck: null`. The
   README already says so (`README.md:34-36`), but the line remains, while the register Dockerfile removes its own
   for the same reason. No compose `healthcheck` exists either, so `localtest` depends on `workflow-engine` without
   any readiness check. Fix: remove the line (or mark it), and move the probe into compose as
   `healthcheck: test: ["CMD","bash","-c",…]` if readiness matters. **Low.**
5. **The init script is not idempotent.** `localtest/postgres-init/20-workflow-engine.sh:4` uses a plain
   `CREATE DATABASE` with `ON_ERROR_STOP` and `set -e`. `README.md:59-61` says "Each init script is idempotent" and
   suggests rerunning them by hand. A rerun on an existing volume fails, though harmlessly. Fix: use
   `SELECT 'CREATE DATABASE workflow_engine' WHERE NOT EXISTS (…) \gexec`, as `30-events.sh` does. The script also
   sits at the stack root rather than under `components/workflow-engine/postgres-init/` like events and storage.
   **Low.**
6. **The README's host setup is incomplete.** `README.md:49` adds only `local.altinn.cloud` to `/etc/hosts`. Apps on
   the host also need `workflow-engine.local.altinn.cloud` and `pdf.local.altinn.cloud` (`run-app.sh:43,47`). This
   machine has them because studioctl prepared `/etc/hosts`. I did not verify whether public DNS resolves these
   names: `dig` returned nothing. Fix: add both names to the hosts line. **Medium** (the v9 backend needs the engine
   for every process transition).
7. **`UPSTREAM.md` is incomplete.** It has no sync or re-pin procedure, unlike `storage/vendor/UPSTREAM.md`. Its
   "Not copied" list also omits `.docker/` (which holds the Postgres tuning SQL), `.k6/`, `.gitignore` and the
   engine's own `Dockerfile` and `Dockerfile.dockerignore`. Fix: add the `git diff --stat <pin> <new> -- <paths>`
   procedure and a step to carry Dockerfile changes over, and complete the list. **Low.**
8. **Telemetry exports go nowhere.** OTLP exporters are registered with the default `localhost:4317` and no
   collector exists. Fix: set `EngineSettings__EnableTelemetry=false` in compose, or point
   `OTEL_EXPORTER_OTLP_ENDPOINT` at a collector if one is added. **Low.**
9. **The engine's state persists across stack restarts.** The `postgres-data` volume keeps the engine database.
   `/api/v1/health/ready` currently reports `failed_workflows: 289` from earlier runs, and throttle state lives in
   `engine.namespace_throttles`. The events database, by contrast, is emptied on every start. Why it matters: a
   failure storm in one Cypress run can still affect, or confuse, the next one. `GET /api/v1/throttles` returns 204
   now, so nothing is throttled at the moment. Fix: decide and document a reset policy (for example, truncate on
   start or a `make reset`). **Low.**
10. **Nothing records where the pdf3 image came from.** `docker-compose.yml:179-188`. The running image cannot be
    tied to a commit (see the PDF section). Fix: pass `SERVICE_VERSION=<sha>` and a
    `org.opencontainers.image.revision` label when building, and record the expected commit in `README.md`. **Medium.**
11. **pdf3 cannot fetch altinncdn.no assets behind a TLS-intercepting proxy.** No `STUDIO_CA_BUNDLE` is set or
    mounted for pdf3 (`docker-compose.yml:181-188`), so PDFs render without the Altinn fonts and logo in this
    sandbox. Fix: mount the host CA bundle and set `STUDIO_CA_BUNDLE` (optional, environment-specific). **Low.**
12. **Engine endpoints are reachable from the network.** Port `8000:8000` binds all interfaces, so the engine API,
    Swagger UI and dashboard (which has no auth) can be reached from the LAN. This is the same for every
    component. Fix: `127.0.0.1:8000:8000`. **Low.**

## Verification done

- I read `AGENTS.md`, `localtest/README.md`, `planned-components/mvp-vendoring-model.md`, the component's README,
  `Dockerfile`, `.dockerignore`, `localtest.build.props/.targets`, `smoke-test.sh`, `vendor/UPSTREAM.md`,
  `docker-compose.yml`, `postgres-init/20-workflow-engine.sh` and `run-app.sh`, plus the proxy's `PlatformRoutes.cs`.
- `git -C altinn-studio fetch -q origin`, then
  `git archive 447e53eb <3 paths> | tar -x` into a scratch directory, then `diff -rq -x bin -x obj` against
  `vendor/src`. Result: only the omissions listed above, no content differences.
- `grep -rn LOCALTEST vendor`. In the research repo:
  - `git log` and `git show --stat` for the component.
  - `git diff ee72c62 HEAD -- vendor`: empty.
  - `git ls-tree` of the baseline: 268 files.
  - `git log -S'build: ./components/workflow-engine'`: only the README commit `57c54dd`.
- `git log 447e53eb..origin/main` on the vendored paths: 0 commits. I also ran it for pdf3 and listed the commits
  between `70300ade45` and main.
- I read upstream at the pin:
  - the Dockerfile and `Dockerfile.dockerignore`
  - the CI workflows `deploy-runtime-workflow-engine-app.yaml` and `runtime-workflow-engine-docker-build.yaml`, which
    build with context `src/`
  - `infra/kustomize` base and tt02
  - studioctl's `workflow_engine.go` and `pdf.go`
  - `postgres-init.sql`
  - pdf3 `Dockerfile.worker`, `docker-entrypoint.sh` and the config environment variables
  - in the vendored engine: `Program.cs`, `WorkflowEngineBuilderExtensions`, `HostExtensions`,
    `DbConnectionResetService`, the telemetry extensions and `Hosting.cs`
- `podman ps`, `podman inspect localtest_workflow-engine_1` and `localtest_pdf3_1` (environment, image),
  `podman image inspect` and `podman image history` of both images, and `podman logs` of both containers.
- Read-only GETs through the proxy:
  - `/api/v1/health/ready`: 200, Healthy, `failed_workflows` 289
  - `/swagger/index.html`: 200
  - `/`: 200
  - `/api/v1/throttles`: 204
- `git reflog` of the clone, to see its HEAD at the time the pdf3 image was built.
- **Not verified:**
  - I did not build or run the smoke test, as instructed.
  - I did not compare the published binaries with an upstream-built image (XML docs, analyzer effects).
  - I did not check whether buildah or podman honour upstream's `Dockerfile.dockerignore` when studioctl builds the
    upstream image.
  - I could not determine the exact commit the pdf3 image was built from.
  - Public DNS for `*.local.altinn.cloud`: `dig` returned nothing.
  - I did not check whether failed workflows or throttles from earlier runs actually affect later Cypress runs.
