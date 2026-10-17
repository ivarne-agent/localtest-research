# authorization

Production Altinn Authorization, the PDP (`vendor/`, see `UPSTREAM.md`), answering `authorization/api/v1/decision`,
`parties` and `roles` for apps. Azure Blob policies, the Access Management, Profile, Resource Registry, OED and
SBL Bridge PIPs, the audit queue and Key Vault are replaced by fakes; Register and Storage stay production HTTP calls
to the other localtest components.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (PDP host, `src/libs/Altinn.Authorization.Api.Contracts`, `src/pkgs` ABAC and PEP) except one `#if LOCALTEST` block |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST` for the vendored projects, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): policy store, PIPs over the register seed, token handling, no-op audit queue, a localtest decision helper, DI composition |
| `policies/` | Built-in policies, copied into the image at `/policies`: `ttd/testapp/policy.xml` (a Studio app policy with `[ORG]`/`[APP]` placeholders) and `resourceregistry/altinn_keyrole_access/policy.xml` (used by signing) |
| `smoke-test.sh` | Starts register, authentication and the PDP from Release builds and checks decisions (see below) |
| `Dockerfile` | Publishes the vendored host with the overrides |

## Directive site

One, a pure addition in `vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`, right after
`ConfigureServices(builder.Services, builder.Configuration)`:

```csharp
#if LOCALTEST
Altinn.Platform.Authorization.Localtest.LocaltestAuthorizationExtensions.AddLocaltestAuthorization(builder);
#endif
```

Placed after the production registrations so `RemoveAll` + add replaces them (they use plain `AddSingleton`, so
registering earlier would lose). The configuration it adds is still seen, because the production services bind
`GeneralSettings`, `PlatformSettings` and feature flags lazily through `IOptions`. The three eager reads inside
`ConfigureServices` are covered: the JWT cookie metadata address is re-applied with a `Configure<JwtCookieOptions>`
(runs before the scheme's post-configure builds the metadata manager), the OED Maskinporten client is never called
(OED is faked), and the access token generator chosen by environment is replaced. Key Vault and Application Insights
are skipped by production code because `kvSetting` is empty.

## Fakes

| Production | Localtest (`fakes/`) |
| --- | --- |
| `IPolicyRepository` (Azure Blob `metadata`, `delegationpolicies`) | `LocaltestPolicyRepository`: an app policy `{org}/{app}/policy.xml` from the mounted folder (`Localtest:Authorization:PoliciesPath`), else one posted to `POST authorization/api/v1/policies` since start, else fetched from the running app at `{AppsBaseUrl}{org}/{app}/api/v1/meta/authorizationpolicy` (today's localtest behaviour). `[ORG]`/`[APP]` placeholders replaced. Reused for `PolicyCacheSeconds` (5). Delegation policies: none. |
| `IPolicyRetrievalPoint` | Production `PolicyRetrievalPoint` wrapped by `LocaltestPolicyRetrievalPoint`, which drops its minutes-long cache entry for the app policy before each read, so a policy edit applies within about 5 s |
| `IAccessManagementWrapper` (AM `policyinformation/roles-and-accesspackages`, `getdelegationchanges`, `authorizedparties`) | `LocaltestAccessManagementWrapper` over the register seed (`{register}/api/localtest/test-users`, `organizations`): roles as `urn:altinn:rolecode:<code>` and `urn:altinn:external-role:ccr:<identifier>`, access packages as `urn:altinn:accesspackage:<id>`; `PRIV` / `urn:altinn:role:privatperson` for the person's own party; a sub-unit gets the roles held for its main unit. No delegations. Authorized parties: self, every party with a role, their sub-units |
| `IProfile` (Profile `internal/user`) | `LocaltestProfile`: user id or ssn to the user's party (uuid, ssn) from the register seed |
| `IRoles` (SBL Bridge, legacy `roles` endpoint) | `LocaltestRoles`: role codes from the register seed |
| `IResourceRegistry` | `LocaltestResourceRegistry`: `resourceregistry/{id}/policy.xml` and optional `resource.json` in the policies folder; no access lists |
| `IOedRoleAssignmentWrapper` | `NoOedRoleAssignments` (empty) |
| `IEventsQueueClient` (audit queue) | `NoopEventsQueueClient`; `FeatureManagement:AuditLog` forced off |
| `IAccessTokenGenerator` (Key Vault certificate) | `LocaltestAccessTokenGenerator`: a token signed with a key made at start; the register only checks the header is present |
| `AccessTokenHandler`, `IPublicSigningKeyProvider` | `LocaltestAccessTokenHandler` (a `PlatformAccessToken` header is enough), `LocaltestPublicSigningKeyProvider` (no keys) |
| (none) | `GET authorization/api/localtest/decision?userId&partyId&org&app&action[&instanceId&task&authenticationLevel]`, anonymous: builds the request an app's PEP sends and posts it to the production `decision` endpoint |

Kept production: `ContextHandler`, `DelegationContextHandler`, `DecisionController`, `PartiesController`, the ABAC
engine, `RegisterService` (Register v1 `parties/partylist`, `partylistbyuuid`, `lookup`, served by the register
component) and `InstanceMetadataRepository` (Storage `instances/{id}/process/authinfo`).

## Configuration

`Localtest:Authorization:PlatformEndpoint` (env `Localtest__Authorization__PlatformEndpoint`, default
`http://local.altinn.cloud:8000/`) is the one setting to give. The composition derives from it, as a source above the
vendored `appsettings.json` (which carries at22/localhost values) and below the environment, so a single endpoint can
still be pointed elsewhere when running without the ingress:

- `PlatformSettings:Api{Register,Storage,ResourceRegistry,AccessManagement,Profile}Endpoint` = `{platform}<service>/api/v1/`
- `GeneralSettings:OpenIdWellKnownEndpoint` = `{platform}authentication/api/v1/openid/`, `RuntimeCookieName` = `AltinnStudioRuntime`
- `Localtest:Authorization:RegisterLocaltestEndpoint` = `{platform}register/api/localtest/`, `AppsBaseUrl` = `{platform}`
- cache timeouts 1 minute (the smallest valid value); feature flags as in production
  (`UserAccessPackageAuthorization`, `SystemUserAccessPackageAuthorization`, `AccessManagementAuthorizedParties`,
  `DecisionRequestLogRequestOnError` on)

Forced over everything: `FeatureManagement:AuditLog=false`. Other settings: `Localtest:Authorization:PoliciesPath`
(default `/policies`), `FetchPoliciesFromApps` (true), `PolicyCacheSeconds` (5).

## What the other components must provide

- Ingress on the platform endpoint: `/authorization/` to this service, `/register/` (v1 parties and the localtest
  endpoints), `/authentication/api/v1/openid/` (JWT validation for `parties`, `roles`), `/storage/api/v1/` (instance
  authinfo), and `/{org}/{app}/` to the running app (policy fetch when no policy is mounted).
- Register: the `RoleCodes` field of `test-users` (added for this component); seeded parties need stable UUIDs.
- Storage: `GET storage/api/v1/instances/{partyId}/{guid}/process/authinfo` without a user token. Until then every
  instance-scoped decision is `Indeterminate`.

## Verified (2026-10-17)

`./smoke-test.sh` with register on 5021, authentication on 8001 (`local.altinn.cloud:8001`), the PDP on 5070 and a
user token from `GET /Home/GetTestUserToken?userId=1337`, requests built from the token claims as the PEP does:

- Ola (1337, DAGL for 500000): `read` and `instantiate` on `ttd/testapp` for 500000 Permit; for 500003, 500002 (INNH)
  and himself (PRIV) NotApplicable; for sub-unit 500001 Permit. Kari (1338): DAGL for 500003 Permit, LEDE/MEDL for
  500000 NotApplicable.
- Instance-scoped request: Indeterminate (Storage authinfo 404). App without a policy (`ttd/otherapp`): Indeterminate.
- Editing the mounted policy changes the decision within 6 s; restoring it changes it back.
- `GET parties?userid=1337` with the bearer token: 501337, 500000 (sub-unit 500001), 500002; `parties/500000/validate`
  true, `500003` false; without a token 401.
- `podman build -t localtest-authorization:dev .`; the container (host network) answers `/health` 200 and the localtest
  decision endpoint Permit for 500000 and NotApplicable for 500003 with the built-in policies.

Not verified: access-package rules (no seeded user has packages for a policy that uses them), a real app's PEP calling
through the ingress, the policy fetch from a running app, `POST policies`, resource-registry decisions.

## Compose

```yaml
  authorization:
    build: ./components/authorization
    environment:
      Localtest__Authorization__PlatformEndpoint: http://local.altinn.cloud:8000/
    volumes:
      - ./components/authorization/policies:/policies:ro   # optional: the image carries the same files
    depends_on:
      - register
    ports:
      - "5070:5070"
```

Run locally without Docker (register on 5020, authentication on 8000):

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
dotnet build -c Release vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization
cd vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/bin/Release/net10.0   # Program.cs loads ./appsettings.json
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5070 \
  Localtest__Authorization__PoliciesPath=$OLDPWD/policies \
  Localtest__Authorization__RegisterLocaltestEndpoint=http://127.0.0.1:5020/register/api/localtest/ \
  PlatformSettings__ApiRegisterEndpoint=http://127.0.0.1:5020/register/api/v1/ \
  dotnet Altinn.Authorization.dll
curl 'http://127.0.0.1:5070/authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read'
```
