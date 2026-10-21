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
failed"). With the group, the signing flow messages its signees and sends its receipts through production Correspondence.

**Status (2026-10-21):** runs on the stack (see "Verified"): the service-owner path, app-lib's credential and the recipient's
dialog-token path end to end, and `signering-brukerstyrt`'s call-to-sign and receipt. The test data's 6-digit party ids pass
the PDP request factory through one `#if !LOCALTEST` site (task 7b, see "Directive sites").

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (`src/`, `Dockerfile`, `docker-compose.yml`) except two additive `#if LOCALTEST` hooks in `Program.cs` and one `#if !LOCALTEST` block in `XacmlRequestFactory.cs` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath` (copied from Notifications): define `LOCALTEST` for the vendored projects and compile `fakes/**/*.cs` into `Altinn.Correspondence.API`. Upstream has no CPM, no `Directory.Build.props`, no analyzers and no warnings as errors |
| `fakes/` | Compiled into the host (namespace `Altinn.Correspondence.Localtest`), below |
| `postgres-init/70-correspondence.sh` | Postgres init script: role `correspondence` (login, not superuser, password `localtest`), database `correspondence` (upstream's own name) owned by it. Idempotent; for an existing volume run it by hand: `podman exec -i localtest-v2-postgres sh < components/correspondence/postgres-init/70-correspondence.sh` |
| `Dockerfile` | Mirrors upstream's `vendor/Dockerfile` (the six-project restore layer, publish, port 2525, the `dotnet` user, `/mnt/storage`); each difference is marked `localtest:`, among them the base images pinned by digest (upstream floats `sdk:10.0`/`aspnet:10.0`) |
| `smoke-test.sh` | Against the stack with the group on, see "Tests" |
| `.dockerignore` | Keeps host `bin/` and `obj/` out of the build context |

## Directive sites

Two pure additions in `vendor/src/Altinn.Correspondence.API/Program.cs` and one `#if !LOCALTEST` block (`vendor/UPSTREAM.md` has the
lines):

1. After the `.AddEnvironmentVariables();` that ends upstream's configuration chain: `AddLocaltestConfiguration(builder.Configuration)`.
   `Program.cs` adds the JSON files and the environment variables a second time after `CreateBuilder`, so the defaults are
   inserted just below that last environment source (above `appsettings.Development.json`, which points at localhost and tt02),
   before `ConfigureServices` reads them.
2. After `services.ConfigureHangfire(config);`, the last registration but Kestrel's: `AddLocaltestServices(services, config)`, so
   its `RemoveAll`/`Add` win and its authentication `Configure` actions run after upstream's.

3. `vendor/src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs:31-36`, the party-id branch of
   `CreateResourceCategory` (task 7b): `#if !LOCALTEST` production's condition (`urn:altinn:partyid` prefix or `IsPartyId`, 8 or 10
   digits), `#else` the prefix or any positive 64-bit integer (`long.TryParse`, `NumberStyles.None`, invariant culture), `#endif`.
   It comes after the organisation-number and national-identity-number branches, so only ids production never issues are treated
   differently. `IsPartyId` stays upstream's, see "Differences from production".

A fourth site (the browser download of attachments through OIDC, `IdportenTokenValidator.cs:26`) is task 11's and needs a report first;
the report is `../arbeidsflate/README.md`, "Correspondence attachments (11b, not implemented)".

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
Profile's two `correspondence/` lookups (production Profile, the profile component, with its scope check; since task 14
they have production semantics: organisations' official addresses from Profile's brreg mirror, seeded from the test data,
and unit contact points only for addresses users registered for the organisation, not every role holder), the PDP, the Resource Registry, Events, KRR (a
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
- KRR (the Register's stand-in) answers `JA` for a person with `reserved: true` in the test data (Rik Forelder, 1003, in the
  seed) and `NEI` for every other seeded person; unknown persons get no entry (`../register/README.md`).
- Profile's two `correspondence/` lookups are production Profile since task 14 (`../profile/README.md`): unit contact
  points are only addresses users registered for the organisation, not every role holder; official addresses come from
  Profile's brreg mirror, seeded once per organisation from the test data. Run with this group on 2026-10-21 (task 14
  phase 2, before task 7b): `smoke-test.sh` 43 ok and the 2 `KNOWN` party-id checks, unchanged.
- **Party ids of any length.** Every PDP request Correspondence builds (`AltinnAuthorizationService.CreateDecisionRequest` →
  `XacmlRequestFactory.CreateResourceCategory`) looks the party up in Register and names it by its bare party id; production
  takes a bare number as a party id only when it has 8 or 10 digits (`StringExtensions.IsPartyId`) and otherwise throws
  (500 "RecipientId is not a valid organization number, person number or party id"). The test data's party ids have 6 digits
  (Digdir 500006, Sophie Salt 501337, `signering-brukerstyrt`'s 600000-700002), so the factory's party-id branch here accepts
  any positive 64-bit integer (directive site 3). Organisation numbers (9 digits) and national identity numbers still take
  their own branches first, and 8- and 10-digit ids behave as in production; only ids production never issues are treated
  differently. `IsPartyId` itself is upstream's, so `CreateDecisionRequest` and `WithUrnPrefix` decide as in production. The
  upstream fix (Correspondence passing `urn:altinn:partyid:{id}`, which removes the site) is drafted in
  `../../../planned-components/correspondence/party-ids.md`, option (c).
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

- **An existing Postgres volume** predates `70-correspondence.sh`, which Postgres runs on an empty volume only: the host then fails
  with `28P01 password authentication failed for user "correspondence"`. Run `studioctl env reset -r localtest-v2 --yes` (or the
  script by hand, see the table at the top) once.
- The browser download of attachments from Arbeidsflate (the OIDC branch of the `AllSchemes` cascade) does not work: a third
  directive site, an OIDC client and a cookie decision (researched in `../arbeidsflate/README.md`, 11b, not implemented). A download with a dialog token as `Authorization` header works
  (the smoke test).
- Organisation recipients are not exercised by the smoke test (DDG Fitness and Digdir have official addresses since test data v3).
- The image is not pinned in `config/images.go` to a build that exists until this branch is on main and the tag is bumped; until
  then use `--dev correspondence` or `STUDIOCTL_IMAGE_LOCALTEST_V2_CORRESPONDENCE`.

## Tests

`./smoke-test.sh [BASE]` needs the stack with `--with correspondence` and checks, in order: `/health` on the host port and all 69
migrations (the `status` call, and in the database itself when `podman` reaches `localtest-v2-postgres`, plus the owner role
being no superuser); 401 without a token, 403 without `altinn:correspondence.write`, 400 `CORR-01036` for an unknown resource;
with a service owner's token: `POST attachment`, the upload with its
MD5, `Published` after the simulated scan, the bytes in `blobs/{id}`; a wrong checksum (400, `Failed` with "Checksum mismatch", no
blob); `POST correspondence` to Sophie Salt with the attachment and an e-mail notification to `Published`, the dialog in
Dialogporten (resource, party, content URL), the status history and Hangfire jobs; the e-mail captured and `Delivered`;
`notification/check` with a raw fake-Maskinporten token (the Maskinporten scheme's metadata override) and without one; an unknown
recipient (404); the reserved person Rik Forelder (KRR `JA`): alone 422 `CORR-01030`, with a second recipient `Reserved`; the
sender's purge of a published correspondence refused (`CORR-01013`). Then **app-lib's credential** (`/Home/GetTestOrgToken`
without `altinn:serviceowner`, so every call goes to the PDP with Digdir's party id 500006): `POST attachment`, the upload,
`GET attachment/{id}` to `Published`, `POST correspondence` to Sophie Salt with that attachment and an e-mail notification,
`details` to `Published`, its dialog. Then **the recipient** of that correspondence with the dialog token from Dialogporten
(task 2's code-flow token for user 1337, party 501337): `content` (which marks it `Read` and enqueues the opened activity), the
attachment download (the bytes), `markasread` after that (production's 400, 1040 "already marked as read"), `confirm` once the
two activity jobs have run, the status history (`Read`, `AttachmentsDownloaded`, `Confirmed`), the dialog's opened and confirmed
activities in Dialogporten, the recipient's purge (`PurgedByRecipient`, the dialog deleted). On the service owner's
correspondence the recipient's `details` (the fetch) and then `markasread` (200, `Read`). Then a publish 5 minutes ahead
(`ReadyForPublish`, its job under `jobs?state=Scheduled`), purged by the sender (`PurgedByAltinn`); the inspection API's paging
and job list. `CORRESPONDENCE_RESTART=1` restarts the container and checks that the data remain and no migration runs again.
64 checks, 67 with the restart; a `FAIL` makes it exit 1. The waits are generous since task 12 (attachments 60 s, a
correspondence to `Published` 180 s, the notification 120 s, the activity jobs and the dialog's activities 90 s; each
returns as soon as it holds): Hangfire polls every 5 s and the first jobs after `env up` are slow, and with the earlier
limits one check failed on each of two first runs. With them the first run after a fresh `env up --with everything`
passed 64 of 64 on 2026-10-21 (task 12, after the authorization, events, Dialogporten and adapter tests), and with
`CORRESPONDENCE_RESTART=1` as the very first test after another fresh `env up`, 67 of 67.

Two orderings matter and are production behaviour: `markasread` needs a `Fetched` status first (`details` or `content` set it)
and answers 400 once the correspondence is `Read`; and a `confirm` sent while one of the opened or download-started activity
jobs patches the same dialog gets Dialogporten's 409 (optimistic concurrency) and answers 500, so the test waits for those jobs.

## Verified

### Task 7b, the party-id site (2026-10-21)

On main 15813c3 (pin `sha-0485d0a`) with this branch on top, `studioctl env up -r localtest-v2 --with correspondence --dev
correspondence` on the existing volumes (the rest from the pinned images):

- Build: `dotnet build -c Release` of the API host with the fakes (the "Running without Docker" command, `--no-incremental`):
  0 errors and the same 24 warnings as without the site (the warning lists compared). The image built in the `env up`.
- `smoke-test.sh`: 64 ok; then with `CORRESPONDENCE_RESTART=1` 66 ok and 1 FAIL (the inspection API's job list showed a job
  Hangfire was still creating, without a state; the check now tolerates that), then 67 ok, 67 ok, 67 ok. The first version of
  the extended recipient path failed twice on production behaviour, both now part of the test: `markasread` after `content` is
  400 1040 (the content call marks it `Read`), and a `confirm` within milliseconds of the download got Dialogporten's 409 on
  the dialog patch (the opened and download-started activity jobs were writing the same dialog) and answered 500.
- `signering-brukerstyrt` under `studioctl run --dev-frontend` (the monorepo's `src/App/frontend/dist` served on 8080 with
  `Access-Control-Allow-Origin: *`; `frontend-test` running beside it, so the Register serves the seed and the apps' data),
  driven with curl as Human Andrefiolin (user 10000, party 600000) with the `AltinnStudioRuntime` cookie and the XSRF header:
  instance, form data with one person founder (himself) and one organisation founder (Sivilisert Avansert Isbjørn SA,
  312405091), `process/next` through the PDF task to `SigningTask_Founders`. Both signees `delegationSuccessful`,
  `notificationStatus` `Sent`: two correspondences on `apps-correspondence-integrasjon4` ("Oppgave - Signer
  stiftelsesdokumenter", sender 991825827, recipients the national identity number and the organisation number), `Published`
  within 3 s, each with a dialog, and each e-mail `Delivered` in the Notifications inbox (`human@example.com`,
  `isbjorn@example.com`, creator `digdir`, `sendersReference` `corr-600000/{instance}`). Signing for himself and on behalf of
  the organisation sent one receipt each ("Kvittering: Signering av stiftelsesdokumenter"), `Published` with the attachment
  `Stiftelse av aksjeselskap.pdf` (103 596 bytes, `Published`). With an auditor (`HarRevisor` `ja`, Offisiell Virtuell Tiger
  AS) the process moved on to `SigningTask_Auditor`, and the call-to-sign on `apps-correspondence-integrasjon2` got production's
  400 `CORR-04010` ("Resource type is not supported", `GenericAccessResource`); the auditor's `notificationStatus` is
  `Failed`. No Hangfire job failed.
- Cypress `signering-brukerstyrt/signing.ts` with the group on (the same frontend setup, `MONOREPO` set, nothing else running):
  1 of 1 passing (27 s). Its correspondences: call-to-sign for both persons and the organisation and two receipts, all
  `Published`.
- With the group on: `../register/smoke-test.sh` 14 ok, `../resource-registry/smoke-test.sh` 43 ok,
  `../notifications/smoke-test.sh` 64 ok, the root `../../smoke-test.sh` 23 ok, `../dialogporten/smoke-test.sh` 60 ok (after
  the Correspondence test; it deletes every dialog).
- With the group off (`env up -r localtest-v2` without `--with`): `/correspondence/...` answers `503` "Component not
  running"; `../../run-platform-regression.sh --no-apps` 44 of 44 (frontend-test, signering-brukerstyrt and
  process-transition-test with `studioctl run --dev-frontend`, the static frontend as above, `MONOREPO` set). It took 50
  minutes instead of about 6: `process-transition-test/workflow-status.ts` alone took 49 minutes, three of its tests passed
  on Cypress's second attempt and one ("a session parked on Task_Service follows the process on to a signing task") took
  12 minutes. The default stack has no Correspondence, so the site cannot be the cause; not investigated further.

Seen on the way: `signering-brukerstyrt`'s PDF task needs `--dev-frontend` and the static frontend (the app's page loads
`altinn-app-frontend.js` from the app-frontend host; without it the PDF generator times out on `#readyForPrint` and
`process/next` answers 504, the instance waiting in the PDF task until the frontend is served). With `signering-brukerstyrt`
the only app the Register serves only the apps' test data, without Digdir (991825827), and the app's page answered 500 on its
party lookup for that organisation; that run was not isolated from the missing frontend.

### Task 7 (2026-10-21)

2026-10-21, on main cc84b1d (pin `sha-55ce57f`) with this branch on top, `studioctl env up -r localtest-v2 --with correspondence
--dev correspondence --dev register --dev resource-registry --dev postgres` (the rest from the pinned images):

- Image build and start: on a fresh Postgres volume (`env reset`) the baked-in `70-correspondence.sh` created the role and
  database and the host applied all 69 EF migrations on Postgres 18.3 as the non-superuser owner; Hangfire created its schema.
  On the earlier volume the host failed with `28P01` until the script ran (see "Known gaps").
- `smoke-test.sh` with `CORRESPONDENCE_RESTART=1`: 46 ok, 2 `KNOWN` (the party-id gap), 0 FAIL, twice (before and after a
  machine restart, the second after the `ttd-localtest-correspondence` policy fix). Seen on the way: the publish needs the dialog
  job first (statuses Initialized, ReadyForPublish, Published within seconds); the notification goes to Notifications as
  `future/orders` with creator `digdir`, `sendersReference` `corr-…`, and is captured and `Delivered`; the dialog-token keys are
  fetched on the first token (`localtest-primary`, `localtest-secondary`).
- With the group on: `../register/smoke-test.sh` 34 ok (27 + 7 for KRR and the `correspondence/` lookups),
  `../resource-registry/smoke-test.sh` 41 ok (35 + 6), `../notifications/smoke-test.sh` 64 ok, `../dialogporten/smoke-test.sh`
  60 ok (before the machine restart, same code), the root `../../smoke-test.sh` 23 ok.
- With the group off: `/correspondence/...` answers `503` problem details (`component` and `group` `correspondence`, the `--with`
  command) and, for `Accept: text/html`, the same as an HTML page; `../../run-platform-regression.sh --no-apps` 44 of 44
  (apps with `studioctl run --dev-frontend`, the monorepo's built frontend served on 8080 with `Access-Control-Allow-Origin: *`,
  6 minutes, no retries).
- Memory: `podman stats --no-stream` 120 to 122 MB for the Correspondence container after the smoke test; the whole stack with
  the correspondence and dialogporten groups left about 3.2 to 4 GB of the machine's 7.8 GB available.

Not verified: organisation recipients reading their correspondence (the signing flow's organisation signee got its
correspondence and e-mail; nobody read it); a signee other than the instance owner signing through the API (the Cypress spec
skips it too, see its TODO); `signering-brukerstyrt` with only its own test data (apps-only Register, no Digdir); Altinn and
ID-porten recipient tokens (the list, overview and attachment calls without a dialog token); the notification job's
`AuthorizeUserIdsForResource` path; the Events component accepting Correspondence's events (the publish jobs succeeded, the
events were not looked up); Swagger under the proxy.

## Running without Docker

```sh
mkdir -p /tmp/corr && cd /tmp/corr && echo '{"sdk":{"version":"10.0.401","rollForward":"latestPatch"}}' > global.json
C=<this folder>; DirectoryBuildPropsPath=$C/localtest.build.props DirectoryBuildTargetsPath=$C/localtest.build.targets \
  dotnet build -c Release $C/vendor/src/Altinn.Correspondence.API/Altinn.Correspondence.API.csproj
```

Upstream has no `global.json`, so build from a directory with one (or the newest installed SDK, possibly a preview, is used).
Run `bin/Release/net10.0/Altinn.Correspondence.API.dll` with `ASPNETCORE_ENVIRONMENT=Development`,
`DatabaseOptions__ConnectionString`, `GeneralSettings__RedisConnectionString` and `Localtest__Correspondence__BlobRootPath`.
