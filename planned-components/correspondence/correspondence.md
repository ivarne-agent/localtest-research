# Correspondence for the new localtest stack

Research date: 2026-10-20 (task 7 of [`../../TODO.md`](../../TODO.md)). Read-only, plus a scratch build and two scratch
starts of the host against a throwaway Postgres in the session scratchpad (section 4.3). No image was built, and no
container or studioctl command was run. The only calls to the running stack were GETs: a token from
`/Home/GetTestOrgToken`, OpenID and JWKS metadata, and a Resource Registry lookup for a resource that does not exist.
Nothing in the stack was created or deleted. Claims that come from reading code and were not observed are marked
"not verified".

`U` below is `https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89`. Paths
without a link are relative to `src/` at that commit.

## Summary

- **Production Correspondence cannot publish without Dialogporten.** The publish job marks a correspondence `Failed`
  when it has no Dialogporten dialog ("Dialogporten dialog not created",
  `PublishCorrespondence/PublishCorrespondenceHandler.cs:72-74`). The dialog is created by a Hangfire job before the
  publish job. Dialogporten is therefore a runtime dependency, not an optional one. Correspondence also needs Redis:
  `Program.cs:140` connects eagerly, and without Redis the host aborts at start (seen in the scratch run). The
  Dialogporten group already runs a Redis.
- **Recommendation: Option A. Vendor the single API host with two additive hooks and no `#if !LOCALTEST` site.** The
  host (`Altinn.Correspondence.API`, six projects, about 35 000 lines of C# outside migrations) runs its own background
  work in Hangfire on Postgres. There is no Service Bus, no Kafka and no second process. Most of its Azure use is
  either behind interfaces or switched off by `Development`. Verified in the scratch run: the host builds with the
  props/targets mechanism (0 errors), applies its 69 EF migrations to an empty Postgres 18 database as a non-superuser
  owner, installs the Hangfire schema, and answers `/health` 200. It also validates a stack-minted `ttd` org token over
  http metadata, which proves the `Sender` policy passes, and calls the stack's Resource Registry through the shared
  Maskinporten fake.
- **Three things outside the component are prerequisites:**
  1. Correspondence resources in the Resource Registry seed: `CorrespondenceService` type, with policies that grant the
     local `ttd` org.
  2. A KRR stand-in. Publish and initialize call `kontaktregisteret.no` for every person recipient and fail without
     it.
  3. Two Profile endpoints in the Register component: `profile/api/v1/correspondence/units/contactpoint/lookup` and
     `.../organizations/notificationaddresses/lookup`.
- **Group: a `correspondence` optional group that turns on `dialogporten`, sharing its Redis.** Apps only reach
  Correspondence through the signing flow, which already tolerates failure. The default stack stays as it is.
- **Effort: 7 to 8 days** (section 6.13).
- **Biggest risks:**
  - The end-to-end path crosses five components: Resource Registry, PDP, Register, Dialogporten and Notifications.
    None of the calls that create things was made against the stack.
  - Upstream's `MigrateWithLock` swallows migration errors, so a broken migration shows only as a log line.
  - Attachment downloads opened from Arbeidsflate go through an OIDC login that needs a third `#if !LOCALTEST` site and
    a cookie fix. This plan defers it (section 6.2, item 3).

## 1. Pins

| Repo | Commit | What was read |
| --- | --- | --- |
| [`Altinn/altinn-correspondence`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) | **`6dcf561`** (= `origin/main` after `git fetch`, 2026-10-16, "attachment authorization context (#2150)"). The clone is shallow | all of `src/` except tests, `Dockerfile`, `docker-compose.yml`, the appsettings files |
| `Altinn/app-lib-dotnet` | `f708593` (`origin/main`) | `CorrespondenceClient`, the token resolver, the signing services |
| `Altinn/altinn-studio` | `9493f65` | the old localtest (reference only), `src/test/apps/signering-brukerstyrt`, studioctl's app environment |
| `Altinn/dialogporten`, `Altinn/altinn-dialogporten-adapter`, `Altinn/dialogporten-frontend` | `ec333e4`, `86364b8`, `b04bb5d` | calls to Correspondence and Correspondence-specific rules |
| `Altinn/altinn-profile` | `de3e9fe` | `CorrespondenceController` (the two lookups Correspondence calls) |
| tt02 Resource Registry | live, 2026-10-20 | `apps-correspondence-integrasjon2` and `-4`: resource and policy (GET only) |

This repository: the worktree is on `origin/main` at `178f3da`. The stack components it refers to are those on `main`
(Notifications done, Dialogporten 9a done, 9b in progress).

## 2. Consumers and contract

### 2.1 Endpoints, credentials and who calls them

Every path is under `correspondence/api/v1/` on the platform origin. studioctl already gives apps
`PlatformSettings__ApiCorrespondenceEndpoint = <origin>/correspondence/api/v1/` (`src/cli/internal/cmd/app/env.go:94` in
altinn-studio). Today the proxy answers it with 404.

| Consumer | Calls | Credential | What it does with the answer |
| --- | --- | --- | --- |
| **app-lib** `CorrespondenceClient` | `POST attachment` (JSON, returns a Guid); `POST attachment/{id}/upload` (raw `application/octet-stream`); `GET attachment/{id}` polled once a second, up to 60 times, until `Published`; `POST correspondence` (JSON with `existingAttachments`, not the multipart `upload`); `GET correspondence/{id}/details` | Bearer with scopes `altinn:serviceowner/instances.read`, `instances.write` and `altinn:correspondence.write` (`CorrespondenceAuthenticationMethod.Default()`). **On localtest** (`GeneralSettings:HostName` is `local.altinn.cloud`) app-lib does not use Maskinporten. It calls `GET /Home/GetTestOrgToken?org=…&orgNumber=991825827&authenticationLevel=3&scopes=…` (`AuthenticationTokenResolver.cs:70-90`). Also sends `Ocp-Apim-Subscription-Key`, which is ignored | only HTTP 200 counts as success |
| app-lib `SigningCallToActionService` (`SigningService.cs:113`) | the client above, a correspondence with a notification and no attachments, to each signee | as above | all exceptions are caught: the signee gets `HasBeenMessagedForCallToSign=false`, the UI shows "notification failed", and the process continues |
| app-lib `SigningReceiptService` (`SigningUserAction.cs:170`) | a receipt with the signed data elements as attachments | as above | logged only |
| Test apps | only `signering-brukerstyrt`: `correspondenceResource` `apps-correspondence-integrasjon4` and `-integrasjon2` (`config/process/process.bpmn:56, 92`) | | its e2e spec does not check notification status (grep) |
| **Dialogporten end user / Arbeidsflate** (frontend, through URLs Correspondence wrote into the dialog, `Integrations/Dialogporten/Mappers/CreateDialogRequestMapper.cs:213, 461-512, 585-619`) | `GET correspondence/{id}/content`; `GET confidential-reminders?languageCode=`; `POST {id}/confirm`; `DELETE {id}/purge`; API actions `GET {id}` and the downloads | `Bearer <dialog token>`. Correspondence validates it with the `DialogportenScheme`, using EdDSA keys from `{PlatformGatewayUrl}/dialogporten/api/v1/.well-known/jwks.json` (`API/Auth/EdDsaSecurityKeysCacheService.cs:63`). `RecipientScopePolicy` needs an `iss` that contains "dialogporten" and an `a` claim with `read` (`API/Auth/DependencyInjection.cs:317-324`) | shown in the inbox |
| Arbeidsflate attachment links | `GET {id}/attachment/{aid}/download`, `GET {id}/attachments/downloadall` | plain browser navigation. The `AllSchemes` cascade tries the Altinn JWT, then the dialog token, then challenges with **OIDC against Altinn Authentication** (`AltinnIdProviderSettings` client) and comes back with `?session=` (`API/Auth/CascadeAuthenticationHandler.cs:40-75`, `DependencyInjection.cs:92-240`) | file download |
| Arbeidsflate BFF | `POST {id}/forward` (dialog token); `GET {id}/forward/check` (raw ID-porten token) | | task 11 |
| **Notifications** (reverse call) | `GET {id}/notification/check` and `…/check/main`, the `conditionEndpoint` Correspondence puts on each order (`Application/CreateNotificationOrder/CreateNotificationOrderHandler.cs:381-392`; skipped when the host is `localhost`) | the **Maskinporten** scheme only, with scope `altinn:system/notifications.condition.check`. The stack's Notifications sends a raw token from the fake Maskinporten (no exchange) | decides whether the notification still goes out |
| Dialogporten, the adapter, Resource Registry, Events, Register, Storage | none (grep at the pins) | | |
| The old localtest (`src/Runtime/localtest`) | **nothing**: no Correspondence route, no emulation, no Maskinporten token endpoint. Its `GetTestOrgToken` had no `iss` and put scopes in `urn:altinn:scope`, so Correspondence would have refused it anyway | | |

The `Sender` policy (`API/Auth/DependencyInjection.cs:280-309`) accepts a token whose `iss` contains "altinn" and
whose `scope` contains `altinn:correspondence.write`. A token from `maskinporten.no` also needs `altinn:serviceowner`.
Verified on the stack (2026-10-20): `/Home/GetTestOrgToken?scopes=…altinn:correspondence.write` returns a token with:

- `iss` `http://local.altinn.cloud:8000/authentication/api/v1/openid/`
- `scope` as requested
- `urn:altinn:org` `ttd`
- `consumer` `{"ID":"0192:991825827"}`

The scratch host accepted that token: `GET correspondence/{random}/details` returned 404 `CORR-01001`, not 401.

A side effect of the policy shape: the fake Maskinporten's issuer `http://local.altinn.cloud:8000/localtest/api/maskinporten/`
contains "altinn" but not "maskinporten.no". A raw fake-Maskinporten token with `altinn:correspondence.write` therefore
passes the `Sender` policy without `altinn:serviceowner`, which production would demand. It fails the `Recipient`
policy, which production would accept with `altinn:correspondence.read`. No consumer in the stack sends such a token.
This is listed in section 8.

### 2.2 What the sender's request goes through

`POST correspondence` with the app's token:

1. Validation: the Resource Registry lookup, which must be type `CorrespondenceService` (`AltinnApp` is allowed for
   transmissions on an existing dialog), returns 400 `CORR-01036` for an unknown resource (seen in the scratch run).
2. Authorization (`Integrations/Altinn/Authorization/AltinnAuthorizationService.cs:146-195`). With the `altinn:serviceowner`
   scope and a `consumer` org number equal to the resource's competent authority, the PDP is bypassed. The app's
   scopes do not include `altinn:serviceowner`, so the **PDP decides** on action `write`, with subject attributes taken
   from the token's `urn:altinn:*` claims.
3. The KRR check for person recipients (`InitializeCorrespondenceValidationHelper.cs:416`).
4. Register lookups for the recipients.
5. The rows, then Hangfire jobs: the dialog (`InitializeCorrespondencesHandler.cs:211-262`), then the publish at
   `RequestedPublishTime` (`Helpers/HangfireScheduleHelper.cs:25-75`), the notification order, events, and due date,
   expiry and confidential-reminder jobs.

The publish job (`PublishCorrespondenceHandler.cs:52-85`) looks the sender and recipient up in Register, requires the
dialog, and checks KRR again. It then sets `Published` or `Failed`, orders the notifications
(`SendNotificationOrderHandler`), and polls Notifications for delivery 5 minutes after the send time
(`SendNotificationOrderHandler.cs:28, 152`).

## 3. What production Correspondence needs, and what the stack has

| Dependency | Production | In the stack today | Missing / plan |
| --- | --- | --- | --- |
| **Postgres** | Azure Flexible Server, Entra token when the connection string has no password (`Program.cs:179-198`, `Persistence/DependencyInjection.cs:41-61`); schema `correspondence` through **EF Core migrations** (69, `Persistence/Migrations/`), applied by the host **only in `Development`** (`Program.cs:71-80`, `MigrateWithLock`, advisory lock 123456789) | shared Postgres 18.3 (`localtest/postgres/`) | `70-correspondence.sh`: role `correspondence` (password `localtest`), database `correspondence` owned by it. Migrations at start (section 6.6) |
| **Hangfire** | `Hangfire.PostgreSql` on the same database, schema `hangfire` created by Hangfire itself. Two servers: `default` (15 workers) and `live-migration`/`migration` (4). 5 s schedule polling. Three daily recurring jobs (IP restrictions, daily report, bruksmonster cleanup); the Maskinporten JWK rotation is off unless `MaskinportenJwkRotationSettings:Enabled` (`Integrations/Hangfire/DependencyInjection.cs`, `API/Helpers/RecurringJobRegistration.cs`) | nothing (none needed) | runs unchanged inside the host. 2 workers, migration server off (`MigrationWorkerCountPerReplica=0`) |
| **Blob storage** | Azure Blob, block uploads with an MD5, container `attachments` per storage account (per service owner, or the default account from `AttachmentStorageOptions:ConnectionString`), container `reports` for the daily Parquet report, SAS URLs for composed-e-mail attachments (`Persistence/Repositories/StorageRepository.cs`, interface `Core/Repositories/IStorageRepository.cs`, 7 methods) | Storage's `FileBlobRepository` pattern (`localtest/components/storage/fakes/Blobs/`) | **mock**: `FileStorageRepository` on a volume (section 6.3). With no service-owner row, `AttachmentHelper.GetStorageProvider` returns null and the default account is used (`Application/Helpers/AttachmentHelper.cs:117-132`), so no service-owner provisioning is needed |
| **Malware scan** | Defender for Storage, Event Grid webhook `webhooks/malwarescanresults` | | **off by `Development`**: upload enqueues `SimulateMalwareScanResult`, which runs the production `MalwareScanResultHandler` with "No threats found" (`AttachmentHelper.cs:85-93, 266-290`) |
| **Dialogporten** | service-owner API: `POST/PATCH/GET/DELETE dialogporten/api/v1/serviceowner/dialogs…`, transmissions, activities, `actions/purge`, `actions/restore`, `endusercontext/systemlabels` (`Integrations/Dialogporten/DialogportenService.cs`, 1 423 lines). Exchanged Altinn token with `digdir:dialogporten.serviceprovider`, `.serviceprovider.admin`, `digdir:dialogporten.correspondence`. The dialog token is validated with the JWKS above | the `dialogporten` group (WebApi 8030), service-owner API verified in 9a with org tokens; JWKS at `dialogporten/api/v1/.well-known/jwks.json` (seen: two `localtest-*` Ed25519 keys, issuer `http://local.altinn.cloud:8000/dialogporten/api/v1`) | **required**, not optional: without a dialog every publish fails. `DialogportenSettings:Issuer` set. The 12-hour key refresh replaced (section 6.3). Not verified: creating a correspondence dialog (`CorrespondenceService` resource type, the `.correspondence` scope rules in Dialogporten's `IUserResourceRegistry.cs:109-114`) |
| **Resource Registry** | `GET resourceregistry/api/v1/resource/{id}`, `/policy/rights`, `/policy`; 10-minute HybridCache (`Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs:32, 88, 156, 199`) | the component (seed of non-app resources, `fakes/Seed/{id}/`) | **seed** `CorrespondenceService` resources with policies (section 6.8) |
| **Authorization (PDP)** | `POST authorization/api/v1/authorize` with the exchanged token (`altinn:authorization/authorize.admin`) | the component; resource policies come from the Resource Registry | nothing new. Not verified that the PDP serves a non-app resource's policy for action `write` and subject `urn:altinn:org` |
| **Register** | `register/api/v1/correspondence/parties/{uuid}/roles/correspondence-roles`, `…/main-units`, `…/query?fields=…`; 24-hour party cache (`Integrations/Altinn/Register/AltinnRegisterService.cs:33, 75, 127, 166`) | vendored Register: `CorrespondenceController` exists at its pin, policy `InternalOrPlatformAccess` = scope `altinn:register/partylookup.admin` (`RegisterHost.cs:170-171`), which Correspondence's token carries. Digdir (991825827, party 500006) is in the seed | nothing new |
| **Profile** | `POST profile/api/v1/correspondence/units/contactpoint/lookup` and `…/correspondence/organizations/notificationaddresses/lookup`, scope `altinn:profile/correspondence.notificationsettings.read` (altinn-profile `Controllers/CorrespondenceController.cs`, `Program.cs:198`) | the Register component serves the four **anonymous** lookups Notifications uses (`LocaltestProfileContactPointsController`) under `profile/api/v1/…`, **not** the `correspondence/` variants | **add** the two routes to the Register's Profile fakes, same answers, with the scope check (question 4) |
| **Notifications** | `POST notifications/api/v1/future/orders`, `orders/{id}/status` plus `links.self`, `future/shipment/{id}`, `future/orders/composed-email`, `PUT orders/{id}/cancel` (`Integrations/Altinn/Notifications/AltinnNotificationService.cs`) | the component, which plans for exactly this consumer (Notifications plan section 2.2). The captured inboxes show the messages | nothing new. Creator is `digdir`; `sendersReference` is `corr-…` |
| **KRR** (contact and reservation register) | `POST {ContactReservationRegistryBaseUrl}rest/v2/personer`, **raw** Maskinporten token `krr:global/kontaktinformasjon.read`, 2 s timeout, throws on any failure (`Integrations/Altinn/ContactReservationRegistry/ContactReservationRegistryService.cs:20-60`) | nothing | **add** an anonymous `register/api/localtest/krr/rest/v2/personer` in the Register fakes answering `reservasjon: "NEI"` for every seeded person (section 6.7). Without it, initialize and publish fail for every person recipient |
| **Events** | `POST events/api/v1/events` (CloudEvents), source `{CorrespondenceBaseUrl}/correspondence/api/v1/{…}` (`Integrations/Altinn/Events/AltinnEventBus.cs:36, 55`) | the component | nothing. Not verified that Events authorizes the publish for a non-app resource (the token has `altinn:events.publish.admin`) |
| **Storage** (SBL bridge) | `storage/api/v1/sblbridge/…` for Altinn 2 sync | the component, A2 endpoints off | **off**: `GeneralSettings:DisableCallsToAltinn2=true` (`Integrations/Altinn/Storage/AltinnStorageService.cs:26, 50`) |
| **Access Management** | `IAltinnAccessManagementService` is registered but **used nowhere** (grep) | | nothing |
| **Maskinporten / token exchange** | `Altinn.ApiClients.Maskinporten` 10.1.0, `SettingsJwkClientDefinition` per typed client, `ExhangeToAltinnToken=true`, 16 scopes (`API/appsettings.json:14-18`, `Integrations/DependencyInjection.cs:93-101`). Raw token for KRR (`:143-154`). **Without `MaskinportenSettings:ClientId` every client is swapped for an upstream dev stub** (`:45-56`) | the shared `IMaskinportenService` fake (`localtest/components/shared/fakes/Maskinporten/`), the fake Maskinporten token endpoint, production `exchange/maskinporten` | copy the fake; set a client id so the production clients are registered; `UseAltinnTestOrg=false` (acts as `digdir`, as Dialogporten does). **Verified in the scratch run** |
| **Incoming token validation** | Altinn: `AltinnOptions:OpenIdWellKnown`. Maskinporten: **hard-coded** `https://test.maskinporten.no/...` outside Production (`API/Auth/DependencyInjection.cs:57-64`). Dialog token: EdDSA keys | Authentication's OpenID metadata (http), the fake Maskinporten's metadata (http) | `ConfigureAll<JwtBearerOptions>` `RequireHttpsMetadata=false`, and a named `Configure` of the Maskinporten scheme's `MetadataAddress`, registered after `ConfigureAuthentication`. **Verified for the Altinn scheme**; not verified for Maskinporten (the Notifications condition check) |
| **Redis** | `AddStackExchangeRedisCache` (HybridCache L2, OIDC state, the OIDC session hand-off), Data Protection keys, `ConnectionMultiplexer.Connect` **eagerly** (`Program.cs:135-143`) | the `redis` container of the Dialogporten group | share it with `defaultDatabase=1`. Without Redis the host aborts at start (scratch run 1). With `abortConnect=false` and no Redis it starts, but every cache call waits 5 s and fails (scratch run 2), so that is not an option |
| **Key Vault** | only the Maskinporten JWK rotation and its maintenance endpoint (`Integrations/Azure/KeyVaultSecretStore.cs`) | | **off**: rotation disabled by default |
| **Azure Resource Manager** | storage accounts per service owner (skipped in Development, `AzureResourceManagerService.cs:88`), container app IP restrictions (daily job), container app refresh | | **mock**: a no-op `IResourceManager`, so the daily IP job logs one line instead of failing ten Hangfire retries with `DefaultAzureCredential` |
| **Application Insights / OTLP** | only when `GeneralSettings:ApplicationInsightsConnectionString` is set (`Integrations/OpenTelemetry/DependencyInjection.cs:61`) | | **off** |
| **Slack** | `SlackDevClient` when `GeneralSettings:SlackUrl` is empty | | **off** (logs) |
| **Altinn CDN, brreg** | not used by the runtime path (`altinncdn` and `brreg` appear only in the report code) | | nothing |
| **Service Bus / Kafka** | none | | |

## 4. Build chain

| Question | Finding |
| --- | --- |
| Projects | `API` (host, `Microsoft.NET.Sdk.Web`), `Application`, `Core`, `Common`, `Integrations`, `Persistence`. All six go into the image. Lines of C# outside `Migrations/`: API 7 215, Application 11 856, Integrations 8 835, Core 2 967, Persistence 2 939, Common 937 |
| SDK | **No `global.json`**; the Dockerfile uses the floating `mcr.microsoft.com/dotnet/sdk:10.0`. The machine has 10.0.302, 10.0.401 and 11.0.100-rc.2, so a scratch build needs its own `global.json` or it picks the RC |
| Package management | **No CPM** (versions in each csproj), **no `Directory.Build.props/targets`**, no lock files, no `TreatWarningsAsErrors`, no StyleCop or other analyzer package. `GenerateDocumentationFile` with `NoWarn 1591` in the host |
| `localtest.build.props/.targets` | Notifications' props work unchanged: the vendored import resolves to nothing, `LOCALTEST` is defined, and warnings-as-errors is off anyway. The targets file only adds `fakes/**/*.cs` to `Altinn.Correspondence.API`. Fake code can use the host's internal types and Core's public interfaces |
| Dockerfile | [`Dockerfile`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/Dockerfile) at the repository root: build context is the root, restore from the six csproj in layers, `dotnet publish -c Release -o out`, runtime `aspnet:10.0` (**Debian, unpinned, no digest**), `EXPOSE 2525`, `ASPNETCORE_URLS=http://+:2525`, user `dotnet` created with `groupadd -r`/`useradd -r`, `/mnt/storage` created and owned by it, `ENTRYPOINT dotnet Altinn.Correspondence.API.dll`. No `HEALTHCHECK` |
| Native pieces | none |

### 4.3 What was run (all in the scratchpad; the reference clone was not modified, `git status` clean)

1. `git archive 6dcf561 src Dockerfile` into a scratch `vendor/`. Notifications' `localtest.build.props`, a
   targets file compiling `fakes/**/*.cs` into the host, a `global.json` pinning 10.0.401, the three shared Maskinporten
   fake files, and one scratch fake file: configuration defaults, the Maskinporten fake, the JWT options and a
   throwing `IStorageRepository`. The two additive hooks of section 6.2 went into `Program.cs`.
   `dotnet build -c Release` with `DirectoryBuildPropsPath`/`DirectoryBuildTargetsPath` set: **0 errors, 24 warnings
   (all upstream's own: `CS0618`, `CS1573`)**, 13 s warm. The fake types are in the host assembly.
2. Started a throwaway Postgres **18.6** from `/usr/lib/postgresql/18/bin` (scratch data directory, port 55432) and ran
   the proposed init script twice (idempotent). Started the host with `ASPNETCORE_ENVIRONMENT=Development`:
   - **Run 1 (Redis unreachable):** aborts in `Program.cs:140` with `RedisConnectionException`.
   - **Run 2 (`abortConnect=false`):**
     - All **69 migrations applied** as the non-superuser owner: 18 tables in `correspondence`, `hstore` created, 7
       notification templates seeded.
     - `pg_stat_statements`, `pg_cron` and `pgaudit` were skipped by their own `EXCEPTION` blocks.
     - Two `CREATE INDEX CONCURRENTLY` migrations warn that they cannot run in a transaction, and the indexes exist
       anyway.
     - EF logs two "Failed executing DbCommand" lines while probing the missing history table at first start (noise).
     - Hangfire installed 12 tables in `hangfire` and registered the three recurring jobs.
     - `/health` returned 200 `Healthy`, `/healthz` 200, and a token-less `POST correspondence` 401.
     - Every Redis use waited 5 s and logged a failure.
     - Memory: about **197 MB RSS** idle.
   - **Run 3** (same, the stack reachable):
     - The EdDSA cache loaded the stack's two Dialogporten keys.
     - The stack's `ttd` org token was accepted (404 for a random id).
     - `POST correspondence` for a non-existent resource returned 400 `CORR-01036` after a real Resource Registry
       lookup through the Maskinporten fake and the exchange.
     - During one minute of run 2 the stack's front door returned 502 or refused connections. Another task was
       restarting it, so the EdDSA fetch in run 2 found 0 keys, which shows the failure mode of section 6.3 item 4.
3. Stopped the host and Postgres and deleted `bin/` and `obj/`.

## 5. Options

### Option A (recommended): vendor the API host, fakes for blobs and Azure, on the shared Postgres

Production `src/` unchanged except two additive hooks in `Program.cs`. The other components serve the real
dependencies, and three small additions go into components that already exist (Resource Registry seed, Register: KRR
and two Profile routes).

| What | Production | Local |
| --- | --- | --- |
| Background processing | Hangfire on Postgres | the same, 2 workers |
| Blobs | Azure Blob | `FileStorageRepository` on a volume |
| Malware scan | Defender and Event Grid | upstream's own Development simulation |
| Dialogporten, Notifications, Register, PDP, Resource Registry, Events | HTTP | the same HTTP calls to the stack's components |
| KRR | kontaktregisteret.no | an endpoint in the Register fakes, through the production client |
| Redis | Azure Cache | the Dialogporten group's Redis, database 1 |
| Azure Resource Manager, Key Vault, App Insights, Slack | Azure | no-op `IResourceManager`; the rest off by configuration |

Pros:

- Every consumer-visible behaviour is production's: statuses, the dialog content and actions, notification orders and
  their conditions, idempotency, validation error codes.
- The diff against upstream is two additive lines.
- No new infrastructure.
- The dialog and notification side effects are exactly what a tester wants to see.

Cons:

- It is only useful together with Dialogporten.
- The first correspondence crosses five components, so failures surface as Hangfire job retries in a log rather than
  as an HTTP error.

### Option B: a fake-only Correspondence

Written fresh, covering what app-lib calls: `POST attachment`, `upload`, `GET attachment/{id}` returning `Published`,
`POST correspondence`, `GET details`. It would record the correspondences and show them under `api/localtest/`. About
600 to 900 lines for the app-facing slice, and 1 day of work.

- **Pros:** no Dialogporten, Redis or Postgres; could run in the default stack; signing flows get a green tick.
- **Cons:**
  - It invents the contract it is meant to test: status machine, validation codes, resource-type and PDP checks,
    reservation, notification templates.
  - It produces no dialog and no notification, so nothing shows in the inbox or in the Notifications capture.
  - Every Arbeidsflate, dialog-token and Notifications-condition path stays untested.
  - It needs its own test suite to track a very active upstream.

**Rejected** by the project rule (vendor when the build chain allows it, and here it does without a single
`#if !LOCALTEST`). It could be revisited only as a "Correspondence off" stand-in for the default stack (question 1).

### Option C: vendor with upstream's own dev stubs

Leave `MaskinportenSettings:ClientId` empty. Upstream then swaps in `DialogportenDevService`,
`AltinnDevNotificationService`, `AltinnRegisterDevService` and six more. Zero fakes, runs without Dialogporten.
**Rejected:** every integration becomes a stub that answers fixed values (the dev Dialogporten returns a random id, the
dev Notifications pretends), so the stack would show nothing and test nothing beyond Correspondence's own database.
Worth knowing as the fallback if Option A's first run stalls on a dependency.

### Recommendation

**A**, in its own optional group that turns on `dialogporten` (section 6.11). A fake-only stand-in in the default stack
(B) is a separate decision for the product owner (question 1). It is not needed for the signing flows, which already
tolerate a missing Correspondence.

## 6. Option A in detail

### 6.1 What to copy

Under `localtest/components/correspondence/vendor/`, layout preserved:

- `src/` (the six projects, `Properties/` included)
- `Dockerfile`, for reference and the mirror
- `docker-compose.yml`, for reference only

Not copied: `Test/`, `flux/`, the Postman collection, `README*`, `start.bat`.

Copy with `git archive 6dcf561`; the first commit is unmodified. `vendor/UPSTREAM.md` records the pin, the paths, the
two hook sites and the `tools/repin-vendor.py` invocation.

### 6.2 Directive sites (two, both additive; zero `#if !LOCALTEST`)

Both are in [`src/Altinn.Correspondence.API/Program.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs).
Both were built and started in the scratch run.

1. **After line 49** (the `.AddEnvironmentVariables();` that ends the `builder.Configuration` chain, before
   `StartupAppSettingsLogging` on line 50), this pure addition:

   ```csharp
   #if LOCALTEST
       Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestConfiguration(builder.Configuration);
   #endif
   ```

   It inserts an in-memory source **just below the last environment-variable source and above every JSON file**.

   Why it has to be here and not right after `CreateBuilder` (the Dialogporten pattern): `Program.cs:45-49` adds
   `appsettings.json`, `appsettings.Development.json` and the environment variables **a second time**, so a source
   inserted earlier would end up below `appsettings.Development.json`. That file points the database, Redis and every
   platform URL at localhost and tt02.

   It must also come before `ConfigureServices` (line 51), which reads the connection string, `GeneralSettings`,
   `AltinnOptions`, `MaskinportenSettings` and `DialogportenSettings` eagerly (lines 89, 118-140,
   `Auth/DependencyInjection.cs:23-30`, `Integrations/DependencyInjection.cs:31-34`).

2. **After line 163** (`services.ConfigureHangfire(config);`, the last registration but Kestrel's), this pure
   addition:

   ```csharp
   #if LOCALTEST
       Altinn.Correspondence.Localtest.LocaltestCorrespondence.AddLocaltestServices(services, config);
   #endif
   ```

   It runs after every production registration, so `Replace`/`RemoveAll` win. The JWT and OIDC `Configure` actions are
   registered after `ConfigureAuthentication` (line 148), so they run after upstream's `AddJwtBearer` lambdas and
   before `JwtBearerPostConfigureOptions` builds the metadata managers. Dialogporten's README explains why a
   `PostConfigure` would be too late.

Candidates deliberately **not** used:

- **`Program.cs:135-143` (Redis).** Configuration only, because the Redis exists in the group.
- **`Program.cs:71` (migrations in Development only).** `Development` is the environment anyway.
- **`Program.cs:82` (recurring jobs).** The no-op `IResourceManager` makes the IP job harmless; the report and
  bruksmonster jobs run against the file store and the database.
- **[`API/Auth/IdportenTokenValidator.cs:26`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs#L26)**
  (`new HttpDocumentRetriever()`, which refuses http metadata). This would be the third site, `#if !LOCALTEST`
  production / `#else` `new HttpDocumentRetriever { RequireHttps = false }`, and it is only needed for the
  **browser download of attachments** from Arbeidsflate (the OIDC branch of the cascade). That branch also needs:
  - a seeded OIDC client in Authentication (task 2 style: `AltinnIdProviderSettings:ClientId/ClientSecret`, redirect
    `{origin}/correspondence/api/v1/idporten-callback`)
  - a cookie fix. The correlation cookie is `SameSite=None` with `Secure` forced (`DependencyInjection.cs:103-104`);
    browsers reject a `SameSite=None` cookie without `Secure`, so the proxy's `Secure` stripping over http breaks it
    (not verified in a browser).

  **Deferred to the Arbeidsflate task (11).** `IdportenTokenValidator` is a concrete class without virtual members, so
  no DI fake can replace it.

### 6.3 Fakes (`fakes/`, namespace `Altinn.Correspondence.Localtest`, compiled into the host)

| File | Replaces or adds | Lines (estimate) |
| --- | --- | --- |
| `LocaltestCorrespondence.cs` | the two hooks: configuration defaults (6.5); the `RemoveAll`/`Add` calls below; `AddLocaltestMaskinporten` | 180 |
| `LocaltestCorrespondenceOptions.cs` | `Localtest:Correspondence:{PlatformEndpoint,BlobRootPath}` | 30 |
| `Storage/FileStorageRepository.cs` | `IStorageRepository` (7 methods): attachments at `{BlobRootPath}/attachments/{id}`, reports at `{BlobRootPath}/reports/{name}` with the two count tags in a sidecar JSON. MD5 over the stream with `HashMismatchException` on a checksum mismatch, as upstream. `locationUrl` `{platform}/correspondence/api/localtest/blobs/{id}`, which ends with the id as the malware handler parses it. `GenerateSasUrl` returns the same URL | 170 |
| `Azure/LocaltestResourceManager.cs` | `IResourceManager`: deploy is a no-op, IP ranges empty (the updater logs one line and returns), service tags null | 40 |
| `Auth/LocaltestAuthentication.cs` | `ConfigureAll<JwtBearerOptions>` and `ConfigureAll<OpenIdConnectOptions>` with `RequireHttpsMetadata=false`. `Configure<JwtBearerOptions>("Maskinporten")` with `MetadataAddress = {platform}localtest/api/maskinporten/.well-known/oauth-authorization-server`. For `DialogportenScheme`, an `IssuerSigningKeyResolver` over a small cache that refreshes the JWKS **on a key miss** (at most every 10 s) instead of upstream's start-plus-12-hours `EdDsaSecurityKeysCacheService`. That service stays (it is harmless), but its static list is no longer read. Without this, a Correspondence started before Dialogporten is ready rejects every dialog token for 12 hours (seen in run 2: 0 keys) | 90 |
| `Maskinporten/*` | copy of `components/shared/fakes/Maskinporten/` (three files) | 176 |
| `Inspection/LocaltestCorrespondenceController.cs`, `…Queries.cs` | `correspondence/api/localtest/` (6.9) and the blob download route | 380 |

About **1 070 lines**, of which 176 are copied. No `prepare.sql`, because the inspection reads production tables and
Hangfire's. No reset on start (6.6).

Not replaced, because production code serves them through the stack:

- Dialogporten, Notifications, Register, Profile, PDP, Resource Registry, Events
- KRR, through the production client to the Register fake
- `ISlackClient` (upstream's dev client)

### 6.4 Timing and job behaviour worth knowing

- **Order of a send:**
  1. `POST correspondence` returns `Initialized` at once.
  2. The dialog job runs within Hangfire's 5 s polling.
  3. The publish job runs at `max(RequestedPublishTime, now)`.
  4. Notifications are ordered by the publish job.
  5. Notifications' capture delivers them within seconds.
  6. The delivery check comes 5 minutes after the send time (a constant), then follows its backoff.

  A tester sees `Published` a few seconds after the send, and the notification `Delivered` in the Notifications inbox
  long before Correspondence's own notification status says so.
- **When Dialogporten fails:** `CreateDialogportenDialog` uses Hangfire's default `AutomaticRetry` (10 attempts with
  growing delays), then `Delete`, and only then does the publish continuation run and set `Failed`. A dialog that
  cannot be created therefore leaves the correspondence in `Initialized` for a long time. The inspection API shows the
  failed job (6.9). The retry schedule is upstream's and not shortened (no directive).
- **Non-production values upstream already applies** because the environment is not `Production`:
  - the reminder notification after 1 day (production 7, `CreateNotificationOrderHandler.cs:217`)
  - the unread-confidential check after 1 minute (production 7 days, `InitializeCorrespondencesHandler.cs:189-191`)
  - the minimum attachment expiry of 1 day (production 14, `AttachmentHelper.cs:224`)
- **`Development` side effects:**
  - **Token lifetime is not validated** on any scheme (`ValidateLifetime = !IsDevelopment()`). This is a difference
    from production, listed in section 8.
  - Internal endpoints appear in Swagger (`ShowInternalApisInDevelopmentApplicationModelConvention`).
  - The exception handler includes messages.
  - Logging is Debug unless the defaults lower it (6.5).

### 6.5 Configuration defaults (just below the environment variables)

With `{platform}` = `Localtest:Correspondence:PlatformEndpoint` (default `http://local.altinn.cloud:8000/`, no trailing
slash after trimming):

| Key | Value |
| --- | --- |
| `DatabaseOptions:ConnectionString` | from studioctl: `Host=postgres;Port=5432;Database=correspondence;Username=correspondence;Password=localtest` (a password keeps Entra out, `Program.cs:186`) |
| `AttachmentStorageOptions:ConnectionString` | `UseDevelopmentStorage=true` (never read: the repository is replaced. It must be non-empty because the option is `required`) |
| `AltinnOptions:PlatformGatewayUrl` | `{platform}/`: base of every outgoing platform call, the EdDSA JWKS and the expected Altinn issuer in `RecipientScopePolicy` |
| `AltinnOptions:OpenIdWellKnown` | `{platform}/authentication/api/v1/openid/.well-known/openid-configuration` |
| `AltinnOptions:ArbeidsflateOriginsCommaSeparated` | `http://af.local.altinn.cloud:8000` (CORS, task 11) |
| `DialogportenSettings:Issuer` | `{platform}/dialogporten/api/v1` (seen in the stack's metadata) |
| `MaskinportenSettings:ClientId`, `Environment`, `EncodedJwk`, `UseAltinnTestOrg`, `ExhangeToAltinnToken` | `991825827` (a client id is what selects the production clients; nine digits make it Digdir's org number at the fake), `test`, a dummy JWK, `false`, `true`; `Scope` stays upstream's list |
| `Localtest:Maskinporten:Issuer`, `ExchangeEndpoint` | `{platform}/localtest/api/maskinporten/`, `{platform}/authentication/api/v1/exchange/maskinporten` |
| `GeneralSettings:CorrespondenceBaseUrl` | `{platform}/`: the URLs written into dialogs, notification condition endpoints and event sources. Must not be `localhost`, or the condition endpoint is dropped (`CreateNotificationOrderHandler.cs:387`) |
| `GeneralSettings:RedisConnectionString` | `redis:6379,defaultDatabase=1` (the group's Redis; Dialogporten uses database 0) |
| `GeneralSettings:ContactReservationRegistryBaseUrl` | `{platform}/register/api/localtest/krr/` |
| `GeneralSettings:ArbeidsflateBaseUrl` | `http://af.local.altinn.cloud:8000` |
| `GeneralSettings:DisableCallsToAltinn2` | `true` |
| `GeneralSettings:WorkerCountPerReplica`, `MigrationWorkerCountPerReplica` | `2`, `0` (production 15 and 4) |
| `GeneralSettings:EnableDownloadAll` | `true` (as the test environments) |
| `GeneralSettings:SlackUrl`, `ApplicationInsightsConnectionString`, `MalwareScanBypassWhiteList` | empty |
| `IdportenSettings:Issuer` | `{platform}/localtest/api/idporten/` |
| `AltinnIdProviderSettings:ClientId/ClientSecret` | upstream's `clientId`/`clientSecret` placeholders until task 11 (6.2) |
| `Logging:LogLevel:Default`, `…EntityFrameworkCore.Database.Command`, `Npgsql.Command` | `Information`, `Warning`, `Warning` (the Development file has Debug and every SQL command) |
| `Localtest:Correspondence:BlobRootPath` | `/mnt/storage` (the directory upstream's image already creates and hands to `dotnet`) |

Environment from the image: `ASPNETCORE_ENVIRONMENT=Development` (migrations at start, malware simulation, storage
deploy skipped) and upstream's `ASPNETCORE_URLS=http://+:2525`.

### 6.6 Postgres

`localtest/components/correspondence/postgres-init/70-correspondence.sh`, baked into `localtest/postgres/Dockerfile`
with one `COPY` line, in Dialogporten's style:

- role `correspondence`: login, password `localtest`, **not** superuser
- database `correspondence`, owned by that role. This is upstream's own database name; its PIM-grant migration
  mentions it

Idempotent; the scratch run executed it twice.

- **Migrations:** the host applies its 69 EF migrations at start (`Development`). There is no migrator image and no
  bundle.
- **Hangfire:** creates its `hangfire` schema at first resolution.
- **Extensions:**
  - `hstore` is created by a migration as the owner; it is trusted in Postgres 13 and later.
  - `pg_stat_statements` (preloaded in the shared image, but `CREATE EXTENSION` needs a superuser), `pg_cron` and
    `pgaudit` are skipped by the migrations' own exception blocks.

**No reset on start**, for the same reason as Dialogporten: correspondences and their dialogs must stay in step. The
only reset is `studioctl env reset -r localtest-v2`, which removes the shared Postgres volume. The blob volume must go
with it (the studioctl wiring removes `correspondence-blobs` on reset). Not verified on the shared image's 18.3; the
scratch server was 18.6.

**Risk:** `MigrateWithLock` catches every exception and prints `Migration failed: …` to the console
(`Persistence/Helpers/ApplicationDbContextExtensions.cs:33-36`). The host then starts and `/health` (`CanConnectAsync`)
still says Healthy. The smoke test must therefore check that a known migration is in `__EFMigrationsHistory`, or the
inspection API must report the migration count.

### 6.7 What the stack must add outside the component

1. **Resource Registry seed** (`localtest/components/resource-registry/fakes/Seed/{id}/`), applied at start as today:

   | Id | Type | Owner | Policy |
   | --- | --- | --- | --- |
   | `apps-correspondence-integrasjon4` | `CorrespondenceService` | `ttd`, organisation 991825827 (as tt02) | the tt02 policy (rules: org `digdir` read/write/subscribe/publish; roles `dagl` and `priv` read/write; fetched 2026-10-20) **plus `ttd` beside `digdir`** in the service-owner rule, marked as a localtest deviation |
   | `apps-correspondence-integrasjon2` | `GenericAccessResource`, as tt02 | as above | as above |
   | `ttd-localtest-correspondence` | `CorrespondenceService` | `ttd` | written for the smoke test: `ttd` write; `dagl`, `priv` read |

   **Why `ttd` must be added:**
   - The tt02 policies grant the service-owner rule to `urn:altinn:org` `digdir`.
   - Locally the app's token says `ttd` (`GetTestOrgToken` exchanges with `test=true`).
   - The app's scopes miss `altinn:serviceowner`, so the PDP decides, and it would deny.

   **Note:** `integrasjon2` is a `GenericAccessResource`, which Correspondence refuses for a new correspondence (400,
   error 4010), in production as well. It is listed so that `signering-brukerstyrt` gets the production error rather
   than a 404 (question 6).
2. **KRR in the Register fakes:** `POST register/api/localtest/krr/rest/v2/personer`, anonymous (production gets a raw
   Maskinporten token and nothing checks it locally). Body `{"personidentifikatorer": […]}`; answer `{"personer":
   [{"personidentifikator", "reservasjon": "NEI", "status": "AKTIV", "kontaktinformasjon": {…}}]}`. Persons outside the
   seed get no entry. About 60 lines. Its route sits under the Register's `api/localtest/` because `/register` is
   already proxied. A `reserved` flag in the seed would later serve both this and Notifications' reservation question
   (Notifications plan question 5).
3. **Profile's Correspondence lookups in the Register fakes:**
   - `POST profile/api/v1/correspondence/units/contactpoint/lookup`
   - `POST profile/api/v1/correspondence/organizations/notificationaddresses/lookup`

   They give the same answers as the existing anonymous routes in `LocaltestProfileContactPointsController`. Production
   requires the scope `altinn:profile/correspondence.notificationsettings.read`. About 40 lines.

### 6.8 Test-data mapping: what an app's correspondence becomes

- **Sender:** the org number from the request (`0192:991825827` from app-lib for `ttd`), which is Digdir (party
  500006) in Register.
- **Recipient Sophie Salt (person `01839012345`, party 501337):**
  - KRR says not reserved.
  - The dialog is created for `urn:altinn:person:identifier-no:01839012345`.
  - Notifications resolves her e-mail (`1337@altinnstudiotestusers.com`) and captures it.
  - The SMS to her bare `87654321` is rejected by production Notifications, as recorded in TODO.md task 6.
- **Recipient DDG Fitness AS (500000):**
  - Correspondence's own Profile lookup (6.7, item 3) and Notifications' organisation path.
  - The seed has no official organisation addresses, so organisation notifications end as Notifications' 422 path.
    Correspondence then stores no order and the correspondence still publishes (not verified).
- **The dialog:**
  - `serviceResource` is `urn:altinn:resource:{resourceId}`.
  - The content reference is `{platform}/correspondence/api/v1/correspondence/{id}/content`.
  - Actions are confirm, purge and the attachment downloads.
  - It appears in Dialogporten's end-user API for the recipient (task 2 token) and in its service-owner API.

  The known `ttd`/`digdir` search pain point (TODO.md task 10) applies: Correspondence acts as `digdir`.

### 6.9 Inspection API

`correspondence/api/localtest/`, anonymous, read-only apart from blob download:

| Call | Returns |
| --- | --- |
| `GET correspondences?after=&resourceId=&sender=&recipient=&status=&limit=` | `{ items, next }`: id, resource, sender, recipient, `sendersReference`, title, current status with time, dialog id and transmission id (external references), attachment count, notification count, created |
| `GET correspondences/{id}` | the correspondence with its whole status history, content, attachments (status, size, checksum, `blobs/{id}` link), notifications (order id, shipment id, `notificationSent`, reminder flag, with a link to `notifications/api/localtest/messages?orderId=…`), external references, forwarding events, and the **Hangfire jobs** whose arguments mention the id (state, attempts, last exception) |
| `GET jobs?state=Failed\|Scheduled\|Processing&limit=` | Hangfire jobs read from the `hangfire` tables: method, arguments, state, reason, exception message. This replaces a Hangfire dashboard, which would need `UseHangfireDashboard` after `Build()`, a third hook |
| `GET status` | migration count from `__EFMigrationsHistory`, latest migration, Hangfire servers, EdDSA keys loaded |
| `GET blobs/{id}` | the attachment's bytes (the target of `locationUrl` and `GenerateSasUrl`) |

The inspection GUI (task 8) reads `correspondences` as the list, `correspondences/{id}` as the drill-down, and links
the dialog (`dialogporten/api/v1/serviceowner/dialogs/{id}`) and the Notifications inbox. No stream in the first
version; the GUI polls.

### 6.10 Dockerfile

Mirror [`Dockerfile`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/Dockerfile)
line for line:

- `sdk:10.0` and `aspnet:10.0`
- the six-csproj restore layer
- `COPY src`, `dotnet publish -c Release -o out`
- `EXPOSE 2525`, `ASPNETCORE_URLS`
- the `groupadd`/`useradd` user
- `/mnt/storage`
- `USER dotnet`, the entrypoint

Deviations, each marked `localtest:`:

- The build context is the component folder, so the `COPY` paths gain `vendor/`.
- `localtest.build.props`, `.targets` and `fakes/` are copied first, and `ENV DirectoryBuildPropsPath/…TargetsPath` is
  set before restore.
- `ENV ASPNETCORE_ENVIRONMENT=Development` in the runtime stage.
- **Pinning:** upstream's base images float (`10.0`, no digest). Either pin both by digest as a `localtest:` deviation,
  for reproducible images like every other component, or follow upstream's floating tags (question 7). Proposed: pin
  by digest and bump on re-pin.

The image is Debian-based, unlike Notifications and Events (Alpine) and Storage (chiseled), which is upstream's choice.
It needs no `tzdata` step; nothing uses `Europe/Oslo` in the runtime path (grep). Not measured: image size and build
time.

### 6.11 studioctl and image integration

All in `localtest/studioctl/src/cli/`, modelled on Notifications and the Dialogporten group:

| Where | Change |
| --- | --- |
| `internal/cmd/env/localtestv2/groups.go` | a second group, `groupCorrespondence = "correspondence"` (default off), and an **implication**: enabling `correspondence` enables `dialogporten` (a small `groupRequires` map applied in `ResolveGroups`, about 20 lines with a test; `--without dialogporten --with correspondence` is an error) |
| `internal/cmd/env/localtestv2/components.go` | `componentCorrespondence = "correspondence"`, host port **8033**, container port `2525`, `healthPath = "/health"` (production: `CanConnectAsync`, 200 `Healthy`; `/healthz` is liveness only), `group = groupCorrespondence`. Env: `Localtest__Correspondence__PlatformEndpoint`, `DatabaseOptions__ConnectionString` (role `correspondence`, `Database=correspondence`), `GeneralSettings__RedisConnectionString=redis:6379,defaultDatabase=1`. Volume `correspondence-blobs` at `/mnt/storage`. No certificate: Correspondence neither signs nor validates platform access tokens. Deps: postgres, register, authorization, resource-registry, notifications, events, **redis** and **dialogporten-webapi** (healthy, so the migrations have run and the JWKS is served). Default `extraHosts` (the front door). `routes = {host: AppHostName, prefix: "/correspondence"}`; the paths already start with `/correspondence/api/v1`, so there is no rewrite |
| `components(opts)` | one line in start order, after the Dialogporten hosts |
| `internal/config/images.go` | `Correspondence ImageSpec`, `localtestV2Image("correspondence")`, override `STUDIOCTL_IMAGE_LOCALTEST_V2_CORRESPONDENCE`, entry in the iteration list |
| `topology_internal_test.go` | route `local.altinn.cloud/correspondence` → `hostPortCorrespondence` when the group is on, absent when off |
| reset | `env reset` also removes the `correspondence-blobs` volume |
| `.github/workflows/images.yml` | matrix entry `correspondence` and the name in the manifest job |
| `localtest/postgres/Dockerfile` | `COPY components/correspondence/postgres-init/70-correspondence.sh …` |
| Provisioning (`AppProvisioner`) | **nothing per app.** Resources are seeded in the Resource Registry. A service owner needs no `initialize-service-owner` call, because the default storage is used when the service-owner row is missing |

**Why a separate group, and not membership of `dialogporten`:**

- Correspondence cannot publish without Dialogporten, so it must not run without it.
- Dialogporten users (the adapter, Arbeidsflate, dialog tests) do not need Correspondence's ~200 MB and its Hangfire
  polling.
- Apps without signing never call it.

The implication keeps `--with correspondence` a single flag. The simpler alternative is to make it a fourth member of
the `dialogporten` group: no Go change to group resolution, at the cost of ~200 MB whenever Dialogporten runs
(question 2). **Without the group**, apps keep today's behaviour: the signing flow's sends get the proxy's 404 and are
swallowed (section 2.1).

### 6.12 Verification plan (`components/correspondence/smoke-test.sh`, through the proxy, needs `--with correspondence`)

1. `GET /health` 200 on 8033, and `GET correspondence/api/localtest/status` shows the expected migration count and
   two EdDSA keys.
2. No token on `POST correspondence/api/v1/correspondence` gives 401. A `ttd` org token without
   `altinn:correspondence.write` gives 403. With the scope, an unknown resource gives 400 `CORR-01036`.
3. The app-lib sequence with `/Home/GetTestOrgToken?scopes=altinn:serviceowner/instances.read
   altinn:serviceowner/instances.write altinn:correspondence.write`:
   1. `POST attachment` for `ttd-localtest-correspondence`.
   2. `POST attachment/{id}/upload` with a small file and its MD5.
   3. `GET attachment/{id}` reaches `Published` within 10 s (the simulated scan).
   4. The bytes are at `correspondence/api/localtest/blobs/{id}`.
4. `POST correspondence` to Sophie Salt with that attachment and an e-mail notification gives 200 `Initialized`.
   Within 30 s, `GET correspondence/{id}/details` says `Published`; the external references hold a dialog id; the
   dialog exists at `GET dialogporten/api/v1/serviceowner/dialogs/{dialogId}` with the right `serviceResource`, party
   and content URL.
5. The Notifications capture holds an e-mail to `1337@altinnstudiotestusers.com` with `sendersReference` `corr-…` and
   creator `digdir`. The condition check succeeded: the Notifications log has no failed condition call, and the message
   was sent.
6. A wrong checksum on upload gives `Failed` with the hash-mismatch text. A recipient outside the seed gives the
   production error for an unknown party.
7. Recipient path with a dialog token (task 10's method): a code-flow token for user 1337 from
   `localtest/api/authentication/token` → `GET dialogporten/api/v1/enduser/dialogs/{id}` for `dialogToken` → `GET
   correspondence/{id}/content` with it returns the body, and `POST {id}/markasread` and `POST {id}/confirm` answer 200.
   The status history shows `Read` and `Confirmed`, and the dialog gets the opened and confirmed activities.
8. `DELETE correspondence/{id}/purge` as the sender gives `PurgedByAltinn`/`PurgedByRecipient` per production, and the
   dialog is purged.
9. A correspondence with `RequestedPublishTime` 30 s ahead stays `ReadyForPublish` until then, and `jobs?state=Scheduled`
   lists its publish job.
10. Restart the container: the correspondences remain; no migration runs again.
11. `signering-brukerstyrt` with `studioctl run` and `--with correspondence`: the call-to-sign reaches Correspondence;
    `HasBeenMessagedForCallToSign` is true for a signee on `integrasjon4`; the receipt correspondence publishes with
    the signed PDF attachment.
12. Group off: `/correspondence/...` returns the proxy's 404; `run-platform-regression.sh` stays green.

Run next to the Dialogporten, Notifications, Resource Registry and Register suites (the latter with the KRR and Profile
checks).

### 6.13 Effort and risk

**7 to 8 days** to a green smoke test:

| Work | Days |
| --- | --- |
| Vendoring, props/targets, Dockerfile | 0.5 |
| Hooks, configuration, file storage, auth options, resource manager, Maskinporten copy | 1.5 |
| Postgres script and first run | 0.5 |
| Inspection API including the Hangfire queries | 1 |
| Resource Registry seed (three resources and policies) | 0.5 |
| Register: KRR and the two Profile routes, with checks | 0.75 |
| studioctl group, implication, Redis sharing, image workflow, tests | 1 |
| End-to-end through Dialogporten and Notifications, the dialog-token checks, `signering-brukerstyrt`, fixes | 1.25 to 2.25 |

| Risk | Note |
| --- | --- |
| Five-component path | Resource Registry, PDP, Register, Dialogporten and Notifications each have to accept Correspondence's exchanged `digdir` token and the new resources. Every failure shows as a Hangfire retry, which is why the inspection API exposes jobs |
| Swallowed migration errors | 6.6; the smoke test checks the migration count |
| Dialogporten's correspondence rules | the `digdir:dialogporten.correspondence` scope rules, unopened-content handling and the Resource Registry cache (20 minutes in Dialogporten, 10 in Correspondence) are read, not run. The seed is applied at start, so the caches see it from the first call |
| Order of start | Correspondence before Dialogporten's JWKS is up. Handled by the deps and the refresh-on-miss resolver |
| Upstream activity | `main` moves weekly. Migrations are EF and additive; the two hooks sit at stable lines (the configuration chain and the end of `ConfigureServices`) |
| Development semantics | lifetime validation off, malware simulated, migrations at start. All upstream behaviour, documented as differences |
| Image | Debian base, unpinned upstream; size and build time not measured |

## 7. Where a developer sees a sent correspondence

1. **Correspondence itself:** `correspondence/api/localtest/correspondences` (list) and `…/{id}` (status history,
   attachments, notifications, dialog id, jobs), plus the production `GET correspondence/{id}/details` with the
   sender's token. Swagger at `/correspondence/api/v1/swagger` (Development; not verified that the path base matches).
2. **Dialogporten:** the dialog through the service-owner API (by id; the search pain point applies), the end-user API
   with a task 2 token, later Arbeidsflate (task 11).
3. **Notifications:** the inboxes at `notifications/api/localtest/inboxes`, creator `digdir`, `sendersReference`
   `corr-…`.
4. **Events:** `events/api/localtest/` shows the `app.correspondence.*` events Correspondence publishes (not verified
   that Events accepts them).
5. **The app:** the signee list in the signing task stops showing "notification failed".

## 8. Known differences from production

1. `Development` environment:
   - token lifetime not validated (`ValidateLifetime` false on all three JWT schemes)
   - internal endpoints in Swagger
   - exception messages in problem details
   - migrations at start
2. Malware scanning is simulated ("No threats found") by upstream's Development code. There is no infected-file path.
3. Blobs are files on a volume. SAS URLs are plain anonymous localtest URLs. There is one default storage account
   (no per-service-owner accounts; no `initialize-service-owner`).
4. KRR answers "not reserved" for every seeded person. Unknown persons get no entry.
5. Correspondence acts as `digdir` towards the platform (`UseAltinnTestOrg=false`), as Dialogporten does. Apps act as
   `ttd`. The seeded policies add `ttd` to the service-owner rule; tt02's grant only `digdir`.
6. Dialog-token keys refresh on a key miss instead of every 12 hours.
7. Hangfire runs 2 workers and no migration server. The Hangfire dashboard is not mapped (it isn't in production either).
8. The fake Maskinporten's issuer contains "altinn" but not "maskinporten.no", so `RecipientScopePolicy` and
   `SenderScopePolicy` classify raw fake-Maskinporten tokens differently from real Maskinporten tokens (2.1). No stack
   consumer is affected; the Notifications condition check uses the Maskinporten scheme and scope check, not those
   policies.
9. The browser attachment download through OIDC does not work until task 11 (6.2).
10. Azure Resource Manager, Key Vault (JWK rotation), Slack, Application Insights, the Altinn 2 SBL bridge: off.
11. The 10-minute Resource Registry cache and the 24-hour Register party cache are production's (HybridCache over
    Redis). A changed `testData.json` name is not seen for up to 24 hours unless Redis database 1 is flushed and the
    container restarted.

## 9. Where localtest would be faster than production (rows for `localtest/README.md`)

| What | Production | Localtest |
| --- | --- | --- |
| Correspondence: Dialogporten dialog-token signing keys | fetched at start and every 12 h (`EdDsaSecurityKeysCacheService`) | fetched again on an unknown key, at most every 10 s |
| Correspondence: malware scan of an uploaded attachment | Defender scan, result through Event Grid (seconds to minutes) | simulated "clean" through a Hangfire job within seconds (upstream's Development behaviour) |
| Correspondence: reminder notification | 7 days after the first (`CreateNotificationOrderHandler.cs:217`) | 1 day (upstream's non-production value, unchanged) |
| Correspondence: unread-confidential reminder check | 7 days after publish | 1 minute (upstream's non-production value, unchanged) |
| Correspondence: minimum attachment expiry | 14 days | 1 day (upstream's non-production value, unchanged) |
| Correspondence: notification delivery | Notifications delivers through ACS and Link Mobility (minutes) | captured and `Delivered` within a second; Correspondence's own delivery check still runs 5 minutes after the send time (unchanged constant) |

Not faster, listed so nobody looks for a shortcut:

- Hangfire's 5 s schedule polling
- the 10-minute Resource Registry cache and 24-hour Register cache in Correspondence
- Hangfire's retry schedule for a failed dialog creation

## 10. Open questions for the product owner

1. **Default stack:** keep Correspondence opt-in (proposed; apps' signing sends keep failing quietly without the group),
   or add a fake-only Correspondence (Option B) to the default stack so signing flows get a "sent" without Dialogporten?
2. **Group:** its own `correspondence` group that turns on `dialogporten` (proposed), or a member of the `dialogporten`
   group (simpler, ~200 MB more whenever Dialogporten runs)?
3. **KRR:** an endpoint in the Register fakes through the production client (proposed), or an in-process
   `IContactReservationRegistryService` fake in Correspondence? And should the seed get a `reserved` flag now, shared
   with Notifications' open question 5?
4. **Profile's `correspondence/` lookups:** require the scope `altinn:profile/correspondence.notificationsettings.read`
   as production does (proposed), or anonymous like the four Notifications lookups?
5. **Seeded policies:** add `ttd` beside `digdir` in the service-owner rule of the tt02 copies (proposed), or change the
   localtest app's `GetTestOrgToken` so the app token says `digdir`? The latter would change every other org-token
   consumer.
6. **`apps-correspondence-integrasjon2`:** seed it as tt02 has it (`GenericAccessResource`, so Correspondence refuses
   it in production too), or as a `CorrespondenceService` so `signering-brukerstyrt`'s second task works locally?
7. **Base images:** pin `sdk:10.0` and `aspnet:10.0` by digest as a `localtest:` deviation (proposed), or follow
   upstream's floating tags?
8. **Attachment download from Arbeidsflate:** accept it as a task 11 item (a third, `#if !LOCALTEST` site in
   `IdportenTokenValidator.cs:26`, a seeded OIDC client, a cookie decision), or include it here?
9. **Failure visibility:** is the inspection API's `jobs` view enough, or should a failed dialog creation also shorten
   Hangfire's retries locally? That would need a directive, because the attribute is on the method.

## 11. Not verified

- Nothing was created through the stack. The paths from a valid `POST correspondence` onward have not run:
  - the PDP decision for a seeded resource
  - the Register correspondence endpoints
  - Dialogporten dialog creation with the `digdir` token and the correspondence scope
  - the Notifications order and its condition callback to Correspondence's Maskinporten scheme
  - the Events publish
  - the dialog-token calls
- The migrations ran on Postgres 18.6 in a scratch server, not on the shared image's 18.3.
- `FileStorageRepository`, the inspection API, the KRR and Profile routes and the studioctl wiring are not written.
  Their line counts are estimates.
- The Maskinporten-scheme metadata override was built and registered, but no Maskinporten-issued token was presented
  to it.
- Swagger's path under the proxy, the image size and build time, and the memory with real traffic were not measured.
- Option B's size is a judgement.
- App-lib behaviour comes from reading `origin/main`. `signering-brukerstyrt` was not run.
- That an organisation recipient without addresses still publishes (Notifications 422 → no order) is inferred from
  `AltinnNotificationService` (Notifications plan 2.2), not run.
- The clone is shallow, so the history of the upstream files was not read.
