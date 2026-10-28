# authentication

Production Altinn Authentication (`vendor/`, see `UPSTREAM.md`) with Postgres replaced by memory and Key Vault by the
shared test certificate. Everything that stands in for a system outside Authentication lives in the localtest app
(`../../localtest/`): the fake ID-porten with the test-user picker, the fake Maskinporten, and the old localtest's
start page and `/Home` test helpers. Authentication reaches them by configuration and runs its own OIDC client, token
validation, Profile client, session handling and token exchanges unchanged. It is also an OpenID Connect provider for
registered clients (authorization code with PKCE, refresh, logout), with two clients seeded: see
[As an OpenID Connect provider](#as-an-openid-connect-provider).

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `#if` sites (`git diff` against the baseline commit shows them); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): configuration and DI composition (`LocaltestAuthenticationExtensions.cs`, `LocaltestAuthenticationOptions.cs`), the seeded OIDC clients (`LocaltestOidcClients.cs`), key providers (`Keys/`), the level table (`Login/LocaltestAuthLevels.cs`), in-memory repositories (`Persistence/`), cookies without `Secure` on http (`Cookies/`, a copy of `../shared/fakes/Cookies/`) |
| `smoke-test.sh` | Against the running stack: the browser sign-in, and the OIDC provider for the seeded clients (see [Verified](#verified)) |
| `Dockerfile` | Mirrors the upstream `vendor/src/apps/Altinn.Authentication/Dockerfile` (same pinned Alpine images, publish, non-root `dotnet` user); each difference is marked `localtest:`. Listens on 5101, the port the stack uses |

## Directive sites in `vendor/`

Service discovery (task 25) needs no site: `AltinnHost.CreateWebApplicationBuilder` already registers
`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(... AddServiceDiscovery())` through `AddAltinnServiceDefaults`
([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)). Since task 40
`AddLocaltestAuthentication` removes its pass-through provider (`LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders`,
`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`; ServiceDefaults has no setting for it): a service name without
an entry fails at once, a real host (the public origin, altinncdn.no, a client's logout URL) is used as it is.

1. `AuthenticationHost.cs`: `AddLocaltestAuthentication(builder)` just before the persistence layer. Its settings
   are read eagerly there and its repositories use TryAdd, so the in-memory ones registered first win.
2. `AuthenticationController.cs`, `IsSafeSameOrSubdomainHttps`: the `goto` guard accepts plain http as well,
   because localtest serves `local.altinn.cloud` without TLS.

**Cookies without `Secure` (task 21b): no site, reported here first.** Production hard-codes `Secure = true` on every
cookie it writes: `AltinnPartyId`, `AltinnPartyUuid`, `AltinnStudioRuntime` and `altinnsession` at sign-in and on
re-issue (`OidcServerService.cs`, `AuthenticationController.cs`), their expiries at logout and on a failed refresh, and
the deletions of `AltinnLogoutInfo` and `AltinnStudioRuntime` (`LogoutController.cs`); twelve places in four files. The
stack is plain http, where a browser refuses a `Secure` cookie, so the front doors used to remove the flag. Instead of
twelve `#if !LOCALTEST` blocks, `fakes/Cookies/LocaltestCookiePolicy.cs` (a copy of `../shared/fakes/Cookies/`) adds
ASP.NET Core's cookie policy middleware in front of the host's pipeline through an `IStartupFilter`, registered from
`AddLocaltestAuthentication`: on a request that is not https every cookie appended or deleted loses `Secure`, and a
`SameSite=None` one becomes `Lax` (none of Authentication's is `None`). On https nothing changes. The antiforgery cookie
`AS-XSRF-TOKEN` was never `Secure`. Inventory and decision: [`../../reviews/cookies.md`](../../reviews/cookies.md).

## What the fakes replace

| Production | Localtest |
| --- | --- |
| Postgres: sessions, upstream login transactions, unregistered client requests | `Persistence/`: in memory, so a restart signs everyone out |
| Postgres: the OIDC provider's clients, login transactions, authorization codes, refresh tokens | `Persistence/`: in memory, same semantics (below); the clients are seeded from configuration |
| Postgres health check | Removed from the health checks; the service's own stays |
| Key Vault: keys for incoming platform access tokens | `Keys/LocaltestKeyProviders.cs`, `LocaltestPublicSigningKeyProvider`: the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`) for every issuer |
| `SigningKeysRetriever` (provider metadata and JWKS over https only, cached 12 h) | `LocaltestSigningKeysRetriever`: over plain http, no cache, so a restarted localtest app's new keys are picked up at once |
| Hard-coded `Secure` on every cookie it writes | `Cookies/LocaltestCookiePolicy.cs`: on plain http the cookie policy middleware removes it (above, "Directive sites") |

Everything else is configuration (`fakes/LocaltestAuthenticationExtensions.cs`):

- The `idporten` OIDC provider points at the localtest app's fake ID-porten (`localtest/api/idporten/`): issuer and
  authorize on the public origin, token and metadata (and the exchange's ID-porten metadata) by service name,
  `http://localtest/localtest/api/idporten/…` (task 40; the callback's issuer is checked against the configured one). The level table `OidcProviders:idporten:AuthLevels` comes from
  `Login/LocaltestAuthLevels.cs`; the localtest app's picker has the same table (`IdPorten/AuthLevels.cs`).
- `MaskinportenWellKnownConfigEndpoint` (and the alternative) point at the fake Maskinporten
  (`localtest/api/maskinporten/.well-known/oauth-authorization-server`). The exchange accepts a token when that URL
  contains its `iss`, so the public URL is used.
- `PlatformSettings:ApiProfileEndpoint` points at the profile component (production Profile, `http://profile/profile/api/v1/`),
  which answers `internal/user` (`Localtest:Authentication:ProfileEndpoint` overrides it). Until task 14 the register component
  answered it.
- Everything derived from `GeneralSettings:PlatformEndpoint` is forced (the vendored `appsettings.json` carries at22
  values); `PlatformEndpoint` itself comes from the environment. The calls to other services are forced to service
  names (task 40, replacing `Localtest:Authentication:InternalPlatformEndpoint`): Notifications, Profile, the Resource
  Registry, the Register (v1 and v2 internal), the PDP and Access Management (`AccessManagementSettings:ApiAccessManagementEndpoint`,
  at22 upstream) as `http://<service>/<prefix>/…`; its own OpenID metadata for the JwtCookie handler stays public.
- `GeneralSettings:OidcRefreshTokenPepper` is `bG9jYWx0ZXN0LXBlcHBlcg==`, base64 of `localtest-pepper`: production
  decodes it (`Convert.FromBase64String`) to hash client secrets and refresh tokens. (It was the plain string until
  2026-10-20, which made every client-secret check answer `invalid_client` "Server configuration error".)
- `OidcProviders:idporten:LogoutEndpoint` is the fake ID-porten's `endsession`, where `openid/logout` sends the browser.
- `Localtest:Authentication:OidcClients` seeds the registered clients (`LocaltestOidcClients.cs`).
- No database, no migrations, no audit log, no Key Vault.

ServiceDefaults runs in environment `UNKNOWN`: the defaults are added after `AltinnHost` read its own settings, and
`LOCAL` is not wanted (it would fetch platform tokens from the remote test token generator).

## A sign-in

1. The app sends the browser to production `authentication?goTo=…`, which redirects to the fake ID-porten's
   `authorize` with state, nonce and an S256 PKCE challenge.
2. The picker (each test user's own party, level 0-4) answers with a one-time code to production `upstream/callback`.
3. Production `OidcProviderService` redeems the code at the fake's token endpoint with the verifier; production
   `UpstreamTokenValidator` checks the id and access tokens against the fake's JWKS (signature, issuer, lifetime,
   nonce).
4. Production `GetUserFromToken`, the Profile lookup, session creation, the cookie JWT and the cookies follow. Persons
   sign in by `pid`; a self-identified test user's token carries `urn:altinn:userid`, which takes production's
   Profile-by-userId branch.

The old localtest's `/Home/LogInTestUser` and `/Home/GetTestUserToken` run the same steps over HTTP from the localtest
app, which posts the picker for the chosen user and rewrites the party cookies to the selected party.

Authentication levels map to acr values from production's built-in ID-porten table (`OidcAuthLevelDefaults.IdPorten`);
production claim shaping turns the acr into `urn:altinn:authlevel`:

| Level | acr | `SecurityLevel` |
| --- | --- | --- |
| 0 | `level0` | `SelfIdentifed` |
| 1 | `level1` | `NotSensitive` |
| 2 | `level2` | `QuiteSensitive` |
| 3 | `idporten-loa-substantial` | `Sensitive` |
| 4 | `idporten-loa-high` | `VerySensitive` |

Cookies are `Secure` in production; on the stack's plain http they come without it (the cookie policy in `fakes/Cookies/`,
see "Directive sites"), so a browser keeps them and the front door passes them unchanged.

## As an OpenID Connect provider

Production's OIDC server code (`OidcFrontChannelController`, `OidcTokenController`, `OidcServerService`,
`TokenService`) runs unchanged; the four Postgres repositories it needs beyond the sign-in's are in memory
(`fakes/Persistence/`), mirroring production's SQL:

| Repository | Production table | In memory |
| --- | --- | --- |
| `InMemoryOidcServerClientRepository` | `oidcserver.client` (rows inserted by operations) | Seeded from `Localtest:Authentication:OidcClients` at start; the plain secret is hashed with production's `ClientSecretHasher.HashHmac` and the decoded pepper, so production's verifier runs and the legacy-hash upgrade (`TryUpgradeClientSecretHashAsync`, implemented) never fires for a seeded client. `InsertClientAsync` adds one at run time |
| `InMemoryLoginTransactionRepository` | `oidcserver.login_transaction` | A registered client's `authorize` request (`RequestId` a v7 GUID, `pending`, the service's 10-minute expiry); returned whatever its status, as production does |
| `InMemoryAuthorizationCodeRepository` | `oidcserver.authorization_code` | `GetAsync` finds unused, unexpired codes only; `TryConsumeAsync` is one conditional update under a lock (same client, same `redirect_uri` string, unused, unexpired); `GetClientsPartOfSession` reads every code of the session, which logout uses to notify clients |
| `InMemoryRefreshTokenRepository` | `oidcserver.refresh_token_family`, `oidcserver.refresh_token` | One family per (client, subject, session); lookup by the HMAC key; rotation marks the old token `used`; revoking touches `active`/`rotated` tokens; `GetFamiliesByOpSidAsync` lists the unrevoked families of a session |

Rows are dropped a day after they expire (production keeps them or leaves them to sweeps).

### Seeded clients

All three are confidential, `client_secret_basic`, PKCE S256. The first two may request `openid digdir:dialogporten.noconsent
altinn:portal/enduser altinn:correspondence.forward.check`, `correspondence-localtest` `openid profile`, what Correspondence's
challenge asks for (lower-case: production compares requested scopes as sent, and every requested scope must be allowed).
The access token's `scope` is the session's whatever the client asked for (task 2's finding), which is how
Correspondence's token passes its download policy (`altinn:portal/enduser` or `altinn:correspondence.read` for an
Altinn-issued token).

| Client | Secret | Redirect URI | Post-logout / front-channel logout | For |
| --- | --- | --- | --- | --- |
| `localtest-smoke` | `localtest-smoke-secret` | `{PlatformEndpoint}localtest/api/authentication/callback` | `{PlatformEndpoint}` / – | The localtest app's headless code-flow helper (`localtest/api/authentication/`, see `../../localtest/README.md`), tests |
| `arbeidsflate-localtest` | `arbeidsflate-localtest-secret` | `http://af.local.altinn.cloud:8000/api/cb` | `http://af.local.altinn.cloud:8000/` / `…/api/frontchannel-logout` | Arbeidsflate's BFF (`../../../planned-components/dialogporten/arbeidsflate.md` section 3.4) |
| `correspondence-localtest` | `correspondence-localtest-secret` | `{PlatformEndpoint}correspondence/api/v1/idporten-callback` | – / – | Correspondence's own OIDC client: an attachment link clicked in the inbox carries no token, and Correspondence challenges this client and validates the access token itself (`../correspondence/README.md`, task 11b). With a session (the `AltinnStudioRuntime` cookie) `authorize` answers with a code at once, no picker |

Override or add with `Localtest__Authentication__OidcClients__{n}__{Field}` (fields as in
`LocaltestOidcClient`: `ClientId`, `ClientSecret`, `RedirectUris__{i}`, `AllowedScopes__{i}`, `FrontchannelLogoutUri`,
…). The localtest app's helper keeps the same ids, secrets and redirect URIs for the clients it acts as
(`Authentication/SeededClients.cs`: the first two; `correspondence-localtest` is only ever Correspondence's).

### The flow

1. `GET authentication/api/v1/authorize` with `response_type=code`, `client_id`, the exact `redirect_uri`, scopes
   including `openid`, `state`, `nonce`, an S256 `code_challenge` and optionally `acr_values`: production validates
   the client binding, stores the login transaction and redirects to the fake ID-porten's picker. (Without a live
   session it adds `selfregistered-email` to the upstream `acr_values`; the picker ignores values it does not know.)
2. The picker answers production `upstream/callback`, which signs the user in exactly as the `goTo` sign-in does (same
   session, same four cookies) and then redirects to the client's `redirect_uri` with `code` and `state` (code lifetime
   120 s).
3. `POST authentication/api/v1/token` with `Authorization: Basic`, `grant_type=authorization_code`, `code`,
   `redirect_uri`, `code_verifier`: `access_token`, `id_token` (`aud` = client, `nonce`, `sid`), `token_type`,
   `expires_in` 600, `scope`, `refresh_token`, `refresh_token_expires_in` 1800.
4. `grant_type=refresh_token` rotates: a new refresh token; the old one presented again revokes the whole family.
5. `GET authentication/api/v1/openid/logout` deletes the session found through the `AltinnStudioRuntime` cookie, revokes
   its refresh-token families, calls each session client's front-channel logout URI server-side, and redirects the
   browser to the fake ID-porten's `endsession` with `post_logout_redirect_uri=…/logout/handleloggedout`, which sends it
   back there, and `handleloggedout` on to the platform's start page.

**`pid`.** The code-flow access and id tokens carry `pid` (the user's national identity number): the session's
`ExternalId` is `urn:altinn:person:identifier-no:{pid}` from the fake ID-porten's token, and `ClaimsPrincipalBuilder`
writes it for the code flow. The cookie JWT (`AltinnStudioRuntime`, `/Home/GetTestUserToken`) has none, by production
design (`isAuthCookie`). Dialogporten's end-user API identifies a person by `pid`, so it needs the code-flow token.

### Differences from production and known gaps

- **Logout needs the session cookie.** Production reads the session id from the `AltinnStudioRuntime` cookie; an
  `id_token_hint` it issued itself is not used (`ChooseProviderByIssuer` knows only the upstream issuers, logs "Invalid
  id_token_hint" and goes on). A logout with the hint alone answers 302 to the platform and ends nothing. A browser
  sends the cookie; scripts take it from the helper (which sets it as `/Home/GetTestUserToken` does).
- **`post_logout_redirect_uri` is not honoured.** With a session the redirect is always the upstream logout; production
  never reads the client's post-logout URIs (its repository does not even map them). They are seeded for completeness.
- **Front-channel logout is server-side**, so `authentication` must resolve the client's host: `af.local.altinn.cloud`
  is an ingress host of studioctl's topology since task 11, which every container maps to the host gateway. A failed
  call is logged and logout continues.
- **A revoked family is never replaced** for the same (client, subject, session): production's unique constraint keeps
  it and `GetOrCreateFamilyAsync` throws, so after reuse detection a code issued from the same session (browser session
  reuse at `authorize`) fails at the token endpoint with a 500. Mirrored, not fixed.
- Production's `RequirePkce`, `RequireNonce` and `SubjectType` are constants in its repository and unused by the
  service; the seeded values are informational.
- The fake ID-porten keeps no session, so its `endsession` ends nothing; the next `authorize` shows the picker again.

## Telemetry (task 53)

The stack's rule: traces, metrics and logs over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with
`OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_SERVICE_NAME`; nothing otherwise. Production code already follows the first
half: `AltinnHost.CreateWebApplicationBuilder("authentication", ...)` runs `AddAltinnServiceDefaults`, which sets up logs,
metrics (runtime, ASP.NET Core, Kestrel, HttpClient) and traces (ASP.NET Core without health paths, HttpClient,
tail sampling), Npgsql tracing through `AddAltinnPostgresDataSource`, Authentication's own source and meters and adds the OTLP exporter for each signal when the variable is set (Application Insights only
without it, and only with a connection string, which localtest never has). Its resource detector sets
`service.name` `authentication` after the SDK's environment detector, so `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` are
ignored and `service.instance.id` is the container's host name. Both runtimes set `OTEL_SERVICE_NAME` to that same
name (the manifest name), so localtest adds nothing and no vendored file changes. Until task 61 (2026-10-28) the fakes'
hook called `AddLocaltestOtlpResource` (a copy of `../shared/fakes/Otlp/`) to let the variables win; it changed no
name under studioctl or Aspire, so it was removed ([`TODO.md`](../../../TODO.md) section 61). `AddLocaltestOtlp` was never an option:
`UseOtlpExporter()` throws next to ServiceDefaults' own exporters.

Verified without the call (2026-10-28, task 61, a local image, `env up -r localtest-v2 --with monitoring`): `smoke-test.sh` 33 `ok` with its OTLP check;
under `service.name` `authentication` and `service.instance.id` the container's host name, 308 spans, 670 metric series, 31 log records in VictoriaTraces,
VictoriaMetrics and VictoriaLogs (evidence `/home/agent/code/.handover/otlp-resource-workaround-removal/`).

Verified 2026-10-26, with the call, without the stack (the host run as a process with `ASPNETCORE_ENVIRONMENT=Development`, a fake OTLP/HTTP collector on a loopback
port, `http/protobuf`): with the variable and `OTEL_SERVICE_NAME=from-env`, `/v1/traces`, `/v1/metrics` and `/v1/logs`
arrive with `service.name` `from-env`; with the variable alone, `authentication`; without it nothing reaches the collector or the
SDK defaults (4317, 4318). Verified on the stack (2026-10-26, task 53's stack proof, images `sha-609c646`, `studioctl env up -r localtest-v2 --with everything`): `smoke-test.sh` passed with its OTLP check, and the stack's run left 566 spans, 724 metric series and 128 log records under `service.name` `authentication`. Without the monitoring group the container has no `OTEL_*` variable and its log no exporter line. A 150 s capture of SYNs and UDP in its network namespace during the smoke tests showed no lookup of `otel-collector` and nothing to 4317 or 4318.

## Verified

The localtest app's `smoke-test.sh` (sections `identity providers` and `home`) and the root `smoke-test.sh` cover the
sign-in at every level, the party cookies, `refresh`, the org token through the fake Maskinporten and the production
exchange, and the Studio token.

`./smoke-test.sh [BASE]` (this folder) covers the browser sign-in and the OIDC provider: both discovery documents list
`end_session_endpoint`; Arbeidsflate's exact `authorize` through the picker to its `redirect_uri` and the token
exchange with its secret and verifier; the helper's tokens for user 1337 (`pid` equal to the Register's national
identity number, `nonce`, `sid`, `aud`); `invalid_client` for a wrong secret, `invalid_grant` for a spent code;
refresh rotation and reuse detection; logout through `endsession` and `handleloggedout`, after which the session's
refresh token is refused; and, with the dialogporten group up, Dialogporten's end-user dialog list with the code-flow
access token (the cookie JWT, without `pid`, is refused with 403). Since task 21b also the sign-in's callback straight on
Authentication (`authentication:5101`, from inside the stack's network since task 27a): its four cookies without `Secure`, and the same attributes through the front door. That
is 34 checks with the group and 32 without it, where the last two are skipped.

On the stack (2026-10-20, `env up -r localtest-v2 --with dialogporten --dev authentication --dev localtest`): 32 ok,
none failing. User 1337's access token carries `pid` `01839012345`, the Register's national identity number for
Sophie Salt; `GET dialogporten/api/v1/enduser/dialogs?party=urn:altinn:person:identifier-no:01839012345` answers 200
with her dialogs (without `party` or a service resource it is a 400 validation error, not an authorization one).
Regressions on the same stack: root `smoke-test.sh` 23 ok; the localtest app's smoke test with `PROXY_CHECKS=0` 64 ok,
every `identity providers` and `home` check passing; `run-platform-regression.sh --no-apps` 44 tests, all specs
passed.

2026-10-21 (task 14, production Profile from the profile component answering `internal/user`): 30 ok on the default
stack and 32 with `--with everything` (the other two need Dialogporten), as recorded in `../profile/README.md` and
`../../README.md`.

2026-10-21 (task 11b, the third seeded client, `--with arbeidsflate --with correspondence --dev authentication --dev
correspondence`): this test 32 ok; `correspondence-localtest` exercised by Correspondence's smoke test (its 12 browser-download
checks: `authorize` with the session cookie answers a code at once, the token exchange from inside Correspondence, a code
of `localtest-smoke` refused with `invalid_grant` "Code not issued to this client"); the platform regression 44 of 44 on
the default stack with `--dev authentication`. Scratch `dotnet build -c Release` of the host: 0 errors, no warning in `fakes/`.

## Run a changed component

The stack runs through studioctl's `localtest-v2` runtime (see `../../README.md`), which pulls this component's image
from ghcr. To run a change made here instead, build it from this checkout:

```sh
cd localtest/components/authentication && studioctl env up -r localtest-v2 --dev-authentication
```

`--dev-<container>[=<path>]` builds the image from the Dockerfile at the path or in the directory (default: the current
one). The front door cannot route the component's prefix to an instance running in an IDE; `--dev` rebuilds the image.
