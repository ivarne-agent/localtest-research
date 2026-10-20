# workflow-engine

The production Altinn workflow engine (`vendor/`, see [`vendor/UPSTREAM.md`](vendor/UPSTREAM.md)): the host
`WorkflowEngine.App` from the Altinn Studio monorepo with its library projects and `Altinn.Studio.Common`, built with
the localtest overrides and run on the stack's Postgres. Apps enqueue workflows at
`http://workflow-engine.local.altinn.cloud:8000/api/v1/`; the engine calls them back through the proxy.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy of `src/` paths (`Runtime/workflow-engine-app`, `Runtime/workflow-engine/src`, `common/dotnet/Altinn.Studio.Common` and the build files on their path) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import the nearest vendored props/targets, define `LOCALTEST` for vendored projects only, bypass CSharpier |
| `Dockerfile` | Mirrors upstream `Runtime/workflow-engine-app/Dockerfile` (same digest-pinned `aspnet:10.0` / `sdk:10.0` images, same restore/publish, non-root `$APP_UID`, port 9090) with the overrides; defaults `ASPNETCORE_ENVIRONMENT=Development` |
| `smoke-test.sh` | Fresh Postgres 16 container + engine on a free port, checks `GET /api/v1/health/ready` → 200, migrations and a clean log. `./smoke-test.sh` runs the vendored host with `dotnet`; `./smoke-test.sh --image` runs the image |

## Directive sites in `vendor/`

**None.** The engine is localtest-aware through configuration alone (studioctl runs the same code unmodified):
`appsettings.Development.json` already points `AppCommandSettings:CommandEndpoint` at
`http://local.altinn.cloud:8000/…`, the engine creates its schema with EF migrations at startup, and nothing is
wired to Azure. `git diff <baseline commit> -- vendor` is empty; `LOCALTEST` is defined so that
future sites follow the `#if !LOCALTEST` / `#else` convention. No fakes.

## Configuration

Environment only; no localtest-specific keys.

| Key | Value in the stack |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` (image default). Selects `appsettings.Development.json`, and makes the host read an optional `appsettings.Local.json` instead of `/app/secrets/secrets.json` |
| `ASPNETCORE_HTTP_PORTS` | `9090` (image default, as upstream) |
| `ConnectionStrings__WorkflowEngine` | `Host=postgres;Port=5432;Database=workflow_engine;Username=postgres;Password=postgres`. The database must exist: `../../postgres-init/20-workflow-engine.sh` creates it (and `pg_stat_statements`, preloaded by the postgres service's command); the engine migrates its `engine` schema itself |
| `AppCommandSettings__CommandEndpoint` | `http://local.altinn.cloud:8000/{Org}/{App}/instances/{InstanceOwnerPartyId}/{InstanceGuid}/workflow-engine-callbacks/` (same as `appsettings.Development.json`; set explicitly so the callback route is visible) |

The readiness endpoint is the versioned `/api/v1/health/ready`; the versionless `/health/ready` is a redirect. The
image has no curl or wget, and podman would drop a Dockerfile `HEALTHCHECK` anyway; studioctl probes it from outside.

## In the stack

studioctl's `localtest-v2` runtime runs the monorepo's own workflow-engine image (as its classic localtest does), not
one built from this vendored copy. This copy is for building and testing the engine with the rest of this repository's
stack; its Dockerfile mirrors the monorepo's, so the two images match apart from the `localtest:` lines.

## Build and test

```sh
podman build -t localtest-workflow-engine:dev .   # from this folder
./smoke-test.sh && ./smoke-test.sh --image
```
