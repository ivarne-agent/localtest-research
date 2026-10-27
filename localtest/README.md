# localtest MVP

Local platform for Altinn apps, built from vendored production services and small fakes. Each component runs the
production source at a pinned upstream commit; local behaviour comes from `#if !LOCALTEST` switches and an adjacent
`fakes/` folder, never from edits to the vendored code. Design:
[`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md). The rules every part
follows (the manifest, the front door, service discovery, ports and health, the certificate, the volumes, what the app
runner sets) are in [`CONVENTIONS.md`](CONVENTIONS.md).

**Two runtimes, one manifest.** The stack runs under studioctl (`studioctl env up -r localtest-v2`, [Run it](#run-it))
or as container resources of a .NET Aspire AppHost through the hosting package
[`aspire/Altinn.App.Aspire/`](aspire/Altinn.App.Aspire/README.md) (`AddAltinnLocaltest()` or `AddAltinnLocaltestMinimal()`, `AddAltinnApp<T>()`). Both
are thin loaders of the [manifest](studioctl/src/cli/internal/localtestv2/manifest/README.md): the same containers,
environment, `services__*` variables, volumes and certificate, and the localtest container as the front door, the only
published port (8000 onto its 8080). Routes, the `503` pages and the app registration therefore behave the same under
both, and [`manifest-parity.yml`](../.github/workflows/manifest-parity.yml) holds both to studioctl's golden files. What
differs: `--dev-<container>` builds (studioctl only), the health mechanism (probe containers, or `exec` into Postgres),
the container names (`localtest-v2-<name>`, or Aspire's `<name>-<hash>`) and the dashboard. Run one at a time: both need
port 8000.

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory store: a built-in seed plus the `testData.json` each app runner posts (see its README), and the KRR stand-in |
| `components/profile/` | Production Profile on the shared Postgres (`/profile`): user profiles for apps, Authentication, the PDP and Access Management, the contact point lookups of Notifications and Correspondence, Arbeidsflate's favourites, settings, address verification, professional notification settings and organisation addresses. Test users' contact details, settings and organisations' official addresses come from the Register's test data through stand-ins for KRR and brreg and Profile's own sync jobs (every minute; settings and organisation addresses seeded once, user changes win); `profile/api/localtest/` shows the rows and runs the sync |
| `components/authentication/` | Production Authentication on memory instead of Postgres; it signs in against the localtest app's fake ID-porten and trusts its fake Maskinporten, so its own OIDC client, token validation, Profile client and exchanges run. It is also the OpenID Connect provider for registered clients (authorization code with PKCE, refresh, logout), with `localtest-smoke` and `arbeidsflate-localtest` seeded; those tokens carry `pid` |
| `components/authorization/` | Production PDP with app policies in the policy store shared with Access Management; roles, access packages, delegations and authorized parties from Access Management, resources and resource policies from the Resource Registry |
| `components/access-management/` | Production Access Management on Postgres: imports parties and roles from the Register, takes instance delegations from apps, syncs resources from the Resource Registry, serves the PDP's PIP and authorized parties |
| `components/storage/` | Production Storage on Postgres with file-backed blobs and file scan marked clean; application metadata and texts pushed by the app runner |
| `components/events/` | Production Events on the shared Postgres, emptied on every start: apps publish events, subscriptions are validated and delivered inside the stack, webhooks to anything outside it are captured; `events/api/localtest/` shows it all |
| `components/notifications/` | Production Notifications (the orders API host) on the shared Postgres: apps, Authentication and Correspondence order e-mail and SMS as in production, recipients resolve through production Profile, and every message is captured in an inbox per address or number (`notifications/api/localtest/`) and delivered by the production status machine; nothing is sent |
| `components/resource-registry/` | Production Resource Registry on the shared Postgres: app resources computed from Storage's application list and the pushed policies (no cache), a built-in seed of other resources, the `updated` subject feed, the org list from altinncdn.no; the runner triggers the subject reload as Designer does |
| `components/dialogporten/` | **Optional group** (`env up --with dialogporten`): production Dialogporten WebApi, GraphQL and Service hosts on a `dialogporten` database in the shared Postgres, with a Redis; service-owner API with org tokens, resource checks against the Resource Registry, events to the Events component, GraphQL under `/dialogporten/graphql` (CORS for Arbeidsflate's origin). The provisioner's `sync` makes a pushed app known at once; `dialogporten/api/v1/localtest/` clears the caches, lists and deletes dialogs. Dialogs persist until `env reset` or that delete |
| `components/dialogporten-adapter/` | **Optional group** (`dialogporten`, with Dialogporten): the production Dialogporten adapter. Storage's outbox (always on) hands every instance change to its anonymous `storage/dialogporten/api/localtest/enqueue`, and the production sync turns the instance into a dialog (attachments, activities, transmissions with the adapter's own receipt, delete with the dialog token) and writes `dialog.id` back. Without the group Storage logs one line per change and drops it |
| `components/receipt/` | **Optional group** (`env up --with receipt`): production Altinn Receipt, the platform receipt for archived instances at `/receipt/{partyId}/{instanceGuid}` (the target of Dialogporten's go-to action for archived instances), reading Storage, Register and Profile with the user's cookie and a platform access token signed with the shared certificate; one fake rewrites Storage's attachment links to the public origin. Stateless, nothing provisioned. Open it on `local.altinn.cloud`, not `localhost` |
| `components/correspondence/` | **Optional group** (`env up --with correspondence`, which also starts the Dialogporten WebApi and Redis it depends on, not the rest of the `dialogporten` group): production Altinn Correspondence (the API host, Hangfire inside it) on a `correspondence` database in the shared Postgres, sharing Dialogporten's Redis (database 1), attachments in a file store on a volume, about 120 MB. A service owner's correspondence runs end to end on production code: the Resource Registry seed's correspondence resources, KRR (with the test data's reservations) and production Profile's lookups, a dialog in Dialogporten, the e-mail in the Notifications capture; `correspondence/api/localtest/` shows correspondences, their Hangfire jobs and the migration count. Apps' sends (app-lib's credential, the signing flow), the recipient's dialog-token calls and, with the `arbeidsflate` group, an attachment clicked in the inbox (Correspondence's own OIDC client in Authentication, no picker with a session) run on production code (see its README) |
| `components/arbeidsflate/` | **Optional group** (`env up --with arbeidsflate`, which also starts the BFF and the Dialogporten GraphQL host, WebApi and Redis it depends on, not the Dialogporten Service or adapter): production Arbeidsflate, the Altinn inbox, on its own host `http://af.local.altinn.cloud:8000` (a hosts entry for the browser, `studioctl env hosts add`): the SPA behind nginx, which proxies `/api/` to the BFF; the BFF signs users in through production Authentication's OIDC provider (the seeded client `arbeidsflate-localtest`), keeps sessions in Redis (database 2) and its own rows in an `arbeidsflate` database, proxies GraphQL to Dialogporten with the user's token and serves the profile page from production Profile. Upstream's tree unmodified plus three `localtest:` patches applied at image build (discovery over http, the party cookies' domain, the BFF's cookies `Secure` only over https). With the `correspondence` group a correspondence's attachment downloads from the inbox (11b in its README) |
| `components/shared/` | Source for fakes more than one component copies (the `IMaskinportenService` fake), with its own smoke test; not an image |
| `localtest/` | The localtest app (C#, Native AOT): the front door in both runtimes (routes by host and path prefix to the services by name, the `503` pages, the app registration that routes and provisions an app, the frontend dev server's route, `Secure` removed from an app's cookies; [`CONVENTIONS.md`](CONVENTIONS.md#the-front-door)), the fake ID-porten (test-user picker, end-session endpoint) and Maskinporten that production Authentication trusts, the old localtest's start page and `/Home` test helpers, and a headless code-flow helper (`localtest/api/authentication/token`) for production Authentication's OIDC tokens with `pid` |
| `testdata/` | The `testData.json` format the stack reads (the Register's seed and every app's file): field reference, consumers, merge rules and the JSON Schema `test-users.schema.v3.json` |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate them with the production token handlers |
| `aspire/Altinn.App.Aspire/` | The Aspire hosting package: the manifest's containers as persistent containers of an AppHost, `AddAltinnApp<Projects.App>(localtest)` with `studioctl run`'s environment and the front-door registration; published to GitHub Packages by `.github/workflows/aspire-package.yml` |
| `aspire/samples/app/` | The Aspire sample: the `ttd/testapp` fixture completed into an app on app-lib 8.12.15 (`App/`) and the single-file AppHosts that run it with a second copy, on the full stack (`AppHost/apphost.cs`) and in minimal mode (`AppHost/apphost-minimal.cs`) |
| `studioctl/` | The studioctl source vendored from `Altinn/altinn-studio` with the `localtest-v2` runtime on top, built from here until it is upstreamed (see its [README](studioctl/README.md) and [UPSTREAM.md](studioctl/UPSTREAM.md)) |
| `tools/bump-tag.sh` | Moves every research image's `sha-<commit>` pin in the manifest to one build ([`CONVENTIONS.md`](CONVENTIONS.md#tags)) |
| `tools/vendor-sync.py` | The vendored copies against upstream: `check` proves each `vendor/` is upstream at its pin plus `LOCALTEST` blocks, `status` shows how far upstream has moved, `sync` stages a new pin for an agent to finish ([`docs/vendor-sync.md`](docs/vendor-sync.md)); the pin and paths are the metadata block in each `vendor/UPSTREAM.md` |
| `reviews/` | Reviews of how each component is localtestified: vendored code fidelity, Dockerfile, fakes, differences from production, findings (dated records) |
| `smoke/apps/` | The `ttd/testapp` fixture (metadata, texts, policy) the smoke tests push |
| `smoke/lib/containers.sh` | The smoke tests' container names under either runtime, and requests from inside the stack's network ([Tests](#tests)) |
| `smoke/workflow-engine.sh` | The workflow engine image studioctl runs, on its own with a fresh Postgres ([Tests](#tests)); the engine is not vendored |
| `postgres/`, `postgres-init/` | The shared Postgres image: Postgres 18 with every component's database init script baked in |
| `redis/` | The Redis image Dialogporten, Correspondence and Arbeidsflate share |
| `run-app.sh`, `stop-apps.sh` | Start an app from a source checkout against the stack (build, run, then one `PUT localtest/api/apps/{org}/{app}`) and stop all of them (`DELETE`); `studioctl run` does the same |
| `run-cypress.sh` | Run the app-frontend Cypress suite from the monorepo against the stack |
| `run-platform-regression.sh` | The six app-frontend specs that once failed because of the platform ([Tests](#tests)) |
| `build-pdf3.sh` | Build a pdf3 worker image from an altinn-studio checkout (`localtest-pdf3:dev`) for the old `localtest` runtime (`STUDIOCTL_IMAGE_PDF3`); under localtest-v2 run a pdf3 build with `--dev-pdf3=<path>` instead |
| `smoke-test.sh` | End-to-end check of the running stack through the front door |
| `docs/verify-after-sync.md` | The checklist for verifying everything after a sync (a vendored component, studioctl, the image pins, the Aspire package): static checks, machine setup, the stack levels with their expected numbers, known false alarms, cleanup |

No component publishes a host port: everything is reached through `http://local.altinn.cloud:8000` and its host names,
and inside the stack the containers reach each other by name ([`CONVENTIONS.md`](CONVENTIONS.md#service-discovery)).
Fakes reach the vendored services the way their production counterparts do: the picker resolves users through the
Register API, and `testData.json` is Register seed data, not a file other components read. Today's localtest in
`altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a reference for which behaviours the fakes
must provide.

## What the app runner pushes

In production Altinn Studio pushes an app's artefacts to the platform at deploy time; locally the front door does it
when an app is registered with it. The runners start the app with the environment in
[`CONVENTIONS.md`](CONVENTIONS.md#what-the-app-runner-pushes) and make one call,
`PUT localtest/api/apps/{org}/{app}` with the app's address (`http://host.docker.internal:<port>`, or
`http://<org>-<app>:5005` for a `studioctl run --mode container` app on the stack's network): studioctl-server's
discovery for every app it finds (a `studioctl run` and an app started from an IDE alike; again when the app comes back
on another port; `DELETE` when it is gone), the Aspire package on the app's ready event (and the dashboard's
`provision` / `deprovision` commands), `run-app.sh` by hand. `studioctl run` waits until `GET localtest/api/apps` lists
the app as provisioned. The localtest app routes `/{org}/{app}/` to the app, reads its artefacts from the app's own API
and runs, calling the platform by service name
([`localtest/README.md`](localtest/README.md#registering-an-app-localtestapiapps-anonymous)):

| Step | Endpoint | What it does |
| --- | --- | --- |
| 1 | `PUT register/api/localtest/apps/{org}/{app}` with the app's `App/wwwroot/testData.json` as the body (empty body: no test data) | Register stores the test data as posted and rebuilds its store (built-in seed plus every registered app); it fetches nothing. The file format, its JSON Schema and the merge rules: [`testdata/`](testdata/README.md) |
| 2 | `POST storage/api/v1/applications?appId={org}/{app}` (or `PUT storage/api/v1/applications/{org}/{app}` when it exists) with `App/config/applicationmetadata.json`, and `POST`/`PUT storage/api/v1/applications/{org}/{app}/texts[/{lang}]` with each `App/config/texts/resource.{lang}.json` | Production Storage endpoints, with the Studio token (`GET /Home/GetTestStudioToken`: Designer's own `studio`/`studio.designer` platform access token exchanged through the production `exchange/altinnstudio`), exactly Designer's credential |
| 3 | `POST authorization/api/v1/policies?org={org}&app={app}` with `App/config/authorization/policy.xml`, `[ORG]`/`[org]`/`[APP]`/`[app]` replaced as Designer's `ReplacePolicyPlaceholderTokens` does | Production PDP endpoint (Studio token); the policy lands in the policy store shared with Access Management, which reads it for delegable rights |
| 3b | `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` (when the app has a policy) | Production Resource Registry endpoint, anonymous, as Designer calls it at deploy: reads the pushed policy from the policy store and upserts the app's subject pairs for the `updated` feed. The app's resource itself is computed from Storage's application list |
| 4 | `POST accessmanagement/api/localtest/refresh` | Access Management re-imports parties and external roles from the Register's streams (production code, full re-poll), runs the production resource sync against the Resource Registry component (service owners, `app_{org}_{app}` resources and their policy subjects from its `updated` feed, read from the epoch each time), then applies the seed's own roles, Altinn 2 role codes and access packages and prunes what the seed no longer grants |
| 5 (optional) | `POST dialogporten/api/v1/localtest/sync`, only when the `dialogporten` group is up (when `dialogporten-adapter` has a `services__` address; skipped otherwise) | Dialogporten runs the Janitor's two production syncs (subject-resource pairs from the registry's `updated` feed, resource policy information) and clears its named caches, so the app's resource and subjects are known to Dialogporten at once instead of after its 20-minute resource cache. A failure is logged and does not fail the provisioning; the sync runs only on a registration, so after the group comes up later register the app again See `components/dialogporten/README.md` |

No component fetches from the running app or watches another. The Register's store is rebuilt only by these calls, so
an edited `testData.json` becomes visible after the next registration. Profile is not called: its KRR sync,
organisation address sync and settings seed read the Register's test data every minute (task 14), or at once with
`POST profile/api/localtest/sync`. An app path no runner has registered is the localtest app's 404.

**The inbox links** (tasks 28 and 42). The monorepo app frontend shows a platform link only when its
`PlatformFrontendSettings` URL is configured; it no longer derives one from the page's host. Against localtest-v2 both
runners therefore set the inbox link: with the `arbeidsflate` group up, the Arbeidsflate links
(`PlatformFrontendSettings__Arbeidsflate{Inbox,Dialog,Profile,Parties}Url`) go to `http://af.local.altinn.cloud:8000`,
so "back to inbox" and the receipt's inbox link open the local Arbeidsflate; without the group (minimal mode included)
only `PlatformFrontendSettings__ArbeidsflateInboxUrl` is set, to the front door's Home
(`http://local.altinn.cloud:8000/`), and the profile and parties links are hidden. Both set
`PlatformFrontendSettings__LogoutUrl` to the platform's logout
(`http://local.altinn.cloud:8000/authentication/api/v1/logout`), which signs the user out and lands on Home. Only the
monorepo backend and frontend read them, the released v4 bundle still links the start page
([`CONVENTIONS.md`](CONVENTIONS.md#what-the-app-runner-pushes)). An empty
`PlatformFrontendSettings__ArbeidsflateInboxUrl=` in the app's environment hides the inbox links. The receipt's deep
link needs the instance's dialog, which only the Dialogporten adapter creates, so use `--with arbeidsflate --with
dialogporten`; Arbeidsflate asks for level 3, so sign in to the app at level 3 to land in the inbox signed in. Verified
in a browser on 2026-10-23 (screenshots in [`reviews/task28-back-to-inbox/`](reviews/task28-back-to-inbox/)).

## Where localtest is faster than production

Production runs caches, schedules and queues that make a change visible minutes later; a tester should not wait for
them, but should know they exist, because the app behaves differently in production for that while. Every place where
the stack is faster than production, with both values. A new component adds its rows.

| Where | Production | Localtest |
| --- | --- | --- |
| Dialogporten's named caches (Resource Registry list 20 min, subject-resource pairs 20 min, PDP decisions and authorized parties 15 min, others 5 min to 24 h) and the Janitor's subject and policy syncs | Wait for the entry to expire; the syncs run on the Janitor's schedule | The provisioner's step 5 syncs and clears them after every app push; `POST dialogporten/api/v1/localtest/caches/clear` clears all fourteen in all three hosts at any time (`components/dialogporten/`) |
| The Resource Registry's copy of Storage's application list | 10-minute memory cache: `resourcelist` and the app resources lag a deploy by up to ten minutes | No cache (task 5a): a pushed app is a resource on the next request (`components/resource-registry/`) |
| Access Management's Register import and resource sync | A hosted service every 2 minutes | On every provisioning (`POST accessmanagement/api/localtest/refresh`, step 4) and at start |
| The PDP's app policy | Cached for minutes after a deploy | Read from the policy store on every decision (`components/authorization/`); its 1-minute profile and party caches and 5-minute delegation-change cache are production's, cleared at once by `POST authorization/api/localtest/refresh` |
| Authentication's signing keys of the ID-porten and Maskinporten issuers | Metadata and JWKS cached 12 hours | Not cached, so a restarted localtest app's keys apply at once (`components/authentication/`) |
| Storage's application metadata, text and app title caches | 300, 3600 and 3600 seconds | 30 seconds each (`components/storage/`) |
| Events: registration, delivery and retries | Azure Service Bus and Storage queues; the claim loop idles 2 s; subscriptions and main units cached 3600 s; outbound retries from 30 s up to 12 h | In-process Wolverine queues, no broker hop; the claim loop idles 1 s; subscriptions cached 5 s, main units 30 s; retries from 2 s, ten over about seven minutes (`components/events/`). A Dialogporten event was registered within two seconds of being published |
| Notifications: sending, the provider's result and the delivery report | Process loops with 30 tasks and a 30 s idle delay; the e-mail and SMS providers report back after seconds to hours; SMS only between 09 and 17 | 1 task and a 1 s idle delay per loop; the responder applies the send result after 100 ms and `Delivered` after 500 ms (`ResultDelayMs`, `DeliveryReportDelayMs`); SMS at any hour (`components/notifications/`) |
| Metrics export (`OTEL_METRIC_EXPORT_INTERVAL`), with `--with monitoring` and in minimal mode | Every 15 s: the workflow engine's and pdf3's deployments and the Designer chart set `15000` (altinn-studio `src/Runtime/workflow-engine-app/infra/kustomize/base/deployment.yaml`, `src/Runtime/pdf3/infra/core/base/worker.yaml`, `charts/altinn-designer/templates/deployment.yaml`) | Every 5 s: studioctl sets `5000` on every container it gives the OTLP variables, unless the container's own `envs` set it (manifest README, "What the runner adds"); a request shows on a dashboard within seconds |
| Correspondence: Dialogporten's dialog-token signing keys | Fetched at start and every 12 hours (`EdDsaSecurityKeysCacheService`) | Fetched again when a token names an unknown key, at most every 10 s (`components/correspondence/`) |
| Correspondence: malware scan of an uploaded attachment | Defender for Storage, the result through Event Grid (seconds to minutes) | Simulated "No threats found" through a Hangfire job within seconds (upstream's own Development behaviour) |
| Correspondence: reminder notification | 7 days after the first | 1 day (upstream's non-production value, unchanged) |
| Correspondence: unread-confidential reminder check | 7 days after publish | 1 minute (upstream's non-production value, unchanged) |
| Correspondence: minimum attachment expiry | 14 days | 1 day (upstream's non-production value, unchanged) |
| Correspondence: notification delivery | Notifications delivers through the e-mail and SMS providers (minutes) | Captured and `Delivered` within a second; Correspondence's own delivery check still runs 5 minutes after the send time (unchanged constant). Not faster: Hangfire's 5 s schedule polling, its retry schedule for a failed dialog, Correspondence's 10-minute Resource Registry and 24-hour Register caches |
| Profile's Register id cache (users, parties, main units) | 600 s | 10 s (`components/profile/`) |
| Profile's database migrations | A Kubernetes pre-upgrade hook before the deployment | At every start, before the host listens |
| Arbeidsflate's BFF migrations | A separate job with the BFF image (`MIGRATION_RUN=true`) before the deployment | At every start of the BFF container, before the server (`components/arbeidsflate/bff-start.sh`) |
| Profile's KRR sync (persons' e-mail, mobile number, reservation) | `KrrSyncWaitTimeInMinutes` from the deployment (upstream default 10) against Kontaktregisteret's change log | Every minute against the Register's KRR stand-in, plus at start and on `POST profile/api/localtest/sync` |
| Profile's organisation address sync (an address written through Profile becomes visible in the lookups when brreg reports it back) | brreg's change log, `OrgSyncWaitTimeInMinutes` from the deployment (upstream default 10) | Every minute against the brreg stand-in, which accepts every write, or at once with `POST profile/api/localtest/sync` |
| Storage's outbox to the Dialogporten adapter | Low-priority events (saves, process steps) batched 60 s, high-priority 5 s, urgent (create, delete) 0 s; then Azure Service Bus | 2 s / 0 s / 0 s (`WolverineSettings:LowPriorityDelaySecs`, `HighPriorityDelaySecs` in Storage's fakes), then an HTTP post to the adapter's in-process queue (`components/storage/`, `components/dialogporten-adapter/`) |

## Run it

The stack runs through studioctl's `localtest-v2` runtime, built from [`studioctl/src/cli`](studioctl/src/cli) in this
repository (`make dev-install`, see [`studioctl/README.md`](studioctl/README.md)) until it is upstreamed. It starts
the manifest's containers in dependency order, each checked healthy before what depends on it starts, publishes the
front door on `http://local.altinn.cloud:8000`, and has studioctl-server register every app it discovers:

```sh
studioctl env up -r localtest-v2                         # the default group, pinned images; --latest runs :latest
studioctl env up -r localtest-v2 --with dialogporten     # also Dialogporten's three hosts, Redis and the adapter
studioctl env up -r localtest-v2 --with receipt          # also the platform receipt
studioctl env up -r localtest-v2 --with correspondence   # also Correspondence, Dialogporten's WebApi and Redis
studioctl env up -r localtest-v2 --with arbeidsflate     # also the inbox on http://af.local.altinn.cloud:8000, its BFF,
                                                         # Dialogporten's GraphQL host, WebApi and Redis
studioctl env up -r localtest-v2 --with everything       # every optional group
studioctl env up -r localtest-v2 --minimal               # four containers (postgres, storage, workflow-engine, localtest),
                                                         # the rest emulated in the front door, nothing persisted
sudo studioctl env hosts add -r localtest-v2             # once: the host names for the browser
studioctl run                                            # in an app checkout
```

`--with <group>` starts the group's root containers and what they depend on; `--without <group>` leaves out that
group's own roots (a container another enabled group depends on still starts); a later `env up` converges to the new
choice ([`CONVENTIONS.md`](CONVENTIONS.md#the-manifest)). A group that is off has no containers and no `services__`
address, so its routes answer `503` "Component not running" with the `--with` command; a container that is on but does
not answer gets `503` "Component not answering". `env down`, `env status`, `env logs [<container>]` and
`env reset --yes` (deletes the runtime's `localtest-v2-*` volumes only; an Aspire AppHost's `altinn-localtest-*` stay,
[`CONVENTIONS.md`](CONVENTIONS.md#volumes)) take `-r localtest-v2` too.

**Minimal mode.** `--minimal` replaces the default group with `minimal`: the real Storage and workflow engine on an
ephemeral Postgres, and the front door emulating Register, Profile, Authentication, a permit-all PDP, Access
Management, Events, Notifications and the PDF service (one static page) in memory. Every volume is a tmpfs, so each
start is a clean slate; `--with`/`--without` are refused, `env status` names the mode. For quick work and an app's own
tests, not for authorization or platform bugs ([`CONVENTIONS.md`](CONVENTIONS.md#minimal-mode),
[`localtest/Emulation/README.md`](localtest/Emulation/README.md)). Under Aspire: `builder.AddAltinnLocaltestMinimal()`,
session containers on tmpfs ([package README](aspire/Altinn.App.Aspire/README.md#minimal-mode)).

**Another origin.** `--host <name>` and `--port <n>` move the public origin off `http://local.altinn.cloud:8000`:
`studioctl env up -r localtest-v2 --host altinn.test --port 18000` serves the platform on `http://altinn.test:18000` and
the dedicated hosts on `pdf.altinn.test`, `workflow-engine.altinn.test`, `app-frontend.altinn.test` and `af.altinn.test`
(the manifest's `public` section lists them). `--port 0` picks a free port. The state file records the origin, so
`env status` (its `Origin:` line), `env down`, `studioctl run`, `app env` and the dev-server route use it until the next
`env up` with other values, which stops the environment and starts it at the new origin; a plain `env up` keeps it.
`env hosts add` writes the default names only: for a custom host add the line to `/etc/hosts` yourself
(`127.0.0.1 altinn.test pdf.altinn.test workflow-engine.altinn.test app-frontend.altinn.test af.altinn.test`); `env up`
refuses with that line while a name does not resolve to loopback. Under Aspire: `PublicHost` and `PublicPort` (0 for a
port Aspire allocates) on both option classes ([package README](aspire/Altinn.App.Aspire/README.md#the-public-origin)).
For v4 apps (app-lib 8, the released app frontend), until the app-frontend-react fix, prefer a host that starts with
`local.` (`local.altinn.test`): the released app frontend throws "Unknown domain" on party selection for any other
three-label host. The monorepo app frontend takes its platform links from the settings and works on any host. Proven on
2026-10-23 at `http://altinn.test:18000` (root 26/26, authentication 34/34, Dialogporten 60/60, a sign-in in the
browser, screenshot [`localtest/docs/origin/`](localtest/docs/origin/app-after-sign-in-altinn-test-18000.png)) and with
`--minimal --port 0` (root 23 + 3 skipped). The rule and what follows the origin:
[`CONVENTIONS.md`](CONVENTIONS.md#the-public-origin-and-host-names).

**Container mode.** `studioctl run --mode container` builds the app's image from the app's own `Dockerfile` (as for the
localtest runtime; an app without one cannot run in this mode) and runs it as a container on the stack's network,
`localtest-v2_network`, under the alias `<org>-<app>`; every teardown (`env down` and the others) removes an app
container still on it. The front door reaches the app at `http://<org>-<app>:5005`, which studioctl-server registers;
the app calls the platform through the public origin, not the components' names, its host names mapped to the host gateway (`--add-host local.altinn.cloud:host-gateway` and the four
subdomains), as the components do. It gets the environment of a process run (the certificate mounted at `/certs`, the
Arbeidsflate and logout links, Kestrel on `0.0.0.0:5005`) with its secrets at `/mnt/app-secrets` and its keys at
`/mnt/keys`, keeps a loopback port for studioctl-server's discovery, and `--dev-frontend` works as for a process. Limits:
the default origin only (`run --mode container` is refused at another `--host` or `--port`), studioctl only (an Aspire
AppHost runs apps as projects). The rule: [`CONVENTIONS.md`](CONVENTIONS.md#the-runtimes).

**Behind a TLS-intercepting proxy** (a corporate network, a sandbox): set `STUDIO_CA_BUNDLE` to a PEM file with the
proxy's CA before `env up`. pdf3's Chromium trusts its own NSS store, not the system's, so without it the app frontend
from `altinncdn.no` does not load and PDF generation times out (`net::ERR_CERT_AUTHORITY_INVALID` in pdf3's log, the
app's `process/next` answers 500 "Pdf generation failed"). The runner mounts a copy of the file in pdf3 (every
container the manifest marks `caBundle`) and sets the variable there, and pdf3's entrypoint imports it at start. A
changed or removed bundle restarts pdf3 on the next `env up`; a certificate studioctl cannot parse is left out with a
warning, and a file that cannot be read, or holds no usable certificate, gives a warning. The old `localtest` runtime
shares the code (`stack.LoadCABundle`), and its pdf3 gets the same mount. An Aspire
AppHost reads the same variable from its environment. The .NET and Node components trust the system store and
`NODE_EXTRA_CA_CERTS` of their containers as your container runtime configures them; the variable does not change
those ([the manifest README](studioctl/src/cli/internal/localtestv2/manifest/README.md#what-the-runner-adds)).

**Images.** [`images.yml`](../.github/workflows/images.yml) builds
`ghcr.io/ivarne-agent/localtest-research/localtest-<name>` for every component, the localtest app, Postgres and Redis,
tagged `sha-<commit>` and `latest`; the manifest pins one build, moved only by `tools/bump-tag.sh`
([`CONVENTIONS.md`](CONVENTIONS.md#tags)).

**`--dev`.** `studioctl env up -r localtest-v2 --dev-<container>[=<path>]` builds
that container from a Dockerfile, or a directory holding one, by default the current directory, and runs it as
`localtest-v2-dev-<container>` instead of the pinned image. A file inside the current directory builds with the
current directory as context:

```sh
cd components/storage && studioctl env up -r localtest-v2 --dev-storage
cd components/dialogporten && studioctl env up -r localtest-v2 --with dialogporten --dev-dialogporten-webapi=Dockerfile.webapi
cd localtest && studioctl env up -r localtest-v2 --dev-postgres=postgres/Dockerfile
```

Postgres runs its init scripts only on an empty volume: after a component adds one, run it by hand (see the component's
README) or `studioctl env reset -r localtest-v2 --yes`.

**The frontend dev server.** `studioctl run --dev-frontend` points the app's assets at
`http://app-frontend.local.altinn.cloud:8000` and, under localtest-v2, routes that host to the dev server on the host's
port 8080 (`PUT localtest/api/routes/frontend-dev-server` with `http://host.docker.internal:8080`), removing the route
when the run stops (a `--detach` run leaves it). Serve the frontend there: `yarn docker` in `src/App/frontend` builds a
development bundle and serves `dist` on 8080 with CORS, `yarn start --port 8080` runs Vite. Without a route that host is
served like the main host. To set or clear the route by hand (an app started from an IDE, a stale route):
`curl -X PUT http://local.altinn.cloud:8000/localtest/api/routes/frontend-dev-server -H 'Content-Type: application/json' -d '{"url":"http://host.docker.internal:8080"}'`
and `curl -X DELETE http://local.altinn.cloud:8000/localtest/api/routes/frontend-dev-server`
([`localtest/README.md`](localtest/README.md#the-frontend-dev-server-localtestapiroutes-anonymous)).

**Cypress.** `run-app.sh`, `stop-apps.sh` and `run-platform-regression.sh` register apps with the front door by hand.
`run-platform-regression.sh --no-apps` runs the six platform specs against apps started with `studioctl run`
(frontend-test, signering-brukerstyrt, process-transition-test). The specs test the monorepo's frontend, so start those
apps with `studioctl run --dev-frontend` and serve the built `src/App/frontend/dist` with
`Access-Control-Allow-Origin` (the bundle loads as a cross-origin module script); with the released CDN bundle the page
renders empty and every spec fails. Set `MONOREPO=/path/to/altinn-studio` for `run-cypress.sh` (and so for
`run-platform-regression.sh`) when running from a worktree.

## Tests

After a sync, [`docs/verify-after-sync.md`](docs/verify-after-sync.md) puts these suites, the builds and unit tests in
order from cheap to expensive, with the expected numbers and the known false alarms.

Every suite runs against `http://local.altinn.cloud:8000`, or the origin in `LOCALTEST_ORIGIN`
(`LOCALTEST_ORIGIN=http://altinn.test:18000 ./smoke-test.sh`), which also sets the `af.`, `pdf.` and `app-frontend.`
hosts the suites derive from it; a suite's own `BASE` argument still wins.

| Command | What it checks |
| --- | --- |
| `./smoke-test.sh` | Through the front door: picker sign-in, production cookies without the Secure flag, `refresh`, an org token through the Maskinporten exchange, the Studio token, metadata/texts/policy pushes, PDP decisions, and a full Storage instance lifecycle (create, process start by the app, upload authorized by the PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing), texts read back, the org's application list, no errors in Storage's log (skipped without podman). 26 checks, all pass on 2026-10-21 (task 12) |
| `components/authentication/smoke-test.sh` | Against the stack: the browser sign-in, both discovery documents' `end_session_endpoint`, Authentication as an OIDC provider for the seeded clients (Arbeidsflate's `authorize` through the picker and the token exchange, the code-flow helper's tokens with `pid`, `invalid_client` and `invalid_grant`, refresh rotation and reuse detection, logout through the fake ID-porten's `endsession`), and with `--with dialogporten` Dialogporten's end-user dialog list with the code-flow token (the cookie JWT refused). 30 checks, 32 with the `dialogporten` group |
| `components/authorization/smoke-test.sh` | Against the stack: decisions for the seeded roles through Access Management, re-pushed policies, authorized parties, no errors in the PDP's log beyond the three the run provokes (skipped without podman). 31 checks, all pass on 2026-10-21 (task 12) |
| `localtest/smoke-test.sh` | The localtest app: the front door (routes by host and path, trailing-slash redirects, cookies without `Secure`, streaming, the app-frontend files, the 503 for a service that is off, the registration API and the dev-server route), the fake ID-porten and Maskinporten, and the `/Home` login contract (every level, party cookies, token helpers). 81 checks; 80 of 81 under both runtimes on 2026-10-22 (`/app-frontend/` needs `FRONTEND_DIST`, which neither runtime mounts). Run it with no app registered under Aspire (below) |
| `components/resource-registry/smoke-test.sh` | Against the stack: the fixture app pushed as the runner does, app and seeded resources, policies byte-equal, the `updated` feed paged and soft-deleting, orgs, subject lookup, platform access tokens. 43 checks (2026-10-21) |
| `components/dialogporten/smoke-test.sh` | Needs `--with dialogporten`: readiness of the three hosts, the JWKS and issuer, the fixture app pushed and synced, a dialog created, read, searched and deleted with an org token, wrong scope, foreign resource and missing token refused, a late-pushed app known after `sync` and after `caches/clear`, the dialog's event in the Events component, GraphQL with the org token through the rewrite, CORS for Arbeidsflate, the localtest list and delete of dialogs (deletes every dialog in the stack). 60 checks |
| `components/dialogporten-adapter/smoke-test.sh` | Needs `--with dialogporten`: an instance followed from Storage's outbox to a dialog (created within 10 s with resource, party and go-to link, `dialog.id` written back; attachment after an upload; status and a transmission after the submission), the adapter's receipt and delete with the end user's dialog token from Dialogporten, idempotent `syncDialog/simple`, the refusals. 39 checks. `--off` checks the default stack: an instance gets no dialog and Storage logs one line (7 checks). Run it before the Dialogporten smoke test, or a few seconds after it: that test's last step deletes every dialog and clears the caches, and straight after it this test's first service-owner read of the dialog answered 404 once |
| `components/receipt/smoke-test.sh` | Needs `--with receipt` (27 checks, all pass on 2026-10-21): health, an archived instance created for party 500000 as the root smoke test does, the sign-in redirect with the receipt as `goto` and back, page, bundle and CSS, `users/current` through Profile with the platform access token, the instance with its party and attachment links rewritten to the public origin that return the bytes, the language cookie, the hidden attachment groups, 403 for user 1002 and 404 for an unknown instance, no access-token warning in the log |
| `components/correspondence/smoke-test.sh` | Needs `--with correspondence` (76 checks, 79 with `CORRESPONDENCE_RESTART=1`; 76 of 76 on 2026-10-21, task 11b; 64 of 64 on the first run after `env up` since task 12's longer waits, before 11b's 12): health and all 69 EF migrations (in the status API and in the database), the refusals (401, 403, 400 `CORR-01036`), with a service owner's token the attachment and correspondence sequence to `Published`, the blob in the file store, a checksum mismatch, the dialog in Dialogporten, the e-mail in the Notifications capture, the notification condition check with a raw fake-Maskinporten token, an unknown recipient, the reserved person (422 `CORR-01030` alone, `Reserved` beside another recipient), the sender's refused and allowed purges, a scheduled publish and its Hangfire job; app-lib's credential (no `altinn:serviceowner`, the PDP decides) from attachment to a published correspondence; the recipient with the dialog token from Dialogporten reading, downloading, marking as read, confirming and purging, and the dialog's opened and confirmed activities; the browser download from the inbox (task 11b: the attachment URL with only user 1337's session cookie, Correspondence's OIDC challenge, no picker, `download?session=` with the bytes; a reused session, no cookie, another client's code and a missing correlation cookie, every walk bounded); a restart |
| `components/arbeidsflate/smoke-test.sh` | Needs `--with arbeidsflate`: readiness and the BFF's health, the SPA (stream URL, asset, CSP and no HSTS), the login round trip with a cookie jar as user 1337 through the picker, an instance followed to its dialog and the dialog in the inbox through the BFF's GraphQL (parties, `searchDialogs`, `dialogById` with a dialog token), the profile page against production Profile (favourites, settings), the init-session seam, the refusals, logout through the BFF and Authentication's server-side front-channel logout. 56 checks, all pass on 2026-10-21 (task 11, with `--with arbeidsflate` and with `--with everything`) |
| `components/events/smoke-test.sh` | Against the stack: app events published, deduplicated, authorized and delivered; subscriptions validated; capture of external endpoints; `registeredTime` in UTC; the production read API and its 30-second delay; reset; Dialogporten sources of two apps authorized apart. 27 checks, all pass on 2026-10-21 (task 12; 25 of 27 on the Events image without directive sites 4 and 5, as it should). Not straight after the Dialogporten adapter's smoke test: its dialog's Dialogporten events arrive after this test's reset and fail "stored once" |
| `components/notifications/smoke-test.sh` | Against the stack: orders with direct, person (incl. a KRR-reserved one), self-identified and organisation recipients (official addresses, main-unit fallback), order chains, idempotency, instant orders, cancel, inboxes, the 422 problem details, reset. 64 checks, all pass on 2026-10-21 (task 18, and task 14 against production Profile) |
| `components/profile/smoke-test.sh` | Against the stack: health, the six migrations and the seed (`POST sync`, the KRR row, DDG Fitness's addresses from the brreg change log), the user endpoints the Register fakes served (by id, UUID and SSN, `internal/user`, `users/current`, 401/403), production semantics (`phoneNumber` from KRR, `isReserved`, the contact point lookups, the organisation lookup with the main-unit fallback), Correspondence's scoped lookups, favourites, settings (a user change survives the seed), address verification with the code from the Notifications inbox feeding `units/contactpoint/lookup`, organisation addresses through the PDP and the brreg stand-in; `PERSIST=set`/`check` around a restart. 52 checks, all pass on 2026-10-21 (task 14) |
| `components/register/smoke-test.sh` | The component on its own; it also runs through the front door (`smoke-test.sh http://local.altinn.cloud:8000`; 14 checks since task 14 moved the Profile checks to `components/profile/`) |
| `smoke/workflow-engine.sh` | The workflow engine image studioctl runs (the monorepo's `tt02`, `IMAGE=` for another) on its own on a fresh Postgres container: health, migrations, a clean log (3 checks; 3 of 3 on 2026-10-21 beside the stack). The engine is not vendored here: studioctl runs the monorepo's image (TODO.md task 58 removed the unused copy) |
| `components/shared/smoke-test.sh` | The shared `IMaskinportenService` fake against the stack, with `UseAltinnTestOrg` on and off (2 checks) |
| `./run-cypress.sh --spec <specs>` | The app-frontend Cypress specs; start every app those specs use first (`studioctl run --dev-frontend` under studioctl, see "Run it") |
| `./run-platform-regression.sh` | The six app-frontend specs that once failed because of the platform (sign-in, Storage paging, self-identified users, party selection, signing delegations, Events for the workflow engine); starts and stops their apps with `run-app.sh`, or with `--no-apps` uses apps already started with `studioctl run --dev-frontend` (see "Run it": the monorepo's built frontend served with `Access-Control-Allow-Origin`). 44 tests, about six minutes; all pass on 2026-10-20 and again on 2026-10-21 (task 16, default stack; task 18, schema v3 seed; task 14, production Profile). The full suite is mostly about the frontend |

Under studioctl, stop a `studioctl run` of `ttd/testapp` (`studioctl stop`) before the suites, as under Aspire it is
deprovisioned first: studioctl-server registers a running app again as soon as the front door no longer lists it, so a
`DELETE` by hand does not stick while the app runs. Run on 2026-10-23 (task 32 phase B), pins `sha-6d928ad`,
`--with dialogporten`, after `studioctl stop` and the PDP refresh: root 26 of 26, `components/authentication` 34 of 34,
`components/authorization` 31 of 31, `components/dialogporten` 60 of 60, `localtest/smoke-test.sh` 80 of 81 (the
`/app-frontend/` bundle); root 26 of 26 again with `--dev-storage=localtest/components/storage`, and after
`env reset --yes` and `env up` on empty volumes.

Run on 2026-10-22 (task 27a phase 2) under studioctl with manifest v2 and the localtest container as the front door,
pinned images `sha-f76b62b`, `--with dialogporten`: root 26 of 26, `components/authentication` 34 of 34,
`components/authorization` 31 of 31, `components/dialogporten` 60 of 60, `components/dialogporten-adapter` 39 of 39,
`components/events` 27 of 27, `components/notifications` 64 of 64, `components/profile` 52 of 52 and
`localtest/smoke-test.sh` with every check 80 of 81 (the `/app-frontend/` bundle needs `FRONTEND_DIST`, which the stack
does not mount); with `--with correspondence --with receipt` (Correspondence without the Dialogporten group, only the
WebApi and Redis it depends on): `components/correspondence` 78 of 78 and `components/receipt` 27 of 27. The tests read
the containers without a route of their own (health endpoints, the direct cookie checks) from inside the stack's network
through `smoke/lib/containers.sh`, since no component publishes a host port any more. After `env reset --yes`, `env up -r localtest-v2` on empty volumes
took 21.6 s (every init script ran, every component migrated) and the root suite passed 26 of 26.

The smoke tests that read a container's log or run a command in it (the root test's Storage log, the authorization
test's policy store and PDP log, the dialogporten-adapter test's Storage log, the receipt's log, Correspondence's
database and restart) find the container through [`smoke/lib/containers.sh`](smoke/lib/containers.sh):
`localtest_container <name>` prints studioctl's `localtest-v2-<name>` when it runs, otherwise the one running Aspire
container `<name>-<suffix>` (the resource is the manifest name), and
`LOCALTEST_CONTAINER_<NAME>` (`STORAGE_CONTAINER`, `RECEIPT_CONTAINER` and the authorization test's `CONTAINER` still
work) names one by hand. Without podman or docker, or with no such container or two, the check is skipped with the
reason.

**Against an Aspire-hosted stack** (the sample AppHost, [`aspire/samples/app/`](aspire/samples/app), see the
package's [README](aspire/Altinn.App.Aspire/README.md#run-under-podman)), with no studioctl stack running:

1. Deprovision the app first: its `deprovision` command in the dashboard, or by hand
   `curl -X DELETE http://local.altinn.cloud:8000/localtest/api/apps/ttd/testapp`. A provisioned app's
   `testData.json` replaces the Register's seed (when every registered app posted test data, only theirs is loaded), so
   the suites' seed users (1337, 1003, ...) do not exist while the sample is registered.
2. Drop the PDP's one-minute party cache: `curl -X POST http://local.altinn.cloud:8000/authorization/api/localtest/refresh`.
3. Run the suites as against studioctl. The root suite and the others that push the `ttd/testapp` fixture overwrite the sample's metadata, texts and
   policy in Storage and the PDP.
4. Provision the sample again: its `provision` command, or restart the app resource.

Checks that cannot apply under Aspire: anything that needs `--dev`.

Run on 2026-10-22 (task 27b) against the sample AppHost with `dialogporten`, after the `DELETE` and the PDP refresh:
root 26 of 26, `components/authentication` 34 of 34, `components/dialogporten` 60 of 60, `components/authorization`
31 of 31 and `localtest/smoke-test.sh` with every check 80 of 81 (`/app-frontend/` needs `FRONTEND_DIST`, as under
studioctl).

Earlier runs, with the Aspire package's own front door (task 21f, now removed) and with studioctl-server as the front
door (2026-10-20), are recorded in the package's [History](aspire/Altinn.App.Aspire/README.md#history) and the localtest
app's [Verified](localtest/README.md#verified-2026-10-20-identity-features-and-home) sections.

## Status

Running together and verified: all components above, with sign-in, Storage, PDP decisions, instance delegations
(signing) and authorized parties on production code. All 13 test apps in `altinn-studio/src/test/apps` run against
the stack: 101 Cypress specs with 418 tests (run 2026-10-18), all passing except one test in process-transition-test's
`workflow-status.ts`, which failed because there was no Events service: every publish was a 404, and the workflow
engine throttled the app's namespace. With the events component (2026-10-19) the spec passes 19 of 19 with events on. The
full suite was last run on 2026-10-20 with the localtest app as the front door (419 tests, see
[`localtest/README.md`](localtest/README.md)); the runs recorded under studioctl are the platform regression's.

With the notifications component (2026-10-20) apps and platform services that order notifications get production answers and a tester sees every message in an inbox.

With the correspondence group (2026-10-21) a service owner's and an app's correspondence get production answers, a dialog and a captured notification, and the recipient reads, confirms and purges it with a dialog token; `signering-brukerstyrt` messages its signees and sends its receipts with the signed PDF (task 7b: one `#if !LOCALTEST` site lets the test data's 6-digit party ids through Correspondence's PDP request factory, see `components/correspondence/`). Without the group `/correspondence` answers `503` "Component not running" and the signing flow's sends fail quietly as before (the platform regression is 44 of 44 on the default stack).

With the profile component (task 14, 2026-10-21) production Profile replaces the Register's Profile fakes in the default stack (about 170 MB, healthy 3 s after start including its migrations and seed). Every suite passes on it: Profile 52, Register 14, Resource Registry 43, Authentication 30 (32 with Dialogporten), Authorization 30, Notifications 64, root 23; with `--with everything` Correspondence 43 + 2 `KNOWN`, Receipt 27, Dialogporten 60, the Dialogporten adapter 39; the platform regression 44 of 44. Memory with every group up: 4.4 GB used of 7.8 GB, 3.5 GB available (`free -m`). Behaviour changes: `phoneNumber` is the KRR mobile number, and `units/contactpoint/lookup` returns only addresses users registered for the organisation (see `components/profile/`).

With task 12 (2026-10-21, pin `sha-735e5d2` with `--dev events`) the shared Postgres runs in UTC as production's does, and
Dialogporten's sources no longer count as app sources in Events. Every suite passes with `--with everything`:
Authorization 31, Events 27, Dialogporten 60, the adapter 39 (34 on the first try straight after the Dialogporten test, the
known 404), Correspondence 64 on its first run (67 with the restart), Notifications 64, Profile 52, Register 14, Resource
Registry 43, Authentication 32, Receipt 27, root 26; the workflow engine's own test 3 of 3 with studioctl's image; the
platform regression 44 of 44 on the default stack.

With the arbeidsflate group (task 11, 2026-10-21) the inbox runs on `af.local.altinn.cloud:8000` against production Authentication, Dialogporten and Profile: sign-in through the picker, the apps' dialogs (drafts under Utkast), live updates over Dialogporten's stream, the profile page and logout with Authentication's front-channel call were checked in Chromium. Images built here: the BFF 1.47 GB (whole-workspace install, as upstream), the SPA 67 MB. Every suite passes with the group: Arbeidsflate 56, the adapter 39, Authentication 32, Dialogporten 60, root 26; with `--with everything` Arbeidsflate 56 and Correspondence 64; the platform regression 44 of 44 on the default stack. Memory with every group up: 4.2 GB used of 7.8 GB, 3.8 GB available (`free -m`), the BFF 362 MB of it (`components/arbeidsflate/`, "Verified").

With task 11b (2026-10-21, `--with arbeidsflate --with correspondence --dev correspondence --dev authentication`) a Correspondence attachment clicked in the inbox downloads over plain http, without a picker when the user is signed in: Correspondence challenges its own OIDC client (`correspondence-localtest`, Authentication's third seeded client), Authentication reuses the browser's session, and one `#if !LOCALTEST` site lets Correspondence's `IdportenTokenValidator` read Authentication's metadata over http; its correlation and nonce cookies are `SameSite=Lax` without `Secure` (fakes). Checked in Chromium (`components/arbeidsflate/docs/attachment.png`). Every suite passes with both groups: Correspondence 76 (64 and the browser download's 12), Authentication 32, Arbeidsflate 56, Dialogporten 60, root 26; the platform regression 44 of 44 on the default stack with `--dev authentication`.

Not yet: the inspection GUI over the components' `api/localtest/` endpoints (deferred); a
CI check that every `vendor/` is upstream plus `LOCALTEST` blocks (`tools/vendor-sync.py check --all` is run by hand, no CI
for now);
a smoke run in CI against the stack; and the rest of the studioctl integration (upstreaming; `/app-frontend/` needs the
built frontend mounted, `FRONTEND_DIST`, which neither runtime does). All of it, with status, is in [`../TODO.md`](../TODO.md).
