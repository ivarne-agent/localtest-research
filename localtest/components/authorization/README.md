# authorization

Production Altinn Authorization, the PDP (`vendor/`, see `UPSTREAM.md`), answering `authorization/api/v1/decision`,
`parties` and `roles` for apps, plus an emulated Access Management `accessmanagement/api/v1/enduser/authorizedparties`
(app-lib's party list). Azure Blob policies, the Access Management, Profile, Resource Registry, OED and
SBL Bridge PIPs, the audit queue and Key Vault are replaced by fakes; Register and Storage stay production HTTP calls
to the other localtest components.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (PDP host, `src/libs/Altinn.Authorization.Api.Contracts`, `src/pkgs` ABAC and PEP) except one `#if LOCALTEST` block |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST` for the vendored projects, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): policy store, PIPs over the register seed, token handling, no-op audit queue, a localtest decision helper, DI composition |
| `policies/` | Built-in policies, copied into the image at `/policies`: `ttd/testapp/policy.xml` (a Studio app policy with `[ORG]`/`[APP]` placeholders), `ttd/frontend-test/policy.xml` (copy of `src/test/apps/frontend-test/App/config/authorization/policy.xml` in altinn-studio, for the Cypress suite) and `resourceregistry/altinn_keyrole_access/policy.xml` (used by signing) |
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
| `IAccessManagementWrapper` (AM `policyinformation/roles-and-accesspackages`, `getdelegationchanges`, `authorizedparties`) | `LocaltestAccessManagementWrapper` over the register seed (`{register}/api/localtest/test-users`, `organizations`): roles as `urn:altinn:rolecode:<code>` in lower and upper case (see [Role codes](#role-codes)) and `urn:altinn:external-role:ccr:<identifier>`, access packages as `urn:altinn:accesspackage:<id>`; `PRIV` / `urn:altinn:role:privatperson` for the person's own party, plus any roles the register lists for it; a sub-unit gets the roles held for its main unit. No delegations. Authorized parties: the same list as the endpoint below (`LocaltestAuthorizedParties`) |
| `IProfile` (Profile `internal/user`) | `LocaltestProfile`: user id or ssn to the user's party (uuid, ssn) from the register seed |
| `IRoles` (SBL Bridge, legacy `roles` endpoint) | `LocaltestRoles`: role codes from the register seed |
| `IResourceRegistry` | `LocaltestResourceRegistry`: `resourceregistry/{id}/policy.xml` and optional `resource.json` in the policies folder; no access lists |
| `IOedRoleAssignmentWrapper` | `NoOedRoleAssignments` (empty) |
| `IEventsQueueClient` (audit queue) | `NoopEventsQueueClient`; `FeatureManagement:AuditLog` forced off |
| `IAccessTokenGenerator` (Key Vault certificate) | `LocaltestAccessTokenGenerator`: a token signed with a key made at start; the register only checks the header is present |
| `AccessTokenHandler`, `IPublicSigningKeyProvider` | `LocaltestAccessTokenHandler` (a `PlatformAccessToken` header is enough), `LocaltestPublicSigningKeyProvider` (no keys) |
| Access Management `GET accessmanagement/api/v1/enduser/authorizedparties` | `LocaltestAuthorizedPartiesController`, see [Authorized parties](#authorized-parties) |
| (none) | `GET authorization/api/localtest/decision?userId&partyId&org&app&action[&instanceId&task&authenticationLevel]`, anonymous: builds the request an app's PEP sends and posts it to the production `decision` endpoint |
| (none) | `POST authorization/api/localtest/refresh`, anonymous: drops the cached register seed and every production PDP cache entry (profiles, parties, policies) |

Kept production: `ContextHandler`, `DelegationContextHandler`, `DecisionController`, `PartiesController`, the ABAC
engine, `RegisterService` (Register v1 `parties/partylist`, `partylistbyuuid`, `lookup`, served by the register
component) and `InstanceMetadataRepository` (Storage `instances/{id}/process/authinfo`).

## Authorized parties

`GET accessmanagement/api/v1/enduser/authorizedparties` is what app-lib's `AuthorizationClient.GetPartyList` calls
(`{ApiAccessManagementEndpoint}enduser/authorizedparties?includeRoles=true&includeAccessPackages=true&...`, with the
user's token). The user is the `urn:altinn:userid` claim of the bearer token or runtime cookie; without a token,
`?userId=` is accepted (tools, debugging); a token plus a different `?userId=` is 403, neither is 401. Response, the
Access Management envelope with the contracts `AuthorizedPartyDto` (camelCase, `type` as `Person` / `Organization`):

```json
{"links": {"next": null}, "data": [
  {"partyId": 501337, "partyUuid": "…501337", "name": "Ola Nordmann", "personId": "01817510120", "type": "Person",
   "authorizedRoles": ["priv"], "authorizedAccessPackages": [], "onlyHierarchyElementWithNoAccess": false, "isDeleted": false, "subunits": []},
  {"partyId": 500000, "partyUuid": "…500000", "name": "DDG Fitness AS", "organizationNumber": "910000012", "type": "Organization",
   "unitType": null, "authorizedRoles": ["dagl"], "authorizedAccessPackages": [], "onlyHierarchyElementWithNoAccess": false,
   "subunits": [{"partyId": 500001, "name": "DDG Fitness Bergen", "organizationNumber": "910000020", "type": "Organization",
                 "unitType": "BEDR", "parentId": "…500000", "authorizedRoles": ["dagl"], "subunits": []}]},
  {"partyId": 500002, "name": "Nordmann Bakeri ENK", "organizationNumber": "910000039", "type": "Organization", "authorizedRoles": ["innh"], "subunits": []}
]}
```

Built from the register seed by `LocaltestAuthorizedParties`: the person's own party first (`priv`, plus any roles and
packages the register lists for the user's own party), then every party the register lists for the user, with
lower-case role codes and its access packages (identifiers without the `urn:altinn:accesspackage:` prefix); under an
organisation all its sub-units from `organizations` with the main unit's roles (plus any held directly). A sub-unit
held without its main unit brings the main unit along with `onlyHierarchyElementWithNoAccess: true`, as Access
Management does. `includeRoles=false` / `includeAccessPackages=false` empty those lists; the other flags
(`includeResources`, `includeInstances`, ...) are accepted and ignored: there are no resource or instance delegations
locally. The PDP's own `authorization/api/v1/parties?userId=` returns the same list (production code, through the
wrapper), sub-units as `childParties`.

Fields read from the register defensively (missing is null, unknown is ignored): per user `parties[]` with `roles`,
`roleCodes`, `accessPackages`, optionally `unitType` and `isDeleted`; per organisation optionally `unitType` and
`isDeleted`. Today the register sends no `unitType` for organisations, so main units have `unitType: null` and sub-units
default to `BEDR`.

## Role codes

Production Access Management sends `urn:altinn:rolecode` values in lower case (`dagl`, `regna`), and Studio app
policies match those (the frontend-test policy uses `dagl`, `regna`, `seln`); older policies and the register seed
use upper case (`DAGL`). XACML `string-equal` is case-sensitive, so the PIP emits every code in both cases (and as
seeded, if mixed). `authorizedRoles` in the authorized-parties list is lower case only. The person's own party always
has `PRIV`/`priv` and `urn:altinn:role:privatperson`.

## frontend-test policy

`policies/ttd/frontend-test/policy.xml` (concrete, no placeholders; `partyTypesAllowed` in the app is all false, which
means every party type may instantiate). What it grants:

| Subject | Actions |
| --- | --- |
| `rolecode` `dagl`, `regna` or `seln` | `instantiate`, `read`, `pdfIfRequested` on the app; `read`, `write`, `confirm`, `reject` in `Task_1`..`Task_5`, `Task_Custom_Confirm`, `Task_Default_Confirm`, `EndEvent_1`; `fill`, `conflictingOptionsReset`, `shiftingOptionsAdd`, `shiftingOptionsRemoveAll` in `Task_2`; `sortPets`, `generatePets` in `Task_3`; `delete`; `read` on the `events` app resource |
| `urn:altinn:org` `ttd` (the service owner) | `write` on the app; `complete` in `EndEvent_1` |

Nothing for `priv`/`PRIV`: a person acting for themselves is denied unless the register lists one of those roles for
their own party. The old localtest did exactly that for the Cypress users (user 12345 had `regna`, `dagl`, `priv` for
party 512345 in `testdata/authorization/roles`), so the register seed must carry those role codes for the self party
(`parties[]` entry for the user's own party with `roleCodes`) for `defaultPartyId: 12345.512345` to instantiate.

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

- Ingress on the platform endpoint: `/authorization/` and `/accessmanagement/` to this service, `/register/` (v1 parties and the localtest
  endpoints), `/authentication/api/v1/openid/` (JWT validation for `parties`, `roles`), `/storage/api/v1/` (instance
  authinfo), and `/{org}/{app}/` to the running app (policy fetch when no policy is mounted).
- Register: the `RoleCodes` field of `test-users` (added for this component); seeded parties need stable UUIDs. For
  the Cypress users: role codes for the user's own party (`regna`, `dagl`, `priv` as in the old localtest), Altinn 2
  codes that are not CCR roles (`regna`, `A0239`, ...), and `unitType` on organisations.
- Caching: the register seed is re-read at most every 2 s, so users and parties added by
  `POST register/api/localtest/apps` are seen almost at once. Production PDP caches (user profile, party lookups) last
  1 minute, including a "user not found" profile; `POST authorization/api/localtest/refresh` clears them all.
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

Added 2026-10-17 (`./smoke-test.sh` with `REG_PORT=5031 AUTHN_PORT=8011 PDP_PORT=5080`, all ok; the script adds user
12345 / party 512345 with no roles to a copy of the register seed):

- frontend-test: Ola (1337, DAGL emitted as `dagl`) on 500000 Permit for `instantiate`, `read`, `write` in `Task_1`,
  `confirm` in `Task_Custom_Confirm`, and `instantiate` on sub-unit 500001; NotApplicable for `write` without a task
  and `complete` (org only) and on 500002 (INNH). Kari (1338) DAGL on 500003 Permit. User 12345 for himself
  NotApplicable (only `PRIV`/`priv`). With a hand-made `test-users` that lists `roleCodes: [regna, dagl, priv]` for
  12345's own party (served by a stub, register v1 from the real register): `instantiate`, `read`, `write` in `Task_1`
  Permit and the self party lists `["priv", "regna", "dagl"]`.
- `accessmanagement/api/v1/enduser/authorizedparties` with Ola's token: 501337 (Person, `priv`), 500000 (`dagl`,
  sub-unit 500001 `dagl`), 500002 (`innh`); `includeRoles=false` empties roles; `?userId=12345` without a token; 401
  without either; 403 for a different `?userId`. `parties?userid=1337`: 501337, 500000 (500001), 500002.
- Image rebuilt; the container answers the frontend-test decision and the authorized-parties call.

Not verified: app-lib actually calling the endpoint through the ingress, `onlyHierarchyElementWithNoAccess` (no seeded
user holds a sub-unit without its main unit), access-package rules (no seeded user has packages for a policy that uses them), a real app's PEP calling
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
