# Localtest components: Events and Notifications

Research date: 2026-10-16. Sources: `altinn-studio` monorepo (main), `app-lib-dotnet` (main, 4740bb28),
`.reference/altinn-events` (41297b5, 2026-10-16), `.reference/altinn-notifications` (c9e1d23, 2026-10-16).
Read-only research; nothing was built or run. Statements about runtime behaviour of the real services are
from code reading only and are marked "not confirmed" where relevant.

## Executive summary

- **Both localtest emulations are write-only sinks.** Events: `POST events/api/v1/app` and `.../events` write the
  CloudEvent as JSON to disk; `POST .../subscriptions` echoes the request and never delivers anything. Notifications:
  `POST notifications/api/v1/orders/email|sms` run copied validation + contact-point lookup and write the order as JSON to
  disk. Nothing is ever sent, delivered, or shown in a UI.
- **Drift is large but mostly irrelevant.** The Notifications copy was last synced with production 2024-06-18
  (app-localtest#107); production `OrderRequestService`/`ContactPointService` are 4-5x larger now. The Events copy is a
  2020-2023 shape (custom CloudEvent model; production now uses CloudNative SDK, Postgres, Wolverine/ASB).
- **Version gap that matters:** current app libraries (v8 `app-lib-dotnet` and v9 monorepo) call
  `notifications/api/v1/future/orders` (instantiation notification) and `.../orders/{id}/cancel`
  (v9 cancel on process end). **Localtest implements neither** (404). v9 retired the Events receive stack
  (`IEventHandler`, `EventsReceiverController`, subscriptions) in altinn-studio#19859, so only v8 still calls
  `events/api/v1/subscriptions`.
- **Running the real services is heavy and has hard blockers.** Both need Postgres + Azure Service Bus (the ASB emulator,
  which itself needs an MS SQL Server 2022 container), Key Vault-backed platform access token validation, and register /
  profile endpoints localtest does not have. Events refuses `http://` subscription endpoints (apps run on
  `http://local.altinn.cloud`), so webhooks to apps cannot work unmodified. Notifications has no dev email/SMS sink; real
  sending needs Azure Communication Services / Link Mobility credentials.
- **Subtree merge is not worth it**: ~31k lines (Notifications API) / ~12k lines (Events + Functions) of production code
  whose value locally is persistence/queueing/delivery that we would stub anyway.
- **Recommendation:** keep thin in-memory/disk stubs, but rewrite them as small, contract-shaped fakes (delete the
  copied Core/validators), add the missing `future/orders` and `orders/{id}/cancel` endpoints, and optionally add a
  "sent notifications / registered events" page in localtest. Optionally emulate delivery (events to an app's
  `eventsreceiver`, call the notification condition endpoint) in the stub if v8 users need it.

## 1. Provenance and drift

### Files in localtest

| Area | Files | Lines |
|---|---|---|
| Events controllers | `src/Controllers/Events/{AppController,EventsController,SubscriptionController}.cs` | 83 / 95 / 65 |
| Events services | `src/Services/Events/Implementation/{EventsRepository,SubscriptionService}.cs`, `Interface/*` | 132 / 70 / ~90 |
| Events models | `src/Models/Events/{CloudEvent,Subscription,SubscriptionRequestModel,ServiceError}.cs` | ~225 |
| Notifications | `src/Notifications/{API,Core,LocalTestNotifications}` | ~4100 (whole set incl. events: 4893) |

(All paths relative to `altinn-studio/src/Runtime/localtest/`.)

`src/Notifications/README.md` documents the copy policy: Core "can be copied directly", controllers copied with auth
changes, persistence re-implemented on disk. `src/LocalTest.csproj` defines `LOCALTEST` so copied files can carry
`#if !LOCALTEST` guards; only the two Notifications controllers (6 guards each) and Storage `DataController` use it.

### History

Monorepo (`git log -- src/Runtime/localtest/src/Notifications src/Runtime/localtest/src/Controllers/Events`): 6 commits,
none of them a production sync:

```
2c80c7bd0d 2026-09-21 feat(app)!: send an idempotency key when raising Altinn events (#20584)
841f4bfdbe 2026-08-31 chore!: fix all existing typos ... (#19835)
6c7511864d 2026-08-21 chore(localtest): upgrade to .NET 10 (#20099)
1166612623 2026-06-04 Merge commit ...
c51304927d 2026-02-19 feat: localtest - migrate to yarp ... (#17839)
e7f38bd6a7 2025-09-23 Add 'src/Runtime/localtest/' from commit 'd9c4e618...'
```

Pre-monorepo history (second parent of the subtree add, d9c4e618):

```
bcfe7a7da4 2024-11-26 Make Localtest compile without warnings (#127)
812906e2f0 2024-06-18 latest notifications validation logic (#107)   <- last Notifications sync
1e28413c2b 2024-06-11 new notification features (#101)
e18334337e 2024-04-11 Support SMS notifications (#94)
2200dd5472 2023-11-28 add controller for handling generic events (#69)
be9992676f 2023-11-20 notifications funcitonality added (#62)
98e336a3fb 2022-10-28 Add more test users and support for subscribing to events in localtest (#9092)
dfc2bebd1f 2020-10-02 Client code for the events component in app template (#4861)
```

The only recent functional change (idempotency key, #20584) was written by hand in localtest to mirror production; it
was not copied.

### Notifications drift (localtest `src/Notifications/{API,Core}` vs `components/api/src/Altinn.Notifications{,.Core}`)

Diff of 86 files, ignoring `using`/`namespace`/whitespace (changed lines, localtest/prod line count):

- ~19 files identical, ~25 files trivially different (<= 6 lines, mostly nullable/style).
- 6 localtest files no longer exist in production: `IBaseNotificationOrderExt`, `OrderIdExt`, `ValidationConstants`,
  `NotificationOrderConfig`, `IResultType`, `GenericServiceUpdate`.
- Heavy divergence in the parts that carry behaviour:

| File | Changed lines | Localtest / prod lines |
|---|---|---|
| `Core/Services/ContactPointService.cs` | 794 | 212 / 791 |
| `Core/Services/OrderRequestService.cs` | 619 | 148 / 690 |
| `Core/Persistence/IOrderRepository.cs` | 243 | 54 / 290 |
| `API/Mappers/OrderMapper.cs` | 219 | 247 / 364 |
| `Core/Models/Orders/NotificationOrder.cs` | 63 | 127 / 163 |
| `API/Validators/EmailNotificationOrderRequestValidator.cs` | 62 | 78 / 27 (prod moved logic) |
| `Core/Enums/OrderProcessingStatus.cs` | 43 | 15 / 47 |
| `API/Controllers/{Email,Sms}NotificationOrdersController.cs` | 39 each | ~91 / 79 |

Production Notifications API today: 28.7k C# lines + 214 SQL migration files (API), plus shared (0.7k), email-service
(3.1k), sms-service (1.9k). Production has 15 controllers (`orders`, `orders/{id}`, `orders/{id}/status`,
`orders/{id}/cancel`, `orders/{id}/notifications/email|sms`, `future/orders`, `future/orders/instant`,
`future/orders/composed-email`, `future/shipment/{id}`, `future/shipment/feed`, `future/dashboard`, metrics, trigger,
tests/sendcondition). Localtest has 2 (`orders/email`, `orders/sms`).

### Events drift (localtest vs `src/Events` in altinn-events)

| Localtest | Production | Changed lines / sizes |
|---|---|---|
| `Controllers/Events/AppController.cs` | `src/Events/Controllers/AppController.cs` | 292; 83 / 309 |
| `Controllers/Events/EventsController.cs` | `src/Events/Controllers/EventsController.cs` | 191; 95 / 210 |
| `Controllers/Events/SubscriptionController.cs` | `src/Events/Controllers/SubscriptionController.cs` | 188; 65 / 217 |
| `Services/Events/Implementation/SubscriptionService.cs` | `src/Events/Services/SubscriptionService.cs` | 204; 70 / 220 (localtest is all `NotImplementedException` except Create) |
| `Models/Events/Subscription.cs` | `src/Events/Models/Subscription.cs` | 32; 77 / 93 |
| `Models/Events/SubscriptionRequestModel.cs` | `src/Events/Models/SubscriptionRequestModel.cs` | 16 |
| `Models/Events/CloudEvent.cs` | none (prod uses `CloudNative.CloudEvents`) | n/a |

Only controller routes, class names and model shapes survive; logic is localtest-specific. Production Events is 9.5k
lines (+ 2.2k `Events.Functions`), with Postgres repository, Wolverine handlers (registration/inbound/outbound/
validation), PDP authorization, register lookups, webhook delivery and log endpoints.

## 2. What apps actually call

### Events

| Caller | Endpoint | Localtest |
|---|---|---|
| v9 `src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Events/EventsClient.cs` (`AddEvent`, POST `"app"` with JWT + platform access token + optional `Idempotency-Key`) | `events/api/v1/app` | Implemented (writes `events/` JSON + idempotency claim file) |
| v8 `app-lib-dotnet/src/Altinn.App.Core/Infrastructure/Clients/Events/EventsClient.cs` | `events/api/v1/app` | Implemented |
| v8 `.../Clients/Events/EventsSubscriptionClient.cs` (`AddSubscription`, endpoint `{appBaseUrl}api/v1/eventsreceiver?code=...`, sourceFilter app URL) | `events/api/v1/subscriptions` | Echo only; no id, no validation event, no delivery |
| v8 `Altinn.App.Api/Controllers/EventsReceiverController.cs` + `IEventHandler` | (inbound webhook from Events) | Never called by localtest |
| none in app libs | `events/api/v1/events` (generic) | Implemented (disk) |

Event publishing in v9 is triggered from workflow-engine commands `InstanceCreatedAltinnEvent`,
`MovedToAltinnEvent`, `CompletedAltinnEvent` (`Altinn.App.Core/Internal/WorkflowEngine/Commands/AltinnEvents/`), gated by
`AppSettings.RegisterEventsWithEventsComponent` (`ProcessNextRequestFactory.cs:540,550`). v9 removed the receive stack in
altinn-studio#19859 (commit 357bf53456, 2026-08-10; studioctl upgrade has `RemovedEventsReceiveStackDetector`). The docs
already say localtest cannot deliver inbound events
(`altinn-studio-docs/content/altinn-studio/v8/reference/logic/events/subscribing/_index.en.md:15`).

App integration tests do not use localtest Events; `workflow-engine-side-effects` uses a `ControlledEventsClient` stub
(`src/App/backend/test/Altinn.App.Integration.Tests/_testapps/basic/_scenarios/workflow-engine-side-effects/services/SideEffectsScenario.cs`).

### Notifications

Same four clients in v9 (`src/App/backend/src/Altinn.App.Core/Features/Notifications/`) and v8 (`app-lib-dotnet`):

| Client | Endpoint | Used by | Localtest |
|---|---|---|---|
| `Email/EmailNotificationClient.cs` (`IEmailNotificationClient`, public) | `POST orders/email` | Custom app code | Implemented |
| `Sms/SmsNotificationClient.cs` (`ISmsNotificationClient`, public) | `POST orders/sms` | Custom app code | Implemented |
| `Future/NotificationOrderClient.cs` | `POST future/orders` | `NotificationService.NotifyInstanceOwnerOnInstantiation` via workflow command `NotifyInstanceOwnerOnInstantiation` (v9) / `InstancesController.cs:440,770` (v8); sets `ConditionEndpoint` back to app `NotificationCallbackController` (`{org}/{app}/api/v1/notification-webhook-listener/...`) | **Missing (404)** |
| `Order/NotificationCancelClient.cs` | `POST orders/{id}/cancel` | v9 `Cancellation/CancelOnProcessEnd.cs` (v8 registers `SendOnProcessNotEnded` instead, i.e. relies on the condition endpoint) | **Missing (404)** |

Signing call-to-action notifications go via Correspondence (`SigningCallToActionService.WithNotificationIfConfigured`),
not the Notifications API (Correspondence strand). Shipment/status-feed endpoints are not called by the app libraries.

## 3. Running the real services as containers

### Events (`altinn-events`)

- **Image:** `Dockerfile` (root) builds `src/Events` + `DbTools`, port 5080, runs Yuniql migrations from `Migration/` at
  startup when `PostgreSQLSettings:EnableDBConnection`. `docker-compose.yml` only builds/runs that one container
  (no DB, no queues). No published image used by others is referenced (not confirmed whether one exists in a public registry).
- **Postgres:** db `eventsdb`, roles `platform_events_admin` (superuser) / `platform_events`, manual setup per README.
  Migrations use `CREATE EXTENSION pg_trgm` (contrib, in the stock postgres image). Could share the workflow-engine
  Postgres container with an init script, like `components/workflow_engine.go` does with `postgres-init.sql`.
- **Messaging:** Wolverine (`Extensions/WolverineServiceCollectionExtensions.cs`). If any `WolverineSettings:Enable*` is
  true, Azure Service Bus is used (dev: `emulator/docker-compose.yaml` = `mcr.microsoft.com/azure-messaging/servicebus-emulator`
  + `mcr.microsoft.com/mssql/server:2022-latest`, `ACCEPT_EULA`, SA password). If all false, the legacy path publishes to
  Azure Storage Queues (Azurite) and delivery is done by `src/Events.Functions` (Azure Functions isolated worker, queue
  triggers; no Dockerfile). Default `appsettings.json` has `EnableOutboundListener=false` (outbound handled by Functions),
  but `Wolverine/Handlers/OutboundEventHandler.cs` and `ValidationEventHandler.cs` live in the API, so enabling all
  listeners in one container + ASB emulator is plausibly enough (not confirmed). `RegisteredEventsBackgroundService` polls
  Postgres for registered events.
- **Key Vault / telemetry:** `Program.cs:107-111` adds Key Vault if `kvSetting:SecretUri` set; App Insights exporters
  only if connection string set (`Program.cs:323`). OTel otherwise.
- **Inbound auth:** `AddJwtCookie` against `GeneralSettings:OpenIdWellKnownEndpoint` (localtest serves
  `authentication/api/v1/openid/.well-known/openid-configuration`, so app tokens should validate; issuer/audience not
  validated). `events/api/v1/app` requires policy `PlatformAccess` (platform access token) and checks the token issuer
  against `Source.AbsolutePath`; generic `events` needs `altinn:events.publish` or platform access; subscribe/query need
  `altinn:events.subscribe` (non-app subscriptions). Access token validation uses `Altinn.Common.AccessToken`
  `PublicSigningKeyProvider`, which reads issuer certificates **from Azure Key Vault** (decompiled 5.1.15); escape hatch
  `AccessTokenSettings:DisableAccessTokenVerification` exists. Whether the provider can be constructed without a
  `kvSetting:SecretUri` is not confirmed.
- **Outbound deps:** `PlatformSettings:RegisterApiBaseAddress` - `RegisterApiClient` calls
  `v2/internal/parties/query?fields=identifiers` and `v2/internal/parties/main-units` plus party lookup (localtest only
  has `register/api/v1/...`); `PlatformSettings:ApiAuthorizationEndpoint` - PDP via `Altinn.Common.PEP` (localtest has
  `authorization/api/v1/decision`); `ApiProfileEndpoint`; outgoing platform access tokens via `AccessTokenGenerator`
  (signing cert source not confirmed). `AppsDomain` (default `apps.altinn.no`) decides app vs generic subscription.
- **Webhooks to the app through localtest's proxy:** **No, not unmodified.** `SubscriptionController.IsValidUri`
  (line 191) requires `Uri.Scheme == https` for `EndPoint` and `SourceFilter`; apps run at
  `http://local.altinn.cloud/{org}/{app}/` (`GeneralSettings.ExternalAppBaseUrl` default). The subscription also goes
  through an async validation round-trip (`SubscriptionService.SendAndValidate` posts a validation event to the endpoint,
  which v8's `EventsReceiverController` answers). Plus PDP authorization of each event per subscriber. And v9 apps no
  longer have a receiver at all.

### Notifications (`altinn-notifications`)

- **Images:** `components/api/Dockerfile` (port 5090, copies migrations and Views), `components/email-service/Dockerfile`,
  `components/sms-service/Dockerfile`. No compose file for the app itself; `tools/asb-emulator/docker-compose.yaml`
  (same ASB emulator + MSSQL pair), `tools/dev-setup/dbsetup.sh` for roles. `getting-started.md` describes running with
  `dotnet run`.
- **Postgres:** `notificationsdb`, roles `platform_notifications_admin` / `platform_notifications`, migrations in
  `Altinn.Notifications.Persistence/Migration` (214 SQL files, no `CREATE EXTENSION`).
- **Messaging:** Azure Service Bus via Wolverine is unconditional in the API
  (`Altinn.Notifications.Integrations/Extensions/WolverineServiceCollectionExtensions.cs` - listeners for email/SMS send
  results, delivery reports, rate limit; publishers for send queues). Kafka is gone (no references). Order processing is
  an in-process `ProcessItemsBackgroundService` (`SELECT ... FOR UPDATE SKIP LOCKED`); sending is triggered by cron
  jobs calling `TriggerController` (`docs/architecture/notifications.md` "Cron jobs": `send-email-trigger` every minute,
  etc., defined in the private altinn-studio-ops repo).
- **Providers:** email-service uses Azure Communication Services (`CommunicationServicesSettings:ConnectionString`),
  sms-service uses Link Mobility (`SmsGatewaySettings`). **No dev sink/fake** exists in any component (grep for
  fake/stub/sink/smtp found nothing). Without the email/sms services, orders and notifications sit in Postgres
  unsent, and their result listeners never fire.
- **Key Vault:** `AddSecretsFromKeyVault` (Program.cs:238-257); dev settings use a placeholder vault URI.
  App Insights optional.
- **Inbound auth:** JWT cookie against OpenID metadata (default points at AT23); policies
  `POLICY_CREATE_SCOPE_OR_PLATFORM_ACCESS` (`altinn:serviceowner/notifications.create` or platform access token),
  dashboard, composed email, end-user. Same Key Vault-backed access token validation issue as Events.
- **Outbound deps:** Profile `users/contactpoint/lookup`, `users/contactpoint/lookupsi`, `units/contactpoint/lookup`,
  `organizations/notificationaddresses/lookup`; Register `parties/nameslookup` (+ org contact points); Authorization
  decision; Dialogporten (`DialogportenSettings:Enabled=true` by default, AT23 URL); `SendConditionClient` calls the
  app's condition endpoint with a **Maskinporten** token (`SendConditionClient:MaskinportenSettings`). Localtest has none
  of the profile/register contact-point endpoints (its `LocalProfileClient`/`LocalRegisterClient` bypass HTTP).

### Effort to add to studioctl

Pattern exists (`src/cli/internal/cmd/env/localtest/components/workflow_engine.go`: pulled/dev-built image, Postgres
container with init SQL + healthcheck, env vars, bound-topology destination). Per service you would add: service
container, DB init, ASB emulator + MSSQL (shared between both, ~2 GB RAM; MSSQL 2022 image is amd64-only, arm64 Macs need
emulation or Azure SQL Edge - not verified here), Key Vault bypass, and new localtest endpoints (register v2 internal,
profile contact points). For Notifications also email/SMS workers with a fake provider that does not exist upstream,
and cron triggers. The images would also need to be published somewhere studioctl can pull (not confirmed that they are).

## 4. Developer-visible behaviour of localtest's emulation

None beyond "the call succeeds":

- `LocalTestNotifications/LocalOrderRepository.cs` writes `{LocalTestingStorageBasePath}notifications/orders/{id}.json`;
  every read method throws `NotImplementedException`. No controller or view lists orders (`src/Views` has FrontendVersion,
  Home, Shared, StorageExplorer, TenorUsers; grep for notif/cloudevent finds nothing).
- `LocalProfileClient`/`LocalRegisterClient` resolve recipients from localtest test data, so an app gets a realistic
  "recipient lookup" response (missing contact point -> failure), which is the one behaviour developers can observe.
- `LocalAuthorizationService` allows everything.
- Events: JSON files under `{DocumentDbFolder}events/`, idempotency claims under `events/idempotency/`. Nothing reads them.

## 5. Verdict

| | (a) Subtree merge | (b) Real image(s) via studioctl | (c) Thin stub (keep/rewrite) |
|---|---|---|---|
| **Events** | Poor. 12k lines whose core is Postgres + Wolverine/ASB + PDP + Register v2; would need the same infra as (b) or extensive `#if LOCALTEST` surgery. Separate repo with own release cadence. | Feasible but heavy: Postgres + ASB emulator (MSSQL) + access-token/Key Vault bypass + register v2 endpoints in localtest. **Blockers:** https-only subscription endpoints (apps are http), v9 apps have no receiver, MSSQL/arm64 and EULA friction. Value: only `POST app` is used, which is fire-and-forget. | **Recommended.** ~100 lines: accept `POST app`/`events` (+ idempotency), keep on disk/in memory; optionally list them in a localtest page. Subscriptions only matter for v8; optionally emulate delivery to `eventsreceiver` in-stub if v8 demand exists. |
| **Notifications** | Poor. 31k+ lines plus workers; the copied Core already drifted 2+ years because syncing is manual. | Heavy: API + Postgres + ASB emulator (MSSQL) + email/SMS workers with **no dev sink** + cron triggers + Maskinporten for condition checks + profile/register contact-point endpoints + Key Vault bypass. **Blockers:** no fake provider upstream, Key Vault-based token validation, missing localtest endpoints. | **Recommended.** Replace the ~4k-line copy with a contract-level fake: `orders/email`, `orders/sms`, **add `future/orders` (404 today for v8 and v9 instantiation notifications) and `orders/{id}/cancel` (v9)**, keep recipient resolution from test data, store orders, and optionally a localtest UI "sent notifications" (rendered email/SMS) and calling the app's `ConditionEndpoint`. |

Hard blockers for (b), summarised:
1. Events subscription endpoint and source filter must be `https` (`SubscriptionController.cs:191-194`).
2. Platform access token validation fetches certs from Azure Key Vault (`PublicSigningKeyProvider`); must be disabled via
   `AccessTokenSettings:DisableAccessTokenVerification` (works for the handler; KV client construction not confirmed).
3. Both require Azure Service Bus; locally only via the ASB emulator, which requires an MSSQL Server container.
4. Notifications has no non-production email/SMS sink.
5. Localtest lacks register v2 internal endpoints (Events) and profile/register contact-point lookup endpoints
   (Notifications); those belong to the Register/Profile strands.

Overall: these are fire-and-forget side effects for an app developer. The cheapest correct option is a small stub that
matches the app-facing contracts (including the missing `future/orders` and cancel endpoints), plus optional visibility
in the localtest UI. Revisit (b) only if Events/Notifications ship a supported "local mode" (in-memory transport, fake
providers, file-based signing keys).
