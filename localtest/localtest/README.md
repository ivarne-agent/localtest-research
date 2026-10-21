# localtest

The localtest app: the stack's front door, and the home of the localtest features that are not a platform component.
A small ASP.NET app published with Native AOT: one 16 MB binary on `runtime-deps`, a 34 MB image, no .NET runtime.
Each feature keeps its own code folder and its own API under `localtest/api/{feature}/`.

| Feature | Folder | API |
| --- | --- | --- |
| Proxy: one origin, `local.altinn.cloud:8000`, split by host name and path prefix onto the platform components and the apps the app runner has registered. Replaced the nginx ingress on 2026-10-19 with the same contract | `Proxy/` | `localtest/api/proxy/` |
| ID-porten: a fake OpenID Connect provider whose login page is the test-user picker; production Authentication signs in against it as against ID-porten | `IdPorten/` | `localtest/api/idporten/` |
| Maskinporten: discovery and JWKS that production Authentication's `exchange/maskinporten` trusts, and the minting `/Home/GetTestOrgToken` uses | `Maskinporten/` | `localtest/api/maskinporten/` |
| Home: the old localtest's start page and test helpers (`/`, `/Home/LogInTestUser`, `/Home/GetTest*Token`, `/Home/Localtest/Version`), which sign in and exchange tokens through production Authentication | `Home/` | `/`, `/Home/…` (the old contract) |
| Authentication: production Authentication's authorization-code flow for a seeded OIDC client, run headlessly; tokens with `pid`, refresh | `Authentication/` | `localtest/api/authentication/` |

| File | What |
| --- | --- |
| `Program.cs` | Composition: dedicated hosts first, then the app-frontend files, routing, the features' endpoints, the catch-all forwarder |
| `LocaltestSettings.cs` | Settings the features share: the public `Localtest:PlatformEndpoint` and the shared test certificate's path |
| `Tokens/` | `SigningKey.cs`: RS256 keys (generated, or the shared certificate's), JWKS and a hand-written JWT writer, used by the identity features; `TokenErrorResponse.cs`, the token endpoint error both fakes answer |
| `IdPorten/` | The fake ID-porten: `IdPortenApi.cs` (endpoints and picker), `AuthorizationCodes.cs`, `TestUsers.cs` (the register's test users, also used by Home), `AuthLevels.cs` |
| `Maskinporten/` | The fake Maskinporten: `MaskinportenApi.cs` (metadata, JWKS, token endpoint, minting) |
| `Home/` | `HomeApi.cs` (the old contract), `AuthenticationClient.cs` (the headless sign-in and the exchanges over HTTP; its upstream half is shared with `Authentication/`) |
| `Authentication/` | `AuthenticationApi.cs` (endpoints), `CodeFlow.cs` (the code flow over HTTP), `SeededClients.cs` (the seeded clients' ids, secrets and redirect URIs) |
| `LocaltestJsonContext.cs` | Source-generated JSON for every feature (Native AOT has no reflection serializer) |
| `Proxy/PlatformRoutes.cs` | The route table: dedicated hosts, exact paths and path prefixes, and the upstream addresses |
| `Proxy/AppRoutes.cs`, `Proxy/AppRoutesApi.cs` | The app routes and the API the app runner pushes them through |
| `Proxy/Forwarder.cs` | Resolves a request, forwards it with [YARP](https://github.com/dotnet/yarp)'s `IHttpForwarder` (the forwarding core the old localtest also uses), and the transform (original `Host`, cookies without `Secure`) |
| `Dockerfile` | Native AOT publish for the build machine's architecture (`linux-musl-arm64` or `-x64`) |
| `smoke-test.sh` | Against the running stack: the proxy's routing contract and its app route API, the fake ID-porten and Maskinporten, and the Home contract (sign-in at every level, party cookies, token helpers) |

## Proxy

### Routes

In order:

1. **Dedicated hosts** own every path, the localtest app's own endpoints included: `pdf.local.altinn.cloud` → `pdf3`,
   `workflow-engine.local.altinn.cloud` → `workflow-engine`.
2. **`/app-frontend/`**: the built app-frontend (`FRONTEND_DIST`, mounted at `/frontend`) as static files, with
   `Access-Control-Allow-Origin: *` and `Cache-Control: no-cache`. The apps' asset base.
3. **The localtest app's own endpoints**: `GET /health`, `localtest/api/…` (below), and the start page and `/Home/…`
   (Home).
4. **App routes**: `/{org}/{app}/…` → the registered upstream, path unchanged.
5. **Platform routes** on the main host, path unchanged: `/authentication/` → authentication; `/register/` → register; `/profile/` → profile; `/authorization/` → the PDP; `/accessmanagement/` → Access
   Management; `/storage/` → Storage; `/events/` → Events.
6. Anything else: `404` with a hint to start the app with `run-app.sh`.

A prefix requested without its trailing slash (`/storage`, `/ttd/app?x=1`) is redirected with `301` to the slashed
path, query kept, as nginx did. Matching is ordinal and case-sensitive, like the nginx locations.

Upstreams default to the container names of the compose file the stack used to run from (`register:5020`,
`profile:5030`, `authentication:5101`, `authorization:5070`, `access-management:5080`, `storage:5010`, `events:5080`, `pdf3:5031`,
`workflow-engine:9090`). Override one with `Proxy__Upstreams__<name>`. Behind studioctl's `localtest-v2` runtime
studioctl-server is the front door and this proxy is not used; studioctl sets `Proxy__Upstreams__register` and
`__authentication` to the public origin, because the identity features and `/Home` call those two. `GET
localtest/api/proxy/upstreams` lists the effective ones.

### What it does to a request

- **Streams** both ways, unbuffered: server-sent events (`events/api/localtest/stream`) and downloads arrive as the
  upstream writes them; WebSocket upgrades are passed on.
- **Headers**: YARP's default transform (all headers copied, `X-Forwarded-For`, `-Proto` and `-Host` set), plus the
  original `Host`, because the components and apps build their own links from it.
- **Cookies**: the `Secure` attribute is removed from every `Set-Cookie`, because the browser talks plain http to this
  origin while the production services set it (the nginx ingress did the same with `proxy_cookie_flags ~ nosecure`).
- **Limits**: request bodies up to 200 MB; an upstream may stay silent for 5 minutes (PDF generation, big uploads)
  before the request fails; connect timeout 5 seconds. A failed upstream is a `502` naming the route and address.
- Connections to an upstream are recycled every 30 seconds, so a recreated container's new address is picked up.

### App routes (`localtest/api/proxy/`, anonymous)

| Call | What |
| --- | --- |
| `PUT apps/{org}/{app}` with `{ "upstream": "http://host.docker.internal:5005" }` | Route `/{org}/{app}/…` to the app. Org and app are lower-case letters, digits and hyphens; the upstream is an http(s) origin without a path. Replaces an existing route |
| `DELETE apps/{org}/{app}` | Remove the route (`404` if there was none) |
| `GET apps` | Every route with its upstream and registration time |
| `GET upstreams` | The platform upstreams in effect |

`../run-app.sh` registers each app it starts; `../stop-apps.sh` removes them. Routes are saved to
`/data/app-routes.json` (`Proxy__AppRoutesFile`) on the `localtest-data` volume and read at start, so a recreated
container keeps routing the apps that still run. Nothing watches the file, and nothing reloads.

## ID-porten

A fake ID-porten for production Authentication (`components/authentication/`), whose `idporten` provider points here
by configuration. It replaced the in-process picker and fake token handling inside Authentication on 2026-10-19, so
Authentication's own OIDC client and token validation run (plan:
`../../planned-components/authentication/localtest-app-identity.md`).

| Endpoint (`localtest/api/idporten/`) | What |
| --- | --- |
| `GET .well-known/openid-configuration` | Issuer `{Localtest:PlatformEndpoint}localtest/api/idporten` (default `http://local.altinn.cloud:8000/…`, or `IdPorten:Issuer`), endpoints (`end_session_endpoint` included), `S256` |
| `GET jwks` | The RS256 public key, generated at start (tokens live 5 minutes; Authentication's localtest key retriever fetches the JWKS on every validation, so a restart only drops logins in flight) |
| `GET authorize` | The picker: every test user's own party (`UserSelect=userId.partyId`) and the level 0-4 (`AuthenticationLevel`), preselected from the highest known `acr_values`, else 3. Keeps `client_id`, `redirect_uri`, `state`, `nonce` and the PKCE challenge in hidden fields |
| `POST authorize` | The form: a one-time code (60 s) for the picked user, then `302 {redirect_uri}?code&state` |
| `POST token` | `authorization_code` with `redirect_uri`, `client_id` and the S256 `code_verifier` checked; answers a signed `id_token` (`aud` = client id, `nonce`, `auth_time`, `sid`) and `access_token` (`client_id`, `scope`) |
| `GET endsession` | RP-initiated logout, as ID-porten's: `302` to `post_logout_redirect_uri` (on the platform origin, else `400`) with `state` appended; without one a plain "signed out" page. `id_token_hint` and `client_id` are accepted and not checked. Production Authentication's `openid/logout` sends the browser here (`OidcProviders:idporten:LogoutEndpoint`) with its `logout/handleloggedout` as the redirect. The fake keeps no login session (codes are one-time), so nothing is ended |

Both tokens carry `iss`, a pairwise `sub`, `pid` (or `urn:altinn:userid` for a self-identified test user without a
national identity number), `acr` (level table in `IdPorten/AuthLevels.cs`, kept in step with Authentication's
`OidcProviders:idporten:AuthLevels`), `amr: ["TestID"]`, `actual_iss`, `jti`, `iat`, `nbf`, `exp`. JWTs are written
by hand (`Tokens/SigningKey.cs`): the IdentityModel token libraries are not verified for Native AOT. Test users
come from the register's `register/api/localtest/test-users`, read from its upstream directly.

## Maskinporten

A fake Maskinporten for production Authentication's `exchange/maskinporten`, whose `MaskinportenWellKnownConfigEndpoint`
points here. The exchange accepts a token when that URL contains the token's `iss`, so the issuer is
`{Localtest:PlatformEndpoint}localtest/api/maskinporten/` (or `Maskinporten:Issuer`).

| Endpoint (`localtest/api/maskinporten/`) | What |
| --- | --- |
| `GET .well-known/oauth-authorization-server` | Issuer, `jwks_uri`, `token_endpoint`, `token_endpoint_auth_methods_supported` (`private_key_jwt`), `jwt-bearer` |
| `GET jwks` | The RS256 public key, generated at start |
| `POST token` | Anonymous, `application/x-www-form-urlencoded`: `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer` (else `400 unsupported_grant_type`) and `assertion`, a compact JWT. Answers `200 {access_token, token_type: "Bearer", expires_in: 1800, scope}` |

The token endpoint reads only the assertion's payload: the header and signature are ignored (an unsigned `alg: none`
assertion is accepted) and `exp` and `aud` are not checked. A missing or undecodable assertion, or one without `iss`,
is `400 invalid_request`. The token's `client_id` is `iss`, its `scope` the assertion's `scope` (echoed, empty when
absent). The organisation number, which becomes `consumer` (`0192:{orgNumber}`), is the first that applies:

1. `consumer_org` in the assertion;
2. `iss` when it is exactly nine digits (the client id is the organisation number);
3. 991825827 (Digdir), which production `exchange/maskinporten?test=true` turns into `ttd`.

There is no client table and no client is refused; one Information line names the client id and the rule that applied.
`/Home/GetTestOrgToken` mints in-process with the same `MaskinportenApi.Mint` (`client_id` `localtest`): `iss`, `scope`,
`client_id`, `consumer`, `jti`, `iat`, `nbf`, `exp` (30 minutes).

## Home

The old localtest's login contract, which the app-frontend Cypress suite, app-lib's `LocaltestValidation`, scripts
and developers use. It moved here from Authentication's fakes on 2026-10-20, unchanged. Nothing is minted as an Altinn
token: user tokens come from production Authentication's sign-in against the fake ID-porten, org and Studio tokens from
its production exchanges. Calls to Authentication go straight to its upstream with the public `Host`.

| Endpoint | Behaviour |
| --- | --- |
| `GET /`, `/Home`, `/Home/Index` | Start page: `<select id="UserSelect">` with `userId.partyId` options (`Name`, and `Name for Org` for each party in the user's register `parties`), `AuthenticationLevel` 0-4 (default 2), optional `AppPathSelection`; `?goto=` becomes `RedirectUrl` |
| `POST /Home/LogInTestUser` | Form or JSON: `UserSelect`, `AuthenticationLevel` (-1 skips the login), `action`, `RedirectUrl`, `AppPathSelection`. Runs the production sign-in headlessly (production `authentication?goTo=`, a code from the fake ID-porten for the chosen user, production `upstream/callback`), appends its cookies with `AltinnPartyId`/`AltinnPartyUuid` replaced by the selected party and `Secure` removed (the JWT keeps the user's own party, as in production). `action=reauthenticate` answers 204; otherwise redirects to `RedirectUrl` (same origin only), else `/{AppPathSelection}/`, else `/` |
| `GET /Home/Localtest/Version` | `8` (`application/json`), the version `LocaltestValidation` in app-lib probes |
| `GET /Home/GetTestUserToken[/{userId}]?userId&partyId&authenticationLevel` | Same headless sign-in; returns the JWT and sets the cookies (party cookies on `partyId` when given). Default level 2 |
| `GET /Home/GetTestOrgToken[/{org}]?org&orgNumber&authenticationLevel&scopes` | A fake Maskinporten token for `orgNumber` (default 991825827, Digdir) and `scopes` (default the service-owner instance scopes), exchanged through production `exchange/maskinporten?test=true`, which answers level 3 and turns Digdir into `ttd`. `org` and `authenticationLevel` are accepted and ignored, as before |
| `GET /Home/GetTestStudioToken` | Designer's credential: a platform access token with issuer `studio` and app `studio.designer`, signed with the shared test certificate (`Localtest:AccessTokenCertificatePath`, `/certs` mounted from `../certs`), exchanged through production `exchange/altinnstudio`. Carries `urn:altinn:app=studio.designer`, which Storage's and Authorization's `StudioDesignerAccess` policy requires for pushing metadata, texts and policies |

Production `exchange/maskinporten` looks the organisation code up in `https://altinncdn.no/orgs/altinn-orgs.json`, so a
service-owner org token needs internet access from the authentication container.

## Authentication

Production Authentication is also an OpenID Connect provider for registered clients
(`../components/authentication/README.md`, "As an OpenID Connect provider"). This feature runs that flow without a
browser for the two seeded clients, `localtest-smoke` (default) and `arbeidsflate-localtest`, acting as the client with
its seeded secret (`SeededClients.cs`, kept in step with `components/authentication/fakes/LocaltestOidcClients.cs`):

1. production `authorize` with the client's redirect URI, the four scopes (or `scope`), `state`, `nonce`, an S256 PKCE
   challenge and the level's `acr_values`;
2. the redirect to the fake ID-porten is answered for the chosen user without the picker, and production
   `upstream/callback` is called (the same steps `/Home` uses, `AuthenticationClient.SignInUpstream`);
3. the callback's `302` to the client's `redirect_uri` is read, not followed, for `code` and `state` (checked);
4. production `token` with `client_secret_basic`, the code and the verifier.

| Endpoint (`localtest/api/authentication/`, anonymous) | What |
| --- | --- |
| `GET token?client&userId&partyId&authenticationLevel&scope` | The whole flow; production's token response (`access_token`, `id_token`, `token_type`, `expires_in`, `scope`, `refresh_token`, `refresh_token_expires_in`) plus `pid`, the access token's `pid` claim (null when absent). Defaults as `/Home/GetTestUserToken`: `userId` required, level 2, the user's own party. Sets the session cookies production's callback set (`Secure` removed, party cookies on `partyId`), so an `openid/logout` with them ends that session. Production's error status and body are passed through; a failure before the token call is a `502` problem naming the step |
| `GET code?…` (same parameters) | Steps 1-3 only: `{client_id, code, code_verifier, redirect_uri, state, nonce}`, for a test that redeems the code itself (wrong secret, spent code). Sets the cookies too |
| `POST refresh` (form or query: `client`, `refresh_token`) | Production's `refresh_token` grant as the client; the response as for `token` |
| `GET callback` | `localtest-smoke`'s registered redirect URI: answers its query parameters as JSON (`code`, `state`, or `error`, `error_description`), for a browser that drives the picker itself |

The access token carries `pid`, `acr`, `urn:altinn:authlevel`, `urn:altinn:userid`, the party claims, `sid` and
`scope` (`openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read`, the session's scopes, not
the requested ones), which is what Dialogporten's end-user API needs; `/Home/GetTestUserToken`'s cookie JWT has no
`pid` by production design.

## Build notes

- `PublishAot` with `IsAotCompatible` and warnings as errors: the AOT and trim analysers run on every build. YARP 2.3.0
  publishes without AOT or trim warnings.
- All JSON goes through the source-generated `LocaltestJsonContext`; a new feature adds its types there. Responses that
  pass another service's JSON on (the Authentication feature's token responses) are rewritten with `JsonDocument` and
  `Utf8JsonWriter`, which need no serializer metadata.
- Give minimal-API lambdas an explicit return type (`async Task<IResult> (…) =>`) when the inferred one would be
  nullable: for `return error ?? Results.Json(…)` with an `IResult? error` the AOT request delegate generator emits
  uncompilable code for `Task<IResult?>`.
- The catch-all is `MapFallback("{**path}", RequestDelegate)`. The default fallback pattern (`{*path:nonfile}`) skips
  every path that looks like a file (`…/report.pdf`), and with Native AOT the request delegate generator rewrites the
  typed-lambda overload of `MapFallback` and loses an explicit pattern, so the plain `RequestDelegate` overload is used.
- `UseRouting()` comes after the static files: the catch-all matches every path, and the static file middleware does
  nothing for a request that already has an endpoint. A feature's endpoints are mapped before the catch-all and win
  over it by route precedence.
- The AOT publish is memory-hungry; don't run it alongside a Cypress run on a small machine.

## Verified (2026-10-20): identity features and Home

- `./smoke-test.sh`, 65 checks: the proxy, the fake ID-porten on its own (picker page, `state` and `iss` in the
  redirect, off-platform `redirect_uri` and unknown levels refused, a wrong `redirect_uri` or PKCE verifier and a spent
  code refused at the token endpoint, the id_token's `kid` in the JWKS with `aud`, `nonce` and `acr`), the Maskinporten
  metadata, and the whole `/Home` contract through production Authentication (every level, party cookies, the org
  token through `exchange/maskinporten`, the Studio token through `exchange/altinnstudio`).
- Authentication validates the fake's tokens with its production `UpstreamTokenValidator`, with
  `ValidateCallbackIssuer` and `StrictIdTokenValidation` on.
- The full Cypress suite (13 app folders, 419 tests) through it; the failures were a flaky spec and a timezone bug in
  two specs, see `../../planned-components/authentication/localtest-app-identity.md`.
- Runs as the image's `app` user (uid 1654) on pinned base images; a `localtest-data` volume made by the earlier root
  image needs `chown -R 1654:1654` once.

## Verified (2026-10-19)

- Side by side with the nginx ingress (port 8001 against 8000, same `Host`): identical status, content type and body
  for the start page, `/Home`, OpenID metadata, Register, Storage, Events, the PDP, app requests and the app-frontend
  bundle; identical `Set-Cookie` headers from the sign-in (no `Secure`); identical answers on the PDF and
  workflow-engine hosts.
- In nginx's place: `./smoke-test.sh` 22 ok, root smoke 23 ok, Events smoke 23 ok, authorization smoke 30 ok.
- Cypress through it, all 13 test apps running: 10 app folders (anonymous-stateless-app, expression-validation-test,
  multiple-datamodels-test, navigation-test-subform, payment-test, process-transition-test, service-task,
  signering-brukerstyrt, signing-test, stateless-app) with 73 tests, all passing, before a machine reboot cut the run
  off in subform-test; subform-test, component-library and frontend-test not yet run.
