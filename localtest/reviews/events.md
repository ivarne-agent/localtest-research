# Events review (2026-10-20)

> **Superseded in part (2026-10-20, task 9b):** finding 10 says only apps publish, so `AppsDomain` = platform host has
> no effect. Dialogporten now publishes to the generic endpoint, and the effect is real: its sources are cut to
> `.../dialogporten/api` for the cached source filter (see `../components/dialogporten/README.md`, "Events", and the
> Events README's known gaps). Task 9b also saw `registeredTime` two hours behind UTC. Both fixed on 2026-10-21 (task
> 12): two directive sites in Events for the sources, the shared Postgres in UTC for the time (Events README,
> "Directive sites" and "Postgres"). Compose and the `localtest_events_1` container names below are from before studioctl
> (2026-10-20).

> **Status (2026-10-20):** all findings handled. Fixed: 1 (the Dockerfile mirrors upstream's: pinned digests, the DbTools build, whose generated SQL equals the committed script, `Migration` from the build stage, non-root `dotnet`), 2 (sync instructions), 3 (no HEALTHCHECK), 4 (default log level Information), 5 (webhooks to `host.docker.internal` are delivered; `localhost` documented), 7 (a failure to record a delivery is logged, not thrown; `id`/`type` read only as strings), 9 (the dead Key Vault override removed, the comment corrected), 11 (the docs). Documented in the README's known gaps: 6, 8, 10, 12. Verified: events smoke 22/22, a receiver on the host got the subscription validation.

## Summary

The vendored tree matches upstream `Altinn/altinn-events@1a4a80c` exactly, apart from `UPSTREAM.md` and three small,
rule-conforming `LOCALTEST` sites. Upstream `main` has no newer commits, so there is no drift. The Azure replacements
are minimal and mostly reuse production classes: the production Wolverine bus publishers on local queues, the
production retry policies, the production Postgres repositories and migrations. The real localtest-only code is the
webhook capture, the change notifications and the inspection API. The main gap is the `Dockerfile`, which does not
follow the project's mirror rule: no pinned digests, no non-root user, no DbTools step, and a dead `HEALTHCHECK`.
`vendor/UPSTREAM.md` also has no sync section. The other findings are low-severity behaviour and documentation issues:
Debug log volume, webhook-capture rules for host endpoints, and reset racing in-memory queues.

## Vendored code

- Pin: `1a4a80c4599e9e176c04bce1b5da7861ca1c54a6` (main, 2026-10-19). Copied paths: `src/Events`, `src/Events.Common`,
  `src/DbTools`, `stylecop.json`, `.editorconfig`, `Dockerfile`.
- Fidelity: `git archive <pin> <those paths>` into an empty scratch folder, then `diff -rq -x bin -x obj` against
  `localtest/components/events/vendor/`. Only these differ:
  - `UPSTREAM.md` (local only)
  - `src/Events/Program.cs`
  - `src/Events/Controllers/SubscriptionController.cs`
- History: the baseline commit `2a3288f` matches the upstream archive (only `UPSTREAM.md` extra).
  `git diff 2a3288f HEAD -- localtest/components/events/vendor` shows 2 files with 11 insertions and 0 deletions.
- The upstream build regenerates `Migration/v0.59/02-functions-and-procedures.sql` with DbTools, and localtest skips
  that step. I ran the DbTools concatenation in Python over the vendored `FunctionsAndProcedures/`. The output is
  byte-identical to the committed file, so the image's migrations are the same today.
- Directive sites (all follow the rule):

| # | Where | Kind | What it does |
| --- | --- | --- | --- |
| 1 | `vendor/src/Events/Program.cs:63-65` | pure `#if LOCALTEST` addition | `LocaltestEventsExtensions.AddLocaltestConfiguration(builder.Configuration)` after `SetConfigurationProviders`: localtest defaults below the environment variables, forces the Wolverine `Enable*` flags off and App Insights/Key Vault empty |
| 2 | `vendor/src/Events/Program.cs:70-72` | pure `#if LOCALTEST` addition | `LocaltestEventsExtensions.AddLocaltestEvents(builder)` after `ConfigureServices`: DI replacements, decorators, capture handler |
| 3 | `vendor/src/Events/Controllers/SubscriptionController.cs:193-198` | `#if !LOCALTEST` production line unchanged / `#else` | `IsValidUri` accepts http as well as https (used for both `EndPoint` and `SourceFilter`) |

## Upstream drift

None. `git fetch` of `.reference/altinn-events`: `origin/main` = `1a4a80c`.
`gh api repos/Altinn/altinn-events/compare/1a4a80c…main` returns `identical, ahead_by=0`.
Upstream `main` head commit date: 2026-10-19T09:12:02Z.

## Dockerfile

`localtest/components/events/Dockerfile` does **not** mirror `vendor/Dockerfile`, which breaks the 2026-10-19 rule in
`planned-components/mvp-vendoring-model.md:41-43`. Storage, register, authentication and workflow-engine follow that
rule.

| Upstream (`vendor/Dockerfile`) | Localtest (`Dockerfile`) |
| --- | --- |
| `sdk:10.0.402-alpine3.24@sha256:230c…` | `sdk:10.0-alpine` (floating, no digest) |
| `aspnet:10.0.13-alpine3.24@sha256:819b…` | `aspnet:10.0-alpine` (floating, no digest; the running image happens to have `DOTNET_VERSION=10.0.13`) |
| Copies `src/Events`, `src/DbTools`, `src/Events.Common` to `/Events` etc. | `COPY . .` of the whole component folder (README, smoke test, postgres-init too) |
| `dotnet build DbTools -o /app_tools` (the PostBuild target then regenerates the migration script) | missing |
| `dotnet build` + `dotnet publish -o /app_output` | `dotnet publish -o /app` only |
| `COPY --from=build /Events/Migration ./Migration` (after DbTools) | `COPY vendor/src/Events/Migration ./Migration` from the context |
| `addgroup -g 3000 dotnet && adduser -u 1000 …`, `USER dotnet`, `mkdir /tmp/logtelemetry` | missing; runs as **root** (`podman inspect`: `Config.User` empty; logs show DataProtection keys in `/root/.aspnet`) |
| no environment | `ASPNETCORE_ENVIRONMENT=Development`, `ASPNETCORE_URLS=http://+:5080` (no `localtest:` marker) |
| no HEALTHCHECK | `HEALTHCHECK … wget …/health`: dead, because podman builds OCI images (`podman image inspect`: `Healthcheck` null) |

No line carries a `localtest:` marker.

## Fakes and what they replace

| File (`localtest/components/events/fakes/`) | Replaces / adds | Production instead? |
| --- | --- | --- |
| `LocaltestEventsExtensions.cs` | Composition: config defaults (platform URLs, `AppsDomain`=platform host, short caches, short retry schedules), forced Azure-off flags. DI: bus publishers instead of Storage Queue publishers (`IRegistrationEventPublisher`, `ISubscriptionValidationPublisher`), production retry policies attached to local queues through `ConfigureWolverine` | Already production classes. The publisher swap is needed because upstream couples the bus publishers to `IsAzureServiceBusEnabled` (`WolverineServiceCollectionExtensions.cs:27-51`). An upstream flag such as "Wolverine local transport" would remove it |
| `LocaltestEventsOptions.cs` | Settings for the fakes (`PlatformEndpoint`, `Delivery`, `ResetOnStart`) | n/a |
| `Queues/LocaltestEventsQueueClient.cs` | `IEventsQueueClient` (Azure Storage Queues), always failing with a clear message. Reached only by the Functions callback endpoints `inbound`/`outbound` and the unused legacy publishers | Only with Azurite, an extra container. The fake is right |
| `Auth/LocaltestPublicSigningKeyProvider.cs` | `IPublicSigningKeyProvider` (Key Vault): returns the shared `certs/localtest-accesstoken.pfx` for every issuer | Same pattern as Storage. Could be shared code across components |
| `Startup/LocaltestAdminDatabase.cs`, `Startup/LocaltestDatabaseStartup.cs`, `Startup/prepare.sql`, `Startup/reset-on-start.sql` | Ops work production does outside the app: `trace_log` year partitions for this year and next, the `localtest.delivery` table, and TRUNCATE on start | Partition creation stands in for ops/migrations. The reset is a localtest feature |
| `Delivery/LocaltestWebhookDeliveryHandler.cs`, `Delivery/LocaltestDeliveryLog.cs` | Adds a `DelegatingHandler` on the production `WebhookService` client: records every POST and captures (synthetic 200) endpoints outside the stack | Localtest feature; production `WebhookService` is unchanged |
| `Changes/LocaltestChanges.cs`, `Changes/NotifyingRepositories.cs` | Pass-through decorators on `ICloudEventRepository`, `IUnitOfWorkRepository`, `ITraceLogRepository`, `ISubscriptionRepository` that raise in-process change notifications | Localtest feature (inspection stream) |
| `Inspection/LocaltestEventsController.cs`, `Inspection/LocaltestEventsStream.cs` | Anonymous `events/api/localtest/` read API over the production tables, SSE stream, reset | Localtest feature |

These pieces are not faked: Service Bus (gone; Wolverine local routing runs the production handlers in-process),
Azure Functions (the API holds every handler), Key Vault (off because nothing sets `kvSetting:SecretUri`),
Postgres (the real shared server, production migrations), and Register/PDP (the vendored components). There are no
in-memory database fakes. That matches the stated preference for production code, though the user prefers in-memory
over extra databases and this component adds a database (`eventsdb`) to the shared server.

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Transport | Azure Service Bus queues, durable, `SendInline`/`ProcessInline`, N listeners per queue | Wolverine local in-memory queues. Messages are not durable: a container stop loses queued outbound deliveries and scheduled retries |
| Retry schedule | Outbound: 30 s … 12 h, 10 steps (upstream appsettings) | Outbound: 2 s … 60 s, 10 steps (~7 min); the other queues 2–10 s |
| Final failure | ASB dead-letter queue | `MoveToErrorQueue` on a non-durable local queue. Where it ends up is not verified |
| Caches | Subscriptions 3600 s (appsettings), main units default | 5 s, 30 s; claim-loop idle delay 1 s (prod 2 s) |
| Subscription endpoint and source filter | https only | http or https |
| Webhook delivery | Every endpoint is POSTed | `LocalOnly`: POSTs only to the platform host, its subdomains and single-label hosts; everything else gets a synthetic 200 and is marked validated/delivered |
| App detection | `AppsDomain=apps.altinn.no` | `AppsDomain=local.altinn.cloud` (the platform host is also the apps host) |
| Data lifetime | Persistent; 90-day cron cleanup on Azure | Emptied on every container start; cron migration is a no-op |
| Environment | Production: Warning logging, `/events/api/v1/error` handler, no Swagger, https OpenID metadata | Development: Debug logging (~14 log lines per second from the idle claim loop), developer exception page with stack traces, Swagger, `RequireHttpsMetadata=false`, AlwaysOn trace sampler |
| Platform access tokens | Keys per issuer from Key Vault | One shared test certificate for every issuer (the issuer-to-org check in `AppController` still applies) |
| Container | Non-root `dotnet` user | root |
| Extra API | none | anonymous `events/api/localtest/*` (read, SSE, `DELETE events`) and a `localtest` schema in `eventsdb` |

## Findings

1. **Dockerfile does not mirror upstream.** `localtest/components/events/Dockerfile:1-20` against
   `vendor/Dockerfile:1-30`. It has floating base tags with no digests, no DbTools step, no non-root user, no
   `/tmp/logtelemetry`, `COPY . .`, and no `localtest:` markers. This breaks the project rule. The image runs as
   root, unlike production. Base image updates are unreproducible. If an upstream commit lands
   `FunctionsAndProcedures/` changes without regenerating the versioned script, localtest silently runs different
   SQL from production.

   Fix: rewrite it on the model of `components/storage/Dockerfile`: the same digests, the DbTools build to
   `/app_tools`, publish to `/app_output`, `Migration` copied from the build stage, `addgroup`/`adduser`/`USER dotnet`
   and `mkdir /tmp/logtelemetry`. Mark the env lines `localtest:` and delete the `HEALTHCHECK`. Check that uid 1000
   can read the mounted `/certs` and `/accesstoken` pfx files.

   Severity: **medium**.
2. **`vendor/UPSTREAM.md` has no "Syncing to a newer upstream commit" section** (storage's has one, including the
   Dockerfile step).

   Fix: add the same five steps, using these paths: `src/Events src/Events.Common src/DbTools stylecop.json
   .editorconfig Dockerfile`. The LOCALTEST files are `Program.cs` and `SubscriptionController.cs`.

   Severity: **low**.
3. **Dead `HEALTHCHECK`.** `Dockerfile:19`. Podman drops it (confirmed: `Healthcheck` null on the image), and compose
   defines no healthcheck for `events`.

   Fix: remove it. If something needs readiness, add a compose `healthcheck:` (the image has busybox `wget`).

   Severity: **low**.
4. **Development log volume.** `vendor/src/Events/appsettings.Development.json` sets `Default: Debug`, and the claim
   loop polls every second (`PrimaryTaskIdleDelaySeconds=1`). The running container wrote 5,612 lines in about 6
   minutes with 6 HTTP requests, almost all `Npgsql.*` debug/info lines for `claim_registered_event()`. Real
   problems drown in `podman logs`.

   Fix: in compose or the localtest defaults, set `Logging__LogLevel__Npgsql=Warning` (and possibly
   `Logging__LogLevel__Default=Information`), or the production levels.

   Severity: **low**.
5. **Delivery rule for endpoints on the developer's machine.**
   `fakes/Delivery/LocaltestWebhookDeliveryHandler.cs:131-151`. Single-label hosts are "delivered", so a subscription
   to `http://localhost:NNNN/…` is POSTed to the events container itself (connection refused, then retries).
   `host.docker.internal` (the documented way to reach host processes, `docker-compose.yml:7`) and IP literals
   contain dots and are silently **captured with 200**. A developer testing their own webhook receiver outside the
   proxy sees "validated/delivered" while nothing arrives.

   Fix: treat `host.docker.internal` (and private/loopback IPs) as local. Document that `localhost` means the
   container. Consider capturing `localhost` with an explanatory error instead of sending.

   Severity: **low/medium**.
6. **`DELETE events/api/localtest/events` does not clear the in-memory Wolverine queues.**
   `fakes/Inspection/LocaltestEventsController.cs:257-274`. Queued `OutboundEventCommand`s and scheduled retries
   (up to ~7 min outbound) survive the TRUNCATE. After a reset, deliveries and `WebhookPostResponse` trace rows can
   appear for events that no longer exist, which confuses tests that reset between cases.

   Fix: document it, or wait for the local queues to drain or stop them (for example through Wolverine's
   `IWolverineRuntime` endpoints) before truncating. The startup reset is not affected.

   Severity: **low**.
7. **Recording failure breaks delivery.** `LocaltestWebhookDeliveryHandler.cs:104,127`. `log.Record` runs in the
   request path. A DB error (for example `localtest.delivery` missing after a manual TRUNCATE/DROP, or a pool timeout)
   throws out of `SendAsync`, after the real POST in the "delivered" case. Production retry logic then re-sends, so
   the subscriber gets duplicates. `ReadEvent` (`:153-173`) catches only `JsonException`, so a non-string `id`/`type`
   throws `InvalidOperationException` and fails the delivery.

   Fix: wrap recording in try/catch with a log warning. Use `ValueKind == String` checks.

   Severity: **low**.
8. **Slack-style deliveries lose their event link.** `ReadEvent` reads `id`/`type` from the body. For
   `hooks.slack.com` endpoints production sends `{ "text": … }`, so `event_id` is null and the event's delivery
   counts stay 0. Moot under `LocalOnly`/`CaptureAll` for the id, but the GUI miscounts.

   Fix: accept and document it. Linking these deliveries properly would need production code to pass the event id
   along (for example a header or a `HttpRequestMessage.Options` entry), which the vendoring rules rule out.

   Severity: **low**.
9. **Forcing `kvSetting:SecretUri` empty is cosmetic.** `fakes/LocaltestEventsExtensions.cs:87-90`. Production checks
   it inside `SetConfigurationProviders` (`Program.cs:113-118`), before directive 1 runs. If anyone sets
   `kvSetting__SecretUri`, Key Vault is still added. The class comment ("turns off everything Azure (Key Vault …)")
   overstates it.

   Fix: correct the comment, or fail fast if the value was set.

   Severity: **low**.
10. **`AppsDomain` = platform host makes every platform-host source filter an "app subscription".**
    `LocaltestEventsExtensions.cs:60` together with `SubscriptionController.cs:214` (`DnsSafeHost.EndsWith(AppsDomain)`).
    Today only apps publish, so this has no effect. Once generic publishers (Correspondence) share
    `local.altinn.cloud`, their source-filtered subscriptions will take the app path.

    Fix: note it in the README for the Correspondence work. Separate app hostnames would need `{org}.apps.*` routing.

    Severity: **low** (future).
11. **Stale or inaccurate docs.**
    - `smoke-test.sh:2` still says "database in memory".
    - The README's inspection table says `GET events` returns `{ items, next }`, but `next` is omitted when null
      (global `WhenWritingNull`; observed `{"items":[]}`).
    - The README's Dockerfile row ("copies `Migration/` next to it as upstream does") hides that upstream copies it
      after DbTools.
    - The README's Configuration section mentions only the OpenID-over-http effect of `Development`; it should list
      the others (Debug logging, developer exception page, Swagger, sampler).
    - `localtest-components/events-vendoring.md` status bullet on Scrutor keyed descriptors applies to the removed
      in-memory version; the current code only decorates.
    - The README and commit message say "smoke-test.sh: 23 ok", but I count 22 checks in the script. Not reconciled.
    - Neither the README nor `smoke-test.sh` says that the smoke test wipes the running stack's events and
      subscriptions (`DELETE …?subscriptions=true` at the start and end) and pushes the `ttd/testapp` policy.

    Fix: update the text.

    Severity: **low**.
12. **`prepare.sql` future conflict.** `fakes/Startup/prepare.sql:7-15` creates yearly partitions ahead of upstream
    (2027 today). Upstream's own `events.create_logs_partition()` (`Migration/FunctionsAndProcedures/createlogspartition.sql`)
    creates **monthly** partitions. If ops or a future migration switches to monthly partitions, they would overlap
    the localtest yearly ones and the migration would fail on start. Today nothing calls that function.

    Fix: none needed now. Re-check on each sync.

    Severity: **low**.

No internet dependencies were found. There is no `altinncdn.no` or other https fetch in the vendored code or the
fakes; OpenID metadata, PDP and Register all go through `local.altinn.cloud` → localtest proxy. The Npgsql
`libgssapi_krb5.so.2` load error at startup is harmless (Alpine without Kerberos; same in the upstream image).

## Verification done

- Read `AGENTS.md`, `localtest/README.md`, `planned-components/mvp-vendoring-model.md`,
  `localtest-components/events-vendoring.md`, the component README, `vendor/UPSTREAM.md`, both Dockerfiles, the
  props/targets, every file in `fakes/`, `postgres-init/30-events.sh`, `smoke-test.sh`, and the compose `events` and
  `postgres` services.
- `git -C .reference/altinn-events fetch -q`: `origin/main` = `1a4a80c`. `gh api …/compare/1a4a80c…main`:
  identical.
- Fidelity: `git archive 1a4a80c src/Events src/Events.Common src/DbTools stylecop.json .editorconfig Dockerfile`
  into an empty scratch folder, then `diff -rq -x bin -x obj` against `vendor/`. Result: `UPSTREAM.md` only in
  vendor, plus the 2 directive files. Ran `diff -u` on those 2 files. Did the same for the baseline commit `2a3288f`.
  `git diff --stat 2a3288f HEAD -- …/vendor`: 11 insertions.
- Re-implemented the DbTools concatenation in Python (`-I`, in the scratchpad). The output is byte-identical to
  `Migration/v0.59/02-functions-and-procedures.sql`.
- `podman ps`, `podman inspect localtest_events_1`, `podman image inspect localhost/localtest-events:dev`: User
  empty (root), Healthcheck null, `ASPNETCORE_ENVIRONMENT=Development`, `DOTNET_VERSION=10.0.13`, image created
  2026-10-19 18:33 UTC.
- `podman logs localtest_events_1`: startup shows the reset message, no fail/crit entries, and 5,612 lines.
- GET on `events/api/localtest/{events,subscriptions,deliveries,tracelog}`: 200 with empty results; `status=bogus` →
  400; `events/health` via proxy → 404 (`/health` is not under the `/events/` route).
- **Not verified:**
  - That the running image was built from HEAD. The image is 15 minutes older than commit `42c7f69`, and I did not
    look inside it.
  - Any build or smoke test (forbidden).
  - Where `MoveToErrorQueue` sends messages on non-durable local queues.
  - Behaviour of `localhost`/`host.docker.internal` subscriptions at runtime (inferred from the code).
  - The reset/in-flight queue race (inferred from the code).
  - The developer exception page output (no failing request was made).
  - The "23 ok" count.
