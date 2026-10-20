# receipt

Production Altinn Receipt (`vendor/`, see `vendor/UPSTREAM.md`), the platform receipt for archived instances at
`/receipt/{instanceOwnerPartyId}/{instanceGuid}`: the page Dialogporten's go-to action and the Dialogporten adapter
link archived instances to. It is the **optional group `receipt`** (`studioctl env up -r localtest-v2 --with receipt`,
or `--with everything`), implying no other group. It is stateless: no database, queue or blob store, and nothing is
provisioned. Its backend reads the instance from Storage, the owner from Register and the user from Profile (the
Register component) with the user's `AltinnStudioRuntime` cookie, adding a `platform`/`receipt` platform access token
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
5. No Altinn 2 portal, so no `altinnPersistentContext` cookie: the language comes from the user's profile (the
   Register component's Profile data), as upstream's frontend falls back to it.
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

## Known gaps

- Only an organisation-owned instance viewed by a user with a role (party 500000, user 1337) is in the smoke test;
  person-owned instances, self-identified users and service-owner visits are not.
- The fixture has no PDF, so the PDF link is not exercised (it goes through the same decorator).
- The localtest app's own pre-studioctl proxy (`run-app.sh`, `Proxy/PlatformRoutes.cs`) has no `/receipt/` route.
- Upstream's xUnit tests are not run.
- Once tasks 10 and 11 are in, the smoke test should follow an archived dialog's go-to URL to the receipt.
