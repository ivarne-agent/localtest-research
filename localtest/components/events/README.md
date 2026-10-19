# events

Production Altinn Events (`vendor/`, see `vendor/UPSTREAM.md`) with its database in memory and no Azure: apps publish
to `events/api/v1/app` exactly as against production, the production pipeline registers, authorizes and delivers the
events, and every webhook POST is recorded. Endpoints inside the stack get the POST; endpoints outside it are answered
locally, so nothing leaves the machine. `events/api/localtest/` shows everything for the GUI and for tests.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three `LOCALTEST` sites (`git diff` against the vendoring commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, compile `fakes/` into the host. Upstream has no `Directory.Build.props`, so the import of one is guarded |
| `fakes/` | Compiled into the host assembly (the interfaces live there): in-memory repositories, publishers, signing keys, the webhook capture handler and the inspection API |
| `Dockerfile` | Publishes the vendored host with the overrides. No `Migration/` folder: there is no database |
| `smoke-test.sh` | Against the running stack: publish, idempotency, token checks, subscriptions with validation, delivery and capture, the production read API, reset |

## Directive sites in `vendor/`

1. `src/Events/Program.cs`, after `SetConfigurationProviders`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestConfiguration(…)`.
   `ConfigureServices` reads the Wolverine flags and the Postgres switch eagerly, so the localtest values must be in
   place first.
2. `src/Events/Program.cs`, after `ConfigureServices`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestEvents(builder)`,
   which replaces registrations after the production ones.
3. `src/Events/Controllers/SubscriptionController.cs`, `IsValidUri`: `#if !LOCALTEST` https only, `#else` https or
   http. Apps and the test sink are on `http://local.altinn.cloud:8000`.

## Fakes (`fakes/`, namespace `Altinn.Platform.Events.Localtest`)

| Replaces | With |
| --- | --- |
| Postgres (`eventsdb`: `events`, `subscription`, `trace_log`) | `Persistence/LocaltestEventsStore`, one in-process store, and four repositories on it that follow the production SQL in `Migration/v0.59/02-functions-and-procedures.sql`: the insert with its (id, source) and idempotency-key uniqueness, `getappevents_v2` and `getevents_v2` (`LIKE`/`ILIKE` filters, `after` cursor, the 30-second read delay), `claim_registered_event` (skip rows another unit of work holds), the processed and retry updates (`MaxRetryCount` ends in RetryExhausted), and the subscription functions. The production `SubscriptionRepositoryCachingDecorator` is put back around the in-memory subscriptions |
| `IUnitOfWorkRepository` (an Npgsql transaction) | `InMemoryUnitOfWorkRepository`: holds the claimed row, buffers writes until commit, honours the `event_claimed` savepoint the claim loop rolls back to on failure. `UnitOfWork.Transaction` stays null |
| `IRegistrationEventPublisher`, `ISubscriptionValidationPublisher` (with all Service Bus flags off production picks the Storage Queue ones) | The production bus publishers. With no transport, Wolverine's local routing delivers the commands to the production handlers in this process |
| Retry handling (production attaches its policies only to Service Bus listeners) | The production `Registration/Inbound/Outbound/ValidationEventHandlerPolicy` on the local queues through `services.ConfigureWolverine`, with seconds instead of hours (see Configuration) |
| `IEventsQueueClient` (Azure Storage Queues) | `LocaltestEventsQueueClient`: fails clearly. Only the Azure Functions callback endpoints (`inbound`, `outbound`) still use it |
| `IPublicSigningKeyProvider` (Key Vault) | The shared test certificate, as in Storage, so the production `AccessTokenHandler` validates the apps' platform access tokens |
| nothing (production sends every webhook) | `Delivery/LocaltestWebhookDeliveryHandler` on the production named client `WebhookService`: records each POST (request, response or error, duration) and, by `Localtest:Events:Delivery`, sends it or answers `200` with `X-Localtest-Captured: true` |

Outgoing platform access tokens (Events calls Register as `platform`/`events`) need no fake. Events never binds the
client's `AccessTokenSettings`, so the production `SigningCredentialsResolver` reads its default path,
`../accesstoken/accesstokencredentials.pfx` from `/app`; compose mounts the shared certificate there.

Everything is lost when the container stops. There are no year partitions to run out of (see the `trace_log` risk in
`../../../localtest-components/events-vendoring.md`).

## Inspection API (`events/api/localtest/`, anonymous)

| Call | Returns |
| --- | --- |
| `GET events?after={sequenceNo}&limit=&org=&app=&type=&subject=&alternativeSubject=&resource=&instance={partyId}/{guid}&status=registered\|processed\|retryExhausted` | `{ items: [summary], next }`, in registration order, with a count of deliveries per event (total, delivered, captured, failed) |
| `GET events/{id}` | The stored CloudEvent, its status, its trace log and its deliveries |
| `GET deliveries?after=&eventId=&endpoint=&mode=delivered\|captured` | Every webhook POST, event deliveries and subscription validations alike |
| `GET subscriptions?consumer=` | Every subscription, validated or not |
| `GET tracelog?after=&eventId=` | The production trace log: Registered, DuplicateIdempotencyKeySkipped, OutboundQueue (authorized), Unauthorized, WebhookPostResponse, EndpointValidationSuccess/Failed |
| `GET stream` | Server-sent events, one `change` message per change: `{ component: "events", entity: event\|subscription\|trace\|delivery\|all, kind: created\|updated\|deleted\|reset, id, at }`, and a comment every 15 seconds to keep the connection open |
| `DELETE events[?subscriptions=true]` | Forgets events, trace log and deliveries (and subscriptions), and empties the memory cache (subscriptions, main units, PDP decisions) |

The production read endpoints (`GET events/api/v1/app/{org}/{app}`, `GET events/api/v1/events`) work as well, with
their tokens, PDP filtering and the 30-second delay.

## Configuration

Defaults, inserted just below the environment variables, with `{platform}` = `Localtest:Events:PlatformEndpoint`
(default `http://local.altinn.cloud:8000/`):

| Key | Default |
| --- | --- |
| `GeneralSettings:OpenIdWellKnownEndpoint`, `GeneralSettings:BaseUri` | `{platform}authentication/api/v1/openid/`, `{platform}` |
| `PlatformSettings:ApiAuthorizationEndpoint`, `RegisterApiBaseAddress`, `ApiEventsEndpoint`, `ApiProfileEndpoint` | `{platform}authorization/api/v1/`, `{platform}register/api/`, `{platform}events/api/v1/`, `{platform}profile/api/v1/` |
| `PlatformSettings:AppsDomain` | the platform host. App sources (`http://local.altinn.cloud:8000/ttd/app/instances/…`) count as app events and are cut to `…/ttd/app` for source filters, as for `ttd.apps.altinn.no` |
| `PlatformSettings:SubscriptionCachingLifetimeInSeconds`, `MainUnitCachingLifetimeInSeconds` | 5, 30 (production 3600) |
| `EventsProcessingSettings:PrimaryTaskIdleDelaySeconds` | 1 (production 2) |
| `WolverineSettings:*QueuePolicy` | Cooldowns under 2 s; scheduled retries of 2 s to 60 s (outbound: ten retries over about seven minutes instead of 30 s to 12 h) |
| `Localtest:Events:Delivery` | `LocalOnly`: deliver to the platform host, its subdomains and single-label hosts (compose services, `localhost`), capture everything else. `CaptureAll` sends nothing; `All` sends everything, as production does |

Forced: `kvSetting:SecretUri` empty, `ApplicationInsights:ConnectionString` empty,
`PostgreSQLSettings:EnableDBConnection=false` (no data source, no Npgsql health check, no Yuniql), all six
`WolverineSettings:Enable*` false (no Service Bus). `ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) lets
the JWT handler read the OpenID metadata over http.

## Verified (2026-10-19)

- `./smoke-test.sh`: 23 ok.
- process-transition-test with `RegisterEventsWithEventsComponent` on: `workflow-status.ts` 19 of 19. The run published
  66 app events (created, movedTo per task), all processed, and the workflow engine throttled nothing.
- A subscription to an endpoint that answers 404 fails validation, is retried on the shortened production schedule
  and ends in Wolverine's error queue; the subscription stays unvalidated.
