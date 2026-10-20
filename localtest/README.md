# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes. Each
component runs the production source at a pinned upstream commit; local behaviour comes from `#if !LOCALTEST`
switches and an adjacent `fakes/` folder, never from edits to the vendored code.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory store: a built-in seed plus the `testData.json` each app runner posts (see its README) |
| `components/authentication/` | Production Authentication on memory instead of Postgres; it signs in against the localtest app's fake ID-porten and trusts its fake Maskinporten, so its own OIDC client, token validation, Profile client and exchanges run. It is also the OpenID Connect provider for registered clients (authorization code with PKCE, refresh, logout), with `localtest-smoke` and `arbeidsflate-localtest` seeded; those tokens carry `pid` |
| `components/authorization/` | Production PDP with app policies in the policy store shared with Access Management; roles, access packages, delegations and authorized parties from Access Management, resources and resource policies from the Resource Registry |
| `components/access-management/` | Production Access Management on Postgres: imports parties and roles from the Register, takes instance delegations from apps, syncs resources from the Resource Registry, serves the PDP's PIP and authorized parties |
| `components/storage/` | Production Storage on Postgres with file-backed blobs and file scan marked clean; application metadata and texts pushed by the app runner |
| `components/events/` | Production Events on the shared Postgres, emptied on every start: apps publish events, subscriptions are validated and delivered inside the stack, webhooks to anything outside it are captured; `events/api/localtest/` shows it all |
| `components/notifications/` | Production Notifications (the orders API host) on the shared Postgres: apps, Authentication and Correspondence order e-mail and SMS as in production, recipients resolve through the Register's Profile endpoints, and every message is captured in an inbox per address or number (`notifications/api/localtest/`) and delivered by the production status machine; nothing is sent |
| `components/resource-registry/` | Production Resource Registry on the shared Postgres: app resources computed from Storage's application list and the pushed policies (no cache), a built-in seed of other resources, the `updated` subject feed, the org list from altinncdn.no; the runner triggers the subject reload as Designer does |
| `components/dialogporten/` | **Optional group** (`env up --with dialogporten`): production Dialogporten WebApi, GraphQL and Service hosts on a `dialogporten` database in the shared Postgres, with a Redis; service-owner API with org tokens, resource checks against the Resource Registry, events to the Events component, GraphQL under `/dialogporten/graphql` (CORS for Arbeidsflate's origin). The provisioner's `sync` makes a pushed app known at once; `dialogporten/api/v1/localtest/` clears the caches, lists and deletes dialogs. Dialogs persist until `env reset` or that delete |
| `components/workflow-engine/` | The workflow engine from the altinn-studio monorepo, unmodified |
| `localtest/` | The localtest app, the front door on `local.altinn.cloud:8000` (C#, Native AOT), and the home of localtest features that are not a platform component. Its proxy routes by host and path prefix to the components, one route per app pushed by `run-app.sh` (`PUT localtest/api/proxy/apps/{org}/{app}`), serves the app-frontend files, answers 404 for any app path without a route, and removes `Secure` from cookies. It also hosts the fake ID-porten (test-user picker, end-session endpoint) and Maskinporten that production Authentication trusts, the old localtest's start page and `/Home` test helpers, and a headless code-flow helper (`localtest/api/authentication/token`) that returns production Authentication's OIDC tokens, with `pid`, for a seeded client |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate them with the production token handlers |
| `studioctl/` | The studioctl source vendored from `Altinn/altinn-studio` with the `localtest-v2` runtime on top, built from here until it is upstreamed (see its [README](studioctl/README.md) and [UPSTREAM.md](studioctl/UPSTREAM.md)) |
| `tools/repin-vendor.py` | Re-pins a component's `vendor/` to a newer upstream commit: takes upstream's version of unchanged files, three-way merges the files with `LOCALTEST` sites, reports conflicts (see each `vendor/UPSTREAM.md`) |
| `reviews/` | Reviews of how each component is localtestified: vendored code fidelity, Dockerfile, fakes, differences from production, findings |
| `smoke/apps/` | The `ttd/testapp` fixture (metadata, texts, policy) the smoke tests push |
| `postgres/` | The shared Postgres image: Postgres 18 with every component's database init script baked in |
| `run-app.sh`, `stop-apps.sh` | Start an app from a source checkout against the stack (build, run, route, push its artefacts) and stop all of them |
| `run-cypress.sh` | Run the app-frontend Cypress suite from the monorepo against the stack |
| `smoke-test.sh` | End-to-end check of the running stack through the proxy |

Fakes are written fresh for this layout and reach the vendored services the way their production counterparts do: the
picker resolves users through the Register API, and `testData.json` is Register seed data, not a file other
components read. Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a
reference for which behaviours the fakes must provide.

## What the app runner pushes

In production Altinn Studio pushes an app's artefacts to the platform at deploy time; locally that is the app runner's
job (`run-app.sh` today, studioctl later). No component watches another. For one app the runner calls, in order:

| Step | Endpoint | What it does |
| --- | --- | --- |
| 1 | `PUT register/api/localtest/apps/{org}/{app}` with the app's `App/wwwroot/testData.json` as the body (empty body: no test data) | Register stores the test data as posted and rebuilds its store (built-in seed plus every registered app); it fetches nothing |
| 2 | `POST storage/api/v1/applications?appId={org}/{app}` (or `PUT storage/api/v1/applications/{org}/{app}` when it exists) with `App/config/applicationmetadata.json`, and `POST`/`PUT storage/api/v1/applications/{org}/{app}/texts[/{lang}]` with each `App/config/texts/resource.{lang}.json` | Production Storage endpoints, with the Studio token (`GET /Home/GetTestStudioToken`: Designer's own `studio`/`studio.designer` platform access token exchanged through the production `exchange/altinnstudio`), exactly Designer's credential |
| 3 | `POST authorization/api/v1/policies?org={org}&app={app}` with `App/config/authorization/policy.xml`, `[ORG]`/`[org]`/`[APP]`/`[app]` replaced as Designer's `ReplacePolicyPlaceholderTokens` does | Production PDP endpoint (Studio token); the policy lands in the policy store shared with Access Management, which reads it for delegable rights |
| 3b | `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` (when the app has a policy) | Production Resource Registry endpoint, anonymous, as Designer calls it at deploy: reads the pushed policy from the policy store and upserts the app's subject pairs for the `updated` feed. The app's resource itself is computed from Storage's application list |
| 4 | `POST accessmanagement/api/localtest/refresh` | Access Management re-imports parties and external roles from the Register's streams (production code, full re-poll), runs the production resource sync against the Resource Registry component (service owners, `app_{org}_{app}` resources and their policy subjects from its `updated` feed, read from the epoch each time), then applies the seed's own roles, Altinn 2 role codes and access packages and prunes what the seed no longer grants |
| 5 (optional) | `POST dialogporten/api/v1/localtest/sync`, only when the `dialogporten` group is up (studioctl-server reads it from the route table `env up` writes; skipped with a log line otherwise) | Dialogporten runs the Janitor's two production syncs (subject-resource pairs from the registry's `updated` feed, resource policy information) and clears its named caches, so the app's resource and subjects are known to Dialogporten at once instead of after its 20-minute resource cache. A failure is logged and retried on the next refresh, and does not fail the provisioning; when the group comes up after the apps, one sync runs for all of them. See `components/dialogporten/README.md` |

Nothing is fetched from the running app any more. The Register's store is rebuilt only by these calls, not on a timer,
so an edited `testData.json` becomes visible after the next registration or refresh. An app path the runner has not
registered is a 404 at the proxy.

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
| Storage's outbox to the Dialogporten adapter | 60 s (low priority) / 5 s (high priority) | Planned, task 10: 2 s / 0 s |

## Run it

The stack runs through studioctl's `localtest-v2` runtime, built from [`studioctl/src/cli`](studioctl/src/cli) in this
repository (`make dev-install`, see [`studioctl/README.md`](studioctl/README.md)) until it is upstreamed to
altinn-studio. It starts the components from the images this repository publishes, proxies
`http://local.altinn.cloud:8000` from studioctl-server, pushes each app's artefacts when `studioctl run` discovers it,
and polls every component's `/health`:

```sh
studioctl env up -r localtest-v2            # pinned images from ghcr; --latest follows main
studioctl env up -r localtest-v2 --with dialogporten   # also Dialogporten (three hosts and Redis, about 0.5 GB)
studioctl run                               # in an app checkout
```

`--with dialogporten` starts the optional Dialogporten group (see [`components/dialogporten/`](components/dialogporten/README.md));
without it none of its containers, routes or health checks exist, and `/dialogporten/...` falls through to the localtest app's 404. A later
`env up` with or without the flag converges the environment to that choice.

Images: [`.github/workflows/images.yml`](../.github/workflows/images.yml) builds `ghcr.io/ivarne-agent/localtest-research/localtest-<name>`
for every component, the localtest app and [`postgres/`](postgres/) (Postgres 18 with the components' init scripts baked
in), tagged `sha-<commit>` and `latest`. To run a component from this checkout instead, `studioctl env up -r localtest-v2
--dev <name>` builds it from `components/<name>` (`STUDIOCTL_LOCALTEST_V2_SRC` points at this `localtest/` folder; `--dev dialogporten`
builds the group's three images from `components/dialogporten`). An existing Postgres volume predates a component's init script, which Postgres
only runs on an empty volume: run the script by hand (see the component's README) or `studioctl env reset -r localtest-v2 --yes`.

There is no compose file any more; `run-app.sh`, `stop-apps.sh` and `run-platform-regression.sh` predate the studioctl
runtime and push app routes to the localtest app's own proxy API, which studioctl does not use.
`run-platform-regression.sh --no-apps` runs the six platform specs against apps started with `studioctl run`
(frontend-test, signering-brukerstyrt, process-transition-test). The specs test the monorepo's frontend, so start those
apps with `studioctl run --dev-frontend` and serve the built `src/App/frontend/dist` (the frontend dev server, or any
static server on the port `--dev-frontend` expects); with the released CDN bundle the page renders empty and every spec
fails. Set `MONOREPO=/path/to/altinn-studio` for `run-cypress.sh` when running from a worktree.

## Tests

| Command | What it checks |
| --- | --- |
| `./smoke-test.sh` | Through the proxy: picker sign-in, production cookies without the Secure flag, `refresh`, an org token through the Maskinporten exchange, the Studio token, metadata/texts/policy pushes, PDP decisions, and a full Storage instance lifecycle (create, process start by the app, upload authorized by the PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing) |
| `components/authentication/smoke-test.sh` | Against the stack: the browser sign-in, both discovery documents' `end_session_endpoint`, Authentication as an OIDC provider for the seeded clients (Arbeidsflate's `authorize` through the picker and the token exchange, the code-flow helper's tokens with `pid`, `invalid_client` and `invalid_grant`, refresh rotation and reuse detection, logout through the fake ID-porten's `endsession`), and with `--with dialogporten` Dialogporten's end-user dialog list with the code-flow token (the cookie JWT refused) |
| `components/authorization/smoke-test.sh` | Against the stack: decisions for the seeded roles through Access Management, re-pushed policies, authorized parties |
| `localtest/smoke-test.sh` | The localtest app: the proxy (routes by host and path, trailing-slash redirects, cookies without `Secure`, streaming, the app-frontend files, the app route API), the fake ID-porten and Maskinporten, and the `/Home` login contract (every level, party cookies, token helpers) |
| `components/resource-registry/smoke-test.sh` | Against the stack: the fixture app pushed as the runner does, app and seeded resources, policies byte-equal, the `updated` feed paged and soft-deleting, orgs, subject lookup, platform access tokens |
| `components/dialogporten/smoke-test.sh` | Needs `--with dialogporten`: readiness of the three hosts, the JWKS and issuer, the fixture app pushed and synced, a dialog created, read, searched and deleted with an org token, wrong scope, foreign resource and missing token refused, a late-pushed app known after `sync` and after `caches/clear`, the dialog's event in the Events component, GraphQL with the org token through the rewrite, CORS for Arbeidsflate, the localtest list and delete of dialogs (deletes every dialog in the stack) |
| `components/events/smoke-test.sh` | Against the stack: app events published, deduplicated, authorized and delivered; subscriptions validated; capture of external endpoints; the production read API; reset |
| `components/notifications/smoke-test.sh` | Against the stack: orders with direct, person and self-identified recipients, order chains, idempotency, instant orders, cancel, inboxes, the 422 problem details, reset |
| `components/register/smoke-test.sh`, `components/storage/smoke-test.sh` | The component on its own (Storage needs ports 8000 and 5433 free, so stop the stack first) |
| `./run-cypress.sh --spec <specs>` | The app-frontend Cypress specs; start every app those specs use with `run-app.sh` first |
| `./run-platform-regression.sh` | The six app-frontend specs that once failed because of the platform (sign-in, Storage paging, self-identified users, party selection, signing delegations, Events for the workflow engine); starts and stops their apps. 44 tests, about six minutes; all pass on 2026-10-20. The full suite is mostly about the frontend |

## Status

Running together and verified: all components above, with sign-in, Storage, PDP decisions, instance delegations
(signing) and authorized parties on production code. All 13 test apps in `altinn-studio/src/test/apps` run against
the stack: 101 Cypress specs with 418 tests (run 2026-10-18), all passing except one test in process-transition-test's
`workflow-status.ts`, which failed because there was no Events service: every publish was a 404, and the workflow
engine throttled the app's namespace. With the events component (2026-10-19) the spec passes 19 of 19 with events on.

With the notifications component (2026-10-20) apps and platform services that order notifications get production answers and a tester sees every message in an inbox.

Not yet: Correspondence (apps that send correspondence get a 404), Profile
beyond what the Register component serves, the inspection GUI over the components' `api/localtest/` endpoints, the
a way to keep the vendored copies in
step with upstream, and the rest of the studioctl integration. All of it, with status, is in [`../TODO.md`](../TODO.md).
