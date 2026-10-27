# resource-registry

Production Altinn Resource Registry (`vendor/`: the `src/apps/Altinn.ResourceRegistry/` vertical of `Altinn/altinn-auth`, in the
`altinn-auth` group with Authentication, Authorization and Access Management; see `vendor/UPSTREAM.md`) on the shared Postgres, without Azure. It is the
platform's one answer to "which resources exist, who may use them and which subjects does each policy name": app
resources (`app_{org}_{app}`) are computed from Storage's application list and the policies the app runner pushes, as
in production; non-app resources come from a built-in seed. Dialogporten, Correspondence, Access Management, the PDP and
Authentication consume the same anonymous endpoints (`resourcelist`, `resource/{id}`, `resource/{id}/policy`,
`resource/{id}/policy/rights`, `resource/updated`, `resource/orgs`, `bysubjects`). The registry is reached at
`resourceregistry/api/v1/`.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `LOCALTEST` sites (`python3 localtest/tools/vendor-sync.py check resource-registry` proves it) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: import the nearest vendored `Directory.Build.props` (`src/apps/Altinn.ResourceRegistry/src/`, which chains to the vertical's own; `Directory.Packages.props` is the vertical's too), define `LOCALTEST`, compile `fakes/**/*.cs` into the host and embed `fakes/Seed/**` |
| `fakes/` | Compiled into the host assembly (the interfaces live in `Core`, the host resolves them): policy repository, applications client, signing keys, OIDC metadata, the seed, `OTEL_SERVICE_NAME` over the host's name (`Otlp/`, a copy of `../shared/fakes/Otlp/`) |
| `postgres-init/40-resource-registry.sh` | Postgres init script: the `resourceregistrydb` database and the roles upstream's migrations grant to (`platform_authorization_admin` superuser, `platform_authorization`), password `localtest`, which Access Management's own database step also sets. Idempotent; for an existing volume, where Postgres skips init scripts, run it by hand: `podman exec -i localtest-v2-postgres sh < components/resource-registry/postgres-init/40-resource-registry.sh` |
| `Dockerfile` | Mirrors the upstream `vendor/src/apps/Altinn.ResourceRegistry/Dockerfile` (pinned Alpine SDK image, `dotnet publish` of the host, `aspnet:10.0-alpine`, non-root `dotnet`); each difference is marked `localtest:` |
| `smoke-test.sh` | Against the running stack: the fixture app pushed as the runner does, resources, policies, the seeded Correspondence and Profile resources, the `updated` feed, orgs, subject lookup, platform access tokens (43 checks) |

## Directive sites in `vendor/`

Both in `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs`, `Create`, pure additions:

1. Right after `AltinnHost.CreateWebApplicationBuilder(...)`: `#if LOCALTEST … LocaltestResourceRegistryExtensions.AddLocaltestConfiguration(builder)`.
   `MapPostgreSqlConfiguration` reads `PostgreSQLSettings` eagerly, so the defaults must be in place first.
2. Right before `return builder.Build();`: `#if LOCALTEST … LocaltestResourceRegistryExtensions.AddLocaltestResourceRegistry(builder)`,
   which replaces registrations after the production ones and adds the seed.

`Program.cs` is untouched.

Service discovery (task 25) needs no site: `AltinnHost.CreateWebApplicationBuilder` already registers
`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(... AddServiceDiscovery())` through `AddAltinnServiceDefaults`
([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)). Since task 40
`AddLocaltestResourceRegistry` removes its pass-through provider (`LocaltestServiceDiscovery.AddLocaltestServiceEndpointProviders`,
`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`; ServiceDefaults has no setting for it): a service name without
an entry fails at once, a real host (altinncdn.no, the public origin) is used as it is. Storage, the Register and Access
Management are called by service name; the issuer and the OpenID metadata stay on the public origin.

## Postgres

The production repositories and the eleven Yuniql migrations (`v0.00` to `v0.10`, `resourceregistry` schema: resources with a version table,
access lists, `resourcesubjects` with soft delete, the change feed) run unchanged against `resourceregistrydb` on the
shared Postgres 18, as `platform_authorization_admin` at start. The database holds only derived or seeded data and is
not reset on start.

## Fakes (`fakes/`, namespace `Altinn.ResourceRegistry.Localtest`)

| Replaces / adds | With |
| --- | --- |
| `IPolicyRepository` (two Azure Blob containers, versions, leases) | `Policies/LocaltestFilePolicyRepository` on the shared `policy-store` volume: app policies at `metadata/{org}/{app}/policy.xml`, exactly where the PDP's policy endpoint writes them, so the app runner's one push feeds both; resource policies at `resourceregistry/{id}/resourcepolicy.xml` (upstream's blob name). Written versions under `{file}.versions/{versionId}`, a lease is a process-wide lock. The Azure return types are built with `Response.FromValue` and a stub `Response` |
| `IApplications` (`ApplicationsClient`, Storage's application list in a 10-minute memory cache) | `Applications/LocaltestApplicationsClient`: the same requests and `a1-`/`a2-` filtering without the cache, so `resourcelist` and `resource/app_{org}_{app}` reflect a push at once. There is no refresh endpoint |
| `IPublicSigningKeyProvider` (Key Vault) | `Infrastructure/LocaltestPublicSigningKeyProvider`: the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, mounted at `/certs`), so the production `AccessTokenHandler` validates platform access tokens for real. The same certificate feeds the production `SigningCredentialsResolver` for the outgoing token to Register (`services.Configure<AccessTokenSettings>`) |
| OIDC metadata over https only | `Infrastructure/LocaltestOidcMetadata`: the localtest Authentication component serves its metadata over http (copy of Access Management's) |
| nothing | `Seed/LocaltestSeedService`: the seed of non-app resources, applied at start through the production service layer (`CreateResource`, `StorePolicy`), see below |
| Configuration | Defaults below the environment variables: `PostgreSQLSettings:*` on the shared container, `PlatformSettings:{StorageApiEndpoint,ApiRegisterEndpoint,ApiAccessManagementEndpoint}`, `OidcProviders:altinn:*` on the Authentication component (public origin). Forced: `kvSetting:SecretUri` empty. The `AzureStorageConfiguration` dummies in `appsettings.json` stay (validated only when the real repository resolves them) |

Not replaced, so production code runs against the real thing: the controllers, `ResourceRegistryService`, the Postgres
repositories, the org list from `https://altinncdn.no/orgs/altinn-orgs.json` (so `resource/orgs` is the real list, and
`ttd`, which has no organization number there, gets Digdir's `991825827` as in production), the scope handlers on the write
endpoints and the Register client for access lists.

## The seed

`fakes/Seed/{id}/resource.json` (the `ServiceResource` JSON) and `policy.xml`, embedded in the host and applied at every
start: a resource that is missing is created, and its policy is stored with `StorePolicy`, which derives the subject
pairs of the `updated` feed. An existing resource keeps its row, and its policy is stored again only if the policy store
has none, so a restart changes nothing. The provisioner does not push platform seed data.

| Resource | Type | Owner | Source |
| --- | --- | --- | --- |
| `altinn_keyrole_access` | `GenericAccessResource` | Digdir, not delegable | the resource the PDP stand-in's `policies/resourceregistry/` folder carried (its `policy.xml` is a copy; the `resource.json` is new, written to match the policy) |
| `apps-correspondence-integrasjon4` | `CorrespondenceService` | `TTD`, 991825827 (as tt02 writes it) | tt02, fetched 2026-10-20 (`createdAt`, `updatedAt`, `versionId` dropped): `signering-brukerstyrt`'s first signing task sends its correspondence to it. **Localtest deviation in the policy:** `ttd` beside `digdir` in the service-owner rule (read, write, subscribe, publish), marked with an XML comment. An app's correspondence token says `urn:altinn:org` `ttd` (`/Home/GetTestOrgToken` exchanges with `test=true`) and lacks `altinn:serviceowner`, so Correspondence asks the PDP, which would deny it with tt02's rule. The other rules (`dagl` read/write/publish, `dagl` and `priv` read/write) are tt02's, including their shape: each action sits in its own `AnyOf`, which XACML reads as all of them at once, so `policy/rights` lists `dagl` and `priv` under `write` only and a single `read` by a DAGL or PRIV matches no rule there (as in tt02) |
| `apps-correspondence-integrasjon2` | `GenericAccessResource` | as above | tt02, with the same `ttd` deviation. Kept a `GenericAccessResource` as tt02 has it: Correspondence refuses it for a new correspondence, in production as well, so `signering-brukerstyrt`'s second task gets the production error rather than a 404 |
| `ttd-localtest-correspondence` | `CorrespondenceService` | `ttd`, 991825827 | written for `../correspondence/smoke-test.sh`, modelled on the tt02 policy: `ttd` reads, writes, subscribes and publishes; `dagl` and `priv` read and write (a recipient's read and confirm, also through a dialog token), each rule's actions as alternatives in one `AnyOf` (unlike the tt02 copies) |
| `altinn-profil-api-varslingsdaresser-for-virksomheter` | `Systemresource` | `digdir`, 991825827 | tt02, fetched 2026-10-21 (`createdAt`, `updatedAt`, `versionId` dropped, the policy's byte order mark removed), unchanged otherwise: production Profile's `organizations/{orgNumber}/notificationaddresses/mandatory` endpoints ask the PDP for `read` or `write` on it. One rule: the CCR role codes (`dagl`, `lede`, `innh`, `best`, `hadm`, ...), the role `hovedadministrator` and the access packages `hovedadministrator` and `starte-drive-endre-avvikle-virksomhet` read, write and delete, with authentication level 3 as an obligation |

## Used by

- The app provisioning (the front door's `Provisioning/` since task 26; studioctl-server's `AppProvisioner` before), as step 3b, after the policy push and before the Access
  Management refresh: `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true`,
  as Designer does at deploy. It reads the pushed policy from the policy store and upserts the app's subject pairs
  (soft-deleting removed ones). Anonymous in production and here.
- Access Management, through its production `ResourceRegistryClient` (delegation services) and `AltinnResourceRegistryClient`
  (the production `ResourceSyncService` behind the refresh: `resource/orgs`, `resource/updated`, `resource/{id}`).
- The PDP, through its production `ResourceRegistryWrapper`: `resource/{id}`, `resource/{id}/policy` and the access list
  endpoints (signed with a platform access token from the shared certificate).
- Dialogporten (the optional `dialogporten` group), through its production clients and the provisioner's sync (see
  `../dialogporten/README.md`), and Correspondence (the optional `correspondence` group), for its resources and their
  policies through the PDP.

## Configuration

Defaults, with `{platform}` = `Localtest:ResourceRegistry:PlatformEndpoint` (default `http://local.altinn.cloud:8000/`):

| Key | Default |
| --- | --- |
| `PostgreSQLSettings:AdminConnectionString`, `ConnectionString` | `Host=postgres;Port=5432;Database=resourceregistrydb;Username=platform_authorization_admin` / `platform_authorization`, `{0}` replaced by the `…Pwd` settings (`localtest`); the manifest sets them explicitly |
| `PlatformSettings:StorageApiEndpoint`, `ApiRegisterEndpoint`, `ApiAccessManagementEndpoint` | `http://storage/storage/api/v1/`, `http://register/register/api/v1/`, `http://access-management/accessmanagement/api/v1/` (service names) |
| `OidcProviders:altinn:Issuer`, `WellKnownConfigEndpoint` | `{platform}authentication/api/v1/openid/` and its `.well-known/openid-configuration` |
| `Localtest:ResourceRegistry:PolicyStorePath` | `/policy-store` |

`ASPNETCORE_ENVIRONMENT=Development` (set by the Dockerfile) lets the JWT handler read OpenID metadata over http. It also
brings the developer exception page and Swagger (`/swagger`).

## Differences from production

- No application cache (see the fakes table); production's `resourcelist` lags a push by up to ten minutes.
- Policies are files on the shared volume, not blobs: no real leases, versions are timestamps, not Azure version ids.
- The signing keys are the shared test certificate for every issuer, not the Key Vault keys per issuer.
- Application Insights and Key Vault are off, as without their settings in production.
- The seed is five resources; production's registry holds thousands.

## Telemetry (task 53)

The stack's rule: traces, metrics and logs over OTLP exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, with
`OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_SERVICE_NAME`; nothing otherwise. Production code already follows the first
half: `AltinnHost.CreateWebApplicationBuilder("resource-registry", ...)` runs `AddAltinnServiceDefaults`, which sets up logs,
metrics (runtime, ASP.NET Core, Kestrel, HttpClient) and traces (ASP.NET Core without health paths, HttpClient,
tail sampling), Npgsql tracing through `AddAltinnPostgresDataSource` and adds the OTLP exporter for each signal when the variable is set (Application Insights only
without it, and only with a connection string, which localtest never has). But its resource detector sets
`service.name` `resource-registry` after the SDK's environment detector, so `OTEL_SERVICE_NAME` was ignored. `AddLocaltestResourceRegistry` (`fakes/LocaltestResourceRegistryExtensions.cs`) calls
`AddLocaltestOtlpResource` (`fakes/Otlp/`, a copy of `../shared/fakes/Otlp/`), which with the variable set adds the
environment detector after it, so `OTEL_SERVICE_NAME` and `OTEL_RESOURCE_ATTRIBUTES` win; without the variable it
does nothing. No vendored file changes. Not `AddLocaltestOtlp`: `UseOtlpExporter()` throws next to ServiceDefaults'
own exporters. Under studioctl the name it sets (the manifest name, `resource-registry`) is the one the host already had, so the
difference shows only with another `OTEL_SERVICE_NAME`.

Verified 2026-10-26: the localtest build (`dotnet build -c Release` with the overrides) and the shared helper's
tests (`../shared/Otlp.Tests`, `-p:OtelVersion=1.15.3`). The host needs Postgres at start, so it was not run outside the
stack; the same call was run in Authentication and Register. Verified on the stack (2026-10-26, task 53's stack proof, images `sha-609c646`, `studioctl env up -r localtest-v2 --with everything`): `smoke-test.sh` passed with its OTLP check, and the stack's run left 660 spans, 680 metric series and 1026 log records under `service.name` `resource-registry`. Without the monitoring group the container has no `OTEL_*` variable and its log no exporter line.

## Verified

2026-10-21 (task 14): `smoke-test.sh` 43 of 43, including the 2 checks of the seeded `altinn-profil-api-varslingsdaresser-for-virksomheter`, whose policy the PDP applied to Profile's organisation address endpoints (read as DAGL at level 3, 403 without a role).

2026-10-21 (task 7, from `../correspondence/README.md`): with the `correspondence` group on, `smoke-test.sh` 41 ok (35 + 6
for the three Correspondence resources: types, owner, the `ttd` deviation in the rights).

2026-10-20 (task 5):

- Yuniql ran all eleven migrations on Postgres 18.3 against an empty volume with the init script baked into the `--dev`
  Postgres image (the volume was reset, `env reset`, so the script ran as the image's init step), and the host
  started healthy on the first start. Running the script by hand against the running database changes nothing.
- `./smoke-test.sh`: 35 ok, 0 FAIL. It covers the fresh-app push reaching `resourcelist` at once, `app_ttd_testapp` as an `AltinnApp`
  of `ttd` with organization `991825827`, 404 for unknown ids, the policy byte-equal to the pushed one (the PDP's
  endpoint drops the fixture's byte order mark), `policy/subjects?reloadFromXacml=true` (dagl, regna, `urn:altinn:org:ttd`),
  the `updated` feed paged one row at a time (14 pairs, every pair once, equal to the unpaged feed), a re-pushed policy
  without regna giving `deleted: true` with a newer `updatedAt` and back again, `resource/orgs`, `bysubjects`,
  `policy/rights` of the seeded resource, a platform access token accepted (and a missing or tampered one refused)
  on `access-lists/get-by-member`, and writes needing a token.
- A restart of the container leaves the seed and the 14 feed rows untouched (`already present`, same `updatedAt`).
- `studioctl run` of `signing-test` (built from the monorepo checkout): the provisioner's step 3b (`GET …/policy/subjects?reloadFromXacml=true`,
  200 in the studioctl-server log) produced the app's pairs (`dagl`, `a0237` to `a0241`, `urn:altinn:org:ttd`).
- No regression: `../../smoke-test.sh` 23 ok, `../authorization/smoke-test.sh` 30 ok, `../events/smoke-test.sh` 22 ok.

## Known gaps

- The registry needs `altinncdn.no` (org list); offline, `resourcelist` and app resource lookups fail. Not verified offline.
- The seed's `altinn_keyrole_access/resource.json` is written to fit the policy, not copied from production.
- Npgsql logs "Error loading shared library libgssapi_krb5.so.2" at start (Kerberos probe on Alpine); harmless.
- Access lists work against the Register but there is no seed or test for them.
- `policy/rules` and `policy/rights` answer 500 for an app whose policy puts matches of two categories in one `AnyOf`
  (`All matches in a all must have the same category ruleId …`, thrown by production's `PolicyHelper.FlattenXacmlRule`,
  which both endpoints use). Confirmed on the stack on 2026-10-21 (task 12) for all 8 of the 13 monorepo test
  apps that have such a rule (the template's `delete` rule), listed in [`../../reviews/test-apps.md`](../../reviews/test-apps.md).
  Production behaviour, not fixed here; the policy XML (`policy`) is what to read instead. `policy/subjects` (the
  provisioner's step 3b) does not use that parser and answers 200 for them.
