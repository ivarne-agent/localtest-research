# The localtest-v2 component manifest

[`manifest.json`](manifest.json) is the one description of the localtest-v2 stack: every container, the optional
component groups, the image pin, the Postgres init scripts, the network, the volumes and the front door's routes.
[`manifest.schema.json`](manifest.schema.json) (JSON Schema 2020-12) documents every field; an editor that follows
`$schema` shows the descriptions and completes the keys. studioctl embeds both files and builds its `localtest-v2`
runtime from the manifest (`internal/cmd/env/localtestv2/components.go` is the loader); other runtimes can read the
same file.

## Shape

| Key | What |
| --- | --- |
| `version` | The format version, `1`. A loader refuses one it does not know |
| `containerPrefix`, `network` | Container names are the prefix plus the component name; every container joins the network |
| `certificate` | The shared test certificate's file name and the directory it is mounted at |
| `images` | `repositoryPrefix` and `tag` of the research images (built by `.github/workflows/images.yml`), the tag variable `tagEnv`, the per-image override prefix, the `thirdParty` images (Redis, pinned by digest) and the `core` images studioctl's localtest runtime runs too (the PDF generator, the workflow engine; mirrored from `internal/config/images.go`, which a test checks) |
| `volumes` | Every named volume; `env reset` deletes them |
| `postgres` | The Postgres component, its user and password, and `initScripts`: what `localtest/postgres/Dockerfile` copies into the image (a test checks they agree) |
| `frontDoor` | The front door's health path and its routes to ports on the host itself (`hostRoutes`: the frontend dev server) |
| `app` | The environment an app runs with against the stack, which every runner sets: studioctl's `run` and `app env`, and the Aspire package's `AddAltinnApp` (see [The app section](#the-app-section)) |
| `groups` | The optional groups: `onByDefault`, `implies`, or `impliesAll` for `everything` |
| `components` | Every container; the runtime starts each after its `dependsOn` |

A component:

```json
{
  "name": "storage",
  "description": "Production Storage on Postgres with file-backed blobs.",
  "image": { "research": "storage" },
  "dev": { "context": "components/storage" },
  "containerPort": 5010,
  "hostPort": 8024,
  "healthPath": "/health",
  "environment": {
    "AccessTokenSettings__AccessTokenSigningCertificateFullPath": "{{certPath}}",
    "GeneralSettings__PlatformEndpoint": "{{platform}}",
    "PostgreSqlSettings__ConnectionString": "Host={{alias:postgres}};Port={{port:postgres}};Username=platform_storage;Password={0};Database=storagedb;Include Error Detail=True"
  },
  "mounts": [
    { "kind": "certificateDir" },
    { "kind": "volume", "volume": "localtest-v2-storage-blobs", "target": "/data/blobs" }
  ],
  "dependsOn": ["postgres"],
  "routes": [{ "host": "{{host:app}}", "prefix": "/storage" }]
}
```

The other component keys: `group`; `notes` (why a setting is what it is; loaders ignore them); `command`;
`containerHealthCheck` (`test`, `interval`, `timeout`, `retries`, `startPeriod`, run by the container runtime, which
waits for it before starting what depends on the component); `networkAliases`; `ingressHosts` (default `true`: every
public host name resolves to the host gateway, so the container reaches the front door; `false` for Postgres and
Redis); `extraHostGatewayNames` (`host.docker.internal` for Events); `hostUser` (run as the developer's user, unused
today). A route has `host`, `prefix` (empty for the whole host) and an optional `rewrite`, the path forwarded in place
of the prefix. The `dev` build has a `context` relative to the `localtest/` folder, a `dockerfile` relative to the
context (default `Dockerfile`), and a `selector`, the `--dev` name, when several components are built together
(`dialogporten`, `arbeidsflate`); without one the selector is the component's name.

## The app section

What an app is started with (task 21e). studioctl's `internal/cmd/app/env.go` is its loader, and
`localtest/aspire/Altinn.App.Aspire` reads the same section for an app project in an Aspire AppHost.

| Key | What |
| --- | --- |
| `environment` | The platform endpoints (`PlatformSettings__Api*Endpoint`), `GeneralSettings__ExternalAppBaseUrl` and `HostName`, `AppSettings__OpenIdWellKnownEndpoint`, `ASPNETCORE_ENVIRONMENT`. studioctl sets each unless the shell it runs in already has the variable |
| `fixedEnvironment` | `STUDIOCTL_APP_RUN=1`, set on every run whatever the shell holds |
| `accessTokenSigning` | The `AccessTokenSettings__*` certificate variables, set like `environment` when the runner has the shared certificate (studioctl: while `localtest-v2` is up; the Aspire package: always) |
| `secrets` | `directoryEnv` (`RUNTIME_APP_SECRETS_DIR`, set to the directory the runner creates) and the `files` in it, each with the variable that names it and its `fileName`; `writtenOnEveryRun` marks the development app codes every runner writes with the same bytes (a Go test keeps studioctl's `appsecrets` names equal to these, and the Aspire package's test compares its bytes with `internal/appsecrets/testdata/app-codes.golden.json`) |

The runner adds what only it knows: the Kestrel URL, `AppSettings__AppFrontendAssetBaseUrl` (`--dev-frontend`),
`OTEL_EXPORTER_OTLP_ENDPOINT` (studioctl's collector; under Aspire, the dashboard, which Aspire sets),
`ALTINN_KEYS_DIRECTORY` (an app container) and the secrets directory's path.

The certificate variables need two values only the runner knows, so the section has two placeholders of its own,
allowed in `accessTokenSigning` only (`Parse` refuses them anywhere else):

| Placeholder | Value |
| --- | --- |
| `{{appCertPath}}` | The certificate as the app opens it (app-lib v8's 3.x access token client reads the full path): the host path for a process run (`~/.altinn-studio/.../certs/localtest-accesstoken.pfx`, the Aspire package's `~/.altinn-app-aspire/certs/...`), `/certs/localtest-accesstoken.pfx` in an app container |
| `{{appCertKeysFolder}}` | The certificate's directory as the 1.x client (the v9 backend) resolves it: appended to the parent of the app's working directory (the app root), so a process run gets a traversal from the app root with a leading and a trailing slash (`/../../.altinn-studio/localtest-v2/certs/`), an app container `/certs/` |

`{{certFileName}}` (`certificate.fileName`) is valid everywhere. studioctl's golden file of the app environment,
`internal/cmd/app/testdata/app-environment.golden.json`, was taken before the section existed, for the option
combinations the run tests cover (a process run with and without `localtest-v2`, a random port, an inherited
environment, `--dev-frontend`, ingress port 80, an app container), and holds the loader to it; the Aspire package
compares its expansion with the snapshot's `native-localtest-v2` case.

## Placeholders

`{{name}}` or `{{name:argument}}` in an environment value (of a component or the app section), a command argument, a
container health check argument, a route host or a front-door host route's host. Single braces (`{0}`, `{Org}`,
`{org}`) belong to the components and the app and pass through unchanged. An unknown placeholder, or one naming a component that does not exist, fails `Parse`.

| Placeholder | Value (default topology) |
| --- | --- |
| `{{platform}}` | The public origin with a trailing slash, every `...PlatformEndpoint` setting: `http://local.altinn.cloud:8000/` |
| `{{url:<topology component>}}` | The public origin of a topology component, without a trailing slash: `{{url:app}}` is `http://local.altinn.cloud:8000`, `{{url:arbeidsflate}}` `http://af.local.altinn.cloud:8000` |
| `{{host:<topology component>}}` | The public host name of a topology component: `{{host:app}}` is `local.altinn.cloud`, also `pdf`, `workflowEngine`, `arbeidsflate`, `frontendDevServer` |
| `{{alias:<component>}}` | The first network alias of a component: `{{alias:postgres}}` is `postgres` |
| `{{port:<component>}}` | The container port of a component: `{{port:postgres}}` is `5432` |
| `{{certPath}}` | The certificate's path in a container that mounts its directory: `/certs/localtest-accesstoken.pfx` |
| `{{certFileName}}` | The certificate's file name: `localtest-accesstoken.pfx` |
| `{{postgresUser}}`, `{{postgresPassword}}` | `postgres.user` and `postgres.password` |
| `{{appCertPath}}`, `{{appCertKeysFolder}}` | The runner's; `app.accessTokenSigning` only, see [The app section](#the-app-section) |

The topology components are studioctl's (`internal/envtopology/topology.yaml`); the ingress port follows the topology,
so `{{platform}}` changes with it.

## Rules

Adding a component:

1. Add it to `components` (the list order is the order of `env status` and the health wait; `dependsOn` sets the start
   order). Give it a `description`, an `image` (a research image
   is named like the component and built by `.github/workflows/images.yml`), a `dev` build if it is built from this
   repository, a free `hostPort` in 8020 to 8039 and a `healthPath` if it serves HTTP, and `routes` if the browser or
   another component reaches it through the front door. Reach other components through `{{platform}}` (the public
   origin); only Postgres and Redis are reached on the network, by `{{alias:...}}:{{port:...}}`.
2. A database: an init script in the component's `postgres-init/`, a `COPY` line in `localtest/postgres/Dockerfile` and
   the same pair in `postgres.initScripts`.
3. A named volume: list it in `volumes` first.
4. Under `localtest/studioctl/src/cli`, `go test ./...`: the manifest is checked against the schema and `Parse`, and the
   golden file of the environment (`internal/cmd/env/localtestv2/testdata/environment.golden.json`) fails with the
   change. Review the change, then regenerate it with
   `STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/`.
5. Add it to `localtest/README.md` (the folder table and the host ports).

Adding a group: add it to `groups` with a `description`, `onByDefault` (`false` unless every developer needs it) and
`implies` (the groups its components cannot run without), and put its components' `group` on them. A component may
depend only on always-on components and on components of the groups its own group implies, which `Parse` checks.
`everything` implies every group by `impliesAll`, so it needs no change. Describe the group in
`localtest/studioctl/README.md`.

Moving the pin: set `images.tag` to the `sha-<commit>` of a build `images.yml` published, after a run against it.

## Checking by hand

`go test ./internal/localtestv2/manifest/` validates the manifest against the schema with a small validator that knows
exactly the keywords the schema uses. A full JSON Schema validator gives the same answer:

```sh
python3 -m venv /tmp/venv && /tmp/venv/bin/pip install jsonschema
/tmp/venv/bin/python -m jsonschema -i manifest.json manifest.schema.json   # silent when valid
```

CI runs both, with the golden tests and the Aspire package's comparison against them, in
`.github/workflows/manifest-parity.yml`.
