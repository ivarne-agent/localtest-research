# Plan: fake ID-porten, fake Maskinporten and the /Home contract move into the localtest app

Status: step 1 done 2026-10-20 (fake ID-porten in the localtest app; Authentication's own OIDC client and token
validation run); steps 2-4 open. Approved by the user 2026-10-19, with "as much production code as possible, still no
Service Bus or other Azure services". Builds on the review of `localtest/components/authentication/` (same day) and
[`production-authentication-analysis.md`](production-authentication-analysis.md).

## Goal

Authentication keeps only what replaces *its own* infrastructure (Postgres, Key Vault, https). Everything that stands
in for a system *outside* Authentication moves to the localtest app (`localtest/localtest/`, the front door), as
features next to `Proxy/`:

| Feature | Stands in for | Replaces in `components/authentication/fakes/` |
| --- | --- | --- |
| `IdPorten/` | ID-porten: a real OIDC authorization-code provider with the test-user picker as its login page | `Identity/LocaltestOidcProvider.cs`, `Identity/LocaltestUpstreamTokenValidator.cs`, `Identity/LocaltestTicketStore.cs`, `Login/LocaltestLoginPage.cs`, `Login/LocaltestUserSelect.cs`, `Controllers/LocaltestLoginController.cs` |
| `Maskinporten/` | Maskinporten: metadata, key set, and a localtest-only way to mint tokens | `Controllers/LocaltestMaskinportenController.cs`, the Maskinporten half of `Keys/LocaltestSigningKeys.cs` |
| `Home/` | The old localtest's test helpers (`/`, `/Home`, `LogInTestUser`, `GetTestUserToken`, `GetTestOrgToken`, `GetTestStudioToken`, `Localtest/Version`) | `Controllers/LocaltestHomeController.cs`, `Controllers/LocaltestTokenController.cs`, `Login/LocaltestSignIn.cs`, `Identity/RegisterLocaltestClient.cs`, `LocaltestAccessTokenSigner` |

What stays in Authentication: the three in-memory repositories, `LocaltestSigningKeysRetriever` (metadata over plain
http; production's `HttpDocumentRetriever` requires https), `LocaltestPublicSigningKeyProvider` (Key Vault), the
auth-level configuration default, the setup code, and the two directive sites (host hook, `goTo` over http). Roughly
8 of today's 18 fake files.

## Why now, and what changed since 2026-10-17

The analysis of 2026-10-17 (§6) looked at an own OIDC server and chose the in-place override instead, on the
coordinator's request, because a separate provider meant "the extra hop, the extra service and the keys". Two things
changed:

- **The extra service exists anyway.** The localtest app is the front door since 2026-10-19 and is meant to host
  localtest features that are not platform components (its README). A fake ID-porten adds a feature, not a container.
- **The override skips production code that matters.** Today the fake `IOidcProvider` hands back an *unsigned*
  id_token and the fake `IUpstreamTokenValidator` reads it with a nonce check only. Production's token request (form
  POST with PKCE verifier and client authentication) and production's validation (signature against the provider's
  JWKS, exact issuer, lifetime, nonce) never run locally. With a real-protocol fake they all do.

Mockporten (`altinn-auth/src/apps/Altinn.Mockporten`) is still unfit, for the reasons in §6 of the analysis: Tenor-only
pids, Key Vault-only signing, https redirection, no picker.

This reverses a recorded decision, so it needs the user's go before step 1.

## Design

### `IdPorten/` (in the localtest app)

Endpoints under `localtest/api/idporten/`, issuer `{platform}localtest/api/idporten` (no trailing slash; production
validation is exact for id_tokens when `StrictIdTokenValidation` is on):

| Endpoint | What |
| --- | --- |
| `GET .well-known/openid-configuration` | issuer, `authorization_endpoint`, `token_endpoint`, `jwks_uri`, `code_challenge_methods_supported: [S256]` |
| `GET jwks` | The RS256 public key |
| `GET authorize` | The picker page (today's `LocaltestLoginPage` markup and form field names), keeping `state`, `nonce`, `redirect_uri`, `code_challenge`, `acr_values` |
| `POST authorize` | Picked user and level → one-time code (60 s, single use) bound to the PKCE challenge, nonce and redirect URI → `302 {redirect_uri}?code&state` |
| `POST token` | `grant_type=authorization_code`: checks code, redirect URI and `code_verifier` (S256); returns `id_token` and `access_token`, signed RS256, 5 minutes |

id_token claims as today's fake (`sub`, `pid` or `urn:altinn:userid` for self-identified users, `acr`, `amr`, `sid`,
`auth_time`, `nonce`, `actual_iss`) plus `iss`, `aud` (client id), `iat`, `exp`, `kid` in the header.

Authentication changes by configuration only: `OidcProviders:idporten:{Issuer, AuthorizationEndpoint, TokenEndpoint,
WellKnownConfigEndpoint}` point at the feature; `IdPortenWellKnownConfigEndpoint` too. `IOidcProvider` and
`IUpstreamTokenValidator` go back to production.

**Signing key.** Generated at start, in memory. Production's retriever caches metadata and keys for 12 h (the
`ConfigurationManager` in `ConfigurationMangerHelper`), which would break sign-in after a restart of the localtest
app, but localtest replaces it: `LocaltestSigningKeysRetriever` fetches metadata and JWKS on every call (checked
2026-10-19). Tokens are short-lived, so nothing else outlives a restart. If that retriever ever gets a cache, persist
the key in the `localtest-data` volume instead.

### `Maskinporten/`

`GET localtest/api/maskinporten/.well-known/oauth-authorization-server` and `jwks`, key generated at start as above.
`LocaltestSigningKeysRetriever`'s in-process shortcut for the Maskinporten issuer goes; it fetches like any other.
Production `exchange/maskinporten` checks that the token's `iss` is contained in the configured well-known URL, so the
issuer is `{platform}localtest/api/maskinporten/`. `MaskinportenWellKnownConfigEndpoint` and its alternative point
here. Minting is an internal function of `Home/` (below), not a public token endpoint.

### `Home/`

The old localtest contract stays byte-compatible: routes, form field names (`UserSelect=userId.partyId`,
`AuthenticationLevel`, `AppPathSelection`, `RedirectUrl`, `action`), status codes, the `8` from `Localtest/Version`,
and the party-cookie rewrite. It is what the app-frontend Cypress suite, app-lib's `LocaltestValidation` and
developers use.

The difference is that sign-in becomes an HTTP client of Authentication, as the root `smoke-test.sh` already does with
curl:

1. `GET {authn}authentication?goTo={platform}` with a cookie jar → `302` to `IdPorten/authorize` with `state`, `nonce`,
   `code_challenge`.
2. Issue the code in-process for the chosen user and level (no HTTP to itself).
3. `GET {authn}upstream/callback?code&state` with the jar → Authentication's own cookies, the JWT among them.
4. Rewrite `AltinnPartyId` / `AltinnPartyUuid` to the selected party, append the cookies, redirect or return the JWT.

`GetTestOrgToken` mints a Maskinporten token with the `Maskinporten/` key and calls production `exchange/maskinporten`.
`GetTestStudioToken` signs a platform access token with the shared certificate (`localtest/certs/`, mounted into the
localtest app) and calls `exchange/altinnstudio`. Test users come from the register component's
`register/api/localtest/test-users`, as the picker reads them today.

Routing: `/`, `/Home` and `/Home/` become the localtest app's own endpoints instead of a platform route to
Authentication (`Proxy/PlatformRoutes.cs`).

### Native AOT constraints

- No MVC or Razor: the picker and start page are built as strings, as `LocaltestLoginPage` already does.
- JWT signing by hand: base64url header and payload from the source-generated `LocaltestJsonContext`, RS256 with
  `System.Security.Cryptography.RSA`. About 40 lines. Do not pull in `System.IdentityModel.Tokens.Jwt`; whether
  `Microsoft.IdentityModel.JsonWebTokens` publishes without AOT warnings is unverified.
- PFX loading for the Studio token: `X509CertificateLoader`, AOT-safe.
- The AOT publish is memory-hungry: build when no Cypress run is going (the machine has 7 GB).

## Steps

Each step ends with the authentication smoke test, the root smoke test and the localtest app smoke test passing; the
last step with a full Cypress run (all 13 app folders).

1. **`IdPorten/`** in the localtest app. Point Authentication's `idporten` provider at it by
   configuration; delete the fake provider, validator, ticket store, picker page and login controller from
   Authentication. `LocaltestSignIn` (still in Authentication for `/Home`) switches from the ticket store to the HTTP
   flow above, so `/Home` keeps working in between.
2. **`Maskinporten/`**, and switch `GetTestOrgToken` to it. Delete the Maskinporten controller and key.
3. **`Home/`** moves; the proxy serves `/`, `/Home` and `/Home/` itself. Delete the Home and token controllers,
   `LocaltestSignIn`, `RegisterLocaltestClient` and the access-token signer from Authentication; mount `certs/` into the
   localtest app.
4. Clean-up: READMEs (authentication, localtest app, root), the authentication smoke test split (the `/Home` contract
   checks move to the localtest app's smoke test), the planned-components index.

Estimate: two to three days including the Cypress run.

## Open questions

- **The level-to-acr table.** Authentication needs it as configuration (`OidcProviders:idporten:AuthLevels`, from
  `LocaltestAuthLevels`), and the picker and `LogInTestUser` need it to turn a level into an `acr`. Duplicate it with
  a pointer, or have the picker offer only what Authentication asked for in `acr_values`?
- **Picker URL.** It moves from `authentication/api/v1/localtest/login` to `localtest/api/idporten/authorize`. Nothing
  outside the stack links to it as far as found; keep a redirect from the old path for a while?
- **Ownership.** `Proxy/PlatformRoutes.cs` and the localtest app's README belong to the session that built the
  localtest app; coordinate step 3 with it.
