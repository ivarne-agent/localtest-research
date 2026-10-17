# storage

Production Altinn Storage (`vendor/`, see `UPSTREAM.md`) on a real Postgres, with Azure Blob replaced by files on a
volume, the file-scan queue replaced by an immediate "clean" verdict, Key Vault switched off, and application
metadata and texts read from the running app.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `LOCALTEST` sites in `Program.cs` (`git diff` against the baseline commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, bypass CSharpier, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): file blob repository, file-scan client, app metadata/text source, Key Vault and token stand-ins, DI and configuration composition |
| `postgres-init/10-storage.sh` | Postgres init script: creates `platform_storage_admin`, runs the vendored `infra/postgres_init.sql` unchanged, hands the database and schema to the admin role |
| `Dockerfile` | Publishes the vendored host with the overrides; copies `Migration/` next to it as upstream does |
| `smoke-test.sh`, `smoke/` | Standalone smoke test (fresh Postgres container, register, authentication, a PDP/app stub, Storage) |

## Directive sites in `vendor/`

Both in `src/Storage/Program.cs`:

1. `SetConfigurationProviders`: `#if !LOCALTEST` around `await ConnectToKeyVaultAndSetApplicationInsights(config)`,
   `#else LocaltestStorageExtensions.AddLocaltestConfiguration(config)`. Runs before any setting is read, so the
   localtest defaults reach the eager reads in `ConfigureServices` (JWT metadata address, connection strings).
2. After `ConfigureWolverine(...)`: `#if LOCALTEST LocaltestStorageExtensions.AddLocaltestStorage(builder) #endif`,
   which replaces registrations after the production ones.

Nothing else in the host is hard-wired to Azure: Postgres and Yuniql run as in production, Wolverine uses local
queues in `Development`, Application Insights is only wired when Key Vault yields a connection string.

## Fakes (`fakes/`, namespace `Altinn.Platform.Storage.Localtest`)

| Replaces | With |
| --- | --- |
| `IBlobRepository` (Azure Blob) | `FileBlobRepository`: `{BlobRootPath}/{org}/{blobStoragePath}`, the Azure container layout. Retained blob versions (altinn-storage#1125) are already distinct paths (`…/data-elements/{blobVersionId}` vs legacy `…/data/{dataGuid}`), so no suffix scheme is needed |
| `IFileScanQueueClient` (Azure Queue) | `LocaltestFileScanQueueClient`: runs the production `IDataRepository.UpdateFileScanStatus(…, Clean)` at once. Upload responses still say `Pending` (as in production); every later read says `Clean`. The update does not bump the instance version |
| `IApplicationRepository` | `LocaltestApplicationRepository`: running app → mounted folder → Postgres (`PgApplicationRepository`, which keeps what was POSTed). Writes go to Postgres |
| `ITextRepository` | `LocaltestTextRepository`: same order; `Create` inserts the application row from the app source first when Postgres does not have it (the production insert needs the foreign key) |
| `IPublicSigningKeyProvider` (Key Vault, throws without a URI) | `LocaltestPublicSigningKeyProvider`: returns the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, see `../../certs/README.md`) for every issuer, so the production `AccessTokenHandler` validates incoming platform access tokens for real. The production `AccessTokenGenerator` signs outgoing ones (to Register) with the same certificate |
| PEP `ClaimAccessHandler` | same rule, plus a service-owner token (`urn:altinn:org`, e.g. `/Home/GetTestOrgToken`) satisfies `urn:altinn:app=studio.designer`, the policy on POST/PUT/DELETE of applications and texts. Replaced, not complemented, because the production handler calls `Fail()` |

### Application metadata and texts

The app source (`LocaltestAppSource`) asks the running app through the platform base URL, the same app-lib endpoints
today's localtest uses: `{AppBaseUrl}{org}/{app}/api/v1/applicationmetadata?checkOrgApp=false` and
`{AppBaseUrl}{org}/{app}/api/v1/texts/{lang}` (5 s timeout). If the app does not answer it reads
`{AppsPath}/{org}/{app}/applicationmetadata.json` and `{AppsPath}/{org}/{app}/texts/resource.{lang}.json`. Hits are
cached 30 s (`Localtest:Storage:AppCacheSeconds`), misses 2 s, so a starting app is picked up quickly. If neither
knows the app, Postgres answers (metadata pushed with `POST storage/api/v1/applications`, as Designer does).

No seeding of `storage.applications` is needed for instances: no instance or data SQL joins that table (checked
`Repository/Pg*.cs` and `Migration/FunctionsAndProcedures`; only `texts` and `a2xsls` reference it). Listing
(`GET applications`, `GET applications/{org}`) returns Postgres rows plus folder apps, each answered from the app
source when it can. Running apps are not discovered: an app that is neither in the folder nor in Postgres appears in
`GET applications/{org}/{app}` but not in the lists. `GetAllAppTitles` (messagebox) reads Postgres only.

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
| `Localtest:Storage:AppBaseUrl` | `{platform}` |
| `Localtest:Storage:AppsPath`, `BlobRootPath` | `/apps`, `/data/blobs` |

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

## docker-compose

```yaml
services:
  postgres:
    image: postgres:16
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    volumes:
      - postgres-data:/var/lib/postgresql/data
      - ./components/storage/postgres-init/10-storage.sh:/docker-entrypoint-initdb.d/10-storage.sh:ro
      - ./components/storage/vendor/infra/postgres_init.sql:/storage-init/postgres_init.sql:ro
    healthcheck:
      test: ["CMD", "pg_isready", "-U", "postgres", "-d", "storagedb"]
      interval: 2s
      retries: 30

  storage:
    build: ./components/storage
    environment:
      GeneralSettings__PlatformEndpoint: http://local.altinn.cloud:8000/
      PostgreSqlSettings__AdminConnectionString: Host=postgres;Port=5432;Username=platform_storage_admin;Password={0};Database=storagedb
      PostgreSqlSettings__ConnectionString: Host=postgres;Port=5432;Username=platform_storage;Password={0};Database=storagedb;Include Error Detail=True
    volumes:
      - storage-blobs:/data/blobs
      # Optional fallback when the app is not running: {org}/{app}/applicationmetadata.json and texts/resource.{lang}.json
      # - ./apps:/apps:ro
    depends_on:
      postgres:
        condition: service_healthy
    ports:
      - "5010:5010"

volumes:
  postgres-data:
  storage-blobs:
```

Inside the compose network `local.altinn.cloud:8000` must reach the ingress (for example a network alias
`local.altinn.cloud` on the ingress service listening on 8000), because Storage fetches the OpenID metadata, calls
the PDP, Register and the apps through it. Otherwise set `GeneralSettings__PlatformEndpoint` to the ingress service
URL. The ingress routes `/storage/` to `storage:5010` (Storage's routes already start with `/storage/api/v1/`; do
not strip the prefix).

## Verified

`./smoke-test.sh` (fresh `postgres:16-alpine` via podman, register on 5020, authentication on 8000, a Python stub on
5050 that permits every PDP decision and serves `ttd/liveapp` as a running app, Storage on 5010), all passing:

- migrations to `v0.38` run on the fresh database at startup (16 tables);
- `GET applications/ttd/testapp` from the mounted folder, `GET applications/ttd/liveapp` from the "running app";
- `POST applications` with a service-owner token, then `GET` it back from Postgres, `GET applications/ttd` lists both;
- `GET applications/{org}/{app}/texts/nb` from the folder and from the running app;
- `POST instances?appId=ttd/testapp` for party 500000 as user 1337 (Register lookup through the register component);
- `GET instances/{partyId}/{guid}`; `POST …/data?dataType=attachment` writes `{org}/{org}/{app}/{instanceGuid}/data-elements/{blobVersionId}`
  in the blob folder and `GET …/data/{id}` returns the bytes; the element's `fileScanResult` is `Clean`;
- `GET instances?org=ttd` with the `altinn:serviceowner/instances.read` token; no errors in the Storage log.

The image (`podman build -t localtest-storage:dev .`) starts against a Postgres container on a podman network,
migrates and answers `/health` 200 in about 4 s.

Not verified: real PDP decisions (the authorization component did not exist yet; the stub permits everything),
the full app-lib lifecycle (process next, instance mutations, signing) and the route through an ingress.

Run locally without Docker (Postgres prepared with the init script on 5433):

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
dotnet build -c Release vendor/src/Storage
cd vendor/src/Storage   # Yuniql reads ./Migration relative to the working directory
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5010 \
  PostgreSqlSettings__AdminConnectionString='Host=127.0.0.1;Port=5433;Username=platform_storage_admin;Password={0};Database=storagedb' \
  PostgreSqlSettings__ConnectionString='Host=127.0.0.1;Port=5433;Username=platform_storage;Password={0};Database=storagedb' \
  Localtest__Storage__BlobRootPath=/tmp/storage-blobs Localtest__Storage__AppsPath=$HOME/apps \
  dotnet bin/Release/net10.0/Altinn.Platform.Storage.dll
```
