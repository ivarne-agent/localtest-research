# Register review (2026-10-19, updated 2026-10-20)

Component: [`../components/register/`](../components/register/). Fixes landed in `0c6f093`, `d5c6b2d` and `7d7597d`.

> **Superseded 2026-10-21 (task 14):** the Register no longer answers Profile. Production Profile is its own component
> ([`../components/profile/`](../components/profile/README.md)) on `/profile`; the three Profile controllers were deleted
> (`1f9941d`). The Profile rows in the tables below, findings 7 and 8 and the 20-check smoke test describe the removed
> fakes; the Register's smoke test now has 14 checks. Added since this review: the KRR stand-in
> `register/api/localtest/krr/rest/v2/personer` (task 7) and the schema v3 seed fields `reserved`,
> `notificationAddresses` and `+47` numbers (task 18), see the component README.

## Summary

The localtest Register is production Altinn Register with its Postgres persistence replaced by a read-only in-memory
snapshot built from `testdata/testData.json` plus the test data apps register. The swap goes through one 3-line
`LOCALTEST` hook; everything else is DI and configuration. The fakes also answer Profile's user endpoints, with
Profile's own authorization since this review. The Dockerfile mirrors upstream's.

## Vendored code

- Pin: `Altinn/altinn-register` `f6b5c603` (main). Upstream `main` has not moved since (checked 2026-10-20). The
  `src/apps/Altinn.Register` folder in `Altinn/altinn-auth` is a skeleton without controllers; the move into
  altinn-auth (altinn-auth#4056) is still open, so `altinn-register` stays the source.
- Fidelity: `git archive f6b5c603` of the copied paths, `diff -rq`: only `UPSTREAM.md` and `RegisterHost.cs`
  differ. The upstream `Dockerfile` and `.dockerignore` were added unmodified in `0c6f093`.
- Directive site: `RegisterHost.cs`, after the production persistence block: `#if LOCALTEST AddLocaltestRegister(builder)`.

## Dockerfile

Mirrors upstream's since `d5c6b2d`: pinned `sdk`/`aspnet` `10.0-alpine` digests, `build` and `publish`, `tzdata`
(production's `CcrService` resolves `Europe/Oslo`), the non-root `dotnet` user (uid 1000), `/tmp/logtelemetry`.
Deviations marked `localtest:`: the build context is the component folder with `vendor/` as upstream's context, and
the environment.

## Fakes and what they replace

| Production | Localtest (`fakes/Altinn.Register.Localtest/`) |
| --- | --- |
| Postgres persistence (`IPartyPersistence`, `IPartyExternalRolePersistence`) | Read-only in-memory snapshot (`RegisterStore`, swapped atomically by `RegisterStoreProvider`) |
| External role definitions in Postgres | `StaticExternalRoleDefinitionPersistence` from an embedded JSON |
| Rate limiter on Postgres | `InMemoryRateLimitProvider` |
| Key Vault keys for platform tokens | The shared test certificate for every issuer |
| MassTransit, import jobs, leases, CCR federation | Off (`Altinn:IsTest=true`); their DI dependencies throw if anything asks for them |
| A2 bridge for the v1 endpoints | Off (`Altinn:register:ApiSource:Default=DB`) |
| The Profile service's user endpoints | `LocaltestProfileController` over the same snapshot |
| — | Localtest-only API `register/api/localtest/` (test users, organisations, app test-data registration) |

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Writes | Upserts, role updates, ID-porten e-mail and edu users | `NotSupported`; edit `testData.json` and refresh |
| Change streams | Append-only, version ids only grow | Version ids restart at 1 on every rebuild; role events are only "Added". Access Management's localtest refresh re-reads everything and prunes, which covers it |
| Profile | Separate service, Postgres, KRR | Fake inside Register; `userType` derived from `ssn`; no `profilesettings` PUT/PATCH; the `altinn:portal/enduser` scope on `users/current` is not checked |
| ServiceDefaults environment | From the deployment | `UNKNOWN` (deliberately not `LOCAL`, which would fetch platform tokens remotely) |
| `ASPNETCORE_ENVIRONMENT` | Production | `Development`: developer exception page |

## Findings

All fixed unless marked open.

1. The `Altinn:LocalDev` default never reached ServiceDefaults (added after `AltinnHost` read its settings; the log
   says `Environment: UNKNOWN`). `UNKNOWN` is the right mode, so the default was removed and the comment and README
   say why. Fixed. Raising a configuration switch for the token provider with the auth team is worth it only when a
   component needs `LOCAL` behaviour.
2. The Dockerfile did not mirror upstream (unpinned, root, no `tzdata`, dead HEALTHCHECK). Fixed.
3. Stale "lenient platform-token handler" wording in the README and the build targets. Fixed.
4. Blank lines left over from removed options. Fixed.
5. Nullable warnings in `RegisterStore.cs`. Fixed; the build is warning-free.
6. `UPSTREAM.md` had no sync instructions. Fixed, including the Dockerfile rule and the altinn-auth move.
7. Profile's `users/{id}`, `users/byuuid` and `POST users` were anonymous; production requires a user or org token
   plus a platform access token. Fixed; app-lib sends both (checked through `frontend-test`'s
   `api/v1/profile/user`). `internal/user` stays anonymous as in production.
8. The seed's Profile `userType` was copied from the old localtest's data, where 7 of 9 persons with a national
   identity number were `None`. Fixed on 2026-10-20: the type is set when loading (`SSNIdentified` with `ssn`,
   `SelfIdentified` without) and the field is gone from the schema.

## Verification

- Component smoke test against the stack's Register: 20/20, including the 401/403 checks for missing tokens.
- Root smoke 23/23 and the localtest app smoke 54/54 after each rebuild.
- Cypress: all 13 app folders passed on the non-root image (2026-10-19/20, across two runs).
