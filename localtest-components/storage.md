# Storage strand: replacing localtest's Storage copy with real altinn-storage

Paths below are relative to `/home/agent/code` unless they are absolute. Localtest root:
[`altinn-studio/src/Runtime/localtest`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest) (abbreviated [`LT/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest)). Upstream: `altinn-storage` at
`origin/main` = `1c594731` (2026-10-16, "chore(deps): update nuget non-major dependencies (#1144)"),
abbreviated [`ST/`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage) (= [`altinn-storage/src/Storage`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage)). Monorepo HEAD inspected: `447e53eb97`.

## Executive summary

- **Drift is large.** 65 localtest files map by name to upstream files. Only 3 are identical. Over the
  matched files, a whitespace-insensitive diff shows +3 037 / −5 576 lines (localtest 11 524 lines,
  upstream 14 066). Another 15 localtest files have no upstream counterpart: the file-backed repositories
  and helpers, about 3 800 lines. About 100 upstream `.cs` files have no localtest counterpart: Postgres
  repositories, A2/SBL, migration, metrics, outbox, telemetry and other parts.
  The repository contracts have also diverged. Upstream uses `InstanceInternal` with versions in the
  database. Localtest uses `(Instance, long)` tuples and a side-car `InstanceVersionMetadataStore`.
  Localtest's `InstanceMutationsController` (1 610 lines) and `InstanceMutationRepository` (1 152 lines)
  were reimplemented for localtest. They are not copies of upstream's current transactional implementation.
- **Sync today is hand-porting per PR.** There is no tooling. 23 commits since localtest moved into the
  monorepo (2025-09-23) touch these folders. Early ones are subtree merges from the old `app-localtest`
  repo. Since 2026-05 the commits are individual `fix(localtest)` / `feat` PRs that re-apply upstream
  Storage behaviour by hand. One example is
  [altinn-studio#21010](https://github.com/Altinn/altinn-studio/pull/21010), which mirrors
  [altinn-storage#1136](https://github.com/Altinn/altinn-storage/pull/1136). Upstream
  [altinn-storage#1125](https://github.com/Altinn/altinn-storage/pull/1125) (retained blob versions,
  merged 2026-10-16) is not yet in localtest. The `Altinn.Platform.Storage.Interface` versions also
  differ: localtest uses 5.0.0-rc.1, upstream uses rc.3 and app-lib uses rc.2.
- **(b) Running the real image is feasible, and I recommend it, but it is not drop-in.** Postgres,
  Azurite blob and Azurite queue are required. Wolverine/Service Bus, Key Vault and App Insights can be
  avoided by running with `ASPNETCORE_ENVIRONMENT=Development`. Hard issues:
  1. `PublicSigningKeyProvider` needs a non-empty `kvSetting:SecretUri`. If [`Program.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs) sees that key,
     it connects to Azure Key Vault. Workaround: pass the key as a command-line argument (details in §3.3).
  2. Application metadata and texts live in Postgres and must be pushed by studioctl with a
     `studio.designer` token. Localtest currently reads them live from the app.
  3. File scan stays `Pending`, which blocks submit in app-lib unless a fake scanner is added.
  4. The localtest PDP reads instances in-process through `IInstanceRepository`. It would have to call
     the Storage container over HTTP.
  5. No public Storage image was found, so one must be built from source (not confirmed).
  6. The Storage Explorer page and the file layout of `/AltinnPlatformLocal` are lost.
- **(a) Subtree-merging and compiling the real controllers into localtest with file-backed repositories
  is possible, but it moves the drift rather than removing it.** About 12 upstream repository interfaces
  would need file-backed implementations that reproduce about 2 300 lines of PL/pgSQL semantics: the
  mutation apply, optimistic concurrency, queries and blob versions. Every upstream repository change
  would break the localtest build. Upstream controllers that are not wanted (A2, migration, SBL,
  metrics) must be filtered out. `DataService` catches `PostgresException`. This is cheaper per sync
  than today's approach but costly up front, and it still does not test the real SQL.

## 1. Provenance and drift

### 1.1 File mapping (localtest → upstream) and diff size

Method: map by basename, then `diff -w upstream localtest`. "+" means lines only in localtest, "−" means
lines only upstream. Some of the noise comes from block-scoped vs file-scoped namespaces and from
formatting, so treat the numbers as an upper bound on semantic drift.

| Localtest file ([`LT/src/…`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src)) | Upstream ([`ST/…`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage)) | LT lines | ST lines | + | − |
|---|---|---:|---:|---:|---:|
| [Configuration/Storage/GeneralSettings.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Configuration/Storage/GeneralSettings.cs) | [Configuration/GeneralSettings.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Configuration/GeneralSettings.cs) | 63 | 118 | 6 | 61 |
| [Controllers/Storage/ApplicationsController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/ApplicationsController.cs) | [Controllers/ApplicationsController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/ApplicationsController.cs) | 310 | 297 | 29 | 16 |
| [Controllers/Storage/DataController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/DataController.cs) | [Controllers/DataController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/DataController.cs) | 1428 | 1497 | 453 | 522 |
| [Controllers/Storage/DataLockController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/DataLockController.cs) | [Controllers/DataLockController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/DataLockController.cs) | 216 | 220 | 26 | 30 |
| [Controllers/Storage/InstanceEventsController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/InstanceEventsController.cs) | [Controllers/InstanceEventsController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstanceEventsController.cs) | 188 | 181 | 12 | 5 |
| [Controllers/Storage/InstanceLockController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/InstanceLockController.cs) | [Controllers/InstanceLockController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstanceLockController.cs) | 205 | 209 | 6 | 10 |
| [Controllers/Storage/InstanceMutationsController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/InstanceMutationsController.cs) | [Controllers/InstanceMutationsController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstanceMutationsController.cs) | 1610 | 2197 | 758 | 1345 |
| [Controllers/Storage/InstancesController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/InstancesController.cs) | [Controllers/InstancesController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstancesController.cs) | 1254 | 1504 | 346 | 596 |
| [Controllers/Storage/ProcessController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/ProcessController.cs) | [Controllers/ProcessController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/ProcessController.cs) | 476 | 574 | 109 | 207 |
| [Controllers/Storage/SignController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/SignController.cs) | [Controllers/SignController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/SignController.cs) | 116 | 120 | 20 | 24 |
| [Controllers/Storage/TextsController.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/TextsController.cs) | [Controllers/TextsController.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/TextsController.cs) | 163 | 162 | 1 | 0 |
| [Extensions/Storage/ContentDispositionHeaderValueExtensions.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Extensions/Storage/ContentDispositionHeaderValueExtensions.cs) | [Extensions/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Extensions/ContentDispositionHeaderValueExtensions.cs) | 28 | 27 | 3 | 2 |
| [Extensions/Storage/DataTypeExtensions.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Extensions/Storage/DataTypeExtensions.cs) | [Extensions/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Extensions/DataTypeExtensions.cs) | 51 | 56 | 13 | 18 |
| [Extensions/Storage/StringExtensions.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Extensions/Storage/StringExtensions.cs) | [Extensions/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Extensions/StringExtensions.cs) | 41 | 43 | 4 | 6 |
| [Helpers/Storage/ClaimsPrincipalExtensions.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/ClaimsPrincipalExtensions.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/ClaimsPrincipalExtensions.cs) | 156 | 182 | 3 | 28 |
| [Helpers/Storage/DataElementHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/DataElementHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/DataElementHelper.cs) | 151 | 166 | 56 | 71 |
| [Helpers/Storage/DateTimeHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/DateTimeHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/DateTimeHelper.cs) | 74 | 84 | 13 | 23 |
| [Helpers/Storage/DisableFormValueModelBindingAttribute.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/DisableFormValueModelBindingAttribute.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/DisableFormValueModelBindingAttribute.cs) | 35 | 32 | 8 | 5 |
| [Helpers/Storage/InstanceHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/InstanceHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/InstanceHelper.cs) | 221 | 365 | 51 | 195 |
| [Helpers/Storage/LanguageHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/LanguageHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/LanguageHelper.cs) | 46 | 45 | 3 | 2 |
| [Helpers/Storage/MessageBoxInstance.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/MessageBoxInstance.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/MessageBoxInstance.cs) | 152 | 150 | 6 | 4 |
| [Helpers/Storage/MultipartRequestHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/MultipartRequestHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/MultipartRequestHelper.cs) | 80 | 81 | 11 | 12 |
| [Helpers/Storage/ProcessHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/ProcessHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/ProcessHelper.cs) | 113 | 125 | 22 | 34 |
| [Helpers/Storage/ProcessStatusHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/ProcessStatusHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/ProcessStatusHelper.cs) | 37 | 53 | 2 | 18 |
| [Helpers/Storage/QueryResponse.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/QueryResponse.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/QueryResponse.cs) | 44 | 36 | 10 | 2 |
| [Helpers/Storage/SblInstanceEvent.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/SblInstanceEvent.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/SblInstanceEvent.cs) | 45 | 56 | 3 | 14 |
| [Helpers/Storage/StorageHeaders.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/StorageHeaders.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/StorageHeaders.cs) | 39 | 32 | 7 | 0 |
| [Helpers/Storage/StringHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/StringHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/StringHelper.cs) | 29 | 46 | 1 | 18 |
| [Helpers/Storage/VersionPreconditionHelper.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/VersionPreconditionHelper.cs) | [Helpers/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Helpers/VersionPreconditionHelper.cs) | 189 | 164 | 46 | 21 |
| [Models/Storage/BlobVersionId.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Storage/BlobVersionId.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/BlobVersionId.cs) | 84 | 84 | 0 | 0 |
| [Models/Storage/FileScanRequest.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Storage/FileScanRequest.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/FileScanRequest.cs) | 46 | 55 | 4 | 13 |
| [Models/Storage/PartyType.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Storage/PartyType.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/PartyType.cs) | 19 | 19 | 1 | 1 |
| [Models/Storage/RepositoryException.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Storage/RepositoryException.cs) | [Repository/RepositoryException.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/RepositoryException.cs) | 144 | 173 | 6 | 35 |
| [Services/Storage/Implementation/ApplicationService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ApplicationService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/ApplicationService.cs) | 55 | 82 | 13 | 39 |
| [Services/Storage/Implementation/AuthorizationService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/AuthorizationService.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/AuthorizationService.cs) | 423 | 770 | 112 | 458 |
| [Services/Storage/Implementation/BlobRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/BlobRepository.cs) | [Repository/BlobRepository.cs](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/BlobRepository.cs) (Azure) | 148 | 522 | 66 | 440 |
| [Services/Storage/Implementation/ClaimsPrincipalProvider.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ClaimsPrincipalProvider.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/ClaimsPrincipalProvider.cs) | 24 | 28 | 3 | 7 |
| [Services/Storage/Implementation/DataService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/DataService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/DataService.cs) | 346 | 523 | 174 | 351 |
| [Services/Storage/Implementation/InstanceEventService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceEventService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/InstanceEventService.cs) | 86 | 151 | 18 | 83 |
| [Services/Storage/Implementation/InstanceQueryParameters.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceQueryParameters.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/InstanceQueryParameters.cs) | 457 | 561 | 3 | 107 |
| [Services/Storage/Implementation/ProcessAuthorizer.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ProcessAuthorizer.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/ProcessAuthorizer.cs) | 202 | 163 | 56 | 17 |
| [Services/Storage/Implementation/ProcessDataCleanupService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ProcessDataCleanupService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/ProcessDataCleanupService.cs) | 65 | 52 | 27 | 14 |
| [Services/Storage/Implementation/RegisterService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/RegisterService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/RegisterService.cs) | 36 | 135 | 8 | 107 |
| [Services/Storage/Implementation/SignDocumentCreateResult.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/SignDocumentCreateResult.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/SignDocumentCreateResult.cs) | 25 | 47 | 16 | 38 |
| [Services/Storage/Implementation/SigningService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/SigningService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/SigningService.cs) | 448 | 384 | 170 | 106 |
| [Services/Storage/Implementation/StorageAccessHandler.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/StorageAccessHandler.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/StorageAccessHandler.cs) | 196 | 217 | 55 | 76 |
| [Services/Storage/InstanceLockResult.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/InstanceLockResult.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/InstanceLockResult.cs) | 45 | 43 | 3 | 1 |
| [Services/Storage/Interface/DataElementUpdateContext.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/DataElementUpdateContext.cs) | [Models/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/DataElementUpdateContext.cs) | 29 | 28 | 10 | 9 |
| [Services/Storage/Interface/IApplicationRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IApplicationRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IApplicationRepository.cs) | 69 | 72 | 3 | 6 |
| [Services/Storage/Interface/IApplicationService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IApplicationService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/IApplicationService.cs) | 37 | 37 | 0 | 0 |
| [Services/Storage/Interface/IAuthorization.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IAuthorization.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/IAuthorization.cs) | 78 | 71 | 27 | 20 |
| [Services/Storage/Interface/IBlobRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IBlobRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IBlobRepository.cs) | 62 | 91 | 8 | 37 |
| [Services/Storage/Interface/IClaimsPrincipalProvider.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IClaimsPrincipalProvider.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/IClaimsPrincipalProvider.cs) | 19 | 19 | 1 | 1 |
| [Services/Storage/Interface/IDataRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IDataRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IDataRepository.cs) | 173 | 230 | 54 | 111 |
| [Services/Storage/Interface/IDataService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IDataService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/IDataService.cs) | 96 | 122 | 25 | 51 |
| [Services/Storage/Interface/IInstanceEventRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceEventRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IInstanceEventRepository.cs) | 52 | 56 | 7 | 11 |
| [Services/Storage/Interface/IInstanceEventService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceEventService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/IInstanceEventService.cs) | 53 | 69 | 8 | 24 |
| [Services/Storage/Interface/IInstanceLockRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceLockRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IInstanceLockRepository.cs) | 47 | 49 | 2 | 4 |
| [Services/Storage/Interface/IInstanceMutationRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceMutationRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IInstanceMutationRepository.cs) | 73 | 46 | 44 | 17 |
| [Services/Storage/Interface/IInstanceRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IInstanceRepository.cs) | 128 | 154 | 50 | 76 |
| [Services/Storage/Interface/IProcessAuthorizer.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IProcessAuthorizer.cs) | [Authorization/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Authorization/IProcessAuthorizer.cs) | 54 | 47 | 13 | 6 |
| [Services/Storage/Interface/IProcessDataCleanupService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IProcessDataCleanupService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/IProcessDataCleanupService.cs) | 30 | 27 | 17 | 14 |
| [Services/Storage/Interface/IRegisterService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IRegisterService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/IRegisterService.cs) | 27 | 27 | 2 | 2 |
| [Services/Storage/Interface/ISigningService.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/ISigningService.cs) | [Services/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/ISigningService.cs) | 31 | 33 | 3 | 5 |
| [Services/Storage/Interface/ITextRepository.cs](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/ITextRepository.cs) | [Repository/…](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/ITextRepository.cs) | 57 | 57 | 0 | 0 |

**Localtest-only files** (no upstream counterpart):
- [`Services/Storage/Implementation/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation): [`ApplicationRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ApplicationRepository.cs) (91), [`TextRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/TextRepository.cs) (86),
  [`InstanceRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceRepository.cs) (598), [`DataRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/DataRepository.cs) (1089), [`InstanceMutationRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceMutationRepository.cs) (1152),
  [`InstanceEventRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceEventRepository.cs) (104), [`InstanceLockRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceLockRepository.cs) (143), [`InstanceAndEventsRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceAndEventsRepository.cs) (54),
  [`InstanceVersionMetadataStore.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceVersionMetadataStore.cs) (183), [`AsyncLock.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/AsyncLock.cs) (118, `PartitionedAsyncLock`) and [`InstanceQueryResponse.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/InstanceQueryResponse.cs) (51).
- [`Services/Storage/Interface/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface): [`IInstanceAndEventsRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/IInstanceAndEventsRepository.cs) and [`InstanceVersionResult.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Interface/InstanceVersionResult.cs).
- [`Helpers/Storage/OrgDataContext.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Helpers/Storage/OrgDataContext.cs) and [`Controllers/StorageExplorerController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/StorageExplorerController.cs).

**Upstream-only areas** (about 100 files): [`Repository/Pg*.cs`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository) (about 3 700 lines), [`Migration/`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Migration) (60
procedure files, about 2 300 lines of SQL, including [`applyinstancemutation.sql`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Migration/FunctionsAndProcedures/applyinstancemutation.sql) at 406 lines), and the
controllers `CleanupController`, `ContentOnDemandController`, `MessageboxInstancesController`,
`MetricsController`, `MigrationController`, `SblBridgeController`, `StudioInstancesController` and
`ErrorController`. Also upstream-only: [`Clients/*`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Clients) (SBL bridge, on-demand, PDF and file-scan queue),
`OutboxService`/Wolverine, [`Telemetry/`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Telemetry), `OrganisationService` (Altinn CDN) and [`Models/InstanceInternal.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Models/InstanceInternal.cs).

### 1.2 Endpoint and method-level drift (examples)

- Routes that exist upstream but not in localtest:
  - `GET data/{dataGuid:guid}/versions/{blobVersionId}` (retained blob versions,
    [altinn-storage#1125](https://github.com/Altinn/altinn-storage/pull/1125)).
  - `GET dataelementexists/{dataGuid}` (`PlatformAccess`).
  - A differently shaped `GET {instanceGuid:guid}` in `InstancesController`.
  - All A2, messagebox, migration and Studio endpoints.
- `InstancesController`: upstream adds `AuthorizeCaller`, `AuthorizeCrossOrgAccess`,
  `ResolvePartyFromIdentifier`, `ApplyHardDeleteFilter`, `NormalizeContinuationToken` and other methods.
  Localtest adds `WriteVersionResponseHeaders`.
- `InstanceMutationsController`: upstream has a plan/stage/commit pipeline with staged blobs, replay and
  `RunCommittedMutationSideEffects` (about 30 methods that localtest lacks). Localtest has its own simpler
  multipart pipeline (`MutationContentPart`, `ProcessStatusConflict`, `DeleteAllocatedBlobVersion`).
- `AuthorizationService`: upstream has `AuthorizeEnrichedInstanceAction`, decision caching and
  `GetContextScope`. That is 458 removed lines.
- `DataService`: upstream stages blobs and catches `PostgresException` (`IndicatesDefiniteRollback`).
  Localtest has `DeleteImmediately` and SHA-256 hashing.
- Repository contracts: upstream `IInstanceRepository` returns `InstanceInternal`, `InstanceQueryResult`
  and blob-version references. Localtest returns `(Instance, long)`, `InstanceQueryResponse` and
  `InstanceVersionResult`. Upstream `IInstanceMutationRepository` has `DeleteIdempotencyRecordsCreatedBefore`.

### 1.3 Kinds of local modification

1. **File-backed repositories instead of Postgres and Azure Blob.** JSON files under
   `LocalPlatformSettings.LocalTestingStorageBasePath` are guarded by in-process `PartitionedAsyncLock`.
   Instance queries scan every instance file in memory. Optimistic concurrency versions live in a side-car
   file (`InstanceVersionMetadataStore`, `.instanceversions/versions.json`). Many A2, app-title and
   cleanup methods `throw new NotImplementedException()`.
2. **Application metadata and texts come from the running app**, not from the database (§4).
3. **`#if LOCALTEST`** ([`LT/src/LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj) defines `LOCALTEST`). There are only 2 blocks in
   Storage code, both in [`DataController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/DataController.cs) (lines 539 and 884): `FileScanResult.Clean` instead of
   `Pending`. Most divergence is unmarked inline edits.
4. **Removed infrastructure and auth surfaces:** no `PlatformAccess` (access-token) policy and no
   `AccessTokenHandler`, no Key Vault, no Wolverine/outbox, no SBL bridge, no telemetry.
   `GeneralSettings` is cut to about half its size.
5. **Small behaviour differences.** For example, `ApplicationsController.Update` merges fields into
   `existingApplication`, while upstream overwrites. `CancellationToken` plumbing and spelling fixes from
   the monorepo spell check
   ([altinn-studio#19835](https://github.com/Altinn/altinn-studio/pull/19835)) also add diff noise.

### 1.4 How changes are synced today

`git -C altinn-studio log -- src/Runtime/localtest/src/{Controllers,Services,Helpers}/Storage` shows 23
commits:
- `e7f38bd6a7` (2025-09-23) `Add 'src/Runtime/localtest/'`: the subtree import of the old `app-localtest` repo.
- 2025-09 to 2026-06: `Merge commit …into chore/subtree-sync-*`. These are syncs from `app-localtest`,
  not from altinn-storage.
- 2026-05 onward: hand ports in the PRs that need them. Examples:
  - `4b44bcf7f2` "Port ProcessDataCleanupService from Storage to Localtest (#18927)"
  - `3e51b27bdd` "use aggregate endpoint for instance mutations (#19919)" (+9 316 lines across 50 files)
  - `ce7e727a78` "match Storage data-values concurrency behavior (#20392)"
  - `e5097584a0`, `d5f8e178e3` and `1c81bacb8e` (the last mirrors [altinn-storage#1136](https://github.com/Altinn/altinn-storage/issues/1136))

No commit message references an upstream SHA. Parity is maintained by people, per feature.

## 2. Localtest-only persistence and what depends on it being files

- The root is `LocalPlatformSettings.LocalTestingStorageBasePath`. In studioctl it is
  `"/AltinnPlatformLocal/"`, bind-mounted from `<DataDir>/AltinnPlatformLocal`
  ([`altinn-studio/src/cli/internal/cmd/env/localtest/components/core.go:71,85`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/components/core.go#L71) and [`resources.go:19,66`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/components/resources.go#L19)).
- Layout ([`LT/src/Configuration/LocalPlatformSettings.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Configuration/LocalPlatformSettings.cs)):
  - `documentdb/instances/{partyId}_{guid}.json`
  - `documentdb/data/{partyId}_{guid}/{dataId}.json`, with `.blobversions/` inside each instance's data folder
  - `documentdb/instanceevents/…`, `documentdb/applications/…` and `instancelocks/`
  - `blobs/{org}/{app}/{instanceGuid}/data/{dataGuid}`
  - The same root also holds non-Storage data: `notifications/`, `authorization/` audit, `events`,
    `tenorUsers` and `instanceDelegations`.
- Dependents:
  - **Storage Explorer.** [`LT/src/Controllers/StorageExplorerController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/StorageExplorerController.cs) and
    [`Views/StorageExplorer/Index.cshtml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Views/StorageExplorer/Index.cshtml) show an iframe of `/LocalPlatformStorage/`. That path is
    `UseStaticFiles` plus `UseDirectoryBrowser` over the storage root ([`LT/src/Startup.cs:250-266`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L250-L266)).
    Developers use it to download XML for prefill. It works only because data is plain files.
  - **`studioctl env reset`.** `Env.deletePersistedData` ([`altinn-studio/src/cli/internal/cmd/env/localtest/env.go:685`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/cmd/env/localtest/env.go#L685))
    deletes the whole `AltinnPlatformLocal` directory and the workflow-engine DB volume. With real
    Storage, it would also have to remove the Storage Postgres and Azurite volumes. That is a small change
    that follows the existing workflow-engine pattern.
  - **`LocaltestStorageProbe`** ([`altinn-studio/src/cli/studioctl-server/Discovery/LocaltestStorageProbe.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/studioctl-server/Discovery/LocaltestStorageProbe.cs))
    does **not** read files. It sends `GET /storage/api/v1/applications/{org}/{app}` to localtest and
    returns `Ready` when the returned `id` matches. `AppRegistry.CompleteReadyStarts` ([`AppRegistry.cs:333`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/studioctl-server/Discovery/AppRegistry.cs#L333))
    gates "app started" on this. What it depends on is that Storage's applications endpoint reflects
    the live app, which is the `LocalAppHttp` behaviour.
  - **In-process consumers of the file repositories in other localtest modules:**
    - [`Controllers/Authorization/DecisionController.cs:368`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authorization/DecisionController.cs#L368) and
      [`Services/Authorization/Implementation/PolicyInformationRepository.cs:38,47`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/PolicyInformationRepository.cs#L38) call
      `IInstanceRepository.GetOne` to build XACML context (task, owner).
    - [`Controllers/HomeController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/HomeController.cs) uses `IApplicationRepository`.
    - `RolesController` and `AuthorizedPartiesController` use only the `Altinn.Platform.Storage.Helpers`
      namespace.

## 3. Running the real Storage as a container

### 3.1 Build and image

- [`altinn-storage/Dockerfile`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/Dockerfile) has two stages. It builds `DbTools`, publishes
  [`Altinn.Platform.Storage.csproj`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Altinn.Platform.Storage.csproj) and runs on `aspnet:10.0-resolute-chiseled-extra` as a non-root user,
  `EXPOSE 5010`. `ENTRYPOINT ["dotnet","Altinn.Platform.Storage.dll"]`. It copies `Migration/` next to the app.
- **No public image was found.** The GitHub workflows only publish the `Storage.Interface` NuGet package
  ([`publish-release.yml`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/.github/workflows/publish-release.yml)). Anonymous GHCR probes for `altinn/altinn-storage` were denied. The
  deployment image is presumably built in Azure DevOps to a private ACR (not confirmed). In practice,
  studioctl would have to build from source, the way it does today for pdf3 and workflow-engine in dev
  mode. That needs the source available, either via subtree/vendoring or a pinned clone, or someone has
  to start publishing an image.

### 3.2 Required infrastructure

| Dependency | Needed? | Config | Notes |
|---|---|---|---|
| PostgreSQL | **Required** | `PostgreSqlSettings:{AdminConnectionString,ConnectionString,StorageDbAdminPwd,StorageDbPwd,WorkspacePath}` | Upstream compose and CI use `postgres:16`; studioctl's workflow-engine DB is `postgres:18.3`. Sharing one 18.x instance with a separate DB is likely OK (not verified). DB `storagedb`, schema `storage`, roles `platform_storage_admin` and `platform_storage` must be created first ([`infra/postgres_init.sql`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/infra/postgres_init.sql), [`dbsetup.sh`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/dbsetup.sh)). Yuniql runs all [`Migration/v*`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Migration) scripts at startup with the admin connection (`IsAutoCreateDatabase=false`). |
| Azure Blob (Azurite) | **Required** | `AzureStorageConfiguration:{AccountName,AccountKey,BlobEndPoint,OrgStorageAccount,OrgStorageContainer}` | When `AccountName == "devstoreaccount1"`, `BlobRepository.CreateContainerClient` uses `StorageSharedKeyCredential(OrgStorageAccount, AccountKey)` against `BlobEndPoint`. So `OrgStorageAccount` must be set to `devstoreaccount1`. The default `{0}altinndevstrg01` would fail auth (inferred). Storage never calls `CreateIfNotExists` on blob containers, so `string.Format(OrgStorageContainer, org)` must be created up front. Setting a constant name without `{0}` gives one container for all orgs. |
| Azure Queue (Azurite) | Required if any data type has `EnableFileScan` | `QueueStorageSettings:{ConnectionString,FileScanQueueName}` | `FileScanQueueClient` creates the queue itself. Nothing consumes it locally (see §4). |
| Wolverine / Azure Service Bus | Avoidable | `WolverineSettings:{EnableSending,ServiceBusConnectionString}` | In `Development`, Wolverine uses local in-process queues. With `EnableSending=false` (the default), the outbox (Dialogporten sync) is a no-op. Outside Development it **requires** ASB. |
| Key Vault | Avoidable at startup; see 3.3 | `kvSetting:SecretUri` | `Program.ConnectToKeyVaultAndSetApplicationInsights` adds Azure Key Vault config when `SecretUri` is non-empty. |
| App Insights / Azure Monitor | Optional | from Key Vault | Skipped when there is no connection string. OTel console exporter only. |
| Altinn CDN orgs list | Optional | `GeneralSettings:OrganisationsUrl` | Used only by `StudioInstancesController` and metrics. |
| Environment | `ASPNETCORE_ENVIRONMENT=Development` required | | Also turns off `RequireHttpsMetadata` for JWT metadata, which the HTTP-only localtest needs. Also turns off graceful-shutdown delays and turns on Swagger. |

### 3.3 Auth requirements and the Key Vault blocker

- **User and org tokens:** `AddJwtCookie` with
  `JwtCookieName = GeneralSettings:RuntimeCookieName` (`AltinnStudioRuntime`) and
  `MetadataAddress = GeneralSettings:OpenIdWellKnownEndpoint`. It accepts the cookie or a bearer header
  (JwtCookie behaviour). `ValidateIssuer=false`, `ValidateAudience=false`, signature and lifetime are
  validated, and `ClockSkew=0`. Localtest already serves
  `authentication/api/v1/openid/.well-known/openid-configuration` and `/jwks`
  ([`LT/src/Controllers/Authentication/OpenIdController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authentication/OpenIdController.cs)). Point the endpoint at
  `http://<localtest>/authentication/api/v1/openid/`. The tokens localtest issues should then validate
  (not run end-to-end).
- **Scopes and claims used:**
  - `GeneralSettings:InstanceReadScope` (`altinn:serviceowner/instances.read`)
  - `GeneralSettings:InstanceSyncAdapterScope` (`altinn:storage/instances.syncadapter`), which skips the PDP
  - `altinn:appdeploy`
  - `urn:altinn:app=studio.designer`, required for POST/PUT/DELETE on applications and texts
  - `altinn:correspondence.sblbridge`
- **Platform access token:** the `PlatformAccess` policy goes through `AccessTokenHandler` (header
  `PlatformAccessToken`, `AccessTokenSettings:AccessTokenHeaderId`). It is used only on
  `PUT dataelements/{id}/filescanstatus` and `GET dataelementexists/{id}`. Setting
  `AccessTokenSettings:DisableAccessTokenVerification=true` makes the handler succeed whether or not a
  token is present. Validation errors are swallowed when it is disabled.
- **Blocker (inferred from decompiled `Altinn.Common.AccessToken` 5.1.15, not run):**
  1. `PublicSigningKeyProvider`'s constructor does `new SecretClient(new Uri(kvSetting.SecretUri), …)`.
     With an empty `SecretUri` this throws `UriFormatException`.
  2. `AccessTokenHandler` (singleton `IAuthorizationHandler`) depends on it. Authorization resolves all
     handlers, so every `[Authorize]` request would likely fail.
  3. Setting `SecretUri` through env or JSON makes [`Program.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs) call `AddAzureKeyVault` with
     `DefaultAzureCredential`, so startup would fail outside Azure.
  4. Workaround: pass `--kvSetting:SecretUri=http://localhost/` as a **command-line argument**. Program
     adds command-line config after the Key Vault check, so the check sees an empty value while options
     binding sees the dummy one. This is fragile. An upstream change ("no Key Vault when X") would be cleaner.
- **Outgoing tokens:** `RegisterService` calls `AccessTokenGenerator.GenerateAccessToken`. Without a
  signing cert (`AccessTokenSettings:AccessTokenSigningKeysFolder` / `…CertificateFullPath`), it logs a
  warning and returns `null`, so localtest's Register must not require the header (it does not today).

### 3.4 Outbound dependencies on other platform services

| Dependency | Config key | Used for | Localtest equivalent |
|---|---|---|---|
| Authorization PDP (PEP `PDPAppSI`) | `PlatformSettings:ApiAuthorizationEndpoint` (+ `PlatformSettings:SubscriptionKey`, `PepSettings:PdpDecisionCachingTimeout`) | All instance/data policies (`StorageAccessHandler`, `AuthorizationService`, `ProcessAuthorizer`) | `…/authorization/api/v1/` in localtest |
| Register | `RegisterServiceSettings:ApiRegisterEndpoint` | `GET parties/{id}`, `POST parties/lookup` (party resolution on instance create/query) | [`LT/src/Controllers/Register/PartiesController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register/PartiesController.cs) has both |
| Authentication OpenID metadata | `GeneralSettings:OpenIdWellKnownEndpoint` | JWT signing keys | `OpenIdController` |
| SBL bridge (A2) | `GeneralSettings:BridgeApiAuthorizationEndpoint`, `GeneralSettings:BridgeApiCorrespondenceEndpoint` | `PartiesWithInstancesClient.SetHasAltinn3Instances`, called on **every instance create** ([`InstancesController.cs:480`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstancesController.cs#L480)); correspondence sync | None. Set `GeneralSettings:DisableA2Endpoints=true`, which makes the clients no-ops |
| On-demand (Storage itself) | `GeneralSettings:OndemandEndpoint` | A2 formatting in `DataController` on-demand content | Not needed |
| PDF generator | `GeneralSettings:PdfGeneratorEndpoint` | `ContentOnDemandController` (A2 print) | Not needed (could point to pdf3) |
| Altinn CDN | `GeneralSettings:OrganisationsUrl` | Org list for Studio and metrics | Not needed (outbound internet) |
| File scan | Azure queue `QueueStorageSettings` | Enqueue scan requests | None |
| Profile, Access Management, Events | — | **Not called by Storage** (grep of `ST/`) | — |
| App endpoints | — | **Storage never calls the app.** Application metadata and texts come from its DB | Localtest calls the app (§4) |
| `GeneralSettings:Hostname` | — | Self links (`https://platform.{Hostname}` in [`InstancesController.cs:1465`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/InstancesController.cs#L1465)) | Set to `local.altinn.cloud` (links would be https; not checked whether apps rely on them) |

## 4. Localtest-specific behaviour that real Storage lacks

1. **Application metadata from the live app.** [`LT/src/Services/Storage/Implementation/ApplicationRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Storage/Implementation/ApplicationRepository.cs)
   `FindOne`/`FindAll`/`FindByOrg` go to `ILocalApp.GetApplicationMetadata`
   ([`LT/src/Services/LocalApp/Implementation/LocalAppHttp.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/LocalApp/Implementation/LocalAppHttp.cs)). That calls
   `{org}/{app}/api/v1/applicationmetadata?checkOrgApp=false` on the app through the **host bridge**,
   resolved from the bound topology. It uses a 5 s sliding cache and a 5 s timeout
   ([altinn-studio#19811](https://github.com/Altinn/altinn-studio/pull/19811)).
   - App lists come from discovered bound app routes.
   - `Update` writes a JSON file that reads never consult.
   - Real Storage reads `storage.applications` in Postgres (`PgApplicationRepository`) with a 300 s
     cache (`GeneralSettings:AppMetadataCacheLifeTimeInSeconds`). Metadata is written by Designer
     deploys (`POST /storage/api/v1/applications`, policy `studio.designer`).
   - **Consequence:** studioctl-server's `AppRegistry` would have to push and update metadata (and
     texts) on every app start or change, with a localtest-minted `studio.designer` token. The cache TTL
     would have to be lowered. `LocaltestStorageProbe` would then measure whether the push has happened.
     It would no longer measure whether the app is reachable.
2. **Texts from the app.** `TextRepository.Get(org, app, lang)` calls the app's `/api/v1/texts/{lang}`.
   Real Storage reads the `texts` table, which must be pushed the same way. Other `TextRepository`
   methods are `NotImplemented`.
3. **File scan is short-circuited.** `#if LOCALTEST` sets `FileScanResult.Clean`. Real Storage sets
   `Pending` and enqueues to Azure Queue. app-lib's [`DefaultDataElementValidator.cs:90`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Validation/Default/DefaultDataElementValidator.cs#L90) treats `Pending`
   as a validation error, so **submit is blocked** for data types with `EnableFileScan`. A fake scanner
   is needed: consume `file-scan-inbound` from Azurite and call `PUT …/filescanstatus` (allowed with
   `DisableAccessTokenVerification=true`).
4. **GeneratedFromTask cleanup** (`ProcessDataCleanupService`, `deleteGeneratedElements=false` query
   opt-out). This is **parity, not localtest-only**. Upstream [`ST/Controllers/ProcessController.cs:227,335`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Controllers/ProcessController.cs#L227)
   has the same parameter, and app-lib sends it ([`Altinn.App.Core/Extensions/HttpClientExtension.cs:136`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Extensions/HttpClientExtension.cs#L136)).
5. **The localtest PDP depends on in-process Storage.** `DecisionController` and
   `PolicyInformationRepository` call `IInstanceRepository.GetOne`. With an external Storage they need
   an HTTP client. It should use a syncadapter-scoped token (`altinn:storage/instances.syncadapter`, which
   skips the PDP in `StorageAccessHandler.IsValidSyncAdapterRequest`) to avoid the loop where Storage
   asks the PDP, which asks Storage.
6. **The Storage Explorer and raw-file browsing** of instance and data JSON (§2).
7. **Process and versioning semantics are reimplemented** (`InstanceVersionMetadataStore`,
   `InstanceMutationRepository`). These are the most likely source of real-vs-local behaviour gaps today,
   and the strongest argument for using the real implementation.
8. **Frontend version override** is not a Storage feature. It is a cookie
   (`FrontendVersionController.FRONTEND_URL_COOKIE_NAME`) used by `HomeController`/`LocalFrontendService`.
   It is unaffected.

## 5. Verdict

### (a) Subtree-merge altinn-storage and compile the real controllers into localtest with file-backed repositories

- **Mechanics.** Subtree-merge `altinn-storage` (the monorepo already uses subtree merges for localtest
  and backend-sync). Reference [`src/Storage`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049/src/Storage) source or the project from [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj). Delete the
  `Altinn.Platform.Storage.*` copies. Write file-backed implementations of upstream's repository
  interfaces:
  - `IInstanceRepository`, `IDataRepository`, `IInstanceEventRepository`, `IInstanceLockRepository`,
    `IInstanceMutationRepository`, `IBlobRepository`, `IApplicationRepository` and `ITextRepository`
  - stubs for `IA2Repository`, `IMetricsRepository`, `IOutboxRepository` and `IOrganisationRepository`
- **Hard parts and blockers:**
  - Upstream logic lives partly in about 2 300 lines of PL/pgSQL: `applyinstancemutation`, the read and
    query procedures, blob versions and idempotency. File repositories must reproduce those semantics.
    That is essentially today's 3 800-line localtest repository layer, rewritten against the new
    `InstanceInternal`-based contracts.
  - Upstream changes repository interfaces often (2 major mutation rewrites in Sept 2026). Each subtree
    pull would break the localtest build until the file repositories are updated. Drift becomes compile
    errors, which is better, but the work is still manual.
  - `MapControllers` would pick up every upstream controller (Migration, SBL bridge, Metrics, A2,
    Cleanup, Studio). They must be excluded with an `ApplicationPartManager` filter. Upstream DI is
    inline in [`Program.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs), which cannot be reused, so localtest must replicate the registrations and
    policies by hand.
  - `DataService` depends on Npgsql exception types. Wolverine types are confined to `Pg*` and
    `OutboxService`, which is acceptable.
  - Localtest-specific behaviours (§4.1–4.3) would need `#if LOCALTEST` edits in vendored upstream code,
    or upstream extension points.
- **Effort:** high up front (several weeks), medium ongoing. It also still does not exercise the real
  SQL, which is where upstream's concurrency semantics live.

### (b) Run the real Storage image with Postgres and Azurite as studioctl containers

- **Mechanics.** Add a studioctl component with:
  - a `storage` container (built from source unless a public image appears)
  - a Postgres DB, either shared with workflow-engine's or separate on `postgres:16`
  - an Azurite container (blob and queue)
  - an init step running [`postgres_init.sql`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/infra/postgres_init.sql) and creating the blob container

  Bind route `local.altinn.cloud` `pathPrefix /storage` to the container in [`topology.yaml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/envtopology/topology.yaml) and
  `ProxyMiddleware` (the same pattern as Grafana's path prefix). Configure:
  - `ASPNETCORE_ENVIRONMENT=Development`
  - `GeneralSettings__OpenIdWellKnownEndpoint`, `PlatformSettings__ApiAuthorizationEndpoint` and
    `RegisterServiceSettings__ApiRegisterEndpoint` pointing at localtest
  - `GeneralSettings__DisableA2Endpoints=true` and `AccessTokenSettings__DisableAccessTokenVerification=true`
  - the Azurite settings with `OrgStorageAccount=devstoreaccount1` and the PG connection strings
  - the command-line `--kvSetting:SecretUri=…` workaround

  Extend `env reset` to remove the new volumes. Remove `LT/src/**/Storage/**`.
- **Hard blockers or required new work:**
  1. Key Vault / `PublicSigningKeyProvider` (§3.3). There is a command-line workaround. An upstream
     config flag is preferable.
  2. Metadata and text sync from the app to Storage (§4.1–4.2). This needs new studioctl-server logic, a
     localtest token-minting path for `studio.designer`, and a low cache TTL. It changes the semantics of
     `LocaltestStorageProbe`.
  3. A fake file scanner (§4.3).
  4. Localtest PDP/PIP must fetch instances over HTTP (§4.5).
  5. No public Storage image. Something must build or publish it, and pinning a version against the
     monorepo needs a policy.
  6. Loss of the Storage Explorer. pgAdmin (already present) partly replaces it, and Azurite blobs need
     another viewer.
  7. Footprint and startup: one more .NET service, Azurite, and Yuniql running 39 migration versions plus 60 procedure files on first start (time not measured).
- **Effort:** medium (about 1–2 weeks for a working path, not estimated rigorously). Ongoing cost is
  low: bump the image. It gives real Storage semantics, including SQL concurrency.

**Recommendation:** (b), optionally combined with vendoring the Storage source so studioctl can build the
image in dev mode the way it does for pdf3 and workflow-engine. Ask the altinn-storage team for two things:
- a supported "local" configuration that skips Key Vault and file scan
- a published image

## Not verified

- The real Storage container has not been started against localtest. The Key Vault failure mode and
  token validation are inferred from code.
- Postgres 18 compatibility of the migrations.
- Whether a Storage image exists in a private ACR.
- Whether any app relies on Storage self-link URLs.

## Unlinked references

- Runtime data paths under the storage root (`documentdb/…`, `.instanceversions/versions.json`, `blobs/…`,
  `/AltinnPlatformLocal`) are written by localtest at run time and are not repository files.
- `LT/src/**/Storage/**` is a glob over several directories, so it has no single link target.
- `AppRegistry.cs:~350` was corrected to `AppRegistry.cs:333`, where `CompleteReadyStarts` is defined.
