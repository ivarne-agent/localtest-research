# TODO

The open work on the new localtest, gathered from the "Not yet" paragraph of [`localtest/README.md`](localtest/README.md),
the components' "Known gaps", [`localtest/reviews/`](localtest/reviews/README.md) and the plan indexes under
[`planned-components/`](planned-components/). Each task is written so it can become a subagent's brief once approved.

Status values: **unapproved** (not yet discussed, or discussed and not yet released to an agent), **approved**
(design settled, an agent may be dispatched), **deferred** (discussed, parked until something needs it), **won't do** (discussed and dropped),
**in progress** (agent name, worktree, branch), **done** (commit).
Nothing is dispatched while it is unapproved.

Rules every brief inherits: work in a worktree of this repository under `/home/agent/code/.worktrees/<task>/`; a
change to a `vendor/` file is only ever a `#if !LOCALTEST` (production first) / `#else` / `#endif` block, everything
else goes in `fakes/`; a component's Dockerfile mirrors upstream's with `localtest:` deviations; the old localtest in
`altinn-studio/src/Runtime/localtest` is reference only, never a basis; one image build or AOT publish at a time on
this machine (7.8 GB RAM), never next to a Cypress run; extend the component's `smoke-test.sh`; update the component
README and `vendor/UPSTREAM.md`; no `Co-Authored-By` trailers. The studioctl side of a
component (container spec, image pin, proxy prefix, provisioning calls, health) is changed in the vendored studioctl
source under `localtest/studioctl/src/cli/` (ordinary commits on top of the baseline, see
`localtest/studioctl/UPSTREAM.md`), built with `make dev-install` there and verified with
`studioctl env up -r localtest-v2 --dev <component>`; nothing is edited in an altinn-studio checkout.

| # | Task | Status | Size | Needs the stack |
| --- | --- | --- | --- | --- |
| 1 | [Vendor sync check](#1-vendor-sync-check) | deferred (2026-10-20) | small | no |
| 2 | [Authentication as an OIDC provider, logout, `pid`](#2-authentication-as-an-oidc-provider-logout-pid) | deferred (2026-10-20: until a relying party in the stack needs the code flow, i.e. task 11 or a Dialogporten end-user test) | medium | yes |
| 3 | [Local altinncdn.no](#3-local-altinncdnno) | won't do (2026-10-20: the stack may read altinncdn.no) | small | yes |
| 4 | [Maskinporten token endpoint and the shared `IMaskinportenService` fake](#4-maskinporten-token-endpoint-and-the-shared-imaskinportenservice-fake) | done (2026-10-20, commit 798f18e) | small | yes |
| 5 | [Resource Registry component](#5-resource-registry-component) | unapproved | large | yes |
| 6 | [Notifications](#6-notifications) | unapproved | medium | yes |
| 7 | [Correspondence](#7-correspondence) | unapproved | medium | yes |
| 8 | [Inspection GUI](#8-inspection-gui) | unapproved | medium | yes |
| 9 | [Dialogporten core](#9-dialogporten-core) | unapproved | large | yes |
| 10 | [Dialogporten adapter and the Storage outbox](#10-dialogporten-adapter-and-the-storage-outbox) | unapproved | large | yes |
| 11 | [Arbeidsflate](#11-arbeidsflate) | unapproved | large | yes |
| 12 | [Review leftovers](#12-review-leftovers) | unapproved | small each | mostly |
| 13 | [studioctl integration leftovers](#13-studioctl-integration-leftovers) | unapproved | medium | yes |
| 14 | [Profile beyond the Register](#14-profile-beyond-the-register) | unapproved | unknown | yes |
| 15 | [Localtest URLs in the Maskinporten client package](#15-localtest-urls-in-the-maskinporten-client-package) | unapproved | small | no |

Suggested order: 5, then 6 to 8 as capacity allows, then 9 to 11 in that
order. 13 stays with the maintainer. 12 and 14 are fillers.

## 1. Vendor sync check

**Goal.** A GitHub workflow in this repository that proves every `components/<name>/vendor/` is upstream at its pinned
commit plus `LOCALTEST` directive blocks and nothing else, and reports when upstream has moved past the pin.

**Design (settled so far).** The pin stays in `vendor/UPSTREAM.md` (decided 2026-10-20, no separate machine-readable
pin file for now); the check parses it the way `localtest/tools/repin-vendor.py` already does, so the two share one
parser. For each component: fetch the pinned commit of the upstream repository (shallow, `git fetch --depth 1
<url> <sha>`), `git archive` the copied paths, `diff -rq` against `vendor/`. A differing file passes only if the diff
consists of complete `#if !LOCALTEST … #else … #endif` blocks whose `!LOCALTEST` branch equals upstream's text.
Report, do not fail, when upstream `main` has commits after the pin (count and the newest subject).

**Open.** Pull requests and pushes to `main` only, or also a weekly schedule that opens or updates one issue when
upstream has moved? Does the Dockerfile comparison (upstream Dockerfile versus ours with `localtest:` lines removed)
belong in the same check?

**Verification.** The workflow passes on `main`; a deliberate one-line edit in a vendored file fails it with the file
and hunk named; a deliberate directive block passes.

**Inputs.** `localtest/tools/repin-vendor.py`, every `vendor/UPSTREAM.md`, `planned-components/studioctl-integration.md`
section 1.

## 2. Authentication as an OIDC provider, logout, `pid`

**Goal.** Production Authentication in the stack issues tokens to a registered OIDC client through the
authorization-code flow (what Arbeidsflate's BFF and any other local relying party needs), its logout works, and the
question whether the issued tokens carry `pid` is answered empirically.

**What is known.**

- The cookie token (`AltinnStudioRuntime`, what `/Home/GetTestUserToken` returns) has no `pid` **by design**:
  `ClaimsPrincipalBuilder.GetClaimsPrincipal(…, isAuthCookie: true)` skips it (vendored
  `Helpers/ClaimsPrincipalBuilder.cs:200-202`). Production behaves the same. The "pid is lost" finding in
  `planned-components/dialogporten/arbeidsflate.md` section 3.4 was made on that token and is most likely not a
  defect. The access token the OIDC `token` endpoint issues builds `pid` from the session's `ExternalId`
  (`urn:altinn:person:identifier-no:{SSN}`, `OidcServerService.cs:1327-1337`, `ClaimsPrincipalBuilder.cs:80`). That
  endpoint has never been reached in the stack, because the code flow needs repositories the component lacks.
- `GET openid/logout` answers 500: `OidcProviders:idporten:LogoutEndpoint` is unset and the fake ID-porten has no
  end-session endpoint (verified on the stack, `arbeidsflate.md` section 3.4 item 1).
- `GeneralSettings:OidcRefreshTokenPepper` is `localtest-pepper` in `fakes/LocaltestAuthenticationExtensions.cs:62`;
  production decodes it with `Convert.FromBase64String` (three sites in `TokenService.cs`), so client-secret
  authentication and refresh tokens fail (`shared-prerequisites.md` section 3.3 item 3).

**Scope.**

1. Fakes in `localtest/components/authentication/fakes/Persistence/`: in-memory `IOidcServerClientRepository`
   (seeded from `Localtest:Authentication:OidcClients`, secrets hashed with the production
   `ClientSecretHasher.HashHmac` and the pepper), `ILoginTransactionRepository`, `IAuthorizationCodeRepository`,
   `IRefreshTokenRepository`. Shapes and sizes in `shared-prerequisites.md` section 3.4 (about 350 lines). Registered
   next to the three existing in-memory repositories. No directive sites expected.
2. Pepper: `bG9jYWx0ZXN0LXBlcHBlcg==` (base64 of the same string). Configuration only.
3. Fake ID-porten end-session endpoint in the localtest app (`localtest/localtest/IdPorten/IdPortenApi.cs`):
   `GET localtest/api/idporten/endsession?id_token_hint&post_logout_redirect_uri&state` answers 302 to
   `post_logout_redirect_uri` (with `state`), listed as `end_session_endpoint` in the discovery document;
   `OidcProviders:idporten:LogoutEndpoint` set in the authentication fakes. The fake keeps no login session of its
   own (codes are one-time, 60 s), so there is nothing to invalidate; the picker simply shows again on the next
   `authorize`.
4. A seeded localtest client for tests (id `localtest-smoke`, a plain secret, redirect to a `/Home` helper) and a
   headless helper in the localtest app, `GET /Home/GetTestUserToken?client=<id>…` or a new
   `localtest/api/idporten/…` helper, that drives the production code flow for that client and returns the token
   response. This is what answers the `pid` question and what the Dialogporten smoke test later uses (open question 5
   in `planned-components/dialogporten/README.md`).
5. If the code-flow access token still lacks `pid`: trace `IdentifyOrCreateAltinnUser` (Profile lookup by SSN,
   `UserProfile.Party.SSN` deserialisation in `ProfileService.cs:51`) and report before adding any directive site.

**Open.** Should the Arbeidsflate client (`arbeidsflate-localtest`, redirect `http://af.local.altinn.cloud:8000/api/cb`,
front-channel logout URL) be seeded now, so task 11 only adds the containers? Where does the helper live:
`/Home` (old contract, grows) or `localtest/api/idporten/` (new API)? Is a third directive site in Authentication
acceptable if step 5 finds the loss inside vendored code?

**Verification.** `components/authentication` smoke test extended: discovery lists `end_session_endpoint`; the code flow
for the seeded client (authorize → picker → callback → `POST token` with `client_secret_basic` and PKCE) returns
`access_token`, `id_token`, `refresh_token`; the access token's `pid` equals the picked user's national identity
number; refresh rotates; `GET openid/logout?id_token_hint=…` answers 302 and a later refresh fails; the existing
`goTo` flow and the `/Home` contract are unchanged (`localtest/localtest/smoke-test.sh`, 65 checks). Run
`run-platform-regression.sh` afterwards.

**Inputs.** `planned-components/dialogporten/shared-prerequisites.md` section 3, `arbeidsflate.md` section 3,
`dialogporten-core.md` section 7, the vendored `OidcServerService.cs`, `TokenService.cs`, `ClaimsPrincipalBuilder.cs`,
`PersistanceDependencyInjection.cs`; `localtest/reviews/authentication.md`.

## 3. Local altinncdn.no

**Goal.** Nothing in the stack needs internet access. Today Authentication's `exchange/maskinporten` reads
`https://altinncdn.no/orgs/altinn-orgs.json`, Storage's Studio and metrics endpoints read the same file, and pdf3
loads fonts and the logo from the CDN (`localtest/reviews/README.md`, "Internet").

**Design.** The localtest app serves a committed copy under `localtest/api/cdn/` (or a dedicated host
`altinncdn.local.altinn.cloud`, decide); components get the URL by configuration where upstream has a setting, and
only where upstream hard-codes the host does a `#if !LOCALTEST` site appear (list the sites first, from
`shared-prerequisites.md` section 6). The committed `altinn-orgs.json` is trimmed to the organisations the test data
uses plus `ttd` and `digdir`, or a full copy (open question 7 in the Dialogporten plan).

**Open.** Path on the main host or a dedicated host? Full or trimmed `altinn-orgs.json`? Does pdf3 (monorepo image,
not vendored here) get its fonts through configuration, and if not, is it out of scope?

**Verification.** With the sandbox's outbound network blocked for the containers (or `altinncdn.no` resolved to
nothing), `/Home/GetTestOrgToken` and the Storage endpoints that read the org list still work; the root smoke test
passes.

## 4. Maskinporten token endpoint and the shared `IMaskinportenService` fake

**Goal.** A `POST localtest/api/maskinporten/token` in the localtest app (`client_credentials` with a JWT grant,
answering a token the production `exchange/maskinporten` accepts) and one `IMaskinportenService` fake source file
compiled into the .NET consumers that need a Maskinporten token (the Dialogporten adapter, Dialogporten, later
Correspondence). Contract in `shared-prerequisites.md` sections 1 and 2.

**Decisions (2026-10-20).**

- Client id to organisation number: `consumer_org` in the assertion when present, else a nine-digit client id is the
  organisation number, else the default 991825827 (Digdir). No configured client table. Unknown clients are accepted
  and logged, not refused.
- The assertion's signature, `exp` and `aud` are not checked; only the payload is read.
- The fake's exchange keeps production semantics: `test=true` only when the consumer's `UseAltinnTestOrg` is true.
- The `IMaskinportenService` fake is one canonical source under `localtest/components/shared/fakes/Maskinporten/`,
  **copied** into each consumer's `fakes/` when that consumer is vendored (no shared build context for now); a small
  host under `components/shared/` compiles it against `Altinn.ApiClients.Maskinporten` 10.1.0 and is its smoke test.
  Task 15 is the long-term replacement.
- No consumer exists in the stack until tasks 9 to 11, so the smoke tests are the only callers at first.

**Verification.** `localtest/localtest/smoke-test.sh`: a token from the endpoint exchanges through production
`exchange/maskinporten`; the fake, compiled into a scratch console host, obtains an Altinn token.

## 5. Resource Registry component

**Goal.** Vendor production `Altinn/altinn-resource-registry` as `localtest/components/resource-registry/` (Option A in
`planned-components/dialogporten/resource-registry.md`), on the shared Postgres, deriving app resources from Storage's
application list and the shared `policy-store` volume as production does; retire the stand-ins Access Management and
the PDP carry today (section 3.9); the app runner (studioctl-server's `AppProvisioner`) calls it after the policy push
(section 3.8).

**Design.** As the report: two directive sites, fakes under `Altinn.ResourceRegistry.Localtest`, Dockerfile mirroring
upstream, init script added to `localtest/postgres/`, proxy prefix `/resourceregistry/`, image added to
`.github/workflows/images.yml`, a runtime component in the studioctl patch (`localtest/studioctl/`).

**Decisions (2026-10-20).**

- The production 10-minute application cache in `GetResourceList` goes away locally: a fake replaces the Storage
  applications client (or the cache) so `resourcelist` reflects a pushed app at once. Prefer a replaced service in
  `fakes/` over a directive; no `api/localtest/refresh` endpoint is needed for the cache.
- Non-app resources (today the PDP's `policies/resourceregistry/` seed) are a built-in seed in the registry's fakes,
  applied at start through the production service layer, like the Register's built-in seed. The provisioner does
  not push platform seed data. Correspondence resources wait for task 7.
- A separate `resourceregistrydb` on the shared Postgres (18); the Yuniql run on 18 is the first thing to verify.
- The org list is read from altinncdn.no as in production (task 3 is won't do).
- The registry is the only source for `urn:altinn:resource` policies; the PDP's `resourceregistry/` policy folder moves
  into the seed and its fallback goes.
- The provisioner (studioctl-server `AppProvisioner`, in `localtest/studioctl/src/cli/`) calls
  `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` after the policy push,
  as Designer does; the runtime gets the component spec, image and proxy prefix there too.

**Open.** Which of the Access Management and PDP stand-ins can be removed in the same change without breaking the six
platform specs (or do they go in a second step)? The `updated` feed's start for Access Management's sync.

**Verification.** `components/resource-registry/smoke-test.sh` as planned in section 6.4; `run-platform-regression.sh`
still passes; Access Management's refresh still produces the same `app_{org}_{app}` resources.

## 6. Notifications

**Goal.** Apps that order notifications (`notifications/api/v1/orders…`) get production answers instead of a 404, and a
tester can see what was ordered.

**Design to settle.** Production `Altinn/altinn-notifications` uses Postgres and Kafka, plus e-mail and SMS providers.
Options: (a) vendor the orders API with fakes for Kafka (in-process queue) and the providers (captured, listed under
`notifications/api/localtest/`), the Events model; (b) a fake-only service that validates and stores orders. The
vendoring rule says (a) when the build chain allows it; the Kafka dependency decides. Research first: a short report
under `planned-components/notifications/` (consumers in app-lib, the orders API surface, the build chain, Kafka
coupling, effort), then the implementation as a second task.

**Open.** Everything above; whether Correspondence (task 7) should share the capture GUI.

## 7. Correspondence

**Goal.** Apps that send correspondence (`correspondence/api/v1/…`) get production answers, and the result is visible
locally (and later in the inbox, task 9 to 11).

**Design to settle.** Production `Altinn/altinn-correspondence` needs Postgres, Azure blobs (the Storage component's
file-backed blob pattern applies), Dialogporten, the Resource Registry, Notifications and Maskinporten. It therefore
comes after tasks 4, 5, 6 and 9. Research first, as for Notifications, under `planned-components/correspondence/`.

## 8. Inspection GUI

**Goal.** One page in the localtest app that shows what the components' `api/localtest/` endpoints expose: test users
and parties (Register), events and webhook deliveries (Events), the refresh state and seeded roles (Access
Management), app routes and upstreams (proxy), later notification and correspondence captures. Replaces the old
localtest's StorageExplorer and debug pages for the things developers actually look at.

**Design to settle.** Static HTML plus fetch in the localtest app (Native AOT: no server rendering frameworks, no
build step, source-generated JSON only), under `/localtest/` with the data read from the components through the
front door, or a small SPA built in CI? Which endpoints are missing for a useful view (Storage has no `api/localtest/`
today: instances by app would need one, or pgAdmin stays the answer)?

**Open.** Scope of the first version; whether Storage gets a localtest listing endpoint.

## 9. Dialogporten core

Vendor the WebApi, GraphQL and Service hosts per `planned-components/dialogporten/dialogporten-core.md`: build chain,
Dockerfile, the thirteen `LocalDevelopment` flags, three additive `Program.cs` hooks and two directive sites, Redis,
Postgres 18 init script, proxy routes, smoke test on the service-owner API with the org token. Needs tasks 4 and 5
(boots with `UseLocalDevelopmentResourceRegister=true` for its own smoke test before 5, but no dialog-creating test
until 5 exists). End-user smoke needs the `pid` token from task 2.

## 10. Dialogporten adapter and the Storage outbox

Vendor the adapter per `dialogporten-adapter.md` (five directive sites, prototype in `adapter-prototype/`), switch
Storage's outbox on in its fakes, the in-process Wolverine path POSTing commands to the adapter's localtest enqueue
endpoint. Needs 4 and 9. Open questions 1 and 2 in the Dialogporten plan (receipt link, outbox delays).

## 11. Arbeidsflate

Vendor the pnpm monorepo per `arbeidsflate.md`: pristine tree plus two patches, dedicated host `af.local.altinn.cloud`,
nginx in front of the BFF with `X-Forwarded-Proto: https`, Postgres and Redis, login through the OIDC client from
task 2. Needs 2, 9 and 10. Open questions 8 and 10 (upstreaming the patches, the app-frontend link).

## 12. Review leftovers

Small items from `localtest/reviews/` and the components' "Known gaps", each its own brief:

- Localtest app: `HEAD` on most endpoints; a WebSocket test in `localtest/localtest/smoke-test.sh`.
- Events: `DELETE events/api/localtest/events` does not drain Wolverine's in-memory queues; Slack deliveries recorded
  without their event.
- Access Management: a stub for the SBL bridge's Altinn 2 roles calls (today a proxy 404), if any flow needs it.
- Authorization and Events: Debug logging is very noisy under `ASPNETCORE_ENVIRONMENT=Development`; a localtest
  logging override.
- Workflow engine: state reset (left as decided; revisit only on request).

## 13. studioctl integration leftovers

From `planned-components/studioctl-integration.md`, sections 1, 4, 5, 6 and its open questions, against the vendored
studioctl in `localtest/studioctl/src/cli/` (upstreamed later as one patch, see `localtest/studioctl/UPSTREAM.md`): moving the stack into the monorepo (`src/Runtime/localtest-v2/`) with its image workflow, the
`/health` body (`ok` versus the integration tests' `Healthy` snapshot), docs and the bundled skill, Cypress and
`StudioctlEnvironment` tests in CI against the new runtime, the rollout and the default flip, re-push on app change
(watch, restart or `studioctl app sync`), frontend version override and cookie-domain rewriting (port or drop), and
two front-door gaps seen on 2026-10-20: studioctl-server answers 404 for a platform prefix without its trailing slash
(`/storage`), where the localtest app redirects, and for `/app-frontend/`. The maintainer's; not for a subagent until
split into pieces.

## 14. Profile beyond the Register

The Register component answers the Profile endpoints Authentication and Authorization call. Apps and Dialogporten use
more of Profile (notification settings, favourites, professional notification addresses). Research first: which
Profile endpoints app-lib and the inbox stack call, and whether vendoring `Altinn/altinn-profile` (Postgres, its own
Register and SBL bridge clients) beats extending the Register fakes.

## 15. Localtest URLs in the Maskinporten client package

**Goal.** `Altinn.ApiClients.Maskinporten` accepts a configured token endpoint and exchange endpoint (or a custom
environment) so that a consumer can point it at the localtest app and production Authentication by settings alone,
and the compiled-in `IMaskinportenService` fake from task 4 can be deleted. An upstream change in
`Altinn/altinn-apiclient-maskinporten` (the `switch` tables in `MaskinportenService.GetTokenEndpoint` and
`GetTokenExchangeEndpoint` throw for unknown environments; `MaskinportenSettings` has no URL fields), then a version
bump in each consumer. Also covers the BFF, which is already configurable, and Correspondence's own token service.
