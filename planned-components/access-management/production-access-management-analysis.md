# Production Access Management: vendoring analysis for the localtest MVP

Source: `/home/agent/code/.reference/altinn-auth` at `392f957` (the same commit the authorization component vendors).
Paths are relative to `src/apps/Altinn.AccessManagement/src/` (written `AM/`) or to `src/` (written `src/…`) unless
they start with `localtest/` (= `/home/agent/code/research/localtest/`). Read-only research; nothing was built or run.
"Not confirmed" marks claims taken from names, DLL strings or inference rather than from code that was read or run.

## Executive summary

- **Feasible with one directive site and about eight fakes.** Every infrastructure dependency is a config switch or a
  public DI interface. One `#if LOCALTEST` call at the end of `AccessManagementHost.Create`
  (`AM/Altinn.AccessManagement/AccessManagementHost.cs:164`, before `builder.Build()`), in the same shape as the PDP
  and Register components.
- **Startup and seeding need config only.** Set `RunIntegrationTests=true`. `Program.cs:24-27` then runs `Init()` and
  goes on serving, so no separate init container is needed. `Init()` (`Program.cs:56-79`) does three things:
  - EF `MigrateAsync`;
  - the static seed (`StaticDataIngest.IngestAll`, about 21k lines of C# constants: 149 roles, 223 packages, role
    maps, role-to-package mappings);
  - the Register import when flag `AccessMgmt.Core.HostedServices.RegisterSync.Import` is on.

  The legacy Yuniql scripts (schemas `delegation`, `accessmanagement`) then run when the host starts.
- **Database.**
  - Postgres 15 or later (uses `MERGE`); production and the upstream tests use 16. The only extension is `hstore`.
  - A separate database in our shared Postgres 16 container works.
  - The migrations hard-code GRANTs and OWNERs to the cluster roles `platform_authorization` and
    `platform_authorization_admin`. Both roles must exist (create them as superusers locally).
  - In `RunIntegrationTests` mode the EF migrations run on the **app** connection string, so the app user must be
    able to run DDL.
- **Register sync** reads `register/api/v2/internal/parties/stream` and
  `…/external-roles/assignments/events/stream` with a platform access token. Both streams exist in our Register.
  - **Can run against our Register:** the production `PartySyncService`/`RoleSyncService` code, once three things
    hold:
    1. Organisations carry a known `unitType` (today the seed has none, and `MapOrganization` throws on it).
    2. Self-identified users carry `user.username` (our Register leaves it out; it would probably throw, not
       confirmed).
    3. The platform-token generator is replaced (`ITokenGenerator`).
  - **Leases:** with no lease blob endpoint, leases are `Noop`. The stored cursor is always empty, so the 2-minute
    periodic sync never runs (`IsDbIngested` stays false). The Init import is always a full re-poll.
  - **Proposed "re-poll everything" mode:** a localtest hosted service or endpoint that calls
    `SyncParty(isInit:true)` and `SyncRoles(isInit:true)` again on startup and on refresh.
  - **What a re-poll does not fix:** Register snapshots restart version ids and emit only `Added` events, so
    assignments that have disappeared need a localtest prune step.
- **The APIs we need have no SBL bridge dependency at request time.**
  - `enduser/authorizedparties` and the PIP endpoints are pure EF queries, apart from one Profile call
    (`internal/user`, which our Register already serves).
  - App instance delegation also needs three things:
    - Resource Registry `resourcelist` at request time (needs a fake);
    - a `dbo.resource` row `app_{org}_{app}` with its provider (from the Resource Registry sync, or seeded);
    - the delegation-policy blob store.
  - The PIP endpoints the PDP calls (`policyinformation/*`) are **anonymous**. The PDP's `authorizedparties` and
    `authorizedparty/{id}` sit behind feature flag `RightsDelegationApi`, which must be turned on.
- **Blob storage.** Replace AM's `IPolicyFactory`/`IPolicyRepository` with a file-backed store that keeps versions.
  The PDP reads delegation policies **directly from blob storage** by `BlobStoragePolicyPath` + `BlobStorageVersionId`,
  which it gets from AM's `getdelegationchanges`.
  - **Simplest consistent design:** a shared named volume (AM read-write, PDP read-only) with one file layout,
    `{container}/{path}` plus `{container}/{path}.versions/{versionId}`.
  - **What changes on the PDP side:** the PDP's existing `LocaltestPolicyRepository` gets the delegation-policy read.
    Its `LocaltestAccessManagementWrapper` and `LocaltestAuthorizedPartiesController` are deleted, so the production
    `AccessManagementWrapper` talks to the real AM.
- **Build size.**
  - 12 AM projects (AppHost excluded) plus 6 libs projects (Api.Contracts, Host, Host.Database, Host.Lease,
    Host.Pipeline, Integration.Platform) plus 2 pkgs (ABAC, PEP) = **20 projects**.
  - About 93k hand-written lines of AM, plus about 202k lines of generated EF migrations, plus about 24k lines of
    libs/pkgs.
  - Same TFM (net10.0), central package management and analyzer setup as the PDP, so the props/targets override
    carries over.
  - No MassTransit, Valkey/Redis, Service Bus or Event Grid. Key Vault and App Configuration are config-gated in
    ServiceDefaults.

## 1. Host composition

`Program.cs` (`AM/Altinn.AccessManagement/Program.cs`):

| Line | What |
| --- | --- |
| 14 | `AccessManagementHost.Create(args)` |
| 17 | `InitializeAppLifecycleFeaturesAsync` (reads flag `AccessManagement.Subunit.AdosInheritance` once; `AM/Altinn.AccessMgmt.Core/Extensions/ServiceProviderExtensions.cs:23-30`) |
| 19-27 | `RunInitOnly` → `Init()` and exit; `RunIntegrationTests` → `Init()` then continue |
| 29-52 | `AddDefaultAltinnMiddleware`, Swagger in Development, authn/authz, `UseEfAudit`, `MapControllers`, `MapDefaultAltinnEndpoints` |
| 56-79 | `Init()`: blocking lease `access_management_init`, `AppDbContext.Database.MigrateAsync`, `StaticDataIngest.IngestAll`, `RegisterHostedService.EnsureDbIsIngestWithRegisterData` |

**How the settings bind.** `AccessManagementAppsettings` binds the configuration **root**
(`AccessManagementAppsettings.cs:22-25`, `AccessManagementHost.cs:323`). So the keys are top level:
- `RunInitOnly`, `RunIntegrationTests`, `Environment`;
- `Platform:*`, which feeds libs Integration.Platform;
- `Lease:*`, `Database:*`, `Core:*`.

`AccessManagementHost.Create` (`AccessManagementHost.cs:66-166`) registers the following, with what to do about each
dependency locally:

| Dependency | Registration | Localtest handling |
| --- | --- | --- |
| ServiceDefaults host builder | `AltinnHost.CreateWebApplicationBuilder("access-management", …)` with Application Insights disabled (L69) | Config switch. Key Vault configuration needs `kvSetting:SecretUri` plus credentials; App Configuration needs `Altinn:AppConfiguration:Endpoint` (inferred from DLL strings, consistent with what was verified for the host builder). Leave both unset |
| App Insights / Azure Monitor OTel | only when not Development **and** `ApplicationInsights:InstrumentationKey` is set (L84-104) | Config switch (leave unset) |
| Lease (`Altinn.Authorization.Host.Lease`) | `AddAltinnLease`: `Lease:StorageAccount:BlobEndpoint` missing → `InMemory` = `NoopLeaseService` (L203-217; `src/libs/Altinn.Authorization.Host/src/Altinn.Authorization.Host.Lease/AltinnLease.cs:58-62`) | Config switch (leave unset). See [Register sync](#3-register-sync) for what Noop means |
| Postgres data sources | `AddAltinnDatabase` with App + Migration `NpgsqlDataSource`s (L110-117; `Host.Database/AltinnHostDatabase.cs:22-77`) | Config |
| EF `AppDbContext` | `AddAccessManagementDatabase`; Source = Migration when `RunInitOnly`, else App (L119-148); outbox handlers registered | Config |
| Yuniql (legacy schemas) | `ConfigurePostgreSqlConfiguration` writes `ConnectionStrings:access-management_db`, `_db_migrate`, `Altinn:Npgsql:access-management:Migrate:Enabled` from `PostgreSQLSettings:EnableDBConnection` (L453-482); `AddAltinnPostgresDataSource().AddYuniqlMigrations(...)` (`AM/Altinn.AccessManagement.Persistence/Extensions/PersistenceDependencyInjectionExtensions.cs:128-174`) | Config; runs as a hosted service when the host starts (not confirmed from source, NuGet only) |
| Azure Blob (policies) | `AddDelegationPolicyRepository`: three shared-key `BlobServiceClient`s named Delegations / Metadata / ResourceRegister, plus `TryAddSingleton<IPolicyFactory, PolicyFactory>` (`…Persistence/Extensions/PersistenceDependencyInjectionExtensions.cs:63-126`). `AzureStorageConfiguration` is a **required section** (L67-69) | Replace `IPolicyFactory` (DI). Keep the appsettings Azurite values so option validation passes; the clients are then never resolved |
| Platform integrations (libs) | `AddAltinnPlatformIntegrationDefaults` from `Platform` (L186-201): Register, Resource Registry, SBL bridge and Notifications clients plus `ITokenGenerator` (`TestTool` unless `kvSetting:SecretUri` is set; `src/libs/…/Integration.Platform/Extensions/ServiceCollectionExtensions.cs:90-137`) | Replace `ITokenGenerator` (DI). TestTool options are validated lazily, so give dummy `Platform:Token:TestTool:{Endpoint,Username,Password}` values or make sure they are never resolved (not confirmed which applies) |
| Access tokens (Altinn.Common.AccessToken / AccessTokenClient) | `IPublicSigningKeyProvider → PublicSigningKeyProvider` (L224); `IAccessTokenGenerator`, `ISigningCredentialsResolver` (`AM/Altinn.AccessManagement.Core/Extensions/CoreDependencyInjectionExtensions.cs:47-48`) | Replace `IPublicSigningKeyProvider` (copy the PDP fake). The generator reads `AccessTokenSettings` as in the PDP (not confirmed for AM) |
| PDP client (PEP) | `IPDP → PDPAppSI`, `AuthorizationApiClient` (L224-227) | Production HTTP to our PDP; needed only by endpoints we do not use (scope/resource policies) |
| JwtCookie authentication | only if `OidcProviders:altinn` exists; `MetadataAddress = Issuer`; cookie `PlatformSettings:JwtCookieName` (L338-365) | Config: `OidcProviders__altinn__Issuer={platform}authentication/api/v1/openid/` |
| Integration (old) | `KeyVaultService` (unused), `PlatformAuthorizationTokenProvider` (unused), typed clients for Parties, Profile, AccessLists, Authentication, IdPorten (Maskinporten), AltinnRoles, DelegationRequests; `ResourceRegistryClient` singleton (`AM/Altinn.AccessManagement.Integration/Extensions/IntegrationDependencyInjectionExtensions.cs:26-55`) | Replace `IResourceRegistryClient`. Point the other clients at local services or leave them unused. `AddMaskinportenHttpClient` with empty JWK is assumed lazy (not confirmed) |
| Hosted services | `RegisterHostedService`, `OutboxHandlerJob`, `OutboxReaperJob` (`AM/Altinn.AccessMgmt.Core/Extensions/ServiceCollectionExtensions.cs:56-58`) | Feature flags: `AccessMgmt.Core.HostedServices.{RegisterSync,ResourceRegistrySync,Outbox.Handler,Outbox.Reaper}` |
| Feature management | `AddFeatureManagement` (L76); flags in `appsettings*.json` | Config |
| MassTransit / Service Bus, Valkey/Redis, Event Grid, Storage Queues | none: `Host.MassTransit` is not referenced by AM, and grep finds nothing for the rest | n/a |

**ServiceDefaults pieces used:**
- `AltinnHost.CreateWebApplicationBuilder`, `AddDefaultAltinnMiddleware`, `MapDefaultAltinnEndpoints` (health);
- `GetAltinnServiceDescriptor` (L469);
- `AddAltinnPostgresDataSource` and `AddYuniqlMigrations` (package `Altinn.Authorization.ServiceDefaults.Npgsql.Yuniql`
  5.5.0).

**API projects mounted:** all six. Each is a `ProjectReference` of the host (`Altinn.AccessManagement.csproj:36-42`)
and is discovered as an application part by `AddControllers()` / `MapControllers()`. There is no
`ConfigureApplicationPartManager` anywhere. `AddAccessManagementEnduser` only registers `IInputValidation` and
`AddAccessManagementInternal` only logs (`AM/Altinn.AccessManagement.Api.Enduser/AccessManagementEnduserHost.cs:25-30`,
`AM/Altinn.AccessManagement.Api.Internal/AccessManagementInternalHost.cs:21-25`). The host project has its own
controllers too: `AppsInstanceDelegationController` and `InternalAuthorizedPartiesController`.

## 2. Database

### Mechanisms

1. **EF Core (main schema).**
   - 51 migrations in `AM/Altinn.AccessMgmt.PersistenceEF/Migrations/`.
   - Run by `Init()` only (`Program.cs:72`), never on a plain start.
   - `CustomMigrationsSqlGenerator` adds audit triggers into `dbo_history`, activity-log partitions and table GRANTs to
     both roles (`TableGrantConstants.cs:12`). This applies in both App and Migration mode
     (`AM/Altinn.AccessMgmt.PersistenceEF/Extensions/ServiceCollectionExtensions.cs:205-217`).
   - Schemas created:
     - `dbo` and `dbo_history` (`Migrations/20250922113124_Init.cs:13-17`);
     - `ingest`, owned by `platform_authorization_admin` (`20250922113202_Ingest.cs:14-17`);
     - `consent` (`ConsentSchema.sql`, which also runs `CREATE EXTENSION IF NOT EXISTS hstore`).
   - `translation` and `translation_history` from `conf.json` are not used. Translations live in
     `dbo.translationentry`.
   - `Utils/PreMigrationScripts` and `ManualMigrationScripts` are not embedded: they are historical conversion
     scripts.
2. **Yuniql (legacy).**
   - Embedded `AM/Altinn.AccessManagement.Persistence/Migration/**` (v0.00…v0.18).
   - Schemas `delegation` and `accessmanagement`, plus enums such as `delegation.delegationchangetype`. The Npgsql
     data source maps these enums (`PersistenceDependencyInjectionExtensions.cs:133-169`).
   - GRANTs to `platform_authorization`, `OWNER TO platform_authorization_admin`.
   - Runs at host start when `EnableDBConnection=true`, using the admin connection (inferred: NuGet package, not
     confirmed).
   - The order is therefore EF (Init) first, then Yuniql, the same order as production (pre-deploy job, then pod).
3. **`AddAltinnDatabase`** only registers data sources; it runs no SQL.

Production runs the init as a separate Job with env `RunInitOnly=true`
(`manifests/pre-deploy/base/02-db-migration.yaml`, env at the container spec). Note the image name there,
`accessmanagement`, differs from the deployment's `access-management`.

### Connection strings and config keys

- Keys: `PostgreSQLSettings:ConnectionString`, `AdminConnectionString` (format strings with `{0}`),
  `AuthorizationDbPwd`, `AuthorizationDbAdminPwd`, `EnableDBConnection` (`AccessManagementHost.cs:168-184,453-482`).
  `string.Format` runs unguarded, so all of them are required.
- Defaults: `Database=authorizationdb`, users `platform_authorization` / `platform_authorization_admin`
  (`appsettings.json`). Production at22 uses the same (`manifests/pre-deploy/at22/config/environment.env`).
- If `ConnectionStrings:access-management_db` is already set, the derivation is skipped (L470-475).

### Postgres version and extensions

- `MERGE` (`AM/Altinn.AccessMgmt.Core/Utils/IngestService.cs:114`) needs 15 or later.
- A custom `dbo.uuid_generate_v7()` sits on `gen_random_uuid()`, so there is no dependency on PG18.
- Declarative partitions with a DEFAULT partition.
- Production uses PG 16 (`infra/main.tf:227`); tests use `postgres:16.1-alpine`.
- Only `hstore` is needed, and it ships in the official image.

### Static data

- `StaticDataIngest.IngestAll` upserts C# constants by fixed GUIDs (`AM/Altinn.AccessMgmt.PersistenceEF/Data/StaticDataIngest.cs:71-288`).
- Contents: providers, entity types (6) and variants (58), area groups and areas, packages (223), roles (149,
  including the Altinn 2 codes such as `regna` and `a0239`, and `privatperson`/`priv`), system entities, activity
  types, `IngestRoleMap`, `IngestRolePackage`, `IngestEntityVariantRole`, and EN/NN translations.
- An advisory lock makes it safe to run twice (EF seeding hook plus the explicit call).
- **No resources are seeded.** They come from the Resource Registry sync.
- `daglig-leder` is `RoleConstants.ManagingDirector` (`RoleConstants.cs:521-536`: `Urn
  urn:altinn:external-role:ccr:daglig-leder`, `LegacyUrn urn:altinn:rolecode:dagl`).

### Sharing our Postgres 16 container

Sharing works.
- The database name is free. `authorizationdb` only toggles tracing tweaks
  (`PersistenceEF/Extensions/ServiceCollectionExtensions.cs:34`).
- SQL is schema-qualified, and the advisory lock is per database.
- The cluster roles `platform_authorization` and `platform_authorization_admin` do not collide with Storage's
  `platform_storage*` or the workflow engine's `postgres`.

Proposed `localtest/postgres-init/30-access-management.sh`:

```sh
psql ... -c "CREATE ROLE platform_authorization_admin LOGIN SUPERUSER PASSWORD 'Password';"
psql ... -c "CREATE ROLE platform_authorization LOGIN SUPERUSER PASSWORD 'Password';"   # see note
psql ... -c "CREATE DATABASE accessmanagementdb OWNER platform_authorization_admin;"
```

- **Why the app role is a superuser:** in `RunIntegrationTests` mode the EF migrations run on the app connection.
  Upstream's own tests make it a superuser too (`test/…/EFPostgresFactory.cs:44-45`). There is also no
  `GRANT USAGE ON SCHEMA dbo`, so a non-superuser app role probably fails (not confirmed).
  - Alternative: point both connection strings at the admin user. The role `platform_authorization` must still
    exist because of the GRANTs.
- **Existing volumes:** `docker-entrypoint-initdb.d` runs only on an empty volume, so existing `postgres-data`
  volumes need `docker compose down -v` (or an idempotent init step in the AM container) to get the new database.

## 3. Register sync

### `RegisterHostedService`

File: `AM/Altinn.AccessMgmt.Core/HostedServices/RegisterHostedService.cs`.

- Runs on a timer: immediately at start, then every 2 minutes (L44), with no overlapping runs (L94).
- Flag `AccessMgmt.Core.HostedServices.ResourceRegistrySync`: lease `access_management_resource_registry_sync`, then
  `SyncResourceOwners` and `SyncResources` (L102-111).
- Flag `AccessMgmt.Core.HostedServices.RegisterSync`: lease `access_management_register_sync`, then
  `SyncParty(isInit:false)` and `SyncRoles(isInit:false)` (L113-124).
- `EnsureDbIsIngestWithRegisterData` (L49-86) runs only in `Init()` and only with flag `…RegisterSync.Import`. It
  calls `SyncParty(isInit:true)` and `SyncRoles(isInit:true)`, then sets `RegisterLease.IsDbIngested=true` in the
  lease blob.
- The periodic sync is a no-op while `IsDbIngested` is false (`PartySyncService.cs:48-51`,
  `RoleSyncService.cs:58-61`).
- Checkpoints are the absolute `next` URLs stored as JSON in the lease blob (`HostedServices/Leases/RegisterLease.cs`).
  There is no database table for them.

**With Noop leases:** `Get<T>` returns `new T()` and `Update` does nothing (`Host.Lease/Noop/NoopLease.cs`). Three
consequences:
1. The periodic sync never runs.
2. The Init import always starts from the beginning of each stream: a full re-poll.
3. Our Register resetting version ids on snapshot swaps (`localtest/components/register/fakes/…/Persistence/RegisterStore.cs`,
   `VersionId = ++version` from 0 and `RoleEvents` numbered from 1) does no harm, because no cursor survives.

### Endpoints, auth and paging

Client: `src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/Register/`.

| Stream | Request |
| --- | --- |
| Parties | `GET {Platform:Register:Endpoint}/register/api/v2/internal/parties/stream?fields=identifiers,type,display-name,deleted,deleted-at,user,person.date-of-birth,person.date-of-death,sysuser.type,org.type,si.type,si.email` (`PartiesStreamEndpoint.cs:13-35`) |
| Roles | `GET …/register/api/v2/internal/parties/external-roles/assignments/events/stream` with no fields (`RoleStreamEndpoint.cs:18`) |

- **Paging:** follows `links.next` until it is empty. Payload `{stats, links{next}, data[]}` (`PaginatorStream.cs:62-131`).
- **Auth:** header `PlatformAccessToken` from `ITokenGenerator.CreatePlatformAccessToken`, issuer `platform`, app
  `access-management` (`Extensions/ServiceCollectionExtensions.cs:144-145`). No scopes, no Maskinporten.
  - **Our Register already accepts it:** the lenient platform-token handler validates against the shared certificate.
  - **What we need:** a fake `ITokenGenerator` that signs with `localtest/certs/localtest-accesstoken.pfx` (the
    interface is public; the TestTool and KeyVault implementations are internal).
- **Party DTO:** `Altinn.Register.Contracts.Party` (NuGet 1.8.0).

### What is written

**`PartySyncService`** (`AM/Altinn.AccessMgmt.Core/HostedServices/Services/PartySyncService.cs`) maps each party to a
`dbo.entity` row. `Id` = party uuid; the row also gets `PartyId`, `UserId` (from `user`), `Username`, `IsDeleted` and
`DeletedAt`.

| Party type | Entity | Self-assignment | Requirement / failure |
| --- | --- | --- | --- |
| Person | Person/Person, RefId = SSN, date of birth/death | `privatperson` (L239-253) | — |
| Organization | variant looked up **by `UnitType` name** (AS, ENK, BEDR, AAFY, ADOS, …) | — | `EntityVariantConstants.TryGetByName(organization.UnitType.Value, …)`; **throws on an unknown or missing unit type** (L271-287) |
| SelfIdentifiedUser | SI / SI_EDU / SI_EMAIL, RefId = `User.Value.Username.Value` | `SelfRegisteredUser` | needs the username |
| SystemUser | StandardSystem or AgentSystem | — | — |
| EnterpriseUser | EnterpriseUser | — | — |

- Writes are a bulk temp-table `MERGE` with no delete (`Utils/IngestService.cs:114-122`).
- An exception ends the run (`PartySyncService.cs:136-143`).

**`RoleSyncService`** maps events as follows:
- `roleIdentifier` → `RoleConstants.TryGetByCode`. Unknown identifiers throw (`RoleSyncService.cs:308-321`).
- `Added` → `MERGE` into `dbo.assignment` (from, role, to).
- `Removed` → delete. `forretningsforer` also removes the client delegations linked to it.
- `hovedenhet` (BEDR), `ikke-naeringsdrivende-hovedenhet` (AAFY), and `administrativ-enhet-offentlig-sektor` (ADOS,
  only when flag `AccessManagement.Subunit.AdosInheritance` is on) set `Entity.ParentId = ToParty` on `FromParty`
  (L272-292).

### Against our vendored Register

- **Role events: compatible.**
  - All 103 identifiers in `localtest/components/register/fakes/…/Data/external-role-definitions.json` (61 CCR, 42
    CRA) exist as `Code` in `RoleConstants.cs`. I checked each one with grep.
  - The Register emits `hovedenhet` from sub-unit to main unit (`RegisterStore.cs`, organisations loop), which is
    exactly what AM uses to set `ParentId`.
  - Only `Added` events are produced.
- **Role coverage gap.** The Register turns into assignments only the role codes that resolve to a CCR/CRA definition
  **for an organisation**. Three kinds are lost:
  - Altinn 2-only codes (`REGNA`, `A0239`, …);
  - codes on the user's own party (`selfRoleCodes`, e.g. `regna`/`dagl` for user 12345, used by the Cypress
    frontend-test);
  - `accessPackages` from the seed.

  AM has no Altinn 2 sync, so to keep parity with the current fakes a small localtest ingest should write those as
  `dbo.assignment` rows (A2 roles exist in `RoleConstants`, provider Altinn2) or as package delegations.
- **Organisations without `unitType`** make the whole run fail. The current seed has none (see
  `localtest/components/authorization/README.md`, "Today the register sends no `unitType`"). Fix it in the seed
  (`AS`/`ENK` for main units, `BEDR` for sub-units) or default it in the Register fake.
- **Self-identified users.** The Register fake deliberately keeps the username out of `Usernames`, so `user.username`
  is likely unset and `User.Value.Username.Value` throws (not confirmed: depends on how the Register's v2 mapping
  fills `user`). Either fill the username on the record or accept losing SI users.
- **Persons need `user.userId`** for `enduser/authorizedparties` (it finds the subject by `dbo.entity.UserId`). The
  Register sets `UserIds`.
- **System users** are fine (`SystemUserRecordType.Standard`).

### "Re-poll everything" mode

Production has no reset (no API or flag). Locally, Noop leases already make every Init import a full re-poll. Add a
localtest hosted service with `POST accessmanagement/api/localtest/refresh` that, inside an EF scope with audit
`RegisterImportSystem`, does:

1. `SyncResourceOwners` + `SyncResources` (if the resource sync below is used);
2. `SyncParty(NoopLease, isInit:true)` and `SyncRoles(NoopLease, isInit:true)`, which are idempotent `MERGE`s;
3. a prune step: delete `dbo.assignment` rows created by `RegisterImportSystem` that were not seen in this pass.
   Register snapshots never emit `Removed`, so this needs our own code;
4. the Altinn 2 / self-role / package ingest from `register/api/localtest/test-users`.

Trigger it at startup and on a short timer (the Register re-reads its seed every 2 s; a 10–30 s poll is enough), or
call it from Register's app registration.

### Other sync sources

| Source | Status | Needed for (a) instance delegations / (b) authorized parties / (c) PIP |
| --- | --- | --- |
| Resource Registry sync | `resource/orgs` (providers), `resource/updated?since=` then `resource/{id}`; writes `dbo.resource`, `roleresource`, `packageresource`; no token (`ResourceSyncService.cs`, `Integration.Platform/ResourceRegistry/*`) | (a) **yes**: `InsertInstanceDelegation` needs `dbo.resource` with `RefId == app_{org}_{app}` (`AM/Altinn.AccessManagement.Persistence/DelegationMetadataEF.cs:526`, `SingleAsync`) and a provider for the org. (b), (c) only for `anyOfResourceIds` / role-resource filters |
| SBL bridge / Altinn 2 | `AltinnSblBridgeClient.StreamRoles` exists, but nothing in AM uses it | none |
| Profile | request-time only (`internal/user`) | (b) when include filters are `Auto`; (c) `getdelegationchanges` for a user subject |

## 4. The APIs we need

| API | Route / auth | Chain and data | Request-time outbound |
| --- | --- | --- | --- |
| App instance delegation | `AM/Altinn.AccessManagement/Controllers/ResourceOwnerAPI/AppsInstanceDelegationController.cs`: `POST`/`GET accessmanagement/api/v1/app/delegations/resource/{resourceId}/instance/{instanceId}` (L96, L154), `POST …/app/delegationrevoke/…` (L205), `DELETE` revoke-all (L258), `GET app/delegationcheck/…` (L50). Policy `PlatformAccess` = `AccessTokenRequirement()` with no issuer restriction; the caller is `app_{iss}_{urn:altinn:app}` from the `PlatformAccessToken` header (L300-314). No user token | `AppsInstanceDelegationService` → (1) Resource Registry `resourcelist`, (2) app policy `{org}/{app}/policy.xml` from the Metadata blob; delegable rights are rules whose subject is `urn:altinn:resource:delegation` = `app_{org}_{app}`, (3) parties from `dbo.entity`, (4) `PolicyAdministrationPoint` writes `resourceregistry/{resourceId}/instances/from_{uuid}/to_{uuid}/{instanceId}/delegationpolicy.xml` to Delegations (lease, then write, version id), (5) `DelegationMetadataEF.InsertInstanceDelegation` upserts `dbo.assignment` (role `AppControlledRightholder`) and `dbo.assignmentinstance` (PolicyPath, PolicyVersion) | Resource Registry (`v1/resource/resourcelist`, uncached), blob |
| `GET accessmanagement/api/v1/enduser/authorizedparties` | `AM/Altinn.AccessManagement.Api.Enduser/Controllers/AuthorizedPartiesController.cs:18,37-102`; scope `altinn:portal/enduser` **or** `altinn:accessmanagement/authorizedparties` (Host L390). `include*` flags default **false** | `AuthorizedPartiesServiceEf` (the only registration) → `dbo.entity` by `UserId` → `ConnectionQuery` over `assignment`, `role`, `rolemap`, `delegation`, `entity.ParentId`, `rolepackage`, `assignmentpackage`, `assignmentresource`, `assignmentinstance` | Profile `internal/user` when a filter is `Auto` (memory-cached). **No SBL bridge**: flags `AccessManagement.AuthorizedParties.IncludeAltinn2` / `UsingConnectionQueryOnly` exist only in appsettings, and no `.cs` file reads them |
| `POST accessmanagement/api/v1/policyinformation/getdelegationchanges` | `AM/Altinn.AccessManagement.Api.Internal/Controllers/PolicyInformation/PolicyInformationPointController.cs:17,32-49`; **no `[Authorize]`** and no fallback policy | `PolicyInformationPoint.GetAllDelegations(includeInstanceDelegations:true)` → `assignmentresource`, `assignment` + key roles, `entity` parents, `assignmentinstance`; returns `BlobStoragePolicyPath`/`BlobStorageVersionId` | Profile for user subjects |
| `GET …/policyinformation/roles-and-accesspackages?from&to&…` | same controller, L60-89, anonymous | `ConnectionQuery` (packages on); returns role `Urn` **and** `LegacyUrn` (`urn:altinn:external-role:ccr:daglig-leder`, `urn:altinn:rolecode:dagl`) and `urn:altinn:accesspackage:*` | none |
| `GET accessmanagement/api/v1/authorizedparties`, `authorizedparty/{partyId}` (what the PDP calls) | `AM/Altinn.AccessManagement/Controllers/InternalAuthorizedPartiesController.cs:48-202`; plain `[Authorize]` (user JwtCookie or bearer), `[FeatureGate(RightsDelegationApi)]`, which is **false** in `appsettings.json` and true in Development and production env | same service as enduser | Profile |
| Called by Authentication (for completeness) | `internal/party` (platform issuer), `internal/connections/selfidentifiedusers`, `internal/systemuserclientdelegation/*`, `authorizedparty/{id}` | — | — |

Calls from Storage to AM: not confirmed; Storage is not in this repository.

**Data a seeded user needs to get the same answers as today's fakes.** Example: Ola (1337) with DAGL for 500000,
sub-unit 500001, INNH for 500002.
- `dbo.entity` rows:
  - the person, with `UserId` 1337 and `PartyId`;
  - the organisations, with variants AS/ENK;
  - the sub-unit, with variant BEDR and `ParentId` set.
- `dbo.assignment` rows (org → person): `daglig-leder`, `innehaver`.
- The person's own `privatperson` assignment, which the party sync writes.

The static `rolemap`/`rolepackage` rows then yield:
- the `dagl` legacy code;
- 23 Altinn 2 codes mapped from DAGL (e.g. `a0212`, `IngestRoleMap.cs`);
- DAGL's access packages.

So the AM answers will contain **more** roles and packages than the current fakes. Note also:
- `authorizedRoles` in the authorized-parties list holds both `daglig-leder` and `dagl`
  (`AuthorizedPartiesServiceEf.cs` around L409-419). That is not the current fake's `dagl` only.
- The PIP emits lower-case `urn:altinn:rolecode:dagl` only. The current fake emits both cases. Policies using
  upper-case `DAGL` would stop matching unless the PDP-side case duplication is kept (not confirmed whether production
  Studio policies still use upper case; the README says older ones do).
- `enduser/authorizedparties` requires the scope `altinn:portal/enduser`. The browser login's default portal scopes
  include it (`localtest/components/authentication/vendor/…/Configuration/GeneralSettings.cs:168`). Whether
  `/Home/GetTestUserToken` tokens carry it is not confirmed.

## 5. Blob storage

**AM interfaces.**
- `IPolicyFactory.Create(PolicyAccountType, path)` / `Create(path)` (`AM/Altinn.AccessManagement.Core/Repositories/Interfaces/IPolicyFactory.cs`).
- `IPolicyRepository` is bound to one path: `GetPolicyAsync`, `GetPolicyVersionAsync(version)`, `WritePolicyAsync`,
  `WritePolicyConditionallyAsync(stream, leaseId)`, `DeletePolicyVersionAsync`, `TryAcquireBlobLease`,
  `ReleaseBlobLease`, `PolicyExistsAsync` (`IPolicyRepository.cs:9-71`).
- Routing (`Persistence/Policy/PolicyFactory.cs:30-40`): `*delegationpolicy.xml` goes to Delegations,
  `*resourcepolicy.xml` to ResourceRegister, everything else to Metadata.

**Semantics the fake must keep:**
- Writes return `Response<BlobContentInfo>` with status **201** and a `VersionId`, which callers check
  (`Core/Services/PolicyAdministrationPoint.cs:53,87-88,161-162`).
- A missing blob or version reads as an **empty stream**.
- PAP creates an empty blob before leasing.
- A wrong lease is a 412.
- `BlobsModelFactory.BlobContentInfo(...)` builds the result. Upstream's test fake shows how:
  `test/Altinn.AccessManagement.TestUtils/Mocks/PolicyRepositoryMock.cs:144-179`, `PolicyFactoryMock.cs`.

**Paths:**
- App policy: `{org}/{app}/policy.xml` (Metadata).
- Resource policy: `{id}/resourcepolicy.xml` (ResourceRegister).
- Resource and app delegations: `PolicyHelper.cs:216,231`.
- Instance delegation: `DelegationHelper.cs:527`.
- Path and version are stored in `dbo.assignmentresource` and `dbo.assignmentinstance` (`PolicyPath`,
  `PolicyVersion`).

**PDP side.**
- `DecisionController.cs:691-774` calls AM `getdelegationchanges` with no token, then
  `_prp.GetPolicyVersionAsync(BlobStoragePolicyPath, BlobStorageVersionId)`.
- That reaches its own `IPolicyRepository` (path-parameterised; `Repositories/PolicyRepository.cs:160-168` routes
  `*delegationpolicy.xml` to delegations).
- The PDP gets resource policies from Resource Registry over HTTP, not from blob storage.
- AM has **no** HTTP endpoint that serves delegation policy XML.

**Proposed design: one file store, two adapters, a shared volume.**
- Named volume `policy-store`, mounted read-write in AM and read-only in the PDP at `/data/policies`.
- `delegationpolicies/{path}` holds the current version and `delegationpolicies/{path}.versions/{versionId}` each
  version.
  - `versionId` is a sortable UTC timestamp, like Azure.
  - Writes go to a temp file and are renamed, so the read-only PDP never sees a partial file.
  - Leases are an in-process `SemaphoreSlim` per path plus a lease-id map, enough for one AM instance.
- AM `LocaltestPolicyFactory`/`LocaltestPolicyRepository`:
  - Delegations → the store above.
  - Metadata (read) → the same resolution the PDP fake already uses: `/policies/{org}/{app}/policy.xml`, otherwise
    the running app's `api/v1/meta/authorizationpolicy`, with `[ORG]`/`[APP]` replaced.
  - ResourceRegister (read) → `/policies/resourceregistry/{id}/policy.xml`.
  - Metadata and ResourceRegister writes are not needed (not confirmed that no used path writes them).
- PDP: extend `LocaltestPolicyRepository.GetPolicyVersionAsync`/`GetPolicyAsync` for `*delegationpolicy.xml` to read
  the store. "Delegation policies: none" ends.
- Put the store class in one file and link it into both components' `fakes/`, or copy it. Two thin adapters are needed
  because the interfaces differ.
- Rejected alternatives:
  - Azurite: zero fakes, but it adds a container and the brief rules it out.
  - An HTTP fetch from AM: needs new endpoints on both sides.

## 6. Outbound calls

| Target | Client / config key | Used by the APIs we need | Localtest |
| --- | --- | --- | --- |
| Register v2 streams | `AltinnRegisterClient`, `Platform:Register:Endpoint` (host part only, e.g. `http://local.altinn.cloud:8000`) | sync | production client to our Register; fake `ITokenGenerator` |
| Register v1 (`parties`, `partylist`, `partylistbyuuid`, `lookup`) | `PartiesClient`, `PlatformSettings:ApiRegisterEndpoint`, token `platform`/`access-management` (`Integration/Clients/PartiesClient.cs:52-68`) | not in the paths above (not confirmed for every branch) | point at our Register |
| Profile `internal/user` | `ProfileClient`, `PlatformSettings:ApiProfileEndpoint` | authorized parties (`Auto`), `getdelegationchanges` | our Register serves `profile/api/v1/internal/user` |
| Resource Registry (request time) | `ResourceRegistryClient` (singleton), `PlatformSettings:ApiResourceRegistryEndpoint` | instance delegation (`resourcelist`) | **fake `IResourceRegistryClient`**: resources from `/policies/resourceregistry/*/resource.json`, plus `app_{org}_{app}` for each app known to the Register (`register/api/localtest/apps`) or found under `/policies/{org}/{app}` |
| Resource Registry (sync) | `AltinnResourceRegistryClient`, `Platform:ResourceRegistry:Endpoint` | creates `dbo.resource` and providers | **fake `IAltinnResourceRegistry`** with the same sources (orgs from a static list such as `ttd`, `digdir`), run from the localtest refresh; or upsert provider and resource rows directly |
| PDP `decision` | `PDPAppSI`, `PlatformSettings:ApiAuthorizationEndpoint` | no | production HTTP to our PDP |
| Authorization `parties`, `accesslist/...` | `PartiesClient`, `AccessListAuthorizationClient` | no | point at the PDP |
| Authentication `systemuser`, `systemregister` | `AuthenticationClient`, `ApiAuthenticationEndpoint` | no | point at Authentication |
| SBL bridge | `AltinnRolesClient`, `DelegationRequestProxy`, `PartiesClient` (key roles), `SblBridgeSettings:BaseApiUrl` / `Platform:SblBridge:Endpoint` | no | unreachable URL |
| Notifications | `AltinnNotificationClient`, `Platform:Notifications:Endpoint` | no (outbox flags off) | flags off |
| ID-porten / Maskinporten | `IdPortenAuthorizationClient` | no | leave settings empty |

## 7. Build and vendoring

**Projects to copy, keeping the relative layout as the PDP does:**
- `src/apps/Altinn.AccessManagement/` (excluding `AppHost` and `test/` if preferred; AppHost references
  `src/tools/Altinn.AccessMgmt.FFB`, so leave it out of any build).
- `src/libs/Altinn.Authorization.Api.Contracts`.
- `src/libs/Altinn.Authorization.Host` (`Host`, `Host.Database`, `Host.Lease`, `Host.Pipeline` (referenced by AM
  Core); `Host.MassTransit` unused).
- `src/libs/Altinn.Authorization.Integration` (`Integration.Platform`, which itself references AM Core, a cycle by
  path but not by project).
- `src/pkgs/Altinn.Authorization.ABAC` and `src/pkgs/Altinn.Authorization.PEP`.
- The build files the PDP vendor already lists: `global.json`, `src/Directory.*`, `Altinn.ruleset`, `Stylecop.json`,
  and the per-folder `Directory.Build.*`.
- AM's own `src/apps/Altinn.AccessManagement/src/Directory.Build.props`, which carries an AutoMapper audit
  suppression.

**Build settings:**
- TFM net10.0 (`src/Directory.Build.props:5`); pkgs multi-target net9.0;net10.0. SDK 10.0.107, roll-forward
  `latestMajor`.
- Central package management in `src/Directory.Packages.props`: ServiceDefaults 5.5.0, EF Core 10.0.12, Npgsql 10.0.3,
  Yuniql 1.3.15, Azure.Storage.Blobs, AutoMapper 14, Altinn.Register.Contracts 1.8.0, AccessTokenClient 3.2.12.
- StyleCop analyzers through `src/Directory.Build.targets:105-106`, with `TreatWarningsAsErrors` in places. The PDP
  component's `localtest.build.props` already relaxes this.

**The props/targets override carries over unchanged:**
- `LocaltestIsVendoredProject` based on path, `LOCALTEST` defined, and `fakes/**/*.cs` compiled into the host project
  (`MSBuildProjectName == 'Altinn.AccessManagement'`).
- Every interface to replace is public and reachable from the host: `IPolicyFactory`, `IPolicyRepository`,
  `IResourceRegistryClient` (AM Core), `ITokenGenerator`, `IAltinnResourceRegistry` (Integration.Platform),
  `IPublicSigningKeyProvider` (NuGet).
- The services the fakes call (`IPartySyncService`, `IRoleSyncService`, `IResourceSyncService`, `IIngestService`,
  `AppDbContext`) are public (not confirmed for every helper).

**Size:**

| Part | Size |
| --- | --- |
| AM projects | 12, about 93k lines excluding about 202k lines of EF migrations (PersistenceEF 236k total, AccessMgmt.Core 25k, AM Core 17k, Api.Enduser 5.5k, host 3k) |
| libs | about 11k |
| pkgs | about 10k |
| Publish | heavy compile (migrations); expect 2–4 minutes in the SDK container (not confirmed) |

### Directive sites

**One**, in `AccessManagementHost.Create` right before `return builder.Build();`:

```csharp
#if LOCALTEST
Altinn.AccessManagement.Localtest.LocaltestAccessManagementExtensions.AddLocaltestAccessManagement(builder);
#endif
```

**`Program.cs` stays untouched** because `RunIntegrationTests=true` gives "Init, then serve". The cost: EF migrations
and the Init import run on every container start. That is cheap with idempotent seeds; the re-poll is wanted anyway.

**A possible second site (not confirmed that it is needed):** only if an eager read inside `Create` fights the
override:
- `ConfigureAuthorization` reads `OidcProviders` and `PlatformSettings` eagerly (L340-341). That is handled with env
  configuration, not a directive.
- `GetConnectionStrings` reads eagerly too; also env configuration.

### Fakes (`components/access-management/fakes/`)

| Fake | Replaces | Size guess |
| --- | --- | --- |
| `LocaltestAccessManagementExtensions` | DI composition. Derives config from `Localtest:AccessManagement:PlatformEndpoint` as the PDP does: `PlatformSettings:Api*Endpoint`, `Platform:{Register,ResourceRegistry}:Endpoint`, `OidcProviders:altinn:*`, `JwtCookieName`. Feature flags: `RightsDelegationApi`, `RegisterSync.Import` on; `RegisterSync`, `ResourceRegistrySync`, outbox off. `RunIntegrationTests=true` | 150 lines |
| `LocaltestPublicSigningKeyProvider` | `IPublicSigningKeyProvider` (copy of the PDP's) | 40 |
| `LocaltestTokenGenerator` | `ITokenGenerator` (Integration.Platform): signs with the shared certificate through `IAccessTokenGenerator` | 40 |
| `LocaltestPolicyStore`, `LocaltestPolicyFactory`, `LocaltestPolicyRepository` | `IPolicyFactory`/`IPolicyRepository` (shared versioned store plus Metadata/ResourceRegister resolution) | 300 |
| `LocaltestResourceRegistryClient` | `IResourceRegistryClient` (request time) | 150 |
| `LocaltestAltinnResourceRegistry` (optional) | `IAltinnResourceRegistry` for the production `ResourceSyncService`; or a direct provider/resource upsert | 150 |
| `LocaltestSyncService` + `LocaltestController` | hosted re-poll (parties, roles, resources), prune, Altinn 2 / self-role / package ingest from `register/api/localtest/test-users`; `POST accessmanagement/api/localtest/refresh` | 300 |

**Changes in other components:**
- **PDP:**
  - Drop `LocaltestAccessManagementWrapper` and `LocaltestAuthorizedPartiesController`, so the production
    `AccessManagementWrapper` runs.
  - Extend `LocaltestPolicyRepository` for delegation policies.
  - Mount `policy-store` read-only.
  - Decide whether to keep emitting upper-case role codes (that would need a thin wrapper decorator).
- **Ingress:** `/accessmanagement/` → `access-management`.
- **Register:**
  - `unitType` on organisations;
  - a username for self-identified users;
  - optionally a "changed" signal (or the AM poll interval) after app registration.
- **Compose:** service `access-management` (depends on postgres healthy and register), `postgres-init`, the
  `policy-store` volume, and `Localtest__AccessManagement__PlatformEndpoint`.

### Order of work

1. **Build and start with everything stubbed.**
   - Vendor, overrides, Dockerfile, the one directive, and the extensions with the signing-key provider, token
     generator and policy factory fakes.
   - Postgres roles and database.
   - `RunIntegrationTests=true` with `RegisterSync.Import` **off**.
   - Success: `/health` 200, `__EFMigrationsHistory` holds 51 rows, schemas `delegation`/`accessmanagement` exist after
     the start, and `GET meta/info/roles` answers.
2. **Static data seeding.** Already covered by step 1 (Init). Verify `dbo.role` (149), `dbo.package` (223) and
   `dbo.rolepackage`, and that `meta/info/accesspackages` lists them.
3. **Register sync against our Register.**
   - Turn `RegisterSync.Import` on.
   - Fix `unitType` and the SI username in the Register.
   - Success: `dbo.entity` holds every seeded party (the sub-unit with `ParentId`), and `dbo.assignment` holds
     `daglig-leder` for Ola and 500000.
   - Then add the localtest re-poll, prune and the Altinn 2 / self-role ingest, and check re-poll after
     `POST register/api/localtest/apps`.
4. **Instance delegations end to end with the PDP.**
   - Resource Registry fakes and a `dbo.resource` row for `app_ttd_testapp`.
   - An app policy containing a `urn:altinn:resource:delegation` rule (not confirmed that the sample policies have
     one; signing apps' policies normally do).
   - The shared policy store, and the PDP reading delegation policies.
   - Success: app-lib signing delegates `read`/`sign` on an instance to another person; the PDP permits that person on
     the instance; revoke removes it.
5. **Authorized parties and PIP endpoints replacing the current fakes.**
   - `RightsDelegationApi` on; remove the PDP's fakes and point the ingress at AM.
   - Rerun `localtest/components/authorization/smoke-test.sh` and the frontend-test Cypress flows.
   - Document the differences: more `authorizedRoles`, lower-case-only role codes from the PIP, and
     `onlyHierarchyElementWithNoAccess` computed by production.

## Open points / not confirmed

- How Yuniql runs (ServiceDefaults NuGet, no source): when it runs relative to `Init()`, and where its version table
  lives.
- Whether a non-superuser `platform_authorization` works.
- Whether TestTool option validation fires when `ITokenGenerator` is replaced.
- How Register v2 fills `user.username` for our self-identified records.
- Whether `/Home/GetTestUserToken` tokens carry `altinn:portal/enduser`.
- Whether Studio app policies used locally contain the delegation subject rule.
- Whether anything on the paths we use calls the SBL bridge in a branch not traced here.
