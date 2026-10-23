# Plan: Dialogporten, the Dialogporten adapter and Arbeidsflate in the new localtest

**Superseded 2026-10-23 by task 26:** the host routing read as "studioctl-server host routing" below is the localtest container's (`af.local.altinn.cloud` among its dedicated hosts), and the groups close over `dependsOn` (task 27); see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

## Status (2026-10-21)

All five strands are implemented (Arbeidsflate as task 11 on 2026-10-21; the line was corrected on 2026-10-23). Answers to the collected questions below:
[`../README.md`](../README.md), "Answers to the plans' open questions".

| Strand | Status | Task, commits (`TODO.md`) | Where, smoke test on 2026-10-21 |
| --- | --- | --- | --- |
| Shared prerequisites | Partly implemented: Maskinporten token endpoint and shared fake (task 4, `798f18e`), Authentication as OIDC provider with `pid`, logout and the base64 pepper (task 2, `4b9b5f7..ddd0ecf`), Redis and Postgres 18 (task 9, `84514a3`). The committed `altinn-orgs.json` was dropped (task 3 won't do); the app-frontend inbox link is open (task 11) | 2, 4, 9 | `localtest/localtest/`, `components/shared/`, `components/authentication/` (30, 32 with the group) |
| Resource Registry | Implemented | 5a `e0cba40..b6f4c47`, 5b `9b7278d..aaa099e` | `components/resource-registry/` (43) |
| Dialogporten core | Implemented, optional group `dialogporten` | 9a `4ab03f0..76b8787`, 9b `4749bd7..d7827bf` | `components/dialogporten/` (60) |
| Adapter and Storage outbox | Implemented, adapter in the `dialogporten` group, Storage's outbox on in the default stack | 10, `c940fd6..3c9b03f` | `components/dialogporten-adapter/` (39) |
| Arbeidsflate | Implemented: task 11 and 11b (2026-10-21), optional group `arbeidsflate` | 11 | [`../../localtest/components/arbeidsflate/`](../../localtest/components/arbeidsflate/README.md) |

Decided otherwise than proposed here:

- Maskinporten: the shared fake keeps production semantics (no `test=true`); Dialogporten and the adapter act as
  `digdir`, and a `ttd` service owner's search does not find its own dialogs (a production pain point, kept; task 10).
- No committed `altinn-orgs.json` and no CDN route: every consumer reads altinncdn.no (task 3 won't do).
- Directive sites as built: Dialogporten three additive hooks and one `#if !LOCALTEST` (http URLs, added by task 10),
  the adapter four `#if !LOCALTEST` and three additive hooks, the Resource Registry two additive hooks.
- Opt-in "profile": a studioctl component group, `--with dialogporten`; Correspondence's group implies it.

Superseded statements:

- Headline findings, "no token Authentication issues carries `pid`" and the `openid/logout` 500: task 2 fixed both;
  the cookie token lacks `pid` by production design, the code-flow access token carries it.
- "Where the strands disagree", Maskinporten row, "exchanging with `test=true`": production semantics (task 4).
- "Order", step 1, "the CDN file and route": not done (task 3 won't do).
- "Not verified": everything except Arbeidsflate has since been built as images and run on the stack.

Research date 2026-10-20. Five strands, each a report in this folder, written against the stack as it was that
morning (commit `9f13cbb`). The question: how do we vendor the components an end-user inbox needs, so that a tester
who runs an app locally can see what it does to the user's inbox? Earlier, read-only study:
[`../../localtest-components/dialogporten-arbeidsflate.md`](../../localtest-components/dialogporten-arbeidsflate.md)
(2026-10-18); each report opens with what that study got wrong or stale.

| Report | What it plans | Effort (after its prerequisites) |
| --- | --- | --- |
| [`shared-prerequisites.md`](shared-prerequisites.md) | Maskinporten token endpoint in the localtest app, one shared `IMaskinportenService` fake for the .NET consumers, Authentication as an OIDC provider for a registered client (four in-memory repositories, a seeded client, a pepper bug), Redis, a committed `altinn-orgs.json`, the app-frontend link to the inbox | about 4.5 days |
| [`resource-registry.md`](resource-registry.md) | The missing platform service every inbox consumer needs. Recommends vendoring production `altinn-resource-registry` (Option A) over a fake HTTP service (B) or Dialogporten's local-development path (C) | 4 to 6 days |
| [`dialogporten-core.md`](dialogporten-core.md) | Vendoring the WebApi, GraphQL and Service hosts: build chain (verified with a scratch build), Dockerfile, the thirteen `LocalDevelopment` flags, every outbound dependency checked against the running stack, directive sites, tokens, proxy routes | 5 to 7 days |
| [`dialogporten-adapter.md`](dialogporten-adapter.md) | Vendoring the adapter and switching Storage's outbox on: the in-process Wolverine path verified end to end against the running Authentication, directive sites applied in a scratch build ([`adapter-prototype/`](adapter-prototype/)), links in both directions | 3 to 5 days |
| [`arbeidsflate.md`](arbeidsflate.md) | The pnpm monorepo: pristine vendored tree plus two small patches (verified to apply at the pin and on `origin/main`), the login flow traced through our vendored Authentication, a dedicated host name, nginx in front of the BFF, Postgres and Redis | 3 to 4 days |

Sum: about four to five weeks of implementation, in line with the 2026-10-18 estimate, but with far fewer unknowns:
the build chains of all three .NET repositories and the patches for the TypeScript one are verified, and every
platform dependency has been checked against the stack rather than inferred.

## Headline findings

- **Feasible in the model, with few directives.** Dialogporten needs three additive `Program.cs` hooks and two
  `#if !LOCALTEST` sites, the adapter five sites, Storage none (its fakes already own the outbox switch), the Resource
  Registry two, and Arbeidsflate two patches of 4 + 1 hunks. Everything else is configuration or files of our own.
- **The Resource Registry is the gate.** Dialogporten refuses to create a dialog for a `serviceResource` it cannot
  resolve, and search authorization is built from the registry's `updated` feed. Production computes app resources on
  request from Storage's application list and the shared policy container; the `policy-store` volume the PDP and
  Access Management already share is that container, so vendoring the real service costs no extra push. It also
  retires the stand-ins Access Management and the PDP carry today.
- **Storage to adapter needs no Azure and no Storage directive.** The vendored Storage runs in `Development`, where
  Wolverine routes outbox commands to a sequential in-process queue; a handler in Storage's fakes POSTs each command
  to a localtest enqueue endpoint on the adapter, which puts it on the adapter's own local queue so the production
  handler and its retry policies run. Verified in a scratch run through the production handler.
- **Arbeidsflate logs in through production Authentication as an OIDC provider.** All provider code is vendored; what
  is missing is four in-memory repositories (clients, codes, refresh tokens, sessions), a seeded client, and a fix to
  the localtest `OidcRefreshTokenPepper` (not base64, so client-secret auth and refresh tokens fail today).
- **Two defects found on the running stack that block the inbox regardless of plan:** Authentication's
  `openid/logout` answers 500 (`OidcProviders:idporten:LogoutEndpoint` is null: the fake ID-porten needs an
  end-session endpoint), and no token Authentication issues carries `pid`, while Dialogporten classifies end users by
  `pid`. The fake ID-porten's id_token has `pid`; the loss is inside Authentication's identity step
  ([`arbeidsflate.md`](arbeidsflate.md) section 3, [`dialogporten-core.md`](dialogporten-core.md) section 7). Until
  it is fixed the inbox is empty for every user.
  *2026-10-21: superseded by task 2; code-flow access tokens carry `pid` and logout works (see the status above).*
- **Plain http needs one proxy header.** `@fastify/session` never saves a session when the cookie is `secure` and
  the request protocol is not https, and the BFF hard-codes `secure: true`; an `X-Forwarded-Proto: https` header from
  the nginx in front of it is the fix, not a patch.
- **Stale claims corrected.** The picker token does carry `digdir:dialogporten.noconsent`; `DisableCache` does not
  shorten the 15-minute authorization caches (they are named caches); `RequireHttpsMetadata=false` must be set with
  `Configure`, not `PostConfigure` (verified by failure); no Janitor image is needed (its two sync commands are
  MediatR commands a localtest endpoint can run on the runner's push); the Maskinporten token endpoint is needed by
  the BFF, not by the .NET consumers, which all exchange to Altinn tokens.

## Where the strands disagree, and what the stack decided since

The strands ran in parallel and resolved the shared contracts between them (token endpoint URL and response, the
fake's registration and build include, the CDN route, the OIDC client id and redirect, the Redis databases). Three
points were left to the product owner and one has been decided by a commit that landed while the strands ran
(`84514a3`, "image workflow for ghcr, shared postgres image, remove the compose file").

| Point | Positions | Status |
| --- | --- | --- |
| Postgres 16 with a `uuidv7()` shim, or the shared server on 18 | core: shim now, 18 later; prerequisites: one shared `postgres:18-alpine`, new volume path | **Decided by `84514a3`:** the shared image is now [`localtest/postgres/`](../../localtest/postgres/Dockerfile), Postgres 18.3 with every component's init script baked in. Drop the shim; Dialogporten's and Arbeidsflate's init scripts join that Dockerfile |
| Where the .NET consumers get their Maskinporten token | core: a fake that calls `/Home/GetTestOrgToken` directly; prerequisites and adapter: a shared fake behind the new token endpoint, exchanging with `test=true` | Take the shared fake: the BFF needs the token endpoint anyway, and one fake source file compiled into three hosts beats three variants. Core's direct call is the same thing one hop shorter |
| Resource Registry before or after Dialogporten | core: `UseLocalDevelopmentResourceRegister=true` as a stop-gap to boot the component; registry strand: vendor first, it gates every dialog | Boot Dialogporten with the stop-gap for its own smoke test, but do not plan any dialog-creating test before the registry exists |
| Compose service, opt-in `inbox` profile, `extra_hosts` for `af.local.altinn.cloud` | all strands plan compose services, an opt-in profile and `extra_hosts` | **Changed by `84514a3`:** there is no compose file; the stack runs through studioctl's `localtest-v2` runtime from images built by [`.github/workflows/images.yml`](../../.github/workflows/images.yml). Read every "compose service" as a runtime component plus a matrix entry in that workflow, the profile as a runtime option, and `extra_hosts` as studioctl-server host routing. The configuration tables transfer unchanged |

## Order

1. Prerequisites with no dependents: the CDN file and route, Redis, the Postgres 18 init scripts.
2. The Maskinporten token endpoint and the shared fake (the adapter and Dialogporten strands start here).
3. The Resource Registry component, its runner call after the policy push, and the removal of the stand-ins.
4. Dialogporten, smoke-tested on the service-owner API with the org token.
5. The adapter plus the Storage outbox switch. App instances become dialogs.
6. The Authentication fixes (`pid`, logout, pepper) and OIDC repositories, then Arbeidsflate. Testers see the inbox.
7. The three-line app-frontend change so an app links to the local inbox.

## Open questions for the product owner (collected)

1. Receipt: archived instances link to `/receipt/{id}`, which the stack lacks. Accept the 404, link to the app's
   own receipt page locally (one more adapter directive), or vendor the receipt app later?
2. Outbox delays: the adapter strand proposes 2 s / 0 s locally (production 60 s / 5 s). Keep production values
   behind a setting?
3. Maskinporten client ids: adopt "client id = organisation number" as the convention, or require every client in
   configuration?
4. Should the fake's exchange default to `test=true` (Digdir becomes `ttd`), matching `/Home/GetTestOrgToken` but
   not production, where the adapter and Dialogporten act as `digdir`?
5. A `/Home` helper that completes the authorization-code flow for a seeded client, so `pid` tokens exist for
   Dialogporten's end-user API before Arbeidsflate lands?
6. Redis image: Valkey or Redis 7.2. Licence and naming only.
7. The committed `altinn-orgs.json`: full copy or trimmed list?
8. The two Arbeidsflate patches: keep them, or propose the scheme in `OIDC_PLATFORM_URL` and a `local.altinn.cloud`
   host mapping upstream, which would remove both?
9. Should the Events component classify Dialogporten's event sources as generic events, and does anything local
   subscribe to them?
10. Who owns the monorepo change that makes the app frontend link to the local inbox, and should studioctl pass the
    same settings?

## Not verified (common to all strands)

Nothing was built as an image or run as a container: the machine was at 95 % disk with the shared stack running.
Verification was scratch `dotnet build`s on SDK 10.0.401 (Dialogporten WebApi, the adapter with its directives and
fakes), one adapter host run against the live Authentication, HTTP probes of the running components with the org and
user tokens, and `git apply --check` of the Arbeidsflate patches. Each report lists its own gaps.
