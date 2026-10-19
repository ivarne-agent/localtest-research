# Authorization (PDP) review (2026-10-20)

## Summary

The vendored copy matches upstream `Altinn/altinn-auth` at `392f9578` exactly. The only change is one pure
`#if LOCALTEST` addition in `Program.cs`, and the baseline commit `df644ae` holds the files unmodified. The host is a plain
`WebApplication` (no AltinnHost), so the ServiceDefaults "Environment: UNKNOWN" trap does not apply, and nothing at
runtime needs the internet. The component falls short of the project's later rules. Its Dockerfile does not follow the
upstream one: tags are not pinned by digest, the container runs as root, there is no tzdata/krb5-libs, there are no
`localtest:` markers, and it has a dead `HEALTHCHECK`. `UPSTREAM.md` has no sync instructions. Upstream main is 6 commits
ahead. One of them (`8509a47b4`) removes the `Altinn.Authorization.Models.Register` namespace, which
`fakes/Pip/LocaltestResourceRegistry.cs` imports, so the next sync will not compile without a fix. The Profile PIP fake
could be swapped for production `ProfileWrapper`, because the register component already serves `profile/api/v1/internal/user`.

## Vendored code

- Pin: `392f9578d925b947331a1e90fb609c031654d3d2` (main, 2026-10-16), recorded in `vendor/UPSTREAM.md`.
- Fidelity: ran `git archive 392f957 <all copied paths listed in UPSTREAM.md>` (the build files, `src/apps/Altinn.Authorization`,
  `src/libs/Altinn.Authorization.Api.Contracts`, `src/pkgs/Altinn.Authorization.ABAC`, `src/pkgs/Altinn.Authorization.PEP`)
  into an empty scratch folder, then `diff -rq --exclude=bin --exclude=obj` against `vendor/`. The only differences were `UPSTREAM.md` (only in
  vendor) and `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`. No files are missing or extra.
- History: `git diff --stat df644ae..HEAD -- vendor` shows only `Program.cs | 5 +++++`. `df644ae` ("baseline copy …
  unmodified") extracted and diffed against upstream: identical except `UPSTREAM.md`.
- The working tree has untracked `bin/` and `obj/` folders under all four vendored projects, left by a local build.
  `.dockerignore` excludes them, so they do not reach the image.
- Directive sites (one):

| File | Site | What it does | Rule |
| --- | --- | --- | --- |
| `vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs:61-65` | `#if LOCALTEST` … `#endif` after `ConfigureServices(builder.Services, builder.Configuration);` | Calls `LocaltestAuthorizationExtensions.AddLocaltestAuthorization(builder)`, which adds configuration defaults and replaces DI registrations | Follows the rule: a pure addition, production lines untouched |

## Upstream drift

`gh api repos/Altinn/altinn-auth/compare/392f9578…main`: main is 6 commits ahead and 0 behind.

| Commit | Date | Touches vendored paths? |
| --- | --- | --- |
| `8509a47b4` chore(#3998): move AppsInstanceDelegationController to a standalone Api.Apps project (#4414) | 2026-10-19 | **Yes.** See the notes after the table. |
| `5968cd8a6` chore(containers): move to chiseled base images (#4585) | 2026-10-19 | Changes only `containers/Dockerfile.aspnet-builder` and `-runtime` (sdk `10.0-noble`, runtime `aspnet:10.0-noble-chiseled-extra`). These are not vendored, and the app's own `src/apps/Altinn.Authorization/Dockerfile` is unchanged. |
| `452a8feea`, `15203f7cb`, `afb4624d5`, `3d5181037` | 2026-10-19 | No (Access Management, agent docs, Renovate config) |

Notes on `8509a47b4`:

- `DecisionController.cs`: access-list requests now `TryParse` the org number and return false on failure.
- `ContextHandler.cs`, `ResourceRegistryWrapper.cs` and `IResourceRegistry.cs` change their usings.
- `Models/Register/{OrganizationNumber,PartyIdentifiers,PartyUrn}.cs`, `Models/{ActionIdentifier,ActionUrn}.cs` and
  `Models/ResourceRegistry/{ResourceIdUrn,ResourceIdentifier}.cs` are removed or moved into `src/libs/Altinn.Authorization.Api.Contracts`
  (namespaces `…Api.Contracts.Register`, `…Api.Contracts.ResourceRegistry`, `…Api.Contracts.Authorization`).
- `Helpers/ServiceResourceHelper.cs` is removed.
- `Api.Contracts.csproj` gains `Swashbuckle.AspNetCore.Annotations`.
- `conf.json` gains a dependency on `lib:Altinn.Authorization.Api.Contracts`.
- The Access Management DTOs move into Api.Contracts.

The compare also shows `src/apps/Altinn.Authorization/Models/Register` no longer exists on main (contents API returns 404).

Impact: the removed namespace breaks the build of `fakes/Pip/LocaltestResourceRegistry.cs`, which has
`using Altinn.Authorization.Models.Register;` and uses `PartyUrn` and `ResourceIdUrn` (finding 2). The Access Management
component vendors the same altinn-auth pin and gets DTO moves into the same lib. Re-pin both components together.

## Dockerfile

Upstream at the pin has three candidates:

- `src/apps/Altinn.Authorization/Dockerfile` (vendored as `vendor/src/apps/Altinn.Authorization/Dockerfile`): the classic one. sdk
  `10.0-alpine@sha256:d8ee3981…` with `COPY src/ .`, `dotnet publish -c Release -o /app` in the project folder. Then aspnet
  `10.0-alpine@sha256:27b6b84b…` with `EXPOSE 5050`, `apk add tzdata krb5-libs`, the `dotnet` user (uid 1000, gid 3000),
  `USER dotnet` and `mkdir /tmp/logtelemetry`.
- `src/apps/Altinn.Authorization/docker-bake.hcl`: its only target is `dockerfile-inline = "FROM aspnet-runtime\nENV
  I_AM_TEST=true"` over the shared `containers/Dockerfile.aspnet-*`, and the `default` group is empty. CI builds containers
  with `repoctl vertical containers build` (`.github/workflows/ci.yml:187`), and `repoctl` source is not in the repo. I
  could not verify which file the production Authorization image is built from. `conf.json` has `"dis": {"deploy": false}`, and
  `release-app.yml` offers only `Altinn.AccessManagement`. As the authentication component's UPSTREAM.md concluded, the classic
  Dockerfile is the one to mirror.

Component `Dockerfile` compared with the upstream classic one:

| Upstream | Component | Note |
| --- | --- | --- |
| `sdk:10.0-alpine@sha256:d8ee…` | `sdk:10.0-alpine` (no digest) | Unpinned |
| `WORKDIR /app`, `WORKDIR /src`, `COPY src/ .`, publish from the project folder | `WORKDIR /src`, `COPY . .`, overrides env, publish by csproj path | Needed for the overrides, but not marked `localtest:` |
| `aspnet:10.0-alpine@sha256:27b6…` | `aspnet:10.0-alpine` (no digest) | Unpinned |
| `apk add --no-cache tzdata krb5-libs` | missing | The PDP code has no `TimeZoneInfo` or culture lookups (grep), so there is no visible effect today. It is still a deviation. |
| `addgroup`/`adduser dotnet`, `USER dotnet`, `mkdir /tmp/logtelemetry` | missing: runs as root | Confirmed: `podman inspect localtest_authorization_1` shows an empty `.Config.User`, and DataProtection keys go to `/root/.aspnet`. |
| `EXPOSE 5050` | `EXPOSE 5070`, `ASPNETCORE_URLS=http://+:5070` | Port chosen by localtest. Upstream deployments set 8080 through `manifests/deploy/base/config/environment.env`. |
| (none) | `COPY policies /policies`, `mkdir -p /policy-store`, `ENV ASPNETCORE_ENVIRONMENT=Development …` | Localtest additions, unmarked |
| (none) | `HEALTHCHECK … wget …/health` | Dead: `podman inspect` shows `Healthcheck: null`, because OCI images drop it. The compose file has no healthcheck for the service. |

Moving to the non-root user has a catch: `/policy-store` is a named volume shared with access-management, and both
containers run as root today (`podman inspect` of access-management also shows an empty User). The existing volume is
owned by root, so both images have to switch together, with `chown 1000:3000 /policy-store` in the image or a volume
reset.

## Fakes and what they replace

| File | Replaces (production) | Category | Could it be production code? |
| --- | --- | --- | --- |
| `fakes/LocaltestAuthorizationExtensions.cs` | DI composition: `RemoveAll` and re-add of the registrations below, configuration defaults (platform URLs, 1-minute caches, prod feature flags), audit flags forced off, JWT cookie metadata address re-applied, `AccessTokenGenerator` instead of `DevAccessTokenGenerator` | glue | n/a |
| `fakes/LocaltestAuthorizationOptions.cs` | `Localtest:Authorization` settings | glue | n/a |
| `fakes/Policies/LocaltestPolicyRepository.cs` | `IPolicyRepository` (`PolicyRepository` over Azure Blob `metadata` and `delegationpolicies`, versions, leases) | Azure Blob | No, Blob is excluded by the rules. The file store is the right choice. Leases are no-ops, so `WritePolicyConditionallyAsync` never conflicts. |
| `fakes/Policies/LocaltestResponse.cs` | `Azure.Response` for the fake writes | Azure SDK shim | n/a |
| `fakes/Policies/LocaltestPolicyRetrievalPoint.cs` | Wraps production `PolicyRetrievalPoint` and evicts the app-policy cache entry before each read | cache behaviour | Could be configuration only: `PolicyCacheTimeout` is in minutes and production uses `AbsoluteExpirationRelativeToNow` (not verified in detail). Re-pushes go through this same process, so a wrapper is a convenience, not a necessity. |
| `fakes/Pip/LocaltestProfile.cs` | `IProfile` (`ProfileWrapper`, which does `POST profile/api/v1/internal/user` with `{UserId}`/`{Ssn}`) | other service | **Yes.** The register component serves `profile/api/v1/internal/user` anonymously (`components/register/fakes/Altinn.Register.Localtest/Controllers/LocaltestProfileController.cs:50`), and the proxy routes `/profile/` to register. The default `PlatformSettings:ApiProfileEndpoint` already points there. Authentication uses its production Profile client against it. |
| `fakes/Pip/LocaltestResourceRegistry.cs` | `IResourceRegistry` (`ResourceRegistryWrapper`: resource, resource policy, access lists) | other service | Partly. No resource registry component exists, so the folder stand-in is reasonable. It breaks on the next sync (finding 2). |
| `fakes/Pip/NoOedRoleAssignments.cs` | `IOedRoleAssignmentWrapper` (external Digitalt dødsbo API via Maskinporten) | external service | No, it is external. Empty is fine. |
| `fakes/Infrastructure/NoopEventsQueueClient.cs` | `IEventsQueueClient` (Azure Storage Queue audit log) | Azure Queue | No, it is excluded. `AuditLog` is also forced off. |
| `fakes/Infrastructure/LocaltestPublicSigningKeyProvider.cs` | `IPublicSigningKeyProvider` (Key Vault certificates per issuer) | Key Vault | No. It returns the shared test certificate for every issuer. |
| `fakes/Register/RegisterLocaltestClient.cs` | (none) Reads `register/api/localtest/test-users` and `organizations` | localtest helper | Only `LocaltestProfile` uses it. It disappears if the Profile fake is dropped. |
| `fakes/Controllers/LocaltestDecisionController.cs` | (none) `GET authorization/api/localtest/decision` (anonymous; builds a PEP-style request and posts it to the production `decision`) and `POST …/localtest/refresh` (invalidates the seed and runs `MemoryCache.Compact(1.0)`) | localtest-only API | n/a |
| `policies/resourceregistry/altinn_keyrole_access/policy.xml` | Resource Registry policy for `altinn_keyrole_access` | test data | Provenance not verified |

Kept production (from reading `Program.cs` and the overrides): `ContextHandler`, `DelegationContextHandler`, `PolicyAdministrationPoint`,
`RegisterService` (Register v1), `InstanceMetadataRepository` (Storage authinfo), `AccessManagementWrapper`,
`AccessListAuthorization`, `RolesWrapper` (SBL bridge, which gets a 404 at the proxy and so returns an empty list), `AccessTokenHandler`,
`AccessTokenGenerator`, `EventLogService`, and the JWT cookie scheme.

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| App policy source | Azure Blob `metadata/{org}/{app}/policy.xml`, written by Designer at deploy | File volume `policy-store`, written by the same production `POST policies` endpoint from `run-app.sh`. Editing `policy.xml` does nothing until it is pushed again. |
| Policy cache | `PolicyCacheTimeout` 10 min (appsettings) | App policy re-read from disk on every decision, so a new push applies at once |
| Role, main-unit and key-role caches | 5 min | 1 min, cleared by `POST authorization/api/localtest/refresh` |
| Profile PIP | Profile service | Register seed via fake. `PartyTypeName` is always `Person`; `UserType` and profile settings are not set. |
| Resource Registry | Real registry: every resource, access lists | Only `altinn_keyrole_access` from `/policies`. Other resource ids have no policy and no resource. Access lists are always empty, so access-list-gated resources never Permit. |
| OED (dødsbo) roles | External API | Always none |
| Audit log | `FeatureManagement:AuditLog=true`, Azure Queue | Off, dropped |
| Platform access tokens | Per-issuer Key Vault certificates | One shared test certificate accepted for every issuer |
| Errors | `UseExceptionHandler("/authorization/api/v1/error")` (ProblemDetails) | `UseDeveloperExceptionPage` and `IdentityModelEventSource.ShowPII = true` (Development) |
| OpenID metadata | HTTPS required | `RequireHttpsMetadata=false` (Development), through `http://local.altinn.cloud:8000` |
| Logging | Application Insights | Console at Debug level (`appsettings.Development.json`). In the log I read, 845 of about 900 entries were `JwtCookieHandler` debug lines. |
| Container | non-root `dotnet`, tzdata, port 8080 | root, no tzdata, port 5070 |
| Feature flags | prod `environment.env` | Same values, except `AuditLog`. `AccessManagementAsPipForRoles` and `GeneralSettings:UseStorageApiForInstanceAuthInfo` are left out, but grep finds no code that reads them at the pin, and the same is true of `AccessManagementAuthorizedParties`, which *is* set. No effect. |
| Extra endpoints | none | Anonymous `authorization/api/localtest/decision` and `/refresh` |

## Findings

1. **Dockerfile does not mirror upstream.** `Dockerfile:3,13` uses unpinned `10.0-alpine` tags. It also has no `USER dotnet`
   or `adduser`, no `apk add tzdata krb5-libs` and no `/tmp/logtelemetry`, no `localtest:` markers, and a dead
   `HEALTHCHECK` (`Dockerfile:25`). This breaks the 2026-10-19 rule that storage, register and authentication follow:
   the image drifts silently and runs as root, unlike production. Fix: rewrite along the lines of
   `components/authentication/Dockerfile` against `vendor/src/apps/Altinn.Authorization/Dockerfile` with digests
   `d8ee3981…` and `27b6b84b…`, mark each deviation `localtest:`, and remove the HEALTHCHECK with the standard comment.
   Coordinate the non-root switch with access-management because of the shared `policy-store` volume (chown or volume
   reset). Severity: medium.
2. **The next sync breaks the fakes build.** `fakes/Pip/LocaltestResourceRegistry.cs:9-10` imports
   `Altinn.Authorization.Models.Register`, which upstream `8509a47b4` deletes. `PartyUrn` moves to
   `Altinn.Authorization.Api.Contracts.Register` and `ResourceIdUrn` to `…Api.Contracts.ResourceRegistry`, and
   `IResourceRegistry` now imports those namespaces. Fix: at re-pin, change the usings (and verify
   `Models.ResourceRegistry` still provides `AccessListResourceMembershipWithActionFilterDto`; it is still in that folder
   on main). Severity: medium, because it blocks the sync, though the fix is trivial.
3. **`UPSTREAM.md` has no sync procedure and no Dockerfile rule.** `vendor/UPSTREAM.md` lists the pin and paths only.
   Storage, register and authentication each have a "Syncing to a newer upstream commit" section, with steps for the diff,
   `git archive` and `diff -rq`, and "keep `../Dockerfile` equal to upstream". Fix: add the same section, naming
   `src/apps/Altinn.Authorization/Dockerfile`, the docker-bake note, the single directive site, and the need to re-pin
   together with access-management (same repository, shared `Api.Contracts`). Severity: medium.
4. **Profile PIP fake can be production code.** `fakes/Pip/LocaltestProfile.cs` and
   `fakes/LocaltestAuthorizationExtensions.cs:107` replace `ProfileWrapper`, but the register component answers the same
   `POST profile/api/v1/internal/user` (`{UserId}`/`{Ssn}`), and `PlatformSettings:ApiProfileEndpoint` already points
   there (`LocaltestAuthorizationExtensions.cs:56`). The fake also hard-codes `PartyType.Person` and fills only a subset of
   `UserProfile`. Fix: drop `LocaltestProfile` and the `Replace<IProfile,…>` line, then `RegisterLocaltestClient` and
   probably the seed half of `/refresh`. Re-run `smoke-test.sh`. I have not verified that the register's response
   deserialises into `Altinn.Platform.Profile.Models.UserProfile` with all fields the ContextHandler uses. Severity: medium,
   because the user prefers production code.
5. **Stale documentation and comments about policy caching.** `README.md` (fakes table, `IPolicyRetrievalPoint` row) says
   "a policy edit applies within about 5 s". The `fakes/Policies/LocaltestPolicyRetrievalPoint.cs:14-16` doc says "the
   few-seconds reuse in LocaltestPolicyRepository decides…". But `LocaltestPolicyRepository` has no cache: it reads
   the file on every call (`:32-33,98-99`), so a push applies at once, as the README's own "Verified" section says.
   `LocaltestAuthorizationExtensions.cs:98` also says "folder, posted or running app". Nothing is fetched from apps now.
   Fix: correct the wording. Severity: low.
6. **Stale class docs and dead code from before Access Management.** `LocaltestAuthorizationExtensions.cs:30-31` says it
   replaces "the Access Management … and SBL Bridge PIPs", but both are production now (the code's own comment at `:104-106`
   says so). `RegisterLocaltestClient.cs:14-15` calls itself "the one source of roles and access packages for the Access
   Management, Profile and roles fakes". `TestUserParty`, `PartyGrants`, `Orgs`, `SubUnitsOf` and `UserByPartyUuid` are
   unused (grep), yet every refill fetches `organizations` as well (`:47`). Fix: delete the unused members, the
   `organizations` call and the stale text (or all of it with finding 4). Severity: low.
7. **README "Compose" and "Run locally without Docker" are stale.**
   - The README compose snippet shows `ports: "5070:5070"` and no `certs`/`policy-store` volumes. The actual
     `docker-compose.yml` publishes no port, mounts `./certs` and `policy-store`, and sets
     `AccessTokenSettings__AccessTokenSigningCertificateFullPath`.
   - The standalone recipe sets neither `Localtest__Authorization__PolicyStorePath` (the default `/policy-store` is
     normally not writable outside a container) nor the certificate path. Without the certificate,
     `LocaltestPublicSigningKeyProvider` returns no keys, so every platform access token is rejected: the Studio
     token on policy push, and Register's view of outgoing tokens. I worked this out from the code and did not run it.
   - The recipe claims "authentication on 8000". The PDP also needs access-management, storage and the proxy.

   Fix: copy the current compose block and either complete the standalone recipe or remove it. Severity: low.
8. **`ASPNETCORE_ENVIRONMENT=Development` side effects.** This is required: without it `RequireHttpsMetadata` stays true and
   the HTTP OpenID metadata fails. It also turns on the developer exception page, PII in token errors, user secrets, and
   Debug logging. JwtCookie "not authenticated" debug lines are about 95% of the container log, which hides useful
   lines. Fix (optional): add `Logging__LogLevel__Default=Information` (or `AltinnCore.Authentication.JwtCookie=Information`)
   to the image env as a `localtest:` line. Note in the differences table that errors look different from production.
   Severity: low.
9. **Policy-store writes from two processes are not serialised.** `LocaltestPolicyRepository` uses a per-process
   `SemaphoreSlim`. Access Management's `LocaltestFilePolicyRepository` writes the same volume. The leases that production
   uses for delegation policies are no-ops (`TryAcquireBlobLease` returns a fresh GUID, and `WritePolicyConditionallyAsync`
   ignores the lease). In practice the PDP writes only `metadata/` and AM writes `delegationpolicies/`, so they do not overlap
   today. Also, two near-identical file repositories exist: the PDP's selects the container with `Contains("delegationpolicy.xml")`,
   as production does, while AM's uses `EndsWith`. Fix: none needed now. Consider sharing one implementation. Severity: low.
10. **Untracked build output inside `vendor/`.** `bin/` and `obj/` exist under all four vendored projects (local
    `dotnet build`). They are ignored by `.dockerignore`, but they make `diff -rq` noisy and can confuse a future
    `git subtree`. Fix: `git clean -X` them, or tell the standalone recipe to build with `-o` outside `vendor/`. Severity: low.

No AltinnHost/ServiceDefaults dead-defaults issue: `Program.cs:52` uses `WebApplication.CreateBuilder`. The
`Altinn.Authorization.ServiceDefaults` package is referenced but `AltinnHost` is not called (grep), and the log has no
"Environment:" descriptor line. The in-memory defaults are inserted just below the last
`EnvironmentVariablesConfigurationSource` (`LocaltestAuthorizationExtensions.cs:76-77`), which is above `/app/appsettings.json`.
The production options bind lazily, so they take effect. The one eager read (JWT cookie `MetadataAddress`) is re-applied
(`:93`). No internet dependency: OED's Maskinporten client is registered but never called (OED is faked), the
`DevAccessTokenGenerator` (testtools URL) is replaced, `kvSetting` is empty, and grep finds no `altinncdn` in the vendored host.

## Verification done

- Read `AGENTS.md`, `localtest/README.md`, `planned-components/mvp-vendoring-model.md`, the component `README.md`,
  `vendor/UPSTREAM.md`, `Dockerfile`, `.dockerignore`, `localtest.build.props/.targets`, every file in `fakes/`, the start of
  `smoke-test.sh`, `vendor/…/Program.cs`, `appsettings*.json`, `PlatformAccessTokenDependencyInjectionExtensions.cs`,
  `ProfileWrapper.cs`, `RolesWrapper.cs`, `PolicyRepository.CreateBlobClient`, the authorization block of `docker-compose.yml`,
  `localtest/Proxy/PlatformRoutes.cs`, and the register's `LocaltestProfileController.cs`.
- `git archive 392f957 <paths> | tar -x` into `scratchpad/authz/up`, then `diff -rq --exclude=bin --exclude=obj` against `vendor/`.
  Result: only `UPSTREAM.md` and `Program.cs` differ. `diff -u` of `Program.cs`: 5 added lines.
- `git log -- .` for the component, `git diff --stat df644ae..HEAD -- vendor`, and `git archive df644ae vendor` diffed against upstream (identical).
- Upstream (reference clone at `392f957`): the classic `Dockerfile`, `containers/Dockerfile.aspnet-*`, `docker-bake.hcl`,
  `conf.json`, `.github/workflows/{ci,release-app,deploy-release-app}.yml`, and `manifests/deploy/*/config/environment.env`.
- `gh api repos/Altinn/altinn-auth/compare/392f9578…main` (files and commits), `gh api …/commits/<sha>` for the 4 candidate
  commits, and `gh api …/contents/…?ref=main` for `Models/Register` (404), `Models/ResourceRegistry` and `IResourceRegistry.cs`.
- `podman ps`; `podman inspect localtest_authorization_1` (env, empty User, Healthcheck null) and
  `localtest_access-management_1` (empty User); `podman logs localtest_authorization_1` (about 1750 lines: 3 startup warnings,
  no `fail:`, 845 JwtCookie debug lines).
- GET through the proxy: `authorization/swagger/v1/swagger.json` returned 200, and
  `authorization/api/localtest/decision?userId=1337&partyId=500000&org=ttd&app=testapp&action=read` returned Permit with an
  authentication-level obligation.
- Not verified:
  - which Dockerfile produces the production image (`repoctl` source unavailable);
  - that the register's `internal/user` response is a full drop-in for `ProfileWrapper` (finding 4);
  - where `altinn_keyrole_access/policy.xml` came from;
  - the standalone run recipe;
  - whether production exposes `decision` anonymously.

  No builds, smoke tests, POST requests or container changes were made, per the constraints.
