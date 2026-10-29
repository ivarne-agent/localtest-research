# access-management: production Altinn Access Management

Production Access Management from `Altinn/altinn-auth` (`src/apps/Altinn.AccessManagement`, pin in
`vendor/UPSTREAM.md`), built from the vendored source with the localtest overrides, on the stack's shared Postgres
and a file-backed policy store. Plan and status: `../../../planned-components/access-management/`.

| Part | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except the `#if LOCALTEST` blocks listed below (`git diff` against the baseline commit shows them all) |
| `fakes/` | Localtest composition and replacements, compiled into the host by `localtest.build.targets` |
| `localtest.build.props` / `.targets` | Define `LOCALTEST` for the vendored projects, relax analyzers, compile `fakes/` into the host |
| `postgres-init/45-access-management.sh` | Postgres init script (copied by `../../postgres/Dockerfile`): the roles `platform_authorization_admin` and `platform_authorization` as superusers, `authorizationdb` owned by the admin, `hstore`; passwords from the Postgres container's environment. Idempotent |
| `Dockerfile` | Mirrors the release image: the bake files `vendor/containers/Dockerfile.aspnet-builder`, `-runtime` and `vendor/src/apps/Altinn.AccessManagement/containers/Dockerfile.api` in order (chiseled, non-root `dotnet`, Kerberos libraries); each difference is marked `localtest:`. Listens on 5080 |

## Directive sites in `vendor/`

Service discovery (task 25) needs no site: `AltinnHost.CreateWebApplicationBuilder` already registers
`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(... AddServiceDiscovery())` through `AddAltinnServiceDefaults`
([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)). Since task 40
`AddLocaltestAccessManagement` removes its pass-through provider (`LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders`,
`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`; ServiceDefaults has no setting for it): a service name without
an entry fails at once, a real host is used as it is. Authentication, the PDP, Profile, the Register, the Resource
Registry and Notifications are called by service name; the issuer, the OpenID metadata and `GeneralSettings:Hostname`
stay on the public origin (`Localtest__AccessManagement__PlatformEndpoint`).

Paths relative to `vendor/src/apps/Altinn.AccessManagement/` (line numbers checked 2026-10-21: `Program.cs:19-35`,
`AccessManagementHost.cs:70-73` and `:168-170`; on 2026-10-23 `src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs:25-30` and `:42-51`).

1. `src/Altinn.AccessManagement/Program.cs`: `#if !LOCALTEST` keeps the production init selection (`RunInitOnly`
   exits after init, `RunIntegrationTests` inits then serves); `#else` runs the production `Init()` (migrations, static data, Register import) on every start,
   then the localtest refresh (`LocaltestRefreshService.RunAtStartup`: resource sync and the seed's roles and packages),
   then serves.
2. `src/Altinn.AccessManagement/AccessManagementHost.cs`, right after the host builder is created:
   `#if LOCALTEST LocaltestAccessManagementExtensions.AddLocaltestConfiguration(builder) #endif` (connection strings,
   the OIDC provider and the platform endpoints are read eagerly further down, so the defaults must exist first).
3. Same file, right before `builder.Build()`:
   `#if LOCALTEST LocaltestAccessManagementExtensions.AddLocaltestAccessManagement(builder) #endif`.
4. `src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs` (task 40): the field `_httpClient = new()`
   and the constructor are each `#if !LOCALTEST` / `#else`; the localtest constructor takes `IHttpClientFactory` and
   creates the client from it, so its calls (`v1/resource/…`, `search`, `resourcelist`, `bysubjects`) go through the
   service-discovery handler to `http://resource-registry/`. Production's `new HttpClient()` never sees the resolver.
The token generator in `../../libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/TokenGenerator.cs`
has no site since task 66 (task 63's two blocks, which signed locally, are gone): the host has no Key Vault, so the lib
picks its production `TestTool` generator, which fetches every platform access token for the Register, the Resource
Registry and Notifications from the front door's mock of the online test tool (below). Only the Access Management host
compiles the lib.

## What the fakes replace

| Production | Localtest |
| --- | --- |
| Postgres roles and database created by operations | `postgres-init/45-access-management.sh` in the stack's Postgres image: `platform_authorization_admin` and `platform_authorization` (superusers locally, shared with the Resource Registry) and `authorizationdb` with `hstore`, the passwords from the Postgres container's environment (`PLATFORM_AUTHORIZATION_ADMIN_PASSWORD`, `PLATFORM_AUTHORIZATION_PASSWORD`); the service gets the same values from the manifest (`PostgreSQLSettings__AuthorizationDbAdminPwd`, `__AuthorizationDbPwd`). Postgres runs it on an empty volume only; for an existing volume without `authorizationdb`, run it by hand: `podman exec -i localtest-v2-postgres sh < components/access-management/postgres-init/45-access-management.sh` |
| `IPolicyFactory` over three Azure Blob containers | `LocaltestPolicyFactory` + `LocaltestFilePolicyRepository`: `{PolicyStorePath}/{container}/{path}` with every written version under `{path}.versions/{versionId}`; version ids are what Access Management stores as the delegation policy version |
| `ITokenGenerator` (test tool or Key Vault) | the lib's production `TestTool` generator against the front door's mock of the test tool (`Platform__Token__TestTool__*` in the manifest, below), which signs with the shared test certificate |
| `IPublicSigningKeyProvider` (Key Vault) | the shared test certificate for every issuer, so the production `AccessTokenHandler` validates platform access tokens for real |
| Lease on Azure Blob | config: no blob endpoint, so the production `NoopLeaseService` |

Configuration defaults live in `LocaltestAccessManagementExtensions` (section `Localtest:AccessManagement`): every
platform endpoint by service name, the production connection strings pointed at the shared Postgres, and the periodic
Register and Resource Registry syncs and the outbox hosted services switched off (the init-time Register import stays
on; the refresh runs the resource sync).

The test-tool token settings come from the manifest's `envs` since task 66 (before, the fake set dummy values in code):
`Platform__Token__TestTool__Endpoint` `http://localtest:8080` (the front door by service name), `__Username` and
`__Password` `localtest` (the mock accepts any basic auth; inert values, not secrets), and `Environment` `localtest`:
the host copies the top-level `Environment` (at22 in `appsettings.json`) into `Platform:Token:TestTool:Environment`
(`AccessManagementHost.ConfigureLibsIntegrations`), so a `Platform__Token__TestTool__Environment` would be overwritten.
The lib calls `{Endpoint}/api/GetPlatformAccessToken` with an absolute path, so it uses the front door's temporary root
route (`../../localtest/README.md`, "Test tool"). `__PlatformAccessTokenUrl`
`http://localtest:8080/mockporten/api/GetPlatformAccessToken`, the canonical route, is set too: the lib at the pin
ignores it; the upstream change that adds the setting is prepared (TODO.md task 66), and once it is synced the root
route and the `Endpoint`'s root use go. Access Management depends on the front door (`dependsOn: localtest`), so the
mock answers before its start-up Register import.

The Resource Registry is the real component (`../resource-registry/`), reached through the production clients: the
delegation services' `ResourceRegistryClient` at `PlatformSettings:ApiResourceRegistryEndpoint`
(`http://resource-registry/resourceregistry/api/`, the client appends `v1/resource/...`) and the integration library's
`AltinnResourceRegistryClient` at `Platform:ResourceRegistry:Endpoint` (`http://resource-registry/`, the client appends
`resourceregistry/api/v1/...`). The periodic `ResourceRegistrySync` hosted service stays off; the refresh runs the
production `ResourceSyncService` instead (`SyncResourceOwners`, then `SyncResources`). `SyncResources` reads the
`resource/updated` feed from the `since` in its lease. The localtest lease is the production `NoopLease`, which hands
back an empty lease every time, so each refresh reads the whole feed from the epoch (`since` is not kept between
runs, and nothing needs resetting). The sync upserts, so a refresh after an app push picks up the new subject pairs the
provisioner stored in the registry (step 3b, before the refresh) and re-applying the old ones changes nothing.
Subject pairs the registry has soft-deleted come back as `deleted` updates and remove the role and package links.
Provider rows come from the registry's `resource/orgs`, the real service-owner list from altinncdn.no. The
registry's non-app seed resource (`altinn_keyrole_access`) is synced too.

Provider ids are the production ones (MD5 of the org code). Only a Postgres volume created before the stand-ins were
removed (task 5b, 2026-10-20) is affected: its database holds `ttd` with the stand-in's id, and the first sync then
fails on the unique provider name (the container exits at start). `studioctl env reset -r localtest-v2 --yes` (which
deletes the whole shared Postgres volume) fixes it, or drop just this database once with the stack's Postgres up and
Access Management stopped:
`podman exec localtest-v2-postgres psql -U platform_authorization_admin -d postgres -c "drop database authorizationdb with (force)"`,
then recreate it with the init script (`podman exec -i localtest-v2-postgres sh < components/access-management/postgres-init/45-access-management.sh`)
and start Access Management; each app runner's refresh fills the apps in again.

## Telemetry (task 53)

The stack's rule: traces, metrics and logs over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with
`OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_SERVICE_NAME`; nothing otherwise. Production code already follows the first
half: `AltinnHost.CreateWebApplicationBuilder("access-management", ...)` runs `AddAltinnServiceDefaults`, which sets up logs,
metrics (runtime, ASP.NET Core, Kestrel, HttpClient) and traces (ASP.NET Core without health paths, HttpClient,
tail sampling), Npgsql, the lease, pipeline and core sources and meters and adds the OTLP exporter for each signal when the variable is set (Application Insights only
without it, and only with a connection string, which localtest never has). Its resource detector sets
`service.name` `access-management` after the SDK's environment detector, so `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` are
ignored and `service.instance.id` is the container's host name. Both runtimes set `OTEL_SERVICE_NAME` to that same
name (the manifest name), so localtest adds nothing and no vendored file changes. Until task 61 (2026-10-28) the fakes'
hook called `AddLocaltestOtlpResource` (a copy of `../shared/fakes/Otlp/`) to let the variables win; it changed no
name under studioctl or Aspire, so it was removed ([`TODO.md`](../../../TODO.md) section 61). `AddLocaltestOtlp` was never an option:
`UseOtlpExporter()` throws next to ServiceDefaults' own exporters.

Verified without the call (2026-10-28, task 61, a local image, `env up -r localtest-v2 --with monitoring`): no `smoke-test.sh`; the request by hand;
under `service.name` `access-management` and `service.instance.id` the container's host name, 1253 spans (the `authorizedparties` server span with five Npgsql client spans), 427 metric series, 2634 log records in VictoriaTraces,
VictoriaMetrics and VictoriaLogs (evidence `/home/agent/code/.handover/otlp-resource-workaround-removal/`).

Verified 2026-10-26, with the call, the localtest build (`dotnet build -c Release` with the overrides) and the shared helper's
tests (`../shared/Otlp.Tests`, `-p:OtelVersion=1.15.3`). The host needs Postgres at start, so it was not run outside the
stack; the same call was run in Authentication and Register. Verified on the stack (2026-10-26, task 53's stack proof, images `sha-609c646`, `studioctl env up -r localtest-v2 --with everything`): a request with a `traceparent` (`GET accessmanagement/api/v1/authorizedparties`) showed up as its server span with five Npgsql client spans, and the stack's run left 1384 spans, 622 metric series and 3318 log records under `service.name` `access-management` (it has no `smoke-test.sh`). Without the monitoring group the container has no `OTEL_*` variable and its log no exporter line.
Production's own `UseAzureMonitor` block (`AccessManagementHost.cs:88-108`) runs only outside Development with
`ApplicationInsights:InstrumentationKey`, so never here.

## Status

Steps 1 to 6 of the plan are done. Access Management boots, migrates and seeds on the shared Postgres, imports parties
and external roles from the vendored Register, applies the seed's own roles, Altinn 2 codes and access packages
(`LocaltestRefreshService`, run at start and on `POST accessmanagement/api/localtest/refresh`, the app runner's hook),
syncs app resources from the Resource Registry component, takes instance delegations from apps, and serves the
PDP's PIP (`policyinformation/*`) and authorized parties (`enduser/authorizedparties` for app-lib, the internal
`authorizedparties` for the PDP, which needs `FeatureManagement:RightsDelegationApi`, on as in production). The
proxy routes all of `/accessmanagement/` here. The startup log lists the effective feature flags with their providers.
The container listens on 5080 (no host port; reached as `access-management:5080` inside the stack, see
[`../../CONVENTIONS.md`](../../CONVENTIONS.md#port-and-health)), and the runtime starts it
after Postgres, the Register, the Resource Registry and the profile component (its startup refresh reads the Register
and the Resource Registry; its production Profile client reads users through `profile/api/v1/internal/user`, served by production Profile
since task 14).

There is no smoke test in this folder: `../authorization/smoke-test.sh` exercises Access Management through the PDP
(roles, access packages, authorized parties; 30 checks, all ok on 2026-10-21 with production Profile).

User tokens are validated by production's JWT cookie handler. For an issuer listed under `OidcProviders` it fetches
the metadata with an HTTPS-only retriever regardless of environment; `LocaltestOidcMetadata` registers that issuer's
metadata manager up front with a retriever that allows the authentication component's plain-http endpoint.

## Known gaps

- **Altinn 2 bridge.** `ContextRetrievalService` asks the SBL bridge for Altinn 2 roles in the older rights-delegation
  APIs. There is no bridge locally and no stub, so those calls get the proxy's 404. Apps' instance delegations (signing)
  do not use it.
- **Database roles.** Both production roles (`platform_authorization_admin`, `platform_authorization`) are superusers,
  because in localtest mode the EF migrations run on the app connection. A grant an upstream migration forgets would
  therefore pass locally.

