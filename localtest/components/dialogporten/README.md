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
| `LocalDevelopment:*` | all fourteen explicit: `UseLocalDevelopmentUser`, `DisableAuth`, `UseLocalDevelopmentAltinnAuthorization`, `UseLocalDevelopmentResourceRegister`, `UseLocalDevelopmentCloudEventBus`, `UseLocalDevelopmentNameRegister`, `UseLocalDevelopmentPartyNameRegistry`, `UseLocalDevelopmentOrganizationRegister` (read nowhere), `UseLocalDevelopmentCompactJwsGenerator`, `UseLocalMetricsAggregationStorage` (Janitor only) false; `DisableCache` true (replaces only the unused default `IFusionCache`, the authorization caches are named and unaffected), `UseInMemoryServiceBusTransport` true (the outbox stays in Postgres, the Service host drains it into its in-process bus), `DisableSubjectResourceSyncOnStartup` and `DisablePolicyInformationSyncOnStartup` true (task 9b adds the sync) |

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
- The Resource Registry caches (20 minutes) in Dialogporten's FusionCache: an app pushed after the first dialog request is unknown to Dialogporten for up to
  20 minutes. Task 9b adds the sync and cache-clear endpoints; until then restart `dialogporten-webapi` and flush Redis
  (`podman exec localtest-v2-redis redis-cli flushall`).
- ID-porten, Maskinporten and Authentication are the localtest stack's (http metadata); the Idporten scheme points at the fake ID-porten.

## Memory

`podman stats --no-stream` (2026-10-20, idle after the smoke test): `dialogporten-webapi` 214 MB, `dialogporten-graphql` 139 MB, `dialogporten-service` 120 MB,
`redis` 3.9 MB, about 477 MB together.

## Verified (2026-10-20)

- The three images build from a clean cache (about 1 minute each; the NSwag step of the WebApi build runs) with `--locked-mode` restore and the fakes
  compiled in; with `env reset` and `--dev postgres` the baked-in init script creates the database and the 70 migrations ran on Postgres 18.3, healthy in about 30 seconds.
- `./smoke-test.sh`: 22 ok, 0 FAIL, repeatable. Readiness of the three hosts (GraphQL and Service on their host ports); the JWKS with the two committed keys and the issuer; the fixture app pushed
  and its subjects reloaded; an org token (`/Home/GetTestOrgToken`, `digdir:dialogporten.serviceprovider` and `.search`); `POST serviceowner/dialogs` for `app_ttd_testapp` and party
  `01839012345` gives 201 and a UUIDv7; a token without the scope 403, no token 401, a resource owned by `skd` 403 ("Not allowed to reference the following unowned resources"); read by id; search by party and
  external reference (for the Digdir resource, see above); DELETE 204 and the soft-delete marker; `/dialogporten/graphql` answers 401 as the GraphQL host does on `/graphql`, so the rewrite reached it.
- The Service host's outbox consumer published the created event to the Events component with the faked Maskinporten token (`POST events/api/v1/events` 200 after a
  token request and an exchange in the log). Not asserted by the smoke test (task 9b).
- Dialogs survive `env up` after `env up` (no reset); `env up` without the flag removes the four containers and their routes and the stack is healthy.
- No regression: root `smoke-test.sh` 23 ok, `components/resource-registry/smoke-test.sh` 35 ok (one run in three failed the `grep -q` after a `curl` pipe on its seeded-policy check, a SIGPIPE under `pipefail`
  that predates this component; it passed twice more).

## Known gaps

- **The end-user API is untested: no `pid` token until task 2.** No token the stack issues carries `pid` (the cookie token omits it by design, and the OIDC `token` endpoint needs
  the authorization-code client of task 2), so `enduser/*` and GraphQL queries are untested; the GraphQL test is the 401.
- The sync of subjects and policies, the cache-clear, reset and inspection endpoints, GraphQL with a token and CORS, and what Events does with Dialogporten's event sources are task 9b.
- The group's images are not pinned in `config/images.go` to a build that exists: `localtest-dialogporten-*` appear on ghcr after this branch is on main, and the
  pinned tag is bumped by hand afterwards; until then use `--dev dialogporten` or `STUDIOCTL_IMAGE_LOCALTEST_V2_DIALOGPORTEN_*`.
- A host that fails its start (a failed migration) stays up at high CPU without listening instead of exiting, as upstream's host does; look at `env logs --component dialogporten-webapi`.
- Npgsql logs "libgssapi_krb5.so.2" at start (Kerberos probe); harmless.
