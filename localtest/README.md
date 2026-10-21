# localtest MVP

Local platform for Altinn apps, run by studioctl's `localtest-v2` runtime ("Run it" below), built from vendored
production services and small fakes. Each
component runs the production source at a pinned upstream commit; local behaviour comes from `#if !LOCALTEST`
switches and an adjacent `fakes/` folder, never from edits to the vendored code.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory store: a built-in seed plus the `testData.json` each app runner posts (see its README), and the KRR stand-in |
| `components/profile/` | Production Profile on the shared Postgres (port 8036, `/profile`): user profiles for apps, Authentication, the PDP and Access Management, the contact point lookups of Notifications and Correspondence, Arbeidsflate's favourites, settings, address verification, professional notification settings and organisation addresses. Test users' contact details, settings and organisations' official addresses come from the Register's test data through stand-ins for KRR and brreg and Profile's own sync jobs (every minute; settings and organisation addresses seeded once, user changes win); `profile/api/localtest/` shows the rows and runs the sync |
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
| `components/correspondence/` | **Optional group** (`env up --with correspondence`, which also starts `dialogporten`): production Altinn Correspondence (the API host, Hangfire inside it) on a `correspondence` database in the shared Postgres, sharing Dialogporten's Redis (database 1), attachments in a file store on a volume, about 120 MB. A service owner's correspondence runs end to end on production code: the Resource Registry seed's correspondence resources, KRR (with the test data's reservations) and production Profile's lookups, a dialog in Dialogporten, the e-mail in the Notifications capture; `correspondence/api/localtest/` shows correspondences, their Hangfire jobs and the migration count. Apps' sends (app-lib's credential, the signing flow), the recipient's dialog-token calls and, with the `arbeidsflate` group, an attachment clicked in the inbox (Correspondence's own OIDC client in Authentication, no picker with a session) run on production code (see its README) |
| `components/arbeidsflate/` | **Optional group** (`env up --with arbeidsflate`, which also starts `dialogporten`): production Arbeidsflate, the Altinn inbox, on its own host `http://af.local.altinn.cloud:8000` (a hosts entry for the browser, `studioctl env hosts add`): the SPA behind nginx, which proxies `/api/` to the BFF; the BFF signs users in through production Authentication's OIDC provider (the seeded client `arbeidsflate-localtest`), keeps sessions in Redis (database 2) and its own rows in an `arbeidsflate` database, proxies GraphQL to Dialogporten with the user's token and serves the profile page from production Profile. Upstream's tree unmodified plus three `localtest:` patches applied at image build (discovery over http, the party cookies' domain, the BFF's cookies `Secure` only over https). With the `correspondence` group a correspondence's attachment downloads from the inbox (11b in its README) |
| `components/workflow-engine/` | The workflow engine from the altinn-studio monorepo, unmodified; studioctl runs the monorepo's published image, not a build from here |
| `components/shared/` | Source for fakes more than one component copies (the `IMaskinportenService` fake), with its own smoke test; not an image |
| `localtest/` | The localtest app (C#, Native AOT), the home of localtest features that are not a platform component. Under studioctl it serves whatever no component route claims on `local.altinn.cloud:8000` (`/`, `/Home`, `localtest/api/`, and the 404 for an app path without a route); studioctl-server is the front door. Its own proxy (routes by host and path prefix, app routes pushed by `run-app.sh` through `PUT localtest/api/proxy/apps/{org}/{app}`, the app-frontend files, `Secure` removed from an app's cookies only) is the front door only when the app runs without studioctl. It also hosts the fake ID-porten (test-user picker, end-session endpoint) and Maskinporten that production Authentication trusts, the old localtest's start page and `/Home` test helpers, and a headless code-flow helper (`localtest/api/authentication/token`) that returns production Authentication's OIDC tokens, with `pid`, for a seeded client |
| `testdata/` | The `testData.json` format the stack reads (the Register's seed and every app's file): field reference, consumers, merge rules and the JSON Schema `test-users.schema.v3.json` |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate them with the production token handlers |
| `aspire/Altinn.App.Aspire/` | The Aspire hosting package (task 21d, first slice): `AddAltinnLocaltest().WithGroup(...)` adds the components of the studioctl manifest as persistent containers of an Aspire AppHost behind a YARP front door on `local.altinn.cloud:8000`, with a single-file sample AppHost; built and unit-tested against studioctl's golden file, not yet run under Podman (see its [README](aspire/Altinn.App.Aspire/README.md)) |
| `studioctl/` | The studioctl source vendored from `Altinn/altinn-studio` with the `localtest-v2` runtime on top, built from here until it is upstreamed (see its [README](studioctl/README.md) and [UPSTREAM.md](studioctl/UPSTREAM.md)) |
| `tools/repin-vendor.py` | Re-pins a component's `vendor/` to a newer upstream commit: takes upstream's version of unchanged files, three-way merges the files with `LOCALTEST` sites, reports conflicts (see each `vendor/UPSTREAM.md`) |
| `reviews/` | Reviews of how each component is localtestified: vendored code fidelity, Dockerfile, fakes, differences from production, findings |
| `smoke/apps/` | The `ttd/testapp` fixture (metadata, texts, policy) the smoke tests push |
| `postgres/` | The shared Postgres image: Postgres 18 with every component's database init script baked in |
| `run-app.sh`, `stop-apps.sh` | Start an app from a source checkout against the stack (build, run, route, push its artefacts) and stop all of them; they need the localtest app's own proxy as the front door, so under studioctl use `studioctl run` |
| `run-cypress.sh` | Run the app-frontend Cypress suite from the monorepo against the stack |
| `run-platform-regression.sh` | The six app-frontend specs that once failed because of the platform ("Tests" below) |
| `build-pdf3.sh` | Build a pdf3 worker image from an altinn-studio checkout (`localtest-pdf3:dev`); the stack runs studioctl's `runtime-pdf3-worker` image unless `STUDIOCTL_IMAGE_PDF3` names another |
| `smoke-test.sh` | End-to-end check of the running stack through the front door |

**The component manifest** is the one description of the stack's containers:
[`studioctl/src/cli/internal/localtestv2/manifest/manifest.json`](studioctl/src/cli/internal/localtestv2/manifest/manifest.json)
(with its JSON Schema and a [README](studioctl/src/cli/internal/localtestv2/manifest/README.md) beside it) lists every
component's image, ports, environment, mounts, dependencies, health check, group and front-door routes, the optional
groups, the image pin and the Postgres init scripts. studioctl embeds it and builds the `localtest-v2` runtime from it;
another runtime can read the same file. The ports and routes below are derived from it.

Host ports (loopback only): Register 8020, Authentication 8021,
Authorization 8022, Access Management 8023, Storage 8024, Events 8025, the localtest app 8026, Resource Registry 8027,
the workflow engine 8028, Notifications 8029, Profile 8036, the PDF generator 5300; in the `dialogporten` group the
WebApi 8030, GraphQL 8031, Service 8032 and the adapter 8035; Receipt 8034 (`receipt`); Correspondence 8033
(`correspondence`); in the `arbeidsflate` group the SPA's nginx 8037 (the whole host `af.local.altinn.cloud`) and the
BFF 8038 (no route). Postgres and Redis publish no port. Everything is reached through `http://local.altinn.cloud:8000`
as well, by the route prefixes in the manifest.

Fakes are written fresh for this layout and reach the vendored services the way their production counterparts do: the
picker resolves users through the Register API, and `testData.json` is Register seed data, not a file other
components read. Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a
reference for which behaviours the fakes must provide.

## What the app runner pushes

In production Altinn Studio pushes an app's artefacts to the platform at deploy time; locally that is the app runner's
job. The steps have one implementation, the library
[`Altinn.Studio.Localtest.Provisioning`](studioctl/src/Runtime/common/dotnet/Altinn.Studio.Localtest.Provisioning/README.md),
which reads the artefacts either from the app folder (`DirectoryAppSource`, the planned Aspire package's case) or from
the running app's own API (`RunningAppSource`). studioctl-server's provisioner
(`studioctl/src/cli/studioctl-server/Provisioning/AppProvisioner.cs`) calls it with the running app when it discovers
an app (started by `studioctl run` or from an IDE) and again when the app comes back on another port; an app that is
gone has its test data removed (`DELETE register/api/localtest/apps/{org}/{app}`). `run-app.sh`, which predates it,
is kept as a shell reference for runs without studioctl and does steps 1 to 4 without 3b and 5. No component watches
another. For one app the runner calls, in order:

| Step | Endpoint | What it does |
| --- | --- | --- |
| 1 | `PUT register/api/localtest/apps/{org}/{app}` with the app's `App/wwwroot/testData.json` as the body (empty body: no test data) | Register stores the test data as posted and rebuilds its store (built-in seed plus every registered app); it fetches nothing. The file format, its JSON Schema and the merge rules: [`testdata/`](testdata/README.md) |
| 2 | `POST storage/api/v1/applications?appId={org}/{app}` (or `PUT storage/api/v1/applications/{org}/{app}` when it exists) with `App/config/applicationmetadata.json`, and `POST`/`PUT storage/api/v1/applications/{org}/{app}/texts[/{lang}]` with each `App/config/texts/resource.{lang}.json` | Production Storage endpoints, with the Studio token (`GET /Home/GetTestStudioToken`: Designer's own `studio`/`studio.designer` platform access token exchanged through the production `exchange/altinnstudio`), exactly Designer's credential |
| 3 | `POST authorization/api/v1/policies?org={org}&app={app}` with `App/config/authorization/policy.xml`, `[ORG]`/`[org]`/`[APP]`/`[app]` replaced as Designer's `ReplacePolicyPlaceholderTokens` does | Production PDP endpoint (Studio token); the policy lands in the policy store shared with Access Management, which reads it for delegable rights |
| 3b | `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` (when the app has a policy) | Production Resource Registry endpoint, anonymous, as Designer calls it at deploy: reads the pushed policy from the policy store and upserts the app's subject pairs for the `updated` feed. The app's resource itself is computed from Storage's application list |
| 4 | `POST accessmanagement/api/localtest/refresh` | Access Management re-imports parties and external roles from the Register's streams (production code, full re-poll), runs the production resource sync against the Resource Registry component (service owners, `app_{org}_{app}` resources and their policy subjects from its `updated` feed, read from the epoch each time), then applies the seed's own roles, Altinn 2 role codes and access packages and prunes what the seed no longer grants |
| 5 (optional) | `POST dialogporten/api/v1/localtest/sync`, only when the `dialogporten` group is up (studioctl-server reads it from the route table `env up` writes; skipped with a log line otherwise) | Dialogporten runs the Janitor's two production syncs (subject-resource pairs from the registry's `updated` feed, resource policy information) and clears its named caches, so the app's resource and subjects are known to Dialogporten at once instead of after its 20-minute resource cache. A failure is logged and retried on the next refresh, and does not fail the provisioning; when the group comes up after the apps, one sync runs for all of them. See `components/dialogporten/README.md` |

No component fetches from the running app any more. The Register's store is rebuilt only by these calls, not on a timer,
so an edited `testData.json` becomes visible after the next registration or refresh. Profile is not called: its KRR sync,
organisation address sync and settings seed read the Register's test data every minute (task 14), so an app user's e-mail,
phone number and settings reach Profile up to a minute after step 1, or at once with `POST profile/api/localtest/sync`;
names and ids are live. An app path the runner has not
registered is the localtest app's 404.

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
repository (`make dev-install`, see [`studioctl/README.md`](studioctl/README.md)) until it is upstreamed to
altinn-studio. It starts the components from the images this repository publishes, proxies
`http://local.altinn.cloud:8000` from studioctl-server, pushes each app's artefacts when `studioctl run` discovers it,
and polls every component's `/health`:

```sh
studioctl env up -r localtest-v2            # pinned images from ghcr; --latest follows main
studioctl env up -r localtest-v2 --with dialogporten   # also Dialogporten (three hosts and Redis, about 0.5 GB) and its adapter (about 0.26 GB)
studioctl env up -r localtest-v2 --with receipt        # also the platform receipt for archived instances
studioctl env up -r localtest-v2 --with correspondence # also Correspondence (about 120 MB), which brings the dialogporten group
studioctl env up -r localtest-v2 --with arbeidsflate   # also the inbox on http://af.local.altinn.cloud:8000, which brings the dialogporten group
                                            # (once: sudo studioctl env hosts add -r localtest-v2, for the browser)
studioctl env up -r localtest-v2 --with everything     # every optional group
studioctl run                               # in an app checkout
```

The optional groups are off by default: `dialogporten` (see [`components/dialogporten/`](components/dialogporten/README.md) and
[`components/dialogporten-adapter/`](components/dialogporten-adapter/README.md)),
`receipt` (see [`components/receipt/`](components/receipt/README.md)), `correspondence` (implies `dialogporten`, see [`components/correspondence/`](components/correspondence/README.md)), `arbeidsflate` (implies `dialogporten`, see [`components/arbeidsflate/`](components/arbeidsflate/README.md); the browser needs `af.local.altinn.cloud` in the hosts file, which `studioctl env hosts add` writes with the stack's other names) and `everything` (implies every other group). `--with <group>` starts
a group and what it implies, `--without <group>` leaves out only that group, and leaving out a group an enabled one implies
is an error (see [`studioctl/README.md`](studioctl/README.md)). Without a group none of its containers or health checks exist,
and its routes answer `503` problem details ("Component not running", with `component`, `group` and the `--with`
command; a browser gets the same as an HTML page). A component that is on but does not answer gets `503` "Component
not answering" with the `env logs` and `env status` commands. A path that matches no route still reaches the localtest
app's 404. A later `env up` with or without the flags
converges the environment to that choice.

Images: [`.github/workflows/images.yml`](../.github/workflows/images.yml) builds `ghcr.io/ivarne-agent/localtest-research/localtest-<name>`
for every component, the localtest app and [`postgres/`](postgres/) (Postgres 18 with the components' init scripts baked
in), tagged `sha-<commit>` and `latest`. studioctl runs the build pinned in
the component manifest (`images.tag`, today `sha-7f21882`, the build of task 11b), which is moved by hand
after a run against it. To run a component from this checkout instead, `studioctl env up -r localtest-v2
--dev <name>` builds it from `components/<name>` (`STUDIOCTL_LOCALTEST_V2_SRC` points at this `localtest/` folder; `--dev dialogporten`
builds the group's three images from `components/dialogporten`). An existing Postgres volume predates a component's init script, which Postgres
only runs on an empty volume: run the script by hand (see the component's README) or `studioctl env reset -r localtest-v2 --yes`.

There is no compose file any more; `run-app.sh`, `stop-apps.sh` and `run-platform-regression.sh` predate the studioctl
runtime and push app routes to the localtest app's own proxy API, which studioctl does not use.
`run-platform-regression.sh --no-apps` runs the six platform specs against apps started with `studioctl run`
(frontend-test, signering-brukerstyrt, process-transition-test). The specs test the monorepo's frontend, so start those
apps with `studioctl run --dev-frontend` and serve the built `src/App/frontend/dist` (the frontend dev server, or any
static server on the port `--dev-frontend` expects that sends `Access-Control-Allow-Origin`, since the bundle loads as a cross-origin module script); with the released CDN bundle the page renders empty and every spec
fails. Set `MONOREPO=/path/to/altinn-studio` for `run-cypress.sh` (and so for `run-platform-regression.sh`, which runs
the specs through it) when running from a worktree; the regression script's checkout argument only locates the apps.

## Tests

| Command | What it checks |
| --- | --- |
| `./smoke-test.sh` | Through the front door: picker sign-in, production cookies without the Secure flag, `refresh`, an org token through the Maskinporten exchange, the Studio token, metadata/texts/policy pushes, PDP decisions, and a full Storage instance lifecycle (create, process start by the app, upload authorized by the PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing), texts read back, the org's application list, no errors in Storage's log (skipped without podman). 26 checks, all pass on 2026-10-21 (task 12) |
| `components/authentication/smoke-test.sh` | Against the stack: the browser sign-in, both discovery documents' `end_session_endpoint`, Authentication as an OIDC provider for the seeded clients (Arbeidsflate's `authorize` through the picker and the token exchange, the code-flow helper's tokens with `pid`, `invalid_client` and `invalid_grant`, refresh rotation and reuse detection, logout through the fake ID-porten's `endsession`), and with `--with dialogporten` Dialogporten's end-user dialog list with the code-flow token (the cookie JWT refused). 30 checks, 32 with the `dialogporten` group |
| `components/authorization/smoke-test.sh` | Against the stack: decisions for the seeded roles through Access Management, re-pushed policies, authorized parties, no errors in the PDP's log beyond the three the run provokes (skipped without podman). 31 checks, all pass on 2026-10-21 (task 12) |
| `localtest/smoke-test.sh` | The localtest app: the proxy (routes by host and path, trailing-slash redirects, cookies without `Secure`, streaming, the app-frontend files, the app route API), the fake ID-porten and Maskinporten, and the `/Home` login contract (every level, party cookies, token helpers). 75 checks with the localtest app as the front door. Under studioctl run it with `PROXY_CHECKS=0`, which skips the 9 app-route checks (studioctl-server is the front door, see [`localtest/README.md`](localtest/README.md)): 64 of 66 passed on 2026-10-20, every identity provider and Home check among them; studioctl's front door cannot pass the trailing-slash redirect and `/app-frontend/` checks (`../TODO.md` task 13) |
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
| `components/workflow-engine/smoke-test.sh` | The engine on its own on a fresh Postgres container: health, migrations, a clean log (3 checks); `--image` runs an image instead of the vendored host, by default the one studioctl runs (3 of 3 on 2026-10-21 beside the stack) |
| `components/shared/smoke-test.sh` | The shared `IMaskinportenService` fake against the stack, with `UseAltinnTestOrg` on and off (2 checks) |
| `./run-cypress.sh --spec <specs>` | The app-frontend Cypress specs; start every app those specs use first (`studioctl run --dev-frontend` under studioctl, see "Run it") |
| `./run-platform-regression.sh` | The six app-frontend specs that once failed because of the platform (sign-in, Storage paging, self-identified users, party selection, signing delegations, Events for the workflow engine); starts and stops their apps with `run-app.sh`, or with `--no-apps` uses apps already started with `studioctl run --dev-frontend` (see "Run it": the monorepo's built frontend served with `Access-Control-Allow-Origin`). 44 tests, about six minutes; all pass on 2026-10-20 and again on 2026-10-21 (task 16, default stack; task 18, schema v3 seed; task 14, production Profile). The full suite is mostly about the frontend |

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
CI check that every `vendor/` is upstream plus `LOCALTEST` blocks (`tools/repin-vendor.py` re-pins, nothing checks);
a smoke run in CI against the stack; and the rest of the studioctl integration (upstreaming, the front-door gaps for a
prefix without its trailing slash and for `/app-frontend/`). All of it, with status, is in [`../TODO.md`](../TODO.md).
