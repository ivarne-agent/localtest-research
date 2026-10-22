# Planned components

Plans for replacing localtest emulations with vendored production code. One subfolder per component.
Each plan states the goal, the vendoring approach, where local modifications live, the test-data mapping and
the rollout order. Background research is in [`../localtest-components/`](../localtest-components/).

## Status (2026-10-21)

One row per plan. Task numbers, dates and commit ranges are from [`../TODO.md`](../TODO.md)'s table, corrected to the
commits on `main` where a range there names a commit from before a rebase. Smoke-test counts are the runs recorded on
2026-10-21 in [`../localtest/README.md`](../localtest/README.md), "Status". Each plan carries its own "Status
(2026-10-21)" block at the top, with what was decided otherwise and which of its statements are superseded.

| Plan | Status | Where it landed |
| --- | --- | --- |
| [`mvp-vendoring-model.md`](mvp-vendoring-model.md): the vendoring model | **implemented, with changes** (compose replaced by studioctl, `git subtree` by `git archive` copies, fakes compiled into the host; see its "The model as applied" section) | every [`../localtest/components/<name>/`](../localtest/components/) |
| [`studioctl-integration.md`](studioctl-integration.md), [`studioctl-integration-options.md`](studioctl-integration-options.md) | **partly implemented**: the `localtest-v2` runtime, groups, the 503 front door, provisioning steps 1 to 5 and `env logs -c` in the vendored studioctl; the monorepo move, CI, rollout and the rest are task 13 (unapproved) | [`../localtest/studioctl/`](../localtest/studioctl/); upstream branch `feat/studioctl-localtest-v2-stack` on `ivarne-agent/altinn-studio` |
| [`register/README.md`](register/README.md), [`register/production-register-analysis.md`](register/production-register-analysis.md) | **implemented, differently** (2026-10-17): its own container with one additive hook instead of compiled into `LocalTest.csproj`; its Profile endpoints moved to the Profile component (task 14) | [`../localtest/components/register/`](../localtest/components/register/), smoke 14 |
| [`authentication/production-authentication-analysis.md`](authentication/production-authentication-analysis.md) | **implemented** (2026-10-17, two directive sites); OIDC provider, logout and `pid` added by task 2 (2026-10-20, `4b9b5f7..ddd0ecf`) | [`../localtest/components/authentication/`](../localtest/components/authentication/), smoke 30 (32 with `dialogporten`) |
| [`authentication/localtest-app-identity.md`](authentication/localtest-app-identity.md) | **implemented** (2026-10-20, `1485935`, `180fd3c`) | [`../localtest/localtest/`](../localtest/localtest/) |
| [`access-management/README.md`](access-management/README.md), [`access-management/production-access-management-analysis.md`](access-management/production-access-management-analysis.md) | **implemented** (steps 1 to 6, 2026-10-18); its resource stand-in replaced by the Resource Registry in task 5b | [`../localtest/components/access-management/`](../localtest/components/access-management/) |
| Authorization (PDP): [`../localtest-components/authorization.md`](../localtest-components/authorization.md) (outside this folder) | **implemented** | [`../localtest/components/authorization/`](../localtest/components/authorization/), smoke 30 |
| Storage: [`../localtest-components/storage.md`](../localtest-components/storage.md) (outside this folder) | **implemented**; outbox to the Dialogporten adapter always on since task 10 | [`../localtest/components/storage/`](../localtest/components/storage/) |
| [`dialogporten/README.md`](dialogporten/README.md): index of the five strands | **partly implemented**: four strands done, Arbeidsflate open | see the strand rows |
| [`dialogporten/shared-prerequisites.md`](dialogporten/shared-prerequisites.md) | **partly implemented**: Maskinporten endpoint and shared fake (task 4), OIDC provider (task 2), Redis and Postgres 18 (task 9, `84514a3`); the committed `altinn-orgs.json` **superseded** (task 3 won't do); the app-frontend inbox link **open** (task 11) | [`../localtest/components/shared/`](../localtest/components/shared/), [`../localtest/localtest/`](../localtest/localtest/) |
| [`dialogporten/resource-registry.md`](dialogporten/resource-registry.md) | **implemented**, task 5 (2026-10-20; 5a `e0cba40..b6f4c47`, 5b `9b7278d..aaa099e`) | [`../localtest/components/resource-registry/`](../localtest/components/resource-registry/), smoke 43 |
| [`dialogporten/dialogporten-core.md`](dialogporten/dialogporten-core.md) | **implemented**, task 9 (2026-10-20; 9a `4ab03f0..76b8787`, 9b `4749bd7..d7827bf`), optional group `dialogporten` | [`../localtest/components/dialogporten/`](../localtest/components/dialogporten/), smoke 60 |
| [`dialogporten/dialogporten-adapter.md`](dialogporten/dialogporten-adapter.md) | **implemented**, task 10 (2026-10-21, `c940fd6..3c9b03f`), group `dialogporten` | [`../localtest/components/dialogporten-adapter/`](../localtest/components/dialogporten-adapter/), smoke 39 |
| [`dialogporten/arbeidsflate.md`](dialogporten/arbeidsflate.md) | **open**: task 11, unapproved; its prerequisites (tasks 2, 9, 10, 14) are done | none |
| [`notifications/README.md`](notifications/README.md) | **implemented**, task 6 (2026-10-20, `45e99a1..0b38452`), default stack | [`../localtest/components/notifications/`](../localtest/components/notifications/), smoke 64 |
| [`correspondence/README.md`](correspondence/README.md) | **implemented with a known gap**, task 7 (2026-10-21, `94c1acb..e96f904`), group `correspondence`; the party-id fix [`correspondence/party-ids.md`](correspondence/party-ids.md) is task 7b, **in progress** | [`../localtest/components/correspondence/`](../localtest/components/correspondence/), smoke 43 ok + 2 `KNOWN` |
| [`receipt/README.md`](receipt/README.md) | **implemented**, task 16 (2026-10-21, `f40027a..319a712`), in the optional group `receipt` rather than the default stack the plan recommended | [`../localtest/components/receipt/`](../localtest/components/receipt/), smoke 27 |
| [`profile/README.md`](profile/README.md) | **implemented**, task 14 (2026-10-21, `f7b7f50..097b474`), default stack | [`../localtest/components/profile/`](../localtest/components/profile/), smoke 52 |
| [`inspection-gui/README.md`](inspection-gui/README.md) | **deferred**: task 8 (2026-10-20), pending the owner's decision after task 17 | none |
| [`admin-running-services/README.md`](admin-running-services/README.md) | **research done**, task 17 (2026-10-20); its recommendation (build task 8, do not reuse admin) stands, task 8 stays deferred | none |
| [`aspire/README.md`](aspire/README.md), [`aspire/aspire.md`](aspire/aspire.md) | **research done**, task 20 (2026-10-21): the components map to Aspire container resources, but the front door (Aspire's YARP cannot strip `Secure`), provisioning and the app environment are studioctl's; recommends documenting an AppHost running the app next to `studioctl env up`, and a hosting package generated from studioctl's component table only if Aspire becomes a supported way in | none |
| [`service-discovery.md`](service-discovery.md): components resolving each other by name (`Microsoft.Extensions.ServiceDiscovery`) | **research done, prototype on branch** `research/service-discovery`, task 25 (2026-10-22): the registration in every .NET host (four had it already through `AltinnHost`), compiled per component; the shared base URLs, metadata fetches and the `dependsOn` cycles are what remains | none on `main` |


## Conventions for vendored production code

> 2026-10-21: written for the Register plan's compile-into-localtest approach. As applied, the vendored copies are `git archive` copies with `vendor/UPSTREAM.md`, not subtrees, the fakes live in each component's `fakes/` next to `vendor/`, and `LOCALTEST` comes from `localtest.build.props`; see [`mvp-vendoring-model.md`](mvp-vendoring-model.md), "The model as applied".

- Production source is brought in with `git subtree` (non-squashed, the monorepo's existing practice in the
  root `Makefile`), so upstream history is preserved and `git subtree pull` merges later upstream changes.
- Every local modification to a vendored file sits inside a preprocessor block with the **production code
  first and unchanged**:

  ```csharp
  #if !LOCALTEST
      // production code exactly as upstream has it
  #else
      // localtest replacement
  #endif
  ```

  Keeping the upstream lines in the `#if !LOCALTEST` branch means an upstream change to those lines merges
  as a normal diff hunk against the same text, instead of a conflict against deleted code. Additions that
  have no production counterpart use a bare `#if LOCALTEST … #endif` block. Never edit a vendored line in
  place.
- `LOCALTEST` is defined in `LocalTest.csproj` (`<DefineConstants>$(DefineConstants);LOCALTEST</DefineConstants>`).
  Vendored projects that localtest compiles get the same define from a `Directory.Build.props` in the
  localtest tree, not from edits to the vendored `.csproj` files.
- Files that localtest should not compile at all (Postgres persistence, import jobs, Aspire host) are
  excluded by glob from the localtest-side project file rather than deleted, so the subtree stays complete.
- Localtest-only implementations (in-memory persistence over the test-data model, auth policies) live
  outside the vendored directory, under `src/Runtime/localtest/src/<Component>/`, and are wired in from the
  `#else` branches or from localtest's own `Startup.cs`.

## Answers to the plans' open questions (2026-10-21)

The plans' "Open questions for the product owner" lists, each question with the answer that was taken and where, or
**open**. Sources: the decision paragraphs of tasks 2, 4, 5, 6, 7, 9, 10, 14, 16 and 18 and the groups paragraph in
[`../TODO.md`](../TODO.md), and the component READMEs where the implementation answered a question the owner did not.

### [`dialogporten/README.md`](dialogporten/README.md), "Open questions for the product owner (collected)"

| # | Question | Answer |
| --- | --- | --- |
| 1 | Receipt: accept the 404, link to the app, or vendor the receipt app | Vendor it: task 16, optional group `receipt` (the 404 was accepted in the meantime, task 10) |
| 2 | Outbox delays 2 s / 0 s locally | Taken, by configuration in Storage's fakes (task 10); the production values are listed in `localtest/README.md`, "Where localtest is faster than production". No switch back to production values |
| 3 | Maskinporten client id = organisation number | `consumer_org` from the assertion, else a nine-digit client id, else 991825827; no client table, unknown clients accepted and logged (task 4) |
| 4 | Fake exchange with `test=true` | No: production semantics, `test=true` only when `UseAltinnTestOrg` is true (task 4); Dialogporten and the adapter act as `digdir` (task 9) |
| 5 | A helper that completes the code flow for `pid` tokens | Yes, as `GET localtest/api/authentication/token`, not under `/Home` (task 2) |
| 6 | Valkey or Redis 7.2 | Redis `7.2-alpine` pinned by digest, kept in the `dialogporten` group (task 9; `components/dialogporten/README.md`) |
| 7 | Full or trimmed `altinn-orgs.json` | Moot: task 3 is won't do, the stack reads altinncdn.no |
| 8 | Upstream the two Arbeidsflate patches | **open** (task 11) |
| 9 | Events and Dialogporten's event sources | Answered by 9b: production Events registers them as generic events and nothing local subscribes; a source-filter fix is proposed in `components/dialogporten/README.md`, "Events", not made (**open**) |
| 10 | Owner of the app-frontend inbox link, studioctl settings | **open** (task 11) |

### [`dialogporten/dialogporten-core.md`](dialogporten/dialogporten-core.md) section 15

| # | Answer |
| --- | --- |
| 1 | Shared Postgres 18 (`84514a3`), no `uuidv7()` shim (task 9) |
| 2 | As the README's question 5 |
| 3 | Opt-in group `dialogporten` (task 9) |
| 4 | Internet, as the README's question 7 |
| 5 | As the README's question 9 |

### [`dialogporten/shared-prerequisites.md`](dialogporten/shared-prerequisites.md) section 8

| # | Answer |
| --- | --- |
| 1 | One shared Postgres 18 (`84514a3`) |
| 2 | Redis 7.2, as the README's question 6 |
| 3 | As the README's question 3 |
| 4 | No, as the README's question 4 |
| 5 | Configuration: `Localtest:Authentication:OidcClients`, two clients seeded, `localtest-smoke` and `arbeidsflate-localtest` (task 2) |
| 6 | Moot, as the README's question 7 |
| 7 | **open** (task 11) |

### [`dialogporten/resource-registry.md`](dialogporten/resource-registry.md) section 6.5

| # | Answer |
| --- | --- |
| 1 | A built-in seed in the registry's fakes, applied at start through the production service layer; the runner pushes no seed (task 5) |
| 2 | A separate `resourceregistrydb` (task 5) |
| 3 | The registry reads altinncdn.no (task 5; task 3 won't do) |
| 4 | No cache locally: a fake replaces the Storage applications client, no refresh endpoint (task 5) |
| 5 | The registry is the only source; the PDP's folder moved into the seed (task 5) |

### [`dialogporten/dialogporten-adapter.md`](dialogporten/dialogporten-adapter.md) section 12

| # | Answer |
| --- | --- |
| 1 | The adapter is in the `dialogporten` group; Storage always sends and drops a command with one log line when the adapter is not running (task 10) |
| 2 | As the README's question 1 |
| 3 | As the README's question 2 |
| 4 | An input to task 8 (deferred), not part of 10 |

### [`dialogporten/arbeidsflate.md`](dialogporten/arbeidsflate.md) section 7.7

| # | Answer |
| --- | --- |
| 1 | **open** (task 11) |
| 2 | Changed premise: production Profile serves the profile page's endpoints since task 14, so nothing grows in the Register; whether the page is in task 11's scope is **open** |
| 3 | Internet (task 3 won't do) |
| 4, 5 | **open** (task 11) |

### [`notifications/notifications.md`](notifications/notifications.md) section 6.14

| # | Answer |
| --- | --- |
| 1 | `Delivered` for every captured message; the capture is a set of inboxes with a `receiving` flag, always true for now (task 6) |
| 2 | No normalisation in any fake (task 6); the seed changed instead: every number in full `+47` form (task 18) |
| 3 | The Register component's fakes (task 6); **superseded by task 14**: production Profile serves them |
| 4 | No failure simulation yet; a later per-inbox reception toggle (task 6) |
| 5 | Not then (task 6); the `reserved` flag came with task 18 (schema v3), read by the KRR stand-in and mirrored into `isReserved` |
| 6 | Keep the shared Maskinporten fake in the condition client (task 6) |
| 7 | Yes, attachments by reference (task 6) |
| 8 | No shared GUI (task 6) |

### [`correspondence/correspondence.md`](correspondence/correspondence.md) section 10

| # | Answer |
| --- | --- |
| 1 | Opt-in, no fake-only variant; the default stack unchanged (task 7) |
| 2 | Its own group `correspondence`, implying `dialogporten`, sharing its Redis (database 1) (task 7) |
| 3 | KRR stand-in in the Register fakes (task 7); the `reserved` flag came with task 18 and the stand-in answers `JA` for reserved persons |
| 4 | Production scope required (task 7); production Profile serves the lookups since task 14 |
| 5 | `ttd` beside `digdir` in the seeded tt02 policies, marked as a localtest deviation (task 7) |
| 6 | Seeded as tt02 has it (task 7) |
| 7 | Pinned by digest as a `localtest:` deviation (task 7) |
| 8 | Task 11 (task 7) |
| 9 | The inspection API's `jobs` view, no directive (task 7) |

### [`correspondence/party-ids.md`](correspondence/party-ids.md) section 4

| # | Answer |
| --- | --- |
| 1 | Approved 2026-10-21 with a change: the `#if !LOCALTEST` site accepts any 64-bit integer party id, not only unsigned 32-bit; task 7b **in progress** |
| 2 | **open** |
| 3 | The seed is not renumbered (task 7b brief); the sentence in `localtest/testdata/README.md` is **open** |
| 4, 5 | **open** |

### [`receipt/receipt.md`](receipt/receipt.md) section 7.3

| # | Answer |
| --- | --- |
| 1 | Decided otherwise: its own optional group `receipt`, implying nothing (task 16), not the default stack |
| 2 | Yes, the `selfLinks` rewrite fake (task 16) |
| 3 | Stays as upstream has it; raised upstream with task 11 (task 16) |
| 4 | Yes: task 10's smoke step expects the adapter's 200 Markdown receipt (task 10, corrected 2026-10-20) |
| 5 | Pin `babc0ed`, re-pin on backend changes only (task 16) |
| 6 | Image builds may reach the npm registry (task 16) |

### [`profile/profile.md`](profile/profile.md) section 8

| # | Answer |
| --- | --- |
| 1 | Default stack (task 14) |
| 2 | Production semantics (task 14) |
| 3 | Decided otherwise: official organisation addresses are seeded from the test data (task 18's fields) through a brreg stand-in and Profile's own organisation sync (task 14; `components/profile/README.md`) |
| 4 | User changes win (task 14) |
| 5 | No runner call; the sync jobs run every minute, `POST profile/api/localtest/sync` on demand (task 14) |
| 6 | `phoneNumber` is the KRR mobile number (task 14) |
| 7 | Noted for task 11 (task 14) |

### [`inspection-gui/inspection-gui.md`](inspection-gui/inspection-gui.md) section 8 and [`admin-running-services/admin-running-services.md`](admin-running-services/admin-running-services.md) section 6

All **open**: task 8 is deferred and the owner has not decided whether to un-defer it (admin question 1). The GUI
plan's detection of an off component by the text 404 is superseded by the front door's 503 (task 16), which the plan
already records in its section 5.

### [`studioctl-integration.md`](studioctl-integration.md), "Open questions"

| Question | Answer |
| --- | --- |
| The certificate | Decided otherwise: the test certificate is embedded in studioctl and mounted at `/certs`, not generated per installation |
| Frontend version override and cookie-domain rewriting | **open** (task 13) |
| Re-push on change | **open** (task 13) |
| One Postgres image with baked-in init scripts | Baked in: [`../localtest/postgres/`](../localtest/postgres/) |
| `studioctl env` listing the components | Per component (one container, health poll and route each, groups on top); `topology.yaml` was not checked for this status |

### [`register/README.md`](register/README.md), "Open questions"

| # | Answer |
| --- | --- |
| 1 | One additive hook in `RegisterHost.cs`, no Core directives (the component README) |
| 5 | `test-users.schema.v3.json` in `localtest/testdata/` (task 18; v2 was never published) |
| 2, 3, 4, 6, 7 | Not tracked in `TODO.md`; not verified here |

### [`authentication/localtest-app-identity.md`](authentication/localtest-app-identity.md), "Open questions"

Not tracked in `TODO.md`; not verified here.
