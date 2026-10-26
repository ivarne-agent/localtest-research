# localtest

The localtest app: the stack's front door, and the home of the localtest features that are not a platform component.
A small ASP.NET app published with Native AOT: one binary on `runtime-deps`, no .NET runtime, a 36.5 MB image
(34.8 MiB; 35.5 MB before task 26). Each feature keeps its own code folder and its own API under `localtest/api/{feature}/`.

**One front door for both runtimes (task 26).** The proxy routes by host and path prefix to the platform services by
their service names, answers a `503` for a service that is not running, strips `Secure` from an app's cookies, and
takes one registration call per app that routes and provisions it. studioctl (task 27a) and the Aspire package (task
27b) run it as their only front door, publishing the public port 8000 onto the container's 8080: studioctl-server's
discovery and the package's `AddAltinnApp` register the apps here. The stack-wide rules this app implements (routes,
service discovery without pass-through, the `503`s, the registration, the public host names) are summarised in
[`../CONVENTIONS.md`](../CONVENTIONS.md#the-front-door).

| Feature | Folder | API |
| --- | --- | --- |
| Proxy: one origin, `local.altinn.cloud:8000` by default (`Localtest:PlatformEndpoint`), split by host name and path prefix onto the platform services by name and onto the registered apps; the 503s; the app registration with provisioning; the frontend dev server's route | `Proxy/`, `Provisioning/` | `localtest/api/apps/`, `localtest/api/routes/` |
| ID-porten: a fake OpenID Connect provider whose login page is the test-user picker; production Authentication signs in against it as against ID-porten | `IdPorten/` | `localtest/api/idporten/` |
| Maskinporten: discovery and JWKS that production Authentication's `exchange/maskinporten` trusts, and the minting `/Home/GetTestOrgToken` uses | `Maskinporten/` | `localtest/api/maskinporten/` |
| Home: the old localtest's start page and test helpers (`/`, `/Home/LogInTestUser`, `/Home/GetTest*Token`, `/Home/Localtest/Version`), which sign in and exchange tokens through production Authentication; the start page links to Grafana while its route is live | `Home/` | `/`, `/Home/…` (the old contract) |
| Authentication: production Authentication's authorization-code flow for a seeded OIDC client, run headlessly; tokens with `pid`, refresh | `Authentication/` | `localtest/api/authentication/` |
| Minimal mode: the platform services whose address is the front door's own, emulated in memory under their public prefixes (task 35); nothing of it is registered or mapped otherwise | `Emulation/` | the services' own paths ([Minimal mode](#minimal-mode)) |

| File | What |
| --- | --- |
| `Program.cs` | Composition: service discovery (core and configuration provider), dedicated hosts first, then the app-frontend files, routing, the features' endpoints, the catch-all forwarder |
| `LocaltestSettings.cs` | Settings the features share: the public `Localtest:PlatformEndpoint` (the public host and port are its own), the dedicated subdomains `Localtest:PublicSubdomains`, and the shared test certificate's path |
| `Tokens/` | `SigningKey.cs`: RS256 keys (generated, or the shared certificate's), JWKS and a hand-written JWT writer, used by the identity features; `TokenErrorResponse.cs`, the token endpoint error both fakes answer |
| `IdPorten/` | The fake ID-porten: `IdPortenApi.cs` (endpoints and picker), `AuthorizationCodes.cs`, `TestUsers.cs` (the register's test users, also used by Home), `AuthLevels.cs` |
| `Maskinporten/` | The fake Maskinporten: `MaskinportenApi.cs` (metadata, JWKS, token endpoint, minting) |
| `Home/` | `HomeApi.cs` (the old contract), `AuthenticationClient.cs` (the headless sign-in and the exchanges over HTTP; its upstream half is shared with `Authentication/`), `StudioToken.cs` (Designer's token, for `/Home/GetTestStudioToken` and the provisioning) |
| `Authentication/` | `AuthenticationApi.cs` (endpoints), `CodeFlow.cs` (the code flow over HTTP), `SeededClients.cs` (the seeded clients' ids, secrets and redirect URIs) |
| `LocaltestJsonContext.cs` | Source-generated JSON for every feature (Native AOT has no reflection serializer) |
| `Proxy/PlatformRoutes.cs` | The route table: dedicated hosts and path prefixes to service names, the rewrite, the optional groups for the hints, the public host names |
| `Proxy/Forwarder.cs` | Resolves a request, forwards it with [YARP](https://github.com/dotnet/yarp)'s `IHttpForwarder` (platform routes through service discovery's handler), the transform (original `Host`; the rewrite; `Secure` removed from an app's cookies), and the error path |
| `Proxy/ComponentUnavailable.cs`, `Proxy/ServiceCalls.cs` | The 503 problem details and page ("not running", "not answering"), and how a call to a name without an address fails |
| `Proxy/AppRoutes.cs`, `Proxy/DevServerRoute.cs`, `Proxy/RoutesApi.cs` | The registered apps (with their last provisioning) and the frontend dev server, saved on `/data`, and the API the runners use |
| `Provisioning/` | The deploy steps, copied from studioctl's provisioning library (`AppId.cs` says what changed): `LocaltestProvisioner.cs`, `RunningAppSource.cs`, and `AppProvisioning.cs`, which runs them for a registration |
| `Emulation/` | The minimal mode: `Emulation.cs` (the two calls `Program.cs` makes), `EmulationMode.cs`, one folder per emulated service, the test-data store with its copies of the Register's format files and seed, the static PDF; [its README](Emulation/README.md) |
| `Telemetry/` | `FrontDoorTelemetry.cs`: OpenTelemetry (ASP.NET Core and HttpClient instrumentation, the emulated services' spans) only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set; `LocaltestOtlp.cs`, a copy of the shared OTLP helper ([Telemetry](#telemetry)) |
| `Dockerfile` | Native AOT publish for the build machine's architecture (`linux-musl-arm64` or `-x64`); listens on 8080 |
| `tests/` | `Localtest.Tests.csproj`, xunit v3: the route table, the 503s, the registration API against a fake app and a fake platform on loopback ports, the telemetry (`TelemetryTests.cs`, against an in-process stand-in collector; inherited `OTEL_*` variables are cleared first); `tests/Emulation/`, the minimal mode (`dotnet test tests`) |
| `smoke-test.sh` | Against the running stack: the routing contract, the 503, the registration API and the dev-server route, the fake ID-porten and Maskinporten, and the Home contract (sign-in at every level, party cookies, token helpers), and the monitoring routes (with the group on: Grafana's health, an OTLP export through `/otlp`, the Home link; off: the `503` naming the group and no link). 83 or 84 checks, in both modes; in minimal mode 9 more, one per emulated service (the header and one answer); `PROXY_CHECKS=0` skips the 14 registration checks (only useful before task 27a, when another front door ran) |

## Proxy

### Upstreams by service name

Every platform destination is `http://<service>` with no port (`http://storage`), and every call the app makes to a
component goes the same way (`http://authentication/…`, `http://register/…`, the provisioning's calls).
[`Microsoft.Extensions.ServiceDiscovery`](../../planned-components/service-discovery.md) resolves the name from the
configuration key `services:<service>:http:0`, which the runtime sets as the environment variable
`services__<service>__http__0=http://<alias>:<port>`. Registered are the core and the configuration provider only,
**not the pass-through provider**: a name without an entry fails at once (service discovery throws
`InvalidOperationException: The endpoint collection contains no endpoints`), without a DNS lookup, and the front door
answers "not running" for it. The factory clients get the resolving handler through `ConfigureHttpClientDefaults`; the
forwarder wraps its own `SocketsHttpHandler` with `IServiceDiscoveryHttpMessageHandlerFactory.CreateHandler`, for the
platform routes only: an app's and the dev server's addresses (`http://host.docker.internal:5005`) are not names the
configuration knows, so they go through the plain handler, and the provisioning reads the app through a plain client.
The `services__*` variables are the override: there is no other upstream setting. A `Host` header the caller sets is
kept (the forwarder sets the browser's, the Authentication client the public one), since the resolver only fills one
in.

### Routes

In order:

1. **Dedicated hosts** own every path, the localtest app's own endpoints included. They are subdomains of the public
   host, the host of `Localtest:PlatformEndpoint` (default `local.altinn.cloud`): `pdf.<host>` → `pdf3`,
   `workflow-engine.<host>` → `workflow-engine`, `af.<host>` → `arbeidsflate`, and `app-frontend.<host>` → the frontend
   dev server while one is registered (below; without one that host is served like the main host). The runner lists
   the subdomains that are on in `Localtest:PublicSubdomains` (comma-separated, from the manifest's `public` section;
   unset is all four): one the app does not know stops its start, a known one left out is served like the main host.
   The issuers, redirect URIs and links below follow the same origin, including the seeded `arbeidsflate-localtest`
   client's `http://af.<host>:<port>/api/cb`.
2. **`/app-frontend/`**: the built app-frontend (`FRONTEND_DIST`, mounted at `/frontend`) as static files, with
   `Access-Control-Allow-Origin: *` and `Cache-Control: no-cache`. The apps' asset base.
3. **The localtest app's own endpoints**: `GET /health`, `localtest/api/…` (below), and the start page and `/Home/…`
   (Home).
4. **App routes**: `/{org}/{app}/…` → the registered address, path unchanged.
5. **Platform prefixes** on any other host, matched on segment boundaries, first match wins:
   `/dialogporten/graphql` → `dialogporten-graphql` with the prefix rewritten to `/graphql` (APIM does the same in
   front of the GraphQL host); `/dialogporten/` → `dialogporten-webapi`; `/storage/dialogporten/` →
   `dialogporten-adapter`; `/storage/` → `storage`; `/register/`, `/profile/`, `/authentication/`, `/authorization/`,
   `/events/`, `/notifications/`, `/receipt/`, `/correspondence/` → the service of that name; `/accessmanagement/` →
   `access-management`; `/resourceregistry/` → `resource-registry`; `/grafana/` → `grafana` (Grafana serves from that
   sub-path, `GF_SERVER_SERVE_FROM_SUB_PATH`); `/otlp/` → `otel-collector`, its OTLP/HTTP receiver, with the prefix
   removed (`/otlp/v1/traces` arrives as `/v1/traces`), for apps on the host that send OTLP over HTTP with
   `OTEL_EXPORTER_OTLP_ENDPOINT={origin}/otlp`. The last two are the `monitoring` group's (studioctl's
   `--with monitoring`). Paths are passed on unchanged except for the GraphQL and OTLP rewrites.
6. Anything else: `404` with the text "No app is registered for this path. Start it with studioctl run or an Aspire
   AppHost (or, with this app as the front door, run-app.sh <app-dir> <port>)."

A prefix requested without its trailing slash (`/storage`, `/ttd/app?x=1`) is redirected with `301` to the slashed
path, query kept, as nginx did; not `/dialogporten/graphql`, which is the GraphQL endpoint itself. Matching is ordinal
and case-sensitive, like the nginx locations. The host names are code constants (`PlatformRoutes.Hosts`), the public
origin's, until the runtimes pass them. `GET localtest/api/routes` lists the table with each service's address (null:
not running) and the dev-server route.

### When a service cannot answer

Both are `503 Service Unavailable` with `Cache-Control: no-store`, as RFC 9457 problem details
(`application/problem+json`, with the extension members `component` and `group`), or the same content as a small HTML
page when the request ranks `text/html` above JSON (a browser). Ported from studioctl-server's front door.

| Case | Title | Detail |
| --- | --- | --- |
| The service has no address (`services__<name>__http__0` is not set) | `Component not running` | Names the service and, for an optional group (`dialogporten`, `receipt`, `correspondence`, `arbeidsflate`), the group with both runtimes' hints: `studioctl env up -r localtest-v2 --with <group>`, and `.With<Group>()` or `.WithGroup("<group>")` on `AddAltinnLocaltest()` in an Aspire AppHost. For `monitoring` (Grafana, the collector) only the studioctl command, and that an Aspire AppHost does not run the group: its dashboard shows the telemetry |
| The address does not answer (refused, timed out, unknown host) | `Component not answering` | The forwarder's error and its message (the old `502` text), and `studioctl env logs -r localtest-v2 -c <name>` and `env status`; for an app, whether it still runs; for the dev server, how to remove its route |

A request the client cancelled answers nothing. An app path without a registered app keeps its `404` hint.

### What it does to a request

- **Streams** both ways, unbuffered: server-sent events (`events/api/localtest/stream`) and downloads arrive as the
  upstream writes them; WebSocket upgrades are passed on.
- **Headers**: YARP's default transform (all headers copied, `X-Forwarded-For`, `-Proto` and `-Host` set), plus the
  original `Host`, because the components and apps build their own links from it.
- **Cookies**: a platform component's `Set-Cookie` passes unchanged; the components set no `Secure` cookie over plain
  http themselves (task 21b, [`reviews/cookies.md`](../reviews/cookies.md)). On an app's route the `Secure` attribute
  is still removed, because app-lib's `keepAlive` sets the runtime cookie `Secure` in every environment and the browser
  refuses it from this plain-http origin (task 22 removes this).
- **Limits**: request bodies up to 200 MB; an upstream may stay silent for 5 minutes (PDF generation, big uploads)
  before the request fails; connect timeout 5 seconds.
- Connections to an upstream are recycled every 30 seconds, so a recreated container's new address is picked up.

### Registering an app (`localtest/api/apps/`, anonymous)

One call routes an app and deploys it to the platform the way Altinn Studio deploys one: the app runner (`studioctl
run`, `AddAltinnApp` on the app's ready event, `run-app.sh`) gives the app's address, and the front door reads the
rest from the running app.

| Call | What |
| --- | --- |
| `PUT apps/{org}/{app}` with `{ "url": "http://host.docker.internal:5005" }` | Validates (org and app are lower-case letters, digits and hyphens; not an org that is a platform prefix, `localtest`, `app-frontend` or `health`; the URL is an http(s) origin without a path or query; `400` otherwise), stores the route, then provisions from the app's API under `{url}/{org}/{app}/` (`api/v1/applicationmetadata`, `api/v1/meta/authorizationpolicy`, `api/v1/applicationlanguages`, `api/v1/texts/{lang}`, `testData.json`): the test users to the Register, metadata and texts to Storage, the policy to the PDP, the Resource Registry's subject reload, Access Management's refresh, and Dialogporten's sync when `dialogporten-adapter` has an address. `200` with the route and its provisioning (`state`, `at`, `result`: `textCount`, `hasPolicy`, `hasTestData`, `dialogporten` `Skipped`/`Synced`/`Failed`). A failed provisioning keeps the route and answers `502` problem details naming the failing request (or the service without an address); register again to retry. Idempotent: a repeat provisions again and keeps the registration time |
| `DELETE apps/{org}/{app}` | Removes the app's test users from the Register, refreshes Access Management and removes the route; `204`, also when there was no route. Storage, the policy store and the Resource Registry keep the app, as they keep one Designer no longer deploys. A failed deprovisioning still removes the route and answers `502` |
| `GET apps` | Every route with its address, registration time and last provisioning |

Platform calls go by service name; the Studio token (Designer's `studio`/`studio.designer` platform access token,
exchanged through production `exchange/altinnstudio`) is minted in-process, as `/Home/GetTestStudioToken` does. One
provisioning runs at a time. The steps were copied from studioctl's provisioning library, which task 27a deleted (the
Aspire package's copy went in task 27b).

### The frontend dev server (`localtest/api/routes/`, anonymous)

| Call | What |
| --- | --- |
| `PUT routes/frontend-dev-server` with `{ "url": "http://host.docker.internal:8080" }` | Every path on `app-frontend.local.altinn.cloud` goes to the frontend dev server (`studioctl run --dev-frontend` sends it under localtest-v2 and `DELETE`s it when the run stops). Same URL rules as an app |
| `DELETE routes/frontend-dev-server` | Back to the built app-frontend under `/app-frontend/`; `204` |
| `GET routes` | The platform route table with each service's address, and the dev-server route |

App routes are saved to `/data/app-routes.json` (`Proxy__AppRoutesFile`) and the dev-server route next to it
(`frontend-dev-server.json`), on the `localtest-data` volume (`localtest-v2-localtest-data` under studioctl,
`altinn-localtest-localtest-data` under Aspire), and read at start, so a recreated container keeps routing.
Nothing watches the files.

### Port

The image listens on **8080** (`ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`), the stack's conventional port, and answers
`/health`. The public port (8000 by default; studioctl's `--port`, Aspire's `PublicPort`) is what a runtime publishes
onto it (studioctl since task 27a, the Aspire package since task 27b), and the port of `Localtest:PlatformEndpoint`.

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
come from the register's `register/api/localtest/test-users`, read by service name (`http://register/…`), not through the front door.

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
its production exchanges. Calls to Authentication go by service name (`http://authentication/…`) with the public `Host`.

| Endpoint | Behaviour |
| --- | --- |
| `GET /`, `/Home`, `/Home/Index` | Start page: `<select id="UserSelect">` with `userId.partyId` options (`Name`, and `Name for Org` for each party in the user's register `parties`), `AuthenticationLevel` 0-4 (default 2), optional `AppPathSelection`; `?goto=` becomes `RedirectUrl` |
| `POST /Home/LogInTestUser` | Form or JSON: `UserSelect`, `AuthenticationLevel` (-1 skips the login), `action`, `RedirectUrl`, `AppPathSelection`. Runs the production sign-in headlessly (production `authentication?goTo=`, a code from the fake ID-porten for the chosen user, production `upstream/callback`), appends its cookies with `AltinnPartyId`/`AltinnPartyUuid` replaced by the selected party (Authentication sets them without `Secure` over http; the JWT keeps the user's own party, as in production). `action=reauthenticate` answers 204; otherwise redirects to `RedirectUrl` (same origin only), else `/{AppPathSelection}/`, else `/` |
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
| `GET token?client&userId&partyId&authenticationLevel&scope` | The whole flow; production's token response (`access_token`, `id_token`, `token_type`, `expires_in`, `scope`, `refresh_token`, `refresh_token_expires_in`) plus `pid`, the access token's `pid` claim (null when absent). Defaults as `/Home/GetTestUserToken`: `userId` required, level 2, the user's own party. Sets the session cookies production's callback set (party cookies on `partyId`; no `Secure` over http), so an `openid/logout` with them ends that session. Production's error status and body are passed through; a failure before the token call is a `502` problem naming the step |
| `GET code?…` (same parameters) | Steps 1-3 only: `{client_id, code, code_verifier, redirect_uri, state, nonce}`, for a test that redeems the code itself (wrong secret, spent code). Sets the cookies too |
| `POST refresh` (form or query: `client`, `refresh_token`) | Production's `refresh_token` grant as the client; the response as for `token` |
| `GET callback` | `localtest-smoke`'s registered redirect URI: answers its query parameters as JSON (`code`, `state`, or `error`, `error_description`), for a browser that drives the picker itself |

The access token carries `pid`, `acr`, `urn:altinn:authlevel`, `urn:altinn:userid`, the party claims, `sid` and
`scope` (`openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read`, the session's scopes, not
the requested ones), which is what Dialogporten's end-user API needs; `/Home/GetTestUserToken`'s cookie JWT has no
`pid` by production design.

## Minimal mode

`studioctl env up -r localtest-v2 --minimal` runs `postgres`, `storage`, `workflow-engine` and this container, and
gives every other HTTP service of the default stack this container's own address
(`services__<name>__http__0=http://localtest:8080`). The front door then answers for Register, Profile, Authentication,
Authorization (a PDP that permits everything), Access Management, Events, Notifications, the Resource Registry (`501`)
and pdf3 (one static PDF) itself, in memory, from the test data, and marks every such answer with
`X-Localtest-Emulated: <service>`. Storage and the workflow engine are the real ones. Everything else here works the
same: the routes, the registration (provisioning into memory, and into Storage over HTTP), the fake ID-porten and
Maskinporten, Home (with a banner naming the mode). `GET localtest/api/routes` reports `emulated` for those services,
and the app routes are kept in memory (`Proxy__AppRoutesFile=`): every stop is a clean slate.

What it is for, what is lost (authorization rules, real Register data, PDF content, the optional groups) and why:
[`planned-components/minimal-emulation/README.md`](../../planned-components/minimal-emulation/README.md). What is
emulated, the token claim sets, the test-data copies and the seams it plugs into: [`Emulation/README.md`](Emulation/README.md).

## Telemetry

The stack's rule ([`../components/shared/README.md`](../components/shared/README.md#otlp-helper-fakesotlp)): traces,
metrics and logs over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL`
(`grpc` or `http/protobuf`) and `OTEL_SERVICE_NAME`; nothing otherwise. A per-signal endpoint
(`OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` and the others) overrides the general one for its signal, but does not turn
telemetry on by itself: the exporter always sends all three signals, and one without an endpoint would go to the SDK's
default `localhost:4317`. studioctl sets the variables on every container while the monitoring group's collector runs
(`http://otel-collector:4317`, `grpc`, `OTEL_SERVICE_NAME=localtest`).

- **Without the variable** nothing is registered: no tracer, meter or logger provider, no activity listener, no
  exporter, so no export errors and no extra log lines.
- **Name:** `service.name` is `localtest` unless `OTEL_SERVICE_NAME` is set (the shared helper adds the environment
  detector after the front door's own resource).
- **Traces:** ASP.NET Core and HttpClient instrumentation. A forwarded request is a server span (named after the route:
  `GET storage`, `GET ttd/app`, `GET frontend-dev-server`, with the route in `localtest.route`) and under it the
  forwarder's client span (`peer.service` = the route), whose `traceparent` goes to the destination. An app that sends
  its own `traceparent` is the parent of the server span, so app, front door and platform service are one trace.
- **Not traced:** the monitoring group's routes. `/otlp`, because the span about forwarding an export would be exported
  through the same route and telemetry would feed on itself; `/grafana`, because it is the observer: every dashboard
  view would add a burst of front-door-only spans (Grafana exports none to join them) to the traces being looked at.
  Also `/health` (the runners' probes) and `/app-frontend/` (dozens of static files per page). Their child spans go
  with them: the sampler follows the parent. The metrics still count every request.
- **Minimal mode:** each emulated call is a span of the source `Localtest.Emulation` under the request's server span,
  named after the component and the endpoint's pattern (`Register GET /register/api/v1/parties/{partyId:int}`,
  `Authorization POST /authorization/api/v1/decision`), with `peer.service` set to the service (`register`) and
  `http.response.status_code`. One process has one resource, so these spans are `service.name` `localtest`; a span per
  call with the component in its name and `peer.service` keeps the trace readable ("Register", "Authorization") without
  a provider per component, which an `ActivitySource` per component would not have changed (the source is not the
  service). In-process calls (the provisioning into the emulated services, Home's sign-in) are not HTTP and get no span.
- **Metrics:** ASP.NET Core (hosting, Kestrel) and HttpClient meters. **Logs:** the existing log lines (warnings and
  the start-up lines), also over OTLP.
- **AOT:** OpenTelemetry 1.19.1 (`Extensions.Hosting`, `Exporter.OpenTelemetryProtocol`) and the instrumentation
  packages 1.19.0.

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
- `Microsoft.Extensions.ServiceDiscovery` 10.8.0 and the provisioning code publish for `linux-musl` without AOT or trim
  warnings (task 26, `podman build`, arm64): the image grew from 35.5 MB to 36.5 MB.
- Settings are read from the built configuration in the service registrations (`sp => …`), not from
  `builder.Configuration` before `Build()`, so the test host's configuration (`WebApplicationFactory`) reaches them.
  The one exception is the minimal mode, which decides at `Emulation.Add` what to register; its tests pass their
  settings with `UseSetting`, which the builder sees ([Emulation/README.md](Emulation/README.md#how-the-mode-switches-on)).
- `tests/` sits inside the project folder: `Localtest.csproj` excludes it (`DefaultItemExcludes`) and so does
  `.dockerignore`.

## Verified (2026-10-22): task 26 on today's stack

History: run before task 27a, when studioctl-server was still the front door on 8000; since 27a this image is the front
door in both runtimes ([`../README.md`](../README.md#tests) has the later runs).

`studioctl env up -r localtest-v2 --with dialogporten` (studioctl built from the branch) with this image as the
`localtest` component, studioctl-server still the front door on 8000:

- The container got the 19 `services__<name>__http__0` variables (hyphenated names included) and every component its
  alias; the start page read the test users and `/Home/GetTestStudioToken` exchanged, both by service name.
- **Registration through 8000** (`run-app.sh` with the Aspire sample `ttd/testapp`): `200`, provisioning `succeeded`
  (1 text, policy, test data, Dialogporten `Synced`). A packet capture on the stack's network showed the container's
  calls: the app's five reads at `host.docker.internal:5005`, the exchange with `Host: local.altinn.cloud:8000`, then
  Register, Storage (metadata, texts), the PDP, the Resource Registry, Access Management and Dialogporten's sync, each
  with its service name as `Host`. `DELETE` emptied the Register's app list (studioctl-server, which provisions the
  same app itself, did not push it back), and `PUT` again restored it. A registration under another app name answered
  `502` with the app's own refusal (`409`) as the detail and kept the route. A `DELETE` of an app the Register did not
  know answered `502` (its `404`), now fixed: a removal is idempotent.
- **The container's own port, published as 8001** (a second container of the image on the network, same variables,
  volume and host entries): `smoke-test.sh http://local.altinn.cloud:8001` 52 of 81 ok, every routing, `503`,
  registration and dev-server check among them. The 29 that fail cannot pass through a second container: 27 are the
  identity flows and the issuer checks, because `Localtest:PlatformEndpoint` is `:8000`, so Authentication reaches the
  fake ID-porten and Maskinporten at the first container, whose keys and codes differ from the second's; the others are
  the sign-in cookies (same cause) and `/app-frontend/` (no `FRONTEND_DIST` mounted). The root `smoke-test.sh` against
  8001: 26 of 26 (its browser-like sign-in follows the redirects onto `:8000`, so only its API calls used 8001).
- **The 503s through 8001**: with the GraphQL container stopped, `POST /dialogporten/graphql` answered "Component not
  answering" (`Name does not resolve (dialogporten-graphql:8080)`), as problem details and as the page; with the second
  container started without `services__dialogporten-graphql__http__0`, "Component not running" with the group and both
  runtimes' hints.
- Through studioctl-server on 8000, after deprovisioning the sample: the root `smoke-test.sh` 26 of 26, Authentication's
  34 of 34, and this folder's script with `PROXY_CHECKS=0` 65 of 67 (the trailing-slash redirect and `/app-frontend/`,
  studioctl-server's known gaps).

## Verified (2026-10-20): identity features and Home

Update 2026-10-21: the script has had 75 checks since task 4 added the Maskinporten token checks (2026-10-20, 798f18e).
Under studioctl with `PROXY_CHECKS=0` (66 checks) it passed 64 on 2026-10-20 (`../components/authentication/README.md`),
every identity provider and Home check among them. The run did not record which two failed; the two platform-route
checks studioctl's front door cannot pass are the trailing-slash redirect and `/app-frontend/` (`../../TODO.md` task 13).

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
