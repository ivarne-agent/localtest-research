# correspondence

Production Altinn Correspondence (`vendor/`, see `vendor/UPSTREAM.md`): the API host, with its background work in Hangfire
inside the same process, on a `correspondence` database in the shared Postgres 18, as the **optional component group
`correspondence`** of the `localtest-v2` runtime. The group implies `dialogporten`, so one flag starts both:

```sh
studioctl env up -r localtest-v2 --with correspondence      # --dev correspondence builds the image from this folder
```

| Container | Image | Host port | Public route | Health |
| --- | --- | --- | --- | --- |
| `correspondence` | `localtest-correspondence` | 8033 (container 2525) | `local.altinn.cloud/correspondence` (unchanged path: the host's routes start with `/correspondence/api/v1`) | `/health` (production's database check; `/healthz` is liveness only) |

Apps reach it at `PlatformSettings:ApiCorrespondenceEndpoint` (`{origin}/correspondence/api/v1/`, which studioctl already
gives them); today only the signing flow (`signering-brukerstyrt`) sends correspondence. Without the group the route answers
`503` "Component not running" and the signing flow swallows the failed send, as it did before (the signee shows "notification
failed"). **With the group, an app's own send still fails** (500) until the 6-digit party-id gap is closed (see "Known gaps"):
the signing flow cannot message its signees yet.

**Status (2026-10-21):** runs on the stack (see "Verified"); the service-owner path end to end, app-lib's credential and the
recipient's dialog-token path blocked by the party-id gap.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (`src/`, `Dockerfile`, `docker-compose.yml`) except two additive `#if LOCALTEST` hooks in `Program.cs` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath` (copied from Notifications): define `LOCALTEST` for the vendored projects and compile `fakes/**/*.cs` into `Altinn.Correspondence.API`. Upstream has no CPM, no `Directory.Build.props`, no analyzers and no warnings as errors |
| `fakes/` | Compiled into the host (namespace `Altinn.Correspondence.Localtest`), below |
| `postgres-init/70-correspondence.sh` | Postgres init script: role `correspondence` (login, not superuser, password `localtest`), database `correspondence` (upstream's own name) owned by it. Idempotent; for an existing volume run it by hand: `podman exec -i localtest-v2-postgres sh < components/correspondence/postgres-init/70-correspondence.sh` |
| `Dockerfile` | Mirrors upstream's `vendor/Dockerfile` (the six-project restore layer, publish, port 2525, the `dotnet` user, `/mnt/storage`); each difference is marked `localtest:`, among them the base images pinned by digest (upstream floats `sdk:10.0`/`aspnet:10.0`) |
| `smoke-test.sh` | Against the stack with the group on, see "Tests" |
| `.dockerignore` | Keeps host `bin/` and `obj/` out of the build context |

## Directive sites

Two pure additions in `vendor/src/Altinn.Correspondence.API/Program.cs` (`vendor/UPSTREAM.md` has the lines), no `#if !LOCALTEST`:

1. After the `.AddEnvironmentVariables();` that ends upstream's configuration chain: `AddLocaltestConfiguration(builder.Configuration)`.
   `Program.cs` adds the JSON files and the environment variables a second time after `CreateBuilder`, so the defaults are
   inserted just below that last environment source (above `appsettings.Development.json`, which points at localhost and tt02),
   before `ConfigureServices` reads them.
2. After `services.ConfigureHangfire(config);`, the last registration but Kestrel's: `AddLocaltestServices(services, config)`, so
   its `RemoveAll`/`Add` win and its authentication `Configure` actions run after upstream's.

A third site (the browser download of attachments through OIDC, `IdportenTokenValidator.cs:26`) is task 11's and needs a report first.

## What is faked, and why

| Production | Here | Why |
| --- | --- | --- |
| `IStorageRepository` (Azure Blob, a storage account per service owner) | `Storage/FileStorageRepository.cs`: files at `/mnt/storage/attachments/{id}` (the `localtest-v2-correspondence-blobs` volume) and `reports/{name}` with a `.meta.json` sidecar for the report's two counts and hash. The MD5 of the stream as lower-case hex, `HashMismatchException` and nothing kept on a checksum mismatch, `RequestFailedException` 404 for a missing blob, as production. The location URL and the "SAS" URL are `{origin}/correspondence/api/localtest/blobs/{id}` | No Azure Storage; the Storage component's file-backed pattern |
| `IResourceManager` (Azure Resource Manager) | `AzureResources/LocaltestResourceManager.cs`: deploying storage accounts is a no-op, there are no IP ranges | The daily "Update IP restrictions" job would otherwise fail ten Hangfire retries on `DefaultAzureCredential`; now it logs one line |
| `IMaskinportenService` (the package hard-codes `maskinporten.no`) | `Maskinporten/`, a copy of `../shared/fakes/Maskinporten/` | Production semantics: an unsigned assertion to the localtest app's Maskinporten token endpoint, then production Authentication's `exchange/maskinporten`. Client id `991825827` with `UseAltinnTestOrg` false, so Correspondence acts as `digdir`, as Dialogporten does. KRR gets the raw token (no exchange), as in production |
| `JwtBearerOptions` / `OpenIdConnectOptions` `RequireHttpsMetadata` (true) | `Auth/LocaltestAuthentication.cs`: `ConfigureAll` to false | Authentication, the fake Maskinporten and Dialogporten publish their metadata over http. A `Configure`, not a `PostConfigure`, see the Dialogporten README |
| The Maskinporten scheme's metadata (hard-coded `https://test.maskinporten.no/...` outside Production) | The fake Maskinporten's `{origin}localtest/api/maskinporten/.well-known/oauth-authorization-server` | Notifications' condition check (`{id}/notification/check`) presents a raw fake-Maskinporten token |
| The dialog-token keys (`EdDsaSecurityKeysCacheService`: Dialogporten's JWKS at start and every 12 hours) | `Auth/LocaltestDialogTokenKeys.cs`: the Dialogporten scheme's `IssuerSigningKeyResolver` fetches the JWKS on the first token and again when a token names an unknown key, at most every 10 s (`Localtest:Correspondence:DialogTokenKeyRefreshSeconds`). Upstream's service still runs; its list is no longer read | A Correspondence that starts before Dialogporten serves its keys would refuse every dialog token for 12 hours |
| Settings from the deployment | `LocaltestCorrespondence.AddLocaltestConfiguration`, below | |
| nothing | `Inspection/`: `correspondence/api/localtest/`, below | A failing dependency shows up as a Hangfire job, not as an HTTP error |

Not replaced, because production code serves them through the stack: Dialogporten (service-owner API, transmissions,
activities, purge), Notifications (`future/orders`, status, shipments), Register (the `correspondence/parties/...` lookups),
Profile's two `correspondence/` lookups (in the Register component's fakes), the PDP, the Resource Registry, Events, KRR (a
stand-in in the Register component's fakes, through the production client), Slack (upstream's dev client logs).
Malware scanning is upstream's own Development simulation ("No threats found", through the production
`MalwareScanResultHandler`).

## Configuration

One setting comes from studioctl, `Localtest__Correspondence__PlatformEndpoint` (the public origin); the connection string and
Redis are set explicitly too. Everything else is a default the real setting overrides. With `{platform}` that endpoint:

| Key | Default |
| --- | --- |
| `DatabaseOptions:ConnectionString` | `Host=postgres;Port=5432;Database=correspondence;Username=correspondence;Password=localtest` (a password keeps Entra ID out) |
| `AttachmentStorageOptions:ConnectionString` | `UseDevelopmentStorage=true` (required by the options class, never read) |
| `AltinnOptions:PlatformGatewayUrl`, `OpenIdWellKnown`, `ArbeidsflateOriginsCommaSeparated` | `{platform}`, Authentication's OpenID configuration, `http://af.local.altinn.cloud:8000` |
| `DialogportenSettings:Issuer`, `IdportenSettings:Issuer` | `{platform}dialogporten/api/v1`, `{platform}localtest/api/idporten` (compared with the token's `iss` exactly) |
| `MaskinportenSettings:ClientId`, `Environment`, `EncodedJwk`, `UseAltinnTestOrg`, `ExhangeToAltinnToken` | `991825827` (a client id is what selects the production clients instead of upstream's dev stubs), `test`, `e30=` (never read), `false`, `true`; `Scope` stays upstream's |
| `Localtest:Maskinporten:Issuer`, `ExchangeEndpoint` | `{platform}localtest/api/maskinporten/`, `{platform}authentication/api/v1/exchange/maskinporten` |
| `GeneralSettings:CorrespondenceBaseUrl` | `{platform}`: the URLs in dialogs, notification condition endpoints and event sources (not localhost, or the condition endpoint is dropped) |
| `GeneralSettings:RedisConnectionString` | `redis:6379,defaultDatabase=1` |
| `GeneralSettings:ContactReservationRegistryBaseUrl` | `{platform}register/api/localtest/krr/` |
| `GeneralSettings:DisableCallsToAltinn2`, `EnableDownloadAll`, `ArbeidsflateBaseUrl` | `true`, `true`, `http://af.local.altinn.cloud:8000` |
| `GeneralSettings:WorkerCountPerReplica`, `MigrationWorkerCountPerReplica` | `2`, `0` (production 15 and a 4-worker migration server) |
| `GeneralSettings:ApplicationInsightsConnectionString` | forced empty |
| `Logging:LogLevel:*` | `Information`, with EF commands and Npgsql at `Warning` and EF migrations at `Information` (the Development file logs every SQL command at Debug) |
| `Localtest:Correspondence:BlobRootPath` | `/mnt/storage` (the directory upstream's image creates for `dotnet`) |

`ASPNETCORE_ENVIRONMENT=Development` comes from the Dockerfile: it applies the migrations at start, simulates the malware scan
and skips the storage account deployment.

## Redis, shared with Dialogporten

Correspondence connects to Redis eagerly at start (`ConnectionMultiplexer.Connect` for the Data Protection keys,
`Program.cs:140`) and aborts without it; with `abortConnect=false` and no Redis every HybridCache and OIDC-state call waits
5 seconds and fails. So it uses the Redis of the `dialogporten` group, database 1 (Dialogporten uses 0). That is one reason
the group implies `dialogporten`.

## Dialogporten is required

The publish job marks a correspondence `Failed` when it has no dialog ("Dialogporten dialog not created",
`PublishCorrespondenceHandler.cs`); the dialog is created by an earlier Hangfire job. Correspondence also validates dialog
tokens against Dialogporten's JWKS. It therefore never runs without the Dialogporten group, and studioctl starts it after the
Dialogporten WebApi is healthy (migrated, JWKS served). A dialog that cannot be created leaves the correspondence
`Initialized` for a long time: Hangfire's default `AutomaticRetry` (10 attempts with growing delays) runs first; the
`jobs` view shows it.

Correspondence acts as `digdir` towards Dialogporten (its own Maskinporten client), apps act as `ttd`: the Dialogporten
pain point (a `ttd` service owner's search does not find dialogs of the shared organisation number, see the Dialogporten
README) applies; read a dialog by id.

## Inspection API (`correspondence/api/localtest/`, anonymous, read-only)

| Call | Returns |
| --- | --- |
| `GET correspondences?after=&resourceId=&sender=&recipient=&status=&limit=` | `{ items, next }` in creation order: `id`, `resourceId`, `sender`, `recipient`, `sendersReference`, `title`, the current `status` and `statusChanged`, `dialogId`, `transmissionId`, `attachmentCount`, `notificationCount`, `created`, `requestedPublishTime`, `published`. `sender`/`recipient` match the end of the identifier (`991825827`, `01839012345`); `limit` 1 to 1000, default 100; `after` is the `next` of the previous page |
| `GET correspondences/{id}` | the correspondence with its status history, content, attachments (status history, size, checksum, the `blob` link), notifications (order and shipment ids, `notificationSent`, reminder flag, `captured`: the Notifications capture's `messages?orderId=` link), external references, the `dialog` link (Dialogporten's service-owner API), reply options, forwarding events, and the **Hangfire jobs** whose arguments mention the id (method, state, reason, retry count, last exception) |
| `GET jobs?state=&limit=` | Hangfire jobs newest first, read from the `hangfire` tables; `state` is `Failed`, `Scheduled`, `Processing`, `Enqueued`, `Succeeded`, `Deleted` or `Awaiting`. Replaces the Hangfire dashboard, which would need a third hook |
| `GET status` | `migrations` (`known` in the assembly, `applied`, `latest`, `pending`), `hangfireServers`, `dialogTokenKeys` (the JWKS URL, the key ids fetched, the last fetch, and the count in upstream's unused start-time cache) |
| `GET blobs/{id}` | an attachment's bytes |
| `GET reports/{name}` | a daily report file |

The inspection GUI (task 8) would use `correspondences` as the list and `correspondences/{id}` as the drill-down.

## Where a sent correspondence shows

1. Here: `correspondence/api/localtest/correspondences`, and production's `GET correspondence/api/v1/correspondence/{id}/details`
   with the sender's token. Swagger is on in Development (path under the proxy not checked).
2. Dialogporten: the dialog by id through the service-owner API, the end-user API with a code-flow token (task 2), later
   Arbeidsflate (task 11).
3. Notifications: `notifications/api/localtest/inboxes` (Sophie Salt's e-mail, `1337@altinnstudiotestusers.com`, creator
   `digdir`, `sendersReference` `corr-…`).
4. Events: `events/api/localtest/` (Correspondence's `app.correspondence.*` events; not verified that Events accepts them).

## Postgres and data

The 69 EF migrations run at every start as the database owner (`MigrateWithLock`, Development only); Hangfire creates its own
`hangfire` schema. `hstore` is created by a migration (trusted); `pg_stat_statements`, `pg_cron` and `pgaudit` are skipped by the
migrations' own exception blocks. **`MigrateWithLock` swallows every exception** and only prints `Migration failed: ...`; the
host then starts and `/health` still says Healthy. The `status` call and the smoke test therefore check the migration count.

**No reset on start**: correspondences and their dialogs must stay in step, as Storage's instances and their dialogs. The only
reset is `studioctl env reset -r localtest-v2`, which removes the shared Postgres volume and the
`localtest-v2-correspondence-blobs` volume.

## Differences from production

- `Development`: token lifetime is not validated on any scheme (`ValidateLifetime = !IsDevelopment()`), internal endpoints are in
  Swagger, problem details carry exception messages, migrations run at start.
- Malware scanning is simulated "clean"; there is no infected-file path.
- Blobs are files on one volume; SAS URLs are plain anonymous localtest URLs; there are no per-service-owner storage accounts and
  no `initialize-service-owner` is needed (without a service-owner row the default store is used, as in production).
- KRR answers "not reserved" for every seeded person; unknown persons get no entry (`../register/README.md`).
- Correspondence acts as `digdir`, apps as `ttd`; the seeded tt02 policies add `ttd` beside `digdir` in the service-owner rule
  (`../resource-registry/README.md`).
- Dialog-token keys are fetched again on a key miss instead of every 12 hours.
- Hangfire runs 2 workers and no migration server; the dashboard is not mapped (it is not in production either).
- The fake Maskinporten's issuer contains "altinn" but not "maskinporten.no", so the `Sender` and `Recipient` policies classify a
  raw fake-Maskinporten token as an Altinn token: with `altinn:correspondence.write` it passes `Sender` without
  `altinn:serviceowner`, which production would demand. No consumer in the stack sends one; Notifications' condition check uses
  the Maskinporten scheme and its scope requirement, not those policies.
- Azure Resource Manager, Key Vault (the JWK rotation is off by default), Slack, Application Insights and the Altinn 2 bridge are off.
- The 10-minute Resource Registry cache and the 24-hour Register party cache are production's (HybridCache over Redis): a renamed
  party in `testData.json` is not seen for up to 24 hours unless Redis database 1 is flushed and the container restarted.

## Faster than production

The rows in `../../README.md`, "Where localtest is faster than production": the dialog-token keys, the simulated malware scan,
upstream's non-production reminder, confidential-check and attachment-expiry values, and the captured notifications. Not faster:
Hangfire's 5 s schedule polling, its retry schedule, Correspondence's own caches.

## Known gaps

- **6-digit party ids (test data) against production's party-id rule.** Every PDP request Correspondence builds
  (`AltinnAuthorizationService.CreateDecisionRequest` → `XacmlRequestFactory.CreateResourceCategory`) looks the party up in
  Register and names it by its party id, and production accepts as a party id only an 8- or 10-digit number
  (`StringExtensions.IsPartyId` in `Altinn.Correspondence.Common`). The test data's party ids have 6 digits (Digdir 500006,
  Sophie Salt 501337), so the request throws and the caller gets **500** "RecipientId is not a valid organization number, person
  number or party id: 500006". What fails:
  - **app-lib's credential** (`/Home/GetTestOrgToken` with `altinn:correspondence.write` but without `altinn:serviceowner`) on
    `POST correspondence` and `POST attachment`: the sender check goes to the PDP. So `signering-brukerstyrt`'s call-to-sign and
    receipt cannot succeed until the gap is closed (the signee keeps "notification failed");
  - **the recipient's dialog-token path**: the dialog token is accepted (the keys are fetched on the miss), then `content`,
    the attachment download, `markasread`, `confirm` and the recipient's purge answer 500 (Sophie Salt's 501337).

  Workaround in `smoke-test.sh`: the sender uses a service owner's direct token with `altinn:serviceowner` (as a service owner
  calling with Maskinporten would), which production lets past the PDP when the token's consumer owns the resource
  (`EvaluateBypassConditions`); the two blocked paths are reported as `KNOWN`. No directive and no seed change: the cause and the
  fix are under research (the seed's party ids are used across the stack and the Cypress specs).
- **An existing Postgres volume** predates `70-correspondence.sh`, which Postgres runs on an empty volume only: the host then fails
  with `28P01 password authentication failed for user "correspondence"`. Run `studioctl env reset -r localtest-v2 --yes` (or the
  script by hand, see the table at the top) once.
- The browser download of attachments from Arbeidsflate (the OIDC branch of the `AllSchemes` cascade) does not work: a third
  directive site, an OIDC client and a cookie decision, task 11. A download with a dialog token as `Authorization` header goes
  through the token validation (and then meets the party-id gap).
- Organisation recipients are not exercised by the smoke test (DDG Fitness and Digdir have official addresses since test data v3).
- The image is not pinned in `config/images.go` to a build that exists until this branch is on main and the tag is bumped; until
  then use `--dev correspondence` or `STUDIOCTL_IMAGE_LOCALTEST_V2_CORRESPONDENCE`.

## Tests

`./smoke-test.sh [BASE]` needs the stack with `--with correspondence` and checks, in order: `/health` on the host port and all 69
migrations (the `status` call, and in the database itself when `podman` reaches `localtest-v2-postgres`, plus the owner role
being no superuser); 401 without a token, 403 without `altinn:correspondence.write`, 400 `CORR-01036` for an unknown resource;
app-lib's credential (`KNOWN` while the party-id gap lasts); with a service owner's token: `POST attachment`, the upload with its
MD5, `Published` after the simulated scan, the bytes in `blobs/{id}`; a wrong checksum (400, `Failed` with "Checksum mismatch", no
blob); `POST correspondence` to Sophie Salt with the attachment and an e-mail notification to `Published`, the dialog in
Dialogporten (resource, party, content URL), the status history and Hangfire jobs; the e-mail captured and `Delivered`;
`notification/check` with a raw fake-Maskinporten token (the Maskinporten scheme's metadata override) and without one; an unknown
recipient (404); the reserved person Rik Forelder (KRR `JA`): alone 422 `CORR-01030`, with a second recipient `Reserved`; the
recipient's path with a dialog token from task 2's code-flow token (`dialogToken` read; `content`, download, `markasread`,
`confirm`, purge are `KNOWN` while the gap lasts); the sender's purge of a published correspondence refused (`CORR-01013`); a
publish 5 minutes ahead (`ReadyForPublish`, its job under `jobs?state=Scheduled`), purged by the sender (`PurgedByAltinn`); the
inspection API's paging and job list. `CORRESPONDENCE_RESTART=1` restarts the container and checks that the data remain and no
migration runs again. The summary line counts `ok` and `KNOWN`; a `FAIL` makes it exit 1.

## Verified

2026-10-21, on main cc84b1d (pin `sha-55ce57f`) with this branch on top, `studioctl env up -r localtest-v2 --with correspondence
--dev correspondence --dev register --dev resource-registry --dev postgres` (the rest from the pinned images):

- Image build and start: on a fresh Postgres volume (`env reset`) the baked-in `70-correspondence.sh` created the role and
  database and the host applied all 69 EF migrations on Postgres 18.3 as the non-superuser owner; Hangfire created its schema.
  On the earlier volume the host failed with `28P01` until the script ran (see "Known gaps").
- `smoke-test.sh` with `CORRESPONDENCE_RESTART=1`: 46 ok, 2 `KNOWN` (the party-id gap), 0 FAIL, three times (before and after the
  machine restart, the last after the `ttd-localtest-correspondence` policy fix). Seen on the way: the publish needs the dialog
  job first (statuses Initialized, ReadyForPublish, Published within seconds); the notification goes to Notifications as
  `future/orders` with creator `digdir`, `sendersReference` `corr-…`, and is captured and `Delivered`; the dialog-token keys are
  fetched on the first token (`localtest-primary`, `localtest-secondary`).
- With the group on: `../register/smoke-test.sh` 34 ok (27 + 7 for KRR and the `correspondence/` lookups),
  `../resource-registry/smoke-test.sh` 41 ok (35 + 6), `../notifications/smoke-test.sh` 64 ok, `../dialogporten/smoke-test.sh`
  60 ok, the root `../../smoke-test.sh` 23 ok.
- With the group off: `/correspondence/...` answers `503` problem details (`component` and `group` `correspondence`, the `--with`
  command) and, for `Accept: text/html`, the same as an HTML page; `../../run-platform-regression.sh --no-apps` 44 of 44
  (apps with `studioctl run --dev-frontend`, the monorepo's built frontend served on 8080 with `Access-Control-Allow-Origin: *`,
  6 minutes, no retries).
- Memory: `podman stats --no-stream` 120 to 122 MB for the Correspondence container after the smoke test; the whole stack with
  the correspondence and dialogporten groups left about 3.2 to 4 GB of the machine's 7.8 GB available.

Not verified: `signering-brukerstyrt`'s call-to-sign and receipt through Correspondence (blocked by the party-id gap; its Cypress
spec passes with the group off, where the send is swallowed; with the group on one run failed before the signing step, on the
party selection, and was not investigated further); organisation recipients; the Events component accepting Correspondence's
events (the publish jobs succeeded, the events were not looked up); Swagger under the proxy.

## Running without Docker

```sh
mkdir -p /tmp/corr && cd /tmp/corr && echo '{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}' > global.json
C=<this folder>; DirectoryBuildPropsPath=$C/localtest.build.props DirectoryBuildTargetsPath=$C/localtest.build.targets \
  dotnet build -c Release $C/vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj
```

Upstream has no `global.json`, so build from a directory with one (or the newest installed SDK, possibly a preview, is used).
Run `bin/Release/net10.0/Altinn.Correspondence.API.dll` with `ASPNETCORE_ENVIRONMENT=Development`,
`DatabaseOptions__ConnectionString`, `GeneralSettings__RedisConnectionString` and `Localtest__Correspondence__BlobRootPath`.
