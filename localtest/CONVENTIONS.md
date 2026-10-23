# Conventions of the localtest stack

The rules every part of the stack follows: the manifest, the runtimes (studioctl's `localtest-v2` and the Aspire
package), the localtest app as the front door and the component images. Each section states the rule, the reason and
where the code is. Other documents link here instead of restating them. State as of 2026-10-23 (after tasks 25 to 28, 31 and phase 1 of 35
of [`../TODO.md`](../TODO.md)); where this page and the code disagree, the code is right.

## The manifest

**Rule.** One file,
[`studioctl/src/cli/internal/localtestv2/manifest/manifest.json`](studioctl/src/cli/internal/localtestv2/manifest/manifest.json),
lists the stack in two sections:

```json
{
  "groups": [{ "name": "default", "containers": ["postgres", "register", "…"] }, …],
  "containers": {
    "<name>": { "registry", "tag", "envs", "dependsOn", "volumes", "port"?, "health"? }
  }
}
```

- `groups` names each group's **root** containers. `default` is always on; `dialogporten`, `receipt`, `correspondence`
  and `arbeidsflate` are optional. A group starts its roots and everything they depend on (`dependsOn`, transitively),
  so one group implying another is a dependency, not a group rule: `--with correspondence` starts Dialogporten's WebApi
  and Redis because Correspondence depends on them, but not the Dialogporten Service, GraphQL host or adapter.
  `everything` is not in the file; it is a constant of each runner meaning every optional group. A container no group
  reaches is an error.
- `containers` holds real services only: no app, no routes, no host ports, no build information, no descriptions.
  `envs` are literal strings (no placeholders, no substitution); `port` defaults to 8080 and `health` to `/health`
  (`"tcp"` for Postgres and Redis). `volumes` maps a volume id (`postgres-data`), not a runtime's volume name, to the
  path in the container ([Volumes](#volumes)). An unknown key is an error.
- There is no JSON Schema: the Go reader's test validates the file and `Parse` refuses an invalid one; the Aspire
  package runs the same checks on the same file, which it embeds by relative path.

**Why.** The stack is a list of services; everything both runtimes do alike is runner code, written once in each
runner and held to one golden snapshot, rather than a vocabulary in a file that two readers interpret.

**Code.** The format: [manifest README](studioctl/src/cli/internal/localtestv2/manifest/README.md); the Go reader
`manifest.go` beside it; the selection on the command line `internal/cmd/env/localtestv2/groups.go`
(`--with`/`--without`), in Aspire `WithGroup` and the typed `WithDialogporten()`, `WithReceipt()`,
`WithCorrespondence()`, `WithArbeidsflate()`, `WithEverything()`
([`aspire/Altinn.App.Aspire/src/AltinnLocaltestExtensions.cs`](aspire/Altinn.App.Aspire/src/AltinnLocaltestExtensions.cs)).

## Tags

**Rule.** Every research image carries the same `sha-<commit>` pin (today `sha-6d928ad`); pdf3 and the workflow engine
run the monorepo's `tt02` tag, re-pulled on every start. A pin moves only by
[`tools/bump-tag.sh`](tools/bump-tag.sh)` sha-<commit>`, run by whoever proved that build against the stack, followed by
regenerating the golden snapshot (`STUDIOCTL_UPDATE_GOLDEN=1 go test ./internal/cmd/env/localtestv2/` under
`studioctl/src/cli`) and reviewing its diff. No workflow writes the manifest:
[`images.yml`](../.github/workflows/images.yml) only pushes `sha-<commit>` and, from main, `latest`.
`env up --latest` (Aspire: `AltinnLocaltestOptions.Tag = "latest"`) runs `latest` instead of the pins.

**Why.** A pin is a statement that the stack was run with that build; a workflow cannot make it.

## The runtimes

**Rule.** Both runtimes are thin loaders of the manifest and do the same per container:

| | studioctl `env up -r localtest-v2` | Aspire `AddAltinnLocaltest()` |
| --- | --- | --- |
| Containers | `localtest-v2-<name>` on the network `localtest-v2_network` | resources named as the manifest, children of `altinn-localtest`, persistent containers `<name>-<hash>` |
| Network alias | the manifest name | the manifest name |
| Environment | `envs`, plus the `services__*` variables below | the same |
| Volumes | `localtest-v2-<id>` for each manifest volume id | `altinn-localtest-<id>`: its own, not studioctl's |
| Certificate | the certificate mounts below | the same |
| Start order and health | `dependsOn`; each container's check runs in a short-lived probe container (the Postgres image's bash over `/dev/tcp`) before what depends on it starts | `WaitFor`; the same bash probe through `exec` in the Postgres container |
| Published ports | only `localtest`'s 8080, as 8000 on every interface | the same; Postgres on a loopback port only with `PostgresHostPort` |
| A build from source | `--dev-<container>[=<path>]` | none (`Tag`, or a locally built image by hand) |
| Minimal mode | `--minimal` ([Minimal mode](#minimal-mode)) | `AddAltinnLocaltestMinimal()`, its own resource type |

Run one at a time: both need port 8000.

**Why.** Everything a runtime adds is the same for every container, so it lives in runner code, not in the manifest.

**Code.** studioctl: [`internal/cmd/env/localtestv2/`](studioctl/src/cli/internal/cmd/env/localtestv2/) (`containers.go`,
`health.go`, `dev.go`); Aspire: [`src/Manifest/StackSpec.cs`](aspire/Altinn.App.Aspire/src/Manifest/StackSpec.cs),
`LocaltestStack.cs`, `InNetworkHealthCheck.cs`. Parity: both compare with
`internal/cmd/env/localtestv2/testdata/environment.golden.json`
([`manifest-parity.yml`](../.github/workflows/manifest-parity.yml)).

## Minimal mode

**Rule.** The `minimal` group (`localtest`, `storage`, `workflow-engine`, and `postgres` through `dependsOn`) is the
one group that **replaces** `default` instead of adding to it; like `default` and `everything` it is a constant of
each runner, not an optional group, and `--with`/`--without` are refused with it. The emulated set is derived, not
listed: every HTTP container of `default` that `minimal` does not start (register, profile, authentication,
authorization, access-management, events, resource-registry, notifications, pdf3). The runner gives each of those
`services__<name>__http__0=http://localtest:8080`, the front door's own address, in every started container, and the
front door emulates a service whose address is its own ([`localtest/Emulation/`](localtest/Emulation/README.md)).
Every manifest volume of a started container is a **tmpfs** instead of the named volume (Storage's blobs bounded at
1 GB), and the front door keeps its app routes in memory (`Proxy__AppRoutesFile=`): nothing persists, a stop is a clean
slate, and the `localtest-v2-*` volumes of the default stack are never mounted, so `env reset` has nothing to remove in
the mode and switching back finds the default stack's data intact. A switch between the modes is `env down`, then
`env up`. The state file records the mode; `env status` prints it and the emulated list. `--dev-<container>` works for
the four containers only. **Aspire**: `builder.AddAltinnLocaltestMinimal()` returns a builder of its own resource type
(`AltinnLocaltestMinimalResource`) that no group method takes; the four containers have session lifetime, so an
AppHost's stop is the clean slate, and there is no `reset` command
([package README, "Minimal mode"](aspire/Altinn.App.Aspire/README.md#minimal-mode)).

**What is lost**: authorization (the PDP permits everything, no 403s, authentication levels not enforced), the
Register's real data paths, Profile's settings, Authentication's OIDC provider for seeded clients, Events delivery,
Notifications' pipeline, PDF content, the optional groups. The table and the reasons:
[`../planned-components/minimal-emulation/README.md`](../planned-components/minimal-emulation/README.md), section 5.

**Why.** Quick work and an app's own tests need Storage and the workflow engine (too central to fake) and a platform
that answers; the real components stay the default for fidelity.

**Code.** studioctl: `internal/cmd/env/localtestv2/minimal.go`; the front door: [`localtest/Emulation/`](localtest/Emulation/);
devenv's `tmpfs` mount type (Podman `--tmpfs`, Docker `HostConfig.Tmpfs`); Aspire: `AltinnLocaltestMinimalExtensions.cs`,
`StackSpec.ResolveMinimal`, `LocaltestStack` (`--tmpfs` through `WithContainerRuntimeArgs`, session lifetime). The golden
snapshot has a `minimal` case, which both runtimes' tests compare with.

## The public origin and host names

**Rule.** Everything a browser, an app or a test reaches goes through one origin, `http://local.altinn.cloud:8000`,
and four host names on the same port:

| Host | Goes to |
| --- | --- |
| `local.altinn.cloud` | the platform by path prefix, the apps at `/{org}/{app}/`, the localtest app's own pages and APIs |
| `pdf.local.altinn.cloud` | pdf3 |
| `workflow-engine.local.altinn.cloud` | the workflow engine |
| `af.local.altinn.cloud` | Arbeidsflate (its SPA, which proxies `/api/` to the BFF) |
| `app-frontend.local.altinn.cloud` | the frontend dev server while one is registered; otherwise served like the main host |

The browser needs them in the hosts file (`studioctl env hosts add`). Inside the stack every container resolves these
names and `host.docker.internal` to the container runtime's host gateway (`--add-host <host>:host-gateway`), so a
container reaches the public origin the way a browser does.

**What stays on the public origin** even where a service name would work: token issuers, OIDC discovery documents and
their `jwks_uri`, redirect URIs, links handed to a browser (self links, dialog links, receipt links), cookie domains.
These are values a browser or a token sees, so they must be the address a browser can open.

**Code.** Host routes: [`localtest/Proxy/PlatformRoutes.cs`](localtest/Proxy/PlatformRoutes.cs); extra hosts:
`extraHosts` in `containers.go`.

## The front door

**Rule.** The localtest container is the front door in both runtimes. It listens on 8080 in the container; the runtime
publishes it as 8000.

- **Routes are code**: dedicated hosts first (above), then path prefixes on the main host (`/storage`, `/register`,
  `/profile`, `/authentication`, `/authorization`, `/accessmanagement`, `/events`, `/resourceregistry`,
  `/notifications`, `/receipt`, `/correspondence`, `/dialogporten`, `/dialogporten/graphql` rewritten to `/graphql`,
  `/storage/dialogporten` to the adapter). `GET localtest/api/routes` lists them with each service's address.
- **Upstreams by service name** through `Microsoft.Extensions.ServiceDiscovery` with the core and the configuration
  provider only, **no pass-through**: a destination is `http://<service>`, resolved from `services__<service>__http__0`.
  A service without that variable (its group is off) answers `503` "Component not running", naming the group and the
  `--with` / `.With…()` that starts it; one with an address that does not answer gets `503` "Component not answering"
  with the error. Both are RFC 9457 problem details, or an HTML page for a browser.
- **Apps**: `PUT localtest/api/apps/{org}/{app}` with `{"url": "http://host.docker.internal:<port>"}` routes
  `/{org}/{app}/` to the app and provisions it from the app's own API (see [What the app runner
  pushes](README.md#what-the-app-runner-pushes)); `DELETE` deprovisions it and removes the route; both idempotent.
- **The frontend dev server**: `PUT`/`DELETE localtest/api/routes/frontend-dev-server` routes
  `app-frontend.local.altinn.cloud` to it.
- **`Secure` is stripped** from `Set-Cookie` on app routes only, because app-lib's `keepAlive` sets the runtime cookie
  `Secure` in every environment; this stays until app-lib honours an explicit localtest environment
  ([`../TODO.md`](../TODO.md) task 22).

**Why.** One front door in one image means one route table, one 503 page and one provisioning path for both runtimes;
studioctl-server and the Aspire package carry no proxy of their own.

**Code.** [`localtest/Program.cs`](localtest/Program.cs), [`localtest/Proxy/`](localtest/Proxy/),
[`localtest/Provisioning/`](localtest/Provisioning/); its [README](localtest/README.md#proxy).

## Service discovery

**Rule.**

1. **Every container's name is its network alias** in both runtimes (`storage`, `postgres`, `dialogporten-webapi`).
2. **The runtime injects `services__<name>__http__0=http://<name>:<port>`** into every container for every running
   HTTP container (one with an HTTP health), itself included; Postgres and Redis get none. Not only for `dependsOn`:
   the call graph has cycles (Storage and Authorization, Authorization and Access Management, Register and
   Authorization), and `dependsOn` is start order only. A group that is off has no variables.
3. **.NET components resolve names through the registered discovery**: Register, Authentication, Access Management and
   the Resource Registry natively (`AltinnServiceDefaults`); the others through a three-line `#if LOCALTEST` block
   after `WebApplication.CreateBuilder` (`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(http =>
   http.AddServiceDiscovery())`) in Storage, Profile, Authorization, Events, Notifications, Receipt, Correspondence,
   the Dialogporten adapter and Dialogporten's three hosts. Each component README names its site.
4. **The pass-through provider stays on in the components** until each one's URLs are migrated: today every factory
   client still calls the public origin (the fakes derive their URLs from one `Localtest__<X>__PlatformEndpoint`), and
   pass-through lets such a name fall through to DNS. As each component's settings move to service names
   (`http://register/register/api/v1/`), the provider goes off for it. As of 2026-10-23 no component has migrated; the
   localtest app is the only one without pass-through.
5. **Non-.NET clients use the alias and port directly**: the connection strings (`Host=postgres;Port=5432`,
   `redis://redis:6379/2`), Arbeidsflate's nginx (`arbeidsflate-bff`), pdf3 and the workflow engine.

**Why.** It is how Aspire does it, so the same images run under both runtimes and an Aspire-hosted app gets the same
variables as a container; a configuration-only resolver also makes "not running" a fast, explicit answer.

**Code.** `serviceVariables` in `containers.go`; Aspire's `StackSpec`; the inventory, the call sites and the migration
plan in [`../planned-components/service-discovery.md`](../planned-components/service-discovery.md).

## Port and health

**Rule (target).** An image built here listens on 8080 and answers `GET /health` with 200; the manifest then names
neither. Health is either an HTTP path or `tcp`. Aspire ignores an image's `HEALTHCHECK` for `WaitFor` and Podman's
OCI images drop it, so the runtimes do the check themselves (see [The runtimes](#the-runtimes)).

**Today (transitional)**, read from the manifest:

| Container | Port | Health |
| --- | --- | --- |
| `localtest`, `dialogporten-webapi`, `dialogporten-graphql`, `dialogporten-service` | 8080 (default) | `/health` for localtest; `/health/readiness` for Dialogporten |
| `storage` 5010, `dialogporten-adapter` 5011, `register` 5020, `profile` 5030, `authorization` 5070, `access-management` 5080, `events` 5080, `notifications` 5090, `resource-registry` 5100, `authentication` 5101, `receipt` 5060, `correspondence` 2525 | own port | `/health` (default) |
| `arbeidsflate-bff` | 80 | `/api/readiness` |
| `arbeidsflate` (nginx) | 80 | `/` |
| `pdf3` (monorepo image) | 5031 | `/health/ready` |
| `workflow-engine` (monorepo image) | 9090 | `/api/v1/health/ready` |
| `postgres` | 5432 | `tcp` |
| `redis` | 6379 | `tcp` |

Each component image moves to 8080 in its own small change and the manifest drops its `port` then (section 7 of
[`service-discovery.md`](../planned-components/service-discovery.md#7-port-and-health-convention) lists the change per
image). pdf3 and the workflow engine are not built here and keep their values.

## The certificate

**Rule.** One test certificate signs and validates every platform access token:
[`certs/localtest-accesstoken.pfx`](certs/README.md). studioctl embeds a byte-identical copy, writes it under its data
directory (`~/.altinn-studio/localtest-v2/certs/`; Aspire: `<DataDirectory>/certs`, default `~/.altinn-app-aspire`)
and mounts the directory read-only at **`/certs`** in every container, so the components read
`/certs/localtest-accesstoken.pfx` (`AccessTokenSettings__AccessTokenSigningCertificateFullPath` and the localtest
app's `Localtest__AccessTokenCertificatePath` in the manifest). The same file is also mounted at
**`/accesstoken/accesstokencredentials.pfx`** in every container, because Events, Notifications and Receipt never bind
`AccessTokenSettings` and read the production resolver's default path; that mount goes once those three images link
the path to `/certs`. The app runners point the app at the host copy.

**Why.** The production token handlers run unchanged; the key is public test material.

**Code.** `certs.go` and `containerVolumes` in `containers.go`; Aspire's `LocaltestCertificate.cs`.

## Volumes

**Rule.** Data lives in five named volumes. The manifest names each by an **id**; each runtime names the volume by
its own prefix and the id: studioctl `localtest-v2-<id>`, Aspire `altinn-localtest-<id>` (the default parent
resource's name and a hyphen, whatever the parent is called). The same id on several containers is one volume.

| Id | Container | Path | studioctl | Aspire |
| --- | --- | --- | --- | --- |
| `postgres-data` | postgres | `/var/lib/postgresql` | `localtest-v2-postgres-data` | `altinn-localtest-postgres-data` |
| `policy-store` | authorization, access-management, resource-registry | `/policy-store` | `localtest-v2-policy-store` | `altinn-localtest-policy-store` |
| `storage-blobs` | storage | `/data/blobs` | `localtest-v2-storage-blobs` | `altinn-localtest-storage-blobs` |
| `localtest-data` | localtest (app routes, the dev-server route) | `/data` | `localtest-v2-localtest-data` | `altinn-localtest-localtest-data` |
| `correspondence-blobs` | correspondence | `/mnt/storage` | `localtest-v2-correspondence-blobs` | `altinn-localtest-correspondence-blobs` |

An id is lower-case letters and digits in hyphen-separated words; both readers refuse any other. `env reset -r
localtest-v2` deletes the `localtest-v2-*` volumes only, Aspire's `reset` command on `altinn-localtest` the
`altinn-localtest-*` volumes only. Postgres runs its init scripts only on an empty volume, so each runtime's databases
are created separately, on its first start or after its reset; a component that adds a script needs a reset (in each
runtime) or the script by hand. studioctl's names are the ones it used before the ids, so its data carries over.
studioctl removes each container with its anonymous volumes; no image in the stack needs one, since the runners mount
every data path (none of our images declares a `VOLUME`; Postgres and Redis inherit one from their base images, which
is removed with the container).

**Why.** The runtimes shared the volumes until task 31, and a stopped Aspire container still holding a shared volume
blocked studioctl's `env reset` twice. With a prefix each, the runtimes' data no longer mixes: a reset in one leaves the
other alone.

**Code.** The ids and their check: `manifest.go` (`VolumeIDs`, `volumeID`); studioctl's prefix `volumePrefix` in
`internal/cmd/env/localtestv2/containers.go`, used by `containerVolumes` and `Env.Reset`; Aspire's
`StackSpec.VolumePrefix`, used by `StackSpec.Resolve` and `LocaltestReset`. The golden snapshot states studioctl's
prefix (`volumePrefix`), and Aspire's comparison matches mounts by id.

## What the app runner pushes

**Rule.** The app is not in the manifest. `studioctl run` and Aspire's `AddAltinnApp` start it with the same
environment ([`internal/cmd/app/env.go`](studioctl/src/cli/internal/cmd/app/env.go), Aspire's
`Manifest/AppEnvironment.cs`, both held to `internal/cmd/app/testdata/app-environment.golden.json`;
`studioctl app env` prints it). Each value is a default the app's own environment overrides:

- `ASPNETCORE_ENVIRONMENT=Development`, `STUDIOCTL_APP_RUN=1`, `Kestrel__EndPoints__Http__Url`;
- every `PlatformSettings__Api*Endpoint` on the public origin (`http://local.altinn.cloud:8000/<prefix>/api/v1/`),
  `ApiPdf2Endpoint` on `http://pdf.local.altinn.cloud:8000/pdf`, `ApiWorkflowEngineEndpoint` on
  `http://workflow-engine.local.altinn.cloud:8000/api/v1/`; `AppSettings__OpenIdWellKnownEndpoint`,
  `GeneralSettings__ExternalAppBaseUrl` and `GeneralSettings__HostName`; `OTEL_EXPORTER_OTLP_ENDPOINT` (Aspire sets its
  dashboard's);
- the secrets contract `RUNTIME_APP_SECRETS_DIR`, `RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME`,
  `RUNTIME_APP_SECRETS_APPCODES_FILENAME` (and `ALTINN_KEYS_DIRECTORY` for a container run);
- `AppSettings__AppFrontendAssetBaseUrl` with `--dev-frontend` / `WithFrontendDevServer`;
- against localtest-v2 only: the certificate (`AccessTokenSettings__AccessTokenSigningCertificateFullPath`,
  `…KeysFolder`, `…FileName`) and the Arbeidsflate links `PlatformFrontendSettings__ArbeidsflateInboxUrl`
  (`http://af.local.altinn.cloud:8000/`), `…DialogUrl` (`…/inbox/{dialogId}`), `…ProfileUrl` (`…/profile`) and
  `…PartiesUrl` (`…/profile/parties`).

The Arbeidsflate links are read only by the monorepo backend (`src/App/backend`, app-lib's `PlatformFrontendSettings`)
and the monorepo frontend with its local-host change; the released app-frontend v4 bundle hard-codes the start page for
a local host, and changing that needs an upstream PR (task 28 in [`../TODO.md`](../TODO.md)).
`AccessManagementChangeAndRedirectUrl` stays unset (the stack's Access Management does not serve
`reportee/changeandredirect`).

Then the runner registers the app with the front door (`PUT localtest/api/apps/{org}/{app}` with
`http://host.docker.internal:<port>`): studioctl-server's discovery does it for every app it finds, a `studioctl run`
and an app started from an IDE alike, again when the app restarts or the front door no longer lists it, and
`studioctl run` waits until `GET localtest/api/apps` lists the app as provisioned (a process run only:
`run --mode container` is refused against localtest-v2); `AddAltinnApp` does it on the app's ready event. The provisioning steps are in
[`README.md`](README.md#what-the-app-runner-pushes). Per app are the port, the registration (route and test users), the
secrets directory `apps/<org>/<app>/secrets` and the `provision`/`deprovision` commands; the rest of the environment is
the same for every app, and several apps run against one stack ([Several
apps](aspire/Altinn.App.Aspire/README.md#several-apps)).
