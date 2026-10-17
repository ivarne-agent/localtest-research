# access-management: production Altinn Access Management

Production Access Management from `Altinn/altinn-auth` (`src/apps/Altinn.AccessManagement`, pin in
`vendor/UPSTREAM.md`), built from the vendored source with the localtest overrides, on the stack's shared Postgres
and a file-backed policy store. Plan and status: `../../../planned-components/access-management/`.

| Part | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except the `#if LOCALTEST` blocks listed below (`git diff` against the baseline commit shows them all) |
| `fakes/` | Localtest composition and replacements, compiled into the host by `localtest.build.targets` |
| `localtest.build.props` / `.targets` | Define `LOCALTEST` for the vendored projects, relax analyzers, compile `fakes/` into the host |
| `Dockerfile` | Publishes the vendored host with the overrides; listens on 5080 |

## Directive sites in `vendor/`

1. `src/Altinn.AccessManagement/Program.cs`: `#if !LOCALTEST` keeps the production init selection (`RunInitOnly`
   exits after init, `RunIntegrationTests` inits then serves); `#else` ensures the database and roles exist
   (`LocaltestDatabase`) and runs the production `Init()` (migrations, static data, Register import) on every start,
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
platform endpoint behind the ingress, the production connection strings pointed at the shared Postgres, dummy
test-tool token settings to satisfy eager validation, and the sync and outbox hosted services switched off until
their step in the plan.

## Status

Steps 1 to 4 of the plan: boots, migrates, seeds, imports parties and external roles from the vendored Register, and applies the seed's own roles, Altinn 2 codes and access packages (`LocaltestRefreshService`, run at start and on `POST accessmanagement/api/localtest/refresh`, the app runner's hook; the ingress routes that path here). The startup log lists the effective feature flags with their providers. Not yet routed through the ingress (`/accessmanagement/` still goes
to the PDP's fakes); port 5080 is published on the host for verification.
