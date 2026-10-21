# Storage review (2026-10-19, updated 2026-10-20)

Component: [`../components/storage/`](../components/storage/). Reviewer: Claude, on the user's request; fixes
landed in `1dcb31e`, `9feefb2` and `2f577ae`.

> **Superseded 2026-10-21 (task 10):** Wolverine is no longer switched off. The outbox runs in every stack
> (`WolverineSettings:EnableSending=true`, delays 2 s / 0 s): a localtest handler posts each command to the Dialogporten
> adapter's `api/localtest/enqueue`, and logs one "not running" line when the `dialogporten` group is off (the front door
> answers 503). The summary's "Wolverine/Azure Service Bus switched off" and the "Dialogporten sync" row of the
> differences table are history; see the component README, "Dialogporten outbox". The compose file finding 4 points at
> was removed on 2026-10-20 (`84514a3`); the stack runs through studioctl's `localtest-v2` runtime.

## Summary

The localtest Storage is production Altinn Storage with four pieces swapped out: blobs in files instead of Azure
Blob, an immediate "clean" verdict instead of the file-scan queue, the shared test certificate instead of Key Vault,
and Wolverine/Azure Service Bus switched off. Business logic, SQL and migrations are production code. The vendored
tree equals upstream `9e72a9f1` except three `LOCALTEST` sites, and the Dockerfile mirrors upstream's (chiseled
image, non-root). All review findings are fixed; the remaining differences are deliberate and listed below.

## Vendored code

- Pin: `Altinn/altinn-storage` `9e72a9f1` (main, 2026-10-19), re-pinned from `1c594731` in this review. Upstream
  `main` has not moved since (checked 2026-10-20).
- Fidelity: `git archive 9e72a9f1` of the copied paths, `diff -rq` against `vendor/`: only `UPSTREAM.md` and the two
  files with directives differ. The baseline commit `f777e73` was byte-identical to upstream `1c594731`.
- Directive sites (all production-first or pure additions):
  1. `Program.cs`, `SetConfigurationProviders`: `#if !LOCALTEST` around the Key Vault and Application Insights
     connection, `#else` the localtest configuration defaults.
  2. `Program.cs`, after `ConfigureWolverine`: `#if LOCALTEST` the DI replacements.
  3. `Controllers/InstancesController.cs`, `BuildRequestLink`: the paging `next` link uses the request's own host
     instead of `https://platform.{Hostname}`.

## Dockerfile

Mirrors upstream's line for line since `9feefb2`: pinned `sdk:10.0-resolute` and `aspnet:10.0-resolute-chiseled-extra`
digests, the DbTools build that regenerates the latest migration's functions-and-procedures script during publish
(checked: identical to the committed file apart from the header), `Migration/` copied from the build stage,
`USER $APP_UID` (1654). Deviations are marked `localtest:`: the component layout under `/src`, the overrides, the blob
folder and the environment. No `HEALTHCHECK`: podman builds OCI images, which drop it.

## Fakes and what they replace

| Production | Localtest (`fakes/`) |
| --- | --- |
| Azure Blob (`IBlobRepository`) | `FileBlobRepository`: `{root}/{org}[-NN]/{blobStoragePath}` on the `storage-blobs` volume |
| File-scan queue (`IFileScanQueueClient`) and the scanner | `LocaltestFileScanQueueClient`: production `UpdateFileScanStatus(…, Clean)` at once |
| Key Vault keys for incoming platform tokens (`IPublicSigningKeyProvider`) | The shared test certificate for every issuer |
| Key Vault configuration and Application Insights | Localtest configuration defaults (`LocaltestStorageExtensions.AddLocaltestConfiguration`) |

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| File scan | `Pending` until the scanner reports | `Clean` at once (the upload response still says `Pending`); `Pending` and `Infected` paths cannot be tested |
| Signing keys | Per issuer | One certificate for every issuer |
| Dialogporten sync, A2 bridge, Correspondence | On | Off (`WolverineSettings:EnableSending=false`, `GeneralSettings:DisableA2Endpoints=true`) |
| Caches | App metadata 300 s, texts and titles 3600 s | 30 s |
| `ASPNETCORE_ENVIRONMENT` | Production | `Development`: developer exception page, Debug logging with SQL parameters (`LogParameters=true` from `appsettings.Development.json`), Swagger, no graceful shutdown |
| Internet | — | Studio and metrics endpoints read the org list from `altinncdn.no` |
| Paging `next` links | `https://platform.{Hostname}` | The request's own host |

## Findings

All fixed unless marked open.

1. Orphaned XML comments in `LocaltestStorageOptions.cs` (CS1587 warnings). Fixed.
2. A stale duplicate summary in `fakes/Auth/LocaltestAuth.cs` claiming token verification is off. Fixed.
3. Unused `using`s in the fakes. Fixed.
4. Stale README (directive count, a compose snippet with a dropped `./apps` fallback, "not verified" text). Fixed;
   the compose section now points at `docker-compose.yml`.
5. Two upstream commits behind, one of them the system user authentication-level change. Re-pinned to `9e72a9f1`.
6. `System.Security.Cryptography.Pkcs` 8.0.1 vulnerability warnings come from upstream's dependencies. Open, upstream's.
7. The old Dockerfile did not mirror upstream (Alpine instead of chiseled, root, no DbTools step). Fixed; a volume
   created by the root image needs a one-time `chown -R 1654:1654` (in the README).
8. The README claimed the HEALTHCHECK was dropped because the image has no shell; the real reason is the OCI image
   format. Fixed.

## Verification

- The image from `9e72a9f1` started side by side with the stack (own Postgres and blob volume): migrations to v0.38,
  `/health` in 3 s, blobs written as uid 1654; the root smoke test 23/23 against it.
- In the stack since 2026-10-19: root smoke 23/23 after every recreate; the full Cypress suite on 2026-10-19/20
  (all 13 app folders, across two runs) passed with the chiseled image, and no permission errors in the logs.
- Not verified: nb-NO formatting differences between the old Alpine-with-ICU image and the chiseled `-extra` one.
