# Notifications for the new localtest stack

Research date: 2026-10-20 (task 6 of [`../../TODO.md`](../../TODO.md)). Read-only plus two scratch builds, listed under
"What was run" below; nothing was built as an image, no container or studioctl command was touched. Claims inferred
from code and not observed are marked "not verified".

## Summary

- **The question in TODO.md rests on a stale premise: there is no Kafka.** The pinned `altinn-notifications` has no
  occurrence of "kafka" in any file (`grep -rIil kafka .` is empty). Queues are **Azure Service Bus through Wolverine**,
  and the only place the API host touches it is one registration call
  ([`Program.cs:226`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs#L226)) and three publisher classes that all go through one small
  interface, `IMessageBusPublisher`. The old `altinn-notifications-email` and `-sms` repositories are archived (both
  2026-03-02, `gh api repos/Altinn/<repo>`) and now live in the monorepo as `components/email-service` and
  `components/sms-service`; they are separate hosts that sit behind the queue and are **not needed by the orders API
  locally**.
- **Recommendation: Option A, vendor the orders API host (`components/api` + `components/shared`) with the Azure Service
  Bus publishers replaced by capturing fakes.** Verified in a scratch build and a scratch start of the host: with one
  `#if !LOCALTEST` site the host builds under the `localtest.build.props` mechanism, starts with .NET's development DI
  validation on, answers `/health` 200 and a token-less order 401. Three strongest reasons:
  1. The consumer contract is large and stateful (order chains, shipments, status, notification lists with
     `links.self` that Correspondence follows, idempotency, 422 problem details for recipients without contact points,
     cancel). Production code serves all of it; a fake would re-implement it against four production clients (app-lib,
     Correspondence, Authentication, Access Management).
  2. The seam is tiny and clean: one registration site, three publisher interfaces and two instant-send clients, no
     Kafka or Service Bus type in `Core` or `Persistence`. Directive count 2 (one additive), fakes about 1 400 lines,
     the same scale as Events.
  3. It gives the stack exactly what the inspection GUI wants: every would-be e-mail and SMS is recorded and listable
     under `notifications/api/localtest/` while the production status machinery (Sending, Delivered, Failed_*) runs.
- **Effort 6 to 7 days** (Notifications component about 4.5 days, the contact-point endpoints in the Register component
  about 1 day, studioctl and image wiring about 1 day).
- **Biggest risk:** the database side was not run. 104 migration version folders (about 131 000 lines of SQL in 217 files) have never been
  applied to the shared Postgres 18.3 by this stack, and the capture responder has one timing constraint that must be
  designed in from the start (section 6.4): the claim loop publishes inside an open transaction, so a result applied
  from inside `PublishAsync` would wait for its own lock.
- **One missing platform piece is a prerequisite for person and organisation recipients:** the Profile contact-point
  lookups (`users/contactpoint/lookup`, `lookupsi`, `units/contactpoint/lookup`,
  `organizations/notificationaddresses/lookup`). The stack's Profile (served by the Register component) has none of them
  today. Direct e-mail and SMS recipients work without them.

## 1. Pins

| Repo | Commit | What was read |
| --- | --- | --- |
| [`Altinn/altinn-notifications`](https://github.com/Altinn/altinn-notifications/tree/a4aa959a5c50094048aceb98af8dccd9db17fca8) | `a4aa959` (= `origin/main`, 2026-10-20; the clone is shallow, so history before 2026-10-16 was not read) | `components/api`, `components/shared`, the Email and SMS hosts' queue handlers and mocks, `docs/architecture`, `Dockerfile`, build files, CI |
| [`Altinn/app-lib-dotnet`](https://github.com/Altinn/app-lib-dotnet/tree/f70859365a3ce4e8a1eda393321ddf67473a755c) | `f708593` (= `origin/main`, 2026-10-20; the local checkout is at `4740bb2`, files read with `git show` at the pin) | the five notification clients, `NotificationService`, the callback controller, DI |
| [`Altinn/altinn-correspondence`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) | `6dcf561` (= `origin/main`, 2026-10-16) | `AltinnNotificationService`, `AltinnDevNotificationService`, DI |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810` (= `origin/main`, 2026-10-19; the pin of the vendored Authentication) | Authentication's `AltinnNotificationClient`, Access Management's notification outbox and client |
| [`Altinn/altinn-profile`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24) | `d1f9871` (= `origin/main`, 2026-10-19) | the four contact-point controllers Notifications calls |
| [`Altinn/altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) | `447e53e` (2026-10-16) | the old localtest's Notifications copy (reference only) |
| `Altinn/dialogporten`, `Altinn/altinn-dialogporten-adapter` | `0946da8`, `0a32985` | grepped for calls to Notifications: none (Dialogporten has the condition-check scope for Notifications' *incoming* call, see 2.5) |

This repository: the worktree is on `origin/main` at `5449dfc`.

## 2. The consumer contract

All paths are under `notifications/api/v1/` on the platform origin (`http://local.altinn.cloud:8000/` locally).
studioctl already gives every app `PlatformSettings__ApiNotificationEndpoint = <origin>/notifications/api/v1/`
(`localtest/studioctl/src/cli/internal/cmd/app/env.go:112`), and the vendored Authentication already points its
`ApiNotificationsEndpoint` at the same prefix
(`localtest/components/authentication/fakes/LocaltestAuthenticationExtensions.cs:92`), so no consumer needs a change.

### 2.1 Authentication of the API

The orders API accepts either of two credentials on every `[Authorize(Policy = CreateScopeOrPlatformAccess)]` action:
a **platform access token** in the `PlatformAccessToken` header, or a bearer token with the scope
`altinn:serviceowner/notifications.create`
([policy](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs#L300-L304),
[`CreateScopeOrAccessTokenRequirement.cs:12-16`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Authorization/CreateScopeOrAccessTokenRequirement.cs#L12-L16),
[`AuthorizationConstants.cs:16`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Configuration/AuthorizationConstants.cs#L16)). `ApprovedIssuers` is empty, which I read as "any
issuer whose signature validates" (the handler is in `Altinn.Common.AccessToken`, not read). The order's creator (`creator` in every response, used to
scope reads) is the **issuer of the platform token**, which is the organisation for an app (`ttd`) and `platform` for
Authentication, or the `urn:altinn:org` claim of the bearer token
([`OrgExtractorMiddleware.cs:43-60`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Middleware/OrgExtractorMiddleware.cs#L43-L60)). Validation of the platform token
needs the public signing keys, which production reads from Key Vault (`IPublicSigningKeyProvider`,
[`Program.cs:324`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs#L324)): the shared test certificate fake as in Events. Bearer tokens are
validated against Authentication's OpenID metadata
([`appsettings.json:35-39`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/appsettings.json#L35-L39)).

### 2.2 Endpoints each local consumer calls

| Consumer | Call | Credential | What the consumer does with the answer |
| --- | --- | --- | --- |
| **app-lib** `NotificationOrderClient`, v2 "future" API; ordered from `NotificationService.NotifyInstanceOwnerOnInstantiation` when the instantiation request carries a notification ([`InstancesController.cs:440`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Api/Controllers/InstancesController.cs#L440), [`NotificationService.cs:63-76`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L63-L76)) | `POST future/orders` ([`NotificationOrderClient.cs:50`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/Future/NotificationOrderClient.cs#L50)). Body `NotificationOrderRequest`: `idempotencyId`, `sendersReference`, `requestedSendTime`, `conditionEndpoint` (the app's own callback, `…/{org}/{app}/api/v1/notification-webhook-listener/{instanceId}?code=…`, [`NotificationService.cs:278-287`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/NotificationService.cs#L278-L287)), `recipient` with exactly one of `recipientOrganization` (line 181), `recipientPerson` (213), `recipientExternalIdentity` (245), plus `reminders[]` | platform access token `GenerateAccessToken(org, app)` ([`NotificationOrderClient.cs:60`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/Future/NotificationOrderClient.cs#L60)) | `201` with `{ notificationOrderId, notification: { shipmentId, sendersReference }, reminders: [...] }`; logs the ids; any non-2xx becomes `NotificationOrderException`, which the instantiation catches and logs |
| app-lib `EmailNotificationClient`, v1 | `POST orders/email` ([`EmailNotificationClient.cs:50`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/Email/EmailNotificationClient.cs#L50)); body `subject`, `body`, `sendersReference`, `recipients[]`, `contentType`, `requestedSendTime` | platform access token | `{ orderId }` returned to the app's own code (the `IEmailNotificationClient` interface is public API for app developers) |
| app-lib `SmsNotificationClient`, v1 | `POST orders/sms` ([`SmsNotificationClient.cs:50`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/Sms/SmsNotificationClient.cs#L50)); body `senderNumber`, `body`, `sendersReference`, `recipients[]`, `requestedSendTime` | platform access token | `{ orderId }` (public API, as above) |
| app-lib `NotificationCancelClient` | `PUT orders/{id}/cancel` ([`NotificationCancelClient.cs:47`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Core/Features/Notifications/Order/NotificationCancelClient.cs#L47)) | platform access token | any non-2xx throws `NotificationCancelException` |
| **Notifications calls the app** (reverse direction) | `GET {conditionEndpoint}`, the app's `NotificationCallbackController` ([`NotificationCallbackController.cs:23`](https://github.com/Altinn/app-lib-dotnet/blob/f70859365a3ce4e8a1eda393321ddf67473a755c/src/Altinn.App.Api/Controllers/NotificationCallbackController.cs#L23), anonymous, validates the `code` query value, answers `{ sendNotification: bool }`; a missing instance cancels the order) | production sends a Maskinporten token (`altinn:system/notifications.condition.check`), the app ignores it | decides at send time whether the order still goes out. The URL host is the app's external base URL, so the Notifications container must reach `local.altinn.cloud:8000` (host gateway, as the Events container does) |
| **Correspondence** `AltinnNotificationService` (task 7), all v1 paths | `POST future/orders` with `NotificationOrderRequestV2` ([`AltinnNotificationService.cs:28`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Notifications/AltinnNotificationService.cs#L28)); `PUT orders/{id}/cancel` (:53); `GET orders/{id}/status` (:64) followed by `GET <links.self>` of the e-mail and SMS summaries (:97, :110); `GET future/shipment/{shipmentId}` (:126); `POST future/orders/composed-email` (:145) | Maskinporten bearer, scopes `altinn:serviceowner/notifications.create`, `.read`, `.composedemail.create` ([`appsettings.json:15`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/appsettings.json#L15)), via `AddMaskinportenHttpMessageHandler` ([`DependencyInjection.cs:67`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L67)) | `CreateNotificationV2` treats `422` as "no notification" and returns null, every other failure throws; it stores `notificationOrderId`/`shipmentId`, schedules a delivery check that reads the shipment status. **`links.self` is followed as an absolute URL**, so the API's `GeneralSettings:BaseUri` must be the origin Correspondence can reach (no trailing slash: [`ResourceLinkExtensions.cs:20-26`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Extensions/ResourceLinkExtensions.cs#L20-L26)). Without a Maskinporten client id Correspondence swaps in `AltinnDevNotificationService`, which fakes every answer ([`DependencyInjection.cs:45-48`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L45-L48)) |
| **Authentication** (vendored, in the stack today) `AltinnNotificationClient` | `POST future/orders` with a direct `recipientEmail` (HTML body, `SendingTimePolicy.Anytime`), used by `SelfIdentifiedLinkService` ([`AltinnNotificationClient.cs:25`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Integration/Clients/AltinnNotificationClient.cs#L25), [`SelfIdentifiedLinkService.cs:90`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/SelfIdentifiedLinkService.cs#L90)) | platform access token `platform`/`authentication` (:86) | `true` on 2xx; failures are logged, the link flow reports "not sent". Today the call gets a 404 from the proxy |
| **Access Management** (vendored; outbox handlers are switched off locally, `Outbox.Handler=false`) | `POST future/orders` through `AltinnNotificationClient.Send` ([`SendNotificationEndpoints.cs:25-26`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/Notification/SendNotificationEndpoints.cs#L25-L26)); the handlers treat problem code `NOT-00001` ("no contact information") as completed ([`AccessAddedNotificationHandler.cs:66-71`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/Outbox/AccessAddedNotificationHandler.cs#L66-L71)) | platform access token | dormant until the outbox is enabled (not planned) |
| Dialogporten, the adapter, Resource Registry, Events, Register | none (grep over each clone at the pins in section 1) | | |
| Profile (not in the stack) | calls Notifications (`NotificationsClient`), irrelevant here | | |

The old localtest in altinn-studio emulates only `POST orders/email` and `POST orders/sms`, copied core code writing
orders to a JSON file; it answers nothing else (no `future/orders`, no status, no cancel)
([`README.md`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Notifications/README.md), reference only).

### 2.3 What the stack therefore needs from the service

1. `future/orders` (all five recipient kinds: e-mail, SMS, person, organisation, external identity; reminders; condition
   endpoint; idempotency), `orders/email`, `orders/sms`, `orders/{id}/cancel`.
2. The read side Correspondence and app developers use: `orders/{id}`, `orders?sendersReference=`, `orders/{id}/status`,
   `orders/{id}/notifications/email|sms`, `future/shipment/{id}`, `future/shipment/feed`, `future/log`.
3. The 422 and `NOT-00001` problem details for unreachable recipients, because two consumers branch on them.
4. Every notification must reach a terminal status without leaving the machine, and be visible to a tester.
5. Recipient resolution for persons, organisations and self-identified users (section 4.3), which is where a
   stand-alone fake would have the most to invent.

### 2.4 Which orders the seeded test apps place

Of the apps in `altinn-studio/src/test/apps` only `signering-brukerstyrt` mentions notifications (its signee
providers, which go through Correspondence, task 7); none calls the five clients directly (grep). The instantiation
path runs only when a client sends an `InstantiationNotification` with the instance, which the app frontend does not.
So the first consumers in the stack are Authentication's self-identified link e-mail, Correspondence (task 7) and tests
that POST orders as an app. That is also why the inspection side matters more than volume.

### 2.5 The reverse call from Notifications to Dialogporten and to apps

`DialogportenClient.CheckUserAccessToDialog` is only used by the end-user log (`future/enduser/log`) and returns false
when `DialogportenSettings:Enabled` is false ([`DialogportenClient.cs:49-53`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Dialogporten/DialogportenClient.cs#L49-L53));
locally it stays disabled until task 9 and a user-token consumer exist. When an order's `conditionEndpoint` is a
Dialogporten URL, Dialogporten expects a Maskinporten token with `altinn:system/notifications.condition.check`
([`Constants.cs:90`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/Constants.cs#L90)); the shared `IMaskinportenService` fake produces one.

## 3. How production works

```
caller --POST future/orders--> API host (Altinn.Notifications, port 5090)
   |   validate, resolve contact points (Profile, Register, PDP), write the order chain and notifications  [Postgres]
   v
 background loops in the same host (ProcessItemsBackgroundService, plus cron-curl triggers in AKS):
   past-due orders  --> create notifications per recipient (e-mail / SMS rows, status New)
   e-mail loop      --> claim one New notification (FOR UPDATE ... SKIP LOCKED, open transaction) --> publish SendEmailCommand --> commit
   SMS loop (daytime window 09-17 Oslo, or anytime) --> same with SendSmsCommand
   |                                                   |
   v (Azure Service Bus queues, Wolverine)             v
 Email host: ACS send, status check queue          SMS host: Link Mobility
   |  EmailSendResultCommand / delivery report (Event Grid -> queue)   |  SmsSendResultCommand / SmsDeliveryReportCommand
   v                                                                   v
 API host listeners update notification status (Sending, Succeeded, Delivered, Failed_*)  [Postgres]
```

- **Orders API host.** ASP.NET Core, controllers under `notifications/api/v1/…`
  ([`OrdersController.cs:22`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Controllers/OrdersController.cs#L22), `FutureOrdersController` :24,
  `EmailNotificationOrdersController` :20, `SmsNotificationOrdersController` :20, `ShipmentController` :21,
  `StatusFeedController` :21, `NotificationLogController` :24, `TriggerController` :15, plus dashboard, metrics, instant
  orders and the composed e-mail controller). Everything is created by one `WebApplication`.
- **Processing is inside the host, not in a separate worker.** `ProcessItemsBackgroundService` runs six poll loops
  (past-due orders, retry orders, e-mail, composed e-mail, SMS daytime, SMS anytime), each defaulting to **30 tasks and
  a 30 s idle delay** ([`ProcessItemsBackgroundService.cs:36-110`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Services/ProcessItemsBackgroundService.cs#L36-L110),
  [`NotificationConfig.cs:66-125`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Configuration/NotificationConfig.cs#L66-L125)). The `trigger/*` endpoints
  ([`TriggerController.cs:40-147`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Controllers/TriggerController.cs#L40-L147)) run one iteration on demand and
  are anonymous; AKS cron jobs also call them
  ([docs](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/docs/architecture/notifications.md#L150-L167)).
- **The queue.** Wolverine with the Azure Service Bus transport. The API publishes `SendEmailCommand`,
  `SendComposedEmailCommand`, `SendSmsCommand` ([`WolverineServiceCollectionExtensions.cs:201-239`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L201-L239))
  and listens on five queues (e-mail and SMS send results, e-mail and SMS delivery reports, e-mail rate limit;
  [`WolverineServiceCollectionExtensions.cs:85-182`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Extensions/WolverineServiceCollectionExtensions.cs#L85-L182)). A
  `PastDueOrdersQueueName` setting exists but nothing reads it in the pinned code (grep). The shared helper turns the
  local-routing convention off, uses `UseAzureServiceBus`, and in development auto-purges queues
  ([`WolverineOptionsExtensions.cs:38-72`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/shared/src/Altinn.Notifications.Shared/Extensions/WolverineOptionsExtensions.cs#L38-L72)). The three
  publishers are thin wrappers around `IMessageBusPublisher.PublishCommandAsync`
  ([`EmailCommandPublisher.cs:12-30`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Wolverine/Publishers/EmailCommandPublisher.cs#L12-L30),
  [`WolverinePublisher.cs:55-62`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/shared/src/Altinn.Notifications.Shared/Publishers/WolverinePublisher.cs#L55-L62)).
- **The claim runs in a transaction that is committed after the publish** (the claim functions lock the row with
  `FOR UPDATE OF … SKIP LOCKED`, [`claimanytimesms.sql:21`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Persistence/Migration/FunctionsAndProcedures/claimanytimesms.sql#L21);
  [`EmailNotificationService.cs:99-135`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Services/EmailNotificationService.cs#L99-L135),
  [`SmsNotificationService.cs:109-127`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Services/SmsNotificationService.cs#L109-L127)). The result handlers write the
  same row later, from the queue.
- **Email and SMS hosts.** Separate ASP.NET hosts (about 3 070 and 1 900 lines of C#) that consume the send queues,
  call Azure Communication Services and Link Mobility, and publish results. Both ship an upstream mock behind a setting
  (`CommunicationServicesSettings:MockEmailClient`
  [`ServiceCollectionExtensions.cs:55`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/email-service/src/Altinn.Notifications.Email.Integrations/Configuration/ServiceCollectionExtensions.cs#L55),
  `SmsGatewaySettings:MockSmsClient` [`ServiceCollectionExtensions.cs:39`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/sms-service/src/Altinn.Notifications.Sms.Integrations/Configuration/ServiceCollectionExtensions.cs#L39)),
  which exists for the Service Bus emulator setup in `getting-started.md`. Instant orders skip the queue: the API
  POSTs to the hosts' HTTP endpoints (`notifications/email/api/v1/instantemail`, `notifications/sms/api/v1/instantmessage/send`;
  [`InstantEmailServiceClient.cs:32`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/InstantEmailService/InstantEmailServiceClient.cs#L32),
  [`ShortMessageServiceClient.cs:32`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/ShortMessageService/ShortMessageServiceClient.cs#L32)).
- **Recipient lookup.** `ContactPointService` calls Profile at registration time and again at processing time:
  `users/contactpoint/lookup` (persons, [`ProfileClient.cs:46`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Profile/ProfileClient.cs#L46)),
  `users/contactpoint/lookupsi` (external identities, :93), `organizations/notificationaddresses/lookup` (official
  organisation addresses, :120) and `units/contactpoint/lookup` (user-registered contact points for a party and a
  resource, :70), then filters the users by a PDP decision for the order's resource
  ([`AuthorizationService.cs:83`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Authorization/AuthorizationService.cs#L83)). Register is called for
  names (`parties/nameslookup`, platform token `platform`/`notifications`,
  [`RegisterClient.cs:57`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Register/RegisterClient.cs#L57)) to fill `$recipientName$`-style
  keywords. A request where no recipient can be reached fails with `422` (`NOT-00001` for the callers above).
  Profile's routes match what Notifications calls, all four anonymous and "internal"
  ([`UserContactPointController.cs:54`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Controllers/UserContactPointController.cs#L54), [`UserContactPointController.cs:75`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Controllers/UserContactPointController.cs#L75),
  [`UnitContactPointController.cs:54`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Controllers/UnitContactPointController.cs#L54), [`OrgNotificationAddressController.cs:36`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Controllers/OrgNotificationAddressController.cs#L36)).
- **State served back.** Postgres schema `notifications` (`orders`, `orderschain`, `emailnotifications`,
  `smsnotifications`, `emailtexts`, `smstexts`, `notificationlog`, `statusfeed`, `resourcelimitlog`,
  `deaddeliveryreports`, lookup `sendingtimepolicy`): order with `processingStatus`
  (`Registered`, `Processing`, `Completed`, `Cancelled`, …) and a `notificationsStatusSummary` whose `links.self` point at
  the per-order notification lists; each notification's `sendStatus` (`Sending`, `Succeeded`, `Delivered`, `Failed_*`)
  with a timestamp; the shipment manifest (one entry per recipient with channel and status); the status feed (a polled
  stream of status changes per creator); the notification log (what the end user sees).
- **Config at runtime.** Key Vault only when four `kvSetting` values are all set; App Insights only with a connection
  string; OTLP only with an endpoint; Maskinporten only for `SendConditionClient`
  ([`ServiceCollectionExtensions.cs:64`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Extensions/ServiceCollectionExtensions.cs#L64)).

## 4. Build chain

| Question | Finding |
| --- | --- |
| Projects | Host `Altinn.Notifications` (`Microsoft.NET.Sdk.Web`), `Core`, `Integrations`, `Persistence`, `DbTools` (a build-time tool referenced from `Persistence`), and `components/shared/src/Altinn.Notifications.Shared`. Four of them are needed in the image, plus the tool |
| Size (C#, lines) | host 9 311, `Core` 12 750, `Integrations` 3 048, `Persistence` 3 543, `DbTools` 70, `Shared` 666: about **29 400**; migrations: 104 version folders, about 131 000 lines of SQL in 217 files (5.5 MB, the 49 function and procedure scripts included) |
| SDK | `global.json` pins 10.0.402 with `rollForward: latestFeature` ([`global.json:3-4`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/global.json#L3-L4)); the Dockerfile's `sdk:10.0.402-alpine3.24` is **the same image digest** Events already uses. On this machine only 10.0.401 is installed, so a scratch build needs its own `global.json` |
| Package management | Central: `Directory.Packages.props` at the repo root; **no lock files** (`find -name packages.lock.json` is empty); `TreatWarningsAsErrors=true` and `GenerateDocumentationFile=true` in `Directory.Build.props`; `Directory.Build.targets` only touches test projects |
| Analyzers | StyleCop, added only for `Configuration == Debug` ([`Directory.Build.targets:24-32`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/Directory.Build.targets#L24-L32)); the Release image build does not run it |
| `localtest.build.props`/`.targets` | **Applies unchanged** (Events' two files with the project name changed). Verified in a scratch build: root `Directory.Build.props` is found by `GetPathOfFileAbove`, `LOCALTEST` is defined, `TreatWarningsAsErrors` is overridden, `fakes/**/*.cs` compile into the host. Central package versions reach the fakes because they are compiled into the host |
| Native pieces | None in the closure that matters here: no librdkafka, no `RuntimeCompilation` use at runtime beyond Wolverine's own (it stays on the closure, harmless) |
| Dockerfile | [`components/api/Dockerfile`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/Dockerfile): build context is the **repository root**; restore in layers from the five `.csproj`; `dotnet publish -c Release`; runtime `aspnet:10.0.13-alpine3.24` (same digest as Events), `apk add tzdata` (needed: the SMS send window uses `Europe/Oslo`), copies `Migration/` and `Views/`, user `dotnet` 1000/3000, `EXPOSE 5090`, no `HEALTHCHECK`. Restore skips the missing `DbTools` project at the restore layer (checked in a scratch restore) and the `Persistence` PostBuild target does nothing in a Release build, so the **committed** `Migration/v1.05/04-functions-and-procedures.sql` is what runs; whether it equals what `DbTools` would generate is not verified |
| Database tools | No separate migrator: Yuniql runs in the host at startup (`app.SetUpPostgreSql`, [`Program.cs:96`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs#L96), [`WebApplicationExtensions.cs:13-40`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Persistence/Extensions/WebApplicationExtensions.cs#L13-L40)), `IsAutoCreateDatabase = false`, workspace `{parent of cwd}/{MigrationScriptPath}` in Development |
| Postgres version | Upstream CI runs `postgres:18.6` ([`build-and-analyze.yml:195`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/.github/workflows/build-and-analyze.yml#L195)); the shared image is 18.3. No `CREATE EXTENSION`, no `uuidv7` in the migrations (grep empty) |

### What was run (all in the scratchpad, the reference clone was not modified)

1. Copied the root build files, `components/api/src` and `components/shared/src` into a scratch `vendor/`, Events'
   `localtest.build.props` and `.targets` with the project name changed, one fake file, and the `#if !LOCALTEST`
   site of section 6.2 in `Program.cs`. `dotnet build -c Release` with SDK 10.0.401: **succeeded, 0 warnings, 0 errors**
   in 8 s warm; the host assembly contains the fake types. Deleted `bin/` and `obj/` afterwards.
2. Started that host on a free port in `Development` with `PostgreSQLSettings__EnableDBConnection=false` and the six
   process loops set to zero tasks. First attempt failed in development DI validation with
   `IComposedEmailCommandPublisher` missing (the fake had omitted it, which proved the validation sees every consumer of
   the replaced registrations); with the third publisher added: `GET /health` 200,
   `POST future/orders` without a token 401, clean shutdown. **No database, no order was processed.**
3. A scratch `dotnet restore` with only the csproj layers (the Dockerfile's restore layer), which succeeded.

## 5. Options

### Option A (recommended): vendor the orders API host, replace the Service Bus publishers with capture fakes

Production `components/api` and `components/shared`, unmodified except two sites, on the shared Postgres.

| What | Production | Local |
| --- | --- | --- |
| Queue to the Email and SMS hosts | Service Bus via Wolverine | the three publisher interfaces implemented by capture fakes (section 6.4); `AddWolverineServices` is not called |
| Results coming back | five Wolverine listeners | the capture fake's responder calls `IEmailNotificationService.UpdateSendStatus` and `ISmsNotificationService.UpdateSendStatus` after the claim has committed, which is exactly what the production result handlers do ([`EmailSendResultHandler.cs:23-45`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Integrations/Wolverine/Handlers/EmailSendResultHandler.cs#L23-L45) is a parse followed by that call) |
| Email and SMS hosts | separate hosts | not run; the instant-order clients stay production code and call two localtest endpoints on the host itself |
| Providers (ACS, Link Mobility) | real | not reached; the capture is the provider |
| Recipient lookup | Profile, Register, PDP | the same HTTP calls against the stack's Register (Profile routes), Register and PDP, after the Profile additions of section 6.7 |

Pros: all consumer behaviour is production's; the diff against upstream is two sites; no new infrastructure; the
capture is exactly the tester's view. Cons: the Wolverine retry and dead-letter behaviour of the result queues is not
exercised (a result is applied once, in process); anything registered that needs `IMessageBus` would fail at start (none does: the development DI
validation in the scratch start passed).

### Option B: vendor the API host plus the Email and SMS hosts with a broker stand-in

Run all three hosts with the upstream mocks (`MockEmailClient`, `MockSmsClient`) over a real broker: the Azure Service
Bus emulator from `tools/asb-emulator` needs the emulator image and a SQL Server 2022 container
([compose](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/tools/asb-emulator/docker-compose.yaml#L4-L28)),
an EULA acceptance, a queue configuration file, and a pre-created set of queues (the host disables Wolverine's system
queues in development). That is four more containers (emulator, SQL Server, Email, SMS) and three images to build in
a stack that runs on a 7.8 GB machine, to reproduce a transport whose behaviour no local consumer reads. There is no
capture either: the mocks answer success and only log, so a tester sees nothing. Wolverine local queues cannot replace
the broker because they are per process. **Rejected**: highest fidelity of the transport, none of the visibility the
task asks for, cost far above the value. Revisit only if a consumer ever needs the dead-letter path.

### Option C: a fake-only service implementing the consumer contract

Written fresh (the old localtest's copy is a basis for nothing). The surface in section 2.3 is about twenty endpoints
with real semantics: order chains with reminders, idempotency, recipient validation and the 422/problem-detail
vocabulary, shipment manifests, a status feed with continuation, `links.self` that must be followable, creator scoping,
condition checks, contact-point resolution through three platform services. A realistic fake is 1 500 to 2 500 lines
and needs its own test suite to keep up with a very active upstream (`main` moved with a schema change to the
notification tables while this research ran). It would still need the Profile additions for
person and organisation recipients. **Rejected**: the project rule is production code with small fakes when the build
chain allows it, and here it does; the Events precedent (production repositories, functions and migrations run
unchanged, queues replaced by in-process delivery, webhooks captured, `api/localtest/` for the GUI) applies one to one.

### Recommendation

**A.** The deciding facts are the ones TODO.md thought were against it: the Kafka coupling does not exist, and the
Service Bus coupling is one registration call and three publisher classes. C would only be right if the build chain
failed, and B only if transport fidelity mattered.

## 6. Option A in detail

### 6.1 What to copy

Under `localtest/components/notifications/vendor/`, layout preserved: root `Directory.Build.props`,
`Directory.Build.targets`, `Directory.Packages.props`, `global.json`, `stylecop.json`, `components/api/src/` (five
projects), `components/shared/src/`, and `components/api/Dockerfile` for reference. Not copied:
`components/email-service`, `components/sms-service`, every `test/` folder, `tools/`, `docs/`, `.github/`. Copy with
`git archive` as for Events, first commit unmodified, `vendor/UPSTREAM.md` with pin, paths and the
`tools/repin-vendor.py` invocation.

### 6.2 Directive sites (two, one of them additive)

Both are in [`Program.cs`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs):

1. **Line 43**, after `SetConfigurationProviders(builder.Configuration);`, additive:
   `#if LOCALTEST builder.AddLocaltestConfiguration(); #endif`. Needed because `ConfigureServices` reads
   `GeneralSettings`, `PlatformSettings`, `PostgreSQLSettings` and `NotificationConfig` eagerly
   ([`Program.cs:197`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications/Program.cs#L197), :222). The defaults sit just below the environment
   variables (about 25 keys, section 6.5).
2. **Line 226**, `services.AddWolverineServices(config, builder.Environment);`, production first:
   ```csharp
   #if !LOCALTEST
       services.AddWolverineServices(config, builder.Environment);
   #else
       Altinn.Notifications.Localtest.LocaltestNotificationsExtensions.AddLocaltestMessaging(services, config);
   #endif
   ```
   `AddWolverineServices` throws without a Service Bus connection string and registers hosted services that connect at
   start; it cannot be neutralised by configuration or by a later `Replace`. This exact edit is the one built and
   started in the scratch run.

Everything else is DI replacement or configuration, and the order of `ConfigureServices` makes site 2 the right
place for all of it: `AddAuthorizationRulesAndHandlers` (which registers the Key Vault key provider, :324) and
`AddAuthorizationService` (which registers `SendConditionClient`'s Maskinporten handler) run before line 226, so
`AddLocaltestMessaging` can replace `IPublicSigningKeyProvider` and call `AddLocaltestMaskinporten`. The two instant
clients are registered later (`AddAltinnClients`, :228), so they are **not** replaced: they stay production code and
are pointed at the host's own origin, where a localtest controller plays the Email and SMS hosts' two HTTP endpoints
(section 6.3). That keeps the directive count at two. Not built: the scratch run replaced only the publishers.

### 6.3 Fakes table (`fakes/`, namespace `Altinn.Notifications.Localtest`, compiled into the host)

| File | Replaces or adds | Lines (estimate) |
| --- | --- | --- |
| `LocaltestNotificationsExtensions.cs` | the two hooks: configuration defaults; publishers, key provider, Maskinporten | 160 |
| `LocaltestNotificationsOptions.cs` | `Localtest:Notifications:{PlatformEndpoint,ResetOnStart,Delivery…}` | 35 |
| `Messaging/CapturingPublishers.cs` | `IEmailCommandPublisher`, `IComposedEmailCommandPublisher`, `ISendSmsPublisher` | 110 |
| `Messaging/LocaltestResponder.cs` | in-process `Channel` and a hosted service that applies the send result and, after a short delay, the delivery report; configurable outcome | 130 |
| `Messaging/LocaltestInstantController.cs` | the Email and SMS hosts' instant endpoints (`notifications/email/api/v1/instantemail` answers 202, `notifications/sms/api/v1/instantmessage/send` answers 200): record the message and answer, so the production instant clients run unchanged | 70 |
| `Capture/LocaltestCaptureStore.cs`, `Capture/LocaltestChanges.cs` | writes `localtest.capture` rows and raises change notifications for the stream | 130 |
| `Auth/LocaltestPublicSigningKeyProvider.cs` | `IPublicSigningKeyProvider` over the shared certificate (copy of Events', 40) | 40 |
| `Maskinporten/*` | copy of the shared `IMaskinportenService` fake (176) for `SendConditionClient` | 176 |
| `Startup/LocaltestDatabaseStartup.cs`, `LocaltestAdminDatabase.cs`, `prepare.sql`, `reset-on-start.sql` | after the migrations and before the loops start: the `localtest` schema and the reset (copy of Events' pattern) | 130 |
| `Inspection/LocaltestNotificationsController.cs`, `LocaltestNotificationsStream.cs` | `notifications/api/localtest/` (section 6.9) | 400 |

About **1 400 lines** of C# and SQL (Events: 1 192; Resource Registry: 540). A `postgres-init/50-notifications.sh` of 20
lines.

### 6.4 The capture and the responder (the one design constraint)

Each publisher records the production `Email`, `ComposedEmail` or `Sms` model (to, from, subject, body, content type,
notification id; the order is joined in on read) into `localtest.capture` and posts a work item to an in-process
`Channel`. A hosted service takes items off the channel and, **after the claim transaction has committed**, calls
`UpdateSendStatus` with the configured outcome: `Succeeded` then `Delivered` for e-mail, `Accepted` then `Delivered`
for SMS (defaults, section 6.5). The delay (a few hundred milliseconds, then a retry on lock timeout) is required: the
publisher is called between `GetNewNotification…` and `CommitUnitOfWork`
([`SmsNotificationService.cs:114-125`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Services/SmsNotificationService.cs#L114-L125)), so the notification row is
locked by the caller's own transaction (`FOR UPDATE … SKIP LOCKED`), and an update from inside `PublishAsync` would
wait for a lock that is only released after it returns. Production avoids the problem by going through the queue; the channel is the in-process
equivalent. `Failed_*` outcomes are driven by `Localtest:Notifications:Delivery` (a global setting) and, if the product
owner wants it, a per-address convention (open question 4). `Failed_TransientError` is mapped by the production service
to `New`, which is a retry loop; the responder should not emit it by default.

Instant orders skip the claim: the production instant service posts to the localtest instant controller, which records
the message and answers, and the production service writes the notification with its own status.

### 6.5 Configuration defaults (inserted just below the environment)

| Key | Value |
| --- | --- |
| `GeneralSettings:BaseUri` | the platform origin **without a trailing slash** (`http://local.altinn.cloud:8000`); response links are built from it |
| `GeneralSettings:OpenIdWellKnownEndpoint`, `JwtCookieName` | `{platform}/authentication/api/v1/openid/`, `AltinnStudioRuntime` |
| `PlatformSettings:ApiProfileEndpoint`, `ApiRegisterEndpoint`, `ApiAuthorizationEndpoint` | `{platform}/profile/api/v1/`, `{platform}/register/api/v1/`, `{platform}/authorization/api/v1/` |
| `PlatformSettings:ApiShortMessageServiceEndpoint`, `ApiInstantEmailServiceEndpoint` | `http://localhost:5090/notifications/sms/api/v1/` and `http://localhost:5090/notifications/email/api/v1/`: the host itself, served by the localtest instant controller |
| `DialogportenSettings:Enabled` | `false` |
| `PostgreSQLSettings:*` | from studioctl: `Host=postgres;…;Database=notificationsdb`, roles `platform_notifications_admin` (migrator, superuser) and `platform_notifications`, password `Password`; `MigrationScriptPath` = `app/Migration` (Development resolves it relative to the parent of the working directory, `/`) |
| `NotificationConfig:*ProcessLoopConfig:TaskCount` | 1 each (production 30 × 6 loops) |
| `NotificationConfig:*ProcessLoopConfig:PrimaryTaskIdleDelaySeconds` | 1 (production 30), so an order is processed within seconds |
| `NotificationConfig:SmsSendWindowStartHour`, `SmsSendWindowEndHour` | 0 and 24: the window code uses `TimeSpan` bounds, so SMS leaves at any hour instead of waiting for 09:00 ([`NotificationScheduleService.cs:25-49`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/src/Altinn.Notifications.Core/Services/NotificationScheduleService.cs#L25-L49)); `SmsAnytime` orders are unaffected |
| `SendConditionClient:MaskinportenSettings:*` | dummy values; the shared fake serves the token |
| `kvSetting:SecretUri`, `ApplicationInsights:ConnectionString`, `OtelSettings:Endpoint` | empty |
| `WolverineSettings:*` | not read (the section is bound only inside the skipped `AddWolverineServices`) |
| `Localtest:Notifications:ResetOnStart` | `true`, as Events |
| `Localtest:Notifications:Delivery` | outcomes for e-mail and SMS: `Delivered` (default), `Succeeded`/`Accepted` (no delivery report), `Failed` plus the specific failure, `None` (stay in `Sending`) |

Environment: `ASPNETCORE_ENVIRONMENT=Development` (needed so the JWT handler reads OpenID metadata over http, as in
Events, which also brings Swagger and the developer exception page), `ASPNETCORE_URLS=http://+:5090`, `TZ`/tzdata for
`Europe/Oslo`.

### 6.6 Postgres

A database `notificationsdb`, roles `platform_notifications_admin` (superuser: the migrations `OWNER TO` it) and
`platform_notifications`, from `postgres-init/50-notifications.sh` in Events' style, baked into
[`localtest/postgres/Dockerfile`](../../localtest/postgres/Dockerfile) (one `COPY` line). Postgres 18.3 is the shared
image; upstream's CI is on 18.6, which is the same major. Migrations are Yuniql, applied by the host at start; there is
no migrator image. `reset-on-start.sql`: `TRUNCATE notifications.orderschain, notifications.orders,
notifications.emailnotifications, notifications.smsnotifications, notifications.emailtexts, notifications.smstexts,
notifications.notificationlog, notifications.statusfeed, notifications.resourcelimitlog,
notifications.deaddeliveryreports, localtest.capture RESTART IDENTITY CASCADE`, leaving `sendingtimepolicy` and Yuniql's
version table alone (table list from the `CREATE TABLE` statements in the migrations; **not run**). A 104-folder
migration run on every fresh volume is a first-start cost (not measured); a reset keeps the schema.

### 6.7 What the stack must add outside the component: Profile contact points in the Register component

The Register component serves Profile's user endpoints today
([`localtest/components/register/README.md`](../../localtest/components/register/README.md), `LocaltestProfileController`)
and `/profile` is already routed to it. Add the four anonymous internal endpoints Notifications calls, answered from
the seed in the Profile response shapes (about 250 lines in `fakes/Altinn.Register.Localtest/Controllers/`, the same
data source as `test-users`):

| Endpoint | Answer |
| --- | --- |
| `POST users/contactpoint/lookup` (`nationalIdentityNumbers`, `useStaleContactInfo`) | per found person: `userId`, `nationalIdentityNumber`, `email`, `mobileNumber` (see 6.8), `isReserved=false` |
| `POST users/contactpoint/lookupsi` (`externalIdentities`) | per self-identified user: the e-mail of the `idporten-email` identity, no mobile |
| `POST organizations/notificationaddresses/lookup` | the seed has **no** official organisation addresses: empty lists per organisation (as an organisation without a registration in production) |
| `POST units/contactpoint/lookup` (`organizationNumbers`, `resourceId`) | per organisation: the users that hold a role at the party (the `parties` and `actingFor` data `test-users` already computes), each with e-mail and mobile; the PDP then filters them by the resource's policy as production does |

Why in Register and not in the Notifications fakes: the project rule is that fakes reach vendored services the way
production counterparts do, the stack's Profile is already there, and Access Management and apps may call the same
endpoints later. A fake `IProfileClient` inside Notifications would hide the lookup from every other consumer.

### 6.8 Test-data mapping: what an app-ordered notification resolves to

Register's seed ([`localtest/components/register/testdata/testData.json`](../../localtest/components/register/testdata/testData.json),
read for this report): eight persons, thirteen organisations.

| User (id, party) | E-mail | Mobile in the seed | After normalisation (proposed) | Resolves via |
| --- | --- | --- | --- | --- |
| 1337 Sophie Salt (501337) | `1337@altinnstudiotestusers.com` | `87654321` | `+4787654321` | `lookup` |
| 1001 Pengelens Partner | `test@test.com` | `+4799315829` | same | `lookup` |
| 1002, 1003, 12345 | `test@test.com` | `87654321` | `+4787654321` | `lookup` |
| 2001 MultiParty Prompt | `multipartyprompt@test.com` | `+4790000001` | same | `lookup` |
| 2002 Kari Nordvik | `kari.nordvik@test.com` | `+4791234567` (no `telephoneNumber`) | same | `lookup` |
| 1004 self-identified | `selvregistrert.bruker@example.com` | none | none | `lookupsi` (e-mail only) |
| every organisation | none | none | none | official addresses empty; user-registered contact points only through the users holding roles |

- Seeded mobile numbers without a country code are the old localtest's convention; production returns `+47…` from the
  contact register, and Notifications' own validation of direct recipients wants `+CC…`/`00CC…`. The Profile fake should
  normalise an eight-digit Norwegian number to `+47` (a three-line rule). Not verified that the SMS path rejects a bare
  number.
- An **order for a person** (instantiation by Sophie Salt) therefore creates one e-mail to
  `1337@altinnstudiotestusers.com` and, for an `sms`/`emailPreferred` channel, one SMS to `+4787654321`. Reservations
  never occur (no reserved flag in the seed); a way to mark a test user reserved is open question 5.
- An **order for an organisation owner** (DDG Fitness AS, 500000, resource `app_ttd_{app}`): official addresses empty, so
  the result is the users with a role at 500000 whom the PDP permits for the app's resource. Roles come from the seed
  (Sophie Salt DAGL and REGNA at 500000; Pengelens Partner and Gjentagende Forelder only signing role codes). For
  most test apps the policy lists `dagl` and `regna`, so I expect Sophie Salt's address only. **Not verified**, it
  depends on the Profile fake and on the PDP answering Notifications' decision requests, which come from the
  `Altinn.Common.PEP` package (`PDPAppSI`) the apps use as well.
- An order with a **direct address** (`recipientEmail`, `recipientSms`, v1 `recipients[]`, Authentication's link mail)
  needs no lookup and works with the first slice.
- Every resulting e-mail and SMS lands in the capture and reaches `Delivered` (default), so shipments and notification
  lists look finished to Correspondence's delivery check.

### 6.9 Inspection API and the GUI (answers the closing question of the brief)

**Yes, the inspection of captured notifications belongs in the component's `api/localtest/`**, as for Events, for
three reasons: the capture has to be stored in the component's own database and ordered with the production tables, the
component already owns the reset, and the localtest app's GUI (task 8) is specified as reading components' `api/localtest/`
endpoints through the front door. A shared capture service for Notifications and Correspondence is not needed: Correspondence
orders through Notifications, so its notifications appear in this capture with the Correspondence creator, and its own
inbox view is a separate concern.

`notifications/api/localtest/` (anonymous), modelled on Events':

| Call | Returns |
| --- | --- |
| `GET captures?after=&limit=&channel=email\|sms&creator=&orderId=&to=&q=` | `{ items, next }` in capture order: `id`, `channel`, `notificationId`, `orderId`, `creator`, `sendersReference`, `to`, `from`, `subject`, `body`, `contentType`, `capturedAt`, `result`, `resultAt` |
| `GET captures/{id}` | one capture with its notification row |
| `GET orders?after=&creator=&sendersReference=&limit=` | the production orders with `processingStatus`, recipient counts and per-status counts of their e-mail and SMS notifications |
| `GET orders/{id}` | the order, its notifications with `sendStatus` history, its captures |
| `GET stream` | server-sent events, `{ component: "notifications", entity: order\|notification\|capture\|all, kind: created\|updated\|reset, id, at }` and a comment every 15 s |
| `DELETE captures`, `DELETE orders` | truncate and notify |

The GUI reads `captures` as a mailbox view (one row per mail or SMS with the rendered body, filter on creator and
recipient), `orders/{id}` as the order drill-down, and `stream` to refresh. The production `trigger/*` endpoints are
anonymous in this host, so the page or a test can also force `pastdueoneorder` and `sendemail` without waiting for the
loop; they need no addition.

### 6.10 Dockerfile

Mirror [`components/api/Dockerfile`](https://github.com/Altinn/altinn-notifications/blob/a4aa959a5c50094048aceb98af8dccd9db17fca8/components/api/Dockerfile)
line for line (same pinned SDK and aspnet digests, the layered restore, `dotnet publish -c Release`, `tzdata`, the
`Migration` and `Views` copies, user `dotnet`). Deviations, each marked `localtest:`: the build context is the component
folder, so the paths gain a `vendor/` prefix (`COPY vendor/Directory.Packages.props …`), the two files
`localtest.build.props`/`.targets` and `fakes/` are copied first, `ENV DirectoryBuildPropsPath/DirectoryBuildTargetsPath`
before restore and publish, and in the runtime stage
`ENV ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://+:5090 PostgreSQLSettings__MigrationScriptPath=app/Migration`
and `Logging__LogLevel__Default=Information`. `components/shared/src` and the whole of `components/api/src` (including
`DbTools`, referenced from `Persistence`) go into the build stage as upstream does.

### 6.11 studioctl and image integration

All in the vendored studioctl source under `localtest/studioctl/src/cli/`, modelled on the Events and Resource Registry
specs ([`components.go`](../../localtest/studioctl/src/cli/internal/cmd/env/localtestv2/components.go)):

| Where | Change |
| --- | --- |
| `internal/cmd/env/localtestv2/components.go` | `componentNotifications = "notifications"`, host port `8029` (8020 to 8028 are taken), container port `5090`, `healthPath = "/health"`; `notificationsComponent` with `certSettingEnv`, `Localtest__Notifications__PlatformEndpoint`, `PostgreSQLSettings__AdminConnectionString` and `__ConnectionString` (`platform_notifications_admin`/`platform_notifications`, `Database=notificationsdb`); volumes: `certMount` and a read-only bind of the shared certificate at `/accesstoken/accesstokencredentials.pfx` (the platform-token generator reads its default path, exactly as Events); `extraHosts = ingressHostEntries(opts)` so the condition callbacks reach `local.altinn.cloud:8000`; deps `postgres`, `register`, `authorization`; `routes = {host: AppHostName, prefix: "/notifications"}` |
| `components(opts)` | one line in start order, after `events` |
| `internal/config/images.go` | `Notifications ImageSpec`, `localtestV2Image("notifications")`, env override `STUDIOCTL_IMAGE_LOCALTEST_V2_NOTIFICATIONS`, entry in the iteration list |
| `internal/cmd/env/localtestv2/topology_internal_test.go` | expected route `local.altinn.cloud/notifications` → `hostPortNotifications` |
| `.github/workflows/images.yml` | matrix entry `notifications` and the name in the manifest job's list |
| `localtest/postgres/Dockerfile` | `COPY components/notifications/postgres-init/50-notifications.sh …` |
| Provisioning (`AppProvisioner`) | **nothing**: orders are created at run time by apps, not pushed at deploy; no per-app step |
| Health | `GET /health` (production, always healthy; a database failure shows at startup as a Yuniql error, not in the health check) |
| Reset | the component empties its tables on start; `studioctl env reset` stays as is |

The previously planned `notifications/` "FAKE ONLY: contract-shaped stub" row of
[`../mvp-vendoring-model.md`](../mvp-vendoring-model.md) is superseded by this report (the model's own rule: vendor when
the build chain allows).

### 6.12 Verification plan (`components/notifications/smoke-test.sh`, through the proxy)

Each item is a check in the style of Events' smoke test (`ok`/`FAIL`, `waitfor` for the asynchronous parts):

1. `GET notifications/health` 200 on the host port; `POST notifications/api/v1/future/orders` without a token 401.
2. `DELETE notifications/api/localtest/orders` and `captures` return 204 and both lists are empty.
3. `POST orders/email` with a platform access token (`certs/mint-platform-access-token.sh ttd testapp`) and a direct
   address answers 201 `{ orderId }`; the order appears at `notifications/api/localtest/orders` with creator `ttd`;
   within a few seconds a capture exists with the posted subject and body and the notification reaches `Delivered`.
4. `GET orders/{id}/status` shows `processingStatus` completed and an e-mail summary whose `links.self` is under the
   platform origin; following it returns the notification with `Delivered`.
5. `POST orders/sms` with a `+47` number: a capture, status `Delivered`; with `Localtest:Notifications:Delivery` set
   to a failure outcome (restart or a runtime setting) the status is the failure.
6. `POST future/orders` with `recipientEmail`, `recipientSms`, `recipientPerson` (Sophie Salt's national identity
   number), `recipientOrganization` (DDG Fitness AS with `resourceId` of the fixture app) and
   `recipientExternalIdentity` (`urn:altinn:person:idporten-email:selvregistrert.bruker@example.com`): 201 with
   `notificationOrderId` and `shipmentId`; `GET future/shipment/{id}` lists the recipients with channel and status; the
   person's capture goes to `1337@altinnstudiotestusers.com`; the external identity's to the self-identified address.
7. Idempotency: the same `idempotencyId` twice answers 201 once and the second time the existing order (production
   behaviour; note the actual status code); the same body with another key creates a second order.
8. An order for a person without a contact point (a national identity number that is not in the seed) answers `422`
   with a problem code (`NOT-00001` for the callers above).
9. `PUT orders/{id}/cancel` on an order with a future `requestedSendTime` answers 200 and the order is `Cancelled`; no
   capture is produced when the time passes.
10. A `conditionEndpoint` that answers `{ "sendNotification": false }` (the fixture app's callback or a local sink)
    leaves the order without a capture; `true` produces one. With the Maskinporten fake in the client the call is
    authenticated as in production.
11. Instant order: `POST future/orders/instant/email` (and `sms`) with a direct address answers and a capture appears, through
    the localtest instant controller and the unchanged production client.
12. Bearer path: a token from `/Home/GetTestOrgToken?scopes=altinn:serviceowner/notifications.create` creates an order
    and reads it back (the Correspondence credential).
13. Authentication's `AltinnNotificationClient`, through the real flow of `SelfIdentifiedLinkService` if there is a
    cheap way to start it, otherwise the same `future/orders` body posted with a `platform`/`authentication` token: a
    capture with the HTML body.
14. Restart the container: orders, captures and notifications are empty; `sendingtimepolicy` keeps its rows.
15. The contract with Correspondence, once task 7 exists: its delivery check reads the shipment to a terminal
    status.

Run next to the existing suites: `components/register/smoke-test.sh` (the Profile additions are its own checks:
`lookup` for 1337 and 1004, `units/contactpoint/lookup` for 500000) and the root `smoke-test.sh`.

### 6.13 Effort and risk

**6 to 7 days** to a green smoke test: vendoring, props and Dockerfile 0.5 day; the two hooks, configuration and
responder 1.5 days; Postgres script, startup and reset 0.5 day; inspection API 1 day; Profile contact points in the
Register component 1 day; studioctl, image workflow, tests 1 day; first run, migrations, fixes 0.5 to 1.5 days.

| Risk | Note |
| --- | --- |
| Migrations on the shared Postgres | 104 version folders, never run by this stack, Postgres 18.3 vs upstream's 18.6; Yuniql default version table in a fresh database. First thing to run |
| Responder and the claim transaction | the update must happen after the commit (6.4); get it wrong and notifications sit locked or flap. The smoke test items 3 to 5 cover it |
| Two directive sites | both in `Program.cs`, the second in the middle of `ConfigureServices` next to registrations that change often; the repin script handles them, but a conflict there is the likeliest repin cost |
| Profile response shapes | the Profile DTOs and Notifications' own DTOs are different classes with the same JSON; verified by reading both ends, not by a call |
| Very active upstream | `main` moved from `c9e1d23` (2026-10-16) to the pin with a schema change to the notification tables; every re-pin runs 104 migration folders again on a fresh volume, and the Dockerfile and `Directory.Packages.props` change often |
| Wolverine package stays in the image | the `WolverineFx.*` assemblies ship although unused; harmless (the scratch start proves nothing instantiates them) and removing the package references would need a vendored `.csproj` edit |
| Image size and build time | not measured; the closure is comparable to Events and larger than Resource Registry |

### 6.14 Open questions for the product owner

1. **Default outcome of a captured message**: `Delivered` for every e-mail and SMS (proposed), or stop at `Succeeded`
   and `Accepted` so the delivery report never arrives unless a tester asks for it?
2. **Number normalisation**: add `+47` to bare eight-digit seed numbers in the Profile fake (proposed), or change the
   seed to `+47…` for every user (the seed is also what the old localtest's apps read)?
3. **Who owns the Profile contact-point endpoints**: the Register component's fakes (proposed), or a separate Profile
   component later? This decides whether the Register README is updated now.
4. **Simulating failures**: only a global setting (proposed), or a per-recipient convention such as an address that
   contains `bounce@` ending in `Failed_Bounced`, so one app run can show both outcomes?
5. **Reserved recipients**: add a `reserved` flag to the test data so a tester can see the reservation path, or leave
   reservations out locally?
6. **Condition checks**: keep the Maskinporten fake in the client (production authentication of the callback) or replace
   `IConditionClient` with a plain HTTP client? The fake costs 176 copied lines and one more consumer of the shared file.
7. **Instant orders and composed e-mail** (`future/orders/instant`, `future/orders/composed-email`, attachments by SAS
   URL): planned as captured like the rest; attachments are recorded by reference only. Acceptable?
8. **Whether Correspondence (task 7) should share the capture GUI**: recommended no, as it appears in this capture with
   its own creator; confirm.

## 7. Not verified

- **No database was involved.** The Yuniql run, the reset script's table list, the claim and result path, the
  responder timing and every status transition the repository allows are read, not run. Whether `Delivered` can be
  written straight after `Succeeded` by the `UpdateSendStatus` function is not checked.
- The scratch start proves the DI graph and the two middlewares only; no order was posted, no token validated, no
  JWT metadata fetched.
- The Profile additions are not written; the response shape match between Profile's and Notifications' DTOs is from
  reading both.
- Whether the PDP answers `Altinn.Common.PEP`'s decision request from Notifications (platform token, request shape)
  as it does for apps is inferred, not exercised. The same for `parties/nameslookup` at the vendored Register.
- The 422 body and `NOT-00001` code come from the controllers and from two consumers' branches; no response was seen.
- That the SMS path accepts a bare eight-digit number was not checked.
- The committed `04-functions-and-procedures.sql` equals what `DbTools` would generate was not checked.
- Image size, build time and memory of the running host were not measured.
- Option B was costed from the compose file and the code, not tried; Option C's line estimate is a judgement.
- The clone of `altinn-notifications` is shallow, so when Kafka was removed was not read from history;
  that no occurrence exists at the pin is a fact.
- app-lib behaviour at the pin was read in `src`; the apps in `altinn-studio/src/test/apps` were not run against any
  of this.
