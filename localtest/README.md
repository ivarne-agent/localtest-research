# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes. Each
component runs the production source at a pinned upstream commit; local behaviour comes from `#if !LOCALTEST`
switches and an adjacent `fakes/` folder, never from edits to the vendored code.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory store: a built-in seed plus the `testData.json` each app runner posts (see its README) |
| `components/authentication/` | Production Authentication on memory instead of Postgres; it signs in against the localtest app's fake ID-porten and trusts its fake Maskinporten, so its own OIDC client, token validation, Profile client and exchanges run |
| `components/authorization/` | Production PDP with app policies in the policy store shared with Access Management; roles, access packages, delegations and authorized parties from Access Management |
| `components/access-management/` | Production Access Management on Postgres: imports parties and roles from the Register, takes instance delegations from apps, serves the PDP's PIP and authorized parties |
| `components/storage/` | Production Storage on Postgres with file-backed blobs and file scan marked clean; application metadata and texts pushed by the app runner |
| `components/events/` | Production Events on the shared Postgres, emptied on every start: apps publish events, subscriptions are validated and delivered inside the stack, webhooks to anything outside it are captured; `events/api/localtest/` shows it all |
| `components/workflow-engine/` | The workflow engine from the altinn-studio monorepo, unmodified |
| `localtest/` | The localtest app, the front door on `local.altinn.cloud:8000` (C#, Native AOT), and the home of localtest features that are not a platform component. Its proxy routes by host and path prefix to the components, one route per app pushed by `run-app.sh` (`PUT localtest/api/proxy/apps/{org}/{app}`), serves the app-frontend files, answers 404 for any app path without a route, and removes `Secure` from cookies. It also hosts the fake ID-porten (test-user picker) and Maskinporten that production Authentication trusts, and the old localtest's start page and `/Home` test helpers |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate them with the production token handlers |
| `tools/repin-vendor.py` | Re-pins a component's `vendor/` to a newer upstream commit: takes upstream's version of unchanged files, three-way merges the files with `LOCALTEST` sites, reports conflicts (see each `vendor/UPSTREAM.md`) |
| `reviews/` | Reviews of how each component is localtestified: vendored code fidelity, Dockerfile, fakes, differences from production, findings |
| `smoke/apps/` | The `ttd/testapp` fixture (metadata, texts, policy) the smoke tests push |
| `docker-compose.yml` | `localtest`, `register`, `authentication`, `authorization`, `access-management`, `postgres`, `storage`, `events`, `workflow-engine`, `pdf3`; the localtest app has a fixed address (10.200.0.10) that every component maps `local.altinn.cloud` to, and the others get addresses from 10.200.0.128/25 only, so none can take it |
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
| 4 | `POST accessmanagement/api/localtest/refresh` | Access Management re-imports parties and external roles from the Register's streams (production code, full re-poll), runs the production resource sync against the local stand-in (service owners, `app_{org}_{app}` resources and their policy subjects from Storage and the policy store), then applies the seed's own roles, Altinn 2 role codes and access packages and prunes what the seed no longer grants |

Nothing is fetched from the running app any more. The Register's store is rebuilt only by these calls, not on a timer,
so an edited `testData.json` becomes visible after the next registration or refresh. An app path the runner has not
registered is a 404 at the proxy.

## Run it

```sh
grep -q ' local.altinn.cloud' /etc/hosts || echo '127.0.0.1 local.altinn.cloud' | sudo tee -a /etc/hosts
# The PDF generator and the workflow engine have host names of their own, which the apps on this machine call.
grep -q pdf.local.altinn.cloud /etc/hosts || echo '127.0.0.1 pdf.local.altinn.cloud workflow-engine.local.altinn.cloud' | sudo tee -a /etc/hosts
docker compose up --build -d        # or podman-compose up -d (build the images first)
./run-app.sh ~/code/altinn-studio/src/test/apps/frontend-test 5005
open 'http://local.altinn.cloud:8000/ttd/frontend-test/'
```

`run-app.sh <app-dir> [port]` builds the app, starts it on the given port with platform URLs pointing at the stack,
registers its route with the proxy, and performs the pushes above. Its log is `.run/<org>-<app>.log`. Each app needs its own
port; `./stop-apps.sh` stops them all and removes their routes.

A component added after the Postgres volume was created needs its database created by hand, because Postgres runs
init scripts only on an empty volume. Each init script is idempotent, for example
`podman exec -i localtest_postgres_1 sh < components/events/postgres-init/30-events.sh`.

To swap in a rebuilt image: `podman build -t localhost/localtest-<name>:dev components/<name>`, then recreate the
container. A component other services depend on (the PDP, for example) is only replaced by `podman-compose down` and
`up -d --no-build`; check `podman inspect` that the container runs the new image id.

## Tests

| Command | What it checks |
| --- | --- |
| `./smoke-test.sh` | Through the proxy: picker sign-in, production cookies without the Secure flag, `refresh`, an org token through the Maskinporten exchange, the Studio token, metadata/texts/policy pushes, PDP decisions, and a full Storage instance lifecycle (create, process start by the app, upload authorized by the PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing) |
| `components/authorization/smoke-test.sh` | Against the stack: decisions for the seeded roles through Access Management, re-pushed policies, authorized parties |
| `localtest/smoke-test.sh` | The localtest app: the proxy (routes by host and path, trailing-slash redirects, cookies without `Secure`, streaming, the app-frontend files, the app route API), the fake ID-porten and Maskinporten, and the `/Home` login contract (every level, party cookies, token helpers) |
| `components/events/smoke-test.sh` | Against the stack: app events published, deduplicated, authorized and delivered; subscriptions validated; capture of external endpoints; the production read API; reset |
| `components/register/smoke-test.sh`, `components/storage/smoke-test.sh` | The component on its own (Storage needs ports 8000 and 5433 free, so stop the stack first) |
| `./run-cypress.sh --spec <specs>` | The app-frontend Cypress specs; start every app those specs use with `run-app.sh` first |

## Status

Running together and verified: all components above, with sign-in, Storage, PDP decisions, instance delegations
(signing) and authorized parties on production code. All 13 test apps in `altinn-studio/src/test/apps` run against
the stack: 101 Cypress specs with 418 tests (run 2026-10-18), all passing except one test in process-transition-test's
`workflow-status.ts`, which failed because there was no Events service: every publish was a 404, and the workflow
engine throttled the app's namespace. With the events component (2026-10-19) the spec passes 19 of 19 with events on.

Not yet: Notifications and Correspondence (apps that order notifications or send correspondence get a 404), Profile
beyond what the Register component serves, the inspection GUI over the components' `api/localtest/` endpoints, a
Resource Registry beyond the app resources Access Management derives itself, a way to keep the vendored copies in
step with upstream, and studioctl integration.
