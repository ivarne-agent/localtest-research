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

**Optional component groups (decided 2026-10-20).** `localtest/studioctl/src/cli/internal/cmd/env/localtestv2/groups.go`
becomes a group table: each group has a default (all off) and the groups it implies; `--with x` enables `x` and,
transitively, what it implies; `--without` removes only the named group. Groups: `dialogporten` (Redis, the three
Dialogporten hosts, the adapter from task 10) implies nothing; `receipt` implies nothing; `correspondence` implies
`dialogporten`; `everything` implies every other group. Task 16 lands the table (its first commit, merged ahead of the
rest); 7 and 10 build on it. **Front door for components that are not running (decided 2026-10-20, in task 16's
studioctl step):** `env up` writes every known component's route, enabled or not, and studioctl-server answers `503`
with an RFC 9457 ProblemDetails (`application/problem+json`; a small HTML page when `Accept` prefers `text/html`)
naming the component and group and the exact `studioctl env up -r localtest-v2 --with <group>` command (and
`--with everything`); an enabled component that does not answer gets the same shape pointing at `studioctl env logs`.
A path matching no route still falls through to the localtest app.

| # | Task | Status | Size | Needs the stack |
| --- | --- | --- | --- | --- |
| 1 | [Vendor sync check](#1-vendor-sync-check) | deferred (2026-10-20) | small | no |
| 2 | [Authentication as an OIDC provider, logout, `pid`](#2-authentication-as-an-oidc-provider-logout-pid) | done (2026-10-20, commits 4b9b5f7..ddd0ecf; code-flow tokens carry `pid`, Dialogporten end-user API 200; pin sha-a3f62a8) | medium | yes |
| 3 | [Local altinncdn.no](#3-local-altinncdnno) | won't do (2026-10-20: the stack may read altinncdn.no) | small | yes |
| 4 | [Maskinporten token endpoint and the shared `IMaskinportenService` fake](#4-maskinporten-token-endpoint-and-the-shared-imaskinportenservice-fake) | done (2026-10-20, commit 798f18e) | small | yes |
| 5 | [Resource Registry component](#5-resource-registry-component) | done (2026-10-20: 5a commits e0cba40..b6f4c47, pin a3422419; 5b commits 9b7278d..aaa099e) | large | yes |
| 6 | [Notifications](#6-notifications) | done (2026-10-20, commits 45e99a1..0b38452, pin a4aa959; follow-up: seed data for SMS numbers and organisation addresses, see section 6) | medium | yes |
| 7 | [Correspondence](#7-correspondence) | done (2026-10-21, commits 94c1acb..e96f904; group `correspondence` implying `dialogporten`, port 8033; 7b done the same day closed the 6-digit party-id gap with one directive site, see `planned-components/correspondence/party-ids.md`; pin sha-735e5d2, proven 2026-10-21) | medium | yes |
| 8 | [Inspection GUI](#8-inspection-gui) | deferred (2026-10-20: possible overlap with Altinn Studio's admin of running services, see task 17; plan in `planned-components/inspection-gui/`) | medium | yes |
| 9 | [Dialogporten core](#9-dialogporten-core) | 9a done (2026-10-20, commits 4ab03f0..76b8787, pin ec333e4); 9b done (2026-10-20, commits 4749bd7..d7827bf, pin sha-d7827bf) | large | yes |
| 10 | [Dialogporten adapter and the Storage outbox](#10-dialogporten-adapter-and-the-storage-outbox) | done (2026-10-21, commits c940fd6..3c9b03f; adapter in the dialogporten group, Storage outbox on in the default stack, one `#if !LOCALTEST` site added in Dialogporten for http URLs; pin sha-3c9b03f, proven 2026-10-21 with `--with everything`) | large | yes |
| 11 | [Arbeidsflate](#11-arbeidsflate) | done (2026-10-21, commits dc1ae37..b136eabf; group `arbeidsflate` implying `dialogporten`, host `af.local.altinn.cloud:8000`, nginx 8037 and BFF 8038, two build-time patches on an unmodified vendor tree, 56-check smoke test, inbox verified in Chromium; 11b done the same day (Correspondence attachments from the inbox, commits 789edcd3..8e510e91, one directive site in Correspondence, see section 11); pin sha-7f21882, proven 2026-10-21 on plain images with `--with correspondence`: root, authentication, correspondence and dialogporten smoke tests pass) | large | yes |
| 12 | [Review leftovers](#12-review-leftovers) | done for the audit findings (2026-10-21, commits 6ba99ee..90c6bc4: Postgres in UTC, two Events directive sites for Dialogporten sources, authorization and root smoke tests fixed, Storage standalone test removed, `reviews/test-apps.md`; still open: HEAD on the localtest app, a WebSocket test, the Events reset not draining queues, Slack deliveries without their event, the Altinn 2 roles stub; pin sha-3143730, proven 2026-10-21 on plain images) | small each | mostly |
| 13 | [studioctl integration leftovers](#13-studioctl-integration-leftovers) | unapproved | medium | yes |
| 14 | [Profile beyond the Register](#14-profile-beyond-the-register) | done (2026-10-21, commits f7b7f50..097b474; production Profile in the default stack on port 8036, `/profile` off the Register, two additive hooks; pin sha-0485d0a, proven 2026-10-21 on plain images) | unknown | yes |
| 15 | [Localtest URLs in the Maskinporten client package](#15-localtest-urls-in-the-maskinporten-client-package) | unapproved | small | no |
| 16 | [Receipt](#16-receipt) | done (2026-10-21, commits f40027a..319a712; group `receipt`, port 8034; group table and the 503 front door landed from this task as 6b41c96 and adf6ee6; pin sha-902ddfc, proven 2026-10-21) | medium | yes |
| 17 | [Admin of running services and the inspection GUI](#17-admin-of-running-services-and-the-inspection-gui) | done (2026-10-20: plan in `planned-components/admin-running-services/`; finding: not straightforward, the admin frontend depends on the runtime gateway, Kubernetes, Gitea teams and Designer\'s database; recommends building 8 as planned with admin\'s instance labels; 8 stays deferred pending the owner\'s decision) | small | no |
| 18 | [Test data format: docs, JSON schema, extended baseline](#18-test-data-format-docs-json-schema-extended-baseline) | done (2026-10-21, commits 76fa790..96f1917; `localtest/testdata/` with the v3 schema, seed on v3, `reserved` mirrored into `isReserved`; pin sha-55ce57f, proven 2026-10-21 under task 7) | small | yes |
| 19 | [Documentation bookkeeping](#19-documentation-bookkeeping) | done (2026-10-21, branches `docs/root`, `docs/components-a`, `docs/components-b`, `docs/plans` merged; TODO corrected the same day) | small each | no |
| 20 | [Aspire host for the components](#20-aspire-host-for-the-components) | done (2026-10-21: plan in `planned-components/aspire/`; recommendation: no second runtime; a hybrid where an AppHost runs only the app beside `env up` should need no new code, 0.5 to 1 day to verify and document; a generated hosting package only if Aspire becomes a supported way in, 6 to 9 days; owner\'s decision) | small | no |
| 21 | [Aspire host package for apps](#21-aspire-host-package-for-apps) | in progress (2026-10-21: 21a done and merged, commits 125aa9db..2b5b2ca9: the embedded manifest `src/cli/internal/localtestv2/manifest/manifest.json` with its schema is the source of the component set, groups and the image pin, `components.go` is a loader, golden snapshot test; state file and route tables byte-identical to the table build for default, `--with correspondence` and `--with everything`, every smoke test passed on plain images; 21c done and merged, commit 0e8d02a5: `Altinn.Studio.Localtest.Provisioning` under `src/Runtime/common/dotnet/` (IAppSource with DirectoryAppSource and RunningAppSource, LocaltestProvisioner with Provision/Deprovision/SyncDialogporten, pluggable Studio token), studioctl-server a thin adapter by project reference, every provisioning capture identical to the inline code for two test apps, root and authorization smoke tests and the 44-test platform regression passed; 21b done and merged, commits 4b53d7d7..98e2baeb: cookie-policy fake in Authentication's and Correspondence's `fakes/` (shared source `components/shared/fakes/Cookies/`), Arbeidsflate patch 0003 and nginx without the forged `X-Forwarded-Proto`, both front doors strip `Secure` on app routes only, inventory and phase-2 results in `localtest/reviews/cookies.md`; verified by the coordinator on the dev images: 32 login `Set-Cookie` headers without `Secure`, authentication 34, correspondence 78, arbeidsflate 59, root and shared passing; pin sha-98e2bae, proven 2026-10-22 on plain images with `--with everything`: authentication 34, correspondence 78, arbeidsflate 59, root and receipt passing, 32 login `Set-Cookie` headers without `Secure`; 21d phase 1 dispatched 2026-10-22 (Opus, `.worktrees/task21d-aspire`, branch `task/aspire-frontdoor`, brief `/home/agent/code/.briefs/task21d.md`); 21b's inventory `localtest/reviews/cookies.md` approved by the owner 2026-10-21 (cookie-policy fake instead of twelve directive sites in Authentication; app routes keep the stripping until task 22 fixes app-lib's `keepAlive`); 21d to 21f later) | large | yes |
| 22 | [app-lib honours an explicit localtest environment](#22-app-lib-honours-an-explicit-localtest-environment) | unapproved (follow-up of 21; two items: the explicit environment, and `keepAlive`'s `Secure` from the request, decided 2026-10-21) | small | no |

Suggested order: 21a, 21b, 21c running (stack: 21a, then 21c, then 21b); then 21d and 21e; 8 when decided. 13 stays with the maintainer; 12 is a filler.

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

**Decisions (2026-10-20).** Seed two clients: `localtest-smoke` for tests and `arbeidsflate-localtest` with the
redirect, post-logout and front-channel-logout URLs from `planned-components/dialogporten/arbeidsflate.md` section
3.4, so task 11 only adds containers. The headless code-flow helper is a new localtest-app feature,
`localtest/api/authentication/` (not `/Home`, whose contract is the old localtest's and does not grow), e.g.
`GET localtest/api/authentication/token?client=&userId=&partyId=&authenticationLevel=` returning the token response.
A third directive site in Authentication is not acceptable without a report first.

**Verification.** `components/authentication` smoke test extended: discovery lists `end_session_endpoint`; the code flow
for the seeded client (authorize → picker → callback → `POST token` with `client_secret_basic` and PKCE) returns
`access_token`, `id_token`, `refresh_token`; the access token's `pid` equals the picked user's national identity
number; refresh rotates; `GET openid/logout?id_token_hint=…` answers 302 and a later refresh fails; the existing
`goTo` flow and the `/Home` contract are unchanged (`localtest/localtest/smoke-test.sh`, 65 checks). Run
`run-platform-regression.sh` afterwards.

**Inputs.** `planned-components/dialogporten/shared-prerequisites.md` section 3, `arbeidsflate.md` section 3,
`dialogporten-core.md` section 7, the vendored `OidcServerService.cs`, `TokenService.cs`, `ClaimsPrincipalBuilder.cs`,
`PersistanceDependencyInjection.cs`; `localtest/reviews/authentication.md`.

**Phase 1 (2026-10-20, branch `task/authentication-oidc-provider`, e494026..ade5849, reviewed).** Four in-memory
repositories, the pepper, both seeded clients, `endsession` in the fake ID-porten, and the helper under
`localtest/api/authentication/` (`code`, `token`, `refresh`, `callback`). New `components/authentication/smoke-test.sh`,
30 ok as plain processes with a stub Register and Profile. Findings, all production behaviour and mirrored, not fixed:
logout only ends the session found through the `AltinnStudioRuntime` cookie (Authentication cannot validate its own
`id_token_hint`); `post_logout_redirect_uri` is never honoured; a refresh family revoked by reuse detection cannot be
recreated for the same session (500 at `token`); the access token's `scope` is the session's, and includes
`digdir:dialogporten.noconsent`. Nits for phase 2: `LocaltestOidcClients.ConfigurationDefaults` emits hard-coded
`ClientType`, `TokenEndpointAuthMethod` and `SubjectType` strings (`Pairwise`, while the class defaults to `Public`)
instead of the object's values; the Dialogporten smoke-test header still says no token carries `pid` (after 9b merges).
Phase 2 (on the stack, after 9b): `pid` against the real Register, the Dialogporten end-user API check, the
localtest-app and root smoke tests, `run-platform-regression.sh`.

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

**Steps (decided 2026-10-20).** 5a: the component, its seed, the studioctl runtime integration (spec, image, proxy
prefix, health, provisioning call) and the smoke test, with the Access Management and PDP stand-ins untouched. 5b: the
stand-ins removed, Access Management and the PDP on the production clients, the six platform specs re-run.

**Open.** The `updated` feed's start for Access Management's sync (5b).

**Verification.** `components/resource-registry/smoke-test.sh` as planned in section 6.4; `run-platform-regression.sh`
still passes; Access Management's refresh still produces the same `app_{org}_{app}` resources.

## 6. Notifications

**Goal.** Apps that order notifications (`notifications/api/v1/orders…`) get production answers instead of a 404, and a
tester can see what was ordered.

**Plan (2026-10-20).** [`planned-components/notifications/notifications.md`](planned-components/notifications/notifications.md).
The Kafka premise was stale: upstream `altinn-notifications` (pin a4aa959) uses Azure Service Bus through Wolverine,
reached from the orders API host through one registration call and three publisher classes; the Email and SMS hosts
sit behind the queue and are not needed locally. Recommendation: Option A, vendor the orders API host
(`components/api` + `components/shared`) with capture fakes in place of the publishers (every would-be e-mail and SMS
recorded under `notifications/api/localtest/`, the production status machinery running on the shared Postgres), two
directive sites (one additive, one `#if !LOCALTEST`), about 1 400 lines of fakes, 6 to 7 days including the Profile
contact-point lookups the Register component must gain (four endpoints) and the studioctl wiring. Verified with a
scratch build and start of the host; the 104 migration folders have not been run on Postgres 18. The capture fake
must apply send results after the claim transaction commits (section 6.4).

**Follow-up found while verifying (2026-10-20), resolved by task 18 on 2026-10-21 (v3 seed: `+47` numbers, valid numbers for the four persons, official organisation addresses, `reserved`).** Production Notifications rejects bare eight-digit SMS
numbers (direct recipients: 400; Profile contact points: dropped, `Failed_RecipientNotIdentified`) and adds `+47` only
to numbers starting with 4 or 9, so the seed numbers of users 1337, 1002, 1003 and 12345 (`87654321`-style) never
receive an SMS. Organisation recipients always get 422 at registration because the seed has no official organisation
addresses. Both are seed-data changes in the Register component (`testdata/`), not fakes; the old localtest's apps read
the same seed, so check what else depends on those numbers before changing them. Open questions in section 6.14.

**Decisions (2026-10-20).** Option A as planned. Every captured e-mail and SMS is marked **Delivered** for now
(section 6.14 question 1). The capture is modelled as **inboxes**: one per e-mail address or phone number, each
listing its messages under `notifications/api/localtest/inboxes/…`; a later version adds reception on/off per inbox
with a default for new inboxes (an inbox with reception off makes its messages fail), so the data shape carries a
`receiving` flag now, always true. **No number normalisation** (question 2, decided 2026-10-20): the Profile fake
returns the seed's numbers and addresses exactly as stored, inboxes are keyed by the exact string the message is
addressed to (any number, any text), and whatever production Notifications does with a bare eight-digit number is
production behaviour to observe and document, not to mask in a fake; if it rejects them, the seed data changes. The four Profile contact-point lookups live in the Register component's fakes (question 3). No failure
simulation and no reserved-recipient flag yet (questions 4 and 5; the inbox toggle covers failures later). The
condition-check client keeps the shared Maskinporten fake (question 6, consistent with task 4). Instant and composed
orders captured like the rest, attachments by reference (question 7). Correspondence does not share the capture
GUI (question 8). The send result is applied after the claim transaction commits, through an in-process channel
(section 6.4).

## 7. Correspondence

**Goal.** Apps that send correspondence (`correspondence/api/v1/…`) get production answers, and the result is visible
locally (and later in the inbox, task 9 to 11).

**Design to settle.** Production `Altinn/altinn-correspondence` needs Postgres, Azure blobs (the Storage component's
file-backed blob pattern applies), Dialogporten, the Resource Registry, Notifications and Maskinporten. It therefore
comes after tasks 4, 5, 6 and 9. Research done 2026-10-20: `planned-components/correspondence/` (brief in its README).

**Decisions (2026-10-20).** Vendor at `6dcf561` (Option A) in its own optional group `correspondence`, which implies
`dialogporten` and shares its Redis (`defaultDatabase=1`); the default stack is unchanged, no fake-only variant.
Plan questions 3 to 9 settled as proposed: the KRR stand-in is an anonymous `register/api/localtest/krr/rest/v2/personer`
in the Register fakes answering "NEI" for every seeded person (a `reserved` seed flag later, shared with Notifications);
the two Profile `correspondence/` lookups in the Register fakes require the production scope
`altinn:profile/correspondence.notificationsettings.read`; the seeded tt02 policies get `ttd` beside `digdir` in the
service-owner rule, marked as a localtest deviation (the org token stays `ttd`); `apps-correspondence-integrasjon2`
is seeded as tt02 has it (`GenericAccessResource`, refused by production too); base images pinned by digest as a
`localtest:` deviation; attachment download from Arbeidsflate goes to task 11; failure visibility is the inspection
API's `jobs` view, no directive for Hangfire retries. Host port 8033, route `/correspondence`, Postgres init
`70-correspondence.sh`.

**7b, party ids (done 2026-10-21, commits 84723f6..0d3361b: one `#if !LOCALTEST` site in `XacmlRequestFactory.cs` accepting any positive 64-bit party id; the Correspondence smoke test has 64 real checks, 67 with the restart; `signering-brukerstyrt`'s call-to-sign and receipts go through production Correspondence; upstream fix drafted in `party-ids.md`, to be filed by the owner).** `planned-components/correspondence/party-ids.md`: production Correspondence
names the party by id in every PDP request and guesses the id's type by length (8 or 10 digits), so the seed's 6-digit
party ids give a 500 on app-lib's send and on the recipient's dialog-token path (two KNOWN checks in the smoke test;
the service-owner token bypasses the PDP). Recommended: one `#if !LOCALTEST` site in `XacmlRequestFactory` accepting
any unsigned 32-bit party id (the component's first directive), the two KNOWN checks turned into assertions, the
`signering-brukerstyrt` call-to-sign verified; in parallel an upstream issue (draft in the report) so Correspondence
sends `urn:altinn:partyid:{id}`. Not renumbering the seed. Needs the owner's approval of the directive.

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
Dockerfiles, the fourteen `LocalDevelopment` flags, three additive `Program.cs` hooks and two directive sites, Redis,
a database on the shared Postgres 18, routes, smoke test on the service-owner API with the org token. Tasks 4 and 5
are done, so dialogs for `app_ttd_testapp` can be created from the start. End-user smoke uses the `pid` token of
task 2 (done: the last checks of the authentication smoke test).

**Decisions (2026-10-20).**

- Opt-in: the three hosts and Redis form an optional component group in the studioctl runtime, started with a flag
  (`env up -r localtest-v2 --with dialogporten`); the provisioner's Dialogporten step is skipped when the group is off.
  Apps never call Dialogporten, so the default stack is unchanged (about 1 GB RAM saved).
- Maskinporten: task 4's shared fake (`localtest/components/shared/fakes/Maskinporten/`) copied into the component's
  fakes, production semantics (`UseAltinnTestOrg` false, Dialogporten acts as `digdir`; `ttd` shares Digdir's number
  in the registry so the owner check passes). Not the report's `/Home/GetTestOrgToken` variant.
- Database: `dialogporten` with its own role on the shared Postgres 18 (no `uuidv7()` shim). **No reset on start**:
  dialogs persist across restarts like Storage's instances, and the only reset is `studioctl env reset -r localtest-v2`,
  which deletes the shared Postgres volume and therefore cleans Storage and Dialogporten together (decided
  2026-10-20; instances and their dialogs must never get out of step).
- The `/dialogporten/graphql` → `/graphql` rewrite is a path-rewrite option on the route type in studioctl-server's
  proxy (vendored source), the only route that needs it; it mirrors APIM in production. **Note for task 11
  (Arbeidsflate):** the browser-side GraphQL subscription stream and the BFF's `DIALOGPORTEN_URL` go through this
  route; if WebSockets or the URL shape turn out not to fit, the rewrite (or a dedicated host) may have to change.
- Redis stays, inside the opt-in group (one container, about 10 MB): upstream connects to it eagerly at start in all
  three hosts for HotChocolate GraphQL subscriptions (the Service host publishes, the GraphQL host subscribes, so an
  in-memory transport would never deliver a live update), and uses it as FusionCache's distributed tier and
  backplane and as a health check. Removing it would cost two directive sites in the shared Infrastructure project
  (subscriptions to in-memory, cache registration off) and lose live inbox updates. The component README must say this.
- Fresh pin of upstream `main`; three images from one vendored tree (Dockerfiles mirroring upstream's), three matrix
  entries in the image workflow.
- Steps. **9a**: vendor, build chain, Dockerfiles, Postgres and Redis, hooks and fakes, the optional group in
  studioctl, boot to healthy, the service-owner smoke (create a dialog for `app_ttd_testapp` with the org token,
  read, search, delete; wrong scope and foreign resource refused). **9b**: the provisioner's `sync` step, the cache
  and reset endpoints, the Service host's events into the Events component, GraphQL through the rewrite, CORS for
  `http://af.local.altinn.cloud:8000`, and what Events does with Dialogporten's sources (plan question 9).
- As the report: the fourteen `LocalDevelopment` flags set explicitly, warmup off, committed Ed25519 test key pairs,
  Serilog at Information, the CDN read from the internet, no Janitor and no migration image.

## 10. Dialogporten adapter and the Storage outbox

Vendor the adapter per `planned-components/dialogporten/dialogporten-adapter.md` (four small directive sites plus
additive hooks, prototype in `adapter-prototype/`), switch Storage's outbox on in its fakes, the in-process Wolverine
path POSTing each command to a localtest-only enqueue endpoint on the adapter, which publishes it to the adapter's own
local queue so the production sync handler and its retry and error policies run unchanged. Needs 4, 5 and 9a.

**Decisions (2026-10-20).**

- **Storage always sends.** The Storage fakes stop forcing `WolverineSettings:EnableSending=false`; the outbox and the
  handler run in the default stack too. When the adapter is not running (the `dialogporten` group is off) the handler
  treats an unreachable adapter (connection refused, 502/503/504, or the proxy's no-route answer) as "not enabled":
  one log line, the command dropped, no retry storm, nothing else affected. The adapter joins the `dialogporten`
  group; nothing in Storage's configuration depends on the group.
- **The enqueue endpoint is anonymous.** Only production endpoints carry authentication; every `api/localtest/`
  endpoint in the stack is open, this one included. Storage's handler therefore needs no token for the enqueue (the
  earlier question about `/Home/GetTestOrgToken` in Storage is moot). The adapter's own calls to Storage, Register
  and Dialogporten keep their production tokens through the shared Maskinporten fake.
- **Archived instances' receipt link.** Served by the `receipt` group since task 16; without the group the front door answers 503 (earlier decision: accept the 404).
- **Outbox delays.** Local values `LowPriorityDelaySecs=2`, `HighPriorityDelaySecs=0` (production 60 / 5), by
  configuration: testers should see that a sync is not instant, but not wait a minute. **Every place where localtest
  is faster than production is documented** in one list, `localtest/README.md` section "Where localtest is faster
  than production" (this delay, the Resource Registry's removed 10-minute application cache, Dialogporten's
  cache-clear endpoint against its 15- and 20-minute caches, the Events component's immediate delivery, and whatever
  else exists), each with the production value; new components add their rows.
- **Verification.** The adapter smoke test covers the service-owner-visible path: instance created → dialog within
  10 s with the right resource, party and go-to URL, `dataValues["dialog.id"]` on the instance; process start and a
  data upload → attachment and activity; process end → status change (and a transmission); soft delete → `deleted:
  true` for the service owner; manual `syncDialog/simple` idempotent; the default stack with the group off: an instance
  created produces one log line and nothing else, and `run-platform-regression.sh` stays green. **Dialog-token steps
  are in 10 (decided 2026-10-20, task 2 lands first):** the smoke test takes a code-flow access token for user 1337
  from `GET localtest/api/authentication/token`, reads the dialog through `GET dialogporten/api/v1/enduser/dialogs/{id}`
  for its `dialogToken`, and runs the adapter's two token-validated endpoints with it: the transmission receipt
  (`GET storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}`, which the adapter renders itself as
  Markdown from Dialogporten, Storage, Register and the CDN: expect 200; corrected 2026-10-20 by the Receipt research,
  the 404 accepted earlier concerns only the go-to link of archived instances, `/receipt/{partyId}/{guid}`, until
  task 16) and the delete (`DELETE storage/dialogporten/api/v1/instance/{owner}/{guid}` → Storage soft delete →
  outbox → dialog deleted). No localtest source mints dialog tokens.
- **Organisation list.** The adapter reads `altinn-orgs.json` from the real CDN, as Dialogporten does (task 3 is
  won't do); no committed copy.
- **Re-sync button** per instance (`syncDialog/simple`) is an input to task 8, not part of 10.

**Known pain point, not ours to fix (decided 2026-10-20).** Dialogporten's service-owner search names the caller's
organisation by the first short name the Resource Registry lists for 991825827 (`digdir`), while dialogs for `ttd`
apps carry `ttd` (found in 9a): a `ttd` service owner's search does not find its own dialogs. This is production
behaviour (`ttd` has no organisation number and the registry substitutes Digdir's); the stack does not fix production
issues, so it stays, documented in the Dialogporten and adapter READMEs as a pain point testers will meet.

## 11. Arbeidsflate

Vendor the pnpm monorepo per `arbeidsflate.md`: pristine tree plus two patches, dedicated host `af.local.altinn.cloud`,
nginx in front of the BFF with `X-Forwarded-Proto: https`, Postgres and Redis, login through the OIDC client from
task 2. Needs 2, 9 and 10 (all done; 14 gives the profile page production Profile).

**Decisions (2026-10-21, discussed).** Host name `af.local.altinn.cloud:8000` (studioctl maps every ingress host of the
topology to the host gateway inside the containers, so the authentication container's front-channel logout reaches
it without a hosts entry). The two patches stay as `localtest:` patches; nothing is proposed upstream now. Images are
built locally from the patched tree so they work on plain http; no pulled images. Its own optional group
`arbeidsflate` implying `dialogporten` only (dialogs come from other sources than Correspondence). The profile page is
in scope, served by production Profile. Internet dependencies stay (task 3). Pin: upstream `main` at dispatch.
Postgres init `90-arbeidsflate.sh`, the shared Redis on its own database. **11b, Correspondence attachments from the
browser.** Researched 2026-10-21 in the Arbeidsflate README's "Correspondence attachments" section: the inbox opens
attachments as plain links with cookies; Correspondence then signs the user in through production Authentication's OIDC
provider and validates the token with `IdportenTokenValidator`, whose document retriever requires https
(`IdportenTokenValidator.cs:26`), so plain http loops. Fix: one `#if !LOCALTEST` site (`RequireHttps = false`), a
seeded `correspondence-localtest` OIDC client with scopes `openid profile`, and `SameSite=Lax` /
`SecurePolicy=SameAsRequest` correlation and nonce cookies in Correspondence's fakes. **Done 2026-10-21** (commits 789edcd3..8e510e91: the site at `IdportenTokenValidator.cs:26-31`, the seeded client
`correspondence-localtest`, Lax cookies in the fakes; the Correspondence smoke test has 76 checks, 79 with the restart;
the download clicked in Chromium from the inbox; pin bump pending the images workflow).

## 12. Review leftovers

Small items from `localtest/reviews/` and the components' "Known gaps", each its own brief:

- Localtest app: `HEAD` on most endpoints; a WebSocket test in `localtest/localtest/smoke-test.sh`.
- Events: `DELETE events/api/localtest/events` does not drain Wolverine's in-memory queues; Slack deliveries recorded
  without their event.
- Access Management: a stub for the SBL bridge's Altinn 2 roles calls (today a proxy 404), if any flow needs it.
- Authorization smoke test: its closing "PDP log errors" line reads the compose-era container name
  `localtest_authorization_1`, so under studioctl it always reports 0; read `localtest-v2-authorization` instead.
- Authorization and Events: Debug logging is very noisy under `ASPNETCORE_ENVIRONMENT=Development`; a localtest
  logging override.
- Workflow engine: state reset (left as decided; revisit only on request).
- **Decision (2026-10-21):** work through the items below as one filler task. A test app's policy that production
  code rejects (the `stateless-app` case) is not fixed here: it is recorded in `localtest/reviews/test-apps.md` with the
  rule and the parser's reason, for an upstream fix in the monorepo later.
- Found by the 2026-10-21 documentation audit (task 19):
  - Authorization smoke test: its "store:" line has the same compose-era container name as the "PDP log errors" line.
  - Storage's standalone `smoke-test.sh` cannot run since 2026-10-20: it calls the `/Home` token helpers on
    Authentication, which moved to the localtest app, and sets a setting that no longer exists. Port it or delete it.
  - Events: `registeredTime` is stored as Oslo time because upstream's column default takes `now() at time zone 'utc'`
    on a Postgres running with `TZ=Europe/Oslo`; the read API's 30-second delay therefore never holds anything back
    locally. Fix the container's timezone or document it as a localtest difference (read from the code, not tested).
  - Events: `AppsDomain` treats every Dialogporten source as one app (9b finding); a `LOCALTEST` site or a separate
    host name for apps.
  - Resource Registry: `policy/rules` (and probably `policy/rights`) answers 500 for `stateless-app`'s policy, whose
    rule 3 mixes action and resource matches in one `AnyOf`; production's parser throws on it. Known gap in the README.
  - Workflow engine smoke test: its default `--image` is `localtest-workflow-engine:dev`, which nothing builds; its
    README said Postgres 16.
  - Dockerfile comments in register, authorization, access-management and workflow-engine still mention compose
    healthchecks and mounts; `groups.go`'s dialogporten comment omits the adapter and overstates the memory saving.
  - `localtest/studioctl/UPSTREAM.md` names only the older fork branch; the current one is
    `feat/studioctl-localtest-v2-stack` (pinned at sha-55ce57f, lacking the Correspondence and Profile commits).
  - Correspondence smoke test (seen 2026-10-21 on the pinned images): the first run after `env up` fails one check on
    Hangfire job timing ("reaches Published" once, "the opened and download activity jobs have run" once); the
    second run passes 64 of 64. Raise the waits so the first run passes.

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

Done 2026-10-21: production Profile runs in the default stack (`localtest/components/profile/`, port 8036, route
`/profile`); the Register's Profile controllers are gone. Original question (2026-10-20): the Register component
answered the Profile endpoints Authentication and Authorization call, apps and Dialogporten use more of Profile,
and the research was to decide between vendoring `Altinn/altinn-profile` and extending the Register fakes.

**Conditional approval (2026-10-20).** If the research finds vendoring `Altinn/altinn-profile` (or the alternative it
recommends) straightforward under the vendoring model, dispatch the implementation as a follow-on task after the
running tasks (10, 16, 7) are verified, with the plan's "Proposed brief" and the inherited rules, without a further
round. Otherwise report back.

## 15. Localtest URLs in the Maskinporten client package

**Goal.** `Altinn.ApiClients.Maskinporten` accepts a configured token endpoint and exchange endpoint (or a custom
environment) so that a consumer can point it at the localtest app and production Authentication by settings alone,
and the compiled-in `IMaskinportenService` fake from task 4 can be deleted. An upstream change in
`Altinn/altinn-apiclient-maskinporten` (the `switch` tables in `MaskinportenService.GetTokenEndpoint` and
`GetTokenExchangeEndpoint` throw for unknown environments; `MaskinportenSettings` has no URL fields), then a version
bump in each consumer. Also covers the BFF, which is already configurable, and Correspondence's own token service.

## 16. Receipt

**Goal.** Vendor `Altinn/altinn-receipt`, the platform receipt app, so that archived instances' receipt links
(`/receipt/{instanceOwnerPartyId}/{instanceGuid}`, which Dialogporten's go-to action and the adapter emit for archived
instances, and which the app frontend links to) work locally instead of answering 404 (decided 2026-10-20 in task
10). Research done 2026-10-20: `planned-components/receipt/` (brief in its README).

**Decisions (2026-10-20).** Its own optional group `receipt`, implying nothing (not the default stack). Accept the
`selfLinks` rewrite fake. "Tilbake til innboks" stays as upstream has it, raised upstream with task 11. Pin `babc0ed`,
re-pin on backend changes only. Image builds may reach the npm registry. Host port 8034, route `/receipt`.

## 17. Admin of running services and the inspection GUI

**Goal.** Find out whether Altinn Studio's administration of running services (the Designer `admin` frontend,
`src/Designer/frontend/admin`, and its backend) overlaps with the planned inspection GUI (task 8), and whether it
could be pointed at the localtest stack instead of building a separate page. Research only, under
`planned-components/admin-running-services/`: what the admin frontend shows and calls today, what it would need
from the stack, what task 8 would still add, and a recommendation.

**Conditional approval (2026-10-20).** If the research finds that vendoring the admin frontend and its backend into
the stack as an optional group (`admin`, implying whatever it needs) is straightforward, dispatch that vendoring and
inclusion as a follow-on task once 10, 16 and 7 are finished and verified, without a further round of discussion;
the brief is the plan's "Proposed brief" plus the inherited rules. If it is not straightforward (Designer backend
coupling, Gitea or Kubernetes dependencies, auth model), report back and 8 is reconsidered instead.

## 18. Test data format: docs, JSON schema, extended baseline

**Goal (2026-10-20).** One folder documenting the `testData.json` format the stack reads (the Register component's
seed `components/register/testdata/testData.json` and every app's `App/wwwroot/testData.json`): a Markdown
reference of every field with its consumer, and a JSON Schema the files declare in `$schema`. The schema includes
the extensions decided so far: persons' `reserved` flag (KRR reservation; Notifications plan question 5, task 7's
stand-in), mobile numbers in full international form (`+47…`, because production adds `+47` only to numbers starting
with 4 or 9 and rejects bare eight-digit numbers), organisations' official notification addresses (e-mail and SMS, for
Notifications' organisation recipients, 422 today), and an extension point for the Profile data task 14 identifies.
The baseline seed is modified to use them (ids, names and `userId.partyId` pairs unchanged: Cypress depends on
them), `generate-testdata.py` follows, and the Register fakes load the new fields. The KRR stand-in reads
`reserved` once task 7 has merged.

**Phase 1 (2026-10-20, branch `task/testdata-format`).** Folder `localtest/testdata/` with `README.md` and
`test-users.schema.v3.json` (2020-12, `additionalProperties: false`, `$id` in the altinncdn.no line but unpublished;
v2 was never published, the v1 URL is used by the old localtest's exports and the docs, nothing validates at run time).
Seed on v3: `+47` on every number, `reserved: true` on Rik Forelder (1003) only, official addresses for DDG Fitness
and Digitaliseringsdirektoratet, generator back in step with the file. Kept deviation: the four persons whose numbers
were `87654321`-style get valid Norwegian numbers (`+4799001337` etc.), because libphonenumber in production
Notifications rejects `+4787654321` and the seed would still never receive an SMS; nothing in Cypress or the apps
uses the old digits. Done in phase 2 (96f1917): `reserved` is mirrored into `isReserved` of `users/contactpoint/lookup`, and Profile's KRR sync carries it on since task 14. 7 must rebase onto this: its KRR stand-in answers `JA` for reserved persons, its
`correspondence/organizations/notificationaddresses/lookup` calls the official addresses, its smoke expectations change.

## 19. Documentation bookkeeping

**Goal (2026-10-21).** Every README, review and plan states only what is true of the current behaviour: ports, routes,
groups, which component serves what, smoke-test counts, known gaps, pins, "not yet" and "pending" remarks. Historical
sections stay, dated, as history. Four agents by folder: the root and shared docs (`README.md`, `localtest/README.md`,
`localtest/localtest/`, `localtest/testdata/`, `localtest/studioctl/`, `localtest/postgres/`, `localtest/certs/`,
`localtest/smoke/`), the components of the default stack from before Dialogporten (register, authentication,
authorization, access-management, storage, events, workflow-engine, shared, `localtest/reviews/`), the newer
components (notifications, resource-registry, dialogporten, dialogporten-adapter, receipt, profile; correspondence is
left to task 7b), and `planned-components/` (status headers saying what was implemented and which statements are
superseded, never rewriting the research). Agents report what they could not verify and anything stale in `TODO.md`,
which only the coordinator edits.

## 20. Aspire host for the components

**Goal (2026-10-21).** Find out whether the stack's components can run as container resources of a .NET Aspire
AppHost next to an app project, so an app developer gets the platform from `dotnet run` on the AppHost instead of
`studioctl env up`: what Aspire would need (the images from the registry, the environment studioctl's `components.go`
sets, the front door that studioctl-server provides, the provisioning steps, test data, groups), what it cannot give
(the Unix-socket server, the app runner's provisioning), how app-lib or the app template would reference it, and
whether this competes with or complements studioctl. Research only, under `planned-components/aspire/`, with a
recommendation and a proposed brief.

## 21. Aspire host package for apps

**Goal (2026-10-21, owner's direction).** A separate Aspire hosting package that an app imports in a single-file AppHost
(`var localtest = builder.AddAltinnLocaltest(); builder.AddAltinnApp<App>(localtest);`) to get the platform
components as container resources beside the app, with the same images, settings and provisioning as studioctl. Both
runtimes read one **common manifest** (image tags, ports, environment, mounts, dependencies, groups, routes, init
scripts) and share the **setup functions** (the provisioning steps). The front door no longer strips `Secure` from
cookies: the components that set `Secure` cookies get `#if !LOCALTEST` blocks instead, so a plain YARP (or the localtest
app's proxy) can be the front door. The AppHost package reads the app's `testData.json`, policy, metadata and texts and
coordinates the registration in Storage, the PDP, the Resource Registry, the Register, Access Management and
Dialogporten, as the provisioner does today. Research: `planned-components/aspire/` (its option 2, the generated
layer).

**Sub-items (each its own brief once approved).**

- **21a, the manifest.** Define `localtest/manifest.json` with a JSON schema as the single source of truth: components
  (name, image repository and tag, container port, host port, environment with templated references such as
  `{platform}` and `{postgres}`, mounts, dependencies and health, group), groups with implications, routes (host,
  prefix, rewrite), Postgres init scripts, the certificate. studioctl embeds it (`go:embed`) and `components.go`
  becomes a loader with a test that the two agree during the transition; the pin `tagLocaltestV2` moves into it.
  Decide templating and secrets handling.
- **21b, cookies without the proxy.** Inventory every `Secure` cookie the components set (Authentication's
  `AltinnStudioRuntime` and session cookies, Correspondence's OIDC cookies, the Arbeidsflate BFF's session cookie
  (Node: a patch), the localtest app's own cookies, app-lib's XSRF cookie if any), add `#if !LOCALTEST` blocks or fake
  configuration so they are not `Secure` on plain http, then remove the stripping from studioctl-server and the
  localtest app's proxy. Report before each new directive site, as the model requires.
- **21c, the shared provisioning library.** Extract `AppProvisioner`'s steps (register, Storage metadata and texts, PDP
  policy, Resource Registry subject reload, Access Management refresh, Dialogporten sync) into a .NET library both
  studioctl-server and the Aspire package reference, reading the app's `testData.json`, policy, metadata and texts from
  the app folder or the running app, as today.
- **21d, the front door for Aspire.** `Aspire.Hosting.Yarp` with routes generated from the manifest (host + prefix,
  path rewrite) and the app routes the provisioner adds; the 503 answer for groups that are off is deferred; `local.altinn.cloud:8000` reachable from
  the containers (Aspire network aliases, `--add-host` under Podman).
- **21e, the package.** `Altinn.App.Aspire` (decided 2026-10-22, see the 21d decisions): `AddAltinnLocaltest(options)` adds the containers
  from the embedded manifest with groups, Postgres and Redis, the certificate, waits and health; `AddAltinnApp<T>()`
  sets the app's environment (platform URLs, test certificate), runs the provisioning after the app is healthy, and
  exposes a dashboard command to rerun it; `env reset` as a command; a sample single-file AppHost for a test app; the
  app-lib `studioctl app env --json` import (app-lib v9 in Development, overrides AppHost values unless
  `STUDIOCTL_APP_RUN` is set) handled; Podman verified. Published to GitHub Packages from this repo for the prototype.
- **21f, docs and parity.** A CI check that the manifest, studioctl and the package agree; docs in `localtest/README.md`
  and, when adopted, altinn-studio-docs; the smoke tests runnable against an Aspire-hosted stack.

**Decisions (2026-10-21, discussed one by one).** The checked-in `localtest/manifest.json` with a JSON schema is the
source of truth; studioctl embeds it and `components.go` becomes a loader, with a transition test that the loader
reproduces today's table; the pin moves into it. The Aspire front door is `Aspire.Hosting.Yarp` with routes generated
from the manifest (host + prefix, rewrites), plus the app routes the provisioner adds; the 503 for known components whose group is off is **deferred** for the
Aspire front door (owner, 2026-10-21: low priority), so a request to one falls through to whatever YARP does by default,
documented. The shared provisioning library reads the app's files from disk first (the Aspire
package knows the project path) and falls back to the running app's API (studioctl-server's case). app-lib's import of
studioctl's environment: the package sets `STUDIOCTL_APP_RUN` now, and an app-lib change (honour an explicit localtest
environment) is filed as a follow-up (task 22). 21b runs as one agent: inventory, the sites with a report per
component in its README, then the stripping removed from both proxies; the owner reviews the inventory in the report
before merge. Prototype scope: default stack plus `dialogporten`, one sample test app, the smoke tests against the
Aspire-hosted stack, Podman verified; package `Altinn.App.Aspire` published to GitHub Packages from this repo.
Order: 21a and 21c first, 21b in parallel (code-only), then 21d and 21e, 21f last. Dispatched 2026-10-21 (21a, 21b, 21c).

**21d decisions (2026-10-22, discussed one by one).** Front door: stock `Aspire.Hosting.Yarp`, accepting that it cannot
strip `Secure` from app-lib's `keepAlive` cookie (no cookie transform in the stock image): apps on the Aspire host lose
the ten-minute token refresh over http until task 22 ships; documented in the sample AppHost. Reachability: `--add-host <host>:host-gateway`
on every component as studioctl does (the manifest's `ingressHosts` and `extraHostGatewayNames`), the front door's host
port published on 8000; network aliases on the YARP container are the fallback if `WithContainerRuntimeArgs` fails
under Podman (reported, not silently switched). The app route: YARP routes to the app project resource and Aspire's
container tunnel carries it to the host, the app stays on loopback; `host.docker.internal` with the app on `0.0.0.0` is
the documented fallback if the tunnel fails under Podman (reported). The package is `Altinn.App.Aspire` at
`localtest/aspire/Altinn.App.Aspire/` (owner, 2026-10-22), outside the vendored studioctl tree, so it is this
repository's own and not part of the studioctl upstream patch; it embeds the manifest from the Go tree by relative
path (`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`, single copy) and references the
provisioning library's sources by relative `<Compile Include>` (as studioctl-server includes Runtime.Common), so the
NuGet is one assembly with no unpublished dependency (owner, 2026-10-22); the C# manifest reader lives in the package
and its test compares against the Go golden file by relative path. Scope of 21d: the first slice of the package (skeleton, the
C# manifest reader tested against the Go golden file, `AddAltinnLocaltest()` with the default stack plus the
`dialogporten` group, the YARP front door), proven by `aspire run` under Podman and the root, authentication and
dialogporten smoke tests through port 8000, no app yet; `aspire run` is proven with Postgres alone first, since Aspire
has never run on this machine. 21e: `AddAltinnApp<T>()`, provisioning hook, commands, sample AppHost, publishing.

## 22. app-lib honours an explicit localtest environment

**Goal (2026-10-21, follow-up of 21).** app-lib v9 in Development runs `studioctl app env --json` and lets its values
override the process environment unless `STUDIOCTL_APP_RUN` is set. Propose an app-lib change (and template if needed)
so that an explicitly configured localtest environment, as an Aspire AppHost sets it, is honoured without the
variable. Upstream work in `Altinn/app-lib-dotnet`; unapproved.

**Second item (decided 2026-10-21 under 21b).** `AuthenticationController.KeepAlive` (monorepo `src/App/backend`,
`app-lib-dotnet` v8) re-issues `AltinnStudioRuntime` with `Secure = true` hard-coded in every environment, so an app
behind a plain-http front door loses its ten-minute token refresh: the browser refuses the cookie and the session fails
when the old token expires. Change it to take `Secure` from the request (`Request.IsHttps`, or
`CookieSecurePolicy.SameAsRequest` through a `CookieBuilder`); production behind TLS is unchanged. This is the one
cookie that still makes both front doors strip `Secure`, scoped to app routes since 21b (`localtest/reviews/cookies.md`
rows 19 and A2). When it ships, the app-route stripping goes from studioctl-server and the localtest app's proxy, and
the Aspire front door (21d) can be a plain YARP with no cookie transform; until then 21d carries the same app-route
stripping as a bridge for apps on older app-lib versions. Decision: the components fix their own cookies (21b as
built) rather than keeping the proxy stripping everywhere, because the end state has no front door rewriting cookies.

