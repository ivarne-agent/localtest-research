# arbeidsflate

Production Arbeidsflate, the Altinn inbox (`vendor/`, upstream `Altinn/dialogporten-frontend`, see `vendor/UPSTREAM.md`):
the React SPA and the Fastify BFF (backend for frontend) that signs the user in, keeps the session and proxies GraphQL to
Dialogporten. It is the **optional group `arbeidsflate`** (`studioctl env up -r localtest-v2 --with arbeidsflate`, or
`--with everything`). Groups close over `dependsOn` ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-manifest)):
`--with arbeidsflate` starts `arbeidsflate`, `arbeidsflate-bff`, Redis and Dialogporten's GraphQL and WebApi hosts, not
the Service host or the adapter. The inbox shows Dialogporten's dialogs, which come from the Dialogporten adapter (apps'
instances, so add `--with dialogporten`) and, with `--with correspondence`, from Correspondence. Neither container is
.NET: the BFF and nginx reach the others by alias and port (`postgres:5432`, `redis:6379`, `arbeidsflate-bff:80`), not
through service discovery.

Open it at **`http://af.local.altinn.cloud:8000`**, a host of its own (the SPA and the BFF use absolute paths, `/`,
`/assets`, `/api/...`, so they cannot live under a prefix of `local.altinn.cloud`). The browser needs the name in the
hosts file: `sudo studioctl env hosts add` writes it with the stack's other names (or add `127.0.0.1 af.local.altinn.cloud`
by hand). Containers resolve it through the host gateway without anything.

Apps link into it: the app runners set `PlatformFrontendSettings__Arbeidsflate{Inbox,Dialog,Profile,Parties}Url` to
`/`, `/inbox/{dialogId}`, `/profile` and `/profile/parties` on this host, which the monorepo app frontend uses for "back
to inbox" and the receipt's inbox link (`localtest/README.md`, "What the app runner pushes").

| Container | Image | Port (in the stack) | Route | Health |
| --- | --- | --- | --- | --- |
| `arbeidsflate` | `Dockerfile.frontend`: the SPA built by Vite, served by `nginx:1.25-alpine` | 80 | the whole host `af.local.altinn.cloud` | `GET /` |
| `arbeidsflate-bff` | `Dockerfile.bff`: Node 24, the TypeScript sources run through `@swc-node/register` | 80 | none (nginx proxies `/api/` to its network alias `arbeidsflate-bff`) | `GET /api/readiness` |

State: a database `arbeidsflate` in the shared Postgres (saved searches, favourite groups, a profile row per user), and
database 2 of the shared Redis (sessions, `idp-sid:*` for front-channel logout, Maskinporten and
organisation caches; Dialogporten uses 0, Correspondence 1). Nothing is provisioned and nothing is reset on start.

## Layout

| Path | What |
| --- | --- |
| `vendor/` | Upstream's whole repository at the pin, byte for byte (its tree id is upstream's), plus `UPSTREAM.md` |
| `patches/0001-bff-oidc-discovery-scheme.patch`, `patches/0002-frontend-cookie-domain-localtest.patch`, `patches/0003-bff-cookies-secure-auto.patch` | The three `localtest:` changes to upstream's code, applied at image build (below) |
| `Dockerfile.bff`, `Dockerfile.frontend` | Mirror `vendor/packages/bff/Dockerfile` and `vendor/packages/frontend/Dockerfile`; every difference is marked `localtest:` |
| `bff-start.sh` | The BFF image's command: a throwaway Maskinporten client key when none is set, the TypeORM migrations, then upstream's `pnpm --filter bff run start` |
| `nginx/nginx.conf` | Upstream's `packages/frontend/nginx.conf` plus the `/api/` proxy and the http CSP; every change commented |
| `nginx/nginx-start.sh` | The SPA image's command: writes nginx's `resolver` (the container runtime's DNS server from `/etc/resolv.conf`), then upstream's `/start.sh` |
| `docs/` | Screenshots from the phase 2 browser check ("Verified") |
| `.dockerignore` | Upstream's (root-anchored patterns) under `vendor/`, plus a developer's `node_modules` and `dist` in the packages |
| `postgres-init/90-arbeidsflate.sh` | The role `arbeidsflate` (login, not superuser, password `localtest`) and its database, baked into the shared Postgres image (`../../postgres/Dockerfile`) |
| `smoke-test.sh` | Through the front door, see "Tests" |

## What changed, and why

**The patches, applied at build, not committed onto `vendor/`.** Both Dockerfiles start with the same `source` stage: the
pinned Node image with `git`, `COPY vendor/`, `COPY patches/`, `git apply --check` and `git apply` of every patch. The
build stage then copies the patched tree where upstream has `COPY . .`. This keeps `vendor/` reviewable as upstream's
tree (checkable by tree id, see `vendor/UPSTREAM.md`), puts the whole localtest change in three small files, and makes a
patch that no longer applies after a re-pin fail the image build at once. TypeScript has no `#if`, so this is the
component's form of a directive site.

| Patch | Why |
| --- | --- |
| 0001: `OIDC_PLATFORM_URL` may carry a scheme (`config.oidcDiscoveryUrl`, read by the OIDC plugin, the token verifier and `/api/health`) | Upstream writes `https://${OIDC_PLATFORM_URL}/.well-known/openid-configuration` in three places; Authentication is plain http here, so the BFF would not start. A value without a scheme behaves as upstream |
| 0002: `getCookieDomain()` is `local.altinn.cloud` on `*.local.altinn.cloud` | The SPA writes `AltinnPartyId` and `AltinnPartyUuid` when the user changes party; upstream maps unknown hosts to prod and the browser rejects `Domain=.altinn.no`. With the patch the inbox and the apps share the party cookies Authentication sets for `local.altinn.cloud` |
| 0003: the BFF's session cookie (`server.ts`) and `altinnPersistentContext` (`graphql/profile/queries.ts`, `mutations.ts`) are `secure: 'auto'` instead of `true` (task 21b) | Upstream hard-codes `Secure`; a browser refuses it over http, and `@fastify/session` refuses to save a `secure: true` session on an http request. `'auto'` (both `@fastify/session` and `@fastify/cookie`) sets `Secure` exactly when `request.protocol` is https, as behind production's TLS ingress; on http the session cookie gets `SameSite=Lax` (unset upstream, which browsers read as Lax). The front door no longer removes `Secure`, so this replaces that, and nginx no longer claims https. Reported first in `../../reviews/cookies.md` (A1), the task 21b inventory |

**The Dockerfiles** (each `localtest:` line explains itself):

- Context is this folder, not upstream's repository root; the patched tree comes from the `source` stage.
- Base images pinned by digest (upstream: `node:24.15.0-slim`, `nginx:1.25-alpine`), like every other component's.
- `NODE_EXTRA_CA_CERTS` exported for the install and build steps when the sandbox's CA bundle is mounted (corepack and pnpm
  download through Node); absent in CI.
- `Dockerfile.frontend`: `pnpm install --frozen-lockfile --filter 'frontend...'` instead of the whole workspace, the
  install-size mitigation of the plan. It is a pure build-time change: the build stage is discarded and the nginx stage
  gets the same `dist/` (checked 2026-10-21 in a scratch copy: the filtered install type-checks and builds). Our
  `nginx/nginx.conf` replaces upstream's; `start.sh` is upstream's, copied from `vendor/`.
- `Dockerfile.bff` keeps upstream's whole-workspace install and `pnpm turbo build`, although the BFF needs only its own
  packages: its runtime image *is* the build stage, so a filtered install would change what the image contains, and that
  is not a build-time-only change (decided for task 11). The image is therefore large (the plan estimated 1.3 to 2 GB;
  measured in phase 2). Its command is `bff-start.sh`.

**`bff-start.sh`.** Production runs the TypeORM migrations as a separate job with the same image and `MIGRATION_RUN=true`
(`vendor/.azure/applications/bff-migration-job`); here the container runs them on every start (idempotent; a failure
exits and stops the container), then upstream's command. It also sets `MASKINPORTEN_JWK`, when unset, to a fresh RSA JWK
with a `kid`: the BFF signs its Maskinporten client assertion with it, and the localtest app's fake Maskinporten reads the
assertion's claims, not its signature, so no key is committed.

**`nginx/nginx.conf`.** Upstream's file is the SPA only; its `/api` reaches the BFF through traefik (`vendor/compose.yml`)
or Azure's ingress. Ours adds `location ^~ /api/` to `http://arbeidsflate-bff:80` and changes the headers. The BFF's alias is resolved per
request (through a variable and the `resolver` that `nginx-start.sh` writes, cached 10 s): a literal host is resolved once
at start, and in phase 2 a restarted BFF container had a new address and nginx answered 502 until it was restarted too.

- No `X-Forwarded-Proto` of its own (until task 21b it sent `https`: with upstream's hard-coded `secure: true`,
  `@fastify/session` saved a session only when `request.protocol` was https, and studioctl-server removed the cookie's
  `Secure` on the way back). With patch 0003 the cookies are `secure: 'auto'`, so the BFF (Fastify, `trustProxy: true`)
  sees the stack's http, saves the session and sets its cookies without `Secure`; a forged `https` would turn `Secure`
  back on, also through upstream's hook in `graphql/fastifyHeaders.ts`.
- The CSP's `connect-src` and `img-src` also allow `http://local.altinn.cloud:8000` (upstream allows `'self'` and
  `https:`): the SPA's subscription stream, dialog content and transmission receipts it fetches with the dialog token,
  and images on the platform host.
- No `Strict-Transport-Security` (meaningless over http, and harmful to a browser that once saw it over https).
- `Host` and `X-Forwarded-Host` are `$http_host` (with the port); `/api/` responses get no server-level headers (one
  marker header stops nginx's inheritance), as upstream's ingress adds none; streams unbuffered, 300 s read timeout.

## Hosting shape

```
browser ── http://af.local.altinn.cloud:8000/* ──► the localtest container (the front door, keeps Host and Set-Cookie)
                                                   └► arbeidsflate:80  (nginx, by services__arbeidsflate__http__0)
                                                        ├ /api/*  ──► arbeidsflate-bff:80 (no forged X-Forwarded-Proto)
                                                        └ /*      ──► the SPA (index.html for every client route)
browser ── http://local.altinn.cloud:8000/dialogporten/graphql/stream ──► Dialogporten GraphQL (SSE, CORS for af.)
BFF     ── http://local.altinn.cloud:8000/{authentication,dialogporten,profile,register,resourceregistry,localtest}/...
           (through the host gateway, as every component reaches the platform)
```

The front door routes the host to `arbeidsflate` in code (`localtest/Proxy/PlatformRoutes.cs`); studioctl declares it as
the topology component `arbeidsflate` (`af.local.altinn.cloud`), so it is one of the
public hosts every container maps to the host gateway (Authentication's server-side front-channel logout reaches the BFF
that way) and one of the names `studioctl env hosts add` writes. With the group off, every request on the host answers
the front door's `503` problem details naming the group and the `--with arbeidsflate` command.

### Configuration

The SPA's three runtime values are written into `index.html` by upstream's `start.sh` (`envsubst`):
`DIALOGPORTEN_STREAM_URL` is `http://local.altinn.cloud:8000/dialogporten/graphql/stream`, production's shape (APIM's
`/dialogporten/graphql/stream`); the front door forwards it to the GraphQL host's `/graphql/stream`. The two Application
Insights values stay empty, which turns telemetry off.

The BFF's environment, set by studioctl (the `arbeidsflate-bff` component in the component manifest,
`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`):

| Variable | Value | Why |
| --- | --- | --- |
| `HOSTNAME` | `http://af.local.altinn.cloud:8000` | `redirect_uri` is `HOSTNAME/api/cb`, equal byte for byte to the seeded client's |
| `OIDC_PLATFORM_URL` | `http://local.altinn.cloud:8000/authentication/api/v1/openid` | Authentication's discovery (patch 0001 keeps the scheme) |
| `OIDC_CLIENT_ID` / `OIDC_CLIENT_SECRET` | `arbeidsflate-localtest` / `arbeidsflate-localtest-secret` | The client Authentication seeds (`../authentication/README.md`, "Seeded clients") |
| `PLATFORM_BASEURL` | `http://local.altinn.cloud:8000` | Profile, Register, the Resource Registry, Notifications, Correspondence, `exchange/maskinporten` |
| `DIALOGPORTEN_URL` | `http://local.altinn.cloud:8000/dialogporten` | `/graphql` (the front door's rewrite to the GraphQL host) and `/health` (the WebApi) |
| `DB_CONNECTION_STRING` | `postgres://arbeidsflate:localtest@postgres:5432/arbeidsflate` | TypeORM; `DB_USE_AAD_AUTH` stays false |
| `REDIS_CONNECTION_STRING` | `redis://redis:6379/2` | The shared `redis` container, database 2 |
| `SESSION_SECRET` | a fixed string of 50 characters | Signs the session cookie (at least 32) |
| `AUTH_CONTEXT_COOKIE_DOMAIN` | `local.altinn.cloud` | The `altinnPersistentContext` language cookie the BFF writes for the apps |
| `ENVIRONMENT`, `NODE_ENV` | `test`, `production` | `prod` would filter organisations; production's compose and Bicep run `production` |
| `ENABLE_GRAPHIQL`, `ENABLE_INIT_SESSION_ENDPOINT` | `true`, `true` | As upstream everywhere but production: GraphiQL at `/api/graphiql`, the test seam below |
| `MASKINPORTEN_ISSUER`, `MASKINPORTEN_CLIENT_ID` | `http://local.altinn.cloud:8000/localtest/api/maskinporten/`, `991825827` | The fake Maskinporten (the BFF posts to `{issuer}token`); a nine-digit client id is the organisation number (Digdir). `MASKINPORTEN_SCOPE` keeps upstream's default (`altinn:register/partylookup.admin altinn:serviceowner/notifications.create`). Used for the profile page's username lookup in the Register |
| `PORT` | `80` | From the image (upstream's `ENV PORT=80`) |
| unset | `APP_CONFIG_CONNECTION_STRING`, `OTEL_*`, `ALTINN2_*`, `OCP_APIM_SUBSCRIPTION_KEY`, `REGISTER_SUBSCRIPTION_KEY`, `PERSON_URN_ENC_KEYS` | Feature flags and the alert banner fall back to upstream's defaults (a warning per request); no telemetry; Altinn 2 is not called; no API Management; the built-in development key encrypts person URNs in GraphQL responses |

## The OIDC flow against the stack

The BFF is a confidential client of production Authentication's OpenID Connect provider (task 2), with the seeded client
`arbeidsflate-localtest`; Authentication signs the user in through the localtest app's fake ID-porten picker.

1. `GET /api/login`: the BFF stores the PKCE verifier, `state` and `nonce` in a new session (saved over http
   because its cookie is `secure: 'auto'`, patch 0003) and redirects to `authentication/api/v1/authorize/` with `response_type=code`,
   `redirect_uri=http://af.local.altinn.cloud:8000/api/cb`, scopes `digdir:dialogporten.noconsent openid
   altinn:portal/enduser altinn:correspondence.forward.check`, `acr_values=idporten-loa-substantial`, S256.
2. Authentication checks the client binding and, without a live session, sends the browser to the picker
   (`localtest/api/idporten/authorize`); with one (an `AltinnStudioRuntime` cookie from an app's sign-in) it skips it.
3. The picker answers Authentication's `upstream/callback`, which signs the user in (the same four cookies as an app's
   sign-in, `Domain=local.altinn.cloud`) and redirects to `/api/cb?code&state`.
4. `/api/cb` checks `state`, posts the code to `authentication/api/v1/token/` with Basic authentication and the verifier,
   checks the id token's `nonce`, keeps the access, id and refresh tokens and `pid` in the session, stores
   `idp-sid:{sid}` in Redis and redirects to `/`. The access token carries `pid`, which Dialogporten needs to see a person.
5. Every `/api/graphql` call forwards the session's access token to Dialogporten; `/api/isAuthenticated` (polled by the
   SPA) refreshes it a minute before it expires (`refresh_token` grant, rotation).
6. `GET /api/logout` destroys the session and redirects to Authentication's `openid/logout/?id_token_hint=…`.
   Authentication ends its session (found through the `AltinnStudioRuntime` cookie), revokes the refresh tokens, calls
   `http://af.local.altinn.cloud:8000/api/frontchannel-logout?sid&iss` **from its container** (the BFF drops any session
   stored under that `sid`), and redirects through the fake ID-porten's `endsession` to `logout/handleloggedout` and the
   platform's start page. A logout that starts elsewhere (an app) ends the inbox's session the same way.

## The `/api/init-session` test seam

Kept, as upstream enables it everywhere but production: `POST /api/init-session` with `{"token": "<access token>"}`
writes a session for the token (no refresh, no id token, so `/api/logout` answers 401; it lasts until the token
expires) and answers `{"cookie": "arbeidsflate=<id>.<signature>", "expires": ...}`. Use a code-flow token, which carries
`pid` (`GET http://local.altinn.cloud:8000/localtest/api/authentication/token?client=localtest-smoke&userId=1337&authenticationLevel=3`,
its `access_token`); `/Home/GetTestUserToken`'s cookie JWT has no `pid` by production design, so Dialogporten would not
see a person. Playwright: `context.addCookies([{ name: 'arbeidsflate', value, domain: 'af.local.altinn.cloud', path: '/',
httpOnly: true }])`, then `page.goto('http://af.local.altinn.cloud:8000/')`.

## Differences from production

1. Plain http on `af.local.altinn.cloud:8000`; the BFF knows it (no forged `X-Forwarded-Proto`) and sets its cookies
   without `Secure` (patch 0003).
2. The three patches: discovery over http, the party cookies' domain, the BFF's cookies `secure: 'auto'`.
3. The SPA's environment detection maps `af.local.altinn.cloud` to `prod` (upstream's default): links out of the inbox
   (help, Altinn's information pages, access management) go to production sites. Dialogs' own links (go-to, attachments,
   receipts) are what the adapter and Correspondence write, on the stack's origin.
4. The CSP allows the stack's http origin; no HSTS.
5. Migrations at every start of the BFF container instead of a separate job.
6. No Azure App Configuration: feature flags at upstream's defaults (the notification log and forward-by-e-mail are off),
   no alert banner; no Application Insights, no OpenTelemetry.
7. The Maskinporten client key is generated at start and never checked; Maskinporten is the localtest app's fake.
8. Internet dependencies stay, as elsewhere in the stack (task 3 is won't do): the Inter font from altinncdn.no in the
   browser and the organisation list (`altinn-orgs.json`, cached 24 hours in Redis) in the BFF; without internet the
   service owners' names and logos are missing.

Nothing is faster than production in the inbox itself: the BFF's caches (organisations 24 hours, service resources
refreshed every 6 hours) are upstream's. The migrations at start are listed in `../../README.md` beside Profile's.

## Known gaps

- Both images are in the pinned build (`sha-6d928ad` in the manifest, 2026-10-23); `--dev arbeidsflate` and
  `--dev arbeidsflate-bff=Dockerfile.bff` build them from this folder.
- **An existing Postgres volume needs the init script once.** A volume created before this component predates
  `90-arbeidsflate.sh` (Postgres runs init scripts on an empty volume only), and the BFF then fails to connect: run
  `studioctl env reset -r localtest-v2 --yes` once (it also empties Storage and Dialogporten), or the script by hand (`podman exec -i localtest-v2-postgres sh <
  components/arbeidsflate/postgres-init/90-arbeidsflate.sh`).
- Upstream's nginx sends its security headers (the CSP among them) for deep links but not for `/`, whose `index.html`
  goes through the `.html` location; kept as upstream has it.
- Logout ends on the platform's start page, not back in the inbox: neither the BFF nor production Authentication uses a
  `post_logout_redirect_uri` (`../authentication/README.md`).
- **App dialogs are drafts.** The adapter gives an instance that is not submitted the status `DRAFT`, and the inbox lists
  drafts under **Utkast** (Drafts), not **Innboks**, which shows "no messages" for them. Submitted instances move on.
- The SPA uses the party in the URL (`?party=`) and the user's own party by default: select the organisation (DDG
  Fitness for party 500000) in the account menu to see the dialogs of the adapter's test instances.
- `podman logs` once showed only the BFF's stderr for a container's first start (stdout, `pnpm`'s banner and the pino
  lines, missing); after a restart both streams appeared. Not investigated (the journald log driver, not this component).
- Not verified: GraphiQL's assets in a browser (the page answers 200).

## Correspondence attachments (11b, implemented)

A Correspondence dialog's attachment clicked in the inbox downloads over plain http, without a picker when the user is
signed in (2026-10-21). Nothing changed in this component: the inbox renders the link as before, and the change is one
`#if !LOCALTEST` site in Correspondence, a third seeded client in Authentication and two cookie settings in Correspondence's
fakes, as the research below proposed.

**What changed.**

1. `../correspondence/vendor/src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs:26-31`: `#if !LOCALTEST` production's
   `new HttpDocumentRetriever()` `#else` `new HttpDocumentRetriever { RequireHttps = false }` `#endif`, with a `// localtest:`
   comment (Correspondence's fourth directive site, listed in its `vendor/UPSTREAM.md` and README).
2. `../authentication/fakes/LocaltestOidcClients.cs`: the seeded client `correspondence-localtest` (secret
   `correspondence-localtest-secret`, `client_secret_basic`, PKCE S256, redirect URI exactly
   `{PlatformEndpoint}correspondence/api/v1/idporten-callback`, allowed scopes `openid profile`, the two Correspondence's challenge
   requests at the pin), in the component README's table. The localtest app's `SeededClients.cs` does not list it: its helper
   never acts as Correspondence.
3. `../correspondence/fakes/`: `AltinnIdProviderSettings:ClientId` and `ClientSecret` set to that client
   (`LocaltestCorrespondence.cs`); the OIDC scheme's correlation and nonce cookies `SameSite=Lax`, `SecurePolicy=SameAsRequest`
   (`Auth/LocaltestAuthentication.cs`, a `Configure` for the named scheme registered after upstream's `AddOpenIdConnect`). Lax
   suffices: the callback is a top-level GET redirect from Authentication on the same site, and Lax cookies go with top-level GET
   navigations even across sites.

**Verification** (`--with arbeidsflate --with correspondence --dev correspondence --dev authentication`, pin `sha-16754da` for
the rest): Correspondence's `smoke-test.sh` has a browser-download section (12 checks, 76 in all, every one passing): the
challenge, the cookies, `authorize` straight to the callback with user 1337's session, `download?session=` with the bytes, the
one-time session, no cookie (the picker, no loop), another client's code and a missing correlation cookie (upstream's one
restart, then the bytes or the inbox), every walk bounded. In Chromium as user 1337: sign in through the picker, open the
dialog "Localtest smoke smoke-main" (`docs/attachment.png`: "1 vedlegg", the link "Smoke"), click the attachment: `smoke.txt`
downloaded, byte-equal to the blob, with no picker. This README's `smoke-test.sh` 56 of 56 with both groups on.

### The research (2026-10-21, before the change)

Read from the code, nothing run then; the line references are to the vendored Correspondence at its pin.

**What the inbox does.** `getAttachmentLinks` (`vendor/packages/frontend/src/api/hooks/useDialogById.tsx`, lines 126-145)
keeps the attachment URLs for the browser (`consumerType` Gui) and renders them as plain links (`target=_blank`): a
top-level navigation with cookies only, no `Authorization` header and no BFF involvement (the dialog token is only sent
with GUI actions). Correspondence writes the URL
`{CorrespondenceBaseUrl}/correspondence/api/v1/correspondence/{id}/attachment/{attachmentId}/download` into the dialog
(`CreateDialogRequestMapper.cs`, lines 446-448 and 644-651; "download all" `/attachments/downloadall`), here
`http://local.altinn.cloud:8000/correspondence/...`.

**What Correspondence does with it.** The download policy uses the `AllSchemes` cascade
(`../correspondence/vendor/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs`, lines 268-270): a bearer token
(Altinn or Maskinporten), then the dialog token, then OpenID Connect. Without a token and without `?session=`, it
challenges its own OIDC client (lines 92-112): authority `{PlatformGatewayUrl}/authentication/api/v1/openid`, that is
**production Authentication's OIDC provider**, not ID-porten (`IdportenSettings` and `IdPortenXacmlMapper` play no part);
client id and secret from `AltinnIdProviderSettings`; code flow with PKCE, query response mode, scopes `openid profile`;
redirect URI forced to `{CorrespondenceBaseUrl}/correspondence/api/v1/idporten-callback`, `acr_values`
`selfregistered-email idporten-loa-substantial` (lines 200-206); the OIDC state in the hybrid cache (Redis), not a cookie.
On the callback it caches the **access token** under a new GUID for 5 minutes and redirects to the original URL with
`?session={guid}` (lines 207-227). Back on the download, `CascadeAuthenticationHandler.HandleOpenIdConnectAsync` takes the
token out of the cache once and validates it with `IdportenTokenValidator`.

**What `IdportenTokenValidator` expects** (`Auth/IdportenTokenValidator.cs`, lines 19-49): the discovery document at
`Authority + "/.well-known/openid-configuration"` (line 24; `options.Configuration` is never set, so its
`AuthorizationEndpoint` branch never applies), the issuer from that document
(`http://local.altinn.cloud:8000/authentication/api/v1/openid/`), the keys from its JWKS, RS256 (Authentication's signing
default), no audience check. The access token has what Correspondence then needs: `scope` with `altinn:portal/enduser`
(`TokenHelper.cs`, lines 13-16), `pid`, `acr`, user and party claims; authorization goes through
`AltinnTokenXacmlMapper`, since the issuer is Altinn's.

**Why it fails here, and the third directive site it needs.** Line 26 builds the retriever as `new HttpDocumentRetriever()`,
whose `RequireHttps` defaults to true: over plain http `GetConfigurationAsync` throws, the cascade falls through to a new
challenge, and Authentication's live session hands out a new code at once: a redirect loop. The fakes' `RequireHttpsMetadata
= false` (`fakes/Auth/LocaltestAuthentication.cs`, line 30) does not reach this retriever, and the validator is a concrete
class injected by type, so a fake cannot replace it. The site: `#if !LOCALTEST` `new HttpDocumentRetriever()` `#else` `new
HttpDocumentRetriever { RequireHttps = false }` `#endif` (Correspondence's fourth site counting task 7b's and the two
additive hooks; the component README's "needs a report first" was this section).

**The OIDC client.** Production Authentication, as production does (going to the fake ID-porten directly would change the
issuer, the claims and the authorization path); Authentication reuses the browser's session (`OidcServerService.cs`,
lines 132-165), so a user signed in to the inbox sees no picker. Authentication needs a third seeded client,
`correspondence-localtest` with a secret, redirect URI exactly `{PlatformEndpoint}correspondence/api/v1/idporten-callback`,
allowed scopes `openid profile` (every requested scope must be allowed, and the two seeded clients do not allow
`profile`), and Correspondence's fakes set `AltinnIdProviderSettings:ClientId` and `ClientSecret` to match (today
upstream's Development placeholders `clientId`/`clientSecret` apply). The fake ID-porten needs nothing: it is only
Authentication's upstream.

**The cookie decision.** The correlation cookie is `SecurePolicy=Always`, `SameSite=None` (`DependencyInjection.cs`, lines
103-107), and the nonce cookie keeps the framework default. Over http the browser either refuses the `Secure` cookie or,
with `Secure` stripped by the front door, rejects `SameSite=None` without `Secure`: the callback fails with "Correlation
failed", retries once and lands on `ArbeidsflateBaseUrl`. Decision proposed: in Correspondence's fakes, after upstream's
configuration, both cookies `SameSite=Lax` and `SecurePolicy=SameAsRequest`. No vendor change; Lax is enough because the
callback is a top-level GET on the same site. Upstream's best-effort cookie expiry on the retry path keeps sending
`Secure`/`None` and is ignored, which is harmless.

**Proposed brief (followed).** Goal: a Correspondence attachment clicked in the inbox downloads over plain http, without a picker
when the user is signed in. Steps: (1) the `#if !LOCALTEST` site at `IdportenTokenValidator.cs:26`, listed in
`../correspondence/vendor/UPSTREAM.md` and README; (2) seed `correspondence-localtest` in the authentication component
(`fakes/LocaltestOidcClients.cs` and its README table; `client_secret_basic`, PKCE, scopes `openid profile`, redirect
`{PlatformEndpoint}correspondence/api/v1/idporten-callback`); (3) Correspondence's fakes: `AltinnIdProviderSettings`
client id and secret, both cookies Lax / `SameAsRequest`; (4) remove this gap and the Correspondence README's.
Expected: one vendor site, everything else in the two components' fakes. Verification: a curl walk from the download URL
with the `AltinnStudioRuntime` cookie (302 to `authorize`, 302 to `idporten-callback?code`, 302 to `download?session=`,
200 with the bytes), a reused `session` refused with 401, and in Chromium: sign in to the inbox, open a correspondence,
click its attachment.

## Tests

`./smoke-test.sh [BASE]` needs `--with arbeidsflate --with dialogporten` (the instance's dialog needs the adapter) and goes through the front
door: readiness and the BFF's `/api/health` (Postgres, Redis, OIDC over http, Dialogporten); the SPA (the stream URL in
`index.html`, an asset with its cache headers, the CSP and no HSTS on a deep link, GraphiQL behind `/api/`); the BFF's
session cookie without `Secure` straight from nginx (`arbeidsflate:80`, from inside the stack's network) and unchanged through the front door (task 21b); the login
round trip with a cookie jar as user 1337 (picker, `/api/cb`, the session cookie without `Secure`,
`/api/isAuthenticated`); the fixture app pushed and an instance of it for party 500000 followed to its dialog; GraphQL
through the BFF (parties, the dialog in `searchDialogs`, `dialogById` with a dialog token, no CORS headers on the
same-origin call, the stream URL's preflight and 401 on Dialogporten); the profile page against production Profile (the
user, `altinnPersistentContext` set without `Secure`, a favourite added, read back from Profile and removed, `showClientUnits` changed and restored); the init-session
seam; the refusals; logout through the BFF to `handleloggedout`; and the front-channel logout of a second session by
Authentication's server-side call: 59 checks (56 before task 21b). Its header lists what it leaves behind.

## Verified

2026-10-21, phase 2, on this machine (pin `sha-3143730` for every other component, these two images built here, the
Postgres image built from this checkout with `--dev postgres`, the volume reset once):

- **Images**, one at a time with `podman build`: `localtest-v2-arbeidsflate-bff:dev` **1.47 GB**, first build 1 min 22 s
  (base image pull, whole-workspace `pnpm install`, `pnpm turbo build`, which also builds the docs site; the BFF types
  generation logs two Redis connection errors at build time, upstream's, harmless); `localtest-v2-arbeidsflate:dev`
  **67 MB**, 48 s (filtered install). Disk went from 14 to 11 GB free with both.
- **Start**: `env up -r localtest-v2 --with arbeidsflate` healthy in 39 s; the BFF runs its four migrations and listens
  about 2 s after its container starts.
- **`smoke-test.sh`: 56 of 56**, three times: with `--with arbeidsflate`, again after the nginx resolver change, and with
  `--with everything`. The username lookup (the profile page's `partyUsername`, Maskinporten through the fake, the
  exchange, the Register's `dialogporten/parties/query`) answers `SophieDDG` for user 1337, and Redis database 2 holds
  both cached Maskinporten tokens: it works, it does not fail silently.
- **Front-channel logout**: nginx's access log shows Authentication's server-side `GET /api/frontchannel-logout?sid=…
  &iss=http://local.altinn.cloud:8000/authentication/api/v1/openid/` (no user agent, from the host gateway) answered
  200 for every logout, and the test's second session is gone afterwards.
- **Browser** (Chromium through `playwright-cli`, user 1337): opening `http://af.local.altinn.cloud:8000` goes straight to
  the picker and back to the inbox, in Nynorsk (the user's Profile setting). After choosing DDG Fitness in the account
  menu, the party cookies were written for `.local.altinn.cloud` (patch 0002). The adapter's test instances are under
  **Utkast** (`docs/drafts.png`). Opening one opened the subscription stream: `POST
  http://local.altinn.cloud:8000/dialogporten/graphql/stream` with the dialog token, 200 `text/event-stream`, with
  Dialogporten's `Access-Control-Allow-Origin` for the inbox. Starting the instance's process and uploading an attachment
  in Storage then showed the attachment on the open page without a reload: the stream event made the SPA fetch the
  dialog again (`docs/dialog.png`: "1 vedlegg, live-update.txt", the go-to button to the app on the stack's origin). The
  profile page (`docs/profile.png`) shows Profile's data: KRR phone and e-mail, two notification addresses, eleven
  parties, the language. Logout from the account menu went through Authentication and the fake ID-porten to the
  localtest start page, nginx logged the front-channel call, and the old session cookie got 401. Console: only the
  expected warnings (no Application Insights key; the Cross-Origin-Opener-Policy header ignored over http; the BFF's
  401 for `/api/isAuthenticated` before sign-in).
- **Regressions with the group on**: Dialogporten adapter 39 of 39, Authentication 32 of 32, Dialogporten 60 of 60, root
  26 of 26. With `--with everything`: Correspondence 64 of 64.
- **Memory with `--with everything`** (`free -m` a minute after start: 4164 MB used of 7949, 3784 MB available;
  `podman stats --no-stream`): `arbeidsflate-bff` 362 MB (the largest container), `arbeidsflate` 5 MB, Access Management
  198, Postgres 242, Events 266, Dialogporten WebApi 174, Service 141, GraphQL 132, Correspondence 113, workflow engine
  102, Profile 99, Storage 95, Authentication 69, Resource Registry 68, Register 67, Notifications 63, Authorization 50,
  Receipt 45, adapter 44, PDF 43, localtest 13, Redis 4.
- **Default stack** (the group off): `run-platform-regression.sh --no-apps` with the three apps under `studioctl run
  --dev-frontend` and the monorepo's built frontend served with `Access-Control-Allow-Origin`: 44 of 44.

2026-10-21, phase 1 (no images, no stack): `git apply --check` of both patches against `git archive bb74dff`; a scratch
copy with the patches applied: `pnpm install --frozen-lockfile --filter 'bff...'` and `pnpm --filter bff run typecheck`
pass, `config.oidcDiscoveryUrl` is `http://…/.well-known/openid-configuration` for an http value and `https://…` for
production's value; `pnpm install --frozen-lockfile --filter 'frontend...'` and `pnpm --filter frontend run build` pass
and the bundle has the patched `getCookieDomain`; `shellcheck` on the scripts; studioctl's `go build ./... && go test
./...`.

## Run it

```sh
sudo studioctl env hosts add -r localtest-v2              # once: af.local.altinn.cloud and the stack's other names
studioctl env reset -r localtest-v2 --yes                # once, if the Postgres volume predates this component
# studioctl may build the two images of the --dev name side by side; on a small machine build them one at a time
# first, so that the runtime's builds below are cache hits (the tags are the ones studioctl gives a --dev build):
podman build -f Dockerfile.bff -t localtest-v2-dev-arbeidsflate-bff .
podman build -f Dockerfile.frontend -t localtest-v2-dev-arbeidsflate .
studioctl env up -r localtest-v2 --with arbeidsflate --with dialogporten \
  --dev arbeidsflate=Dockerfile.frontend --dev arbeidsflate-bff=Dockerfile.bff   # or without --dev: the pinned images
```

Then open `http://af.local.altinn.cloud:8000`, sign in as a test user, choose the party in the account menu, and start
an app instance (`studioctl run` in an app) to see its dialog under **Utkast**.
