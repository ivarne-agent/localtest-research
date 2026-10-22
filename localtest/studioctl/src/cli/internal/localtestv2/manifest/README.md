# The localtest-v2 manifest (v2)

`manifest.json` lists the stack's containers and the groups that select them. studioctl's `localtest-v2` runtime
(`internal/cmd/env/localtestv2`) starts the stack from it; the file is embedded in the binary. There is no schema: the
package's test (`manifest_internal_test.go`) validates the file, and `Parse` refuses an invalid one.

```json
{
  "groups": [
    { "name": "default", "containers": ["postgres", "register", "…"] },
    { "name": "dialogporten", "containers": ["dialogporten-graphql", "dialogporten-service", "dialogporten-adapter"] }
  ],
  "containers": {
    "storage": {
      "registry": "ghcr.io/ivarne-agent/localtest-research/localtest-storage",
      "tag": "sha-f76b62b",
      "port": 5010,
      "envs": { "GeneralSettings__PlatformEndpoint": "http://local.altinn.cloud:8000/" },
      "dependsOn": ["postgres"],
      "volumes": { "localtest-v2-storage-blobs": "/data/blobs" }
    }
  }
}
```

## `groups`

A list of `{name, containers}`. `containers` are the group's root containers; a group starts them and everything they
depend on (`dependsOn`, transitively). `default` is always on; the others are what `env up --with <group>` adds and
`--without <group>` removes. `everything` is not in the file: it is a constant of the runner meaning every group. A
container no group reaches is an error. One group implying another is a dependency, not a group rule: `--with
correspondence` starts Dialogporten's WebApi and Redis because Correspondence depends on them.

## `containers`

A map from the container's name to:

| Key | Meaning |
| --- | --- |
| `registry` | The image repository, with the registry host |
| `tag` | The image tag (a digest may follow: `7.2-alpine@sha256:…`). The research images all carry one `sha-<commit>` pin |
| `envs` | Environment variables, literal strings. No placeholders: the public origin is written out (`http://local.altinn.cloud:8000/`), the data stores by name and port (`Host=postgres;Port=5432`, `redis://redis:6379/2`), the certificate as `/certs/localtest-accesstoken.pfx` |
| `dependsOn` | Containers that start, and are healthy, before this one |
| `volumes` | Named volume to path in the container; `env reset` deletes them |
| `port` | The container's HTTP (or TCP) port; default 8080 |
| `health` | An HTTP path answering 200 when healthy (default `/health`), or `"tcp"` for a port that accepts connections (Postgres, Redis) |

Nothing else is allowed (an unknown key is an error): no descriptions, routes, host ports, commands, health-check
commands or build information. A container's notes belong in its component's README; a command belongs in its image
(`localtest/postgres/Dockerfile`, `localtest/redis/Dockerfile`); a dev build is `env up --dev-<container>=<path>`.

**Transitional (task 27a):** the component images still listen on their own ports (5010, 5020, …), so most containers
name their `port`; the default 8080 applies to the localtest app and Dialogporten's hosts. The per-component image
changes of task 25 move them to 8080 and drop the key. Redis still runs the upstream image (by digest) until
`localtest/redis/Dockerfile` is published and pinned.

## What the runner adds

Every container gets, besides its `envs`:

- `services__<name>__http__0=http://<name>:<port>` for every running container with an HTTP health (not Postgres or
  Redis), itself included: service discovery as Aspire does it. A group that is off has no variables, so the front
  door answers "not running" for it.
- its name as network alias, the public hosts (`local.altinn.cloud`, `pdf.`, `workflow-engine.`, `app-frontend.`,
  `af.`) and `host.docker.internal` pointed at the host gateway;
- the shared certificate directory read-only at `/certs`, and (until the Events, Notifications and Receipt images link
  it) the certificate file at `/accesstoken/accesstokencredentials.pfx`.

Only the `localtest` container publishes a port: 8000 (the public origin) onto its 8080. The app's environment is not
in the manifest: it is runner code (`internal/cmd/app/env.go`), held by `internal/cmd/app/testdata/app-environment.golden.json`.

## Changing it

- Regenerate the snapshot after any change and review the diff:
  `STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/` (under `src/cli`).
- Tags change only in a separate, tested step: the images workflow never writes the manifest; whoever proved a build
  runs `localtest/tools/bump-tag.sh sha-<commit>` and regenerates the snapshot.
- The Aspire package (`localtest/aspire/Altinn.App.Aspire`) embeds this file by relative path and compares what it
  builds with the snapshots above (`.github/workflows/manifest-parity.yml`).
