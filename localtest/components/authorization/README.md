# authorization

Production Altinn Authorization, the PDP (`vendor/`, see `UPSTREAM.md`), answering `authorization/api/v1/decision`,
`parties` and `roles` for apps. Azure Blob policies (a file store shared with Access Management), the OED PIP, the
audit queue and Key Vault are replaced by fakes; Register, Profile (`internal/user`, answered by the profile
component, production Profile), Storage, Access Management (roles, access packages, delegations, authorized parties) and the Resource
Registry (resources, resource policies such as `altinn_keyrole_access`, access lists) stay production HTTP calls to the
other localtest components.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy (PDP host, `src/libs/Altinn.Authorization.Api.Contracts`, `src/pkgs` ABAC and PEP) except one `#if LOCALTEST` block |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST` for the vendored projects, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): policy store, OED PIP, token handling, no-op audit queue, a localtest decision helper, DI composition |
| `smoke-test.sh` | Checks decisions, policy pushes and authorized parties against the running stack (see below) |
| `Dockerfile` | Mirrors the upstream `vendor/src/apps/Altinn.Authorization/Dockerfile` (pinned Alpine images, tzdata and krb5-libs, non-root `dotnet`); each difference is marked `localtest:`. Listens on 5070 |

## Directive sites

Service discovery (task 25): a pure `#if LOCALTEST` addition (the package comes transitively from `Altinn.Authorization.ServiceDefaults`) right after `WebApplication.CreateBuilder` calls
`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery` (`fakes/ServiceDiscovery/`, a copy of `../shared/fakes/`), so
factory clients resolve `http://<service>/` from `services__<service>__http__0`; since task 40 without pass-through: a
name without an entry fails at once, a real host (a dot, an IP, `localhost`) is used as it is ([`planned-components/service-discovery.md`](../../../planned-components/service-discovery.md)).

**Service names (task 40):** the Register, Storage, the Resource Registry, Access Management and Profile are called as
`http://<service>/<prefix>/…` (fakes' defaults), and the fake's `localtest/decision` calls the production endpoint as
`http://authorization/authorization/api/v1/decision`; `Localtest__Authorization__PlatformEndpoint` stays the public
origin (JwtCookie metadata, SBL links).

The other, a pure addition in `vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`, right after
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
| `IPolicyRepository` (Azure Blob `metadata`, `delegationpolicies`) | `LocaltestPolicyRepository`: the file store shared with Access Management (`Localtest:Authorization:PolicyStorePath`, the `policy-store` volume), laid out as the containers: `metadata/{org}/{app}/policy.xml` written by the production `POST authorization/api/v1/policies` (the runner pushes it, placeholders already replaced as Designer does) and `delegationpolicies/{path}` with versions under `{path}.versions/{versionId}`, written by Access Management and read here by the version it recorded. Nothing is fetched from apps and no placeholders are replaced |
| `IPolicyRetrievalPoint` | Production `PolicyRetrievalPoint` wrapped by `LocaltestPolicyRetrievalPoint`, which drops its minutes-long cache entry for the app policy before each read; the store reads the file every time, so a pushed policy applies to the next decision |
| `IOedRoleAssignmentWrapper` | `NoOedRoleAssignments` (empty) |
| `IEventsQueueClient` (audit queue) | `NoopEventsQueueClient`; `FeatureManagement:AuditLog` forced off |
| `IPublicSigningKeyProvider` (Key Vault certificates) | `LocaltestPublicSigningKeyProvider`: the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`, see `../../certs/README.md`) for every issuer. The production `AccessTokenGenerator` and `AccessTokenHandler` run unchanged: outgoing platform access tokens (to Register) are signed with it, incoming ones validated against it |
| (none) | `GET authorization/api/localtest/decision?userId&partyId&org&app&action[&instanceId&task&authenticationLevel]`, anonymous: builds the request an app's PEP sends and posts it to the production `decision` endpoint |
| (none) | `POST authorization/api/localtest/refresh`, anonymous: drops every production PDP cache entry (profiles, parties, roles and packages, delegation changes) |

Kept production: `ContextHandler`, `DelegationContextHandler`, `DecisionController`, `PartiesController`, the ABAC
engine, `RegisterService` (Register v1 `parties/partylist`, `partylistbyuuid`, `lookup`, served by the register
component), `ProfileWrapper` (Profile `internal/user`, served by the profile component), `InstanceMetadataRepository` (Storage `instances/{id}/process/authinfo`), `AccessManagementWrapper`
(Access Management `policyinformation/roles-and-accesspackages`, `policyinformation/getdelegationchanges`,
`authorizedparties`, `authorizedparty/{partyId}`, served by the access-management component) and `RolesWrapper` (the
Altinn 2 `roles` endpoint over the SBL bridge, which does not exist locally: it answers an empty list, and nothing
locally calls it).

## Authorized parties and role codes

Served by the access-management component, not here: app-lib's `GetPartyList` calls
`accessmanagement/api/v1/enduser/authorizedparties` (the proxy routes all of `/accessmanagement/` there), and this
PDP's `parties` endpoint calls Access Management's internal `authorizedparties` through the production wrapper. See
`../access-management/README.md`.

Access Management sends `urn:altinn:rolecode` values in lower case (`dagl`, `regna`). Every role-code match in the
test apps' policies and the Studio template uses `string-equal-ignore-case`, so upper-case policies (`DAGL`) match.

## What the other components must provide

- Proxy on the platform endpoint: `/authorization/` to this service, `/accessmanagement/` to Access Management (PIP
  and authorized parties), `/register/` (v1 parties and the localtest endpoints), `/profile/` (`internal/user`), `/authentication/api/v1/openid/`
  (JWT validation for `parties`, `roles`), `/storage/api/v1/` (instance authinfo).
- Register: v1 parties for the subject and resource party lookups.
- Profile (the profile component): `internal/user` for the user profile.
- Caching: production PDP caches (user profile, party lookups) last 1 minute, including a "user not found" profile, so
  users and parties added by `PUT register/api/localtest/apps/{org}/{app}` are seen after a minute;
  `POST authorization/api/localtest/refresh` clears them all at once.
- Storage: `GET storage/api/v1/instances/{partyId}/{guid}/process/authinfo` without a user token.
- Access Management: the production PIP and authorized-parties endpoints; its delegation-change lookups are cached here
  for five minutes per subject, party and app (not instance), so drop the caches with `POST
  authorization/api/localtest/refresh` after new instance delegations when a test needs them at once.

## Verified (2026-10-18)

`./smoke-test.sh` against the running stack (30 checks, all ok), user tokens from `GET /Home/GetTestUserToken`,
requests built from the token claims as the PEP does, roles and packages from Access Management:

- Policies pushed with the Studio token (200); a service-owner token is refused (403).
- `ttd/testapp`: Sophie Salt (1337, DAGL on DDG Fitness and its sub-units) Permit for `read`/`instantiate` on 500000, a
  sub-unit and herself (REGNA); NotApplicable on 500005 and 500900. Kari Nordvik (2002, Altinn 2 codes only on 500000)
  NotApplicable.
- An unknown instance and an app without a pushed policy are Indeterminate; a re-pushed policy applies at once.
- `ttd/frontend-test` with the app's own policy: task-scoped `write`/`confirm` Permit, org-only rules NotApplicable for
  users, Ola Nordmann (12345) Permit for himself.
- Authorized parties from Access Management: Sophie's five top-level parties with sub-units, `dagl` on DDG Fitness and
  its sub-units with `includeRoles=true`; 401 without a token; the PDP's `parties?userid=1337` returns the same ids.

Again on 2026-10-21 (task 14, `internal/user` from the profile component): 30 of 30, as recorded in
`../profile/README.md` and `../../README.md`.

The "store:" listing (`podman exec`) and the closing check on the log (`podman logs` since the start of the run) find the
container through [`../../smoke/lib/containers.sh`](../../smoke/lib/containers.sh) (studioctl's `localtest-v2-authorization`
or the Aspire package's `authorization-<hash>`; `CONTAINER=` overrides it). The log check is the 31st check: it
expects no `fail:`/`crit:` entry other than the three the run provokes itself (the unknown instance's 404, logged
twice, and the app without a policy) and prints the first ones when there are. Without podman or the container both
say "skipped" and do not fail the run (changed 2026-10-21, task 12; the compose-era name made them show nothing and 0
before). 31 of 31 on 2026-10-21 (pin `sha-735e5d2`, `--with everything`). The full log: `studioctl env logs -r localtest-v2 --component authorization`.

## Run a changed component

The stack runs through studioctl's `localtest-v2` runtime (see `../../README.md`), which pulls this component's image
from ghcr. To run a change made here instead, build it from this checkout:

```sh
cd localtest/components/authorization && studioctl env up -r localtest-v2 --dev-authorization
```

`--dev-<container>[=<path>]` builds the image from the Dockerfile at the path or in the directory (default: the current
one). The front door cannot route the component's prefix to an instance running in an IDE; `--dev` rebuilds the image.

In the stack the PDP gets the platform endpoint, the shared certificate at `/certs` and the `policy-store` volume
(`localtest-v2-policy-store` under studioctl) shared with Access Management and the Resource Registry; the front door (the localtest container) routes
`/authorization/` to it ([`../../CONVENTIONS.md`](../../CONVENTIONS.md)).
