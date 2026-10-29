# Authentication: upstreaming

- Upstream: [Altinn/altinn-auth](https://github.com/Altinn/altinn-auth), `src/apps/Altinn.Authentication/` (with
  `src/pkgs/JWTCookieAuthentication/`)
- Pin: `3d51810` (main, 2026-10-19),
  [`../components/authentication/vendor/UPSTREAM.md`](../components/authentication/vendor/UPSTREAM.md); none of the 33
  upstream commits since touches the directive sites (checked 2026-10-27)
- Group: `altinn-auth`, one pin shared with [Authorization](authorization.md) and
  [Access Management](access-management.md); the three move together.
- Reminders: at every sync, grep the new tree for cookies written other than through `Response.Cookies` (the cookie
  policy in `fakes/Cookies/` sees only those).
- Host: an `AltinnServiceDefaults` host (`AltinnHost.CreateWebApplicationBuilder("authentication", ...)`,
  `Altinn.Authorization.ServiceDefaults` 6.0.0), so service discovery and OTLP export come from the shared library; its
  changes are in [`authorization-utils.md`](authorization-utils.md).
- Localtest modifications: 2 directive sites under `src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`
  (`AuthenticationHost.cs`, `Controllers/AuthenticationController.cs`), the composition
  `fakes/LocaltestAuthenticationExtensions.cs` with `fakes/LocaltestAuthenticationOptions.cs`, the seeded clients
  `fakes/LocaltestOidcClients.cs`, the level table `fakes/Login/`, the key providers `fakes/Keys/`, the in-memory
  repositories `fakes/Persistence/`, the shared copies `fakes/Cookies/` and `fakes/ServiceDiscovery/`, two build
  overrides, and the `localtest:` deviations in `Dockerfile` and `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/authentication/` (sites relative to `vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`).

Upstream plans its own local setup: the epic
[altinn-auth#4048](https://github.com/Altinn/altinn-auth/issues/4048) (Aspire, Mockporten, one command from clone to a
working system-user flow) with [altinn-auth#4062](https://github.com/Altinn/altinn-auth/issues/4062) (Authentication in
the app host, its own database, Mockporten as the only upstream provider) and
[altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050) (the configuration contract for local runs). It
targets `https://local.altinn.cloud` and keeps `Secure` cookies, so the http items below need to be raised there.

## Phase 1: easy fixes

- [ ] **Allow an http `goTo` target in Development.** `IsSafeSameOrSubdomainHttps` in
      `Controllers/AuthenticationController.cs` refuses every target that is not `https`. Accept `http` as well when the
      host runs in Development (or behind a setting such as `GeneralSettings:AllowHttpRedirectTargets`, default false),
      keeping the credential and same-or-subdomain checks.
  - Why: the stack serves `local.altinn.cloud` over plain http, so every sign-in that returns to an app or to the
    platform would be refused.
  - Size: small.
  - Removes: site `AuthenticationController.cs:1057-1070`.
  - Upstream: none found. The method is still unchanged on `main`.
  - Detail: [`../reviews/authentication.md`](../reviews/authentication.md), "Vendored code", site 2.

- [ ] **Fetch provider metadata over http in Development.** `Helpers/ConfigurationMangerHelper.cs:25-28` (used by
      `Services/SigningKeysRetriever.cs`) builds its `ConfigurationManager` with `new HttpDocumentRetriever()`, whose
      `RequireHttps` defaults to `true`. Set `RequireHttps` from the environment (false only in Development) or from a
      setting, as the JWT cookie handler already does with `RequireHttpsMetadata`.
  - Why: the localtest app's fake ID-porten and Maskinporten publish their metadata and JWKS over plain http, so the
    upstream callback and the Maskinporten exchange fail before any token is checked.
  - Size: small.
  - Removes: `LocaltestSigningKeysRetriever` and its registration (half of `fakes/Keys/`; the folder goes with the Key
    Vault item below). Production keeps the document for 12 hours, so after this change the localtest app's fakes
    must keep their signing keys across restarts (today they generate new ones at start, and the fake retriever does
    not cache); I could not confirm whether production asks for a refresh when a token's `kid` is unknown.
  - Upstream: none found.
  - Detail: [`../../planned-components/authentication/production-authentication-analysis.md`](../../planned-components/authentication/production-authentication-analysis.md),
    the "Upstream and Maskinporten metadata" row.

- [ ] **A setting for the `Secure` attribute on the cookies Authentication writes.** Production hard-codes
      `Secure = true` in twelve places in four files: `AltinnPartyId`, `AltinnPartyUuid`, `AltinnStudioRuntime` and
      `altinnsession` at sign-in and re-issue (`Services/OidcServerService.cs`, `Controllers/AuthenticationController.cs`),
      their expiries at logout and on a failed refresh, the deletions in `Controllers/LogoutController.cs`, and the
      default of `CookieInstruction.Secure` (`Altinn.Platform.Authentication.Core/Models/Oidc/AuthorizeResult.cs`). Read
      one setting instead (for example `GeneralSettings:SecureCookies`, default true) in all of them.
  - Why: a browser refuses a `Secure` cookie from an http origin, so on the stack nobody would stay signed in.
  - Size: small.
  - Removes: `fakes/Cookies/` (the cookie policy middleware, registered from the hook) and the `reminders` entry in
    `UPSTREAM.md`.
  - Upstream: none found. Deriving the flag from `Request.IsHttps` would be smaller, but it is only right in production
    if the TLS ingress's `X-Forwarded-Proto` reaches the host: ServiceDefaults sets `ForwardedHeadersOptions` to
    `All`, while [`../reviews/cookies.md`](../reviews/cookies.md) found `UseForwardedHeaders()` taking no effect; I
    could not confirm what production sees, so the setting is the safer proposal.
  - Detail: [`../reviews/cookies.md`](../reviews/cookies.md), inventory rows 1 to 8 and decision F1.

## Phase 2: structural changes

- [ ] **Service discovery without the pass-through for service names.** `AddAltinnServiceDefaults` already registers
      `AddServiceDiscovery()` and the factory clients' handler; what remains is a supported way to turn off the
      pass-through provider for single-label names, in the shared library (see the README's
      ["Across services"](README.md#across-services)).
  - Why: with the pass-through a service whose group is off is a DNS lookup and a connection error instead of an
    immediate "no endpoint"; localtest removes the internal factory by type name.
  - Size: small in code; needs agreement on the option, since production relies on the pass-through for DNS names.
  - Removes: `fakes/ServiceDiscovery/` and the `AddLocaltestServiceEndpointProviders` call in the hook.
  - Upstream: none found; see [`authorization-utils.md`](authorization-utils.md).
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **A supported local configuration without Key Vault for platform access token keys.** Production's
      `IPublicSigningKeyProvider` reads the issuers' certificates from Key Vault. Accept a certificate file (or a JWKS)
      for every issuer when no Key Vault is configured, the shape the README's
      ["Across services"](README.md#across-services) proposes for every service.
  - Why: without Key Vault no incoming platform access token validates.
  - Size: medium; part of the cross-service decision.
  - Removes: `fakes/Keys/` (`LocaltestPublicSigningKeyProvider`, with the metadata item above for the rest).
  - Upstream: related [altinn-auth#4050](https://github.com/Altinn/altinn-auth/issues/4050) (local configuration
    contract, platform tokens from Mockporten or a local JWKS) and
    [altinn-auth#4650](https://github.com/Altinn/altinn-auth/issues/4650) (secrets and the checked-in signing
    certificate fallback).
  - Related: [altinn-accesstoken#280](https://github.com/Altinn/altinn-accesstoken/pull/280) (open, by another contributor,
    2026-10-28) reads the issuers' public certificate from a file (`AccessTokenSettings:PublicSigningCertificatePath`)
    when no Key Vault is configured; released and bumped, it replaces `LocaltestPublicSigningKeyProvider`. It removes
    nothing yet. This host binds `AccessTokenSettings`, so the setting would take effect.
  - Detail: [`../components/authentication/README.md`](../components/authentication/README.md), "What the fakes
    replace".

- [ ] **Run on Postgres with registered OIDC clients seeded by the service.** Localtest keeps sessions, login
      transactions, codes, refresh tokens and the OIDC clients in memory because it has no supported way to put the
      clients into `oidcserver.client`. Upstream: a seed for registered clients in Development (ServiceDefaults'
      Npgsql seeding, which runs only in Development, or clients read from configuration). Localtest then runs the
      production migrations on the stack's shared Postgres (a `postgres-init` script for the roles and the database, as
      other components have) and drops the in-memory repositories.
  - Why: every restart signs everyone out, and the in-memory repositories mirror production's SQL by hand (a revoked
    refresh family, retention), which drifts with every upstream change.
  - Size: medium; the seeding format needs agreement, the rest is localtest work.
  - Removes: `fakes/Persistence/`, `fakes/LocaltestOidcClients.cs`, `fakes/LocaltestAuthenticationOptions.cs` (the
    clients and the Profile endpoint, which becomes a setting in the manifest), the Npgsql health-check removal in
    the hook, and, once the items above have landed and the configuration defaults have moved to the manifest, the
    hook itself: site `AuthenticationHost.cs:293-295`.
  - Upstream: related [altinn-auth#4062](https://github.com/Altinn/altinn-auth/issues/4062) (own database, system
    register seed) and [altinn-auth#4054](https://github.com/Altinn/altinn-auth/issues/4054) (test data and seeding).
  - Detail: [`../components/authentication/README.md`](../components/authentication/README.md), "As an OpenID Connect
    provider"; [`../../planned-components/authentication/production-authentication-analysis.md`](../../planned-components/authentication/production-authentication-analysis.md),
    the Postgres row.

- [ ] **Publish the image where localtest can pull it.** `src/apps/Altinn.Authentication/docker-bake.hcl` has an empty
      `default` group and an inline placeholder Dockerfile, so CI (`repoctl vertical containers build --push`) pushes no
      Authentication image; production images are built in Azure DevOps. Add `src/apps/Altinn.Authentication/containers/Dockerfile.api`
      and put the target in the default group, as
      [altinn-auth#4739](https://github.com/Altinn/altinn-auth/pull/4739) did for Authorization, so
      `ghcr.io/altinn/altinn-auth/authentication-api:<sha>` is pushed. The Authorization and Access Management images
      there pull anonymously (checked 2026-10-27).
  - Why: once the items above are merged, upstream's image runs locally unchanged, but there is no image to pull.
  - Size: small in code; a release decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the vendored copy.
  - Upstream: related [altinn-auth#4722](https://github.com/Altinn/altinn-auth/issues/4722) (the Authentication
    verticals in the monorepo's shared setup).

## Stays local

- `fakes/LocaltestAuthenticationExtensions.cs`, configuration defaults: the fake ID-porten and Maskinporten endpoints,
  the service names, the pepper and the at22 values it overrides: deployment settings; they move to the manifest
  when the hook goes.
- `fakes/Login/`: the level table for the fake ID-porten (`OidcProviders:idporten:AuthLevels`), configuration for a
  test-only stand-in of an external system; it moves to the manifest with the rest.
- `Dockerfile`: `COPY vendor/...` and the `/src` layout with `DirectoryBuildPropsPath` and `DirectoryBuildTargetsPath`:
  the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://+:5101`: deployment settings; with a
  published image they move to the manifest. Development is also what the http items above key on.
- `.dockerignore`: build output and the Dockerfile kept out of the context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the documentation `NoWarn` and the
  vendored-project test: the mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.

Coverage: 2 sites, 8 fakes (`LocaltestAuthenticationExtensions.cs`, `LocaltestAuthenticationOptions.cs`,
`LocaltestOidcClients.cs`, `Login/`, `Keys/`, `Persistence/`, `Cookies/`, `ServiceDiscovery/`), 2 build
overrides, 2 Dockerfile and 1 `.dockerignore` deviations: all accounted for. `Otlp/` and its `OTEL_SERVICE_NAME` item
went in task 61 ([`../../TODO.md`](../../TODO.md)): the host's own name is the manifest name, so nothing was left to remove.
