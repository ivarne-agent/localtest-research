# access-management: production Altinn Access Management

Production Access Management from `Altinn/altinn-auth` (`src/apps/Altinn.AccessManagement`, pin in
`vendor/UPSTREAM.md`), built from the vendored source with the localtest overrides, on the stack's shared Postgres
and a file-backed policy store. Plan and status: `../../../planned-components/access-management/`.

| Part | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except the `#if LOCALTEST` blocks listed below (`git diff` against the baseline commit shows them all) |
| `fakes/` | Localtest composition and replacements, compiled into the host by `localtest.build.targets` |
| `localtest.build.props` / `.targets` | Define `LOCALTEST` for the vendored projects, relax analyzers, compile `fakes/` into the host |
| `Dockerfile` | Mirrors the release image: the bake files `vendor/containers/Dockerfile.aspnet-builder`, `-runtime` and `vendor/src/apps/Altinn.AccessManagement/containers/Dockerfile.api` in order (chiseled, non-root `dotnet`, Kerberos libraries); each difference is marked `localtest:`. Listens on 5080 |

## Directive sites in `vendor/`

Paths relative to `vendor/src/apps/Altinn.AccessManagement/` (line numbers checked 2026-10-21: `Program.cs:19-35`,
`AccessManagementHost.cs:70-73` and `:168-170`).

1. `src/Altinn.AccessManagement/Program.cs`: `#if !LOCALTEST` keeps the production init selection (`RunInitOnly`
   exits after init, `RunIntegrationTests` inits then serves); `#else` ensures the database and roles exist
   (`LocaltestDatabase`) and runs the production `Init()` (migrations, static data, Register import) on every start,
   then the localtest refresh (`LocaltestRefreshService.RunAtStartup`: resource sync and the seed's roles and packages),
   then serves.
2. `src/Altinn.AccessManagement/AccessManagementHost.cs`, right after the host builder is created:
   `#if LOCALTEST LocaltestAccessManagementExtensions.AddLocaltestConfiguration(builder) #endif` (connection strings,
   the OIDC provider and the platform endpoints are read eagerly further down, so the defaults must exist first).
3. Same file, right before `builder.Build()`:
   `#if LOCALTEST LocaltestAccessManagementExtensions.AddLocaltestAccessManagement(builder) #endif`.

## What the fakes replace

| Production | Localtest |
| --- | --- |
| Postgres roles and database created by operations | `LocaltestDatabase`: creates `platform_authorization_admin` and `platform_authorization` (superusers locally) and `authorizationdb` with `hstore` on the shared container when missing; no volume reset needed |
| `IPolicyFactory` over three Azure Blob containers | `LocaltestPolicyFactory` + `LocaltestFilePolicyRepository`: `{PolicyStorePath}/{container}/{path}` with every written version under `{path}.versions/{versionId}`; version ids are what Access Management stores as the delegation policy version |
| `ITokenGenerator` (test tool or Key Vault) | `LocaltestTokenGenerator`: the production `IAccessTokenGenerator` signing with the shared test certificate (`../../certs`) |
| `IPublicSigningKeyProvider` (Key Vault) | the shared test certificate for every issuer, so the production `AccessTokenHandler` validates platform access tokens for real |
| Lease on Azure Blob | config: no blob endpoint, so the production `NoopLeaseService` |

Configuration defaults live in `LocaltestAccessManagementExtensions` (section `Localtest:AccessManagement`): every
platform endpoint behind the proxy, the production connection strings pointed at the shared Postgres, dummy
test-tool token settings to satisfy eager validation, and the periodic Register and Resource Registry syncs and the
outbox hosted services switched off (the init-time Register import stays on; the refresh runs the resource sync).

The Resource Registry is the real component (`../resource-registry/`), reached through the production clients: the
delegation services' `ResourceRegistryClient` at `PlatformSettings:ApiResourceRegistryEndpoint`
(`{platform}resourceregistry/api/`, the client appends `v1/resource/...`) and the integration library's
`AltinnResourceRegistryClient` at `Platform:ResourceRegistry:Endpoint` (`{platform}`, the client appends
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
`podman exec localtest-v2-postgres psql -U platform_authorization_admin -d postgres -c "drop database authorizationdb with (force)"`.
Start recreates it, and each app runner's refresh fills the apps in again.

## Status

Steps 1 to 6 of the plan are done. Access Management boots, migrates and seeds on the shared Postgres, imports parties
and external roles from the vendored Register, applies the seed's own roles, Altinn 2 codes and access packages
(`LocaltestRefreshService`, run at start and on `POST accessmanagement/api/localtest/refresh`, the app runner's hook),
syncs app resources from the Resource Registry component, takes instance delegations from apps, and serves the
PDP's PIP (`policyinformation/*`) and authorized parties (`enduser/authorizedparties` for app-lib, the internal
`authorizedparties` for the PDP, which needs `FeatureManagement:RightsDelegationApi`, on as in production). The
proxy routes all of `/accessmanagement/` here. The startup log lists the effective feature flags with their providers.
The container listens on 5080; studioctl publishes it on the host as `127.0.0.1:8023` for debugging, and starts it
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

