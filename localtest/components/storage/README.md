# storage

Production Altinn Storage (`vendor/`, see `UPSTREAM.md`) on a real Postgres, with Azure Blob replaced by files on a
volume, the file-scan queue replaced by an immediate "clean" verdict, Key Vault switched off. Application metadata
and texts arrive through the production endpoints, pushed by the app runner.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three `LOCALTEST` sites, two in `Program.cs` and one in `InstancesController.cs` (`git diff` against the baseline commit shows them); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import the vendored `Directory.Build.props` when it is there (not in the image), define `LOCALTEST`, bypass CSharpier, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): file blob repository, file-scan client, signing-key provider, DI and configuration composition |
| `postgres-init/10-storage.sh` | Postgres init script: creates `platform_storage_admin`, runs the vendored `infra/postgres_init.sql` unchanged, hands the database and schema to the admin role |
| `Dockerfile` | Mirrors the upstream `vendor/Dockerfile` (same pinned images, DbTools step, chiseled non-root runtime); each difference is marked `localtest:` (see [Image](#image)) |
| `smoke-test.sh`, `smoke/` | Standalone smoke test of the host build, not the image (fresh Postgres container, register, authentication, a PDP stub, Storage) |

## Directive sites in `vendor/`

Two in `src/Storage/Program.cs`, one in `src/Storage/Controllers/InstancesController.cs`:

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

Nothing else in the host is hard-wired to Azure: Postgres and Yuniql run as in production, Wolverine uses local
queues in `Development`, Application Insights is only wired when Key Vault yields a connection string.

## Fakes (`fakes/`, namespace `Altinn.Platform.Storage.Localtest`)

| Replaces | With |
| --- | --- |
| `IBlobRepository` (Azure Blob) | `FileBlobRepository`: `{BlobRootPath}/{org}/{blobStoragePath}`, the Azure container layout. Retained blob versions (altinn-storage#1125) are already distinct paths (`…/data-elements/{blobVersionId}` vs legacy `…/data/{dataGuid}`), so no suffix scheme is needed |
| `IFileScanQueueClient` (Azure Queue) | `LocaltestFileScanQueueClient`: runs the production `IDataRepository.UpdateFileScanStatus(…, Clean)` at once. Upload responses still say `Pending` (as in production); every later read says `Clean`. The update does not bump the instance version |
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

Forced (cannot differ without Azure): `kvSetting:SecretUri` empty, `WolverineSettings:EnableSending=false`,
`GeneralSettings:DisableA2Endpoints=true` (SBL bridge clients are no-ops). Platform access tokens are verified against the
shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, mounted from `../../certs`).
`ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) is required: Wolverine uses local queues only in
Development, and JWT metadata over plain http is allowed only there. Postgres connection strings come from the
environment (`PostgreSqlSettings__AdminConnectionString` / `__ConnectionString`, `{0}` is replaced by the password
settings, both `Password` as upstream).

## Postgres

One shared `postgres:16` service. `postgres-init/10-storage.sh` (mounted into `/docker-entrypoint-initdb.d`, with
`vendor/infra/postgres_init.sql` mounted at `/storage-init/postgres_init.sql`) creates the `storagedb` database, the
`storage` schema and the roles `platform_storage_admin` / `platform_storage`, independent of `POSTGRES_USER`. Storage
runs the production Yuniql migrations (`Migration/v0.00` … `v0.38`) itself at startup with the admin connection; a
fresh database is migrated in about a second.

## In the stack

studioctl's `localtest-v2` runtime runs Storage with the Postgres connection strings (the `localtest-postgres` image
creates `storagedb` and its roles with `postgres-init/10-storage.sh` and `vendor/infra/postgres_init.sql`),
`GeneralSettings__PlatformEndpoint`, the shared certificate at `/certs`
(`AccessTokenSettings__AccessTokenSigningCertificateFullPath`) and a blob volume at `/data/blobs`. Storage reaches the
OpenID metadata, the PDP, Register and the apps through `local.altinn.cloud:8000`, which studioctl-server serves;
studioctl-server routes `/storage/` to Storage without stripping the prefix (Storage's routes start with
`/storage/api/v1/`). To run a change made here: `STUDIOCTL_LOCALTEST_V2_SRC=<this repository>/localtest studioctl env up
-r localtest-v2 --dev storage`.

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
  `wget` or `curl`. studioctl polls `/health` over HTTP instead.
- A `storage-blobs` volume created by the earlier root-run image is owned by root and has to be handed to the app
  user once: `podman run --rm -v localtest_storage-blobs:/d docker.io/library/postgres:16-alpine chown -R 1654:1654 /d`.
- Npgsql logs `libgssapi_krb5.so.2: cannot open shared object file` at startup: the chiseled image has no Kerberos
  library, as in production, and nothing uses it.

## Verified

`./smoke-test.sh` (fresh `postgres:16-alpine` via podman, register on 5020, authentication on 8000, a Python stub on
5050 that permits every PDP decision, Storage on 5010), as of the fetch-based version; the metadata rows below now
describe the pushed flow verified by the root `../../smoke-test.sh`:

- migrations to `v0.38` run on the fresh database at startup (16 tables);
- `POST applications?appId=ttd/testapp` and `POST applications/ttd/testapp/texts` with a service-owner token
  (`smoke/apps/ttd/testapp`), then `GET` them back from Postgres;
- `POST instances?appId=ttd/testapp` for party 500000 as user 1337 (Register lookup through the register component);
- `GET instances/{partyId}/{guid}`; `POST …/data?dataType=attachment` writes `{org}/{org}/{app}/{instanceGuid}/data-elements/{blobVersionId}`
  in the blob folder and `GET …/data/{id}` returns the bytes; the element's `fileScanResult` is `Clean`;
- `GET instances?org=ttd` with the `altinn:serviceowner/instances.read` token; no errors in the Storage log.

The image (`podman build -t localtest-storage:dev .`) starts against a Postgres container on a podman network,
migrates and answers `/health` 200 in about 3 s.

The image built from upstream `9e72a9f1` (2026-10-19) passes the root `../../smoke-test.sh` (23 checks: real PDP
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
