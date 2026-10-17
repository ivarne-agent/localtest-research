# Access Management: vendor the production service

Status: steps 1 to 4 done (2026-10-17 evening), step 5 next. Working mode agreed with the product owner: one step at a time, each verified and
reported before the next starts.

## Why

The app's signing feature delegates instance rights to signees through Access Management
(`accessmanagement/api/v1/app/delegations/...`), and the production PDP asks Access Management for delegation
changes, roles and access packages. With Access Management emulated inside the PDP's fakes, `signing.ts` cannot
pass and every role answer is a local approximation. Vendoring the production service removes that emulation.

## Decisions

| Topic | Decision |
| --- | --- |
| Source | `Altinn/altinn-auth` `src/apps/Altinn.AccessManagement` plus its `src/libs` closure, pristine copy first, then `#if !LOCALTEST` only. |
| Database | Its own database on the stack's shared Postgres 16, created by the existing init script. |
| Blob storage | No Azurite. File-backed fakes as for Storage and the PDP; how the PDP reads delegation policies written by Access Management is decided by the analysis (shared volume or HTTP). |
| Roles and access packages | **Both come from `testData.json`.** Register already serves the external roles; access packages must reach Access Management from the same seed (mechanism to be chosen from the analysis). |
| Register sync | Production's own sync runs unchanged against the vendored Register's v2 streams at init. Refresh is a push from the app runner (`POST accessmanagement/api/localtest/refresh`, a full re-poll, since the Register's stream is a snapshot that renumbers versions on rebuild); no polling, no Register-to-Access-Management coupling. |
| Altinn 2 / SBL bridge | Obsolete and about to be removed upstream: ignored locally with the smallest possible change (stub to empty, no directive unless unavoidable). |
| Rollout | Keep the PDP's role fakes until the sync is proven; retire them last. |
| Self-identified users | Production-faithful: modelled as ID-porten e-mail registrations (no username, display name = e-mail, `urn:altinn:person:idporten-email`), as `register.get_or_create_email_user` creates them. The test user is `selvregistrert.bruker@example.com`; legacy username users are not modelled. |
| Unit types | Explicit in the built-in `testData.json`; sub-units `BEDR`, `Testdepartementet` `STAT`, `Skog og Fjell Consulting` `ENK`. |

## Steps

1. **Done.** Pristine copy (`05c82c1`), build overrides, Dockerfile, three directive sites, five fakes; the host starts on the shared Postgres.
2. **Done, verified.** `LocaltestDatabase` creates the roles and database at start (no volume reset); EF (49 migrations) and Yuniql ran; static seed in place: 149 roles, 223 packages, 1 420 role-package rows; `/health` 200 and `meta/info/accesspackages/export` answers.
3. **Done, verified.** Production's `PartySyncService`/`RoleSyncService` run unchanged at init against the vendored Register's v2 streams (`RegisterSync.Import` on): 22 parties and 16 external role assignments imported, sub-units with `ParentId` via `hovedenhet`, `daglig-leder` for Sophie Salt on DDG Fitness. Open: a refresh after apps register their `testData.json` at runtime (the import runs only at start) and pruning of assignments that disappear.
4. **Done, verified.** `POST accessmanagement/api/localtest/refresh` (also run at start) reruns the production import from version 0 and writes the seed's own-party roles, Altinn 2 role codes and access packages through Access Management's services (`GetOrCreateAssignmentInternal`, `ImportAssignmentPackages`), then prunes role and package assignments to test users the seed no longer grants. Verified: Ola Nordmann's `regna`/`daglig-leder`/`privatperson` on his own party, Sophie Salt's Altinn 2 codes on DDG Fitness, Pengelens Partner's `lonn` and `regnskapsforer-med-signeringsrettighet` packages on DDG Fitness; the sync's structural rows untouched. The runner (`run-app.sh`) calls the refresh after registering an app.
5. Instance delegations end to end: app delegates, Access Management writes the policy, the PDP permits the signee. `signing.ts` green.
6. Authorized parties and the PIP endpoints served by Access Management; remove the PDP's Access Management fakes.

Analysis: `production-access-management-analysis.md` (in progress).
