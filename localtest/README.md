# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes. Each
component runs the production source at a pinned upstream commit; local behaviour comes from `#if !LOCALTEST`
switches and an adjacent `fakes/` folder, never from edits to the vendored code.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory store: a built-in seed plus the `testData.json` each app runner posts (see its README) |
| `components/authentication/` | Production Authentication with the upstream ID-porten login replaced by a test-user picker; the old localtest token endpoints (`/Home/GetTestUserToken`, `GetTestOrgToken`, `GetTestStudioToken`) on the production exchanges |
| `components/authorization/` | Production PDP with app policies in the policy store shared with Access Management; roles, access packages, delegations and authorized parties from Access Management |
| `components/access-management/` | Production Access Management on Postgres: imports parties and roles from the Register, takes instance delegations from apps, serves the PDP's PIP and authorized parties |
| `components/storage/` | Production Storage on Postgres with file-backed blobs and file scan marked clean; application metadata and texts pushed by the app runner |
| `components/workflow-engine/` | The workflow engine from the altinn-studio monorepo, unmodified |
| `ingress/` | nginx on `local.altinn.cloud:8000`: path-prefix routing to the components, one drop-in route per app started with `run-app.sh` (`ingress/app-routes/`), 404 for any app path without a route, Secure cookie flag removed |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate them with the production token handlers |
| `smoke/apps/` | The `ttd/testapp` fixture (metadata, texts, policy) the smoke tests push |
| `docker-compose.yml` | `ingress`, `register`, `authentication`, `authorization`, `access-management`, `postgres`, `storage`, `workflow-engine`, `pdf3`; the ingress has a fixed address that every component maps `local.altinn.cloud` to |
| `run-app.sh`, `stop-apps.sh` | Start an app from a source checkout against the stack (build, run, route, push its artefacts) and stop all of them |
| `run-cypress.sh` | Run the app-frontend Cypress suite from the monorepo against the stack |
| `smoke-test.sh` | End-to-end check of the running stack through the ingress |

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
registered is a 404 at the ingress.

## Run it

```sh
grep -q local.altinn.cloud /etc/hosts || echo '127.0.0.1 local.altinn.cloud' | sudo tee -a /etc/hosts
docker compose up --build -d        # or podman-compose up -d (build the images first)
./run-app.sh ~/code/altinn-studio/src/test/apps/frontend-test 5005
open 'http://local.altinn.cloud:8000/ttd/frontend-test/'
```

`run-app.sh <app-dir> [port]` builds the app, starts it on the given port with platform URLs pointing at the stack,
writes its ingress route, and performs the pushes above. Its log is `.run/<org>-<app>.log`. Each app needs its own
port; `./stop-apps.sh` stops them all and removes their routes.

To swap in a rebuilt image: `podman build -t localhost/localtest-<name>:dev components/<name>`, then recreate the
container. A component other services depend on (the PDP, for example) is only replaced by `podman-compose down` and
`up -d --no-build`; check `podman inspect` that the container runs the new image id.

## Tests

| Command | What it checks |
| --- | --- |
| `./smoke-test.sh` | Through the ingress: picker sign-in, production cookies without the Secure flag, `refresh`, an org token through the Maskinporten exchange, the Studio token, metadata/texts/policy pushes, PDP decisions, and a full Storage instance lifecycle (create, process start by the app, upload authorized by the PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing) |
| `components/authorization/smoke-test.sh` | Against the stack: decisions for the seeded roles through Access Management, re-pushed policies, authorized parties |
| `components/register/smoke-test.sh`, `components/storage/smoke-test.sh` | The component on its own (Storage needs ports 8000 and 5433 free, so stop the stack first) |
| `./run-cypress.sh --spec <specs>` | The app-frontend Cypress specs; start every app those specs use with `run-app.sh` first |

## Status

Running together and verified: all components above, with sign-in, Storage, PDP decisions, instance delegations
(signing) and authorized parties on production code. All 13 test apps in `altinn-studio/src/test/apps` run against
the stack: 101 Cypress specs with 418 tests, all passing except one test in process-transition-test's
`workflow-status.ts`. That app publishes Altinn events, and there is no Events service yet: every publish is a 404,
the workflow engine retries the step and then throttles the app's namespace for an hour, which stalls later
scenarios. Started with `AppSettings__RegisterEventsWithEventsComponent=false`, the spec passes 19 of 19.

Not yet: an Events service, stubs for Profile, Notifications and Correspondence (apps that send correspondence log a 404), a
Resource Registry beyond the app resources Access Management derives itself, a way to keep the vendored copies in
step with upstream, and studioctl integration.
