# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory register seeded from `testdata/testData.json` (see its README) |
| `components/authentication/` | Production Authentication with the upstream provider replaced by a test-user picker (see its README) |
| `components/authorization/` | Production Authorization (PDP) with policies from a folder or the running app and PIPs over the register seed (see its README) |
| `components/storage/` | Production Storage on Postgres, file-backed blobs, scan marked clean; application metadata and texts pushed by the app runner (see its README) |
| `smoke-test.sh` | End-to-end check of the running stack through the ingress |
| `ingress/` | nginx on `local.altinn.cloud:8000`: path-prefix routing to the components, one drop-in file per app started with `run-app.sh` (`ingress/app-routes/`, graceful reload), `/` to the app on the developer machine, Secure cookie flag removed |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate against it with the production token handlers (see its README) |
| `docker-compose.yml` | `ingress`, `register`, `authentication`, `authorization`, `postgres`, `storage`; the ingress has a fixed address that every component maps `local.altinn.cloud` to |

Fake-only components (authentication, later profile, events, notifications) are written fresh for this layout and
consume the vendored services the way their production counterparts do: the authentication fake resolves users and
parties through the Register API, and `testData.json` is the Register seed, not a shared file.
Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a reference for
which behaviours the fakes must provide.

## What the app runner pushes

In production Altinn Studio pushes an app's artefacts to the platform at deploy time; locally that is the app runner's
job (`run-app.sh` today, studioctl later). No component watches another. For one app the runner calls, in order:

| Step | Endpoint | What it does |
| --- | --- | --- |
| 1 | `PUT register/api/localtest/apps/{org}/{app}` with the app's `App/wwwroot/testData.json` as the body (empty body: no test data) | Register stores the test data as posted and rebuilds its store (built-in seed plus every registered app); it fetches nothing |
| 2 | `POST storage/api/v1/applications?appId={org}/{app}` (or `PUT storage/api/v1/applications/{org}/{app}` when it exists) with `App/config/applicationmetadata.json`, and `POST`/`PUT storage/api/v1/applications/{org}/{app}/texts[/{lang}]` with each `App/config/texts/resource.{lang}.json` | Production Storage endpoints, with a service-owner token standing in for Designer's `studio.designer` claim |
| 3 | `POST accessmanagement/api/localtest/refresh` | Access Management re-imports parties and external roles from the Register's streams (full re-poll, production code), then applies the seed's own roles, Altinn 2 role codes and access packages and prunes what the seed no longer grants |

Still fetched by the component itself from the running app, to be turned into a push later: the authorization policy
(PDP). The Register's store is rebuilt only by these calls, not on a timer, so an edited `testData.json` becomes
visible after the next registration or refresh. An app path the runner has not registered is a 404 at the ingress.

Run it:

```sh
grep -q local.altinn.cloud /etc/hosts || echo '127.0.0.1 local.altinn.cloud' | sudo tee -a /etc/hosts
docker compose up --build -d        # or podman-compose
open 'http://local.altinn.cloud:8000/authentication/api/v1/authentication?goTo=http://local.altinn.cloud:8000/ttd/testapp/'
```

`./smoke-test.sh` verifies through the ingress: browser sign-in with the picker, the production cookies without the
Secure flag, `refresh`, an org token through the production Maskinporten exchange, PDP decisions for the seeded roles,
and a full instance lifecycle on production Storage (create, process start by the app, data upload authorized by the
PDP, read-back, scan result, denial for a user without the role, process end, service-owner listing).

Status: ingress, register, authentication, authorization and storage run together. Not yet: an app driven through the
stack (the app on the developer machine is routed but untested), profile/events/notifications stubs, and studioctl
integration.
