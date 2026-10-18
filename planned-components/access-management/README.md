# Access Management: vendor the production service

Status: steps 1 to 6 done (2026-10-18). The plan is complete. Working mode agreed with the product owner: one step at a time, each verified and
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
5. **Done, verified.** The app delegates signee rights through the production `POST accessmanagement/api/v1/app/delegations/...` (platform access token), Access Management validates the parties (Register import), the app resource (`dbo.resource`, filled by the production resource sync against the local Resource Registry stand-in) and the delegable rights (the pushed app policy in the shared policy store), writes the delegation policy into the store and the change into Postgres; the PDP asks Access Management's `policyinformation/getdelegationchanges` through the production wrapper and reads the delegation policy version from the same store. Verified: `signing.ts` green; the signee (user 10002) reads the instance (200) while a non-signee gets 403, before and after a refresh; a key-role holder of the delegated-to organisation is permitted too. Note: the production PDP caches a delegation lookup for five minutes keyed by subject, party and app (not instance), so a user who was checked on one instance is denied on a newer one until the cache expires or `POST authorization/api/localtest/refresh` drops it; the Cypress spec's own comment describes the same limitation. Fixes on the way: organisations without `unitType` get one in the Register (the import drops the whole page otherwise), the refresh keeps Access Management's own rightholder assignments, the runner replaces all four placeholder spellings as Designer does.
6. **Done, verified.** The ingress routes all of `/accessmanagement/` to Access Management. The PDP uses the production `AccessManagementWrapper` for roles, access packages, delegation changes and authorized parties; its seed-based wrapper, authorized-parties controller, role mapping and Altinn 2 roles fake are deleted. Access Management's `RightsDelegationApi` flag is on (it gates the internal `authorizedparties` the PDP calls), and `LocaltestOidcMetadata` lets production's JWT cookie handler read the authentication component's http metadata for the configured issuer. Role codes arrive in lower case; every role-code match in the test apps' policies is `string-equal-ignore-case`. Verified: PDP smoke test (now against the stack) 30 ok, root smoke test 23 ok, authorized parties for Sophie Salt and Ola Nordmann from the real endpoint, all 72 Cypress specs for frontend-test, component-library and signering-brukerstyrt green (330 tests).

Analysis: `production-access-management-analysis.md` (in progress).
