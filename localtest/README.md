# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory register seeded from `testdata/testData.json` (see its README) |
| `components/authentication/` | Production Authentication with the upstream provider replaced by a test-user picker (see its README) |
| `components/authorization/` | Production Authorization (PDP) with policies from a folder or the running app and PIPs over the register seed (see its README) |
| `components/storage/` | Production Storage on Postgres, file-backed blobs, scan marked clean, app metadata from the app or `apps/` (see its README) |
| `apps/` | Fallback application metadata and texts per `{org}/{app}` for Storage when the app is not running (sample `ttd/testapp`) |
| `smoke-test.sh` | End-to-end check of the running stack through the ingress |
| `ingress/` | nginx on `local.altinn.cloud:8000`: path-prefix routing to the components, one drop-in file per app started with `run-app.sh` (`ingress/app-routes/`, graceful reload), `/` to the app on the developer machine, Secure cookie flag removed |
| `certs/` | The shared test certificate apps and components sign platform access tokens with; the components validate against it with the production token handlers (see its README) |
| `docker-compose.yml` | `ingress`, `register`, `authentication`, `authorization`, `postgres`, `storage`; the ingress has a fixed address that every component maps `local.altinn.cloud` to |

Fake-only components (authentication, later profile, events, notifications) are written fresh for this layout and
consume the vendored services the way their production counterparts do: the authentication fake resolves users and
parties through the Register API, and `testData.json` is the Register seed, not a shared file.
Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a reference for
which behaviours the fakes must provide.

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
