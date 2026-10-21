# Plan: Correspondence in the new localtest

## Status (2026-10-21)

- **Implemented with a known gap** as task 7 (2026-10-21; `TODO.md` gives `043c483..e96f904`, on `main`
  `94c1acb..e96f904`) in [`../../localtest/components/correspondence/`](../../localtest/components/correspondence/README.md),
  optional group `correspondence` implying `dialogporten`, host port 8033, route `/correspondence`. Smoke test 43 ok and
  2 `KNOWN` on 2026-10-21 (task 14's run with `--with everything`; task 7's own run recorded 46 ok and 2 `KNOWN`).
- Taken: Option A at `6dcf561`, two additive hooks, the fakes as planned, the seed and the Register's KRR stand-in,
  questions 3 to 9 of section 10 as proposed (task 7).
- Known gap: production Correspondence rejects the seed's 6-digit party ids on every PDP-checked call, so app-lib's
  send and the recipient's dialog-token path fail; [`party-ids.md`](party-ids.md). Task 7b, **in progress**, adds the
  component's first `#if !LOCALTEST` site (approved 2026-10-21: any 64-bit integer party id).
- Later changes outside the plan: the KRR stand-in answers `JA` for persons with `reserved` (task 18); Profile's two
  `correspondence/` lookups are production Profile (task 14).

Superseded statements:

- Headline findings and the brief, "no `#if !LOCALTEST` site": task 7b adds one in `XacmlRequestFactory` (in progress).
- Headline findings and the brief item 7, Profile's `correspondence/` lookups in the Register fakes: production
  Profile since task 14.
- Brief, verification, "with the group off: `/correspondence/` returns 404": the front door answers `503` "Component not
  running" with the `--with correspondence` command (task 16).

Research date 2026-10-20 (task 7 of [`../../TODO.md`](../../TODO.md)), written against this repository at `178f3da`.
Upstream pin: [`Altinn/altinn-correspondence@6dcf561`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89)
(`origin/main`, 2026-10-16). The question was whether to vendor the production `Altinn/altinn-correspondence` with fakes
or build a fake-only service, and how it fits with Dialogporten, Notifications and the Resource Registry. One report is
in this folder.

| Report | What it plans | Effort |
| --- | --- | --- |
| [`correspondence.md`](correspondence.md) | Consumer contract (app-lib, the signing flow, Arbeidsflate and dialog tokens, the Notifications condition check, the old localtest), production dependencies against what the stack has, build chain (verified with a scratch build and three scratch starts on a throwaway Postgres), three options, directive sites, fakes, configuration, Postgres, Dockerfile, studioctl group, test-data mapping, inspection API, smoke list, differences, faster-than-production rows, open questions | 7 to 8 days |
| [`party-ids.md`](party-ids.md) | 2026-10-21, after task 7 met it on the stack: why production Correspondence accepts only 8- or 10-digit party ids (`IsPartyId`, history #1595/#1762/#2042/#2043), what breaks with the seed's 6-digit ids, which other components care (none), options (renumber the seed, one `#if !LOCALTEST` site in `XacmlRequestFactory`, an upstream fix, UUIDs). Recommends the site now and the upstream fix, no seed change | half a day (the site); upstream about an hour |

## Headline findings

- **Dialogporten is a hard runtime dependency.** The publish job marks a correspondence `Failed` when no dialog exists.
  Correspondence also connects eagerly to Redis at start. Both exist only in the optional `dialogporten` group, so
  Correspondence belongs to an opt-in group too: proposed is a `correspondence` group that turns `dialogporten` on and
  shares its Redis (database 1).
- **The seam is small.**
  - One host; background work is Hangfire on the same Postgres. There is no Service Bus or Kafka and no second process.
  - Malware scanning and storage-account deployment are already simulated or skipped by upstream in `Development`.
  - Two additive `#if LOCALTEST` hooks in `Program.cs` and **no `#if !LOCALTEST` site** cover everything. That was
    built and started in the scratch run.
    *2026-10-21: task 7b (in progress) adds the first `#if !LOCALTEST` site, in `XacmlRequestFactory`, for the 6-digit party ids.*
  - Fakes come to about 1 070 lines: a file-backed `IStorageRepository`, a no-op `IResourceManager`, auth options for
    http metadata, a refresh-on-miss resolver for dialog-token keys, the shared Maskinporten fake, and the inspection
    API.
- **The build chain is the simplest so far:** no CPM, no `Directory.Build.props`, no warnings as errors, no analyzers.
  The 69 EF migrations ran cleanly on an empty Postgres 18 database as a non-superuser owner, and Hangfire created its
  own schema.
- **Three additions in other components are prerequisites:**
  1. `CorrespondenceService` resources in the Resource Registry seed, with policies that grant `ttd`. The tt02 policies
     grant only `digdir`, and the app's token says `ttd`.
  2. A KRR endpoint in the Register fakes. Correspondence checks reservation for every person recipient and fails
     without it.
  3. Profile's two `correspondence/` contact-point lookups in the Register fakes.
- **App-lib needs no change.** On localtest it takes its token from `/Home/GetTestOrgToken`. The stack's version
  produces a token the production `Sender` policy accepts; this was checked against the scratch host. Today the only
  caller in the test apps is `signering-brukerstyrt`'s signing flow, which already swallows a failed send.
- **The old localtest has nothing for Correspondence.**
- **Deferred:** browser download of attachments from Arbeidsflate. It goes through an OIDC login that needs a third
  site (`IdportenTokenValidator.cs:26`), an OIDC client and a cookie fix. It belongs with task 11.

## Recommendation

**Option A: vendor the API host** with the two hooks and the fakes above, in a `correspondence` group that implies
`dialogporten`, on a `correspondence` database (`postgres-init/70-correspondence.sh`), host port 8033, prefix
`/correspondence`.

A fake-only service (Option B) would invent the status machine and produce no dialog and no notification. Upstream's
own dev stubs (Option C) would test nothing beyond the database.

## Not verified

- No correspondence was created through the stack. Every call downstream of a valid `POST correspondence` was read,
  not run:
  - PDP decision for a seeded resource
  - Register correspondence endpoints
  - dialog creation in Dialogporten with the correspondence scope
  - notification orders and the condition callback
  - Events publish
  - dialog-token calls
- Migrations ran on Postgres 18.6 in a scratch server, not on the shared image's 18.3.
- The Maskinporten-scheme override is registered but was never shown a token.
- Fakes, seed, Register additions and studioctl wiring are not written; their sizes are estimates.
- Image size and build time were not measured.
- Section 11 of the report lists the rest.

## Proposed brief

### 7. Correspondence

**Goal.** Apps that send correspondence (`correspondence/api/v1/…`, today only the signing flow) get production answers
when the stack runs with `--with correspondence`. A sent correspondence is visible:

- in Correspondence's `api/localtest/`
- as a Dialogporten dialog
- in the Notifications inboxes

The default stack is unchanged.

**Plan (2026-10-20).** [`planned-components/correspondence/correspondence.md`](planned-components/correspondence/correspondence.md).
Vendor `Altinn/altinn-correspondence` at `6dcf561` (Option A). Settle the open questions in section 10 first.

**Scope.**

1. `localtest/components/correspondence/`:
   - `vendor/`: `src/`, `Dockerfile` and `docker-compose.yml` copied with `git archive`; the first commit unmodified;
     `vendor/UPSTREAM.md` with the pin and the hook lines.
   - `localtest.build.props/.targets`, copied from Notifications; the targets compile `fakes/**/*.cs` into
     `Altinn.Correspondence.API`.
2. Two additive hooks in `vendor/src/Altinn.Correspondence.API/Program.cs`:
   - after line 49: `AddLocaltestConfiguration(builder.Configuration)`. Defaults go just below the last
     environment-variable source, because `Program.cs:45-49` re-adds the JSON files.
   - after line 163: `AddLocaltestServices(services, config)`.

   No `#if !LOCALTEST` site. A third site needs a report first.
3. Fakes (`Altinn.Correspondence.Localtest`), per report section 6.3:
   - `FileStorageRepository`: all 7 methods, MD5 and hash mismatch as upstream, blob root `/mnt/storage`
   - a no-op `IResourceManager`
   - `RequireHttpsMetadata=false` for JWT and OIDC, and the Maskinporten scheme's metadata pointed at the localtest
     app
   - a refresh-on-miss key resolver for `DialogportenScheme`
   - a copy of `components/shared/fakes/Maskinporten/`
   - configuration defaults per section 6.5 (`MaskinportenSettings:ClientId` must be set, or upstream swaps in its dev
     stubs; `CorrespondenceBaseUrl` the platform origin; Redis `redis:6379,defaultDatabase=1`; `DisableCallsToAltinn2`)
4. `correspondence/api/localtest/` per section 6.9: `correspondences`, `correspondences/{id}` including its Hangfire
   jobs, `jobs`, `status` (migration count, EdDSA keys), `blobs/{id}`.
5. `postgres-init/70-correspondence.sh`: role and database `correspondence`, non-superuser owner, password `localtest`.
   One `COPY` line in `localtest/postgres/Dockerfile`. No reset on start; `env reset` also removes the blob volume.
6. A `Dockerfile` mirroring upstream (`sdk:10.0`/`aspnet:10.0`, port 2525, user `dotnet`, `/mnt/storage`), with the
   `localtest:` deviations of section 6.10.
7. Outside the component:
   - Resource Registry seed: `apps-correspondence-integrasjon4`, `-integrasjon2` (copies of tt02 with `ttd` added to
     the service-owner rule, marked as a localtest deviation) and `ttd-localtest-correspondence`.
   - Register fakes: `POST register/api/localtest/krr/rest/v2/personer` (everyone "NEI").
   - Register fakes: `POST profile/api/v1/correspondence/units/contactpoint/lookup` and
     `…/organizations/notificationaddresses/lookup`.
   - Each with checks in the owning component's smoke test.
8. studioctl (`localtest/studioctl/src/cli/`):
   - group `correspondence` (default off), which turns on `dialogporten`
   - component `correspondence` on host port 8033, container port 2525, health `/health`, route `/correspondence`
   - deps postgres, register, authorization, resource-registry, notifications, events, redis, dialogporten-webapi
   - volume `correspondence-blobs`
   - image spec and override, topology test
   - matrix entry in `.github/workflows/images.yml`
   - no provisioning step

**Verification.**

- `components/correspondence/smoke-test.sh` as in report section 6.12: health and migration count; 401, 403 and 400
  `CORR-01036`; the app-lib attachment and correspondence sequence with the `GetTestOrgToken` credential to
  `Published`; a dialog in Dialogporten; a captured e-mail in the Notifications inbox; checksum failure; scheduled
  publish; restart persistence; the dialog-token path (content, markasread, confirm) with a task 2 token; purge.
- `signering-brukerstyrt` under `studioctl run --with correspondence`: the signee is marked as messaged.
- With the group off: `/correspondence/` returns 404 and `run-platform-regression.sh` is green.
  *2026-10-21: superseded by task 16's front door: `503` "Component not running" with the `--with` command.*
- The Resource Registry, Register, Notifications and Dialogporten smoke tests still pass.
- The new rows are added to `localtest/README.md` "Where localtest is faster than production" (report section 9).

**Inputs.**

- `planned-components/correspondence/correspondence.md`
- `planned-components/notifications/notifications.md` section 2.2
- `localtest/components/{notifications,dialogporten,resource-registry,register,storage}/README.md`
- `localtest/components/shared/README.md`
- TODO.md tasks 2, 9 and 10 (dialog tokens, the group, the `ttd`/`digdir` pain point)

**Depends on.** Tasks 4, 5, 6 and 9a (done). 9b for the Dialogporten group's state. Task 2 phase 2 for the
dialog-token checks. Task 11 owns the Arbeidsflate attachment download.
