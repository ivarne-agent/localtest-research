# localtest MVP

Compose-based local platform for Altinn apps, built from vendored production services and small fakes.
Design: [`../planned-components/mvp-vendoring-model.md`](../planned-components/mvp-vendoring-model.md).

| Folder | What |
| --- | --- |
| `components/register/` | Production Register on an in-memory register seeded from `testdata/testData.json` (see its README) |
| `components/authentication/` | Production Authentication with the upstream provider replaced by a test-user picker (see its README) |
| `components/authorization/` | Production Authorization (PDP) with policies from a folder or the running app and PIPs over the register seed (see its README) |
| `docker-compose.yml` | `register` and `authentication` services; no ingress yet, so the authentication service is reached on its own port |

Fake-only components (authentication, later profile, events, notifications) are written fresh for this layout and
consume the vendored services the way their production counterparts do: the authentication fake resolves users and
parties through the Register API, and `testData.json` is the Register seed, not a shared file.
Today's localtest in `altinn-studio/src/Runtime/localtest` is not a basis for anything here, only a reference for
which behaviours the fakes must provide.

Status: register and authentication run standalone and together (see the component READMEs for the verified
flows). Next: nginx ingress on `local.altinn.cloud:8000`, then authorization (PDP) and storage.
