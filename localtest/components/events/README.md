# events

Production Altinn Events (`vendor/`, see `vendor/UPSTREAM.md`) on the shared Postgres, without Azure: apps publish to
`events/api/v1/app` exactly as against production, and the production pipeline registers, authorizes and delivers the
events with its own repositories and migrations. Every webhook POST is recorded. Endpoints inside the stack get the
POST; endpoints outside it are answered locally, so nothing leaves the machine. The database is emptied on every start
of the component. `events/api/localtest/` shows everything for the GUI and for tests.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except five `LOCALTEST` sites (`git diff` against the vendoring commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, compile `fakes/**/*.cs` into the host and embed `fakes/**/*.sql`. Upstream has no `Directory.Build.props`, so the import of one is guarded |
| `fakes/` | Compiled into the host assembly (the interfaces live there): publishers, signing keys, the startup scripts, change notifications, the webhook capture handler and the inspection API |
| `postgres-init/30-events.sh` | Postgres init script: the `eventsdb` database and the roles upstream's README asks for (`platform_events_admin` superuser, `platform_events`), password `Password`. Copied into the stack's Postgres image (`../../postgres/Dockerfile`). Idempotent; for an existing volume, where Postgres skips init scripts, run it by hand: `podman exec -i localtest-v2-postgres sh < components/events/postgres-init/30-events.sh` |
| `Dockerfile` | Mirrors the upstream `vendor/Dockerfile` (pinned Alpine images, the DbTools build that regenerates the latest migration's functions-and-procedures script, `Migration/` copied from the build stage, non-root `dotnet`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the running stack: publish, idempotency, token checks, subscriptions with validation, delivery and capture, `registeredTime` in UTC, the production read API with its 30-second delay, reset, Dialogporten sources of two apps authorized apart |

## Directive sites in `vendor/`

1. `src/Events/Program.cs`, after `SetConfigurationProviders`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestConfiguration(…)`.
   `ConfigureServices` reads the Wolverine flags eagerly, so the localtest values must be in place first.
2. `src/Events/Program.cs`, after `ConfigureServices`: `#if LOCALTEST LocaltestEventsExtensions.AddLocaltestEvents(builder)`,
   which replaces registrations after the production ones.
3. `src/Events/Controllers/SubscriptionController.cs`, `IsValidUri`: `#if !LOCALTEST` https only, `#else` https or
   http. Apps and the test sink are on `http://local.altinn.cloud:8000`.
4. `src/Events/Services/OutboundService.cs`, `GetSourceFilter`, and 5. `src/Events/Controllers/SubscriptionController.cs`,
   `IsAppSubscription` (task 12, 2026-10-21): `#if !LOCALTEST` the production host test, `#else` the same test and
   the source's first path segment not a platform component's prefix (`fakes/Sources/LocaltestAppSources.cs`).
6. `src/Events/Program.cs`: Service discovery (task 25): a pure `#if LOCALTEST` addition right after `WebApplication.CreateBuilder` calls
   `LocaltestServiceDiscovery.AddLocaltestServiceDiscovery` (`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`), so
   factory clients resolve `http://<service>/` from `services__<service>__http__0`; since task 40 without pass-through: a
   name without an entry fails at once, a real host (a dot, an IP, `localhost`) is used as it is ([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)).

**Service names (task 40):** the PDP, the Register and Profile are called as `http://authorization/authorization/api/v1/`,
`http://register/register/api/`, `http://profile/profile/api/v1/` (fakes' defaults); `Localtest__Events__PlatformEndpoint`
stays the public origin (the next links, the CloudEvent `source`, `AppsDomain`, the JwtCookie metadata). Webhooks go
through the same resolver: a subscriber on a real host (a dot, an IP, `localhost`) is sent as it is, one on a service
name to its address, and one on a single-label host that is no running service fails at once: the resolver sits in front of the capture
handler, so Events logs "Send to webhook … failed with error message The endpoint collection contains no endpoints" and
`localtest/deliveries` has no row for it (checked 2026-10-23); a delivery to a service name is recorded with the
resolved address (`http://register:5020/…`).

**Report for sites 4 and 5 (2026-10-21, written before the change).** Production tells an app's source from any other by
its host alone: `GetSourceFilter` cuts a source whose host contains `PlatformSettings:AppsDomain` (`apps.altinn.no`) to
`{scheme}://{host}/{org}/{app}`, the key under which a consumer's authorization decision for app events is cached, and
`IsAppSubscription` lets a subscription whose source filter's host ends with it through as an app subscription, without
a resource filter or the `altinn:events.subscribe` scope. Platform components (`platform.altinn.no`) never match. In
the stack apps and platform components share `local.altinn.cloud`, so `AppsDomain` has to be that host, and since task
9b Dialogporten's (and Correspondence's) sources on it are app sources too: every dialog event is cut to
`.../dialogporten/api`, so one consumer's cached decision for one app's dialog event is reused for every other app's
for 5 seconds, and a `.../dialogporten/...` source filter makes an app subscription for "org" `dialogporten`, app `api`,
where production asks for the subscribe scope and a resource filter. Nothing in `fakes/` can change it: both are private
methods (one of a controller) whose result picks a production code path (the cache key; the app or the generic
subscription service), `AppsDomain` is one string matched with `Contains`/`EndsWith` against a host every component
shares, and replacing `OutboundService` would mean copying it. The other fix, apps on their own host
(`{org}.apps.local.altinn.cloud`), touches studioctl's routes, app-lib's URLs and the Cypress setup. So the sites keep
production's host test and add one condition under `#else`: a source whose first path segment is a platform component's
prefix (the routes the front door serves on the platform host: `dialogporten`, `correspondence`, `storage`, `events`, ...) is
not an app source. Dialogporten's events are then cached per dialog URL and a Dialogporten source filter is a generic
subscription, both as in production; app sources are unchanged.

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

**Time zone (2026-10-21, task 12).** Upstream's column default `registeredtime timestamptz default (now() at time zone
'utc')` (`Migration/v0.21/01-setup-table.sql`) turns the UTC wall clock into a `timestamp` and back into a
`timestamptz` with the session time zone, so it is right only on a server in UTC, as production's is. The stack's
Postgres ran with `TZ=Europe/Oslo` (studioctl, and initdb copies `TZ` into `postgresql.conf`), so every event was stored
two hours early in summer time (one in winter; seen in task 9b: `time` 19:44:05Z, `registeredTime` 17:44:13+00:00) and
the read API's 30-second delay never held anything back. studioctl now runs the shared Postgres with `TZ=UTC` and
`-c timezone=UTC -c log_timezone=UTC` (the `postgres` component in
`localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json`), which also covers a data volume created under the old setting. The same default is in Profile
(`created_date_time`) and Dialogporten (`current_timestamp at time zone 'utc'`), which this fixes as well; no init
script or fake in the stack relies on Oslo time in the database. The smoke test checks `registeredTime` against the
clock and that the read API holds back an event published after one older than 30 seconds.

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
`../accesstoken/accesstokencredentials.pfx` from `/app`; both runtimes mount the shared certificate there too, until this
image links that path to `/certs` ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-certificate)).

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
(`GET events/api/v1/app/{org}/{app}`, `GET events/api/v1/events`) work as well, with their tokens and PDP filtering.
Their 30-second delay (`registeredtime <= now() - interval '30 second'`) holds back the newest events as in production,
since the stack's Postgres runs in UTC (see Postgres; before 2026-10-21 it held back nothing). As upstream's
`getappevents_v2` is written, the limit is the newest event at least 30 seconds old, and while no event is that old
(just after a reset or a start) there is no limit at all and every event is returned.

## Configuration

Defaults, inserted just below the environment variables, with `{platform}` = `Localtest:Events:PlatformEndpoint`
(default `http://local.altinn.cloud:8000/`):

| Key | Default |
| --- | --- |
| `GeneralSettings:OpenIdWellKnownEndpoint`, `GeneralSettings:BaseUri` | `{platform}authentication/api/v1/openid/`, `{platform}` |
| `PlatformSettings:ApiAuthorizationEndpoint`, `RegisterApiBaseAddress`, `ApiEventsEndpoint`, `ApiProfileEndpoint` | `{platform}authorization/api/v1/`, `{platform}register/api/`, `{platform}events/api/v1/`, `{platform}profile/api/v1/` |
| `PlatformSettings:AppsDomain` | the platform host. App sources (`http://local.altinn.cloud:8000/ttd/app/instances/…`) count as app events and are cut to `…/ttd/app` for source filters, as for `ttd.apps.altinn.no`; sources under a platform component's prefix (`…/dialogporten/…`, `…/correspondence/…`) do not (directive sites 4 and 5) |
| `PlatformSettings:SubscriptionCachingLifetimeInSeconds`, `MainUnitCachingLifetimeInSeconds` | 5, 30 (production 3600) |
| `EventsProcessingSettings:PrimaryTaskIdleDelaySeconds` | 1 (production 2) |
| `WolverineSettings:*QueuePolicy` | Cooldowns under 2 s; scheduled retries of 2 s to 60 s (outbound: ten retries over about seven minutes instead of 30 s to 12 h) |
| `Localtest:Events:ResetOnStart` | `true`: empty the database on every start |
| `Localtest:Events:Delivery` | `LocalOnly`: deliver to the platform host, its subdomains, single-label hosts (service names with an address; `localhost` is the events container itself) and `host.docker.internal`, the developer's machine (a receiver there must listen on more than 127.0.0.1, for example `0.0.0.0`); capture everything else. `CaptureAll` sends nothing; `All` sends everything, as production does |

Forced: `kvSetting:SecretUri` empty, `ApplicationInsights:ConnectionString` empty, all six
`WolverineSettings:Enable*` false (no Service Bus). The manifest sets `PostgreSQLSettings__AdminConnectionString` and
`__ConnectionString` (`Host=postgres;…;Database=eventsdb`, `{0}` is replaced by upstream's `Password` settings).
`ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) lets the JWT handler read the OpenID metadata over http.
It also brings the developer exception page and Swagger, and Development's Debug logging, which the image lowers to
Information (`Logging__LogLevel__Default`).

## Telemetry (OTLP)

The stack's rule (TODO.md task 53): traces, metrics and logs go over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc`, the .NET default, or `http/protobuf`) and `OTEL_SERVICE_NAME`; without it nothing is exported and no export error is logged. A per-signal endpoint alone does not turn it on. Directive site `src/Events/Program.cs:170-176`: `AddAzureMonitorTelemetryExporters` under `#if !LOCALTEST`, `#else` `LocaltestOtlp.AddLocaltestOtlp` (`fakes/Otlp/`, a copy of `../shared/fakes/Otlp/`) on the host's own pipeline (its sources, ASP.NET Core, HttpClient, Npgsql, `RequestFilterProcessor`, its meter, the logs); `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1 in `localtest.build.targets`. The service name is `platform-events` unless `OTEL_SERVICE_NAME` is set. The fakes no longer force `ApplicationInsights:ConnectionString` empty: nothing reads it under `LOCALTEST`.

`dotnet build -c Release` of the host with the overrides: 0 errors, no new warnings (2026-10-26). The host needs Postgres to start, so it was not run. Not yet verified in the image: rebuild it (one image build at a time), bump the tag with `../../tools/bump-tag.sh`, and add the smoke check (with the variable pointing at a throwaway collector, a request creates a span with the right `service.name`; without it the container log shows no export errors).

## Verified (2026-10-19)

- 2026-10-21 (task 12, pin `sha-735e5d2` with `--dev events`, `--with everything`, Postgres in UTC): `./smoke-test.sh`
  27 of 27, again 27 of 27 on the default stack. Negative control on the pinned Events image, which predates directive
  sites 4 and 5: 25 of 27, the two Dialogporten-source checks fail as they should (the other app's dialog event was
  delivered on the first one's cached Permit; the `.../dialogporten/api` source filter took the app path). The
  `registeredTime` check passes on both, since it depends on Postgres only.
- `./smoke-test.sh`: 22 ok (2026-10-20, after the Dockerfile mirrored upstream); again 22 of 22 on 2026-10-21 with the
  dialogporten group up (task 10, run before the Dialogporten and adapter smoke tests,
  `../dialogporten-adapter/README.md`).
- Since task 9b (2026-10-20) Dialogporten's Service host publishes its dialog events to `events/api/v1/events` and they
  are registered like any other event (`../dialogporten/smoke-test.sh` finds `dialogporten.dialog.created.v1` through the
  inspection API). Correspondence (task 7) publishes `app.correspondence.*` events too; that Events stores them is not
  verified (`../correspondence/README.md`).
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
  are gone. Nor does it stop other components' outboxes: Dialogporten's events for a dialog created just before can
  still be registered after the reset. The reset on start is not affected.
- **Order of smoke tests.** `./smoke-test.sh` resets and then expects exactly its own events, so run it before
  `../dialogporten/smoke-test.sh` and `../dialogporten-adapter/smoke-test.sh`, or a few seconds after them: right after
  them a late Dialogporten event makes its "stored once" check fail (seen 2026-10-20 and 2026-10-21).
- Deliveries to Slack endpoints are recorded without their event: production wraps the event as `{ "text": … }`, so
  the record has no event id and the event's delivery counts leave it out.
- `localhost` in a subscription endpoint is the events container itself; use `host.docker.internal` for a receiver on
  the developer's machine.
- `fakes/Startup/prepare.sql` creates yearly partitions ahead of upstream's; upstream's `events.create_logs_partition()`
  creates monthly ones and nothing calls it today. Re-check on each sync.

