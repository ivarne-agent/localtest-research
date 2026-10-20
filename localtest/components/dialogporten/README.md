# dialogporten

Production Dialogporten (`vendor/`, see `vendor/UPSTREAM.md`): the WebApi, GraphQL and Service hosts on a `dialogporten`
database in the shared Postgres 18, with a Redis, as an **optional component group** of the `localtest-v2` runtime. It is off
by default (apps never call Dialogporten, and the group costs about 0.5 GB of memory) and started with

```sh
studioctl env up -r localtest-v2 --with dialogporten      # --dev dialogporten builds the three images from this folder
```

| Container | Image | Host port | Public route | Health |
| --- | --- | --- | --- | --- |
| `redis` | `docker.io/library/redis:7.2-alpine` by digest | none | none | `redis-cli ping` (container health check) |
| `dialogporten-webapi` | `localtest-dialogporten-webapi` | 8030 | `local.altinn.cloud/dialogporten` (unchanged path: the host serves itself under `UsePathBase("/dialogporten")`) | `/health/readiness` |
| `dialogporten-graphql` | `localtest-dialogporten-graphql` | 8031 | `local.altinn.cloud/dialogporten/graphql[/...]`, forwarded as `/graphql[/...]` | `/health/readiness` |
| `dialogporten-service` | `localtest-dialogporten-service` | 8032 | none (it consumes the in-process bus) | `/health/readiness` |

The WebApi, GraphQL and Service hosts all migrate the database at start (production's `DevelopmentMigratorHostedService`, active in
`Development`), and two of them at once collide on the schema ("column ... already exists"). The runtime therefore waits for the WebApi's container health
check (a GET of `/health/readiness` over bash's `/dev/tcp`, the aspnet image has no curl) before it starts the other two.
Without `--with dialogporten` none of the four containers, routes or health checks exists, and `/dialogporten/...` is answered by
the catch-all route to the localtest app (404, "No app is registered for this path").

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except three one-line `LOCALTEST` hooks, one per host's `Program.cs` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import upstream's `Directory.Build.props`, define `LOCALTEST`, relax warnings-as-errors for the vendored projects, compile `fakes/Infrastructure` into the Infrastructure project and `fakes/{WebApi,GraphQL,Service}` into the host. No `ProjectReference` or `PackageReference` is added: upstream restores with `--locked-mode` and that fails with NU1004 |
| `Dockerfile.webapi`, `.graphql`, `.service` | Mirror upstream's three per-host Dockerfiles (pinned `aspnet:10.0.12` and `sdk:10.0.401`, locked restore of the closure, publish without the apphost, non-root `$APP_UID`); each difference is marked `localtest:` |
| `fakes/` | The hook, the configuration defaults, the issuer caches and the Maskinporten fake (below) |
| `postgres-init/50-dialogporten.sh` | Postgres init script: role `dialogporten` (password `localtest`), database `dialogporten` owned by it, `pg_trgm` and `btree_gin`. Idempotent; for an existing volume run it by hand: `podman exec -i localtest-v2-postgres sh < components/dialogporten/postgres-init/50-dialogporten.sh` |
| `smoke-test.sh` | Against the running stack, see "Verified" |
| `.dockerignore` | Keeps host `bin/` and `obj/` out of the build context (a host build of the vendored projects leaves `project.assets.json` with host paths, which breaks the image's locked restore) |

## Directive sites

One per host, right after `var builder = WebApplication.CreateBuilder(args);` in `Program.cs` (`vendor/UPSTREAM.md` has the lines):
`LocaltestWebApi.Apply(builder)`, `LocaltestGraphQl.Apply(builder)` and `LocaltestService.Apply(builder)`. Each (1) inserts the
configuration defaults below the environment variables and above the JSON files, and (2) installs a service-provider factory that applies the replacements to the
finished service collection, so they win over production's registrations whatever their order.

## Fakes (`fakes/`)

| Replaces / adds | With |
| --- | --- |
| `IMaskinportenService` (the `Altinn.ApiClients.Maskinporten` package hard-codes `maskinporten.no`) | `Infrastructure/Maskinporten/`, a copy of `../shared/fakes/Maskinporten/` with production semantics: an unsigned assertion to the localtest app's Maskinporten token endpoint, then Authentication's `exchange/maskinporten`, with `?test=true` only when `UseAltinnTestOrg` is true, which it is not here, so Dialogporten acts as `digdir` and the exchanged token names org number 991825827. The three Maskinporten-backed clients (events, party names, authorization) are otherwise the production ones. A client id that is not an organisation number is Digdir |
| `ITokenIssuerCache` of the WebApi and of the GraphQL host (production builds an `OpenIdConnectConfigurationRetriever` that refuses http) | `WebApi/Authentication/LocaltestTokenIssuerCache.cs` and `GraphQL/...`: the production classes with `HttpDocumentRetriever { RequireHttps = false }` |
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

## Provisioning: studioctl-server's step 5

studioctl-server's `AppProvisioner` deploys each app it discovers (`../../README.md`, "What the app runner pushes"). With the group
up it adds **step 5, `POST dialogporten/api/v1/localtest/sync`**, after step 4 (Access Management's refresh): the subject sync reads
what step 3b made the registry derive from the pushed policy, and the cache clear also drops the authorized-parties cache, which
then reads the test users Access Management has just refreshed. A freshly pushed app is therefore known to Dialogporten at once
instead of after the 20-minute resource cache (the gap 9a documented).

- **Whether the group runs** comes from the route table `env up` writes together with the state file's `groups` (the
  bound-topology file studioctl-server already watches for its proxy): the `dialogporten-webapi` route exists exactly when the
  group is on. No probe and no second copy of the group list; the server is not restarted when only the groups change, so the
  state file is not part of its start configuration either. Without the group the log says
  `Dialogporten sync for ttd/stateless-app skipped: the dialogporten group is not running`.
- **The group can come later.** When `env up --with dialogporten` runs while apps are provisioned, the next refresh syncs once for
  all of them (`Dialogporten synced the Resource Registry for the provisioned apps: ...`).
- **A failure does not fail the provisioning** (Dialogporten is optional): it is logged once as a warning and retried on every
  refresh until it answers, which is what happens while the hosts are still starting.

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
- **Proposed fix (not made in this step):** an `#if LOCALTEST` in the Events component's `OutboundService.GetSourceFilter` and
  `SubscriptionController.IsAppSubscription` that treats a source on the platform host as an app source only when its first path
  segment is not a platform prefix (`dialogporten`, later `correspondence`), or, closer to production, serving apps on their own
  host (`{org}.apps.local.altinn.cloud`) and setting `AppsDomain` to it, which is larger (studioctl routes, app-lib URLs). Until
  then a tester subscribing to Dialogporten's events should give a resource filter and no source filter, which behaves as in
  production.

## GraphQL and CORS

`/dialogporten/graphql[/...]` is forwarded to the GraphQL host's `/graphql[/...]` (the proxy's `rewritePrefix`, as APIM does). The
endpoint requires an authenticated caller of any accepted scheme, so the **org token** reaches it: the schema
(`GET /dialogporten/graphql?sdl`) and introspection (`{ __typename }` is `Queries`) work. Every query field carries the end-user
policy (scope `digdir:dialogporten` and a person), so `{ parties { party } }` with the org token answers `AUTH_NOT_AUTHORIZED`: a real
query **waits for the `pid` token of task 2**. CORS: a preflight from `http://af.local.altinn.cloud:8000` (`OPTIONS`, `POST`,
`authorization,content-type`) is answered 204 with `Access-Control-Allow-Origin` for that origin and the requested method and headers;
the actual POST from it carries the allow-origin header; another origin gets none.

## Configuration

One setting is needed from studioctl, `Localtest__Dialogporten__PlatformEndpoint` (the public origin, `http://local.altinn.cloud:8000/`);
everything else is a default that the real setting overrides (`Infrastructure__DialogDbConnectionString=...`). With `{platform}` that endpoint:

| Key | Default |
| --- | --- |
| `Infrastructure:DialogDbConnectionString` | `Host=postgres;Port=5432;Database=dialogporten;Username=dialogporten;Password=localtest;Include Error Detail=True` |
| `Infrastructure:Redis:ConnectionString` | `redis:6379` |
| `Infrastructure:Altinn:BaseUri`, `EventsBaseUri`, `SubscriptionKey` | `{platform}`, `{platform}`, `localtest` (the header must not be null) |
| `Infrastructure:Maskinporten:*` | `Environment` test, `ClientId` `localtest-dialogporten`, upstream's development `Scope` list (events publish and admin, party lookup, authorize, authorized parties), `EncodedJwk` the base64 of `{"kty":"RSA"}` (validated non-empty, never read), `UseAltinnTestOrg` false; `Localtest:Maskinporten:Issuer` and `ExchangeEndpoint` under `{platform}` |
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

The adapter and Correspondence (later tasks) validate dialog tokens against `{platform}dialogporten/api/v1/.well-known/jwks.json`.

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
upstream's compose runs), pinned by digest, no persistence (`--save "" --appendonly no`), no published port; Valkey would do as well.

## Differences from production

- The message bus is in-process (`UseInMemoryServiceBusTransport`); no Azure Service Bus, App Configuration, Key Vault, Application Insights or OTLP.
- Dialogporten acts as `digdir`: its Maskinporten token is the localtest app's fake, exchanged by production Authentication. `ttd` shares Digdir's
  organisation number in the Resource Registry, so a `ttd` app passes the owner check, but the caller's organisation short name is the first the
  registry lists for that number (`digdir`) while a dialog for a `ttd` app gets the org `ttd`: it is read by id but a service-owner search (which only returns
  the caller's own org) does not find it. A dialog for a Digdir-owned resource (the seeded `altinn_keyrole_access`) is found. The smoke test checks both.
  **Production behaviour, not fixed here** (`ttd` has no organisation number and the registry substitutes Digdir's; TODO.md, task 9): a pain point testers will meet.
- The named caches (the Resource Registry's list for 20 minutes, authorization for 15) are the production ones, but the provisioner's sync clears them
  after every app push and `POST dialogporten/api/v1/localtest/caches/clear` clears them at any time (`../../README.md`, "Where localtest is faster than
  production"). Production waits for them to expire.
- ID-porten, Maskinporten and Authentication are the localtest stack's (http metadata); the Idporten scheme points at the fake ID-porten.

## Memory

`podman stats --no-stream` (2026-10-20, idle after the smoke test): `dialogporten-webapi` 214 MB, `dialogporten-graphql` 139 MB, `dialogporten-service` 120 MB,
`redis` 3.9 MB, about 477 MB together.

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

- **The end-user API is untested: no `pid` token until task 2.** No token the stack issues carries `pid` (the cookie token omits it by design, and the OIDC `token` endpoint needs
  the authorization-code client of task 2), so `enduser/*` and GraphQL queries are untested; GraphQL is checked with the org token (schema, introspection, the refusal).
- The `ttd`/`digdir` search mismatch (see "Differences from production"): production behaviour, not fixed here.
- The Events component's `AppsDomain` heuristic files Dialogporten's sources under `.../dialogporten/api` (see "Events"); the fix is proposed, not made.
- `DELETE localtest/dialogs` leaves the MassTransit outbox: events of dialogs created just before the truncate can still be published, and the Service host's
  search indexer can log a failure for a dialog that is gone. The same outbox makes `components/events/smoke-test.sh` fail
  when it runs right after this smoke test (seen 2026-10-20: its event list had one dialog event more than the two it
  expects): run the Events smoke test first, or wait a few seconds.
- GraphQL subscriptions (server-sent events or WebSockets through the proxy) are untested; they need an end-user dialog token (task 2, task 11).
- The Events inspection API shows `registeredTime` two hours behind the actual UTC time (seen on Dialogporten's events: `time` 19:44:05Z,
  `registeredTime` 17:44:13+00:00); not investigated, it is the Events component's.
- The group's images are not pinned in `config/images.go` to a build that exists: `localtest-dialogporten-*` appear on ghcr after this branch is on main, and the
  pinned tag is bumped by hand afterwards; until then use `--dev dialogporten` or `STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_*`.
- A host that fails its start (a failed migration) stays up at high CPU without listening instead of exiting, as upstream's host does; look at `env logs --component dialogporten-webapi`.
- Npgsql logs "libgssapi_krb5.so.2" at start (Kerberos probe); harmless.
