# Production Authentication vendored for localtest: analysis

## Status (2026-10-21)

- **Implemented** (2026-10-17, `b642ef3`, `0e5dbb3` and following) in
  [`../../localtest/components/authentication/`](../../localtest/components/authentication/README.md) with the two
  directive sites recommended here (the host hook and the `goto` guard over http). Re-pinned to altinn-auth `3d51810`
  on 2026-10-20. Smoke test 30 ok on 2026-10-21 (32 with the `dialogporten` group).
- Since then: the fake upstream providers moved out of Authentication into the localtest app
  ([`localtest-app-identity.md`](localtest-app-identity.md), 2026-10-20), and task 2 (2026-10-20, `4b9b5f7..ddd0ecf`)
  made Authentication an OIDC provider for registered clients (four in-memory repositories, a base64 pepper, two
  seeded clients, logout through the fake ID-porten's `endsession`); its code-flow access tokens carry `pid`.

Superseded statements:

- The in-place override of the upstream-provider step inside Authentication (sections 2 to 5): replaced by the fake
  ID-porten in the localtest app, which production Authentication signs in against as a normal OIDC provider.
- Postgres 16 for the scratch run: the component keeps its state in memory, and the shared server is Postgres 18.

Status: research, 2026-10-17. Source: `Altinn/altinn-auth` at `392f9578` (shallow clone in
`/home/agent/code/.reference/altinn-auth`). Paths below are relative to
`src/apps/Altinn.Authentication/src/` unless they start with another root. Line numbers refer to that commit.
Builds on [`../../localtest-components/authn-profile-register.md`](../../localtest-components/authn-profile-register.md) §1 and §4,
[`../mvp-vendoring-model.md`](../mvp-vendoring-model.md) and [`../register/`](../register/).

Scope change during the work: the coordinator asked to replace the "fake OIDC provider" idea with an **in-place
override of the upstream-provider step inside vendored Authentication**. Question 4 (Mockporten / own OIDC server) is
therefore only a short note (§6). The new questions are answered in §2 (a), §3–§4 (b), §5 (c), §8 (d) and §3, §4, §7, §10 (e).

## Executive summary

- **Vendoring production Authentication is feasible and verified.** The host (`Altinn.Platform.Authentication.csproj`)
  builds standalone from the clone in 11 s (306 warnings, 0 errors). A copy under `/tmp/authbuild` with one
  `#if LOCALTEST` hook started against a local Postgres 16, ran all Yuniql migrations (2.4 s), and completed a full
  browser sign-in, `refresh`, and `exchange/maskinporten` for a service owner and a system user (§9).
- **The seam is clean and mostly behind interfaces.** Browser sign-in is a state machine in `OidcServerService`:
  `AuthorizeUnregisteredClient` (`Services/OidcServerService.cs:210`) persists a transaction and redirects to
  `OidcProvider.AuthorizationEndpoint` (`:1536`). `HandleUpstreamCallback` (`:251`) turns `code` into a
  `UserAuthenticationModel` via two interfaces, `IOidcProvider.GetTokens` (`:795`) and
  `IUpstreamTokenValidator.ValidateTokenAsync` (`:804`). Production `AuthenticationHelper.GetUserFromToken` (`:816`) then
  maps the claims, and the rest is shared downstream code: Profile lookup, session, cookie JWT, and cookies (`:298-424`).
- **Recommended override: 2 directive sites plus DI fakes.**
  1. The host hook in `AuthenticationHost.cs:293`.
  2. The https-only `goto` guard in `Controllers/AuthenticationController.cs:158`.

  The picker is a fakes-project controller. The `idporten` provider's `AuthorizationEndpoint` is configured to point
  at it. Fake `IOidcProvider` and `IUpstreamTokenValidator` hand the picked identity to production
  `GetUserFromToken`, so everything after it is production code.

  One optional third directive, `AuthenticationController.cs:246`, renders the picker directly at
  `authentication/api/v1/authentication?goto=` instead of redirecting to it.
- **Dependencies of the browser flow:**
  - Profile `POST internal/user` (`Services/ProfileService.cs:44`). This is not Register: the browser flow does not
    call Register at all.
  - 3 Postgres repositories: unregistered request, upstream transaction, session.
  - The signing pfx, which already falls back locally to the same cert as localtest.

  Postgres can be the shared compose container (zero code) or in-memory repositories behind `TryAddTransient`
  interfaces. Without a database the host still starts; `/health` returns 503 and sign-in returns 500 until the
  repositories are replaced.
- **Things that do not work out of the box over plain http on `local.altinn.cloud:8000`:**
  - The `goto` must be https (directive 2).
  - Every cookie is hard-coded `Secure` (`OidcServerService.cs:341-371`). Browsers drop these on a non-localhost http
    origin. Fix with nginx `proxy_cookie_flags ~ nosecure`.
  - Upstream and Maskinporten JWKS fetches require https (`HttpDocumentRetriever.RequireHttps` defaults to `true`).
    Fix with a DI fake for `ISigningKeysRetriever`.
  - `kvSetting:SecretUri` cannot be set. It makes `AltinnHost` add Key Vault and crash, yet `PublicSigningKeyProvider`
    throws without it. Fix with a DI fake.
  - Authentication must reach its own `jwks_uri`, which is built from `PlatformEndpoint`. Fix with a compose network
    alias on the ingress.
  - The app treats tokens without `actual_iss=localtest` as "old localtest" tokens. Fix with config
    `OidcProviders:idporten:ProviderClaims=[actual_iss]`, verified.
- **Non-browser tokens:** a fakes-project `LocaltestTokenController` (not a directive) serves the existing
  `/Home/GetTestOrgToken` and `/Home/auth/*` contracts.
  - **User tokens:** it runs the production sign-in headlessly (`AuthorizeUnregisteredClient` + `HandleUpstreamCallback`
    on the public `IOidcServerService`). The tokens are real sessions, so `refresh` works.
  - **Org, service-owner and system-user tokens:** it signs a Maskinporten-shaped token with a local key and passes it
    through the real `exchange/maskinporten`.
- **Verdict:** recommend **(a) vendored Authentication with an in-place picker override**.
  - **What it buys:** byte-exact claims (`sub=urn:altinn:party:uuid:…`, `sid`, `jti`, `originaliss`, `acr`, `amr`,
    `scope`, `nameid` on refresh), the `AltinnPartyUuid` and `altinnsession` cookies, real refresh, sliding sessions,
    logout, step-up via `acr_values`, and exchange semantics, including the system-user `authorization_details`
    reshaping.
  - **What it costs:** 2 directives, about 700–900 lines of fakes, one Postgres database (or about 500 more lines for
    in-memory repositories), a Profile `internal/user` endpoint, and ingress tweaks.
  - **Fully fake (b)** is about the same amount of code, but its fidelity has to be re-derived and kept in sync by hand.

## 1. Architecture (question 1)

From [`docs/architecture.md`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/docs/architecture.md) and the code. All routes are under `authentication/api/v1`.

| Flow | Entry | Code |
|---|---|---|
| Browser sign-in, unregistered client (Altinn Apps) | `GET authentication?goto=&acr_values=&iss=` | `Controllers/AuthenticationController.cs:140-251` → `OidcServerService.AuthorizeUnregisteredClient` `:210` |
| Upstream callback | `GET upstream/callback?code&state` | `Controllers/OidcFrontChannelController.cs:87-139` → `HandleUpstreamCallback` `:251` |
| OIDC server for registered clients (Arbeidsflate) | `GET authorize`, `POST token` | `OidcFrontChannelController.cs:33`, `OidcTokenController.cs:27` |
| Refresh | `GET refresh` (`[Authorize]`, cookie or bearer) | `AuthenticationController.cs:258-321`. It re-signs the principal, and `HandleSessionRefresh` (`OidcServerService.cs:436`) slides the session when `sid` is present and the scope contains `altinn:portal/enduser`. It **throws** when that session is missing (`:460`). |
| Exchange | `GET exchange/{id-porten\|maskinporten\|altinnstudio}` | `AuthenticationController.cs:326-372`, `:430` (Maskinporten), `:548` (ID-porten), `:374` (Studio) |
| Logout | `GET logout`, `openid/logout`, front-channel | `LogoutController.cs:55`, `OidcFrontChannelController.cs:141,177` |
| System users, system register, requests, client delegation | `systemuser/*`, `systemregister/*` | separate controllers, Postgres `business_application` schema plus AccessManagement |
| Discovery and JWKS | `openid/.well-known/openid-configuration[/jwks]` | `OpenIdController.cs`. Issuer and `jwks_uri` are built from `GeneralSettings.PlatformEndpoint` (`GeneralSettings.cs:118-124`). |

**Upstream providers** come from the `OidcProviders:{key}` dictionary
(`Extensions/ServiceCollectionExtension.cs:17-34`, `Model/OidcProvider.cs`).

- Fields: `Issuer`, `AuthorizationEndpoint`, `TokenEndpoint`, `WellKnownConfigEndpoint` (JWKS source), `LogoutEndpoint`,
  `ClientId`/`ClientSecret` (or a private_key_jwt), `Scope` (default `openid`), `ProviderClaims` (claims copied into the
  session and the Altinn token), `ExternalIdentityClaim`, `ClaimMappings` (defaults `pid`/`acr`/`amr`/`email`,
  `Model/OidcClaimMappings.cs`), `AuthLevels`, `AuthMethodMappings`, `RequireSyntheticPid`, and PAR/DPoP/strict
  options.
- Any OIDC provider can be configured. The base `appsettings.json:49` ships it empty.
- **Provider choice** (`OidcServerService.cs:1413-1449`): the `iss` query parameter (matched to key or issuer), then
  the provider that owns the requested `acr_values` (`OidcAcrValueCatalog`), then the **hard-coded** key `idporten`
  (`:89`). `GeneralSettings.DefaultOidcProvider` is read nowhere.
- **Claims the upstream id_token must carry** (`Helpers/AuthenticationHelper.cs:62-300`):
  - `nonce`, which must equal the transaction nonce.
  - `iss`, which must equal the configured `Issuer` up to a trailing slash.
  - A signature from a JWKS key.
  - `exp`.
  - `pid` (or `urn:altinn:userid`, an `ExternalIdentityClaim`, or `email` with `acr=selfregistered-email`).
  - `acr`, mapped through the ID-porten defaults (`Model/OidcAuthLevelDefaults.cs:24-32`): `level0`/`selfregistered-email`
    → 0, `level1` → 1, `level2` → 2, `idporten-loa-substantial`/`level3` → 3, `idporten-loa-high`/`level4` → 4.
  - Optionally `amr`, `sid`, `auth_time` and `jti`.
- The access token is validated the same way unless `TreatAccessTokenAsOpaque` is set; its `scope` claim becomes the
  session scope. `DefaultPortalScopes` is always added (`OidcServerService.cs:754`, `GeneralSettings.cs:168`).

## 2. The seam and the override (question a)

```
GET authentication?goto=          AuthenticationController.cs:141
  goto guard (https only)         :158  -> IsSafeSameOrSubdomainHttps :1050
  reuse JWT cookie / session      :170-221   (production; no upstream round trip)
  AuthorizeUnregisteredClient     :242 -> OidcServerService.cs:210
      ChooseProvider              :212 (-> "idporten")
      insert unregistered request :224 (Postgres)
      insert upstream tx (state, nonce, PKCE)  :226 -> :1203 (Postgres)
      ResolveUpstreamAuthorizeUrl :228 -> :1532; :1536 UriBuilder(provider.AuthorizationEndpoint)
  Redirect(UpstreamAuthorizeUrl)  AuthenticationController.cs:246
--- browser at upstream IdP ---
GET upstream/callback             OidcFrontChannelController.cs:87 -> HandleUpstreamCallback :115
  validate state, load upstream tx            OidcServerService.cs:258 (Postgres)
  SetCallbackSuccessAsync                     :274
  ExtractUserIdentityFromUpstream             :291 -> :793
      IOidcProvider.GetTokens(code,…)         :795   (interface, DI: AuthenticationHost.cs:197)
      IUpstreamTokenValidator.ValidateTokenAsync :804 (interface, DI: :217)
      AuthenticationHelper.GetUserFromToken   :816   -> UserAuthenticationModel   <== seam type
  IdentifyOrCreateAltinnUser                  :298 -> :1679 (IProfile.GetUserProfile = Profile POST internal/user)
  AddLocalScopes                              :319
  CreateOrUpdateOidcSession                   :322 -> :1311 (Postgres oidc_session)
  ITokenService.CreateCookieToken             :324 -> ClaimsPrincipalBuilder + TokenIssuerService (pfx)
  cookies AltinnPartyId/Uuid, AltinnStudioRuntime, altinnsession  :336-380
  MarkUpstreamTokenExchanged                  :424
  -> RedirectToGoTo
```

**Internal type between the two halves:** `Model/UserAuthenticationModel.cs`. It carries SSN, UserID, PartyID,
PartyUuid, Acr, AuthenticationLevel, Amr, Sid, Scope, ProviderClaims, and TokenIssuer/TokenSubject.
`IdentifyOrCreateAltinnUser` fills in the Altinn ids from Profile:

- By SSN, or by UserID for self-identified users.
- Its early return when UserID, PartyID and PartyUuid are all set (`:1683`) is unreachable from tokens, because
  `GetUserFromToken` never reads a party uuid claim.

**Recommended override (production code runs from `GetUserFromToken` on):**

| # | Site | Kind | What |
|---|---|---|---|
| 1 | `AuthenticationHost.cs:293` (after `builder.AddPersistanceLayer()`) | `#if LOCALTEST` hook | `LocaltestOverrides.Apply(builder)`: `Replace` DI services (table §3) and `AddApplicationPart(fakes assembly)` for the picker and token controllers |
| 2 | `AuthenticationController.cs:158` (or inside `IsSafeSameOrSubdomainHttps`, `:1050-1059`) | `#if !LOCALTEST … #else` | Accept `http` as well as `https` for the configured host. Avoidable only if localtest serves https. |
| 3 (optional) | `AuthenticationController.cs:244-246` | `#else` | Render the picker view at this URL with `state`/`acr_values` from `result.UpstreamAuthorizeUrl`, instead of `Redirect`. Saves one 302. |
| — | config, no directive | `OidcProviders:idporten:AuthorizationEndpoint = http://local.altinn.cloud:8000/authentication/api/v1/localtest/login` | The production redirect lands on the in-host picker |

Picker POST (fakes controller):

1. Store the chosen identity (pid or userId, acr, optional preselected party) in an in-memory ticket store under a
   random `code`.
2. Do one of the following:
   - redirect to `upstream/callback?code&state` (zero extra directives), or
   - call `IOidcServerService.HandleUpstreamCallback` in-process and write the returned cookies (about 15 lines,
     mirroring `OidcFrontChannelController.SetCookies`), which saves the callback hop.

The fake `IOidcProvider.GetTokens` turns the ticket into an `OidcCodeResponse` whose id_token holds `pid`, `acr`, `amr`,
`sid`, `auth_time`, `actual_iss=localtest`, `iss=<configured Issuer>`. The fake `IUpstreamTokenValidator` parses it
without network or signature checks. It delegates to the real validator for any other provider.

Click count is the same as today: one picker submit. With site 3 and the in-process callback the redirect count also
matches today: one 302 to `goto`.

An alternative to the two DI fakes is one directive at `OidcServerService.cs:291` that builds
`UserAuthenticationModel` from the ticket. This is more visible, but it bypasses `GetUserFromToken` (acr→level and amr
mapping). DI is preferred.

## 3. Infrastructure dependencies (question 2)

| Dependency | Where | Need | Localtest |
|---|---|---|---|
| Postgres | `Persistance/Extensions/PersistanceDependencyInjection.cs:20-41`. Yuniql migrations run at startup when `PostgreSQLSettings:EnableDBConnection=true` (`AuthenticationHost.cs:308-333`). Tables: `oidcserver.{unregistered_client_request, login_transaction_upstream, oidc_session, login_transaction, authorization_code, refresh_token(_family), client, selfidentified_user_credential}`; `business_application.{system_register, system_user_profile, request, change_request, …}`. | Browser flow: unregistered request, upstream tx, session (refresh and logout use the session). Exchange: none. | **Container (verified):** shared `postgres` with init SQL `CREATE ROLE auth_authentication_admin LOGIN CREATEROLE; CREATE ROLE auth_authentication LOGIN; CREATE DATABASE authentication OWNER auth_authentication_admin;`. **Or mock:** all repositories are interfaces registered with `TryAddTransient`; in-memory versions of `IUnregisteredClientRepository` (4 methods), `IUpstreamLoginTransactionRepository` (7) and `IOidcSessionRepository` (8) are enough for the browser flow, about 400–500 lines. Without a DB the host starts (verified); `/health` returns 503 from the Npgsql health check (remove it in the hook). |
| Key Vault, JWT signing | `Services/JwtSigningCertificateProvider.cs:62-65` | Falls back to `CertificateSettings:CertificatePath` (`jwtselfsignedcert.pfx`, `qwer1234`) when `kvSetting:ClientId`/`ClientSecret` are empty | **Off.** The same cert as localtest (earlier report). Rollover delay falls back to any valid cert (`TokenIssuerService.cs:71-92`). |
| Key Vault, config source | `AltinnHost` (ServiceDefaults 6.0.0) adds Key Vault when `kvSetting:SecretUri` is set. With it set, startup crashed in the test (§9). | — | **Off:** leave `kvSetting:SecretUri` empty |
| `PublicSigningKeyProvider` (designer and platform access-token keys) | `AuthenticationHost.cs:193`; its constructor throws `UriFormatException` on an empty `kvSetting:SecretUri` (verified). Injected into `AuthenticationController`, so **every** endpoint of that controller returns 500. | Studio exchange, access-token validation | **Mock (required):** `Replace` with keys from the localtest cert, or an empty list |
| Upstream and Maskinporten metadata | `Helpers/ConfigurationMangerHelper.cs:25-28` uses `new HttpDocumentRetriever()`; `RequireHttps` defaults to `true` (decompiled `Microsoft.IdentityModel.Protocols.HttpDocumentRetriever`, IDX20108). Lazy; nothing is fetched at startup. | Upstream callback (unless faked), exchange | **Mock:** `Replace` `ISigningKeysRetriever` with a retriever that sets `RequireHttps=false` (verified). With the in-host picker the upstream fetch disappears entirely. |
| Data protection | `Extensions/DataProtectionConfiguration.cs`: file system in Development, Azure Blob otherwise | — | **Off:** run with `ASPNETCORE_ENVIRONMENT=Development`. Required anyway, because any other environment needs blob keys and sets `RequireHttpsMetadata=true` (`AuthenticationHost.cs:128-131`). Development also loads `appsettings.Development.json` (at22 endpoints, `AuditLog: true`, an `altinn` provider), so override with env vars. |
| Audit queue (Azure Storage) | `EventsQueueClient`/`EventLogService`, gated by `FeatureManagement:AuditLog` | — | **Off:** `FeatureManagement__AuditLog=false`, optionally a no-op `IEventsQueueClient` |
| App Insights, OTel | ServiceDefaults | — | Off (no connection string) |
| Redis/Valkey, App Configuration, Service Bus | none in this service (grep) | — | — |
| SBL bridge | Removed (ADR-0004). Only legacy-cookie *deletion* remains. | Not needed to log in | — |
| `altinn-orgs.json` | `Services/OrganisationsService.cs:36,60`, plain `HttpClient` (http allowed) | Maskinporten exchange with `altinn:serviceowner` scope | **Config:** `GeneralSettings:OrganisationRepositoryLocation` → a local file (CDN list merged with testdata orgs) |
| Platform access token (outbound) | `AccessTokenGenerator` (AccessTokenClient 3.2.12) returns `null` on missing cert (decompiled, catch → null) | Register and Notifications calls | **Config:** point `AccessTokenSettings` at a localtest cert, so Register's presence check passes |

## 4. Platform dependencies (question 3)

| Service | Calls | Used by | Works locally? |
|---|---|---|---|
| **Profile** | `POST {ApiProfileEndpoint}internal/user` with `{ssn}` or `{userId}`, no auth header (`Services/ProfileService.cs:40-62`). Uses `userId`, `userUuid`, `partyId`, `party.partyUuid`, `party.ssn`, `party.person.dateOfDeath`, `profileSettingPreference.preSelectedPartyId/preselectedPartyUuid` (`OidcServerService.cs:1690-1810`). A 404 means sign-in fails with 500. Nothing is created here for persons; ids are only looked up. | **Every browser sign-in** | Needs this endpoint in the thin Profile fake (today's report lists only `users/*`). Alternatively an in-process `IProfile` fake over testData or Register: `IProfile` is DI (`AuthenticationHost.cs:161`). |
| Register v2 internal | `POST internal/parties/query?fields=uuid,id,user,person.date-of-death` (`Integration/Clients/PartiesClient.cs` ~`:184-219`) with `PlatformAccessToken` | `exchange/id-porten` | Yes: the vendored Register keeps `V2/PartyController.Query` (register analysis §5, row `:413`) and accepts a present `PlatformAccessToken` (§6) |
| Register v2 internal | `POST internal/parties/self-identified` (`Integration/Clients/RegisterUserProvisioningClient.cs:23`) | Sign-in of SI/educational/email users without pid | **No:** excluded in the Register plan. Avoid it by sending `urn:altinn:userid` for localtest SI users, which takes the Profile-by-UserId branch (`OidcServerService.cs:1786`). |
| Register v1 | `parties/{id}`, `parties/byuuid/{uuid}`, `parties/lookup`, `organizations/{orgNo}` (`PartiesClient.cs:72-164`) | `refresh?enrichPid=true` (`AuthenticationController.cs:270-283`), system users | Works with vendored Register v1 for `byuuid`. Not on the MVP path. |
| Authorization PDP, AccessManagement, ResourceRegistry, Notifications | `PDPAppSI`, `AccessManagementClient`, `ResourceRegistryClient`, `AltinnNotificationClient` | System users, client delegation, SI link e-mail | Not needed for login, refresh or exchange. Leave the URLs pointing at local components; calls fail only on those endpoints. |
| Events | none | — | — |

Conclusion: browser login needs Profile `internal/user` only. Exchange needs nothing beyond the orgs file
(Maskinporten) or Register v2 `query` (ID-porten). Both are available from the planned local components.

## 5. Login page placement (question c)

- The app sends unauthenticated users to `{ApiAuthenticationEndpoint}authentication?goto=<url>`
  ([`App/backend/src/Altinn.App.Api/Controllers/HomeController.cs:139`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Controllers/HomeController.cs#L139)).
  studioctl sets that to `{platform}/authentication/api/v1/`.
- **Yes, the picker can live at that exact URL.** Use directive 3 at `AuthenticationController.cs:244-246`: render the
  view only when production would redirect upstream. Session reuse (`:170-221`) and step-up stay production.
  - Without directive 3 the same URL issues a 302 to `/authentication/api/v1/localtest/login` (config only).
- `acr_values` from the app (step-up) reaches the picker in the upstream query (`OidcServerService.cs:1648-1662`), so
  the picker can preselect the level, as today's localtest does from the XACML obligation.
- **Switching user:** production reuses a live session (`:170-221`), so a second visit skips the picker. The picker
  needs a "log in as someone else" link to `logout` (`LogoutController.cs:55`). Without `LogoutEndpoint` on the
  provider, logout redirects to `BaseUrl` (`:78`).
- **Party selection:** production sets `AltinnPartyId`/`AltinnPartyUuid` to the user's own party, or Profile's
  preselected party (`OidcServerService.cs:329-334`). Two options:
  - Pick the party in the app's own party selection page (production behaviour).
  - Have the picker's in-process callback overwrite the two cookies.
- A landing page at `/` (app list with links to `authentication?goto=`) can stay in the fakes project.

## 6. Mockporten and own OIDC server (question 4, short)

- **Mockporten** (`src/apps/Altinn.Mockporten`) is a real OIDC authorization-code IdP for test environments.
  - Endpoints: `GET/POST /Authorize` (password + pid form), `POST /token`, `POST /par`, discovery and JWKS.
  - It requires a shared password and a Tenor-only pid (month 81–92). Localtest users such as `01017512345` are
    rejected.
  - Signing is Key Vault only (`Services/Implementation/JwtSigningCertificateProvider.cs:35-36`, no file fallback).
  - It uses `UseHttpsRedirection`, and its id_token doubles as access token.
  - It has no picker. Unfit without several directives.
- **Own OIDC server:** it would have to serve discovery, `/authorize`, `/token` and `/jwks` (the jwks over https, or
  with the `ISigningKeysRetriever` fake). The id_token claims are listed in §1. This works (proved with a 90-line
  Python stub in §9), but it is superseded by the in-host override, which removes the extra hop, the extra service and
  the keys.

## 7. Cookie, token and http compatibility (question 5)

- **Cookies** (`OidcServerService.cs:336-371`, `GeneralSettings`):

  | Cookie | HttpOnly | Notes |
  |---|---|---|
  | `AltinnStudioRuntime` | yes | |
  | `altinnsession` | yes | |
  | `AltinnPartyId` | no | |
  | `AltinnPartyUuid` | no | |

  All four are `Secure; SameSite=Lax; Domain=GeneralSettings.HostName; Path=/`.
  - `Secure` is hard-coded at all sites. On `http://local.altinn.cloud:8000`, browsers refuse Secure cookies from
    non-localhost http origins, so add nginx `proxy_cookie_flags ~ nosecure;` (nginx ≥ 1.19.3) on the
    `/authentication/` location. No directive is needed.
  - Set `HostName=local.altinn.cloud`.
- **Token** (verified, §9): RS256, `kid` = cert thumbprint, `iss = {PlatformEndpoint}authentication/api/v1/openid/`,
  no `aud`, `exp` = `JwtValidityMinutes` (set 960 to match localtest).
  - Claims: `sub=urn:altinn:party:uuid:…`, `sid`, `jti`, `urn:altinn:party:uuid`, `urn:altinn:partyid`,
    `urn:altinn:userid`, `originaliss`, `acr`, `urn:altinn:authlevel`, `amr`, `urn:altinn:authenticatemethod`,
    `auth_time`, `scope`, plus `ProviderClaims`.
  - Exchange tokens additionally keep the upstream claims.
- **App validation** (`ServiceCollectionExtensions.cs:558-577` in App backend): JwtCookie with
  `ValidateIssuer=false`, `ValidateAudience=false`, and metadata from `AppSettings:OpenIdWellKnownEndpoint`.
  `RequireHttpsMetadata=false` only in Development. Compatible.
  - The app classifies tokens as "old localtest" when `HostName` is `local.altinn.cloud` and `actual_iss` is not
    `localtest` (`Altinn.App.Core/Features/Auth/AuthenticationContext.cs:81-115`). Production tokens lack it. Add it
    with `OidcProviders:idporten:ProviderClaims:0=actual_iss` and the fake id_token claim (verified in the cookie
    token).
- **Self-reachability:**
  - Authentication validates its own cookie on `refresh` through `GeneralSettings:OpenIdWellKnownEndpoint`, then
    follows `jwks_uri` built from `PlatformEndpoint` (`http://local.altinn.cloud:8000/…`).
  - In compose this resolves to 127.0.0.1 inside the container. Give the ingress the network alias
    `local.altinn.cloud` and listen on 8000 inside the network as well.
  - Verified: refresh returned 500 (IDX20803) until port 8000 was reachable.
- **http elsewhere:**
  - `goto` must be https: directive 2. Verified: an http goto gets a 302 to `BaseUrl`.
  - Upstream and Maskinporten metadata need https: the `ISigningKeysRetriever` fake.
  - No `UseHttpsRedirection` or HSTS in the host (`Program.cs`).
  - The `refresh` "test cookie" branch (`AuthenticationController.cs:299`) only triggers on host `localhost`.

## 8. Non-browser tokens (question d)

There is no mint endpoint in production. A fakes-project `LocaltestTokenController` (application part added in hook 1,
so no extra directive) keeps today's routes. The ingress routes `/Home/GetTest*` and `/Home/auth/*` to Authentication.

| Need | Route (today's contract) | Implementation on production code |
|---|---|---|
| App backend `AltinnToken` in localtest mode | `GET /Home/GetTestOrgToken?org&orgNumber=991825827&authenticationLevel&scopes` ([`AuthenticationTokenResolver.cs:96-97`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs#L96-L97), v8 `:78`) | Sign a Maskinporten-shaped token (`iss`=local Maskinporten issuer, `consumer` 0192:orgnr, `scope`, `actual_iss`) with a fakes key, published at a fakes-served `.well-known/oauth-authorization-server`. `GeneralSettings:MaskinportenWellKnownConfigEndpoint` points at it. Then call `exchange/maskinporten` in-process via loopback HTTP. The org claim comes from `altinn-orgs.json` (`AuthenticationController.cs:470-480`); `test=true` maps digdir to ttd. The orgs file must map each app org to its number, or the controller resolves `org`→orgnr first. |
| Org and service-owner tokens, integration tests | `/Home/auth/org`, `/Home/auth/serviceowner` | Same path |
| System-user tokens | `/Home/auth/systemuser` | Same path with `authorization_details` (verified). Production turns a one-element array into an object; that is the real shape. |
| User tokens (portal) | `/Home/GetTestUserToken`, `/Home/auth/user` | Headless production sign-in: `IOidcServerService.AuthorizeUnregisteredClient` (take `state` from the returned URL), put the identity in the ticket store, then `HandleUpstreamCallback` and return the `AltinnStudioRuntime` value. This creates a real session, so `keepAlive` → `refresh` works. A user token minted without a session but with `sid` and portal scope makes `refresh` throw (`OidcServerService.cs:456-461`). |
| Self-identified users | `/Home/auth/selfidentifieduser` | Same, with `urn:altinn:userid` instead of `pid` (Profile-by-UserId branch) |
| ID-porten end-user-system tokens (optional) | new | Fake ID-porten token through the real `exchange/id-porten`, which resolves the user in vendored Register v2 `query` |

## 9. Verification run (2026-10-17, `/tmp/authbuild`, clone untouched)

1. **Standalone build:** `dotnet build …/Altinn.Platform.Authentication.csproj` from an rsync copy: success, 0 errors,
   306 warnings, 11 s.
2. **LOCALTEST build:** hook 1 plus a `Localtest/LocaltestOverrides.cs` replacing `IPublicSigningKeyProvider` and
   `ISigningKeysRetriever`, built through `DirectoryBuildPropsPath` (imports the vendored props, adds `LOCALTEST`).
   - It failed first on StyleCop **SA1600 as an error** even with `TreatWarningsAsErrors=false`, because the ruleset
     sets the severity. `NoWarn SA1600` in the localtest props fixed it.
   - This answers the model's open question on analyzers: code compiled inside the vendored project is subject to
     `Altinn3.ruleset`.
3. **Run:** `ASPNETCORE_ENVIRONMENT=Development`, Postgres 16 container with two roles and the `authentication` database.
   - Migrations v0.00–v0.29 applied in 2.4 s.
   - Without `kvSetting:SecretUri`, `GET authentication` returned 500 from the `PublicSigningKeyProvider` constructor.
     With it set, the host crashed at startup in Key Vault config. With the DI fake both were fine.
4. **Browser flow** with a 90-line Python fake IdP plus a `POST profile/api/v1/internal/user` stub:
   - `goto=http://…` → 302 to `BaseUrl`.
   - `goto=https://local.altinn.cloud/ttd/app/` → 302 to the IdP with PKCE, nonce and
     `acr_values=selfregistered-email idporten-loa-substantial`.
   - Callback → 302 to goto with the four Secure cookies. The Profile body was `{"ssn":"01017512345", "userId":0,…}`.
5. **Refresh:** bearer `refresh` → 200 once the self-JWKS was reachable. The refreshed token has `nameid` instead of
   `sub`; that is production behaviour.
6. **Exchange:** `exchange/maskinporten?test=true` gave `urn:altinn:org=ttd`, `orgNumber`, `authlevel=3`, Altinn `iss`
   and `sid`. The system-user exchange returned 200 with `authorization_details`.
7. **No DB, no repository fakes:** the host starts, `/health` returns 503, sign-in returns 500.

Not verified: logout, `acr_values` step-up, `exchange/id-porten` against the vendored Register, a real browser behind
nginx with `proxy_cookie_flags`, and the app consuming these tokens end to end.

## 10. Verdict (question 7, revised)

| | (a) Vendored Authentication + in-place picker | (b) Fully fake service |
|---|---|---|
| Directive sites | 2 required (host hook `AuthenticationHost.cs:293`, goto guard `AuthenticationController.cs:158`), 1 optional (`:246`) | 0 (no vendor) |
| Fakes code | Picker controller and view (~250 lines), ticket store plus fake `IOidcProvider`/`IUpstreamTokenValidator` (~120), `ISigningKeysRetriever` and `IPublicSigningKeyProvider` (~60), token controller with the `/Home/*` contracts and the local Maskinporten issuer (~250), overrides (~50). About 700–900 lines in total, plus ~450 for in-memory repositories if Postgres is not wanted. | Picker, token minting for all token kinds, claim sets, openid/jwks, refresh, cookies: ~800–1 200 lines, all re-derived by hand |
| Infra | Postgres database (shared container) or in-memory repositories; Profile `internal/user`; orgs file; ingress alias plus `proxy_cookie_flags`; Development environment; ~10 s startup including migrations | None |
| Fidelity | Exact production claims, cookies (incl. `AltinnPartyUuid`, `altinnsession`), sessions, refresh with sliding expiry, logout, acr step-up, exchange (Maskinporten, system user, ID-porten), org lookup, deceased refusal | Approximate; drifts as production changes (today's gaps: `AltinnPartyUuid`, `originaliss`, `sid`/session semantics, refresh `nameid`) |
| Risk | Upstream refactors of `OidcServerService`/`AuthenticationController` can move the 2 sites. `appsettings.Development.json` drift. Analyzer strictness for in-project code. | Silent divergence that apps only discover in tt02 |

**Recommendation: (a).** Implementation list:

1. **Vendor closure:** `src/apps/Altinn.Authentication` plus `src/pkgs/JWTCookieAuthentication` (relative
   `ProjectReference`, `Altinn.Platform.Authentication.csproj:80`). Everything else comes from NuGet.
2. **Directives:** host hook, goto guard, and optionally the picker render.
3. **DI fakes:**
   - `IOidcProvider` and `IUpstreamTokenValidator`: the picker hand-off.
   - `ISigningKeysRetriever`: http metadata.
   - `IPublicSigningKeyProvider`: localtest cert.
   - `IEventsQueueClient`: no-op.
   - Optional: `IProfile` in-process, the in-memory `IUnregisteredClientRepository`, `IUpstreamLoginTransactionRepository`
     and `IOidcSessionRepository`, and removal of the Npgsql health check.
4. **Fakes controllers:** picker (`localtest/login`), `/Home/GetTestOrgToken`, `/Home/GetTestUserToken`,
   `/Home/GetTestSystemUserToken`, `/Home/auth/*`, local Maskinporten `.well-known` and JWKS, and the orgs file.
5. **Config:**
   - Environment: `ASPNETCORE_ENVIRONMENT=Development`.
   - `GeneralSettings`: `PlatformEndpoint`, `HostName`, `BaseUrl`, `OpenIdWellKnownEndpoint` (self),
     `OidcRefreshTokenPepper`, `JwtValidityMinutes=960`, `MaskinportenWellKnown*`, `OrganisationRepositoryLocation`.
   - `OidcProviders:idporten:{Issuer, AuthorizationEndpoint, ClientId, ProviderClaims:[actual_iss]}`.
   - `PlatformSettings:Api*Endpoint`, `AccessTokenSettings` cert, `FeatureManagement:AuditLog=false`, and the
     Postgres connection strings.
6. **Ingress and other services:**
   - Ingress: `/authentication/` and `/Home/` go to Authentication, with `proxy_cookie_flags ~ nosecure` and the
     network alias `local.altinn.cloud:8000`.
   - Profile fake: add `POST profile/api/v1/internal/user`.
