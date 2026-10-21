# Register: vendor the production Register into localtest

## Status (2026-10-21)

- **Implemented, differently** (2026-10-17, `ffe60ff`, `c84a68e..0dddadc`), in
  [`../../localtest/components/register/`](../../localtest/components/register/README.md): production Register as its
  own container in this research repository's stack, not compiled into the old localtest's `LocalTest.csproj`. Smoke
  test 14 ok on 2026-10-21 (through the proxy).
- As built: the vendored copy is `git archive`-equivalent with `vendor/UPSTREAM.md` (no subtree), the production host
  runs with one additive `#if LOCALTEST` hook in `RegisterHost.cs`, the in-memory persistence is a fakes project
  (`fakes/Altinn.Register.Localtest/`) linked by `localtest.build.targets`, and the seed is
  `components/register/testdata/testData.json` (schema v3 since task 18) plus the `testData.json` each app runner posts.
- Since then: the KRR stand-in (task 7), and task 14 deleted the Register's Profile controllers (production Profile
  is its own component).

Superseded statements:

- "Approach in one paragraph", steps 0 to 2: subtrees in the monorepo, the allowlist compiled into `LocalTest.csproj`,
  localtest's `Startup.cs`, the `PersonsController.cs:96` directive. None of it applies to the component as built.
- Step 4: the neighbours are separate production components that call the Register over HTTP; Profile is production
  Profile (task 14).
- Step 5: rollout through the monorepo's localtest image; the stack is run by studioctl's `localtest-v2` runtime.

Status: planned (2026-10-16); see "Status (2026-10-21)" above. Background: [`production-register-analysis.md`](production-register-analysis.md)
(includes a verified scratch build of localtest with the production controllers compiled in) and the strand
report [`../../localtest-components/authn-profile-register.md`](../../localtest-components/authn-profile-register.md).

## Goal

Localtest serves the **production Register API surface** (v1 and v2) from the production controllers and Core
services, compiled from vendored upstream source, with persistence implemented in memory over localtest's
test-data model ([`testdata/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata) plus each app's `testData.json`). The fake login page and the test-user picker stay
in localtest and read the same store. Upstream changes arrive with `git subtree pull` and show up as compile
errors or merge conflicts instead of silent drift.

Why Register first: every real platform service we may later run next to localtest (PDP, Access Management,
Profile, Authentication, Storage) resolves people and organisations through Register, mostly v2 by party UUID.
A production-shaped local Register is the prerequisite for all of them.

## Approach in one paragraph

Two split subtrees from `Altinn/altinn-register` ([`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register) and
[`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts)) land under one vendored root in the monorepo. [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj) references
[`Altinn.Register.Core`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core) and [`Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) as projects and compiles an **allowlist** of about 40
files from the [`Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/) host project (controllers, mediator handlers, auth requirement types). The
production host ([`Program.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs), [`RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs)), Postgres persistence, Aspire, MassTransit transport, imports
and SBL-bridge clients are not compiled at all. Localtest's own [`Startup.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs) registers the ~15 services the
controllers need, selects the DB-backed handlers with `Altinn:register:ApiSource:Default=DB`, and supplies an
in-memory `IPartyPersistence` family built from the test-data model. Every edit inside a vendored file uses

```csharp
#if !LOCALTEST
    // production code, unchanged
#else
    // localtest replacement
#endif
```

so upstream hunks merge against their original text. The analysis found that because the host composition is
not reused and unwanted files are simply not listed, **very few directive sites are needed**: one required
([`PersonsController.cs:96`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PersonsController.cs#L96), a claim constant missing from localtest's older `JWTCookieAuthentication`) and an
optional group of eleven in Core's service registration (A2 and CCR handlers), which can instead be satisfied by
eight throwing stub registrations in localtest code. The directive rule still applies to any future edit.

## Steps

### Step 0: land the contracts switch (days)

Rebase and merge [altinn-studio#20436](https://github.com/Altinn/altinn-studio/pull/20436): localtest moves from
the frozen `Altinn.Platform.Models` to [`Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) 1.8.0 (`V1` namespace) and gains the
deterministic party UUID (`GuidHelpers.FromInt`) for test data without one. Carry its open review points:
`PartyLookup` must reject both-or-neither identifiers, `PersonLookupIdentifiers` still aliases the old package,
and the UUID helper overflows on `int.MinValue`. This is independent of the rest and already de-risks the model
types. Its parent [altinn-studio#20155](https://github.com/Altinn/altinn-studio/pull/20155) decides the App SDK
side (vendored `Party` for XML serialization) and does not block this step.

### Step 1: vendor the source

1. Add the vendored root, for example `src/Runtime/register/`, with the upstream relative layout preserved so
   Core's relative `ProjectReference` to Contracts resolves unchanged:
   - [`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register) ← `git subtree split --prefix=src/apps/Altinn.Register` of upstream `main`
     (234 commits, about 4 s, deterministic)
   - [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) ← split of [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) (45 commits)
   - monorepo-owned copies of the upstream root [`Directory.Build.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.props), [`Directory.Build.targets`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.targets),
     [`Directory.Packages.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Packages.props) and [`stylecop.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/stylecop.json) (the split trees import them with `GetPathOfFileAbove`).
     This is where `LOCALTEST` is defined for the vendored projects if the Core directive sites are used.
2. Add a [`Makefile`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile) macro that fetches upstream, splits the two prefixes and `git subtree merge`s each into its
   destination; the existing `pull-subtree` macro only pulls whole repositories
   ([`Makefile:90-99`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile#L90-L99)).
3. Pin the vendored commit in the PR description and in the vendored root README, since split trees carry no
   upstream tag.

Register moves into `Altinn/altinn-auth` with [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056)
while keeping both paths, so later pulls should split the same prefixes from `altinn-auth` and continue as
normal merges. Not confirmed until the move happens; the whole-repo alternative would not survive it.

### Step 2: compile the production API into localtest

1. [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj): project references to [`Altinn.Register.Core`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core) and [`Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) (plus the
   closure `Altinn.Register.Contracts.MassTransit` and `ServiceDefaults.MassTransit.Abstractions`), the allowlist
   of host files, and the six added packages listed in the analysis (`MassTransit.Abstractions` is the only
   MassTransit piece). Files excluded: see the table in the analysis (host, Swagger descriptions, CCR, cleanup,
   import, bridge clients, debug/error/CCR/support/self-identified controllers).
2. Remove localtest's own [`Controllers/Register/*.cs`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register) and the [`Services/Register`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Register) wrappers in the same PR; the
   production routes would otherwise be ambiguous.
3. Localtest composition (`AddLocaltestRegister()` in localtest code, not in vendored code):
   - Core services via the upstream extension, DB-backed handlers via `Altinn:register:ApiSource:Default=DB`,
     `V2.PartyController` registered as a transient service (the facade controllers call it directly).
   - Authorization policies `InternalOrPlatformAccess` and `PlatformAccess` registered with the **production
     requirement types**, satisfied by PEP's scope handler for test tokens carrying
     `altinn:register/partylookup.admin` and by a localtest handler for `IAccessTokenRequirement` that accepts a
     present `PlatformAccessToken` header (signature validation against localtest's certificate is an open
     question). Do not reuse the production `AccessTokenHandler` with verification disabled; it still fails a
     present but unverifiable token.
   - An in-process `IAuthorizationClient` that calls localtest's authorization `PartiesService` instead of the
     vendored HTTP client, so `parties/{id}` validation does not loop back over HTTP.
   - API versioning with `AssumeDefaultVersionWhenUnspecified = true`, otherwise every unversioned localtest
     controller returns 400 (verified).
   - Production JSON options scoped to the Register controllers; localtest's global `JsonStringEnumConverter`
     turns v1 `partyTypeName: 2` into `"Organisation"` (verified).
4. Directive sites: [`PersonsController.cs:96`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PersonsController.cs#L96) (`#if !LOCALTEST` reads `AltinnCoreClaimTypes.PartyUUID`,
   `#else` compares the claim type string). Choose between the eleven Core registration sites and eight throwing
   stubs; the stubs keep Core directive-free at the cost of dead registrations.

### Step 3: in-memory persistence over the test-data model

Implement, in localtest code, against Core's interfaces and reuse Core's `UnitOfWorkManager` unchanged:

| Interface | Members | Must work for localtest |
| --- | ---: | ---: |
| `IPartyPersistence` | 14 | 8 (`GetPartyById`, `LookupParties` with field masks and main/sub-unit transforms, streams, max version ids, `UpsertParty` for set-username) |
| `IPartyExternalRolePersistence` | 6 | 5 |
| `IExternalRoleDefinitionPersistence` | 4 | 4 (static role table; source to decide, see open questions) |
| `IRateLimitProvider` | 2 | 2 |

Estimated 1,200 to 1,500 lines plus tests, against about 3,850 lines of Postgres code upstream. Imports, sagas
and CCR upserts throw `NotSupportedException`.

Data mapping (details in the analysis): persons and orgs from [`AppTestDataModel`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/AppTestDataModel.cs) map onto `PartyRecord`,
`PersonRecord`, `OrganizationRecord`; party UUID from the test data or the deterministic helper; version ids
assigned on load; `partyRoles` become `ExternalRoleAssignment`s with source CCR and lower-case identifiers
(`daglig-leder` and friends); user ids and user names come from the person entry. Production fields with no
localtest source (dates of birth, unit status details, created/modified timestamps) get synthesized defaults.

### Step 4: move the neighbours onto the same store

Profile (`users/current`, `users/{id}`, and the missing `users/byuuid` and `POST users`), the fake login and
party picker, Storage's instance-owner lookup, and the PDP's roles and party list read the Register store through
one in-process service instead of [`TestDataService`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/TestDataService.cs) directly. This removes the duplicate role source
([`testdata/authorization/roles`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/roles) versus party definitions) and is what later lets a real PDP or Access Management
container consume `internal/parties/stream` and `external-roles`.

### Step 5: verification and rollout

- Unit tests for the persistence layer; contract tests that replay the production controller tests where they
  do not need Postgres (not confirmed how many are reusable).
- The app backend integration tests in [`src/App/backend/test/Altinn.App.Integration.Tests`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/test/Altinn.App.Integration.Tests) and the frontend e2e
  party mocks exercise `parties/{id}`, `lookup`, `partylist`, `persons`, `apps/parties/query`.
- Keep the old localtest Register controllers on `main` until the vendored path passes those suites; the image
  follows `main` and a developer's studioctl may be older, so no studioctl change is needed for this component.

## Blocking issues found by the scratch build

1. **Invalid identifiers in the built-in test data.** Production `PersonIdentifier`/`OrganizationIdentifier`
   enforce mod-11 checksums. Three built-in persons (`01017512345`, `01039012345`, `01899699001`) and five
   orgs (`897069631`, `897069650`, `897069651`, `897069653`, `900000001`) fail, and these numbers are used in
   frontend e2e mocks and backend tests across the monorepo. Options: replace the built-in users (breaking for
   anyone who hard-coded them), skip invalid entries with a warning, or relax the check under `#if !LOCALTEST` in
   the vendored Contracts (changes a published contract type; least attractive).
2. **JSON enum format** and **API versioning** as described in step 2.
3. **Localtest's `JWTCookieAuthentication` 3.0.1.** Bumping to 4.0.4 breaks [`OpenIdController`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authentication/OpenIdController.cs) and
   [`AuthenticationService`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authentication/Implementation/AuthenticationService.cs) (verified), hence the one required directive site. A later localtest-side upgrade
   removes it.

## Open questions

1. Core directive sites versus throwing stubs (decides whether the vendored root props define `LOCALTEST`).
2. Strictness of the `PlatformAccessToken` check locally: presence only, or signature against localtest's cert.
3. Source and refresh of the external-role definition table: replay upstream SQL migrations or snapshot from tt02.
4. v1 `name` for persons: A2 style (`LAST FIRST MIDDLE`) or today's `First Middle Last`.
5. Whether the extended test data (roles by CCR identifier, unit status, date of birth, system-user parties)
   warrants `test-users.schema.v2.json`; the current model already has optional `partyUuid`.
6. Contracts version: source at the pinned commit says 1.7.0 while NuGet has 1.8.0; which commit built 1.8.0.
7. MassTransit licence if upstream moves to v9 (only `MassTransit.Abstractions` is compiled today).

## Pinned sources

- `Altinn/altinn-register` at [`f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0)
- `Altinn/altinn-studio` at [`447e53eb9797d4fffe63d0c61d05a1f692b4ec48`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48)
