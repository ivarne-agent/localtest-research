# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory register seeded from `testdata/testData.json` (see its README) |
| `components/authentication/` | Production Authentication with the upstream provider replaced by a test-user picker (see its README) |
| `ingress/` | nginx on `local.altinn.cloud:8000`: path-prefix routing to the components, `/` to the app on the developer machine, Secure cookie flag removed |
| `docker-compose.yml` | `ingress`, `register`, `authentication` (authorization and storage follow); the ingress has a fixed address that every component maps `local.altinn.cloud` to |

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

Verified through the ingress: browser sign-in with the picker, the four production cookies without the Secure flag,
`refresh`, an org token through the production Maskinporten exchange, a headless user token, and session reuse.

Status: ingress, register and authentication done; authorization (PDP) and storage in progress.
