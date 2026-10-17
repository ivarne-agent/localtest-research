# Access Management: vendor the production service

Status: planning (2026-10-17). Working mode agreed with the product owner: one step at a time, each verified and
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
| Register sync | Try production's own sync against the vendored Register's v2 streams first; fall back to a localtest full re-poll over HTTP only if the streams do not carry what it needs. |
| Altinn 2 / SBL bridge | Obsolete and about to be removed upstream: ignored locally with the smallest possible change (stub to empty, no directive unless unavoidable). |
| Rollout | Keep the PDP's role fakes until the sync is proven; retire them last. |

## Steps

1. Pristine copy, build overrides, Dockerfile; host starts against Postgres with outbound dependencies stubbed.
2. Database roles, migrations and static-data seeding as production's init mode does them.
3. Register sync against the vendored Register; verify entities and role assignments for the seeded users.
4. Access packages from `testData.json` into Access Management.
5. Instance delegations end to end: app delegates, Access Management writes the policy, the PDP permits the signee. `signing.ts` green.
6. Authorized parties and the PIP endpoints served by Access Management; remove the PDP's Access Management fakes.

Analysis: `production-access-management-analysis.md` (in progress).
