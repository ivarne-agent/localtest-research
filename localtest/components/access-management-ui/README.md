# access-management-ui

Production Access Management GUI, `am.ui` (`vendor/`, upstream `Altinn/altinn-access-management-frontend`, see
`vendor/UPSTREAM.md`): the React SPA and the .NET BFF (backend for frontend) that serves it, signs nothing in itself
(the user's `AltinnStudioRuntime` cookie is the login) and calls the platform for it. It is the **optional group
`access-management`** (`studioctl env up -r localtest-v2 --with access-management`, `--with everything`, or
`.WithAccessManagement()` in an Aspire AppHost), which starts only the `access-management-ui` container: Access
Management's API (`access-management`), Authentication, the PDP, Profile, the Register and the Resource Registry are in
the default stack ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-manifest)). It is stateless: no database, nothing
provisioned. Research and decisions: [`../../../planned-components/access-management-ui/plan.md`](../../../planned-components/access-management-ui/plan.md)
(TODO.md task 68).

Open it at **`http://am.ui.local.altinn.cloud:8000/accessmanagement/ui`**, a host of its own: everything it serves is
under `/accessmanagement/` (`/accessmanagement/ui/…` the SPA, `/accessmanagement/api/v1/…` its API,
`/accessmanagement/assets/…` the bundle), which is the Access Management API's prefix on the main host. The browser
needs the name in the hosts file: `sudo studioctl env hosts add` writes it with the stack's other names (or add
`127.0.0.1 am.ui.local.altinn.cloud` by hand). Signed out, it sends the browser to the stack's Authentication and its
test-user picker and back; signed in to an app or the inbox, it opens directly.

| Path                                                                                                                      | What                                                                                                                                                                                                                                                                                                        |
| ------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `vendor/`                                                                                                                 | Upstream's whole repository at the pin, blob for blob (its tree id is upstream's), plus `UPSTREAM.md`                                                                                                                                                                                                       |
| `patches/0001-bff-localtest-hooks.patch`, `0002-bff-access-token-certificate.patch`, `0003-spa-links-follow-origin.patch` | The three localtest changes, applied at image build (below)                                                                                                                                                                                                                                                 |
| `fakes/ServiceDiscovery/`, `fakes/Cookies/`                                                                               | Copies of `../shared/fakes/`, compiled into the BFF                                                                                                                                                                                                                                                         |
| `localtest.build.props` / `.targets`                                                                                      | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import the vendored `Directory.Build.props`, define `LOCALTEST`, relax the analyzers for the fakes, compile `fakes/**/*.cs` into `Altinn.AccessManagement.UI` and add `Microsoft.Extensions.ServiceDiscovery` 10.8.0 |
| `Dockerfile`, `.dockerignore`                                                                                             | Mirror upstream's `Dockerfile` (the Vite build on `node:22-slim`, the BFF published on `sdk:10.0-alpine`, `aspnet:10.0-alpine` at runtime); each difference is marked `localtest:`                                                                                                                          |
| `smoke-test.sh`                                                                                                           | Through the front door, see "Tests"                                                                                                                                                                                                                                                                         |

## What changed, and why

**The patches, applied at build, not committed onto `vendor/`**, as Arbeidsflate's: the SPA's change is TypeScript,
which has no `#if`, and `vendor-sync.py` takes one mode per component (`local: patches`). The Dockerfile's `source` stage
(the SDK image, which has git) copies `vendor/` and `patches/`, runs `git apply --check` and `git apply`; the Node and
.NET stages build from that tree. The C# hunks are `#if LOCALTEST` / `#if !LOCALTEST` blocks, so they read as the house's
directive sites (`LOCALTEST` comes from `localtest.build.props`).

| Patch | What                                                                                                                                                                                                                                                                    | Why                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ----- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 0001  | `Program.cs`, after `WebApplication.CreateBuilder`: `LocaltestServiceDiscovery.AddLocaltestServiceDiscovery`, `LocaltestCookiePolicy.AddLocaltestCookiePolicy` and `AccessTokenSettings` bound to its section; in the JWT cookie options `RequireHttpsMetadata = false` | The platform by service name; cookies without `Secure` on plain http (`XSRF-TOKEN`, `selectedLanguage`, the party cookies and a refreshed `AltinnStudioRuntime` are `Secure` outside Development, and without `XSRF-TOKEN` the SPA sends no antiforgery header, so every POST would fail); patch 0002's certificate path (upstream never binds the section); Authentication's metadata over http (upstream allows it in Development only) |
| 0002  | `AccessTokenProvider.GetAccessToken`: `#if !LOCALTEST` the Key Vault certificate `JWTCertificate` `#else` the file `AccessTokenSettings:AccessTokenSigningCertificateFullPath` (`/certs/localtest-accesstoken.pfx`)                                                     | The BFF signs its platform access tokens (issuer `amui`, app `access-management-ui`) for Profile, the Register and Authentication; every component's `IPublicSigningKeyProvider` fake returns the shared certificate for any issuer, so they accept `amui` unchanged                                                                                                                                                                      |
| 0003  | `pathUtils.tsx`: on http on `am.ui.<host>` the platform and the start page are `<host>`, the inbox `af.<host>`, the GUI's own base `am.ui.<host>`, the page's port kept                                                                                                 | Upstream maps every host it does not know to production: the party switch (`reportee/changeandredirect`), logout, the login on an expired token and the inbox links went to `*.altinn.no`                                                                                                                                                                                                                                                 |

**The image** (each `localtest:` line explains itself): the `source` stage; `docker.io/library/` before `node:22-slim`
(Podman's short names outside a terminal); `NODE_EXTRA_CA_CERTS` for corepack and Yarn when the sandbox's CA bundle is
mounted; the backend stage keeps the component layout and sets the two MSBuild variables; **`ASPNETCORE_ENVIRONMENT=Production`**
instead of upstream's `Development` (which production's deployment overrides): Development loads the SPA from Vite's dev
server on `localhost:5173` and `appsettings.Development.json` (at22, the Access Management mock, every feature flag on);
`Production` has no settings file of its own, so `appsettings.json` applies, every `MockSettings` switch and every
`FeatureManagement` flag off (the owner's decision: production's feature set); port 8080 (`ASPNETCORE_HTTP_PORTS`), the
stack's convention, so the manifest names neither port nor health (`/health`).

## Configuration

The manifest's `access-management-ui` envs; everything else is `appsettings.json`'s.

| Variable                                                                                                                       | Value                                                                                                                                                                                       | Why                                                                                                                                                                                                                                                                                                                           |
| ------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `PlatformSettings__ApiAccessManagementEndpoint`                                                                                | `http://access-management/accessmanagement/api/`                                                                                                                                            | Versionless; the clients append `v1/` or `v2/`. By service name                                                                                                                                                                                                                                                               |
| `PlatformSettings__ApiProfileEndpoint`, `__ApiRegisterEndpoint`, `__ApiAuthorizationEndpoint`, `__ApiResourceRegistryEndpoint` | `http://profile/profile/api/v1/`, `http://register/register/api/v1/`, `http://authorization/authorization/api/v1/` (the PEP's PDP client), `http://resource-registry/resourceregistry/api/` | By service name                                                                                                                                                                                                                                                                                                               |
| `PlatformSettings__ApiAuthenticationEndpoint`                                                                                  | `{{origin}}/authentication/api/v1/`                                                                                                                                                         | The public origin: `HomeController` also sends the browser there to sign in (`…authentication?goto=`)                                                                                                                                                                                                                         |
| `PlatformSettings__OpenIdWellKnownEndpoint`                                                                                    | `{{origin}}/authentication/api/v1/openid/`                                                                                                                                                  | The JWT cookie's metadata, fetched outside the factory, naming a public `jwks_uri`                                                                                                                                                                                                                                            |
| `GeneralSettings__FrontendBaseUrl`                                                                                             | `{{origin:am.ui}}`                                                                                                                                                                          | The sign-in's `goto` and `changeandredirect`'s fallback                                                                                                                                                                                                                                                                       |
| `GeneralSettings__Hostname`                                                                                                    | `{{host}}`                                                                                                                                                                                  | The party cookies' and the refreshed runtime cookie's domain, Authentication's                                                                                                                                                                                                                                                |
| `GeneralSettings__AllowedRedirectDomains__2`                                                                                   | `{{host}}`                                                                                                                                                                                  | `changeandredirect`'s `goTo` check; `appsettings.json`'s `altinn.no` and `altinn.cloud` stay, the third entry covers a custom host                                                                                                                                                                                            |
| `AccessTokenSettings__AccessTokenSigningCertificateFullPath`                                                                   | `/certs/localtest-accesstoken.pfx`                                                                                                                                                          | Patch 0002                                                                                                                                                                                                                                                                                                                    |
| unset                                                                                                                          | `PlatformSettings__ApiDialogportenEndpoint`, `KeyVaultSettings__SecretUri`, `Altinn__AppConfiguration__*`, `PlatformSettings__SubscriptionKey`                                              | Upstream's empty values: no dialog lookup for instance delegations (the client returns nothing without a base, and Dialogporten is an optional group); Key Vault's `new Uri("")` fails at start and is caught (two error lines, no network); no App Configuration; an empty `Ocp-Apim-Subscription-Key`, which nothing checks |

`dependsOn`: `access-management`, `authentication`, `authorization` (start order only; the BFF calls nothing at start).

## Seeded system resources

The GUI asks the PDP whether the user may manage a party (`isAdmin`, `isHovedadmin`, `isClientAdmin`, … on Altinn's
system resources `altinn_access_management`, `altinn_access_management_hovedadmin`, `altinn_client_administration`,
`altinn_instance_delegation`, `altinn_maskinporten_scope_delegation`), and Access Management's end-user API refuses every
delegation without `write` on `altinn_access_management`. The stack's Resource Registry seeds the five from tt02
(`../resource-registry/README.md`, "The seed"), so a DAGL (the access package `hovedadministrator`) manages their
organisation and a person their own party, at authentication level 3.

## Links in and out

- The inbox (Arbeidsflate, patch 0004 there) links its "Tilgangsstyring", a dialog's "who has access" and the party switch
  to `am.ui.<host>`.
- Apps: while the group runs, `studioctl run`, `studioctl app env` and the Aspire package set
  `PlatformFrontendSettings__AccessManagementChangeAndRedirectUrl`
  (`http://am.ui.local.altinn.cloud:8000/accessmanagement/api/v1/reportee/changeandredirect?partyId={partyId}&goTo={goTo}`)
  and `…AccessManagementUrl` (`…/accessmanagement/ui`); without it both stay unset ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#what-the-app-runner-pushes)).
- Access Management's consent and service-owner request links name `http://am.ui.<host>:<port>/…` (two directive sites
  there, `../access-management/README.md`).
- Out of the GUI: the platform (logout, sign-in) and Home on the public origin, the inbox on `af.<host>` (503 naming its
  group when it is off); the info portal's help and news links stay production's.

## Differences from production

1. Plain http on `am.ui.local.altinn.cloud:8000`; its cookies without `Secure` there (the cookie policy), no HSTS.
2. The three patches: the platform by service name, the cookies, http metadata, the shared signing certificate, the SPA's
   links.
3. `Production` with `appsettings.json` alone: every feature flag off (no Skyra, no activity log, no handled requests,
   no ID-porten authorizations, …), every mock off. Azure App Configuration, which production uses for flags in some
   environments, is not configured.
4. No Key Vault, no Application Insights, no OpenTelemetry (see Known gaps).
5. Internet dependencies stay, as elsewhere in the stack: the Inter font and favicon from altinncdn.no in the browser,
   the org list (`altinn-orgs.json`) and the consent templates (`raw.githubusercontent.com`) in the BFF.

## Known gaps

- **Requests** (`Forespørsler`): the SPA asks `request/received/count` on every page; the stack's Access Management (its
  pin) has the end-user request API behind its own flags (`EnableRequestAssignmentResource`, `…Package`), off, so the
  count is a 404 in the console and the requests pages are empty. Production's feature set, not chased (the owner, task 68).
- **System users**: the pending system-user requests (Authentication's system-user endpoints) answer 500: the stack's
  Authentication has no Postgres for them. The system access pages are empty.
- **Telemetry**: the BFF has Application Insights only (off without the Key Vault key); with the `monitoring` group
  nothing of the GUI shows in Grafana. An upstream item (`../../upstreaming/access-management-ui.md`).
- A person who is not yet a right holder is added the production way (national identity number and last name); the GUI
  does not look a person up by party uuid.
- The data-protection keys are in the container; a restart invalidates the antiforgery tokens until the page is reloaded.

## Tests

`./smoke-test.sh [BASE]` needs `--with access-management` (or `--with everything`) and goes through the front door:
health; the redirect to Authentication without a cookie (`goto` on `am.ui` with `openAccountMenu`); the sign-in as a
browser does it, with a cookie jar (the picker as user 1337 at level 3, Authentication's `upstream/callback`, back on the
GUI with the runtime cookie); the shell (the Razor view with the bundle, not Vite's dev server, production's feature
flags all off, `XSRF-TOKEN` without `Secure`, a CSP, no HSTS, the bundle with patch 0003, the stylesheet, the
antiforgery pair); the BFF's API against the stack (the user's profile from Profile with the `amui` token, the parties
from Access Management, DDG Fitness from the Register, `isAdmin` and `isHovedadmin` on DDG Fitness and `isAdmin` on the
user's own party from the PDP with the seeded policies, the roles on DDG Fitness); the party switch the apps and the
inbox use (`changeandredirect` to the inbox with `AltinnPartyId` for the public host without `Secure`, a foreign `goTo`
refused); and one delegation through the GUI's API: a POST without the antiforgery header refused, Kari Nordvik added as
a right holder of DDG Fitness by identity number and last name, the access package `sport-og-fritid` delegated to her,
read back from Access Management's own end-user API and the GUI's, revoked and gone, and the right holder removed: 40
checks. It leaves nothing behind but the sign-in's Authentication session.

## Verified

2026-10-30, task 68, on this machine (4 cores, 8 GB, arm64). The disk had about 2 GB free, too little for the five
image builds (the SDK, Node and runtime base images alone are about 3 GB), so the images were built by the images
workflow from the branch (`workflow_dispatch`, run 41129854906, `sha-099d4e8`) and the stack ran them through a
studioctl build whose manifest pinned the five changed images (this one, Access Management, the Resource Registry, the
front door, Arbeidsflate) to that tag and the rest to `sha-d3bbf4a`:

- **Image**: 151 MB (`aspnet:10.0-alpine` and the BFF with the bundle); the workflow built it in about 4 minutes per
  architecture without a cache.
- **`--with everything`** (29 containers, with `monitoring`): up in 49 s; root 30 of 30; this `smoke-test.sh` 40 of 40;
  `localtest/smoke-test.sh` 87 and two `FAIL`: the known `app-frontend` bundle (verify-after-sync 6.1) and "a service that
  is off" (its default `/correspondence/` runs with `everything`); Resource Registry 44 of 44, Authorization 32 of 32,
  Arbeidsflate 59 of 59. Memory (`podman stats`): the GUI 108 MB after the suites and a browser session, Access
  Management 366 MB; `free -m` 2350 MB available.
- **`--with access-management`** alone (14 containers): up in 27 s from `everything`; root 29 (one skip), this suite 40
  of 40; the GUI 64 MB, 4540 MB available.
- **Default stack** (13 containers): `am.ui.local.altinn.cloud` answers 503 problem details naming the group,
  `--with access-management` and `.WithAccessManagement()` (an HTML page for a browser); root 29 (one skip);
  `localtest/smoke-test.sh` 86 and the known `FAIL`. `studioctl app env` sets no Access Management link.
- **Minimal mode**: root 26, four skips; `am.ui` answers 503.
- **Browser** (Chromium, `playwright-cli`): `/accessmanagement/ui` without a cookie went to the picker; as Sophie Salt at
  level 3 the GUI opened with the account menu listing her parties (Sophie Salt, DDG Fitness and its three sub-units, Eas
  Health Consulting, …); choosing DDG Fitness switched party and showed the administrator's menu (Brukarar, Fullmakter,
  Klientadministrasjon, Maskinportenadministrasjon, …); "Brukarar" listed Sophie Salt as "Dagleg leiar" and the others
  with access. In the inbox (`af.`), the menu's "Tilgangsstyring" and "Sjå mine fullmakter" link to
  `http://am.ui.local.altinn.cloud:8000/accessmanagement/ui…` and open the GUI signed in. Console: the `request/received/count`
  404 (Known gaps). Screenshots in the task's evidence folder (`/home/agent/code/.handover/task68-access-management-ui/`).
- **An app**: `studioctl app env` with the group up gives `AccessManagementChangeAndRedirectUrl` and
  `AccessManagementUrl` on `am.ui`, the smoke test's `changeandredirect` call is the template filled in.

Static: the BFF with the patches and the overrides builds with 0 warnings and 0 errors (upstream's own build: 0 and 0);
run outside a container in `Production` it answers `/health` and redirects `/accessmanagement/ui/…` to Authentication;
`vendor-sync.py check --all` passes (`patches (3 apply)`).
