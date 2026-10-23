# Cookies with `Secure` on a plain-http stack (2026-10-21, task 21b)

> **Superseded 2026-10-23 by task 26:** the `Secure` stripping on app routes now happens only in the localtest container, the front door in both runtimes; studioctl-server's `PlatformTransformer` and the Aspire package's plain YARP no longer forward anything, and the component cookie findings are unchanged; see [`localtest/CONVENTIONS.md`](../CONVENTIONS.md).

Every cookie the stack sets with `Secure`, read from the vendored code, the fakes, the patches and the localtest app at
`f8735996`, and from app-lib (the monorepo's `src/App/backend` at `altinn-studio` `origin/main` `3ccccf7867`, which the
test apps reference by project, and `Altinn/app-lib-dotnet` `v8.12.15`). Searched with
`grep -rnE '\bSecure\b|CookieSecurePolicy|SecurePolicy|secure\s*[:=]'` and with every `Cookies.Append`/`Cookies.Delete`,
`setCookie`, `document.cookie` and `CookieOptions` across `localtest/components/*/{vendor,fakes,patches,nginx}` and
`localtest/localtest/`, tests excluded. **For the owner's review before merge** (`TODO.md` task 21, decisions).

Until now the front doors removed `Secure` from every `Set-Cookie`: studioctl-server (`PlatformTransformer.StripSecure`)
and the localtest app's proxy (`Forwarder.cs`, the `Secure` regex), and the localtest app's `/Home` sign-in did the same
to the cookies it relays (`AuthenticationClient.ForBrowser`). A plain YARP (task 21d) does not, so the components must
not set the flag on http themselves.

## What the front doors forward

| Front door | `X-Forwarded-Proto` | Effect |
| --- | --- | --- |
| studioctl-server (`Proxy/PlatformTransformer.cs`) | Not added: it derives from YARP's bare `HttpTransformer` (= `HttpTransformer.Empty`, checked by decompiling YARP 2.3.0); a client's own header is copied through | Components see `http` |
| The localtest app's proxy (`Proxy/Forwarder.cs`) | `HttpTransformer.Default`: set to the request's scheme, `http` | Components see `http` |
| Arbeidsflate's nginx (`components/arbeidsflate/nginx/nginx.conf`) | Hard-coded `https` on `/api/` (task 11: the only way `@fastify/session` would save a `secure: true` session) | Removed by this task, see A1 |

No .NET component honours the header anyway: Authentication calls `UseForwardedHeaders()` with default options
(`ForwardedHeaders.None`, and `ASPNETCORE_FORWARDEDHEADERS_ENABLED` is not set by studioctl); the others do not call it.
So `HttpRequest.IsHttps` is false in every container, which is what the decisions below key on.

## Inventory

Paths are relative to `localtest/components/`; `AUTHN` is
`authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication`, `BFF` is
`arbeidsflate/vendor/packages/bff/src`. SameSite is production's value.

| # | Cookie | Component | Set at | How `Secure` is decided | SameSite | Decision |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `AltinnPartyId` | Authentication | `AUTHN/Services/OidcServerService.cs:341` (sign-in, `CookieInstruction`), written by `Controllers/OidcFrontChannelController.cs:278` | Hard-coded `true` (`CookieInstruction.Secure` defaults to `true` too, `Altinn.Platform.Authentication.Core/Models/Oidc/AuthorizeResult.cs:26`) | Lax | Cookie policy in `fakes/` (F1) |
| 2 | `AltinnPartyUuid` | Authentication | `OidcServerService.cs:352`, same path | Hard-coded `true` | Lax | F1 |
| 3 | `AltinnStudioRuntime` (the runtime JWT) | Authentication | `OidcServerService.cs:363` (sign-in), `:733` (re-issued from the session), `AuthenticationController.cs:214` (copies the instruction), `:302` (`refresh` "for test", only when `PlatformEndpoint` is `http://localhost/`; never on the stack) | Hard-coded `true` | Lax | F1 |
| 4 | `altinnsession` (the OIDC server's session handle) | Authentication | `OidcServerService.cs:374` | Hard-coded `true` | Lax | F1 |
| 5 | `AltinnStudioRuntime`, `altinnsession` expired (logout, `openid/endsession` without `sid`, upstream front-channel logout) | Authentication | `OidcServerService.cs:510`, `:521`, `:612`, `:624`, `:698`; written by `LogoutController.cs:164` / `OidcFrontChannelController.cs:278` | Hard-coded `true` | Lax | F1 (a deletion must carry the attributes it was set with; both sides lose `Secure`) |
| 6 | `AltinnStudioRuntime` expired when the session refresh fails | Authentication | `AuthenticationController.cs:190` | Hard-coded `true` | Lax | F1 |
| 7 | `AltinnLogoutInfo` deleted | Authentication | `LogoutController.cs:99`, `Response.Cookies.Delete` at `:112` | Hard-coded `true` | (none) | F1 |
| 8 | `AltinnStudioRuntime` deleted (`frontchannel_logout`) | Authentication | `LogoutController.cs:144`, `:145` | Hard-coded `true` | (none) | F1 |
| 9 | `AS-XSRF-TOKEN` (antiforgery) | Authentication | `AuthenticationHost.cs:240` | Framework default `CookieSecurePolicy.None`: never `Secure` | Lax | None needed |
| 10 | `.AspNetCore.Correlation.Correspondence` | Correspondence | `correspondence/vendor/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs:103` | `CookieSecurePolicy.Always` upstream; `SameAsRequest` in `fakes/Auth/LocaltestAuthentication.cs:43` since 11b | None upstream, Lax in fakes | Already done (11b); F1 added as the net for 12 |
| 11 | `.AspNetCore.OpenIdConnect.Nonce.*` | Correspondence | Framework (`OpenIdConnectHandler`) | Framework default `Always`; `SameAsRequest` in `LocaltestAuthentication.cs:45` | None upstream, Lax in fakes | Already done (11b) |
| 12 | The correlation and nonce cookies expired on the "Correlation failed" retry path | Correspondence | `DependencyInjection.cs:149` (`ExpireCookie` in `RestartLogin`) | Hard-coded `true` | None | F1 (also turns `None` into Lax, see below). Before: stripped by the front door, then refused by the browser as `SameSite=None` without `Secure`; the expiry never worked locally |
| 13 | `arbeidsflate` (the BFF's session, `@fastify/session` 11.1.1) | Arbeidsflate BFF | `BFF/server.ts:66` (`cookie.secure: true`), and `BFF/graphql/fastifyHeaders.ts:101` sets it `true` when `X-Forwarded-Proto` is `https` | Hard-coded `true`; the session is only saved when `request.protocol` is `https` | (unset) | Patch 0003: `secure: 'auto'`; nginx no longer sends `X-Forwarded-Proto: https` (A1) |
| 14 | `altinnPersistentContext` (the `UL` language value, `Domain=local.altinn.cloud`) | Arbeidsflate BFF | `BFF/graphql/profile/queries.ts:37`, `BFF/graphql/profile/mutations.ts:101` (`@fastify/cookie` 11.0.2) | Hard-coded `true` | (unset) | Patch 0003: `secure: 'auto'` |
| 15 | `AltinnPartyId`, `AltinnPartyUuid` written by the SPA | Arbeidsflate SPA | `arbeidsflate/vendor/packages/frontend/src/cookie.ts:30` (domain from patch 0002) | Never `Secure` | (unset) | None needed |
| 16 | The sign-in cookies `/Home/LogInTestUser`, `/Home/GetTestUserToken` (via `Home/HomeApi.cs:260`) and `localtest/api/authentication/token` (`Authentication/AuthenticationApi.cs:137`) relay | Localtest app | `localtest/localtest/Home/AuthenticationClient.cs:108` (`ForBrowser`) | Copies Authentication's cookies (1 to 4) and removed `Secure` itself | Lax | Removal dropped: Authentication no longer sends it (F1); the localtest app's own code |
| 17 | Receipt | Receipt | No cookie is set: `ReceiptController.cs:120` and `LanguageHelper.cs` only read `altinnPersistentContext` (`UL`); the frontend writes none | n/a | n/a | None needed |
| 18 | Profile, Notifications, Dialogporten, the Dialogporten adapter, Register, Storage, Authorization, Access Management, Events, Resource Registry, the workflow engine | | No `Set-Cookie` anywhere (the JWT cookie handlers in Storage, Authorization, Receipt and others only read `AltinnStudioRuntime`) | n/a | n/a | None needed |
| 19 | `AltinnStudioRuntime` re-issued by `keepAlive` | **app-lib** (the app, not a component) | Monorepo `src/App/backend/src/Altinn.App.Api/Controllers/AuthenticationController.cs:44`; `app-lib-dotnet` `v8.12.15` the same file, line 43 | Hard-coded `true`, in every environment | Lax | **Out of scope**: the front doors keep removing `Secure` on app routes only (A2); follow-up proposed for app-lib (below) |
| 20 | `XSRF-TOKEN` | app-lib | `Controllers/HomeController.cs:96` (monorepo) | Not set (`CookieOptions` default `false`) | (unset) | None needed |
| 21 | `AS-XSRF-TOKEN` | app-lib | `Extensions/ServiceCollectionExtensions.cs:601` (monorepo) | Framework default `None` | (default) | None needed |
| 22 | `AltinnPartyId` (party selection) and its expiries | app-lib | `Helpers/PartySelectionCookie.cs:25`, `:46`, `:48` (monorepo) | Not set | (unset) | None needed |
| 23 | Client-side cookies (`CookieStorage`) | app frontend | Monorepo `src/App/frontend/src/utils/cookieStorage/CookieStorage.ts:12` | `Secure` unless `isLocalEnvironment(host)` | Lax | Not a `Set-Cookie`; no front door ever touched it |

## The decisions

**F1, one cookie policy in `fakes/`, no directive site.** `components/shared/fakes/Cookies/LocaltestCookiePolicy.cs`
(canonical; copied into Authentication's and Correspondence's `fakes/`, as the Maskinporten fake is) registers an
`IStartupFilter` that puts ASP.NET Core's `CookiePolicyMiddleware` in front of the host's pipeline, with
`OnAppendCookie`/`OnDeleteCookie` callbacks: on a request that is not https, a cookie's `Secure` is turned off, and a
`SameSite=None` cookie becomes `Lax` (a browser refuses `None` without `Secure`). On an https request nothing changes, so
production's attributes are kept wherever TLS is in front. The policy sees every cookie written through
`HttpResponse.Cookies`, which is how every cookie in rows 1 to 12 is written (none is written as a raw header); the
options are passed to `UseCookiePolicy` directly, so a host's own `CookiePolicyOptions` (none today) are not changed.
This is the "fake configuration" the brief prefers: 12 hard-coded `Secure = true` in Authentication would otherwise
have been 12 `#if !LOCALTEST` sites in four files. Authentication registers it from `AddLocaltestAuthentication`
(the existing additive hook), Correspondence from `LocaltestCorrespondence` (likewise).

**SameSite.** Authentication's cookies are all `Lax` already: unchanged. Correspondence's retry-path expiry (row 12) is
`None` upstream and becomes `Lax` with `Secure` gone, the same reasoning as 11b's correlation and nonce cookies. The
Arbeidsflate session cookie gets `SameSite=Lax` from `@fastify/session`'s `'auto'` on http (it is unset upstream, which
browsers treat as `Lax`); `altinnPersistentContext` stays unset (`@fastify/cookie`'s `'auto'` sets only `Secure`).

**A1, Arbeidsflate: patch 0003 and nginx.** TypeScript has no `#if`, so the component's form of a site is a build-time
patch (task 11). `0003-bff-cookies-secure-auto.patch` changes the three `secure: true` to `secure: 'auto'`, which both
libraries support (checked in the published `@fastify/session` 11.1.1 `lib/cookie.js` and `@fastify/cookie` 11.0.2
`plugin.js`): `Secure` exactly when `request.protocol` is `https`, as production behind its TLS ingress, plain on the
stack. `@fastify/session` then saves the session on http as well (it refuses only when `secure === true` and the
connection is not https), so nginx's `proxy_set_header X-Forwarded-Proto https` goes; with it the hook in
`fastifyHeaders.ts:101` would turn `Secure` back on.

**A2, app routes keep the stripping.** app-lib's `keepAlive` sets `AltinnStudioRuntime` with `Secure = true` in every
environment (row 19), and apps are not vendored here. app-frontend calls it (`refreshJwtTokenUrl`) to keep a signed-in
user's token alive, so without the stripping a long form session on the stack would lose its cookie refresh: the
browser refuses the `Secure` cookie from an http origin and keeps the old token until it expires. Both front doors
therefore keep removing `Secure`, **only on app routes**: studioctl-server for a bound route whose component is `app`,
the localtest app's proxy for a route its app runner registered. Platform routes pass `Set-Cookie` unchanged.

**Follow-up for app-lib (proposed for `TODO.md` task 22).** `keepAlive` should decide `Secure` from the request
(`Request.IsHttps`, or `CookieSecurePolicy.SameAsRequest` through a `CookieBuilder`), so an app run over plain http
locally gets a cookie the browser keeps, and production behind TLS is unchanged. Task 22 is already the app-lib
follow-up of task 21 (an explicit localtest environment); adding this item there would let an Aspire front door be a
plain YARP for app routes too. Upstream in the monorepo (`src/App/backend`), with `app-lib-dotnet` v8 if it is still
patched.

## What phase 2 verifies

Every login path through the front door with curl (no `Secure` in any `Set-Cookie`), the platform regression, the
Arbeidsflate inbox login and logout, Correspondence's attachment download from the inbox, Receipt's sign-in round
trip, the localtest app's `/Home` cookies, and a grep of `Set-Cookie` through the front door across the smoke tests
that finds `Secure` nowhere except on an app's `keepAlive`.

## Verified on the stack (2026-10-22, phase 2)

Images built from this branch: `--dev authentication --dev localtest --dev correspondence --dev arbeidsflate` (both
Arbeidsflate Dockerfiles apply patch 0003 in their `source` stage), the rest at the pin `sha-7f21882`.

- **Every login path through the front door** (`/home/agent/code/.scripts/cookie-capture.sh`, curl, each redirect
  followed by hand): the picker sign-in, `/Home/LogInTestUser`, `localtest/api/authentication/token`, Arbeidsflate's
  `/api/login` through `authorize` for `arbeidsflate-localtest` to `/api/cb` and its `/api/logout`, Receipt's sign-in
  round trip, Correspondence's challenge for `correspondence-localtest` to `idporten-callback`, and `openid/logout`:
  32 `Set-Cookie` headers, none with `Secure`.
- **The components' own ports**: Authentication (8021), Correspondence (8033) and Arbeidsflate's nginx (8037) send the
  same cookie attributes as the front door passes on, without `Secure` (the new smoke checks).
- **Arbeidsflate in Chromium**: the inbox login as Sophie Salt, the inbox with its dialogs
  (`../components/arbeidsflate/docs/cookies-inbox.png`), every cookie `Secure=false` in the browser's store, the
  `arbeidsflate` session unchanged across a reload and `/api/isAuthenticated` 200 (the login loop the forged
  `X-Forwarded-Proto` used to prevent does not return), a Correspondence attachment downloaded from a dialog, and the
  logout (the old session then answers 401).
- **App routes (A2)**: `GET /ttd/frontend-test/api/authentication/keepAlive` (`studioctl run`) through the front door
  answers `AltinnStudioRuntime` without `Secure`; the same request on the app's own port carries `Secure`.
- **Platform regression**: 44 of 44 on the default stack with the four dev images and three apps under
  `studioctl run --dev-frontend` (with `--with everything` as well, Cypress was killed by the OOM killer on this
  7.8 GB machine).
- **Smoke tests on `--with everything`**: root 26, Arbeidsflate 59, Authentication 34, Authorization 31,
  Correspondence 81 (with the restart), Dialogporten adapter 39, Dialogporten 60, Notifications 64, Profile 52,
  Receipt 27, Register 14, Resource Registry 43, shared 10 (with `Cookies.Smoke`), workflow engine 3, all passing.
  Events 27 of 27 on its own; run right after the Dialogporten and Correspondence suites it fails 2 count checks,
  because their delayed `dialogporten.dialog.*` and Correspondence notification events land in Events after its reset
  (not a cookie matter). The localtest app 64 of 66 with `PROXY_CHECKS=0`, the two known failures of task 13.
