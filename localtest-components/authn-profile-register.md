# Localtest: Authentication, Profile, Register and the test-user data model

Research date 2026-10-16. Read-only. Paths are relative to `/home/agent/code`.
Monorepo at [`altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) (current checkout); production references are shallow clones:
[`.reference/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2) @392f957 (2026-10-16, holds live Authentication + Mockporten + JWTCookieAuthentication pkg),
[`.reference/altinn-register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) @f6b5c60 (2026-10-13), [`.reference/altinn-profile`](https://github.com/Altinn/altinn-profile/tree/de3e9fe30044848c904fb0625d899b9f4e4973c5) @de3e9fe (2026-10-15).
[`.reference/altinn-authentication`](https://github.com/Altinn/altinn-authentication/tree/b986f860a2db1c605e4fdf129f1554a5d85e7927) is archived and was not used except as background.

## Executive summary

- **Fake login is small and self-contained.** It is ~1,300 lines: `HomeController` (start page + `LogInTestUser` + legacy `/Home/GetTest*Token`), `TestAuthenticationController` (`/Home/auth/*`), `AuthenticationController` (`authentication/api/v1/refresh|authentication|orgToken|appToken`), `OpenIdController` (well-known + JWKS) and two token services. It signs RS256 tokens with [`jwtselfsignedcert.pfx`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/jwtselfsignedcert.pfx) (password `qwer1234`), which is **byte-identical** to the self-signed fallback cert in production Authentication. Apps validate only the signature (via `AppSettings:OpenIdWellKnownEndpoint` → JWKS); issuer and audience are not validated. Its only data dependency is `TestDataService` (users, party lists, orgs, systems/system users).
- **The test-user model is the hub.** One in-memory `TestDataModel` (Authorization/Profile/Register sections), built from `testdata/` on disk, the apps' `testData.json`, or Tenor uploads, feeds the login page, token minting, Register, Profile, Authorization (roles, party list, claims, PIP, system-user policy), AccessManagement authorized parties, Storage instance-owner lookup and Notifications contact points. Any real replacement service would need this model translated into its own storage and schema.
- **Register and Profile in localtest are 2019/2020 copies.** They were written as thin wrappers over the test data, with only a few changes since (partylist in 2023, nullable flip in 2026). Production Register now has v2 `internal/parties/query|stream`, `apps/parties/query`, `parties/byuuid`, `nameslookup`, `partylistbyuuid` and `identifiers`. Production Profile adds `users/byuuid`, `POST users` (by SSN), profile settings, notification settings, party groups and contact points. **The current app backend already calls endpoints localtest lacks**: `register .../apps/parties/query` (instantiation by external URN, notifications), `profile users/byuuid/{uuid}` and `profile POST users`. All three are absent from localtest, which is a real gap today.
- **Verdict, Authentication:** running the production image locally is technically possible but not useful. It needs Postgres, Register v2 internal user provisioning, Profile, Authorization, AccessManagement, ResourceRegistry, an upstream OIDC provider and an Azure queue. Its user-facing value (ID-porten/Maskinporten/Studio token exchange, OIDC server, system-user registry) is tied to the real IdPs. Keep the localtest fake. At most, add a local `exchange/maskinporten` shim.
- **Verdict, Profile:** the production image is a poor fit. It needs Postgres with migrations and seeded settings, **Register v2 internal** for every user lookup (`v2/internal/parties/query`), platform access tokens validated against Key Vault public keys, plus Notifications, Authorization and KRR/Brreg sync jobs (behind Maskinporten, which can be turned off). Because Register stays in localtest, real Profile would only work if localtest Register also gained the v2 internal API. Recommend keeping Profile in localtest and adding the missing endpoints (byuuid, POST users).

---

## 1. Fake login end to end

### 1.1 Start page → selection
- `GET /`, `/Home`, `/Home/Index?goto=` — [`altinn-studio/src/Runtime/localtest/src/Controllers/HomeController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/HomeController.cs) `Index`. Builds `StartAppModel`:
  - apps list from `ILocalApp.GetApplications` (bound app routes) with a 5 s timeout;
  - users from `GetTestUsersAndPartiesSelectList`: for each `TestDataModel.Profile.User`, loads the profile (`IUserProfiles.GetUser`) and party list (`Authorization.Services.Interface.IParties.GetParties(userId)`, which reads `Authorization.PartyList`), and renders option values `"{userId}.{partyId}"`, grouped when a user represents several parties;
  - default auth level read from the selected app's XACML policy obligation `urn:altinn:minimum-authenticationlevel` (`GetAppAuthLevel`; falls back to 2). Choices are -1 (not authenticated) and 0–4. Also exposed as `GET /Home/GetAppAuthenticationLevel?appId=`;
  - cookies remembered: `Localtest_User.Party_Select` and `Localtest_App_Select`.
- View: [`src/Views/Home/Index.cshtml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Views/Home/Index.cshtml); [`Tokens.cshtml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Views/Home/Tokens.cshtml) is a manual token page (`/Home/Tokens`).
- Platform redirect: `GET authentication/api/v1/authentication?goto=` ([`Controllers/Authentication/AuthenticationController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authentication/AuthenticationController.cs)) redirects to `/?goto=…`. The app's `HomeController` sends unauthenticated users here: [`App/backend/src/Altinn.App.Api/Controllers/HomeController.cs:139`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Controllers/HomeController.cs#L139) uses `{ApiAuthenticationEndpoint}authentication?goto=`.

### 1.2 Token issuance
- `POST /Home/LogInTestUser` (`HomeController.LogInTestUser`):
  - If the level is not -1, it calls `TestAuthenticationService.GetUserToken(userId, partyId, level)` and then `CreateJwtCookieAndAppendToResponse`, which sets:
    - `AltinnStudioRuntime` = JWT (HttpOnly, SameSite=Lax, not Secure, `Domain = GeneralSettings.Hostname` = `local.altinn.cloud`, 1337 min);
    - `AltinnPartyId` = partyId;
    - `Localtest_User.Party_Select`. Minor bug: its options are built from the runtime cookie builder (`cookieBuilder.Build`), so it inherits HttpOnly and the 1337 min expiry.
  - `action=reauthenticate` → 204. This is used by Cypress ([`App/frontend/test/e2e/support/auth.ts:201-206`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/test/e2e/support/auth.ts#L201-L206)).
  - With an uploaded prefill XML, it mints a **service-owner** token and instantiates via the app.
  - Otherwise it redirects to `goto` or `/{org}/{app}/`.
- Token minting: [`Services/Authentication/Implementation/AuthenticationService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authentication/Implementation/AuthenticationService.cs) (`IAuthentication`, marked "TODO: delete") and [`TestAuthenticationService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authentication/Implementation/TestAuthenticationService.cs) (newer, "matches tokens actually issued"):
  - Signing: `X509SigningCredentials` from `CertificateSettings.CertificatePath=jwtselfsignedcert.pfx`, `CertificatePwd=qwer1234` ([`src/appsettings.json`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/appsettings.json)), RS256. The pfx is loaded from disk on every call.
  - Lifetime: `AuthnGeneralSettings.JwtCookieValidityTime=960` minutes. `nbf` is back-dated 30 s for host/container clock drift.
  - [`jwtselfsignedcert.pfx`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/jwtselfsignedcert.pfx) and [`JWTValidationCert.cer`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/JWTValidationCert.cer) have the **same SHA-256** as [`.reference/altinn-auth/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/{jwtselfsignedcert.pfx,JWTValidationCert.cer}`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication). The cert is "CN=Altinn Platform Unit test", valid 2020–2120. [`JWTValidationCert.cer`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/JWTValidationCert.cer) is copied to output but not referenced by any localtest code.
- Claim sets (`TestAuthenticationService`):

| Type | Claims |
|---|---|
| User (`GetUserPayload`) | `actual_iss=localtest`, `nameidentifier`, `urn:altinn:userid`, `urn:altinn:partyid`, `urn:altinn:authenticatemethod=BankID`, `urn:altinn:authlevel`, `jti`, `scope=altinn:portal/enduser`. No `iss`. |
| Self-identified | as user + `urn:altinn:username`, method `SelfIdentified`, level 0. The user must have `UserType.SelfIdentified`. |
| Org (Maskinporten-like) | `iss=https://platform.tt02.altinn.no/authentication/api/v1/openid/`, `consumer {authority: iso6523-actorid-upis, ID: 0192:orgno}`, `urn:altinn:orgNumber`, method `maskinporten`, level 3, `client_id`, `token_type=Bearer`, scope (default `altinn:instances.read altinn:instances.write`). |
| Service owner | as org + `urn:altinn:org`; default scope `altinn:serviceowner/instances.read altinn:serviceowner/instances.write`. |
| System user | as org (orgNumber = supplier, taken from `systemId.Split('_')[0]`) + `authorization_details {type: urn:altinn:systemuser, systemuser_id:[guid], system_id, systemuser_org: 0192:orgno}`. |

  - Legacy `AuthenticationService.GenerateTokenForProfile` adds `ClaimsService` custom claims from [`testdata/authorization/claims`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/claims) (only on the `/Home/GetTestUserToken` path). `GetUserPayload` does **not** add custom claims, so the login page token lacks them. Inferred from code; not run.
- Token APIs (no auth required):
  - `TestAuthenticationController` `[Route("Home/auth")]`: `GET user?userId&partyId&authenticationLevel&scope`, `selfidentifieduser?username`, `org?orgNumber&scope`, `serviceowner?orgNumber&org&scope`, `systemuser?systemId&systemUserId&scope`, `generate?type=1..5…`, `info`. Validates against test data, e.g. the user must be able to represent the party (`Authorization.PartyList`). Used by [`App/backend/test/Altinn.App.Integration.Tests/_fixture/Operations/AppFixture.Auth.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/test/Altinn.App.Integration.Tests/_fixture/Operations/AppFixture.Auth.cs).
  - Legacy `HomeController`: `GET /Home/GetTestUserToken/{userId}?authenticationLevel`, `GET /Home/GetTestOrgToken/{org}?orgNumber&scopes&authenticationLevel`, `GET /Home/GetTestSystemUserToken`.
    - **The app runtime depends on `GetTestOrgToken`**: [`App/backend/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs:96-97`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs#L96-L97) (and the same in `app-lib-dotnet` v8 [`:78`](https://github.com/Altinn/app-lib-dotnet/blob/4740bb28ea13e775c13ca2a2a19942877acf2be1/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs#L78)) uses it instead of the Maskinporten→Altinn exchange when `IsLocaltestPlatform()`. It hardcodes `orgNumber=991825827`.
    - Integration tests also use `GetTestUserToken`; [`CustomOpenApiController.cs:716`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Controllers/CustomOpenApiController.cs#L716) documents it.
  - `AuthenticationController`: `GET refresh` ([Authorize]: re-signs the current principal; the app calls it from [`AuthenticationClient.cs:46`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Authentication/AuthenticationClient.cs#L46) via `/{org}/{app}/api/authentication/keepAlive`), `GET orgToken`, `GET appToken` (old helpers).
  - No studioctl Go code calls these token endpoints (grep of [`src/cli`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli) found none). studioctl only sets the app's endpoints (below).
- No `logout` endpoint exists in localtest. App frontend settings have `logoutUrl` for production; not confirmed what localtest frontend config uses.

### 1.3 OpenID metadata and app validation
- [`Controllers/Authentication/OpenIdController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authentication/OpenIdController.cs) (copied from production; still has the Key Vault branch):
  - `GET authentication/api/v1/openid/.well-known/openid-configuration` — issuer and endpoints are built from `http://{Request.Host}`;
  - `…/openid-configuration/jwks` — one RSA key, `kid` = cert thumbprint, `x5c` chain.
- App backend: [`App/backend/src/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:558-577`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs#L558-L577) → `AddJwtCookie` with `JwtCookieName=AltinnStudioRuntime`, `MetadataAddress = AppSettings:OpenIdWellKnownEndpoint`, `ValidateIssuer=false`, `ValidateAudience=false`, `ValidateLifetime`, `ClockSkew=0`. HTTPS metadata is optional in Development.
  - The JWTCookieAuthentication package appends `.well-known/openid-configuration` ([`.reference/altinn-auth/src/pkgs/JWTCookieAuthentication/src/Altinn.Common.Authentication/JwtCookie/JwtCookiePostConfigureOptions.cs:28-36`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs/JWTCookieAuthentication/src/Altinn.Common.Authentication/JwtCookie/JwtCookiePostConfigureOptions.cs#L28-L36)).
  - Default value: template [`App/template/v8|v9/src/App/appsettings.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/template) → `http://localhost:5101/authentication/api/v1/openid/`. studioctl overrides it with `{platform}/authentication/api/v1/openid/` ([`cli/internal/cmd/app/env.go:82`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/app/env.go#L82)). It also sets `ApiRegisterEndpoint`, `ApiProfileEndpoint` and `ApiAuthenticationEndpoint` to `{platform}/register|profile|authentication/api/v1/` ([`env.go:84-89`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/app/env.go#L84-L89)).
- Localtest validates its own tokens the same way ([`Startup.cs:156-170`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L156-L170), metadata `http://localhost:5101/authentication/api/v1/openid`).
- Package versions: localtest and App backend use `JWTCookieAuthentication` **3.0.1** ([`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj), [`App/backend/Directory.Packages.props:25`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/Directory.Packages.props#L25)). Upstream is at **4.0.4** ([`.reference/altinn-auth/.release-please-manifest.json`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/.release-please-manifest.json)).
- Routing: studioctl topology has no separate auth/profile/register component. These paths are served by the localtest container itself ([`cli/internal/cmd/env/localtest/components/*.go`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/components) only adds pdf, workflow-engine (+ Postgres), pgadmin and monitoring).

**Surface that must stay:**
- `/`, `/Home/*` (Index, LogInTestUser, Tokens, GetTest*Token, GetAppAuthenticationLevel, auth/*, DebugUsers, TenorUsers);
- `authentication/api/v1/{authentication,refresh,orgToken,appToken,openid/.well-known/openid-configuration[/jwks]}`;
- the signing pfx;
- the cookies `AltinnStudioRuntime` and `AltinnPartyId`.

**Dependencies:** `TestDataService`, `IUserProfiles`, Authorization `IParties` (party list), `ILocalApp` (app list, XACML policy, instantiate), `AltinnOrgsClient` (CDN orgs, legacy org-token path), `IClaims`, `IOrganizations`.

## 2. Test-user data model

### 2.1 Sources and merge ([`src/Services/TestData/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData))
- [`TestDataModel.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/TestDataModel.cs):
  - `Authorization{Claims[userId], PartyList[userId]→Party[], Roles[userId][partyId]→Role[], Systems[id], SystemUsers[id]}`
  - `Profile{User[userId]→UserProfile}`
  - `Register{Org[orgNo]→Organization, Party[partyId]→Party, Person[ssn]→Person}`
  - Types come from `Altinn.Platform.Models` 1.6.1 (old platform contract DTOs).
- Disk ([`TestDataDiskReader.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/TestDataDiskReader.cs), path `LocalPlatformSettings.LocalTestingStaticTestDataPath`, repo [`src/Runtime/localtest/testdata/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata)):
  - [`Profile/User/*.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Profile/User) — 8 users (1001–1004, 12345, 1337, 2001, 2002);
  - [`Register/Org`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Register/Org) — 14; [`Register/Party`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Register/Party) — 20; [`Register/Person`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Register/Person) — 6;
  - [`authorization/claims`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/claims) — 2; [`authorization/partylist`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/partylist) — 8;
  - [`authorization/roles/User_{id}/party_{id}/roles.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/roles);
  - [`authorization/systems/{systemId}/system.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/systems) + [`systemusers/*.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/systems/913312465_sbs/systemusers);
  - [`authorization/resources`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/resources) (74 files; resource registry, not part of the user model).
- App ([`AppTestDataModel.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/AppTestDataModel.cs); schema `https://altinncdn.no/schemas/json/test-users/test-users.schema.v1.json`): fetched from each bound app at `{appId}/testData.json` ([`Services/LocalApp/Implementation/LocalAppHttp.cs:191-260`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/LocalApp/Implementation/LocalAppHttp.cs#L191-L260)), typically `App/wwwroot/testData.json` (example [`src/test/apps/signering-brukerstyrt/App/wwwroot/testData.json`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/test/apps/signering-brukerstyrt/App/wwwroot/testData.json)). The flat model is `persons[]` (partyId, partyUuid, ssn, names, address, email, mobile, userId, userName, language, `customClaims`, `partyRoles{partyId: Role[]}`), `orgs[]` (partyId, partyUuid, orgNumber, parentPartyId, name, addresses, unitType/Status) and `systems[]` (id, name, `systemUsers[{id, orgNumber, actions}]`). The model projects into all three sections; persons without SSN become SelfIdentified parties.
- Tenor ([`Services/Tenor/TenorDataReader.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Tenor/TenorDataReader.cs), UI `TenorUsersController` `/Home/TenorUsers/{Index,Upload,Update}`): reads Freg (person) and BrregErFr (org) JSON files uploaded into `{LocalTestingStorageBasePath}/{TenorDataFolder}`, assigns sequential partyIds, and derives `partyRoles` from Brreg role groups (`Role{Type:"Altinn", Value: role code}`).
- `TestDataService.GetTestData`: cached for 5 s sliding / 30 s absolute. If **all** reachable apps serve testData, only the app data is used. If only some do, the app data is merged into the disk data. With no app data, Tenor data is merged into disk data.
  - `TestDataMerger` merges Profile.User, Register.Party/Person/Org, Authorization.PartyList and Roles, and throws on conflict.
  - It does **not** merge `Claims`, `Systems` or `SystemUsers`. So an app's custom claims and systems are dropped in the mixed case, and app-to-app merging goes through `FromTestDataModel`, which also drops systems and self-identified users. Inferred from code, not run.
- `DebugUsersController` (`/Home/DebugUsers/*`) dumps raw, converted and round-tripped models.

### 2.2 Dependency table — component → test data it reads

| Component (localtest) | Entry points | Test data needed |
|---|---|---|
| Login page / token APIs | `HomeController`, `TestAuthenticationController`, `TestAuthenticationService` | Profile.User (userId, partyId, userName, userType), Authorization.PartyList (who can represent whom), Register.Org keys (org tokens), Authorization.Systems/SystemUsers |
| Legacy tokens | `AuthenticationService` | Profile.User (via IUserProfiles), Authorization.Claims (`ClaimsService`), Register.Org (system user), CDN orgs list |
| Register | [`Controllers/Register/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register) → `PartiesWrapper`, `PersonsWrapper`, `OrganizationsWrapper`, `PersonLookupService` | Register.Party, Register.Person (by SSN), Register.Org; Person lookup matches SSN + last name |
| Profile | [`Controllers/Profile/UsersController`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Profile/UsersController.cs) → `UserProfilesWrapper` | Profile.User + Register.Party (enriched with Person/Org) |
| Authorization roles | `RolesController`, `RolesWrapper`, `ContextHandler` (PIP) | Authorization.Roles[user][party] |
| Authorization party list | `Authorization/PartiesController`, `PartiesService` | Authorization.PartyList |
| Authorization PDP / PIP | `ContextHandler` (party lookup by SSN/orgNo, `GetUserByPersonId`), `DecisionController` (system-user in-memory policy; instance delegation matching by PartyUuid) | Register.Party (+PartyUuid), Profile.User, Authorization.Roles, PartyList, SystemUsers |
| AccessManagement | `AuthorizedPartiesController` (`accessmanagement/api/v1/enduser/authorizedparties`) | Authorization.PartyList (no packages/resources/roles returned) |
| Storage | [`Services/Storage/Implementation/RegisterService`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/RegisterService.cs) → `IParties` (`GetParty`, `LookupPartyIdBySSNOrOrgNo`) used by `InstancesController` | Register.Party (+Person/Org) |
| Notifications | [`Notifications/LocalTestNotifications/LocalProfileClient`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Notifications/LocalTestNotifications/LocalProfileClient.cs), `LocalRegisterClient` | Profile.User (email/phone by SSN), Register.Org (contact points) |
| Tenor import | `TenorDataRepository` | produces persons/orgs/roles in app-model shape |

A real replacement service would have to be fed parties with **both** int `partyId` and `partyUuid` and consistent `userId` to party mappings. Roles would have to be expressed in its own model: production Register v2 "external roles" and AccessManagement packages, not localtest's `Role{Type,Value}`.

## 3. Register and Profile provenance and drift

- **Provenance:** localtest came into the monorepo via the subtree merge `1166612623` (2026-06-04, from `Altinn/app-localtest` at `49e2ba8e6e`). The history before that, through the second parent:
  - [`Controllers/Register/PartiesController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register/PartiesController.cs): first in `115e179331` (2019-11-29, "Feature/2536 local test"), last real change `637352bcee` (2020-05-11), then nullable flip (2026-06-03).
  - [`Controllers/Register/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register) folder: partylist endpoint `1aa51264bb` (2023-01-30), auth policy alignment `3851fe63aa` (2023-06-20).
  - `PersonsController` and `PersonLookupService`: 2022-02/03 (copied from register with `#if`-style edits; namespace still `Altinn.Platform.Register.Core`).
  - [`Profile/UsersController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Profile/UsersController.cs): 2019-12/2020-01.
  - So these are 2019–2020 copies of controller signatures with services rewritten as test-data wrappers. Only the controllers and `PersonLookupService` look like real copies. No storage layer is shared.
- **Localtest Register surface:** `GET register/api/v1/parties/{id}`, `POST parties/lookup`, `POST parties/partylist`, `GET organizations/{orgNr}`, `GET persons` (headers). No auth on parties.
- **Production Register** ([`.reference/altinn-register/src/apps/Altinn.Register/src/Altinn.Register/Controllers`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers)):
  - v1 adds `parties/byuuid/{uuid}`, `nameslookup`, `partylistbyuuid` and `identifiers`; `organizations/contactpoint/lookup`;
  - consumer-specific `apps/parties/query`, `access-management/parties`, `correspondence/parties`, `notifications/parties`, `dialogporten/parties`, `support-dashboard`;
  - v2 `internal/parties/{uuid}|query|stream|main-units|set-username|external-roles…|customers/ccr/*|holders/ccr/daglig-leder`, `internal/parties/self-identified`, `internal/metadata/external-roles`;
  - almost all endpoints require `InternalOrPlatformAccess` (platform access token).
  - Backed by Postgres + MassTransit imports from A2 (SBL), NPR, CCR/SIRE.
- **Profile** — localtest: `GET profile/api/v1/users/{id}`, `GET users/current`, no auth.
  - Production ([`.reference/altinn-profile/src/Altinn.Profile/Controllers`](https://github.com/Altinn/altinn-profile/tree/de3e9fe30044848c904fb0625d899b9f4e4973c5/src/Altinn.Profile/Controllers)): `users/{id}`, `users/byuuid/{uuid}`, `users/current`, `POST users` (SSN body), `PUT|PATCH users/current/profilesettings`, `users/current/party-groups[/favorites]`, `users/current/notificationsettings/parties[/{uuid}|byorgnumber/{orgNo}]`, `users/current/verification/*`, `users/contactpoint/{availability,lookup,lookupsi}`, `units/contactpoint/lookup`, `organizations/{orgNo}/notificationaddresses/mandatory…`, `organizations/notificationaddresses/lookup`, `correspondence/*`, `dashboard/*`, `internal/user`.
- **App calls missing in localtest** (App backend [`src/App/backend/src`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src) and `app-lib-dotnet` v8 both):

| Call | Where | Localtest |
|---|---|---|
| `POST register/api/v1/apps/parties/query?fields=id,user.id` (body `{data:[urn]}`) | `AltinnPartyClient.GetPartyIdByUrn/GetPartyUuidByUrn`; used by [`InstancesController.cs:1955,1990`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Controllers/InstancesController.cs#L1955) (instance owner `ExternalIdentifier`) and [`NotificationService.cs:436`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L436) | **missing** |
| `GET profile/api/v1/users/byuuid/{uuid}` | [`ProfileClient.cs:174`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Profile/ProfileClient.cs#L174); [`NotificationService.cs:446`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L446) | **missing** |
| `POST profile/api/v1/users` (SSN) | [`ProfileClient.cs:135`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Profile/ProfileClient.cs#L135); [`NotificationService.cs:427`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L427), [`SigningCallToActionService.cs:72`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Signing/Services/SigningCallToActionService.cs#L72) ([`InstancesController.cs:1484`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Controllers/InstancesController.cs#L1484) uses the int-id overload, present) | **missing** |
| `GET register/api/v1/parties/{id}`, `POST parties/lookup`, `POST parties/partylist?fetchSubUnits=true`, `GET organizations/{orgNr}`, `GET persons` | `AltinnPartyClient`, [`RegisterClient.cs:106`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Registers/RegisterClient.cs#L106), `RegisterERClient`, `PersonClient` | present (`fetchSubUnits` is ignored) |
| `GET profile/api/v1/users/{id}` | `ProfileClient`, `AuthenticationContext`, `UserHelper`, `BootstrapGlobalService` | present |

## 4. Authentication: production vs localtest

- Production Authentication ([`.reference/altinn-auth/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers)):
  - `GET authentication` (OIDC client login against upstream IdPs, session cookie reuse, ACR step-up);
  - `GET refresh`;
  - `GET exchange/{id-porten|maskinporten|altinnstudio}` (validates against ID-porten/Maskinporten well-known JWKS or Studio signing keys and re-issues an Altinn token; maps org number to org via `altinn-orgs.json`; system-user lookup);
  - an Altinn OIDC server (`authorize`, `token`, `upstream/callback`, front-channel logout, `openid/logout`, `logout`, `introspection`);
  - self-identified email links;
  - the system register, system users, system-user requests and change requests, and client delegation.
- Infrastructure (from [`AuthenticationHost.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/AuthenticationHost.cs) / [`appsettings.json`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/appsettings.json)):
  - Postgres (system users etc.);
  - HTTP clients to Register (`v2/internal` user provisioning), Profile, Authorization, AccessManagement, ResourceRegistry and Notifications;
  - an Azure Storage queue for event logs;
  - Key Vault certs (falls back to the same local pfx when `kvSetting` is empty);
  - upstream `OidcProviders` (ID-porten, or Mockporten for synthetic users), Maskinporten well-known.
- Mockporten ([`.reference/altinn-auth/src/apps/Altinn.Mockporten`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Mockporten)) is an upstream OIDC test-IdP. It is gated by a feature flag and shared password and only accepts synthetic Tenor fnr (month+80). It is meant for Authentication's real login flow in test environments, not as a replacement for localtest's picker. In principle "Authentication + Mockporten" could give a production-like login locally, but you would pick users by Tenor fnr (localtest's built-in users such as 01017512345 are not synthetic numbers). It would also drag in the whole dependency set above.
- What the **app** calls:
  - `refresh` (keepAlive) — present in localtest;
  - `authentication?goto=` — present;
  - `openid/.well-known` — present;
  - `exchange/maskinporten` ([`MaskinportenClient.cs:279`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Maskinporten/MaskinportenClient.cs#L279), v8 [`:239`](https://github.com/Altinn/app-lib-dotnet/blob/4740bb28ea13e775c13ca2a2a19942877acf2be1/src/Altinn.App.Core/Features/Maskinporten/MaskinportenClient.cs#L239)) — **absent**, but bypassed by `AuthenticationTokenResolver` in localtest mode for `AltinnToken`/ServiceOwner. A direct `IMaskinportenClient.GetAltinnExchangedToken` call from custom app code would 404, and would also need a real Maskinporten client.
- Small production-only features localtest only half emulates: token claim shape (system user `authorization_details`, consumer); `AltinnPartyUuid` cookie (production sets it, localtest does not); logout.
- **Verdict:** keep it in localtest.
  - The production image could start locally (dev cert fallback exists). But every useful path either needs real ID-porten/Maskinporten/Studio keys or a Postgres plus five other platform services, Register v2 internal among them, which localtest's Register does not provide.
  - It would not replace the user picker, which is the main thing. Product-owner constraint aside, there is no gain.
  - Worth doing instead: keep claim shapes in sync with production (e.g. add `AltinnPartyUuid`, system-user claims); bump JWTCookieAuthentication; optionally add an `exchange/maskinporten` shim that re-signs any bearer as a service-owner token.

## 5. Profile: production as a container?

- The app calls `users/{id}`, `users/byuuid/{uuid}` and `POST users` (by SSN). It does not call `users/current` (the app frontend gets the profile through the app backend; not confirmed that no frontend path calls Profile directly). It calls no notification-settings or party-group endpoints (grep of [`App/backend/src`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src) found none).
- Production Profile needs:
  - Postgres `profiledb` with admin and app users and migrations ([`docker-compose.yml`](https://github.com/Altinn/altinn-profile/blob/de3e9fe30044848c904fb0625d899b9f4e4973c5/docker-compose.yml), [`dbsetup.sh`](https://github.com/Altinn/altinn-profile/blob/de3e9fe30044848c904fb0625d899b9f4e4973c5/dbsetup.sh));
  - **Register** at `RegisterSettings.ApiRegisterEndpoint` for every user lookup (`UserProfileService` → `RegisterClient` → `v2/internal/parties/query`, `v2/internal/parties/main-units`, `v1/parties/identifiers`);
  - Authorization (`PlatformSettings.ApiAuthorizationEndpoint`);
  - Notifications;
  - `OpenIdWellKnownEndpoint` (could point at localtest);
  - platform access-token validation (`AccessTokenHandler` + `PublicSigningKeyProvider` → Key Vault public certs; not confirmed whether a local cert folder fallback is supported);
  - KRR and Brreg KOF sync jobs via Maskinporten (`JobSettings.KrrSyncEnabled=false` by default, so they can be turned off).
  - No SBL bridge is used in the user-profile path any more (only a comment in `UnitContactPointService`).
- **Verdict:** technically containerizable, but its user data comes from Register v2 internal. Since Register stays in localtest, localtest would have to implement `v2/internal/parties/query` (person + user fields) and seed Postgres for settings and contact info, plus solve platform access tokens. That is more work than adding `users/byuuid` and `POST users` to localtest's Profile wrapper. Recommend: keep Profile in localtest, close the endpoint gaps, and take DTOs from the `Altinn.Profile.Models` / Register contracts packages rather than copying code.

## Unverified / not confirmed
- Behaviour of `TestDataMerger` dropping claims and systems is from reading code only.
- Whether the app frontend calls Profile or Authentication endpoints directly in localtest mode (only the backend was grepped).
- Whether production `Altinn.Common.AccessToken` supports a local public-cert directory (needed to run Profile or Register images locally).

## Unlinked references
- `App/wwwroot/testData.json` and `{appId}/testData.json`: generic per-app path patterns, not a specific repository file.
- `https://altinncdn.no/schemas/json/test-users/test-users.schema.v1.json` and `altinn-orgs.json`: CDN-hosted files, not in any cloned repository.
