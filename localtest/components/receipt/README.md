# receipt

Production Altinn Receipt (`vendor/`, see `vendor/UPSTREAM.md`), the platform receipt for archived instances at
`/receipt/{instanceOwnerPartyId}/{instanceGuid}`: the page Dialogporten's go-to action and the Dialogporten adapter
link archived instances to. It is the **optional group `receipt`** (`studioctl env up -r localtest-v2 --with receipt`,
or `--with everything`), implying no other group. It is stateless: no database, queue or blob store, and nothing is
provisioned. Its backend reads the instance from Storage, the owner from Register and the user from Profile (the
profile component) with the user's `AltinnStudioRuntime` cookie, adding a `platform`/`receipt` platform access token
for Register and Profile; the browser reads Storage's application metadata and texts and altinncdn.no's org list.

| Path | What |
| --- | --- |
| `vendor/` | The whole upstream repository at the pin, unmodified except two `LOCALTEST` sites in `src/backend/Altinn.Receipt/Program.cs` (see `vendor/UPSTREAM.md`) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST` for the vendored project and compile `fakes/**/*.cs` into the host. Upstream's root `Directory.Build.props` (CSharpier) is not imported, as upstream's image never sees it |
| `fakes/` | Compiled into the host (namespace `Altinn.Platform.Receipt.Localtest`): the hook `LocaltestReceipt.Apply` and the `IStorage` decorator `SelfLinksStorage` |
| `Dockerfile`, `.dockerignore` | Mirror upstream's: the Node 24.21 stage bundles the frontend (`yarn --immutable`, `yarn run build`, from the npm registry), the SDK 10.0 stage publishes the host, the chiseled ASP.NET 10.0 runtime runs it as `$APP_UID` on 5060. Each difference is marked `localtest:` |
| `smoke-test.sh` | Against the stack with the group: health, sign-in redirect and back, page and bundle, Profile, the archived instance with rewritten attachment links, language, hidden groups, 403/404, the platform token |

## What is faked

| Production | Here |
| --- | --- |
| `IStorage` (`StorageWrapper`) returning Storage's instance as is | `SelfLinksStorage` wraps the production `StorageWrapper` and rewrites each data element's `selfLinks.platform` from `https://platform.{GeneralSettings:Hostname}/` to `Localtest:Receipt:PlatformEndpoint` (the stack's public origin). Storage keeps the production link format, whose host neither resolves nor serves TLS locally; the frontend uses the link for attachment and PDF downloads and makes it relative once its host is the page's. Same endpoint, same cookie, same authorization |
| Settings from the deployment (chart values) | Environment variables from studioctl: `GeneralSettings__OpenIdWellKnownEndpoint`, `GeneralSettings__Hostname` (`local.altinn.cloud`), the four `PlatformSettings__Api*Endpoint` on the public origin (the browser follows the sign-in redirect), `Localtest__Receipt__PlatformEndpoint`; `ASPNETCORE_ENVIRONMENT=Development` (cookie metadata over http) and `ASPNETCORE_URLS=http://+:5060` in the image |
| The platform access token's signing certificate (Key Vault in production, mount not public) | The shared test certificate bind-mounted read-only at `/accesstoken/accesstokencredentials.pfx`, the path the production `SigningCredentialsResolver` reads when `AccessTokenSettings` is not bound, as for Events and Notifications |

Not faked and not a site: Key Vault (`kvSetting:SecretUri` is empty), Azure Monitor (only with a Key Vault connection
string), graceful shutdown (skipped in Development), the subscription key header (sent, ignored by everything local).

## Differences from production

1. Attachment and PDF links point at the local public origin (the decorator) instead of `https://platform.{host}/`;
   the target endpoint is the same.
2. The sign-in `goto` uses the request's own origin instead of `https://platform.{Hostname}` (directive site 2).
3. `Development`: the developer exception page and PII in token errors instead of ProblemDetails, the always-on trace
   sampler, no five-second shutdown delay, cookie metadata over http.
4. No API Management: the subscription key is sent and ignored.
5. No Altinn 2 portal, so no `altinnPersistentContext` cookie: the language comes from the user's profile (production
   Profile, whose settings are seeded from the test data's `language`), as upstream's frontend falls back to it.
6. "Tilbake til innboks" (back to the inbox) and "log out" go to the localtest start page, which is upstream's own
   behaviour for `local.altinn.cloud` (decided 2026-10-20: stays as upstream has it; to be raised upstream with
   Arbeidsflate, task 11).
7. Org names, fonts, icons and the logo come from the internet in the browser, as in production.

Nothing is faster than in production (the receipt has no cache), so it adds no row to "Where localtest is faster than
production" in `../../README.md`.

## Using it

Open the receipt on `local.altinn.cloud`, never on `localhost:<port>` or `127.0.0.1:8034`: upstream's frontend sends
`localhost` to `platform.at22.altinn.cloud`. Signed out, it redirects to the sign-in picker and back. With the group
off, `/receipt/...` answers `503` problem details naming the group and the `--with receipt` command.

## Verified

2026-10-21, task 14: `smoke-test.sh` 27 of 27 with `--with everything` against production Profile (the profile
component answers `users/current`; `../profile/README.md`, "Verified").

2026-10-21, task 16 (Profile was still the Register's fake):

Stack: `studioctl env up -r localtest-v2 --with receipt --dev receipt` (the rest at the pinned `sha-a3f62a8`), the
receipt image built from this folder by podman through studioctl (Node stage with `corepack` and `yarn --immutable`
from the npm registry, which succeeded behind the sandbox's TLS mediation (not checked whether through the Dockerfile's `NODE_EXTRA_CA_CERTS` line or the build's own CA configuration), SDK publish, chiseled runtime: 145 MB, user 1654, the 3.1 MB
bundle in `wwwroot/receipt/js/react/`); the whole `env up`, build included, took 2 minutes.

- `./smoke-test.sh`: 27 checks, all pass on the first run, the sign-in round trip through the real front door included.
  The receipt's log has no access-token warning, only the expected ones (ASP.NET's `HTTP_PORTS` override by
  `ASPNETCORE_URLS`, data-protection keys inside the container, and the deliberate 401s without a cookie).
- Chromium through the front door (cookie of user 1337): "Testapp (pushed) er sendt inn" with sender, receiver,
  reference number and "Vedlegg (1)"; the attachment link is relative
  (`/storage/api/v1/instances/500000/{guid}/data/{id}`), "Tilbake til innboks" goes to `http://local.altinn.cloud:8000/`.
- The front door's 503s: with the group off, `/receipt/...` answers `503 application/problem+json` "Component not
  running" with `component` and `group` `receipt` and the `--with receipt` command, and a browser `Accept` gets the HTML
  page; `/receipts` and other unmatched paths still get the localtest app's 404. With the group on and the container
  stopped: "Component not answering" with the `env logs -r localtest-v2 -c receipt` and `env status` commands, both
  shapes. That `env logs` command first failed for a stopped container ("no containers are running"); it now shows the
  stopped container's log (`../../studioctl/README.md`).
- `env up -r localtest-v2 --with correspondence --without dialogporten` (and `--with everything --without receipt`)
  exits 1 in the preflight with the group error, before any container, image or proxy starts.
- Regressions on the default stack (group off): the root `../../smoke-test.sh` 23 of 23 (its unregistered-path 404
  included), `../../run-platform-regression.sh --no-apps` 44 of 44 Cypress tests in the six specs, no retries (apps
  with `studioctl run --dev-frontend`, the monorepo's built `src/App/frontend/dist` served on 8080 with
  `Access-Control-Allow-Origin: *`).

## Known gaps

- Only an organisation-owned instance viewed by a user with a role (party 500000, user 1337) is in the smoke test;
  person-owned instances, self-identified users and service-owner visits are not.
- The fixture has no PDF, so the PDF link is not exercised (it goes through the same decorator).
- The localtest app's own pre-studioctl proxy (`run-app.sh`, `Proxy/PlatformRoutes.cs`) has no `/receipt/` route.
- Upstream's xUnit tests are not run.
- The smoke test does not follow an archived dialog's go-to URL to the receipt: task 10's adapter emits it (with the
  `dialogporten` group as well), but no test chains the two yet; Arbeidsflate (task 11) is not in the stack.
