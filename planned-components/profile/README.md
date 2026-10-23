# Plan: Profile beyond the Register

**Superseded 2026-10-23 by task 27:** Profile has no host port any more (host port 8036 is gone); it is reached through the front door at `/profile` and by name inside the stack (`http://profile:5030`); see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

## Status (2026-10-21)

- **Implemented** as task 14 (2026-10-21; `TODO.md` gives `ad2c094..097b474`, on `main` `f7b7f50..097b474`) in
  [`../../localtest/components/profile/`](../../localtest/components/profile/README.md), default stack, host port 8036,
  route `/profile` moved off the Register, whose three Profile controllers are deleted. Smoke test 52 ok on
  2026-10-21; every other suite and the platform regression (44 of 44) passed on the same stack.
- Taken: Option A at `d1f9871`, two additive hooks and no `#if !LOCALTEST`, migrations and seed at start, the KRR
  stand-in feeding the production KRR sync, the Resource Registry seed for the organisation-address resource, and the
  section 8 defaults for questions 1, 2, 4, 5, 6 and 7.
- Decided otherwise: question 3. Organisations' official notification addresses **are seeded**: the test data has them
  since task 18 (schema v3), and a brreg stand-in feeds them to Profile's own organisation sync once per organisation
  (user changes win). Fakes came to 974 lines (estimated about 660), the difference being that seed and the brreg echo.
  The KRR stand-in reads `reserved` (task 18), so Rik Forelder (1003) is reserved.

Superseded statements:

- Brief, "no seeded official organisation addresses", and report section 6.6 and section 7 items 2 to 4 ("nobody is
  reserved", "official addresses only through endpoint 27", "no `OrgSyncJob`"): superseded by task 14's seed and task
  18's test data.
- Headline findings, "Correspondence's two on the task 7 branch" in the Register fakes: task 7 merged, and task 14
  deleted those controllers.

Research date 2026-10-20 (task 14 of [`../../TODO.md`](../../TODO.md)), written against this repository at `a3776b3`.
Upstream pin: [`Altinn/altinn-profile@d1f9871`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24)
(`origin/main`, 2026-10-19). The question: which Profile endpoints the stack's consumers need beyond what the Register
component fakes, and whether vendoring production Profile beats extending those fakes.

| Report | What it plans | Effort |
| --- | --- | --- |
| [`profile.md`](profile.md) | Profile's 48 endpoints with auth and callers, and which ones the Register fakes serve; what production Profile needs against the stack; the build chain (verified by a scratch build and two scratch starts against the running stack's Register, PDP and OpenID metadata); options A, B and C; directive sites, fakes, Postgres, seed mapping, Dockerfile, studioctl, smoke tests, differences, open questions | 5 to 6 days |

## Headline findings

- **Consumers.** Apps, Receipt, Authentication, Authorization, Access Management, Notifications and Correspondence call
  11 Profile endpoints. The Register fakes serve all 11, Correspondence's two on the task 7 branch.
  - Arbeidsflate's BFF makes 12 more calls (13 Profile calls in all, of which only `users/current` is served). None of the 12 is served today:
    - favourites
    - profile settings
    - professional notification settings
    - address verification
    - organisation notification addresses
  - One of the 12 (`notificationsettings/private/phonenumber`) does not exist in Profile at the pin.
  - The task 10 adapter, Dialogporten core, Storage, Events and the app frontend do not call Profile.
- **Production Profile is lighter than the task assumed.**
  - It has no SBL bridge, Altinn 2 client, Service Bus or Redis.
  - It needs Postgres, three Register endpoints (no streams), the PDP, Notifications' instant orders and
    Authentication's OpenID metadata.
  - KRR and brreg are reached only by sync jobs that are off by default, plus one brreg write client.
- **The stack serves every dependency, and this was shown against the running stack.** The scratch host was built with
  the vendoring model's props and targets and two additive hooks. With a user token it:
  - passed the party policy through the stack's Register and PDP (user 1337 at DDG Fitness)
  - returned 403 for a party the user may not act for (1001 at EAS Health Consulting)
  - mapped the Register's answer to a person
  - failed only where it reached the database, which was absent on purpose
- **The Register fakes answer close to production's shapes, with different semantics in three places:**
  - Production takes e-mail, phone number and `isReserved` from its KRR mirror. `phoneNumber` is therefore the mobile
    number, not the seed's telephone number.
  - Production's unit contact points are only the addresses users registered for the organisation, not every holder
    of a role there.
  - The contact-point lookups differ in small fields.
- **The seed path stays single.** Test users' e-mail and phone number reach Profile through the production KRR sync
  job, fed by a fake KRR client that reads the Register's KRR stand-in from task 7. Language and party-prompt settings
  are seeded once per user, when the user has no row.

## Recommendation and verdict

**Option A: vendor `Altinn/altinn-profile` at `d1f9871` into the default stack.**

- Two additive `#if LOCALTEST` hooks in `Program.cs`, after lines 57 and 220. No `#if !LOCALTEST`.
- About 660 lines of fakes: Key Vault keys, KRR client, brreg write client, migrations at start, settings seeder,
  inspection API.
- `postgres-init/80-profile.sh`.
- Host port 8036 and route `/profile`, moved from the Register component. The Register's three Profile controllers are
  then deleted.

Option B, extending the Register fakes, would cost about the same: 1 200 to 1 750 lines and 4 to 5 days. It would
re-implement Profile's merge rules inside the Register component and mask upstream gaps.

Option C, a group-dependent or split Profile, puts one route under two owners, or splits state between them.

**Verdict: straightforward.** It meets the owner's three tests:

- **Small directive count:** 2, both additive.
- **Dependencies the stack already has:** Postgres, Register, PDP, Notifications and Authentication, all exercised by
  the scratch host except Postgres and Notifications.
- **No new auth model:** JwtCookie, platform access tokens, scope checks and PDP decisions, as every other vendored
  component uses.

It is not a filler task, though. It adds a container to the default login path. It deletes Register fakes that
Notifications and Correspondence reach through `/profile`. It changes two answers to production semantics (the phone
number and unit contact points). The brief below takes a stated default for each of the seven open questions in
section 8 of the report, so it can be dispatched under the conditional approval. Dispatch it after task 7 merges,
because task 7 adds Profile controllers to the Register that this task deletes.

## Not verified

- No Postgres was used:
  - the six EF migrations on Postgres 18.3 with a non-superuser owner
  - every database read and write
  - the KRR sync and the settings seed
- No end-to-end call reached a production DTO. Authentication's, app-lib's and Notifications' deserialisation of
  production answers was read, not run.
- The org-address resource's PDP decision. The resource is not yet seeded in the Resource Registry.
- The verification-code path through Notifications.
- Arbeidsflate's code-flow tokens against Profile.
- Image build, size, memory and start time.
- Details in section 9 of the report.

## Proposed brief

### 14. Profile beyond the Register

**Goal.** Production `Altinn/altinn-profile` answers every `/profile/api/v1/…` call in the default stack:

- apps, Receipt, Authentication, Authorization, Access Management, Notifications and Correspondence get production
  answers
- Arbeidsflate's profile page works: favourites, settings, professional notification settings with address
  verification through the Notifications inboxes, and organisation notification addresses

The Register component no longer answers `/profile`.

**Plan (2026-10-20).** [`planned-components/profile/profile.md`](profile.md). Vendor at
`d1f9871` (Option A) with the defaults of section 8 unless the owner says otherwise:

- default stack
- production semantics for unit contact points
- no seeded official organisation addresses
  *2026-10-21: decided otherwise: official addresses are seeded from the test data through a brreg stand-in (task 14, with task 18's fields).*
- user changes win over the seed
- no sync call from the app runner
- `phoneNumber` from the seed's mobile number
- endpoint 23 noted in task 11

**Scope.**

1. `localtest/components/profile/`:
   - `vendor/`: `git archive d1f9871 src Dockerfile .dockerignore docker-compose.yml .editorconfig stylecop.json`; the
     first commit unmodified; `vendor/UPSTREAM.md` with the pin, the hook lines and the sync steps.
   - `localtest.build.props/.targets`, copied from Notifications (project name `Altinn.Profile`).
2. Two additive hooks in `vendor/src/Altinn.Profile/Program.cs`:
   - after line 57 (`SetConfigurationProviders`): `AddLocaltestConfiguration(builder.Configuration)`, defaults just
     below the environment-variable source
   - after line 220 (`AddSyncJobs`): `AddLocaltestServices(services, config)`

   No `#if !LOCALTEST`. A third site needs a report first.
3. Fakes (`Altinn.Profile.Localtest`), per report section 6.3:
   - configuration defaults: platform endpoints; Key Vault and Application Insights off;
     `JobSettings:KrrSyncEnabled=true` every minute; `CoreSettings:ProfileCacheLifetimeSeconds=10`
   - the shared-certificate `IPublicSigningKeyProvider`
   - `IContactRegisterHttpClient` over Register's `api/localtest/test-users` and `api/localtest/krr/rest/v2/personer`,
     `LastUpdated` = now, a change only when the content changes
   - a no-op brreg `IOrganizationNotificationAddressUpdateClient`
   - a start-up hosted service that runs, in order: EF migrations with the admin connection string; one KRR sync;
     `profile_settings` for seeded users without a row (language, `doNotPromptForParty`, preselected party)
   - `profile/api/localtest/`: `status`, `POST sync`, `users/{userId}`
4. `postgres-init/80-profile.sh`: roles `platform_profile_admin` (owner of `profiledb`, not superuser) and
   `platform_profile`, password `Password`. One `COPY` line in `localtest/postgres/Dockerfile`. No reset on start.
5. A `Dockerfile` mirroring upstream (same digests, `Migration/` copy, user `dotnet`, `/tmp/logtelemetry`, port 5030).
   `localtest:` deviations: the build context, the props and targets env, the fakes copy, and
   `ASPNETCORE_URLS=http://+:5030` unless studioctl sets it.
6. Outside the component:
   - Register: delete `LocaltestProfileController`, `LocaltestProfileContactPointsController` and
     `LocaltestProfileCorrespondenceController`, with their DI lines, README section and smoke checks (moved to
     Profile's smoke test). Keep `LocaltestKrrController` as the single KRR source.
   - Resource Registry seed: `altinn-profil-api-varslingsdaresser-for-virksomheter` with its tt02 policy (read, write
     and delete for the ER role codes and the two access packages, report section 3).
   - Update the READMEs of the Register, Authentication, Authorization and Notifications components where they say
     "served by the register component".
7. studioctl (`localtest/studioctl/src/cli/`):
   - component `profile`: host port 8036, container port 5030, health `/health`, route `/profile` (removed from
     `register`), `ASPNETCORE_ENVIRONMENT=Development`, the shared certificate, the two connection strings
     (`Host=postgres`), `Localtest__Profile__PlatformEndpoint`
   - deps: postgres, register, authorization, authentication
   - `profile` added to the deps of authentication, authorization, access-management and notifications (and
     correspondence and receipt where present)
   - default stack, not a group
   - image spec and override, topology test, matrix entry in `.github/workflows/images.yml`

**Verification.**

- `components/profile/smoke-test.sh` as in report section 6.9: health and migrations; the moved Register parity
  checks; the production-semantics checks; the Arbeidsflate paths (favourites, settings round trip, verification code
  read from the Notifications inbox, professional address feeding `units/contactpoint/lookup`, 403 for a foreign
  party); org addresses; the Correspondence scope with `--with correspondence`; restart persistence.
- The Register, Authentication, Authorization, Access Management and Notifications smoke tests still pass.
  Notifications' person recipient still reaches an inbox.
- `run-platform-regression.sh` is green.
- One Cypress run logs in as 1337 without a party prompt.
- `payment-test` prefill shows the seed's mobile number.
- New rows in `localtest/README.md` "Where localtest is faster than production": the 10 s id cache, migrations at
  start, the KRR sync every minute.

**Inputs.**

- `planned-components/profile/profile.md`
- `planned-components/notifications/notifications.md` sections 3 and 6.7
- `planned-components/correspondence/correspondence.md` sections 3 and 6.7
- `planned-components/dialogporten/arbeidsflate.md` (the BFF's Profile rows)
- `localtest/components/{register,notifications,authentication,authorization}/README.md`

**Depends on.**

- Tasks 6 (done) and 7 (merged first: the KRR stand-in and the controllers to delete).
- Task 16's group table, for the front door only.
- Task 11 uses the result.
