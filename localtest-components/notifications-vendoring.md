# Notifications in localtest: vendoring production Altinn Notifications, with a capture sink for e-mail and SMS

Research date: 2026-10-18. Read-only; nothing was built or run. Runtime behaviour is from code reading and is marked
"not confirmed" where it matters.

Sources and pins:

- [`Altinn/altinn-notifications`](https://github.com/Altinn/altinn-notifications/tree/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a) at `c9e1d233` (main, 2026-10-16, "Sms mock (#1942)"), clone in `.reference/altinn-notifications`.
  `Altinn/altinn-notifications-email` and `Altinn/altinn-notifications-sms` are **archived** (last push 2026-03-02); both
  services now live in this repo under `components/email-service` and `components/sms-service`. No separate clone is needed.
- App-lib v9: `altinn-studio` `origin/main` [`02839095`](https://github.com/Altinn/altinn-studio/tree/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend). App-lib v8: [`app-lib-dotnet` `8d059b8c`](https://github.com/Altinn/app-lib-dotnet/tree/8d059b8c5e92df15905f273fa1c22dda91113da4).
- Correspondence: [`altinn-correspondence` `6dcf5619`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89). Profile: [`altinn-profile` `de3e9fe3`](https://github.com/Altinn/altinn-profile/tree/de3e9fe30044848c904fb0625d899b9f4e4973c5).
- Earlier strand: [`events-notifications.md`](events-notifications.md) (16 Oct). This report supersedes its Notifications verdict.

Link shorthand below: **N** = `https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/`.

## Summary

- **Recommendation: vendor all three Notifications hosts (API, email-service, sms-service) in one component**, the way
  Storage and Access Management are vendored. Replace Azure Service Bus with Wolverine's PostgreSQL transport on the
  shared Postgres, and replace the two external providers (Azure Communication Services and Link Mobility) with
  **capture sinks** at their production interfaces, `IEmailServiceClient` and `ISmsClient`. Estimate: about 9
  directive sites, about 1 000 to 1 300 lines of fakes, 6 to 9 working days including a one-day transport spike first.
- **The 16 October blockers have mostly gone:**
  - There is no Kafka (no references).
  - Sending no longer needs cron jobs: an in-process `ProcessItemsBackgroundService` drives it.
  - Since 16 October upstream has mock e-mail and SMS clients behind configuration flags ([#1942](https://github.com/Altinn/altinn-notifications/pull/1942), ACS emulator [#1854](https://github.com/Altinn/altinn-notifications/pull/1854)).
  - Key Vault loads only when four settings are present.
  - Register and PDP now exist as production components.
  - The one hard Azure dependency left is ASB. It is unconditional in all three hosts, and it is exactly what the vendoring
    model is designed to fake.
- **Inspection seam:** the e-mail and SMS clients see the final content: recipient, sender, subject, body, content type,
  and the notification id. They do not see the order. The order context comes from a join on the notification id against
  the production tables, done by one localtest controller in the API host. That controller is the API the GUI uses
  (contract in section 4).
- **Plan B**, if the transport spike fails: run the API host only and capture at its three Wolverine publishers on
  in-process queues. That is 1 container instead of 3 and about 4 directive sites, but it fakes Notifications' own
  e-mail and SMS services rather than an external party.

## 1. Who calls Notifications

### App libraries (v8 and v9 identical on the wire)

- **Base URL:** `PlatformSettings.ApiNotificationEndpoint`. `run-app.sh` already sets it to `{platform}/notifications/api/v1/`.
- **Auth:** every call carries only a `PlatformAccessToken` header with the app's own token (`GenerateAccessToken(org, app)`).
  No Maskinporten, no user JWT ([v9 `ServiceCollectionExtensions.cs#L273-L283`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Extensions/ServiceCollectionExtensions.cs#L273-L283)).

| Call | Trigger | Key body fields |
| --- | --- | --- |
| `POST future/orders` ([v9](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Features/Notifications/Future/NotificationOrderClient.cs#L50-L77), [v8](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Features/Notifications/Future/NotificationOrderClient.cs#L50-L60)) | Instantiation with a `notification` part. v9: the workflow-engine command `NotifyInstanceOwnerOnInstantiation`, retryable on failure ([command](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Internal/WorkflowEngine/Commands/NotifyInstanceOwnerOnInstantiation.cs#L37-L77)). v8: inline in `InstancesController`, log only ([L435-L450](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Api/Controllers/InstancesController.cs#L435-L450)) | See the list below the table ([`NotificationService.cs#L172-L287`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L172-L287)) |
| `POST orders/email`, `POST orders/sms` | Custom app code only (`IEmailNotificationClient`, `ISmsNotificationClient`) | Old v1 order shape; returns `{orderId}` |
| `PUT orders/{id}/cancel` | **None.** `NotificationCancelClient` exists in v8 and v9 but is neither registered in DI nor called. The 16 October report's "v9 cancel on process end" no longer holds on `origin/main` (no `CancelOnProcessEnd`) | — |

Key fields of the `future/orders` body:

- `sendersReference = "app-{instanceId}"`
- `idempotencyId = "{instanceId}-init"`
- `requestedSendTime` = the requested time, or **now + 5 min**
- `conditionEndpoint` = the app's `notification-webhook-listener`
- `recipient` is one of `recipientPerson`, `recipientOrganization` or `recipientExternalIdentity`, each with `emailSettings`, `smsSettings` and `resourceId` (`urn:altinn:resource:app_{org}_{app}`)
- `reminders[]`

**Condition endpoint (Notifications calls the app back).** The route is
`GET {org}/{app}/api/v1/notification-webhook-listener/{partyId}/{instanceGuid}?code=…`.

- The endpoint is `[AllowAnonymous]`. The `code` query parameter is an HS256 JWT that the app signed itself.
- It returns `{ "sendNotification": bool }`.
- It reads the instance from Storage as the service owner. In localtest that token comes from the authentication
  component's `/Home/GetTestOrgToken`
  ([`AuthenticationTokenResolver.cs#L97`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs#L97)).
- Notifications accepts `http` condition endpoints ([`UriRules.cs#L11`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Validators/Rules/UriRules.cs#L11)), so the app's `http://local.altinn.cloud:8000/...` URL validates.
- Notifications attaches a Maskinporten token on that call. The app ignores it.

**Signing call-to-action** goes through Correspondence (`SigningCallToActionService…WithNotificationIfConfigured`), not
the Notifications API directly.

### Correspondence (production)

[`AltinnNotificationService.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Notifications/AltinnNotificationService.cs) makes the following calls through a Maskinporten-authenticated client:

- **Scopes:** `altinn:serviceowner/notifications.create`, `.read` and `notifications.composedemail.create`.
- **Without a Maskinporten `ClientId`** it switches to a no-op `AltinnDevNotificationService`.

| Call | Use |
| --- | --- |
| `POST future/orders` | At publish, via Hangfire. `sendersReference = "corr-…"`, `recipientPerson` / `recipientOrganization` / `recipientEmail` / `recipientSms`, reminders. `requestedSendTime` is at least now + 20 s |
| `GET future/shipment/{id}` | Delivery check (Correspondence's own scheduled Hangfire job) |
| `GET orders/{id}/status` (+ the `links.self` e-mail/SMS URLs) | Legacy orders |
| `POST future/orders/composed-email` | Forwarding a correspondence to an e-mail address (SAS attachments) |
| `PUT orders/{id}/cancel` | Interface only, no callers |

- **Condition endpoints:** Correspondence serves its own, `…/correspondence/{id}/notification/check[/main]`, behind the
  Maskinporten scheme and scope `altinn:system/notifications.condition.check`. It sends `null` instead when its own host
  is localhost.
- **Auth:** whether Correspondence's token reaches Notifications as a raw Maskinporten token or an exchanged Altinn token
  is **not confirmed**. Notifications validates Altinn JWTs only, against `OpenIdWellKnownEndpoint`. This matters once a
  Correspondence component exists.

### Today's localtest

Only `POST orders/email` and `POST orders/sms` exist, with an on-disk repository. There is no `future/orders`, so an
instantiation notification fails for every app version, and there is no condition callback, status or cancel.

## 2. Production structure at `c9e1d233`

| Host | Projects (non-test) | C# lines | Port | Role |
| --- | --- | --- | --- | --- |
| API [`api/Dockerfile`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/Dockerfile) | `Altinn.Notifications`, `.Core`, `.Integrations`, `.Persistence`, `DbTools` + `shared` | 28.7k | 5090 | Orders, processing, contact points, status, all public endpoints |
| email-service | `Altinn.Notifications.Email`, `.Core`, `.Integrations` + `shared` | 3.1k | 5091 | Wolverine consumer → ACS; status polling; `instantemail` |
| sms-service | `Altinn.Notifications.Sms`, `.Core`, `.Integrations` + `shared` | 1.9k | 5092 | Wolverine consumer → Link Mobility; delivery-report webhook; `instantmessage/send` |
| shared | `Altinn.Notifications.Shared` | 0.7k | – | Wolverine commands, ASB defaults, publisher base |

The build uses central package management: WolverineFx 6.49.0, `Altinn.Common.AccessToken` 5.1.15, `Altinn.Common.PEP` 4.2.3,
`Altinn.ApiClients.Maskinporten` 10.1.0, Npgsql 10, Yuniql. `TreatWarningsAsErrors` is on. StyleCop runs in Debug only.

**Postgres.**

- **Database and migrations:** `notificationsdb`, roles `platform_notifications_admin` and `platform_notifications`. 214 Yuniql
  scripts run at start by `app.SetUpPostgreSql` ([`Program.cs#L96`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Program.cs#L96)). No `CREATE EXTENSION`.
- **Tables:** `orders`, `emailtexts`, `emailnotifications`, `smstexts`, `smsnotifications`, `orderschain`, `statusfeed`,
  `notificationlog`, `deaddeliveryreports`, `resourcelimitlog`.
- **Postgres version:** upstream CI runs Postgres **18.6**. The stack runs 16. A grep for PG17/18-only syntax found
  nothing, but the migrations are not confirmed to run on 16.
- **Development quirk:** the migration path is the *parent* of the working directory plus `MigrationScriptPath`
  (`WebApplicationExtensions.cs#L34-L36`). Set it by configuration.

**Pipeline.**

1. **Order intake.** `POST future/orders` (or `orders/email|sms`) validates the order and stores it as `Registered` with
   `creatorname` = the issuer of the platform access token ([`OrgExtractorMiddleware.cs#L43-L45`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Middleware/OrgExtractorMiddleware.cs#L43-L45)).
2. **Order processing.** [`ProcessItemsBackgroundService`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Core/Services/ProcessItemsBackgroundService.cs#L36) runs six loops in-process, all on `SELECT … FOR UPDATE SKIP LOCKED`.
   - Two order loops (past-due and retry) pick up orders whose `requestedSendTime` has passed. Each order goes through
     the condition check (`SendConditionClient`), the contact-point lookup and keyword substitution, and becomes one
     notification row per recipient, with status `New`.
   - Four send loops (e-mail, composed e-mail, SMS daytime, SMS anytime) pick up `New` notifications and publish
     `SendEmailCommand` / `SendComposedEmailCommand` / `SendSmsCommand`.
   - Loop settings are `NotificationConfig:*ProcessLoopConfig`: `TaskCount` 30, idle delay 30 s for the primary task.
   - The cron jobs in [`docs/architecture/notifications.md`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/docs/architecture/notifications.md) (`TriggerController`) are no longer needed for sending.
   - Daytime SMS waits for `CanSendSmsNow`: 09:00 to 17:00 Oslo time from `NotificationConfig:SmsSendWindow*Hour` ([L42](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Core/Services/NotificationScheduleService.cs#L42)).
3. **E-mail sending.** The email-service consumes `SendEmailCommand` and calls `IEmailServiceClient.SendEmail` (ACS),
   which returns an operation id.
   - It then self-enqueues `CheckEmailSendStatusCommand` and polls `GetOperationUpdate` until a terminal state, publishing
     `EmailSendResultCommand` to the API ([`SendingService.cs#L31`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email.Core/Sending/SendingService.cs#L31)).
   - ACS delivery reports reach the API's queue directly through Event Grid → ASB, decoded by `EventGridEnvelopeMapper`,
     which wraps the raw `ServiceBusReceivedMessage` in `EmailDeliveryReportCommand` ([L33](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Integrations/Wolverine/EventGridEnvelopeMapper.cs#L33)).
4. **SMS sending.** The sms-service consumes `SendSmsCommand` and calls `ISmsClient.SendAsync` (Link Mobility XML), which
   returns a gateway reference, then publishes `SmsSendResultCommand` (`Accepted` or a failure).
   - Link Mobility later POSTs an XML delivery report to `notifications/sms/api/v1/reports` (Basic auth). That becomes an
     `SmsDeliveryReportCommand` to the API.
5. **API handlers.** Five Wolverine handlers in the API (e-mail and SMS send result, e-mail and SMS delivery report,
   e-mail rate limit) update the notification rows. The handler policies route unknown or expired reports to `deaddeliveryreports`.
6. **Instant orders** (`future/orders/instant`) bypass the queues and call the email-service and sms-service over HTTP
   (`ApiInstantEmailServiceEndpoint`, `ApiShortMessageServiceEndpoint`). No app-lib caller.

**Messaging.**

- **ASB is unconditional.** `ConfigureNotificationsDefaults` calls `UseAzureServiceBus` and throws on an empty connection
  string ([`shared/…/WolverineOptionsExtensions.cs#L29-L76`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/shared/src/Altinn.Notifications.Shared/Extensions/WolverineOptionsExtensions.cs#L29-L76)).
- **Queue wiring:** every host wires its queues with `ListenToAzureServiceBusQueue` / `ToAzureServiceBusQueue`:
  - API: 5 listeners and 3 publishers ([`WolverineServiceCollectionExtensions.cs#L33-L64`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L33-L64))
  - email-service: 3 listeners and 4 publishers ([L30-L54](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L30-L54))
  - sms-service: 1 listener and 2 publishers ([L29](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/sms-service/src/Altinn.Notifications.Sms.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L29))
- **Retry policies:** the policies are `internal` and reference `ServiceBusException`, as a type only.
- **ASB emulator:** the emulator path still needs MS SQL. It is not used here.

**Outbound dependencies of the API.**

| Dependency | Endpoint | Auth | Localtest today |
| --- | --- | --- | --- |
| Profile | `users/contactpoint/lookup`, `users/contactpoint/lookupsi`, `units/contactpoint/lookup`, `organizations/notificationaddresses/lookup` ([`ProfileClient.cs#L46,L70,L93,L120`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Integrations/Profile/ProfileClient.cs#L46)) | none (anonymous in production Profile too) | **Missing.** The register component serves Profile `users/*` only |
| Register | `POST parties/nameslookup` (keywords such as `$recipientName$`) | platform access token `platform`/`notifications` ([`RegisterClient.cs#L57`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Integrations/Register/RegisterClient.cs#L57)) | Production endpoint in the vendored Register (`PartiesController#nameslookup`, DB source) |
| Authorization PDP | `decision` (org contact points registered by users, per resource) | platform access token | Production PDP |
| App / Correspondence condition endpoint | `GET {conditionEndpoint}` | Maskinporten via `AddMaskinportenHttpClient` ([L64](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications.Integrations/Extensions/ServiceCollectionExtensions.cs#L64)) | Needs a fake token source |
| Dialogporten | `enduser/dialoglookup` (end-user notification log only) | — | Off (`Enabled` defaults to false; Development sets true) |

**Inbound auth.**

- **User and org JWTs:** the JWT cookie handler against `GeneralSettings:OpenIdWellKnownEndpoint`, with
  `RequireHttpsMetadata=false` in Development.
- **Platform access tokens:** `POLICY_CREATE_SCOPE_OR_PLATFORM_ACCESS` accepts a platform access token from any issuer, or
  scope `altinn:serviceowner/notifications.create`. The token is checked by `AccessTokenHandler` with
  `PublicSigningKeyProvider`, the Key Vault–backed provider ([`Program.cs#L321-L327`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Program.cs#L321-L327)).

**Key Vault.** All three hosts load Key Vault only when `kvSetting:ClientId`, `TenantId`, `ClientSecret` and
`SecretUri` are all set ([API `Program.cs#L241-L259`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Program.cs#L241-L259); the email and sms hosts are the same). It is off through configuration, with no directive.

**Upstream mocks** (added 1 September and 16 October). Neither records content, so neither is a capture sink.
- [`MockEmailServiceClient`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email.Integrations/Clients/MockEmailServiceClient.cs), behind `CommunicationServicesSettings:MockEmailClient`:
  - It returns the notification id as the operation id and reports `Delivered`.
  - It publishes an Event Grid–shaped `MockEmailDeliveryReportCommand` to the API's delivery-report queue. The recipient
    is hard-coded `nullstilt@altinn.xyz`.
- [`MockSmsClient`](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/sms-service/src/Altinn.Notifications.Sms.Integrations/LinkMobility/MockSmsClient.cs), behind `SmsGatewaySettings:MockSmsClient`:
  - It POSTs a Link Mobility `DELIVRD` XML report to the sms-service's own `/notifications/sms/api/v1/reports` after
    2 s, with Basic auth.

Both mocks show the right shape for a delivery simulation that runs through the production report paths.

### The 16 October blockers, re-examined

| Blocker then | Now |
| --- | --- |
| ASB needs the emulator and MS SQL | Fake Azure: the Wolverine PostgreSQL transport on the shared Postgres (section 3). No emulator |
| Kafka? | None in the code |
| Key Vault-backed token validation | Same `IPublicSigningKeyProvider` replacement as Storage and Access Management (shared test certificate) |
| Missing register/profile endpoints | Register `nameslookup` is production and present. The 4 Profile contact-point endpoints are added to the register component's Profile fakes |
| No dev sink for e-mail/SMS | Fakes at `IEmailServiceClient` and `ISmsClient`, modelled on the upstream mocks plus capture |
| Cron triggers | Gone: the in-process background service sends |
| Maskinporten for condition checks | Fake token source (section 3) |

## 3. Vendoring plan

### Layout

```
localtest/components/notifications/
  vendor/            # git archive of c9e1d233: components/{api,email-service,sms-service,shared}/src,
                     #   root Directory.Build.props/.targets, Directory.Packages.props, global.json, stylecop.json,
                     #   .editorconfig; no tests, no tools/ (UPSTREAM.md records the pin)
  fakes/
    Shared/          # compiled into Altinn.Notifications.Shared: transport shim, capture store, simulation rules
    Api/             # compiled into Altinn.Notifications (host): config defaults, DI overrides, inspection API, DB bootstrap
    Email/           # compiled into Altinn.Notifications.Email (host): LocaltestEmailServiceClient
    Sms/             # compiled into Altinn.Notifications.Sms (host): LocaltestSmsClient
  localtest.build.props / .targets
  Dockerfile         # one image, three published hosts; compose picks the entrypoint
```

- **Image:** one image keeps the three hosts on one pin and one build.
- **Compose:** three services from that image: `notifications` (5090), `notifications-email` (5091) and `notifications-sms` (5092).
- **Ingress:** routes `/notifications/` to the API only. The email and sms services are reached on compose DNS.
- **Postgres:** a `notificationsdb` database with the two roles, created on start by the API fakes (the Access
  Management `LocaltestDatabase` pattern), so no volume reset is needed.

### Directive sites (about 9)

| # | File | Directive |
| --- | --- | --- |
| 1 | `api/…/Altinn.Notifications/Program.cs` after `SetConfigurationProviders` ([L43](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Program.cs#L43)) | `#if LOCALTEST AddLocaltestConfiguration(builder.Configuration)`. Must come before `ConfigureServices`, which reads `GeneralSettings` eagerly (L197) |
| 2 | Same file, before `builder.Build()` ([L94](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/api/src/Altinn.Notifications/Program.cs#L94)) | `#if LOCALTEST AddLocaltestNotifications(builder)`: replaces registrations, ensures the database and roles |
| 3, 4 | `email-service/…/Program.cs` (after `SetConfigurationProviders`; after `ConfigureServices` [L36](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email/Program.cs#L36)) | Configuration defaults; `Replace<IEmailServiceClient, LocaltestEmailServiceClient>` |
| 5, 6 | `sms-service/…/Program.cs` (same places, [L38](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/sms-service/src/Altinn.Notifications.Sms/Program.cs#L38)) | Configuration defaults; `Replace<ISmsClient, LocaltestSmsClient>` |
| 7 | `shared/…/WolverineOptionsExtensions.cs` [L51-L73](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/shared/src/Altinn.Notifications.Shared/Extensions/WolverineOptionsExtensions.cs#L51-L73) | `#if !LOCALTEST` the `UseAzureServiceBus` block, `#else` `opts.UsePostgresqlPersistenceAndTransport(cs, "wolverine")` (the connection string read from configuration in the fake) |
| 8–10 | The three `WolverineServiceCollectionExtensions.cs` files | Swap only the `using Wolverine.AzureServiceBus;` line, `#else using Altinn.Notifications.Localtest.Transport;` (see the shim below). The method bodies, queue names, listener counts and the production handler policies stay untouched |

Sites 8 to 10 are the ones most likely to conflict on upstream pulls: the repo changed its Wolverine wiring twice in the
week before the pin ([#1913](https://github.com/Altinn/altinn-notifications/pull/1913), [#1929](https://github.com/Altinn/altinn-notifications/pull/1929)). Each one is still a single line.

### Transport shim (fake Azure)

`fakes/Shared/Transport` defines extension methods with the ASB names, so the vendored call sites compile unchanged:

- `ListenToAzureServiceBusQueue(this WolverineOptions, string)` → `ListenToPostgresqlQueue(map(name))`. It returns a small
  wrapper with `ListenerCount(int)` and `InteropWith(IAzureServiceBusEnvelopeMapper)`.
- `ToAzureServiceBusQueue(this IPublishToExpression, string)` → `ToPostgresqlQueue(map(name))`.
- `map` turns `altinn.notifications.email.send` into `email_send`. Whether the PostgreSQL transport accepts dots in queue
  names is not confirmed, so mapping is the safe default.
- `InteropWith(EventGridEnvelopeMapper)` becomes `DefaultIncomingMessage<EmailDeliveryReportCommand>()` plus a listener
  serializer that wraps the body in `ServiceBusModelFactory.ServiceBusReceivedMessage(body)`. The production
  `EmailDeliveryReportHandler` then runs `EventGridEvent.Parse` on the same bytes it gets in production. The e-mail sink
  publishes on the upstream `MockEmailDeliveryReportCommand` route ([email extension L108-L112](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L108-L112)), whose JSON is already an Event Grid event.
- `WolverineFx.Postgresql` **6.49.0**, the same version as the vendored WolverineFx, is added to `Altinn.Notifications.Shared` from the targets file.
  - Checked: the version is on NuGet, and its XML docs list `UsePostgresqlPersistenceAndTransport(WolverineOptions, string, string, …)`, `ListenToPostgresqlQueue(WolverineOptions, string)`, `ToPostgresqlQueue(IPublishToExpression, string)`, and `ListenerConfiguration<,>.ListenerCount` / `DefaultIncomingMessage<T>`.
  - Not checked: how to give a version under central package management from the targets file, either a `PackageVersion`
    item or `VersionOverride`.
- **Why the PostgreSQL transport:** it is a supported Wolverine transport and needs no new container. The three
  processes share queue tables in `notificationsdb`. Scheduled retries and the error queue are durable there.
  - **Not confirmed:** that the production policies behave the same on it, in particular `SendInline()`,
    `ProcessInline()`, `EnableAutomaticFailureAcks=false`, `ScheduleRetry` and `MoveToErrorQueue`.
  - **Fallback** if the spike shows gaps: Wolverine's RabbitMQ transport, also 6.49.0 on NuGet, with the same shim and one
    more container.
- Wolverine polls its queue tables internally. That is transport plumbing inside one component, not polling between
  components.

### Fakes and what each replaces

| Fake | Replaces (production) | Notes |
| --- | --- | --- |
| `LocaltestEmailServiceClient` (`IEmailServiceClient`) | `EmailServiceClient` → ACS | Writes the captured e-mail (section 4) and applies the simulation rule. On success it returns the operation id, and `GetOperationUpdate` returns `Succeeded`, then publishes an ACS delivery report (`Delivered`, `Bounced`, `Suppressed`, `Quarantined`, `FilteredSpam`, `Failed`) with the **real** sender and recipient. A send-time failure returns an `EmailClientErrorResponse` (`Failed_InvalidEmailFormat`, `Failed_SupressedRecipient`, `Failed_TransientError`). Composed e-mail records attachment names and SAS URLs without downloading them |
| `LocaltestSmsClient` (`ISmsClient`) | `SmsClient` → Link Mobility | Writes the captured SMS and returns a gateway reference (or `Failed_InvalidRecipient` …). After a configurable delay it POSTs Link Mobility XML (`DELIVRD`, `UNDELIV`, `EXPIRED`, `REJECTD`, …) to the sms-service's own production `reports` endpoint with the configured Basic credentials, as upstream `MockSmsClient` does |
| Transport shim (above) | Azure Service Bus | |
| `LocaltestPublicSigningKeyProvider` | Key Vault signing keys | Shared test certificate, as in Storage and Access Management. The production `AccessTokenHandler` validates the apps' platform tokens |
| Signing-credentials resolver for `AccessTokenGenerator` | Key Vault or file certificate | Outgoing `platform`/`notifications` tokens to Register and the PDP, signed with the shared certificate |
| Maskinporten token source for `SendConditionClient` | Maskinporten (`SettingsJwkClientDefinition`) | Replace `IMaskinportenService` (or the client definition) with a call to the authentication component's `GET /Home/GetTestOrgToken?org=digdir&scopes=altinn:system/notifications.condition.check`. Apps ignore the token. Correspondence would need a Maskinporten-shaped token (open question) |
| `LocaltestInspectionController` + capture store | — (localtest only) | Section 4 |
| Database bootstrap | Operations-created database and roles | Creates `notificationsdb`, the two roles and the `wolverine` schema when missing |

**Outside this component, in the register component's `LocaltestProfileController`** (the Profile fake). Four
anonymous endpoints, same shapes as production Profile (`UserContactPointController`,
`UnitContactPointController`, `OrgNotificationAddressController`):

| Endpoint | Answer from the seed |
| --- | --- |
| `POST profile/api/v1/users/contactpoint/lookup` `{nationalIdentityNumbers, useStaleContactInfo}` | `contactPointsList[{userId, nationalIdentityNumber, isReserved:false, mobileNumber, email}]`. Use the person's **`mobileNumber`** (`+4799315829`), not the profile `phoneNumber` (= `telephoneNumber` `12345678`, the field today's `LocalProfileClient` used) |
| `POST …/users/contactpoint/lookupsi` `{externalIdentities}` | Self-identified e-mail users (`urn:altinn:person:idporten-email:<email>`) → their e-mail |
| `POST …/organizations/notificationaddresses/lookup` `{organizationNumbers}` | Org `eMailAddress` and `mobileNumber` from the seed (as today's `LocalRegisterClient`) |
| `POST …/units/contactpoint/lookup` `{organizationNumbers, resourceId}` | Empty list unless the seed gains per-user org contact points. Each one would trigger a production PDP check |

About 150 lines. An optional seed field `isReserved` (KRR reservation) would let testers see the reserved-recipient path.

### Configuration defaults (in the fakes, below environment variables)

| Key | Localtest |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` (needed for http OIDC metadata) |
| `GeneralSettings:OpenIdWellKnownEndpoint` / `BaseUri` | `{platform}authentication/api/v1/openid/`, `http://local.altinn.cloud:8000` |
| `PlatformSettings:ApiProfileEndpoint`, `ApiRegisterEndpoint`, `ApiAuthorizationEndpoint` | `{platform}profile/api/v1/`, `{platform}register/api/v1/`, `{platform}authorization/api/v1/` |
| `PlatformSettings:ApiShortMessageServiceEndpoint`, `ApiInstantEmailServiceEndpoint` | `http://notifications-sms:5092/notifications/sms/api/v1/`, `http://notifications-email:5091/notifications/email/api/v1/` |
| `PostgreSQLSettings:*ConnectionString`, `MigrationScriptPath` | `Host=postgres…;Database=notificationsdb`; `app/Migration` (Development path quirk) |
| `NotificationConfig:*ProcessLoopConfig` | `TaskCount=1`, `PrimaryTaskIdleDelaySeconds=1`. Production is 30 tasks and 30 s |
| `NotificationConfig:SmsSendWindowStartHour/EndHour` | `0` / `24` so daytime SMS goes out at once (`TimeSpan`, valid). Product decision, see open questions |
| `DialogportenSettings:Enabled`, `kvSetting:*`, `ApplicationInsights:ConnectionString` | `false`, empty, empty |
| `WolverineSettings:*ListenerCount` | 1 (queue names unchanged) |
| `Localtest:Notifications:ConnectionString` | email and sms hosts (needed for the transport and the capture store) |
| `SmsDeliveryReportSettings:UserSettings`, `SmsGatewaySettings` | Local Basic credentials shared by the SMS fake and the report endpoint; a dummy gateway |

### Effort and risk

| Step | Days |
| --- | --- |
| Spike: the API alone on the PostgreSQL transport with the production policies, one e-mail end to end through the email-service | 1 |
| Vendoring, props/targets, Dockerfile, compose, database bootstrap, auth fakes, configuration | 1.5–2 |
| E-mail and SMS sinks with delivery simulation | 1–1.5 |
| Inspection API + capture store | 1.5 |
| Profile contact-point endpoints in the register component | 0.5–1 |
| Smoke test (app instantiation → `future/orders` → condition callback → captured e-mail and SMS → `Delivered` status via `future/shipment/{id}`) | 1 |

Risks, highest first:

1. **Transport behaviour:** the production error policies on the PostgreSQL transport, and the Event Grid interop
   through `ServiceBusModelFactory`. Neither is confirmed. The spike decides between this transport, RabbitMQ and Plan B.
2. **Upstream churn:** the repo is very active (10 commits from 12 to 16 October, two of them reshaping Wolverine).
   Directive sites 7 to 10 sit in that code.
3. **Postgres 16 vs 18:** upstream tests on 18.6. Running the migrations on the stack's 16 is not confirmed; moving the
   stack to 17 or 18 is the fallback.
4. **Schema coupling:** the inspection join reads `notifications.orders`, `emailnotifications` and `smsnotifications`.
   Column drift breaks it, not production code. The smoke test catches it.
5. **Footprint and startup:** three .NET hosts with Wolverine runtime code generation. Start time and memory are not measured.
6. **Waiting times:** app-lib schedules `requestedSendTime` at +5 minutes, so testers wait at least that long unless the
   GUI can release an order early (open question).

### Plan B (if the spike fails)

Vendor the API host only (plus `shared`).

- **Capture:** replace `IEmailCommandPublisher`, `ISendSmsPublisher` and `IComposedEmailCommandPublisher` with capture
  sinks. The commands already carry the final content.
- **Transport:** run Wolverine on in-process queues. Directive 7 (no ASB) and the API's using-swap.
- **Feedback:** the sinks send `EmailSendResultCommand`, `SmsSendResultCommand` and delivery reports on in-process queues
  to the production handlers.
- **Cost:** one container and about 4 directive sites. It skips the email-service and sms-service: ACS and Link Mobility
  mapping, status polling and XML report parsing. That is a fake of an internal party, which the principles do not allow,
  so this is a fallback only.

Vendoring is the right call. A contract-level stub, which the earlier report recommended, would have to reimplement
`future/orders` validation, the contact-point resolution, the keyword substitution, the condition callback,
reminders, order chains, status and the shipment feed. Those are the parts that drifted fastest (`ContactPointService`
went from 212 to 791 lines).

## 4. Inspection seam and API for the GUI

### Where content is complete

| Seam | Has | Lacks |
| --- | --- | --- |
| `IEmailServiceClient.SendEmail(Email)` ([L17](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/email-service/src/Altinn.Notifications.Email.Core/Dependencies/IEmailServiceClient.cs#L17)); `SendComposedEmail` adds attachments | `NotificationId`, `Subject`, `Body` (keywords already substituted by the API), `FromAddress`, `ToAddress`, `ContentType` (Plain/Html) | Order id, creator, resource, sender's reference |
| `ISmsClient.SendAsync(Sms[, ttl])` ([L22](https://github.com/Altinn/altinn-notifications/blob/c9e1d233ec4c45f29928bbf00dc2df0dac1bd81a/components/sms-service/src/Altinn.Notifications.Sms.Core/Dependencies/ISmsClient.cs#L22)) | `NotificationId`, `Sender` (after the API's sender substitution), `Recipient`, `Message`, TTL | Same |
| API tables, joined on the notification id (`emailnotifications`/`smsnotifications.alternateid` → `orders`) | Order id (`orders.alternateid`), `creatorname` (org from the app's token), `sendersreference` (`app-{partyId}/{guid}`, `corr-…`), `requestedsendtime`, `type`, `notificationorder` JSONB (`resourceId`, condition endpoint; exact JSON keys not confirmed), recipient NIN/org number, `result`, `resulttime`, raw `deliveryreport`, order chain (reminders) | — |

So the sinks record what left the platform; the API host's localtest controller joins the order context and status. The
app name comes from the resource id (`app_{org}_{app}`), the instance from the sender's reference. Both are app-lib
conventions, not Notifications fields.

**Capture store.**

- **Table:** `localtest.captured_message` in `notificationsdb`, written by the sinks. Columns: notification id, channel,
  captured at, from/sender, to/recipient, subject, body, content type, provider reference (operation id or gateway
  reference), attachments JSON, simulated outcome.
- **Change feed:** `pg_notify('localtest_notifications', id)` after each insert, so the API host can push changes to the
  GUI without polling.

### Proposed contract (`notifications/api/localtest/v1/`, anonymous, localtest only)

| Endpoint | What |
| --- | --- |
| `GET messages?channel=email\|sms&recipient=&org=&app=&resourceId=&sendersReference=&orderId=&status=&since=&limit=50&cursor=` | Newest first: `notificationId`, `channel`, `capturedAt`, `from`, `to`, `subject` (e-mail), `bodyPreview`, `order{orderId, creator, sendersReference, resourceId, app, requestedSendTime, isReminder}`, `recipient{nationalIdentityNumber\|organizationNumber}`, `status{result, resultTime}` |
| `GET messages/{notificationId}` | Full body (HTML as sent, plus a plain-text rendering), content type, attachments (`filename`, `mimeType`, `sasUrl`), provider reference, raw delivery report, status timeline (`New → Sending → Succeeded/Accepted → Delivered/Failed_*`) from the production row and the capture |
| `GET messages/stream` | Server-sent events, `captured` and `outcome` (`{notificationId, channel, result}`), fed by `LISTEN` |
| `GET orders?status=Registered\|Processing&org=&sendersReference=` | Orders not yet sent, from `notifications.orders`, so the GUI can show "scheduled for 10:42" during the +5-minute wait |
| `POST orders/{orderId}/send-now` (optional) | Sets `requestedsendtime = now()` on a `Registered` order; the production loop picks it up within a second. Writes a production table, so it needs product owner approval |
| `DELETE messages` | Clears the capture store. `?includeOrders=true` also truncates the `notifications` schema |
| `GET/PUT simulation` | `{ "rules":[{"channel":"email","recipient":"*@bounce.test","stage":"delivery","outcome":"Bounced"},{"channel":"sms","recipient":"+4790000000","stage":"send","outcome":"Failed_InvalidRecipient"}], "default":{"email":"Delivered","sms":"DELIVRD"}, "deliveryDelayMs":1000 }`. `stage: send` = rejected by the provider at submit; `stage: delivery` = a later delivery report. Stored in `notificationsdb` so all three hosts apply it |

Statuses always come from the production rows that the production handlers updated, never from the sink, so the GUI
shows what an app or Correspondence would read from `future/shipment/{id}` or `orders/{id}/status`.

## 5. Open questions for the product owner

1. **Transport fallback:** is a fake *transport* (Wolverine on PostgreSQL) acceptable as "fake Azure"? And if it fails,
   do you prefer RabbitMQ (one more container) or Plan B (fakes Notifications' own e-mail and SMS services)?
2. **SMS daytime window:** keep production's 09:00 to 17:00 window for daytime SMS (realistic, but SMS sent after 17:00
   wait until morning), or default to send at any time?
3. **The +5-minute wait:** may the GUI release a scheduled order early (`send-now`), or should testers wait the
   production delay? Should app-lib's +5 minutes become configurable in localtest instead?
4. **Who stores the captures:** should Notifications own the capture store and the query API (as proposed), or push
   each captured message to the GUI component, which stores it?
5. **Seed:** should the seed gain `isReserved` (KRR reservation) and per-user org contact points, so testers can
   exercise those paths?
6. **Correspondence:** when Correspondence joins the stack, does it need a Maskinporten-shaped token for its condition
   endpoint, and which token does it send to Notifications? Those decide the shape of the Maskinporten fakes on both sides.
7. **Upstream:** ask the Notifications team for a pluggable transport or a local mode, and for recording mock clients?
   Either would shrink directive sites 7 to 10 and the sinks.
