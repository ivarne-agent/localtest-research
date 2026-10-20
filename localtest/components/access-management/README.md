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

Paths relative to `vendor/src/apps/Altinn.AccessManagement/`.

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
| `IResourceRegistryClient` (the delegation services' Resource Registry client) and `IAltinnResourceRegistry` (the integration client behind the production `ResourceSyncService`) | `LocaltestResourceRegistryClient` and `LocaltestAltinnResourceRegistry` over one loader (`LocaltestAppResources`): one `app_{org}_{app}` resource per application in Storage, mapped as the production Resource Registry maps apps (`MapApplicationToApplicationResource`), with the resource's subjects (role codes, access packages) read from the app policy in the shared policy store; service owners from the apps' org codes (orgnumbers for `ttd`, `digdir`, `brg`, `skd`, `nav`, `ssb`). The refresh runs the production resource sync against it, which fills `dbo.provider`, `dbo.resource` and the role/package-resource links the instance delegation code needs |
| Lease on Azure Blob | config: no blob endpoint, so the production `NoopLeaseService` |

Configuration defaults live in `LocaltestAccessManagementExtensions` (section `Localtest:AccessManagement`): every
platform endpoint behind the proxy, the production connection strings pointed at the shared Postgres, dummy
test-tool token settings to satisfy eager validation, and the sync and outbox hosted services switched off until
their step in the plan.

## Status

Steps 1 to 6 of the plan are done. Access Management boots, migrates and seeds on the shared Postgres, imports parties
and external roles from the vendored Register, applies the seed's own roles, Altinn 2 codes and access packages
(`LocaltestRefreshService`, run at start and on `POST accessmanagement/api/localtest/refresh`, the app runner's hook),
syncs app resources from the local Resource Registry stand-in, takes instance delegations from apps, and serves the
PDP's PIP (`policyinformation/*`) and authorized parties (`enduser/authorizedparties` for app-lib, the internal
`authorizedparties` for the PDP, which needs `FeatureManagement:RightsDelegationApi`, on as in production). The
proxy routes all of `/accessmanagement/` here. The startup log lists the effective feature flags with their providers;
port 5080 is also published on the host for debugging.

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

