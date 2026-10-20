# events

Production Altinn Events (`vendor/`, see `vendor/UPSTREAM.md`) on the shared Postgres, without Azure: apps publish to
`events/api/v1/app` exactly as against production, and the production pipeline registers, authorizes and delivers the
events with its own repositories and migrations. Every webhook POST is recorded. Endpoints inside the stack get the
POST; endpoints outside it are answered locally, so nothing leaves the machine. The database is emptied on every start
of the component. `events/api/localtest/` shows everything for the GUI and for tests.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three `LOCALTEST` sites (`git diff` against the vendoring commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, compile `fakes/**/*.cs` into the host and embed `fakes/**/*.sql`. Upstream has no `Directory.Build.props`, so the import of one is guarded |
| `fakes/` | Compiled into the host assembly (the interfaces live there): publishers, signing keys, the startup scripts, change notifications, the webhook capture handler and the inspection API |
| `postgres-init/30-events.sh` | Postgres init script: the `eventsdb` database and the roles upstream's README asks for (`platform_events_admin` superuser, `platform_events`), password `Password`. Idempotent; for an existing volume, where Postgres skips init scripts, run it by hand: `podman exec -i localtest_postgres_1 sh < components/events/postgres-init/30-events.sh` |
| `Dockerfile` | Mirrors the upstream `vendor/Dockerfile` (pinned Alpine images, the DbTools build that regenerates the latest migration's functions-and-procedures script, `Migration/` copied from the build stage, non-root `dotnet`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the running stack: publish, idempotency, token checks, subscriptions with validation, delivery and capture, the production read API, reset |

## Directive sites in `vendor/`

1. `src/Events/Program.cs`, after `SetConfigurationProviders`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestConfiguration(…)`.
   `ConfigureServices` reads the Wolverine flags eagerly, so the localtest values must be in place first.
2. `src/Events/Program.cs`, after `ConfigureServices`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestEvents(builder)`,
   which replaces registrations after the production ones.
3. `src/Events/Controllers/SubscriptionController.cs`, `IsValidUri`: `#if !LOCALTEST` https only, `#else` https or
   http. Apps and the test sink are on `http://local.altinn.cloud:8000`.

## Postgres

The production repositories, functions and Yuniql migrations run unchanged against `eventsdb` (`events` schema:
`events`, `subscription`, `trace_log` with yearly partitions, `event_status`). On every start, after the migrations
(`UseYuniql` in `Program.Configure`) and before any hosted service starts, `Startup/LocaltestDatabaseStartup` runs two
embedded scripts as `platform_events_admin`:

| Script | What |
| --- | --- |
| `fakes/Startup/prepare.sql` | Creates the `trace_log` partition for this year and next (upstream has 2025 and 2026 only; without one, saving a webhook response fails), with upstream's naming, so a later upstream migration is a no-op. Creates `localtest.delivery`, the record of every webhook POST, in its own schema |
| `fakes/Startup/reset-on-start.sql` | `TRUNCATE events.events, events.trace_log, events.subscription, localtest.delivery RESTART IDENTITY`: every start begins empty. Leaves `event_status` (lookup values) and Yuniql's version table alone. Skipped with `Localtest__Events__ResetOnStart=false` |

Because this runs in `IHostedLifecycleService.StartingAsync`, the production claim loop never sees the previous
session's events.

## Fakes (`fakes/`, namespace `Altinn.Platform.Events.Localtest`)

| Replaces / adds | With |
| --- | --- |
| `IRegistrationEventPublisher`, `ISubscriptionValidationPublisher` (with all Service Bus flags off production picks the Storage Queue ones) | The production bus publishers. With no transport, Wolverine's local routing delivers the commands to the production handlers in this process |
| Retry handling (production attaches its policies only to Service Bus listeners) | The production `Registration/Inbound/Outbound/ValidationEventHandlerPolicy` on the local queues through `services.ConfigureWolverine`, with seconds instead of hours (see Configuration) |
| `IEventsQueueClient` (Azure Storage Queues) | `LocaltestEventsQueueClient`: fails clearly. Only the Azure Functions callback endpoints (`inbound`, `outbound`) still use it |
| `IPublicSigningKeyProvider` (Key Vault) | The shared test certificate, as in Storage, so the production `AccessTokenHandler` validates the apps' platform access tokens |
| nothing (production sends every webhook) | `Delivery/LocaltestWebhookDeliveryHandler` on the production named client `WebhookService`: records each POST (request, response or error, duration) in `localtest.delivery` and, by `Localtest:Events:Delivery`, sends it or answers `200` with `X-Localtest-Captured: true` |
| nothing | `Changes/`: decorators around the production repositories that raise an in-process notification after each write (an event created, a claimed event's status committed, a trace row, a subscription change), for the inspection stream. They pass every call through unchanged |

Outgoing platform access tokens (Events calls Register as `platform`/`events`) need no fake. Events never binds the
client's `AccessTokenSettings`, so the production `SigningCredentialsResolver` reads its default path,
`../accesstoken/accesstokencredentials.pfx` from `/app`; compose mounts the shared certificate there.

## Inspection API (`events/api/localtest/`, anonymous)

| Call | Returns |
| --- | --- |
| `GET events?after={sequenceNo}&limit=&org=&app=&type=&subject=&alternativeSubject=&resource=&instance={partyId}/{guid}&status=registered\|processed\|retryExhausted` | `{ items: [summary], next }` (`next` left out on the last page), in registration order, with a count of deliveries per event (total, delivered, captured, failed) |
| `GET events/{id}` | The stored CloudEvent, its status, its trace log and its deliveries |
| `GET deliveries?after=&eventId=&endpoint=&mode=delivered\|captured` | Every webhook POST, event deliveries and subscription validations alike |
| `GET subscriptions?consumer=` | Every subscription, validated or not |
| `GET tracelog?after=&eventId=` | The production trace log: Registered, DuplicateIdempotencyKeySkipped, OutboundQueue (authorized), Unauthorized, WebhookPostResponse, EndpointValidationSuccess/Failed |
| `GET stream` | Server-sent events, one `change` message per change: `{ component: "events", entity: event\|subscription\|trace\|delivery\|all, kind: created\|updated\|deleted\|reset, id, at }`, and a comment every 15 seconds to keep the connection open |
| `DELETE events[?subscriptions=true]` | Truncates events, trace log and deliveries (and subscriptions), and empties the memory cache (subscriptions, main units, PDP decisions) |

The list and detail calls read the production tables with plain SQL. The production read endpoints
(`GET events/api/v1/app/{org}/{app}`, `GET events/api/v1/events`) work as well, with their tokens, PDP filtering and
the 30-second delay.

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
| `Localtest:Events:ResetOnStart` | `true`: empty the database on every start |
| `Localtest:Events:Delivery` | `LocalOnly`: deliver to the platform host, its subdomains, single-label hosts (compose services; `localhost` is the events container itself) and `host.docker.internal`, the developer's machine (a receiver there must listen on more than 127.0.0.1, for example `0.0.0.0`); capture everything else. `CaptureAll` sends nothing; `All` sends everything, as production does |

Forced: `kvSetting:SecretUri` empty, `ApplicationInsights:ConnectionString` empty, all six
`WolverineSettings:Enable*` false (no Service Bus). Compose sets `PostgreSQLSettings__AdminConnectionString` and
`__ConnectionString` (`Host=postgres;…;Database=eventsdb`, `{0}` is replaced by upstream's `Password` settings).
`ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) lets the JWT handler read the OpenID metadata over http.
It also brings the developer exception page and Swagger, and Development's Debug logging, which the image lowers to
Information (`Logging__LogLevel__Default`).

## Verified (2026-10-19)

- `./smoke-test.sh`: 22 ok (2026-10-20, after the Dockerfile mirrored upstream).
- process-transition-test with `RegisterEventsWithEventsComponent` on: `workflow-status.ts` 19 of 19. The run published
  69 app events (created, movedTo per task), all processed, and the workflow engine throttled nothing.
- Restarting the container empties `events`, `trace_log`, `subscription` and `localtest.delivery`; `event_status` keeps
  its three rows.
- A subscription to an endpoint that answers 404 fails validation, is retried on the shortened production schedule
  and ends in Wolverine's error queue; the subscription stays unvalidated (checked with the earlier in-memory
  version; the retry path is the same production code).

## Known gaps

- `DELETE events/api/localtest/events` empties the tables but not Wolverine's in-memory queues: deliveries and retries
  already queued (outbound retries run for up to about 7 minutes) can still arrive, and be recorded, for events that
  are gone. The reset on start is not affected.
- Deliveries to Slack endpoints are recorded without their event: production wraps the event as `{ "text": … }`, so
  the record has no event id and the event's delivery counts leave it out.
- `localhost` in a subscription endpoint is the events container itself; use `host.docker.internal` for a receiver on
  the developer's machine.
- `GeneralSettings:AppsDomain` is the platform host, so every source filter on `local.altinn.cloud` counts as an app
  subscription. Only apps publish today; a second publisher on the same host (Correspondence) would need its own host
  name.
- `fakes/Startup/prepare.sql` creates yearly partitions ahead of upstream's; upstream's `events.create_logs_partition()`
  creates monthly ones and nothing calls it today. Re-check on each sync.

