# dialogporten

Production Dialogporten (`vendor/`, see `vendor/UPSTREAM.md`): the WebApi, GraphQL and Service hosts on a `dialogporten`
database in the shared Postgres 18, with a Redis, as an **optional component group** of the `localtest-v2` runtime. The group's
roots are the GraphQL and Service hosts and the Dialogporten adapter (`../dialogporten-adapter/`); the WebApi and Redis come
with them as their `dependsOn`. Groups close over `dependsOn` ([`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-manifest)), so
`--with correspondence` starts the WebApi and Redis only, and `--with arbeidsflate` the WebApi, GraphQL host and Redis. It
is off by default (apps never call Dialogporten, and the three hosts and Redis cost about 0.5 GB of memory, the adapter about
0.26 GB more) and started with

```sh
studioctl env up -r localtest-v2 --with dialogporten      # --dev-dialogporten-webapi=Dockerfile.webapi (and -graphql, -service) builds from this folder
```

| Container | Image | Port (in the stack) | Public route | Health |
| --- | --- | --- | --- | --- |
| `redis` | `localtest-redis` (`../../redis/Dockerfile`: `redis:7.2-alpine` by digest, no persistence) | 6379 | none | `tcp` |
| `dialogporten-webapi` | `localtest-dialogporten-webapi` | 8080 | `local.altinn.cloud/dialogporten` (unchanged path: the host serves itself under `UsePathBase("/dialogporten")`) | `/health/readiness` |
| `dialogporten-graphql` | `localtest-dialogporten-graphql` | 8080 | `local.altinn.cloud/dialogporten/graphql[/...]`, forwarded as `/graphql[/...]` | `/health/readiness` |
| `dialogporten-service` | `localtest-dialogporten-service` | 8080 | none (it consumes the in-process bus) | `/health/readiness` |

The WebApi, GraphQL and Service hosts all migrate the database at start (production's `DevelopmentMigratorHostedService`, active in
`Development`), and two of them at once collide on the schema ("column ... already exists"). The runtime therefore waits for the WebApi's health
check (a GET of `/health/readiness` over bash's `/dev/tcp` from inside the stack's network, see
[`../../CONVENTIONS.md`](../../CONVENTIONS.md#the-runtimes)) before it starts the other two.
Without `--with dialogporten` (or `--with everything`) none of the group's five containers (these four and the adapter)
exists unless another group depends on it (above), and the front door answers `/dialogporten/...`
with `503` problem details ("Component not running", `group` `dialogporten`, the `--with dialogporten` command).

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three one-line `LOCALTEST` hooks, one per host's `Program.cs`, and `#if !LOCALTEST` blocks for http URLs, the OTLP exporter options and the issuer caches (below; `vendor/UPSTREAM.md` lists them all) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import upstream's `Directory.Build.props`, define `LOCALTEST`, relax warnings-as-errors for the vendored projects, compile `fakes/Infrastructure` into the Infrastructure project and `fakes/{WebApi,GraphQL,Service}` into the host. One `PackageReference` (Microsoft.Extensions.ServiceDiscovery, task 25) for the three hosts, with `RestoreLockedMode=false`: upstream's `--locked-mode` restore fails with NU1004 on any added reference, so the Dockerfiles restore without it |
| `Dockerfile.webapi`, `.graphql`, `.service` | Mirror upstream's three per-host Dockerfiles (pinned `aspnet:10.0.12` and `sdk:10.0.401`, locked restore of the closure, publish without the apphost, non-root `$APP_UID`); each difference is marked `localtest:` |
| `fakes/` | The hook, the configuration defaults and the Maskinporten fake (below) |
| `postgres-init/50-dialogporten.sh` | Postgres init script: role `dialogporten` (password from `DIALOGPORTEN_PASSWORD` in the Postgres container's environment), database `dialogporten` owned by it, `pg_trgm` and `btree_gin`. Idempotent; for an existing volume run it by hand: `podman exec -i localtest-v2-postgres sh < components/dialogporten/postgres-init/50-dialogporten.sh` |
| `smoke-test.sh` | Against the running stack, see "Verified" |
| `.dockerignore` | Keeps host `bin/` and `obj/` out of the build context (a host build of the vendored projects leaves `project.assets.json` with host paths, which breaks the image's locked restore) |

## Directive sites

One per host, right after `var builder = WebApplication.CreateBuilder(args);` in `Program.cs` (`vendor/UPSTREAM.md` has the lines):
`LocaltestWebApi.Apply(builder)`, `LocaltestGraphQl.Apply(builder)` and `LocaltestService.Apply(builder)`. Each (1) inserts the
configuration defaults below the environment variables and above the JSON files, and (2) installs a service-provider factory that applies the replacements to the
finished service collection, so they win over production's registrations whatever their order. The same three blocks also
register service discovery (task 25; since task 40 `LocaltestServiceDiscovery.AddLocaltestServiceDiscovery` from `fakes/ServiceDiscovery/`,
a copy of `../shared/fakes/` compiled into each host, without pass-through: a name without an entry fails at once, a real host is used as
it is), so factory clients resolve `http://<service>/` from `services__<service>__http__0`; the package is added in `localtest.build.targets`,
which turns the locked restore off, and the Dockerfiles restore without `--locked-mode` ([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)).

One `#if !LOCALTEST` block (added with the Dialogporten adapter, task 10): the Application project's `IsValidHttpsUrl` validators
(`FluentValidationStringExtensions.cs`) accept `http` as well as `https`. The stack is plain http, so the links in the adapter's dialogs
(go-to and delete actions, the read API action, attachments, transmission attachments) are `http://local.altinn.cloud:8000/...`, and
production's rule refused every such dialog with a 400. A service owner may therefore also create dialogs with http links locally, which
production refuses: a difference testers should know about.

One more `#if !LOCALTEST` block in each of the WebApi's and the GraphQL host's `Common/Authentication/TokenIssuerCache.cs`
(task 63, in place of two copies of the class in `fakes/`): the `ConfigurationManager` that reads each JWT scheme's issuer
gets `new HttpDocumentRetriever { RequireHttps = false }`, because the stack's Authentication, Maskinporten and ID-porten
publish their metadata over http.

## Fakes (`fakes/`)

| Replaces / adds | With |
| --- | --- |
| `IMaskinportenService` (the `Altinn.ApiClients.Maskinporten` package hard-codes `maskinporten.no`) | `Infrastructure/Maskinporten/`, a copy of `../shared/fakes/Maskinporten/` with production semantics: an unsigned assertion to the localtest app's Maskinporten token endpoint, then Authentication's `exchange/maskinporten`, with `?test=true` only when `UseAltinnTestOrg` is true, which it is not here, so Dialogporten acts as `digdir` and the exchanged token names org number 991825827. The three Maskinporten-backed clients (events, party names, authorization) are otherwise the production ones. A client id that is not an organisation number is Digdir |
| `JwtBearerOptions.RequireHttpsMetadata` (true) | `ConfigureAll<JwtBearerOptions>` in the hook. It has to be a `Configure`, not a `PostConfigure`: `JwtBearerPostConfigureOptions` builds the metadata manager from the value and post-configure callbacks run in registration order |
| `Localtest:Dialogporten` options and the defaults | `Infrastructure/LocaltestDialogportenOptions.cs`, `LocaltestConfiguration.cs`: see below |
| Service-provider factory | `Infrastructure/LocaltestOverrides.cs` (a copy of the host's Development default, validating scopes and the build, that runs the replacements first) |
| nothing (adds the localtest endpoints below) | `WebApi/Localtest/LocaltestEndpoints.cs`: four FastEndpoints in a `LocaltestGroup` (anonymous, version 1, left out of the OpenAPI documents), found by the WebApi's own assembly scan, so no second hook; `Infrastructure/LocaltestCaches.cs` names the named caches by upstream's own constants (a rename upstream breaks the build) and clears them |

## Localtest endpoints (`dialogporten/api/v1/localtest/`, anonymous, WebApi only)

None of them exists in production, where the Janitor runs the two Resource Registry syncs on a schedule, the caches expire on their
own and only a purge removes a dialog.

| Call | What it does |
| --- | --- |
| `POST sync[?since=]` | Sends the Janitor's two commands through `ISender`, `SyncSubjectMapCommand` (the registry's `resource/updated` feed since the newest synced row, or `since`) then `SyncPolicyCommand`, then clears every named cache (below). Answers `{ subjectResources, subjectResourcesByResource: { urn: count }, resourcePolicyInformation, cachesCleared, durationMs }`, totals after the sync; a validation error of either command is a 400. Startup's own sync stays off (`DisableSubjectResourceSyncOnStartup`): it would run in all three hosts and block their start, and the provisioner calls this endpoint instead |
| `POST caches/clear` | `ClearAsync(allowFailSafe: false)` on all fourteen named FusionCaches (`NameRegistry`, `ResourceRegistry` 20 min, `OrganizationRegistry`, `Authorization` 15 min, `AuthorizedPartiesResult` 15 min, `SubjectResource` 20 min, the feature-metric, party-resource, referenced-resources, referenced-party-resources, minimum-authentication-level, Access Management metadata, service-resource catalogue and authorized-service-resources caches): the entries are removed, not only expired, so no stale value comes back as a fail-safe fallback. Answers `{ cachesCleared }` |
| `DELETE dialogs` | `TRUNCATE "Dialog", partyresource."PartyResource", search."DialogSearchRebuildQueue" CASCADE`: every dialog and, by cascade, every table referencing one (contents, activities, transmissions, actors, seen log, labels, the search index, ...), plus the two tables production maintains by row trigger or queue, which a truncate does not fire. The synced registry data, lookup tables and the MassTransit outbox stay. Then the caches are cleared. Answers `{ dialogsDeleted, cachesCleared }`. For a fresh test run without a restart; otherwise dialogs persist and only `studioctl env reset` wipes them (with Storage) |
| `GET dialogs?party=&resource=&after=&limit=` | Plain reads of the dialog table for the inspection GUI and tests, past authorization and including soft-deleted dialogs, in creation order: `{ items: [{ id, org, serviceResource, party, status, extendedStatus, externalReference, process, title: { lang: text }, createdAt, updatedAt, deleted, deletedAt }], next }` (`next` is the `after` of the next page, left out on the last). `resource` takes `app_ttd_testapp` or the full urn; `limit` 100 by default, at most 1000 |

**The clear reaches the other hosts.** Every named cache has Redis as its distributed tier and backplane. `ClearAsync` records the
clear in Redis (a marker entry, `__fc:t:!`, that makes older distributed entries count as gone) and publishes it on the cache's
backplane channel. Checked on 2026-10-20 with `redis-cli`: `PUBSUB NUMSUB ResourceRegistry.Backplane:v2` is 3 (WebApi, GraphQL and
Service all hold that cache), and a `SUBSCRIBE` on the channel received the clear message when `caches/clear` was called. A host
subscribes to a cache's channel when it first uses the cache (`Authorization.Backplane:v2` had one subscriber, the WebApi); a host
that has not used it holds nothing to clear and reads the marker from Redis when it does. Upstream sets no `CacheKeyPrefix`, so
the fourteen caches share one marker key in Redis (FusionCache warns about this at start); since the endpoint clears all of them
together, nothing is cleared that would not be anyway.

## Provisioning: step 5

The front door (the localtest app's `Provisioning/`, since task 26) provisions each app registered with it
(`../../README.md`, "What the app runner pushes"). With the group
up it adds **step 5, `POST dialogporten/api/v1/localtest/sync`**, after step 4 (Access Management's refresh): the subject sync reads
what step 3b made the registry derive from the pushed policy, and the cache clear also drops the authorized-parties cache, which
then reads the test users Access Management has just refreshed. A freshly pushed app is therefore known to Dialogporten at once
instead of after the 20-minute resource cache (the gap 9a documented).

- **Whether the group runs** is whether `dialogporten-adapter` has a `services__dialogporten-adapter__http__0` address in
  the localtest container (since task 26; no probe and no copy of the group list). Without it the step is skipped.
  (Until task 26 studioctl-server's `AppProvisioner` read it from its route table and logged
  `Dialogporten sync for ttd/stateless-app skipped: the dialogporten group is not running`.)
- **The group can come later, but needs a registration.** The localtest app runs the sync only while it provisions an app
  (`Provisioning/LocaltestProvisioner.cs`); nothing syncs on its own when `env up --with dialogporten` runs after the apps
  were registered (studioctl-server's refresh loop that did, until task 26, is gone). Register the app again (restart
  `studioctl run`, the Aspire `provision` command, or the `PUT`), or call `POST dialogporten/api/v1/localtest/sync`.
- **A failure does not fail the provisioning** (Dialogporten is optional): it is logged as a warning and the registration
  reports `dialogporten` `Failed` (which happens while the hosts are still starting); register again to retry.

## Events

The Service host's outbox consumer publishes every dialog event (`dialogporten.dialog.created.v1`, `.updated`, `.deleted`,
`.seen`, transmission and activity events) to `events/api/v1/events`, the generic-events endpoint, with Dialogporten's
Maskinporten token exchanged by Authentication; the Events component registers it like any other event. The source is
`{platform}dialogporten/api/v1/enduser/dialogs/{id}`, the resource the dialog's service resource, the subject the party. The smoke
test finds the created event in `events/api/localtest/events?resource=...&type=dialogporten.dialog.created.v1` (about 8 seconds
after the create, the time the Service host's outbox and in-process bus take; not compared with production).

How the Events component classifies them (plan question 9):

- **Nothing in the stack subscribes** (`events/api/localtest/subscriptions` is empty), so no event is delivered; the classification
  only matters once a tester adds a subscription.
- **The inspection API** derives `org` and `app` from the resource, so a dialog event for `app_ttd_testapp` is listed with `org: ttd,
  app: testapp` next to the app's own instance events (`?org=ttd&app=testapp` returns both; `type` or `source` tells them apart).
  Events for other resources (`altinn_keyrole_access`) have no org or app. This is the inspection API's choice, not production
  behaviour, and is harmless.
- **Production's own code treats an event as an app event by its resource** (`urn:altinn:resource:app_`), as in production: a dialog
  for an app's resource is authorized as an app event in both. The difference is the source filter the authorization result is
  cached under: `GetSourceFilter` cuts every source on the `AppsDomain` host to its first two path segments. In production
  `platform.altinn.no` is not under `apps.altinn.no`, so the whole dialog URL is the key; here `AppsDomain` is the platform host and
  every dialog event becomes `http://local.altinn.cloud:8000/dialogporten/api`, so one consumer's cached decision for one app's dialog
  events is reused for every other app's for the cache lifetime (5 seconds here). Likewise a subscription whose source filter is
  `.../dialogporten/...` without a resource filter counts as an app subscription for "org" `dialogporten`, app `api`, where
  production would refuse it (generic subscriptions need a resource filter and the subscribe scope).
- **Fixed 2026-10-21 (task 12):** two `#if !LOCALTEST` sites in the Events component, `OutboundService.GetSourceFilter`
  and `SubscriptionController.IsAppSubscription`, treat a source on the platform host as an app source only when its first
  path segment is not a platform component's prefix (`dialogporten`, `correspondence`, ...). Dialogporten's events are
  cached per dialog URL and a Dialogporten source filter is a generic subscription, as in production (report and sites in
  `../events/README.md`, "Directive sites"; checked by `../events/smoke-test.sh`). The paragraph above describes the
  behaviour before the fix.

## GraphQL and CORS

`/dialogporten/graphql[/...]` is forwarded to the GraphQL host's `/graphql[/...]` (the proxy's `rewritePrefix`, as APIM does). The
endpoint requires an authenticated caller of any accepted scheme, so the **org token** reaches it: the schema
(`GET /dialogporten/graphql?sdl`) and introspection (`{ __typename }` is `Queries`) work. Every query field carries the end-user
policy (scope `digdir:dialogporten` and a person), so `{ parties { party } }` with the org token answers `AUTH_NOT_AUTHORIZED`: a real
query needs the code-flow token of task 2, which carries `pid` (`../authentication/README.md`); a GraphQL query with it is not
tested yet. CORS: a preflight from `http://af.local.altinn.cloud:8000` (`OPTIONS`, `POST`,
`authorization,content-type`) is answered 204 with `Access-Control-Allow-Origin` for that origin and the requested method and headers;
the actual POST from it carries the allow-origin header; another origin gets none.

## Configuration

Two settings come from the manifest (studioctl and the Aspire package): `Localtest__Dialogporten__PlatformEndpoint` (the public
origin, `http://local.altinn.cloud:8000/`) and `Infrastructure__DialogDbConnectionString`
(`Host=postgres;Port=5432;Database=dialogporten;Username=dialogporten;Password=…;Include Error Detail=True`, the password
the manifest also gives the Postgres container as `DIALOGPORTEN_PASSWORD`; no default in the fakes, so without it upstream's
"TODO: Add to local secrets" fails the start). Everything else is a default that the real setting overrides
(`Infrastructure__Redis__ConnectionString=...`). With `{platform}` that endpoint:

| Key | Default |
| --- | --- |
| `Infrastructure:Redis:ConnectionString` | `redis:6379` |
| `Infrastructure:Altinn:BaseUri`, `EventsBaseUri`, `SubscriptionKey` | `{platform}` (public: `MetadataLinkProvider` builds client-facing links from it), `http://events/`, `localtest` (the header must not be null). The four clients on `BaseUri` (Resource Registry, Access Management metadata, party names, the PDP with authorized parties) call the services by name: `Infrastructure/ServiceDiscovery/LocaltestPrefixRouting.cs` (a copy of `../shared/fakes/ServiceDiscovery/`, registered by `LocaltestInfrastructure`), outermost on those clients, moves `{platform}<prefix>/…` to `http://<service>/<prefix>/…` before service discovery resolves it (task 40) |
| `Infrastructure:Maskinporten:*` | `Environment` test, `ClientId` `localtest-dialogporten`, upstream's development `Scope` list (events publish and admin, party lookup, authorize, authorized parties), `EncodedJwk` the base64 of `{"kty":"RSA"}` (validated non-empty, never read), `UseAltinnTestOrg` false; `Localtest:Maskinporten:TokenEndpoint` and `ExchangeEndpoint` the fake's defaults (`http://localtest/…`, `http://authentication/…`) |
| `Infrastructure:Warmup:Enabled` | `false` (its optional phases call the Resource Registry and search as a synthetic pid) |
| `Application:Dialogporten:BaseUri` | `{platform}dialogporten` (token issuer, event sources, OpenAPI server) |
| `Application:Dialogporten:Ed25519KeyPairs:{Primary,Secondary}` | the committed test pairs below |
| `WebApi:Authentication:JwtBearerTokenSchemas` / `GraphQl:Authentication:...` | Maskinporten: `{platform}localtest/api/maskinporten/.well-known/oauth-authorization-server`; Altinn: `{platform}authentication/api/v1/openid/.well-known/openid-configuration`; Idporten: `{platform}localtest/api/idporten/.well-known/openid-configuration` (all three are set because upstream's development file carries three https ones and arrays merge by index) |
| `WebApi:OpenApi:*` | try-it-out on, the fake ID-porten's authorize, token and logout URLs, the fake Maskinporten's token URL, client id `localtest` (validated non-empty; only Swagger UI and Scalar use them) |
| `GraphQl:Cors:AllowedOrigins:0` | `http://af.local.altinn.cloud:8000` (Arbeidsflate, task 11; at least one entry is required) |
| `Serilog:MinimumLevel:Default` | `Information` |
| `LocalDevelopment:*` | all fourteen explicit: `UseLocalDevelopmentUser`, `DisableAuth`, `UseLocalDevelopmentAltinnAuthorization`, `UseLocalDevelopmentResourceRegister`, `UseLocalDevelopmentCloudEventBus`, `UseLocalDevelopmentNameRegister`, `UseLocalDevelopmentPartyNameRegistry`, `UseLocalDevelopmentOrganizationRegister` (read nowhere), `UseLocalDevelopmentCompactJwsGenerator`, `UseLocalMetricsAggregationStorage` (Janitor only) false; `DisableCache` true (replaces only the unused default `IFusionCache`, the authorization caches are named and unaffected), `UseInMemoryServiceBusTransport` true (the outbox stays in Postgres, the Service host drains it into its in-process bus), `DisableSubjectResourceSyncOnStartup` and `DisablePolicyInformationSyncOnStartup` true (the sync is `localtest/sync`, which the provisioner calls) |

`ASPNETCORE_ENVIRONMENT=Development` is set by the Dockerfiles: it selects the in-process bus, the migrator at start and the `LocalDevelopment`
section. The Altinn CDN (`altinn-orgs.json`) is read from the internet, as in Authentication and the Resource Registry; Azure App
Configuration and OTLP are off because their variables are unset.

### Dialog-token keys

The two Ed25519 pairs are test-only, like `../../certs/`, committed in `fakes/Infrastructure/LocaltestConfiguration.cs`. Base64url of the raw
32-byte seed and public key, generated with `openssl genpkey -algorithm ed25519`, taking the last 32 bytes of the PKCS#8 and SPKI DER:

| | Kid | Public component (the JWKS `x`) |
| --- | --- | --- |
| Primary | `localtest-primary` | `Xg0SfHY0oNB6M3om3bY6jJ6JFE16gP4JY6mQeL-1FR4` |
| Secondary | `localtest-secondary` | `wzuM7xyqsySfrhbWomQh_cfa3HETBjleagfPZQwLva8` |

The adapter and Correspondence validate dialog tokens against `{platform}dialogporten/api/v1/.well-known/jwks.json`.

## Postgres and data

The database holds dialogs the way Storage's holds instances: **nothing is reset on start**, so dialogs survive restarts. The only reset is
`studioctl env reset -r localtest-v2 --yes`, which deletes the shared Postgres volume and therefore Storage's and Dialogporten's data together (a
dialog must never outlive its instance or the other way round). The WebApi's migrator applies the 70 migrations at first start; `uuidv7()`, which one migration calls, is built into Postgres 18. Deleting a dialog is a soft delete: it stays readable with
`deletedAt`; a purge removes the row.

## Why Redis, and what removing it would take

Upstream uses Redis in all three hosts, and the group keeps it (one container, about 4 MB):

- **GraphQL subscriptions** (`AddRedisSubscriptions` with `ConnectionMultiplexer.Connect`) connect **eagerly at start** in WebApi, GraphQL and Service: the Service host publishes dialog updates, the GraphQL host subscribes, so an in-memory transport would never deliver a live update across the two processes.
- **FusionCache**: the distributed tier (`AddStackExchangeRedisCache`) and the backplane (`AddFusionCacheStackExchangeRedisBackplane`) of every named cache.
- **Health**: a `redis` check (degraded only).

Removing it would cost two directive sites in the shared Infrastructure project (subscriptions to the in-memory transport, the Redis cache and
backplane registrations off) and lose the live inbox updates in Arbeidsflate (task 11). Redis is 7.2 (the last BSD-licensed Redis, nearest to the 6.0
upstream's compose runs), pinned by digest in `../../redis/Dockerfile`, no persistence (`--save "" --appendonly no`), no published port; Valkey would do as well.
Correspondence (database 1) and the Arbeidsflate BFF (database 2) share it; clients reach it by alias and port (`redis:6379`).

## Differences from production

- The message bus is in-process (`UseInMemoryServiceBusTransport`); no Azure Service Bus, App Configuration, Key Vault or Application Insights. OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, see "Telemetry (OTLP)".
- Dialogporten acts as `digdir`: its Maskinporten token is the localtest app's fake, exchanged by production Authentication. `ttd` shares Digdir's
  organisation number in the Resource Registry, so a `ttd` app passes the owner check, but the caller's organisation short name is the first the
  registry lists for that number (`digdir`) while a dialog for a `ttd` app gets the org `ttd`: it is read by id but a service-owner search (which only returns
  the caller's own org) does not find it. A dialog for a Digdir-owned resource (the seeded `altinn_keyrole_access`) is found. The smoke test checks both.
  **Production behaviour, not fixed here** (`ttd` has no organisation number and the registry substitutes Digdir's; TODO.md, task 9): a pain point testers will meet.
- The named caches (the Resource Registry's list for 20 minutes, authorization for 15) are the production ones, but the provisioner's sync clears them
  after every app push and `POST dialogporten/api/v1/localtest/caches/clear` clears them at any time (`../../README.md`, "Where localtest is faster than
  production"). Production waits for them to expire.
- ID-porten, Maskinporten and Authentication are the localtest stack's (http metadata); the Idporten scheme points at the fake ID-porten.
- URLs in dialogs may be `http` as well as `https` (the one `#if !LOCALTEST` site above); production refuses `http`.

## Telemetry (OTLP)

The stack's rule (TODO.md task 53): traces, metrics and logs go over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL` (`grpc`, the .NET default, or `http/protobuf`) and `OTEL_SERVICE_NAME`; without it nothing is exported and no export error is logged. A per-signal endpoint alone does not turn it on. Dialogporten exported over OTLP from these variables already (`AddDialogportenTelemetry` and the Serilog sinks in `Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs`, name from `OTEL_SERVICE_NAME`, else the application name), but set the endpoint in code for traces and metrics, so with `http/protobuf` they went to the bare endpoint without `/v1/traces` and `/v1/metrics`. Directive site `OpenTelemetryExtensions.cs:106-112`, `:134-139`: upstream's `options.Endpoint` and `options.Protocol` under `#if !LOCALTEST`, nothing in the `#else`, so the exporter reads both variables itself and appends the signal path. Unchanged: `OTEL_EXPORTER_OTLP_PROTOCOL` must be `grpc` or `http/protobuf` when the endpoint is set (unset throws at start); the logs go to OTLP instead of the console when the endpoint is set; metrics go to Azure Monitor instead when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (it is not).

`dotnet build -c Release` of `Digdir.Library.Utils.AspNet` with the overrides: 0 errors, no new warnings (2026-10-26). A scratch host on the same exporter version (1.17.0) with `AddOtlpExporter` and nothing set in code posted to `/v1/traces` and `/v1/metrics` of a fake collector for `http/protobuf`. Verified on the stack (2026-10-26, task 53's stack proof, images `sha-609c646`, `studioctl env up -r localtest-v2 --with everything`): `smoke-test.sh` passed with its OTLP check (`dialogporten-webapi`), and the stack's run left 1691 spans, 1325 metric series and 2792 log records under `service.name` `dialogporten-webapi`, 121 spans, 801 metric series and 217 log records under `service.name` `dialogporten-graphql` and 543 spans, 463 metric series and 1126 log records under `service.name` `dialogporten-service`. Without the monitoring group the containers have no `OTEL_*` variable and their logs no exporter line; studioctl sends `grpc`, so the `http/protobuf` path stays proven only by the scratch host.

## Memory

`podman stats --no-stream` (2026-10-20, idle after the smoke test): `dialogporten-webapi` 214 MB, `dialogporten-graphql` 139 MB, `dialogporten-service` 120 MB,
`redis` 3.9 MB, about 477 MB together.

## Verified (2026-10-20, task 10: the http links)

With the `IsValidHttpsUrl` site, images built from this tree (`--dev dialogporten`): `./smoke-test.sh` 60 of 60; the Dialogporten
adapter's smoke test (`../dialogporten-adapter/`) creates, updates and deletes dialogs with `http://local.altinn.cloud:8000/...` links.

## Verified (2026-10-20, task 9b)

- `./smoke-test.sh`: 60 ok, 0 FAIL, twice in a row. It now counts every check: `... | check` ran in a subshell before (`shopt -s lastpipe`
  fixes it), so 9a's "22 ok" undercounted its own checks (they were 30, all passing) and a failing piped check did not fail the script. New: `sync` after the fixture
  push (non-zero subject pairs for `app_ttd_testapp`, policy information, the caches cleared); an app pushed after the resource list was cached is refused
  (422) until `sync`, then created (201) without a restart, and the same with `caches/clear` (both apps removed from Storage again); the
  `dialogporten.dialog.created.v1` event for the dialog in the Events inspection API (source, subject, org/app, no deliveries); GraphQL with the org
  token (schema, `{ __typename }`, an end-user query refused); CORS (preflight, allow headers, another origin, the actual POST); `GET localtest/dialogs`
  (the deleted smoke dialog with its title, paging); `DELETE localtest/dialogs` (count, the party's service-owner search empty, the live dialog 404,
  the list empty).
- Provisioning with a real app (`studioctl run -d` of `src/test/apps/stateless-app`): the studioctl-server log shows `Dialogporten synced the Resource
  Registry for ttd/stateless-app: 17 subject-resource pair(s), 3 resource policies, caches cleared`, and a dialog for `app_ttd_stateless-app` was created
  (201) right after. After `env up` without the group: `Dialogporten sync for ttd/stateless-app skipped: the dialogporten group is not running`. After
  `env up --with dialogporten` with the app running: one warning while the WebApi was starting, then `... for the provisioned apps: ...` 11 seconds later.
- The cache clear on the backplane, see "Localtest endpoints".
- No regression: root `smoke-test.sh` 23 ok, `components/resource-registry/smoke-test.sh` 35 ok, `components/events/smoke-test.sh` 22 ok (0 FAIL each),
  studioctl `go test ./...` and the studioctl-server tests (983 passed, 2 skipped).

## Verified (2026-10-20, task 9a)

- The three images build from a clean cache (about 1 minute each; the NSwag step of the WebApi build runs) with `--locked-mode` restore and the fakes
  compiled in; with `env reset` and `--dev postgres` the baked-in init script creates the database and the 70 migrations ran on Postgres 18.3, healthy in about 30 seconds.
- `./smoke-test.sh`: 22 ok, 0 FAIL, repeatable. Readiness of the three hosts (GraphQL and Service on their host ports); the JWKS with the two committed keys and the issuer; the fixture app pushed
  and its subjects reloaded; an org token (`/Home/GetTestOrgToken`, `digdir:dialogporten.serviceprovider` and `.search`); `POST serviceowner/dialogs` for `app_ttd_testapp` and party
  `01839012345` gives 201 and a UUIDv7; a token without the scope 403, no token 401, a resource owned by `skd` 403 ("Not allowed to reference the following unowned resources"); read by id; search by party and
  external reference (for the Digdir resource, see above); DELETE 204 and the soft-delete marker; `/dialogporten/graphql` answers 401 as the GraphQL host does on `/graphql`, so the rewrite reached it.
- The Service host's outbox consumer published the created event to the Events component with the faked Maskinporten token (`POST events/api/v1/events` 200 after a
  token request and an exchange in the log). Asserted by the smoke test since 9b.
- Dialogs survive `env up` after `env up` (no reset); `env up` without the flag removes the four containers and their routes and the stack is healthy.
- No regression: root `smoke-test.sh` 23 ok, `components/resource-registry/smoke-test.sh` 35 ok (one run in three failed the `grep -q` after a `curl` pipe on its seeded-policy check, a SIGPIPE under `pipefail`
  that predates this component; it passed twice more).

## Known gaps

- **The end-user API is barely tested.** Since task 2 the code-flow token carries `pid` (the cookie token omits it by design); the last check of
  `../authentication/smoke-test.sh` lists Sophie Salt's dialogs with it (`enduser/dialogs`, 200) when this group is up. The rest of `enduser/*` and
  GraphQL queries are untested; this smoke test checks GraphQL with the org token only (schema, introspection, the refusal).
  Superseded 2026-10-21: until task 2 no token the stack issued carried `pid`, so the end-user API was unreachable.
- The `ttd`/`digdir` search mismatch (see "Differences from production"): production behaviour, not fixed here.
- `DELETE localtest/dialogs` leaves the MassTransit outbox: events of dialogs created just before the truncate can still be published, and the Service host's
  search indexer can log a failure for a dialog that is gone. The same outbox makes `components/events/smoke-test.sh` fail
  when it runs right after this smoke test (seen 2026-10-20: its event list had one dialog event more than the two it
  expects): run the Events smoke test first, or wait a few seconds.
- GraphQL subscriptions (server-sent events or WebSockets through the proxy) are untested; they need an end-user dialog token (task 2, task 11).
- The Events inspection API shows `registeredTime` two hours behind the actual UTC time (seen on Dialogporten's events: `time` 19:44:05Z,
  `registeredTime` 17:44:13+00:00); not investigated, it is the Events component's.
- A host that fails its start (a failed migration) stays up at high CPU without listening instead of exiting, as upstream's host does; look at `env logs --component dialogporten-webapi`.
- Npgsql logs "libgssapi_krb5.so.2" at start (Kerberos probe); harmless.
