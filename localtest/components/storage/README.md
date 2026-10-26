# storage

Production Altinn Storage (`vendor/`, see `UPSTREAM.md`) on a real Postgres, with Azure Blob replaced by files on a
volume, the file-scan queue replaced by an immediate "clean" verdict, Key Vault switched off. Application metadata
and texts arrive through the production endpoints, pushed by the app runner. The outbox to the Dialogporten adapter runs
as in production and ends in a localtest handler that posts each command to the adapter (see
[Dialogporten outbox](#dialogporten-outbox)).

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three `LOCALTEST` sites, two in `Program.cs` and one in `InstancesController.cs` (`git diff` against the baseline commit shows them); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import the vendored `Directory.Build.props` when it is there (not in the image), define `LOCALTEST`, bypass CSharpier, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): file blob repository, file-scan client, signing-key provider, DI and configuration composition |
| `postgres-init/10-storage.sh` | Postgres init script: creates `platform_storage_admin`, runs the vendored `infra/postgres_init.sql` unchanged, hands the database and schema to the admin role |
| `Dockerfile` | Mirrors the upstream `vendor/Dockerfile` (same pinned images, DbTools step, chiseled non-root runtime); each difference is marked `localtest:` (see [Image](#image)) |

## Directive sites in `vendor/`

Three in `src/Storage/Program.cs`, one in `src/Storage/Controllers/InstancesController.cs`:

1. `SetConfigurationProviders`: `#if !LOCALTEST` around `await ConnectToKeyVaultAndSetApplicationInsights(config)`,
   `#else LocaltestStorageExtensions.AddLocaltestConfiguration(config)`. Runs before any setting is read, so the
   localtest defaults reach the eager reads in `ConfigureServices` (JWT metadata address, connection strings).
2. After `ConfigureWolverine(...)`: `#if LOCALTEST LocaltestStorageExtensions.AddLocaltestStorage(builder) #endif`,
   which replaces registrations after the production ones.
3. `InstancesController.BuildRequestLink`: `#if !LOCALTEST` keeps `https://platform.{Hostname}` as the host of the
   `next` link in paged instance queries; `#else` uses the request's own scheme and host. Apps follow `next` when a
   party has more than one page of instances, and there is no `platform.*` host locally. The `selfLinks.platform`
   values still say `https://platform.local.altinn.cloud/...`: that prefix is hard-coded in the
   `Altinn.Platform.Storage.Interface` package and nothing follows those links.
4. Service discovery (task 25): a pure `#if LOCALTEST` addition right after `WebApplication.CreateBuilder` calls
   `LocaltestServiceDiscovery.AddLocaltestServiceDiscovery` (`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`), so
   factory clients resolve `http://<service>/` from `services__<service>__http__0`; since task 40 without pass-through: a
   name without an entry fails at once, a real host (a dot, an IP, `localhost`) is used as it is ([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)).

**Service names (task 40):** the PDP, the Register and the Dialogporten adapter are called as
`http://authorization/authorization/api/v1/`, `http://register/register/api/v1/` and
`http://dialogporten-adapter/storage/dialogporten/` (fakes' defaults); `GeneralSettings__PlatformEndpoint` stays the
public origin (the self links' host, the JwtCookie metadata). In minimal mode the first two resolve to the front door's
emulation (`http://localtest:8080`), and the adapter has no address, which the outbox client treats as "not running".

Nothing else in the host is hard-wired to Azure: Postgres and Yuniql run as in production, Wolverine uses local
queues in `Development`, Application Insights is never wired (it needs a connection string from Key Vault); OTLP replaces it, see "Telemetry (OTLP)".

## Fakes (`fakes/`, namespace `Altinn.Platform.Storage.Localtest`)

| Replaces | With |
| --- | --- |
| `IBlobRepository` (Azure Blob) | `FileBlobRepository`: `{BlobRootPath}/{org}/{blobStoragePath}` (`{org}-NN` when a storage account number is given), the Azure container layout. Retained blob versions (altinn-storage#1125) are already distinct paths (`…/data-elements/{blobVersionId}` vs legacy `…/data/{dataGuid}`), so no suffix scheme is needed. Correspondence's `FileStorageRepository` follows the same file-backed pattern (`../correspondence/README.md`) |
| `IFileScanQueueClient` (Azure Queue) | `LocaltestFileScanQueueClient`: runs the production `IDataRepository.UpdateFileScanStatus(…, Clean)` at once. Upload responses still say `Pending` (as in production); every later read says `Clean`. The update does not bump the instance version |
| Azure Service Bus queue `altinn.dialogportenadapter.webapi` (the outbox's destination outside `Development`) | `Dialogporten/LocaltestDialogportenSyncHandler`, a Wolverine handler on the `Development` local queue, and `LocaltestDialogportenAdapterClient`, which posts the command to the adapter's anonymous `api/localtest/enqueue` (see [Dialogporten outbox](#dialogporten-outbox)) |
| `IPublicSigningKeyProvider` (Key Vault, throws without a URI) | `LocaltestPublicSigningKeyProvider`: returns the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, see `../../certs/README.md`) for every issuer, so the production `AccessTokenHandler` validates incoming platform access tokens for real. The production `AccessTokenGenerator` signs outgoing ones (to Register) with the same certificate |

### Application metadata and texts

Production repositories on Postgres, nothing else. The app runner (`../../run-app.sh`) pushes
`App/config/applicationmetadata.json` with `POST storage/api/v1/applications?appId={org}/{app}` (`PUT
applications/{org}/{app}` when it exists) and each `App/config/texts/resource.{lang}.json` with
`POST applications/{org}/{app}/texts` (`PUT …/texts/{lang}`), exactly what Designer does on deploy. A service-owner
token is not enough: the production `ClaimAccessHandler` requires `urn:altinn:app=studio.designer`, which the Studio
token (`/Home/GetTestStudioToken` on the localtest app, Designer's own credential exchanged by production Authentication) carries. Storage never
asks the app for anything, and an app that was not pushed is unknown to it (`404` on `GET applications/{org}/{app}`).

No seeding of `storage.applications` is needed for instances: no instance or data SQL joins that table (checked
`Repository/Pg*.cs` and `Migration/FunctionsAndProcedures`; only `texts` and `a2xsls` reference it).

## Configuration

`GeneralSettings:PlatformEndpoint` (default `http://local.altinn.cloud:8000/`) is the base for the defaults below.
They are inserted just below the environment variables, so they override the vendored `appsettings.json`
(localhost ports, at22) while `GeneralSettings__…`, `PlatformSettings__…` etc. still win:

| Key | Default |
| --- | --- |
| `GeneralSettings:OpenIdWellKnownEndpoint` | `{platform}authentication/api/v1/openid/` |
| `PlatformSettings:ApiAuthorizationEndpoint` | `{platform}authorization/api/v1/` |
| `RegisterServiceSettings:ApiRegisterEndpoint` | `{platform}register/api/v1/` |
| `GeneralSettings:Hostname` | host of the platform URL (self links become `https://platform.{host}/…`, unchanged production format) |
| `GeneralSettings:OndemandEndpoint`, `PdfGeneratorEndpoint` | `{platform}storage/api/v1/`, `{platform}pdf/` (A2 only) |
| `GeneralSettings:AppMetadata/TextResource/AppTitleCacheLifeTimeInSeconds` | 30 (production 300/3600/3600) |
| `Localtest:Storage:BlobRootPath` | `/data/blobs` |
| `WolverineSettings:EnableSending` | `true` (upstream's `appsettings.json` has `false`; production turns it on): the outbox runs in every stack |
| `WolverineSettings:LowPriorityDelaySecs` / `HighPriorityDelaySecs` | 2 / 0 (production 60 / 5); `UrgentPriorityDelaySecs` stays 0 |
| `WolverineSettings:EnableA2Migration` | `false` (A2 endpoints are disabled) |
| `Localtest:Storage:DialogportenAdapterEndpoint` | `{platform}storage/dialogporten/`, the adapter's public prefix |
| `Logging:LogLevel:Altinn.Platform.Storage.Localtest.Dialogporten` | `Information` (the host logs at `Warning`), for the one line per command |

Forced (cannot differ without Azure): `kvSetting:SecretUri` empty,
`GeneralSettings:DisableA2Endpoints=true` (SBL bridge clients are no-ops). Platform access tokens are verified against the
shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, mounted from `../../certs`).
`ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) is required: Wolverine uses local queues only in
Development, and JWT metadata over plain http is allowed only there. Postgres connection strings come from the
environment (`PostgreSqlSettings__AdminConnectionString` / `__ConnectionString`, `{0}` is replaced by the password
settings, both `Password` as upstream).

## Dialogporten outbox

Production Storage writes an outbox row in the same transaction as every instance mutation that has instance events
(`PgInstanceMutationRepository`), and `OutboxService` publishes the due rows as `SyncInstanceToDialogportenCommand`
(message identity `Altinn.DialogportenAdapter.SyncInstanceCommand`) and deletes them. Outside `Development` Wolverine
sends the command to the adapter's Azure Service Bus queue; in `Development`, which the container runs as, it routes it
to an in-process sequential local queue named after the message namespace. All of that is vendored code, unchanged; there
is no directive site for it.

The localtest part is in `fakes/Dialogporten/`: `LocaltestDialogportenSyncHandler` (a public `*Handler` class with a
static `Handle`, which Wolverine discovers in the host assembly) takes the command off the local queue and
`LocaltestDialogportenAdapterClient` posts `{ appId, partyId, instanceId, instanceCreatedAt, isMigration }` to
`{DialogportenAdapterEndpoint}api/localtest/enqueue`, anonymous, through the front door. The adapter puts it on its own
queue, where its production handler and retry policies take over (`../dialogporten-adapter/README.md`).

Storage always sends, whether or not the adapter runs; nothing in its configuration depends on the `dialogporten` group.
When the group is off the front door (the localtest container) answers the adapter's route with 503 problem details
("Component not running": `dialogporten-adapter` has no `services__` address); a refused connection, 404 (no route claims the path, so it would reach Storage's own `/storage`), 502 and 504
count the same: one line at
Information, `Dialogporten sync of instance {partyId}/{guid} dropped: the Dialogporten adapter is not running ...`, and
the command is dropped. Nothing is retried and the handler never throws, so Wolverine never retries either. Any other
non-success answer is a warning, also dropped. An instance changed while the adapter was down is synced by its next
change or by hand with the adapter's `syncDialog/simple` endpoint. The adapter's own write-back (`PUT .../datavalues` with
`dialog.id`) produces no instance event and so no outbox row.

## Postgres

One shared Postgres, the stack's `localtest-postgres` image (`../../postgres/Dockerfile`, `postgres:18.3`).
`postgres-init/10-storage.sh` (copied into `/docker-entrypoint-initdb.d` by that image, with
`vendor/infra/postgres_init.sql` at `/storage-init/postgres_init.sql`) creates the `storagedb` database, the
`storage` schema and the roles `platform_storage_admin` / `platform_storage`, independent of `POSTGRES_USER`. Postgres
runs init scripts on an empty volume only. Storage
runs the production Yuniql migrations (`Migration/v0.00` … `v0.38`) itself at startup with the admin connection; a
fresh database is migrated in about a second.

## In the stack

The runtime (studioctl's `localtest-v2`, or the Aspire package) runs Storage on port 5010 (no host port) with the Postgres connection strings (the `localtest-postgres` image
creates `storagedb` and its roles with `postgres-init/10-storage.sh` and `vendor/infra/postgres_init.sql`),
`GeneralSettings__PlatformEndpoint`, the shared certificate at `/certs`
(`AccessTokenSettings__AccessTokenSigningCertificateFullPath`) and a blob volume at `/data/blobs`. Storage reaches the
OpenID metadata, the PDP, Register and the apps through `local.altinn.cloud:8000`, the front door (the localtest
container), which routes `/storage/` to Storage by `services__storage__http__0` without stripping the prefix (Storage's
routes start with `/storage/api/v1/`); see [`../../CONVENTIONS.md`](../../CONVENTIONS.md). To run a change made here:
`cd localtest/components/storage && studioctl env up -r localtest-v2 --dev-storage`.

## Image

The `Dockerfile` follows the upstream one line for line, so the image is what production runs apart from the lines
marked `localtest:`:

- Same pinned `sdk:10.0-resolute` and `aspnet:10.0-resolute-chiseled-extra` images, the same DbTools build (it
  regenerates the latest migration's `01-functions-and-procedures.sql` during publish) and the `Migration/` copy from
  the build stage, `USER $APP_UID` (1654).
- `localtest:` the sources keep the component layout under `/src` (the overrides need `vendor/` next to them),
  `fakes/` and the overrides are copied in, and the two `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`
  variables are set. Like upstream, the vendored root `Directory.Build.props` (CSharpier, CI warnings-as-errors) is
  not in the build context.
- `localtest:` `/data/blobs` owned by uid 1654 as a volume, and the environment production gets from its deployment
  (`ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5010`, the blob root).
- No `HEALTHCHECK`: `podman build` produces OCI images, which have no healthcheck field, so podman drops the
  instruction with a warning. The `HEALTHCHECK` lines in the other components' Dockerfiles have no effect for the same
  reason. A check here would also have to be exec form (`CMD ["program", …]`), because the chiseled image has no shell,
  `wget` or `curl`. The runtimes check `/health` over HTTP from inside the stack's network instead (a probe container under
  studioctl, an `exec` in the Postgres container under Aspire).
- Compose era (until 2026-10-20): a `storage-blobs` volume created by the earlier root-run image was owned by root and
  had to be handed to the app user once: `podman run --rm -v localtest_storage-blobs:/d docker.io/library/postgres:16-alpine chown -R 1654:1654 /d`.
  studioctl's volume is `localtest-v2-storage-blobs`, created for the non-root image.
- Npgsql logs `libgssapi_krb5.so.2: cannot open shared object file` at startup: the chiseled image has no Kerberos
  library, as in production, and nothing uses it.

## Telemetry (OTLP)

The stack's rule (TODO.md task 53): traces, metrics and logs go over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc`, the .NET default, or `http/protobuf`) and `OTEL_SERVICE_NAME`; without it nothing is exported and no export error is logged. A per-signal endpoint alone does not turn it on. Directive site `Program.cs:249-258`: production's Azure Monitor exporters under `#if !LOCALTEST`, `#else` `LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`, a copy of `../shared/fakes/Otlp/`) on the host's own pipeline (ASP.NET Core, HttpClient, Npgsql, `RequestFilterProcessor`, its meters, the logs of `ConfigureApplicationLogging`); `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1 in `localtest.build.targets`. The service name is `platform-storage` unless `OTEL_SERVICE_NAME` is set.

`dotnet build -c Release` of the host with the overrides: 0 errors, no new warnings (2026-10-26). The host needs Postgres to start, so it was not run. Verified on the stack (2026-10-26, task 53's stack proof, images `sha-609c646`, `studioctl env up -r localtest-v2 --with everything`): the root `../../smoke-test.sh` passed with its OTLP check, and the stack's run left 1285 spans, 893 metric series and 24 log records under `service.name` `storage`. Without the monitoring group the container has no `OTEL_*` variable and its log no exporter line. A 150 s capture of SYNs and UDP in its network namespace during the smoke tests showed no lookup of `otel-collector` and nothing to 4317 or 4318.

## Verified

Storage is checked by the root `../../smoke-test.sh` through the front door, and for the outbox by
`../dialogporten-adapter/smoke-test.sh` (with `--off` for the default stack). The component's own standalone
`smoke-test.sh` (host build, fresh Postgres, register, authentication and a PDP stub) was deleted on 2026-10-21 (task 12):
it could not run since 2026-10-20, when the `/Home` token helpers it called moved from Authentication to the localtest
app and the `Localtest__Authentication__RegisterLocaltestEndpoint` setting it set went away, and porting it would have
meant a second stack. The checks the root test lacked moved there: texts read back, the org's application list, and no
`fail`/`crit` lines in Storage's log. What it verified on 2026-10-17 and 2026-10-18, on the host build:

- migrations to `v0.38` run on the fresh database at startup (16 tables);
- `POST applications?appId=ttd/testapp` and `POST applications/ttd/testapp/texts` with a service-owner token
  (`smoke/apps/ttd/testapp`), then `GET` them back from Postgres;
- `POST instances?appId=ttd/testapp` for party 500000 as user 1337 (Register lookup through the register component);
- `GET instances/{partyId}/{guid}`; `POST …/data?dataType=attachment` writes `{org}/{org}/{app}/{instanceGuid}/data-elements/{blobVersionId}`
  in the blob folder and `GET …/data/{id}` returns the bytes; the element's `fileScanResult` is `Clean`;
- `GET instances?org=ttd` with the `altinn:serviceowner/instances.read` token; no errors in the Storage log.

The image (`podman build -t localtest-storage:dev .`) starts against a Postgres container on a podman network,
migrates and answers `/health` 200 in about 3 s.

The image built from upstream `9e72a9f1` (2026-10-19) passes the root `../../smoke-test.sh` (23 checks then, 26 since task 12: real PDP
decisions, instance create, process start and end by the app, upload, read-back, file scan `Clean`, denial, service-owner
listing) with a fresh Postgres and blob volume; blobs are written as uid 1654. The full app-lib lifecycle runs in the
Cypress suite, see `../../README.md`.

Run locally without Docker (Postgres prepared with the init script on 5433):

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
dotnet build -c Release vendor/src/Storage
cd vendor/src/Storage   # Yuniql reads ./Migration relative to the working directory
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5010 \
  PostgreSqlSettings__AdminConnectionString='Host=127.0.0.1;Port=5433;Username=platform_storage_admin;Password={0};Database=storagedb' \
  PostgreSqlSettings__ConnectionString='Host=127.0.0.1;Port=5433;Username=platform_storage;Password={0};Database=storagedb' \
  Localtest__Storage__BlobRootPath=/tmp/storage-blobs \
  dotnet bin/Release/net10.0/Altinn.Platform.Storage.dll
```
