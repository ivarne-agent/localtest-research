# The Access Management GUI (am.ui): plan for `localtest/components/access-management-ui/`

TODO.md task 68. Research date 2026-10-30, read from the code at the pin below; nothing was built or run for this
document. The owner's decisions (2026-10-30, verbatim): "GUI should be part of --everything and a --with
access-management." and "I think we should run the production feature set by default. Try to not think too much about
those featues." So: a new optional group `access-management` with the container `access-management-ui`; the API container
`access-management` stays in the default group; `--with everything` includes the group; the feature flags are
`appsettings.json`'s (all off), not studied further.

## Status

- Research: this document. **No blocker** under the brief's three conditions: no fake at all (three build-time patches
  of a few lines each, two copies of shared fakes, one configuration value per client), every dependency is a component
  the stack runs (or the internet, as elsewhere), and the container is one small .NET process (memory below).
- The component follows. Where the component README and this plan differ, the README is right.

## Pins

| Repo | Commit | What |
| --- | --- | --- |
| [`Altinn/altinn-access-management-frontend`](https://github.com/Altinn/altinn-access-management-frontend/tree/76c599d267c2bb701e75343d07dfe71018285a1a) | `76c599d` (main, 2026-10-30 08:53, the head that day) | The SPA (`src/`, Vite, React 19) and the BFF (`backend/src/Altinn.AccessManagement.UI`, .NET 10) |
| `Altinn/altinn-auth` | the stack's pin (`localtest/components/access-management/vendor/UPSTREAM.md`) | Access Management's API, which the BFF calls |
| `Altinn/dialogporten-frontend` | `bb74dff`, Arbeidsflate's pin | The inbox's links to the GUI |

## Summary

- **One image, one container.** Upstream's `Dockerfile` builds the SPA with Vite (`node:22-slim`, `yarn --immutable`,
  `yarn build`), publishes the BFF (`sdk:10.0-alpine`) and copies the bundle into the BFF's `wwwroot/accessmanagement/`
  (`aspnet:10.0-alpine`, port 80). The BFF serves everything: the SPA shell (Razor view), its assets and its own API.
  `Dockerfile.runner` is the Playwright test runner of upstream's CI (`gh-runner` base and browsers), not the
  application: not vendored into an image, not used.
- **Everything lives under `/accessmanagement/`**: `/accessmanagement/ui/{*}` (the shell, `HomeController`),
  `/accessmanagement/api/v1/...` (the BFF's API, 28 controllers), `/accessmanagement/assets/...` (the bundle) and
  `/health`. The SPA's API base is `import.meta.env.BASE_URL` + `accessmanagement/api/v1/...` (relative, same origin).
  The path prefix `/accessmanagement` is Access Management's API prefix on the main host, so the GUI needs **a host of
  its own**, as in production: `am.ui.<host>` (`am.ui.local.altinn.cloud:8000`), the whole host routed to the BFF.
- **Production feature set**: `ASPNETCORE_ENVIRONMENT` must not be `Development` (upstream's image sets it, production's
  deployment overrides it): in Development the Razor view loads the SPA from Vite's dev server on `localhost:5173` and
  `appsettings.Development.json` turns `MockSettings:AccessManagement` and every feature flag on and points at at22.
  The image runs `Production`, for which there is no `appsettings.Production.json`: `appsettings.json` alone, every
  `MockSettings` switch off and every `FeatureManagement` flag off.
- **Three changes to upstream's code, as build-time patches** (`local: patches`, as Arbeidsflate: `vendor/` stays
  upstream's tree, `vendor-sync.py check` applies the patches): the TypeScript one cannot be a directive, and the tool
  takes one mode per component. The C# hunks are written as `#if LOCALTEST` / `#if !LOCALTEST` blocks, so they read as the
  house's directive sites and `LOCALTEST` is defined by `localtest.build.props` as for every .NET component.
  1. `Program.cs`: the shared service-discovery registration and the shared cookie policy right after
     `WebApplication.CreateBuilder` (an additive block), and `RequireHttpsMetadata = false` for the JWT cookie's
     metadata (an additive block; upstream turns it off in Development only, and the stack's Authentication is http).
  2. `AccessTokenProvider.cs`: the platform access token's certificate is read from the stack's shared certificate
     (`/certs/localtest-accesstoken.pfx`) instead of Key Vault (a replacement block).
  3. `src/resources/utils/pathUtils.tsx`: on the stack the links the SPA builds (platform, inbox, start page, its own
     base for `changeandredirect`) follow the page's origin; upstream maps every host it does not know to production.
- **Fakes**: none of the component's own. Two copies of shared fakes, `fakes/ServiceDiscovery/LocaltestServiceDiscovery.cs`
  and `fakes/Cookies/LocaltestCookiePolicy.cs`, compiled into the host by `localtest.build.targets`.
- **Seed**: the GUI's end-user endpoints and Access Management's own end-user write API ask the PDP about Altinn's
  system resources (`altinn_access_management` read/write, `..._hovedadmin`, `altinn_client_administration`,
  `altinn_instance_delegation`, `altinn_maskinporten_scope_delegation`). The stack's Resource Registry does not have
  them, so the PDP answers NotApplicable: the GUI shows the user as no administrator and Access Management refuses every
  delegation. They go into the Resource Registry's seed (`fakes/Seed/<id>/`), copied from tt02 (fetched 2026-10-30), as
  the seed's other system resources were. Data, not code.
- **Links**: Arbeidsflate's patch 0002 grows two functions (its links to the GUI); Access Management gets two directive
  sites (its consent and request links hard-code `https://am.ui.{Hostname}`); studioctl and the Aspire package set the
  apps' `PlatformFrontendSettings__AccessManagementChangeAndRedirectUrl` and `__AccessManagementUrl` when the group runs.

## 1. Outbound calls of the BFF

Every typed client is registered in `Program.cs` (`ConfigureMockableClients`), each behind a `MockSettings` switch, all
off. Base addresses come from `PlatformSettings` (`backend/.../Integration/Configuration/PlatformSettings.cs`); every
client but the Resource Registry's sends the user's `AltinnStudioRuntime` token as `Authorization: Bearer`, several add
a platform access token (section 2) and an empty `Ocp-Apim-Subscription-Key` (`SubscriptionKey` unset: API
Management is not in front of anything here). With the shared service-discovery fake every factory client resolves a
service name from `services__<name>__http__0` and uses a real host (one with a dot) as it is.

| Client (calls) | Setting | Stack value | How it is satisfied |
| --- | --- | --- | --- |
| `AccessManagementClient`, `AccessManagementClientV0`, `AccessPackageClient`, `ConnectionClient`, `RoleClient`, `SingleRightClient`, `InstanceClient`, `RequestClient`, `ClientDelegationClientV1`/`V2`, `ConsentClient`, `MaskinportenClient`, `IdPortenAuthorizationClient` (`enduser/connections/...`, `enduser/request/...`, `authorizedparty/...`, `meta/info/...`, `bff/consent...`, `enduser/maskinporten...`, `bff/idportenauthorization`) | `ApiAccessManagementEndpoint` (versionless; clients append `v1/` or `v2/`) | `http://access-management/accessmanagement/api/` | configuration, by service name |
| `ProfileClient` (`users/{id}`, `users/byuuid/{uuid}`, `users/current/profilesettings`, `users/current/party-groups/favorites`, `organizations/{org}/notificationaddresses/mandatory`) | `ApiProfileEndpoint` | `http://profile/profile/api/v1/` | configuration; platform access token (`amui`) |
| `RegisterClient` (`parties/lookup`, `parties/partylistbyuuid`, `access-management/parties/{uuid}`, `persons`, `parties/nameslookup`) | `ApiRegisterEndpoint` | `http://register/register/api/v1/` | configuration; platform access token |
| `AuthenticationClient` (`refresh`), `SystemRegisterClient`, `SystemUserClient`, `SystemUserRequestClient`, `SystemUserChangeRequestClient`, `SystemUserAgentRequestClient`, `SystemUserAgentDelegationClient`, `SelfIdentifiedUserClient` | `ApiAuthenticationEndpoint` | `{{origin}}/authentication/api/v1/` | configuration; **the public origin**, because `HomeController` also hands this value to the browser (the login redirect, section 5), which a service name cannot serve |
| `AuthorizationApiClient` / `PDPAppSI` (`decision`, Altinn.Common.PEP, the `EndUserResourceAccessHandler` of six policies) | `PlatformSettings:ApiAuthorizationEndpoint` (PEP's own class, same section) | `http://authorization/authorization/api/v1/` | configuration; needs the seed of section 6 for a Permit |
| `ResourceRegistryClient` (`v1/resource/...`, `search`, `resourcelist`, cached) | `ApiResourceRegistryEndpoint` | `http://resource-registry/resourceregistry/api/` | configuration (a singleton built on the factory's default `HttpClient`, which service discovery reaches) |
| `DialogportClient` (`enduser/dialoglookup`, instance to dialog) | `ApiDialogportenEndpoint` | empty (upstream's `appsettings.json` value) | unchanged: the client returns `null` without a base, the GUI then links no dialog; Dialogporten is an optional group, so a fixed address would fail whenever it is off |
| `AltinnCdnClient` (`https://altinncdn.no/orgs/altinn-orgs.json`) | hard-coded | the internet | unchanged; the stack reads altinncdn.no elsewhere too (task 3 won't do) |
| `ConsentClient.GetConsentTemplates` (`https://raw.githubusercontent.com/Altinn/altinn-studio-docs/master/.../consent_templates.json`) | hard-coded | the internet | unchanged; only the consent pages use it. A new egress host for task 44's inventory |
| The JWT cookie handler's metadata (`AltinnCore.Authentication`, outside the factory) | `OpenIdWellKnownEndpoint` | `{{origin}}/authentication/api/v1/openid/` | configuration, public origin (the issuer's document names a public `jwks_uri`); over http only with patch 1's `RequireHttpsMetadata = false` |
| Key Vault: `SecretClient` for the Application Insights key and `AddAzureKeyVault` at start (`Program.cs`) | `KeyVaultSettings:SecretUri` | empty (`appsettings.json`) | unchanged: `new Uri("")` throws and both are caught and logged at start (two error lines), no network call; no Application Insights |
| Key Vault: `KeyVaultService.GetCertificateAsync` (the access-token certificate) | `KeyVaultSettings:SecretUri`, `ClientSettings:CertificateName` | not reached | patch 2 (section 2) |
| Azure App Configuration (`AddAltinnAppConfiguration`) | `Altinn:AppConfiguration:Endpoint`, `Label` | unset | unchanged: skipped with an information line; production sets them only for environments with a label |
| Data protection keys | `ALTINN_KEYS_DIRECTORY` | unset | unchanged: the default directory in the container; a restart invalidates antiforgery tokens, a reload fixes it |

`MockSettings` (`appsettings.json`): all twenty-one listed switches `false`, and the two missing from the file
(`Dialogporten`, `Maskinporten`) default to `false` in `MockSettings`. `LocalKeyVaultService` (the `KeyVault` switch) would
not help either: it returns a PEM certificate without a private key, which `LoadPkcs12` cannot read.

`GeneralSettings`: `FrontendBaseUrl` `{{origin:am.ui}}` (the login redirect's `goto`, the fallback of `changeandredirect`),
`Hostname` `{{host}}` (the domain of the party cookies and of the refreshed runtime cookie, the domain Authentication
uses), `AllowedRedirectDomains` keeps `altinn.no` and `altinn.cloud` and gets `{{host}}` as a third entry
(`GeneralSettings__AllowedRedirectDomains__2`), so `changeandredirect`'s `goTo` is accepted on a custom origin too.

## 2. The `amui` platform access token

`AccessTokenProvider.GetAccessToken` loads the PKCS#12 certificate `ClientSettings:CertificateName` (`JWTCertificate`)
from Key Vault and signs a platform access token with `Altinn.Common.AccessTokenClient`'s `AccessTokenGenerator`:
issuer `amui`, app `access-management-ui`, cached for the token's lifetime. The Profile, Register and Authentication
(system register) clients send it in `PlatformAccessToken`.

How the stack accepts it: every component that validates platform access tokens has a `IPublicSigningKeyProvider` fake
that returns the shared test certificate for **every issuer** (`localtest/certs/README.md`; Profile, Register,
Authentication, Access Management, the PDP, the Resource Registry, Storage, Events, Notifications). So a token signed
with the shared key is accepted under `amui` as under any other issuer; nothing changes in those components. The
certificate is mounted at `/certs/localtest-accesstoken.pfx` in every container (CONVENTIONS, "The certificate").

What the BFF needs: patch 2, a replacement block in `AccessTokenProvider` (not in `KeyVaultService`, which has no
setting at hand and whose name would then lie): `#if !LOCALTEST` the Key Vault call `#else` the file's bytes as base64
from `AccessTokenSettings:AccessTokenSigningCertificateFullPath`, the manifest's usual variable for it
(`/certs/localtest-accesstoken.pfx`). Upstream never binds `AccessTokenSettings` (the provider reads the defaults:
lifetime 300 s, no path), so patch 1's block binds the section. The rest (issuer, app, lifetime, the generator) is
production's.

## 3. Routes and the host

| Path on `am.ui.<host>` | Served by | Note |
| --- | --- | --- |
| `/accessmanagement/`, `/accessmanagement/ui`, `/accessmanagement/ui/{*}` | `HomeController.Index` | The shell: signed in, the Razor view with the feature flags and the bundle; otherwise a redirect to Authentication (section 5) |
| `/accessmanagement/api/v1/...` | the BFF's API controllers | Antiforgery on unsafe methods when the runtime cookie is present (`AS-XSRF-TOKEN` cookie plus `X-XSRF-TOKEN` header, from the `XSRF-TOKEN` cookie the shell sets) |
| `/accessmanagement/assets/...` | static files | `accessmanagement.js`, `.css` and hashed assets |
| `/health` | health checks | Always healthy (`HealthCheck` returns Healthy); the manifest's default health |
| `/` | nothing (404) | Production has no root either |

The front door routes the whole host to the service `access-management-ui`, like `af.<host>`. `am.ui` is two labels;
the manifest's subdomain check (Go `hostLabel`, Aspire `HostLabel`) takes one label today and must take dotted ones,
each label checked. Cookies set for `local.altinn.cloud` (Authentication's) reach `am.ui.local.altinn.cloud`.

The SPA keeps the paths absolute (`/accessmanagement/...`), so it cannot live under a prefix of the main host; the
production host name also keeps the Access Management API's links (`https://am.ui.{Hostname}/…`) and Arbeidsflate's
links in the same shape.

## 4. Hard-coded hosts in the SPA

`src/resources/utils/pathUtils.tsx` is Arbeidsflate's `url.ts` again: `getEnv()` reads `window.location.host` for
`tt02`, `at22`, `at23`, `yt01` and maps everything else to production. On `am.ui.local.altinn.cloud` that is production:

| Function | Production value | Used for | On the stack after patch 3 |
| --- | --- | --- | --- |
| `getAmBaseUrl`, `getAmStartPageUrl` | `https://am.ui.altinn.no/accessmanagement/` | `changeReporteeUtils` (switching party in the account menu: `.../api/v1/reportee/changeandredirect/`), the error page's start link, `getLoginUrl`'s `goto` | `<page origin>/accessmanagement/` |
| `getPlatformUrl`, `getLogoutUrl`, `getLoginUrl` | `https://platform.altinn.no/` | Logout in the menu; the login redirect on an expired token (`tokenUtils`); system user pages' logout | the public origin (`http://local.altinn.cloud:8000/`): the platform is on the main host |
| `getAfUrl` | `https://af.altinn.no/` | Inbox, profile and dialog links | `af.<host>` (`http://af.local.altinn.cloud:8000/`); with the `arbeidsflate` group off the front door answers its 503 hint |
| `getHostUrl`, `getAltinnStartPageUrl` | `https://altinn.no/`, `https://info.altinn.no/` | The start page and the info portal | `getHostUrl` the front door's Home; `getAltinnStartPageUrl` too (there is no info portal locally) |

The rule in the patch: the stack is plain http (`location.protocol === 'http:'`) on a host starting `am.ui.`; the base
host is the page's host without `am.ui.`, port included. That keeps every deployed environment (https) on upstream's
code and follows `--host` and `--port`. The help and news links to `info.altinn.no` and the Altinn 2 links in
`redirectUtils.ts` stay production's (static information pages; Altinn 2 does not exist locally).

Other hosts in the bundle: `altinncdn.no` (fonts, favicon, CSP), `cdn.jsdelivr.net` (CSP only), `ingest.skyra.no`
(Skyra, behind the off `EnableSkyra` flag). The CSP (`SecurityHeadersMiddleware`) allows `'self'` for scripts and
connections, so the same-origin API calls work over http; no `upgrade-insecure-requests`, no HSTS over http.

## 5. Login and cookies

- The BFF authenticates with `AltinnCore.Authentication`'s JWT cookie handler on `AltinnStudioRuntime`
  (`PlatformSettings:JwtCookieName`), validating against the stack's Authentication keys (metadata at the public origin).
  The cookie is set by Authentication for `Domain=local.altinn.cloud`, so a user signed in to an app or the inbox is
  signed in here.
- No cookie: `HomeController.Index` redirects to
  `{ApiAuthenticationEndpoint}authentication?goto={FrontendBaseUrl}{path}?openAccountMenu=true`. The stack's
  Authentication accepts the `goto` (its `IsSafeSameOrSubdomainHttps` site already allows http, and `am.ui.<host>` is a
  subdomain of its host name), sends the browser to the fake ID-porten picker and back to `goto` with the cookies.
- Cookies the BFF writes: `XSRF-TOKEN` (`Secure` outside Development, `SameSite=Strict`), `selectedLanguage`
  (`Secure` outside Development), the party cookies `AltinnPartyId` / `AltinnPartyUuid` (`Secure` outside Development,
  `Domain` = `Hostname`), the refreshed `AltinnStudioRuntime` (`Secure` always), the antiforgery cookie `AS-XSRF-TOKEN`
  (not `Secure`). A browser drops `Secure` cookies over http, and without `XSRF-TOKEN` the SPA cannot send the
  antiforgery header, so every POST would fail. All of them go through `Response.Cookies`, so the shared cookie policy
  (`LocaltestCookiePolicy`, already used by Authentication) removes `Secure` on http requests; no per-cookie site.

## 6. Authorization: the system resources' policies

`EndUserResourceAccessHandler` (six BFF policies, all "with pass-through") asks the PDP for `read` on
`altinn_access_management`, `altinn_client_administration`, `altinn-profil-api-varslingsdaresser-for-virksomheter`,
`altinn_access_management_hovedadmin`, `altinn_instance_delegation` and `altinn_maskinporten_scope_delegation` for the
party in the query; the answer becomes `isAdmin`, `isClientAdmin`, ... which the SPA uses to show the management views.
Access Management's own end-user API refuses writes without `write` on `altinn_access_management`
(`POLICY_ACCESS_MANAGEMENT_ENDUSER_WRITE`) and checks `altinn_access_management_hovedadmin` on requests.

The stack's Resource Registry seeds one of them (`altinn-profil-api-varslingsdaresser-for-virksomheter`, task 14). The
other five go into `localtest/components/resource-registry/fakes/Seed/` from tt02 (`resource/{id}` and
`resource/{id}/policy`, fetched 2026-10-30, `createdAt`, `updatedAt` and `versionId` dropped), unchanged otherwise.
`altinn_access_management`'s policy grants `read` and `write` to the Altinn 2 roles `ADMAI`, `HADM`, `BOBEL` and the access
packages `hovedadministrator`, `tilgangsstyrer`, `innbygger-tilgangsstyring-privatperson`, `konkursbo-lesetilgang`,
`ffor-tilgangsstyrer-nuf`, with authentication level 3 as an obligation. The stack's Access Management derives the
packages from the seed's CCR roles (`DAGL` gives `hovedadministrator`) and gives a person the citizen packages on their
own party, so a test user signed in at level 3 manages their own party and the organisations they lead. The Resource
Registry image needs a rebuild for the seed (it is embedded).

## 7. Links into and out of the GUI

- **Arbeidsflate** (`vendor/packages/frontend/src/auth/url.ts`): `getAccessAMUILink()` (the menu's access management
  link, the dialog's "who has access", the profile's settings and Altinn 2 links) and `createChangePartyAndRedirect()` map
  every unknown host to `am.ui.altinn.no`. Patch 0002 grows the same rule as its `getCookieDomain` hunk: on http on
  `af.<host>`, `http://am.ui.<host:port>/accessmanagement/...`. One patch file, two more hunks; the image rebuilds.
- **Access Management's API** builds `https://am.ui.{GeneralSettings:Hostname}/accessmanagement/ui/consent/request?id=…`
  (`ConsentService.GetConsentViewUri`) and the service-owner request links `https://am.ui.{Hostname}/…/requests/resource`
  and `https://platform.{Hostname}/accessmanagement/api/v1/serviceowner/delegationrequests/{id}/status`
  (`RequestController.BuildLinks`). They follow `Hostname` but hard-code https and assume the platform on `platform.`.
  Two replacement sites: `#else` the same paths on `http://am.ui.{Hostname}` and `http://{Hostname}` (the platform is the
  main host), with the fakes' `GeneralSettings:Hostname` default moved from the host to host and port
  (`new Uri(platform).Authority`, `local.altinn.cloud:8000`). `Hostname` is read nowhere else in the host.
- **Apps** (`PlatformFrontendSettings`, monorepo app-lib and frontend): `AccessManagementChangeAndRedirectUrl`, a
  template with `{partyId}` and `{goTo}` (the app frontend's `urlHelper.ts`, the shape of its tests:
  `http://am.<host>/accessmanagement/api/v1/reportee/changeandredirect?partyId={partyId}&goTo={goTo}`), and
  `AccessManagementUrl` (`.../accessmanagement/ui`, the "who can delegate" link). Both set by studioctl
  (`internal/cmd/app/env.go`) and the Aspire package (`AppEnvironment.cs`) when the `access-management` group runs, left
  unset otherwise (as today). The BFF's `changeandredirect` takes `partyId` (or `partyUuid`, `P`) and `goTo`, writes the
  party cookies for `Hostname` and redirects to `goTo` when its host is in `AllowedRedirectDomains`.

## 8. The component

| Path | What |
| --- | --- |
| `vendor/` | Upstream's whole repository at `76c599d` (1517 files, about 16 MB), pristine; `UPSTREAM.md` with `local: patches` |
| `patches/0001-bff-localtest-hooks.patch` | `Program.cs`: the discovery and cookie-policy hook, `RequireHttpsMetadata = false` |
| `patches/0002-bff-access-token-certificate.patch` | `AccessTokenProvider.cs`: the shared certificate |
| `patches/0003-spa-links-follow-origin.patch` | `pathUtils.tsx` |
| `fakes/ServiceDiscovery/`, `fakes/Cookies/` | Copies of `../shared/fakes/` |
| `localtest.build.props`, `.targets` | Define `LOCALTEST`, compile `fakes/` into `Altinn.AccessManagement.UI`, add `Microsoft.Extensions.ServiceDiscovery` |
| `Dockerfile` | Upstream's three stages with `localtest:` deviations: the `source` stage that applies the patches, the overrides in the backend stage, `NODE_EXTRA_CA_CERTS` for the install behind a proxy, `ASPNETCORE_ENVIRONMENT=Production`, port 8080 |
| `smoke-test.sh`, `README.md` | As the other components |

The manifest container `access-management-ui` (port 8080, health `/health`, both defaults), group
`access-management` (`--with access-management`, `.WithAccessManagement()`), `dependsOn`: `access-management`,
`authentication`, `authorization` (the GUI is useless before them; Access Management already waits for Postgres, the
Register, the Resource Registry, Profile and the front door). The public subdomain `am.ui`, the front door's host route
and 503 hint, studioctl's topology component and `env hosts add`, the Aspire package's hosts and group method; golden
files and tests in both runtimes.

## 9. Memory, build time, image size (estimates; measured values in the component README)

- Memory: one ASP.NET Core process without a database, comparable to Receipt (45 MB) and Authentication (69 MB) in the
  stack; under 150 MB expected. The 8 GiB machine had about 2.5 GB available with `--with everything` (TODO.md
  section 68), so it fits.
- Image: `aspnet:10.0-alpine` (131 MB) plus the BFF (about 40 MB with its Azure and Swagger packages) and the bundle
  (a few MB): about 200 MB.
- Build: the Vite build installs the whole Yarn workspace (Storybook, Vitest, Playwright's package, about 1 GB of
  `node_modules` in the build stage, discarded) and `dotnet publish` restores the BFF. Several minutes on this machine;
  the build stages need about 2 to 3 GB of disk while they exist.

## 10. Not done here, and why

- Telemetry: the BFF has Application Insights only (off without the Key Vault key) and no OpenTelemetry; the stack's
  OTLP rule (task 53) would need instrumentation packages and a site. Left out; a known gap.
- The Dialogporten lookup (instance delegation pages' dialog links): off with the empty endpoint, as upstream's base
  configuration.
- The system user, self-identified user and consent pages call Authentication and Access Management endpoints the
  stack runs; nothing was done for their data (no seeded systems, consents or requests).
