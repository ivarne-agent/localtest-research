# Localtest component: vendoring production Altinn Events

Research date: 2026-10-18. Read-only: nothing was built or run. Sources, all pinned:
[`altinn-events@41297b5`](https://github.com/Altinn/altinn-events/tree/41297b521b983f8624a847f286ee70f93f3ad273) (main, 2026-10-16; `git fetch` today, no newer commit),
[`altinn-studio@0283909`](https://github.com/Altinn/altinn-studio/tree/028390958dfe0e7a91479daca89baa5d654f5946) (origin/main, 2026-10-17, app-lib v9),
[`app-lib-dotnet@8d059b8`](https://github.com/Altinn/app-lib-dotnet/tree/8d059b8c5e92df15905f273fa1c22dda91113da4) (main, 2026-10-16, app-lib v8).
Runtime behaviour inferred from code is marked "not confirmed".

> **Status 2026-10-19: built** as [`localtest/components/events`](../localtest/components/events/README.md), vendored at
> `1a4a80c` (one documentation commit after the pin below). Differences from this plan:
>
> - **No Postgres**: at the product owner's request the database is an in-memory store with repositories that follow
>   the production SQL, behind `PostgreSQLSettings:EnableDBConnection=false`. The init script, the `eventsdb` roles and
>   the `trace_log` partition service below are not needed.
> - **Wolverine local routing works** (risk 1): the production bus publishers reach the production handlers in-process,
>   and the production retry policies attach through `services.ConfigureWolverine`.
> - **Register main units work** (risk 2) once outgoing platform tokens can be signed: Events never binds the client
>   `AccessTokenSettings`, so the certificate is mounted at the default `/accesstoken/accesstokencredentials.pfx`.
> - **Scrutor 7** keeps a decorated registration as a keyed service, so replacing `ISubscriptionRepository` has to remove
>   the keyed descriptor too, then decorate again.
> - The http directive in `SubscriptionController` is in (open question 2 answered for the test sink and v8 apps).

## Summary

- **Vendor it.** The blockers in [`events-notifications.md`](events-notifications.md) (16 Oct) do not hold with the
  approach Storage, Register and Access Management now use:
  - **Azure Service Bus**: every Service Bus endpoint is behind six `WolverineSettings:Enable*` flags. With all six
    off, Wolverine gets no Service Bus transport and falls back to its default local routing, so the API's own
    handlers (register, validate, outbound) run in-process. That is what Storage relies on in Development. Two DI
    replacements in `fakes/` swap the legacy Storage Queue publishers for the production bus publishers. No
    emulator, no MS SQL.
  - **Key Vault**: only loaded when `kvSetting:SecretUri` is set. Platform access tokens use the same
    `IPublicSigningKeyProvider` fake as Storage, so no directive is needed here.
  - **https-only subscriptions**: one directive in `SubscriptionController.IsValidUri`, and only if we want
    subscriptions at all. v9 apps never subscribe.
  - **Register v2 internal endpoints**: the vendored Register component serves them now.
  - **Azure Functions**: not needed. The API project contains every handler the Functions host has.
- **Expected change to the vendored tree**: 3 directive sites, about 12 lines: two pure `#if LOCALTEST` hooks in
  `Program.cs` and the http-scheme switch. Fakes come to about 600 to 800 lines. Estimate: 3 to 4 days including the
  inspection API. Risk: low to medium (see Risks).
- **Inspection seam**: a `DelegatingHandler` on the production named `HttpClient` `"WebhookService"`. Every webhook
  POST (event delivery and subscription validation) goes through it. Add to that the production tables
  `events.events` (every published event with status and retries) and `events.trace_log` (registered,
  authorized/unauthorized, queued, webhook response code). A localtest-only read API under
  `events/api/localtest/` serves the GUI, with an SSE stream so the GUI does not poll.

## 1. Who calls Events

| Caller | Call | Auth | Notes |
| --- | --- | --- | --- |
| v9 `EventsClient.AddEvent` ([`EventsClient.cs:56-116`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Events/EventsClient.cs#L56-L116)) | `POST {ApiEventsEndpoint}app`, JSON CloudEvent (`subject=/party/{id}`, `alternativesubject=/org/{orgnr}` \| `/person/{ssn}` \| external id, `source={ExternalAppBaseUrl}instances/{partyId}/{guid}`) | Bearer user or service-owner token, plus `PlatformAccessToken` header (issuer = org, app = app name), plus `Idempotency-Key` (GUID) | From the workflow-engine commands [`InstanceCreatedAltinnEvent`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Internal/WorkflowEngine/Commands/AltinnEvents/InstanceCreatedAltinnEvent.cs#L32-L37), [`MovedToAltinnEvent`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Internal/WorkflowEngine/Commands/AltinnEvents/MovedToAltinnEvent.cs#L37-L43), [`CompletedAltinnEvent`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Core/Internal/WorkflowEngine/Commands/AltinnEvents/CompletedAltinnEvent.cs#L37-L42) these are **service-owner** token + engine `StepId` as the idempotency key. Also [`InstancesController.RegisterEvent`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/App/backend/src/Altinn.App.Api/Controllers/InstancesController.cs#L2197-L2213) (`app.instance.substatus.changed`, current user, no key). Gated by `AppSettings.RegisterEventsWithEventsComponent` (process-transition-test: [`appsettings.json:12`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/test/apps/process-transition-test/App/appsettings.json#L12)). A failed command is `Retryable`, which is the cause of the 404 retry loop and namespace throttling. |
| v8 `EventsClient.AddEvent` ([`EventsClient.cs:68-117`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Infrastructure/Clients/Events/EventsClient.cs#L68-L117)) | same endpoint, no idempotency key | Runtime cookie/bearer of the current user plus platform access token | `app.instance.created` ([`InstancesController.cs:433,763,948`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Api/Controllers/InstancesController.cs#L433)), `substatus.changed`, `process.movedTo.*` / `process.completed` ([`ProcessEventDispatcher.cs:51-72`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Internal/Process/ProcessEventDispatcher.cs#L51-L72)). Failures are only logged. |
| v8 `EventsSubscriptionClient.AddSubscription` ([`EventsSubscriptionClient.cs:55-104`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Infrastructure/Clients/Events/EventsSubscriptionClient.cs#L55-L104)) | `POST subscriptions` with `endPoint={appBaseUrl}api/v1/eventsreceiver?code=…`, `sourceFilter=appBaseUrl`, `typeFilter` | **None from app-lib.** The app has to add a Maskinporten handler itself, otherwise it gets 401 ([`ServiceCollectionExtensions.cs:281-290`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Extensions/ServiceCollectionExtensions.cs#L281-L290)). | Only caller: `EformidlingStartup` (hosted service, rethrows on failure). Deliveries go to [`EventsReceiverController`](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Api/Controllers/EventsReceiverController.cs) (`{org}/{app}/api/v1/eventsreceiver`, checks `code`, answers `platform.events.validatesubscription`). v9 removed the receive stack ([altinn-studio#19859](https://github.com/Altinn/altinn-studio/issues/19859)). |
| `events/api/v1/events` (generic publish), `GET app/{org}/{app}`, `GET app/party`, `GET events` | – | – | Not called by either app-lib. |
| Other localtest components | none | – | No vendored component (Storage, Register, PDP, Access Management, workflow engine) references Events (grep for `events/api`, `ApiEventsEndpoint`). Production Correspondence publishes generic events, which matters only once Correspondence is vendored. The new GUI is the second caller (section 4). |

The app runner already exports `PlatformSettings__ApiEventsEndpoint=$PLATFORM/events/api/v1/` (`localtest/run-app.sh:42`),
so apps need no change. The ingress has no `/events/` route yet.

## 2. The production service today

| Aspect | What the code does |
| --- | --- |
| Projects | [`src/Events`](https://github.com/Altinn/altinn-events/tree/41297b521b983f8624a847f286ee70f93f3ad273/src/Events) (ASP.NET host, about 9.4k lines, 59 SQL migration files), [`src/Events.Common`](https://github.com/Altinn/altinn-events/tree/41297b521b983f8624a847f286ee70f93f3ad273/src/Events.Common) (one model), [`src/DbTools`](https://github.com/Altinn/altinn-events/tree/41297b521b983f8624a847f286ee70f93f3ad273/src/DbTools) (build-time generator for `vX/…functions-and-procedures.sql`; the generated files are committed, and its `PostBuild` exec only runs when the tool binary exists, so it is skipped in a plain Release build, not confirmed), [`src/Events.Functions`](https://github.com/Altinn/altinn-events/tree/41297b521b983f8624a847f286ee70f93f3ad273/src/Events.Functions) (Azure Functions isolated worker, 2.2k lines, no Dockerfile). Root [`Dockerfile`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/Dockerfile) builds Events + DbTools + Common, port 5080. No `Directory.Build.props`/`Directory.Packages.props` upstream. |
| Bootstrap | Plain `WebApplication` ([`Program.cs`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs)), no Aspire, no service defaults. Key Vault only if `kvSetting:SecretUri` is set ([L107-L112](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L107-L112)). App Insights only with a connection string ([L323-L330](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L323-L330)). Yuniql migrations at startup when `PostgreSQLSettings:EnableDBConnection` ([L377-L396](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L377-L396)). JWT metadata over http only in Development ([L219-L222](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L219-L222)). |
| Postgres | db `eventsdb`, schema `events`, roles `platform_events_admin` (migrations) / `platform_events`. Tables: `events` (`cloudevent jsonb`, `idempotencykey`, `status` 1 Registered / 2 Processed / 3 RetryExhausted, `retrycount`, `retryreason`), `subscription`, `trace_log` (range-partitioned by year: [`y2025`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Migration/v0.46/03-setup-table-partitions.sql), [`y2026`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Migration/v0.50/01-setup-new-partition.sql) only), `event_status`. `CREATE EXTENSION pg_trgm` (a trusted contrib extension, available in `postgres:16-alpine`). No Blob storage. |
| Event pipeline | `POST app` → [`AppController`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/AppController.cs#L65-L100) checks that `source` path starts with `/{issuer}/{app}` of the platform token ([L84](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/AppController.cs#L84)) → `EventsService.RegisterNew` → `IRegistrationEventPublisher` → (queue) → [`RegistrationEventHandler`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Wolverine/Handlers/RegistrationEventHandler.cs) inserts with status 1 and idempotency check → in-process [`RegisteredEventsBackgroundService`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/BackgroundServices/RegisteredEventsBackgroundService.cs) claims rows (`events.claim_registered_event()`, 2 s idle delay) → [`OutboundService.PostOutbound`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/OutboundService.cs#L79-L124) (matching subscriptions, Register main-unit lookup when `alternativesubject` is an org, multi-decision PDP per consumer) → `bus.SendAsync(OutboundEventCommand)` ([L336-L340](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/OutboundService.cs#L336-L340), always on the bus from this path: [`RegisteredEventsProcessingService.cs:69`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/RegisteredEventsProcessingService.cs#L69)) → [`OutboundEventHandler`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Wolverine/Handlers/OutboundEventHandler.cs) → [`WebhookService.Send`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/WebhookService.cs#L42-L67) (plain POST of the CloudEvent, non-2xx throws). |
| Messaging | Wolverine 6.46. [`AddWolverineServices`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Extensions/WolverineServiceCollectionExtensions.cs#L27-L51) configures Service Bus only if [`IsAzureServiceBusEnabled`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Configuration/WolverineSettings.cs#L42-L45) (any of six flags). `ConfigureEventsDefaults`, which disables local routing, is only called in that branch. The two publishers are chosen by flag: [bus or legacy Storage Queue](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Extensions/WolverineServiceCollectionExtensions.cs#L149-L177). The default [`appsettings.json`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/appsettings.json#L40-L48) turns four flags **on**, so localtest must force them off. Retry policies (`*HandlerPolicy`, outbound up to 12 h scheduled retries) are only attached to Service Bus listeners. The unit tests boot the host with all flags off ([`appsettings.unittest.json:16-23`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/test/Altinn.Platform.Events.Tests/appsettings.unittest.json#L16-L23)), which shows the host starts without Service Bus (with mocked services). |
| Azure Functions | Queue-triggered `EventsRegistration`/`EventsInbound`/`EventsOutbound`/`SubscriptionValidation` for the legacy Storage Queue path, plus optional Service Bus listeners for outbound/validation with their **own copy** of `WebhookService`. Each call goes back to the API's `inbound`/`outbound`/`storage/events`/`storage/events/logs` endpoints with a Key Vault certificate. Everything it does also exists in the API as Wolverine handlers. |
| Subscriptions | [`SubscriptionController.Post`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/SubscriptionController.cs#L54-L100): https-only `endPoint`/`sourceFilter` ([L191-L194](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/SubscriptionController.cs#L191-L194)). App subscription if the resource is `urn:altinn:resource:app_*` or the source host ends with `PlatformSettings:AppsDomain`, otherwise scope `altinn:events.subscribe`. PDP check, insert, then `ISubscriptionValidationPublisher` → [`SendAndValidate`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/SubscriptionService.cs#L142-L160) posts `platform.events.validatesubscription` to the endpoint and marks it valid on 2xx. Lookups cached `SubscriptionCachingLifetimeInSeconds` (3600 s). |
| Auth dependencies | Inbound: `AddJwtCookie` against `GeneralSettings:OpenIdWellKnownEndpoint` (issuer/audience not validated); `PlatformAccess` policy via `AccessTokenHandler` + `IPublicSigningKeyProvider` (Key Vault in production). Outbound: PDP (`Altinn.Common.PEP` `PDPAppSI`, `PlatformSettings:ApiAuthorizationEndpoint`); Register `v1/parties/lookup`, `v2/internal/parties/query?fields=identifiers`, `v2/internal/parties/main-units` ([`RegisterApiClient.cs`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/RegisterApiClient.cs#L79-L153)) with a `platform`/`events` platform token from `AccessTokenGenerator` (AccessTokenClient 3.2.12, same as Storage). `ApiProfileEndpoint` is configured but unused. No Resource Registry client: generic-event policies go through the PDP. |
| Feature flags | Only configuration: `WolverineSettings:Enable*`, `PostgreSQLSettings:EnableDBConnection`, `EventsProcessingSettings:TaskCount` (0 disables the background processor). No FeatureManagement. |

## 3. Vendoring plan

### What to copy (`localtest/components/events/vendor/`, `git archive` of 41297b5, committed unmodified first)

`src/Events/` (including `Migration/`), `src/Events.Common/`, `src/DbTools/` (referenced by relative path), root
`stylecop.json`, `.editorconfig`, the upstream `Dockerfile` for reference. **Not** `src/Events.Functions`, `emulator/`
or `test/`.

### Build files

As in Storage, but `localtest.build.props` must guard the import because no `Directory.Build.props` exists upstream
(`Condition="'$(…)' != '' And Exists(…)"`). It defines `LOCALTEST`, sets `TreatWarningsAsErrors=false` and adds
`NoWarn CS1591` (the host has `GenerateDocumentationFile=true`). `localtest.build.targets` compiles `fakes/**/*.cs` into
`Altinn.Platform.Events`, because the interfaces to replace (`IEventsQueueClient`, `ITraceLogService`, the publishers)
live in the host assembly. Fakes are in the same assembly, so they can also use the `internal`
`WebhookService.HttpClientName`.

### Directive sites (3, about 12 lines)

| File | Directive | Why |
| --- | --- | --- |
| [`Program.cs:62`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L62) (after `SetConfigurationProviders`) | `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestConfiguration(builder.Configuration); #endif` | `ConfigureServices` reads `WolverineSettings` and the connection string eagerly, so defaults and forced values must be in place first (same reason as Storage site 1). |
| [`Program.cs:66`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L66) (after `ConfigureServices`) | `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestEvents(builder); #endif` | Single DI override entry point, after the production registrations. |
| [`SubscriptionController.cs:191-194`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/SubscriptionController.cs#L191-L194) `IsValidUri` | `#if !LOCALTEST` https only `#else` https or http `#endif` | Apps and the GUI sink are on `http://local.altinn.cloud:8000`. Needed only if subscriptions are in scope (open question 2). |

No directive for Wolverine, Key Vault, App Insights or Yuniql: all are configuration-driven.

### Fakes (`fakes/`, namespace `Altinn.Platform.Events.Localtest`)

| Replaces / adds | With |
| --- | --- |
| `IRegistrationEventPublisher` (flags off → legacy Storage Queue) | production `RegistrationEventPublisher` (`IMessageBus`). Wolverine's default local routing delivers `RegisterEventCommand` to `RegistrationEventHandler` in-process. |
| `ISubscriptionValidationPublisher` (flags off → Storage Queue) | production `SubscriptionValidationPublisher` → local `ValidationEventHandler` |
| `IEventsQueueClient` (Azure Storage Queues) | a fake that fails clearly ("legacy queue not available in localtest"). Reached only from the Functions callback endpoints `inbound`/`outbound` and the `useAzureServiceBus=false` branch, which the claim loop never uses. |
| `IPublicSigningKeyProvider` (Key Vault) | copy of Storage's `LocaltestPublicSigningKeyProvider` (shared test certificate). Outgoing `platform`/`events` tokens are signed by the production generator with the same certificate. |
| Wolverine error handling | optional `IWolverineExtension` that adds the production `Registration/Outbound/ValidationEventHandlerPolicy` with short local delays from configuration. Without it, a failed webhook is tried once and then dropped (Wolverine default, not confirmed). Verify that Wolverine 6.46 picks up `IWolverineExtension` from DI and that scheduled retries work on non-durable local queues. |
| Webhook capture | `LocaltestWebhookCaptureHandler : DelegatingHandler` on `AddHttpClient("WebhookService")` (section 4) |
| `trace_log` partitions | hosted service: `CREATE TABLE IF NOT EXISTS events.trace_log_y{year} PARTITION OF …` for this year and next, using upstream's naming. Without it, from 2027-01-01 (with no newer upstream migration) `CreateWebhookResponseEntry` throws. It is not wrapped in try/catch ([`TraceLogService.cs:122`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/TraceLogService.cs#L122)), so a successful delivery would be reported as failed. |
| Inspection API + SSE | `LocaltestEventsController` (section 4) |

### Configuration

Defaults are inserted below the environment variables, as Storage does, with `{platform}` = `GeneralSettings:PlatformEndpoint`
(default `http://local.altinn.cloud:8000/`):

| Key | Value |
| --- | --- |
| `GeneralSettings:OpenIdWellKnownEndpoint` | `{platform}authentication/api/v1/openid/` |
| `GeneralSettings:BaseUri` | `{platform}` |
| `PlatformSettings:ApiAuthorizationEndpoint` / `RegisterApiBaseAddress` / `ApiEventsEndpoint` | `{platform}authorization/api/v1/` / `{platform}register/api/` / `{platform}events/api/v1/` |
| `PlatformSettings:AppsDomain` | `local.altinn.cloud`. App sources then count as app events, and `GetSourceFilter` cuts `http://local.altinn.cloud:8000/ttd/app/instances/…` to `…/ttd/app` (same segment logic as `ttd.apps.altinn.no`). |
| `PlatformSettings:SubscriptionCachingLifetimeInSeconds`, `MainUnitCachingLifetimeInSeconds` | 5, 30 (production 3600) so new subscriptions and test data take effect at once |
| `EventsProcessingSettings:PrimaryTaskIdleDelaySeconds` | 1 |
| `*QueuePolicy` delays | seconds instead of hours (only if the policy extension is used) |

Forced: `kvSetting:SecretUri=""`, all six `WolverineSettings:Enable*=false`, `ApplicationInsights:ConnectionString=""`.
Environment variables: `ASPNETCORE_ENVIRONMENT=Development` (http metadata), `PostgreSQLSettings__AdminConnectionString` /
`__ConnectionString` (`Host=postgres;…;Database=eventsdb`, `{0}` = `Password` as upstream),
`AccessTokenSettings__AccessTokenSigningCertificateFullPath=/certs/localtest-accesstoken.pfx`.

### Compose, Postgres, ingress

- `components/events/postgres-init/30-events.sh` (mounted into the shared Postgres): create `platform_events_admin` and
  `platform_events` with password `Password`, create `eventsdb` owned by the admin role. The admin role does not need
  superuser because `pg_trgm` is trusted, but this is not confirmed against migration `v0.00`. Yuniql creates the schema
  and grants at startup. An existing `postgres-data` volume skips init scripts, so `docker compose down -v` or a
  first-start fallback is needed. This is already true for every component added after the first start.
- Service `events` (port 5080, `<<: *platform-host`, `/certs` mounted, depends on `postgres` healthy and `register`). Ingress
  `location /events/ { proxy_pass $events; }` without stripping the prefix, because routes start with `events/api/v1/`.

### Subscription delivery locally

Keep production delivery and put the capture handler in front of it:

- **Local endpoints are delivered for real**: hosts that resolve to the stack (`local.altinn.cloud`, compose service
  names). That covers v8 apps (`/{org}/{app}/api/v1/eventsreceiver` through the ingress, including the validation
  handshake) and the production test sink [`events/api/v1/tests/webhookreceiver`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Controllers/WebhookReceiverController.cs) (always 200). Testers can
  subscribe to that sink and see every matching event in the GUI.
- **Every other host is captured, not sent**: the handler returns a synthetic `200` with header
  `X-Localtest-Captured: true`, so the production trace log, retries and validation behave as for a healthy subscriber.
  This meets "no external communication". Make the mode configurable (`Localtest:Events:Delivery = LocalOnly | CaptureAll | All`).
- PDP filtering of consumers stays production behaviour. The standard app policy has an `[ORG]` → `events` read rule
  (for example [`component-library/policy.xml:236-262`](https://github.com/Altinn/altinn-studio/blob/028390958dfe0e7a91479daca89baa5d654f5946/src/test/apps/component-library/App/config/authorization/policy.xml#L236-L262)), so `/org/ttd` subscriptions to
  ttd apps should be permitted (not confirmed against the vendored PDP's multi-request handling). Denials appear as
  `Unauthorized` trace entries.

### Azure Functions

Drop them. Everything they do runs in the API under the configuration above. Their callback endpoints stay compiled but
unused.

### Effort and risk

| Step | Estimate |
| --- | --- |
| Vendor copy, props/targets, Dockerfile, compose, init script, ingress route | 0.5 day |
| Config + DI fakes (publishers, queue stub, signing keys, partitions, Wolverine policies) | 0.5 to 1 day |
| Capture handler, inspection API, SSE, reset | 1 to 1.5 days |
| Verification: smoke (`POST app` with an app token, `GET` back through the inspection API), process-transition-test `workflow-status.ts` with events on, one subscription to the test sink with delivery, idempotent retry | 1 day |

Risks, highest first:

1. **Wolverine without a transport** (medium). The local-routing path is used by the unit-test host and by Storage, but
   not end to end in Events: handler discovery, `SendAsync` routing to local queues, and the policy extension need a
   build. If local routing fails, the fallback is a fake `IRegistrationEventPublisher` that calls
   `IEventsService.Save` directly, and `IMessageBus` stays for outbound. That is still no directive.
2. **Register main-unit lookup on every org-owned event** (low to medium). `PostOutbound` calls
   `v2/internal/parties/main-units` whenever `alternativesubject` is `/org/…`, even with no subscriptions. The vendored
   Register's in-memory persistence appears to implement main units (`InMemoryPartyPersistence`), but this is not
   confirmed. A failure makes the event retry five times and end as `RetryExhausted`, which the GUI shows.
3. **Year partition** (low, dated): mitigated by the hosted service above. Raise it upstream as well.
4. **Upstream churn** (low): Events is mid-migration from Storage Queues/Functions to Service Bus. The flags,
   publishers and handler names are exactly what the fakes depend on. Pin, and re-check them on each vendor pull.
5. **v8 subscriptions** need an app-side Maskinporten token handler. Localtest has no Maskinporten. Out of scope
   unless the product owner wants them.

Vendoring is the right call: the parts we would otherwise fake (idempotency, claim/retry status, subscription
matching, PDP filtering, the trace log) are the parts testers want to see, and they cost 3 small directive sites.

## 4. Inspection seam and API for the GUI

### Where to capture

| What | Seam | Production code touched |
| --- | --- | --- |
| Every published event, with status/retries/idempotency key | `events.events` (written by `RegistrationEventHandler` → `CloudEventRepository.CreateEvent`, [`L24`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Repository/CloudEventRepository.cs#L24)) | none (read-only SQL from the fake) |
| Lifecycle: Registered, DuplicateIdempotencyKeySkipped, OutboundQueue (authorized, queued per subscription), Unauthorized, WebhookPostResponse (code), EndpointValidationSuccess/Failed | `events.trace_log` via [`TraceLogService`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/TraceLogService.cs) | none. A Scrutor `Decorate<ITraceLogService>` (Scrutor is already referenced) only raises in-process SSE notifications. |
| Exact outgoing webhook request/response (body, headers, latency, captured vs delivered) | `DelegatingHandler` on named client `"WebhookService"` ([`Program.cs:262`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Program.cs#L262), used by [`WebhookService.cs:49`](https://github.com/Altinn/altinn-events/blob/41297b521b983f8624a847f286ee70f93f3ad273/src/Events/Services/WebhookService.cs#L49)) | none. Stored by the fake in its own table `events_localtest.delivery`, created by the fake and kept outside the vendored migrations. |
| Subscriptions | `events.subscription` | none |

The production read endpoints (`GET app/{org}/{app}`, `GET events`) are not enough for a "see everything" view. They
require user/scope tokens and filter through the PDP. They stay available for production-faithful tests.

### Proposed contract (`events/api/localtest/`, anonymous like `register/api/localtest/`)

```
GET    events?after={seq}&limit=100&org=&app=&type=&subject=&alternativeSubject=&resource=&instance={partyId}/{guid}&status=registered|processed|retryExhausted&from=&to=
       → { items: [ { sequenceNo, id, time, registeredTime, type, source, subject, alternativeSubject, resource,
                      resourceInstance, org, app, status, retryCount, retryReason, idempotencyKey,
                      deliveries: { total, delivered, captured, failed, unauthorized } } ], next: {seq}|null }
GET    events/{id}            → { event: <stored CloudEvent JSON>, status…, trace: [trace_log rows by time], deliveries: [delivery] }
GET    deliveries?eventId=&subscriptionId=&outcome=delivered|captured|failed&after={id}&limit=
       → delivery = { id, time, eventId, kind: event|validation, subscriptionId, consumer, endpoint, mode: delivered|captured,
                      request: { headers (Authorization redacted), body }, response: { status, body (≤4 KB), durationMs } | error }
GET    subscriptions?consumer=&includeInvalid=true → production Subscription shape + validated flag
GET    stream                  → text/event-stream: event-registered {id}, event-processed {id,status}, delivery {id,eventId,outcome}
DELETE events?subscriptions=false → truncate events.events, events.trace_log, deliveries (+ events.subscription if true);
                                    clears the IMemoryCache (subscription and PDP decision caches)
```

The GUI component is a browser page. It reads these endpoints through the ingress and listens on `stream`, so neither
component polls the other and Events does not depend on the GUI. Notifications and Correspondence can expose the same
shape later (list, detail, stream, reset), with their own capture on the outbound provider client.

## 5. Open questions for the product owner

1. **External webhook endpoints**: capture with a synthetic 200 (proposed), capture with a failure, or allow real
   delivery behind an opt-in?
2. **Subscriptions in scope?** Only v8 apps with eFormidling subscribe, and they need an app-side Maskinporten token that
   localtest cannot issue today. Is the http directive plus test-sink subscriptions (made from the GUI with an org
   token) enough, or should v8 app subscriptions work?
3. **Retries**: production retry delays (up to 12 h, 5 claim retries), short local delays, or no retries, so failures
   show at once?
4. **Reset scope**: global only, or per app (`org`/`app` filter on `DELETE`)? Keep subscriptions on reset?
5. **GUI coupling**: is "components expose list/detail/stream/reset, GUI reads" the agreed model, rather than
   components pushing records into a GUI store?
6. **Generic events** (`events/api/v1/events`): needed now (no app-lib caller), or only when Correspondence is
   vendored? They need resource policies in the PDP's resource store.
7. **Persistence**: should events survive `docker compose down` (named volume, current default) or start empty on
   every `up`?
