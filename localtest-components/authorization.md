# Localtest strand: Authorization, Access Management, Resource Registry

Research date 2026-10-16. Sources: monorepo `origin/main` 447e53eb97; live
[Altinn/altinn-auth](https://github.com/Altinn/altinn-auth) shallow clone 392f9578 ([`.reference/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2));
archives [`.reference/altinn-authorization-archive`](https://github.com/Altinn/altinn-authorization-archive/tree/40fcc2837ced4b536bf684195e438345a6ef33c3), [`.reference/altinn-access-management-archive`](https://github.com/Altinn/altinn-access-management-archive/tree/4315369f739bc5298f7f13c0bb57824dd25ebfb3),
[`.reference/altinn-resource-registry`](https://github.com/Altinn/altinn-resource-registry/tree/a3422419579832833845e47d3be57c148364bed0) (all three archived on GitHub). Paths below are relative to
`/home/agent/code` unless stated.

## Executive summary

- **All production code lives in one place now:** the `Altinn/altinn-auth` monorepo. It holds the PDP
  ([`src/apps/Altinn.Authorization`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization)), Access Management ([`src/apps/Altinn.AccessManagement`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement)), Resource Registry
  ([`src/apps/Altinn.ResourceRegistry`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry), imported with history 2026-09-25), Authentication, Mockporten, a Register
  placeholder, and the NuGet packages `Altinn.Authorization.ABAC` 0.3.0 and `Altinn.Common.PEP` 5.0.0
  ([`src/pkgs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs)). `altinn-authorization`, `altinn-access-management` and `altinn-resource-registry` are archived.
  All are still .NET 10 ASP.NET services, built as containers in `ghcr.io/altinn/altinn-auth/*` and deployed by
  Flux/Kustomize to AKS ([`manifests/deploy/<env>`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/manifests/deploy)).
- **The production PDP no longer gets roles from SBL Bridge or Register.** Since [altinn-auth#3114](https://github.com/Altinn/altinn-auth/issues/3114) and [#4307](https://github.com/Altinn/altinn-auth/issues/4307)
  (2026-10-12) it asks Access Management `policyinformation/roles-and-accesspackages?from=&to=` (party UUIDs) for
  roles and access packages, and `policyinformation/getdelegationchanges` for delegations. Access Management
  answers from its own Postgres database, which it fills by streaming parties and external role assignments from
  Register v2. The PDP itself has no database. App policies come from Azure Blob (`metadata/{org}/{app}/policy.xml`)
  and resource policies from the Resource Registry API.
- **Localtest's copies are old forks, not copies you could sync.** They were first copied in 2019 and refreshed in
  2023. `ContextHandler` and the constants and models still match production quite closely, but the
  `DecisionController` shares only about 20 % of its lines with production. It builds system-user policies in
  memory, evaluates instance delegations from JSON files, writes audit files, and reads roles straight from
  [`testdata`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata). The Resource Registry and Access Management parts are thin emulations.
- **Verdict.**
  - **(a) Subtree merge, then compile the real controllers with testdata-backed PIP and PRP:** feasible for the
    PDP only. The PDP is about 4 300 lines, has no database, and keeps its PIPs behind interfaces. It is not worth
    doing for Access Management, which has 13 projects on EF Core and Postgres and sync jobs.
  - **(b) Run the real images:** feasible for the PDP alone (`ghcr.io/altinn/altinn-auth/altinn-authorization`
    is publicly pullable). It needs Azurite plus a set of PIP shims in localtest. Running real Access Management
    or Resource Registry as well would need Postgres plus a Register v2 stream emulation that localtest does not
    have. That is the hard blocker.
  - **Recommendation:** treat the PDP as the only realistic candidate. Either run it as an image or vendor it,
    with localtest serving the PIP contracts from testdata. Keep Access Management and Resource Registry
    emulated in localtest.

## 1. Where production code lives today

| Capability | Repo / path | Notes |
| --- | --- | --- |
| PDP `POST authorization/api/v1/decision` | [`altinn-auth/src/apps/Altinn.Authorization/src/Altinn.Authorization/Controllers/DecisionController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Controllers/DecisionController.cs) (796 lines) | The endpoint has no `[Authorize]`, because the cluster network is the trust boundary. `POST authorization/api/v1/authorize` is the external variant and needs the `AUTHORIZESCOPEACCESS` scope. |
| `authorization/api/v1/parties`, `parties/{id}/validate` | [`Controllers/PartiesController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Controllers/PartiesController.cs) | `[Authorize]` (JWT cookie or bearer). Delegates to Access Management `authorizedparties` / `authorizedparty/{id}` and forwards the user's token ([`Services/Implementation/AccessManagementWrapper.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/AccessManagementWrapper.cs)). |
| `authorization/api/v1/roles` | [`Controllers/RolesController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Controllers/RolesController.cs) + [`RolesWrapper.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/RolesWrapper.cs) | Still calls the **SBL Bridge** `authorization/api/roles?coveredByUserId&offeredByPartyId`, although Altinn 2 is shut down (ADR-0001, [altinn-auth#3851](https://github.com/Altinn/altinn-auth/issues/3851)). This endpoint is effectively legacy. |
| PRP, PIP, PAP | [`Services/Implementation/PolicyRetrievalPoint.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/PolicyRetrievalPoint.cs), [`ContextHandler.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/ContextHandler.cs) (932 lines), [`DelegationContextHandler.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/DelegationContextHandler.cs), [`PolicyAdministrationPoint.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/PolicyAdministrationPoint.cs), [`Repositories/PolicyRepository.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Repositories/PolicyRepository.cs) (Azure Blob), [`Repositories/InstanceMetadataRepository.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Repositories/InstanceMetadataRepository.cs) (Storage authinfo) | `PolicyController` `POST authorization/api/v1/policies?org&app` (`studio.designer` claim) writes the app policy to blob. This is how a deploy publishes policy.xml. |
| Access Management (instance delegation, authorized parties, PIP) | [`altinn-auth/src/apps/Altinn.AccessManagement/src/...`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src) | [`Altinn.AccessManagement/Controllers/ResourceOwnerAPI/AppsInstanceDelegationController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Controllers/ResourceOwnerAPI/AppsInstanceDelegationController.cs) (`accessmanagement/api/v1/app/delegations|delegationcheck|delegationrevoke/resource/{r}/instance/{i}`, policy `PLATFORM_ACCESS_AUTHORIZATION`, `PlatformAccessToken` header); [`Api.Enduser/Controllers/AuthorizedPartiesController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Api.Enduser/Controllers/AuthorizedPartiesController.cs) (`accessmanagement/api/v1/enduser/authorizedparties`); [`Altinn.AccessManagement/Controllers/InternalAuthorizedPartiesController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Controllers/InternalAuthorizedPartiesController.cs) (`authorizedparties`, `authorizedparty/{partyId}`); [`Api.Internal/Controllers/PolicyInformation/PolicyInformationPointController.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Api.Internal/Controllers/PolicyInformation/PolicyInformationPointController.cs) (`policyinformation/getdelegationchanges`, `policyinformation/roles-and-accesspackages`). |
| Resource Registry | [`altinn-auth/src/apps/Altinn.ResourceRegistry/src/{Altinn.ResourceRegistry,.Core,.Persistence,.Integration}`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry/src) | Routes `resourceregistry/api/v1/...` and `api/v2/resource`. Postgres plus Azure Blob for policies. [`conf.json`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry/conf.json) has `dis.deploy=false`, so it is not yet deployed through altinn-auth's Flux pipeline; how it is deployed today is not confirmed. |
| NuGet packages | [`altinn-auth/src/pkgs/Altinn.Authorization.ABAC`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs/Altinn.Authorization.ABAC) (0.3.0), [`src/pkgs/Altinn.Authorization.PEP`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs/Altinn.Authorization.PEP) (PackageId `Altinn.Common.PEP`, 5.0.0), [`src/pkgs/JWTCookieAuthentication`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/pkgs/JWTCookieAuthentication) (4.0.4) | Released with release-please ([`.release-please-manifest.json`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/.release-please-manifest.json)). NuGet.org latest: ABAC 0.3.0, PEP 5.0.0. Localtest and the app backend are on ABAC 0.1.1 and PEP 4.2.3. |

**Deployment.** Each vertical has a [`Dockerfile`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/Dockerfile) or [`docker-bake.hcl`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/docker-bake.hcl), [`manifests/deploy/{base,at22..prod}`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/manifests/deploy)
(Kustomize, namespace `product-authorization`, `linkerd` injected, port 8080, `/health/`), and
[`syncroot/<env>`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/syncroot) for Flux ([`.github/workflows/publish-flux-artifacts.yml`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/.github/workflows/publish-flux-artifacts.yml), [`deploy-release-app.yml`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/.github/workflows/deploy-release-app.yml)).
Configuration comes from env files plus Azure App Configuration (`Altinn__AppConfiguration__Endpoint`) and
Key Vault (`kvSetting`).

Image names and whether they can be pulled anonymously (checked with an anonymous ghcr token):

- `ghcr.io/altinn/altinn-auth/altinn-authorization`: public. Tags are short git SHAs, for example `fb8c898`. The
  deployment manifest references `.../authorization:{{GIT_SHA}}`, which is not anonymously readable.
- `ghcr.io/altinn/altinn-auth/access-management`: public.
- Resource Registry image (`resource-registry-*` by bake config): not found or not public. Not confirmed.

**What the PDP uses as its PRP:**

- App policies: Azure Blob `AzureStorageConfiguration.Metadata*` container `metadata`, path
  `{org}/{app}/policy.xml` ([`Helpers/PolicyHelper.GetAltinnAppsPolicyPath`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Helpers/PolicyHelper.cs)). Cached `PolicyCacheTimeout` minutes
  (default 10).
- Resource policies: Resource Registry `GET resource/{id}/policy` ([`ResourceRegistryWrapper.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/ResourceRegistryWrapper.cs)).
- Delegation policies: the blob container `delegationpolicies`, read at the path and version id that Access
  Management returns.

**What the PDP uses as its PIP** ([`appsettings.json`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/appsettings.json) `PlatformSettings`, `GeneralSettings`):

| Data | Endpoint called | Config key |
| --- | --- | --- |
| Roles and access packages | AM `GET policyinformation/roles-and-accesspackages?from={resourcePartyUuid}&to={subjectPartyUuid}&accessRestriction=&viaParty=` → `{roles:[urn:altinn:rolecode:*, urn:altinn:external-role:ccr:*...], accessPackages:[urn:altinn:accesspackage:*]}` | `PlatformSettings:ApiAccessManagementEndpoint` |
| Delegations | AM `POST policyinformation/getdelegationchanges` | same |
| Parties for the parties endpoint | AM `authorizedparties`, `authorizedparty/{id}` (user token forwarded) | same |
| Subject's user profile and party UUID | Profile `POST internal/user` body `{UserId}` or `{Ssn}` | `PlatformSettings:ApiProfileEndpoint` |
| Parties | Register v1 `parties/partylist`, `parties/partylistbyuuid`, `parties/lookup`, `parties/{id}` | `PlatformSettings:ApiRegisterEndpoint` |
| Instance task and process info | Storage `GET instances/{id}/process/authinfo` | `PlatformSettings:ApiStorageEndpoint` |
| Resource metadata, access lists | RR `resource/{id}`, `resource/{id}/policy`, `access-lists/memberships?party&resource`, `access-lists/get-by-member` | `PlatformSettings:ApiResourceRegistryEndpoint` |
| OED (digitalt dødsbo) roles | external OED API via Maskinporten | `GeneralSettings:OedAuthzApiEndpoint`, `OedAuthzMaskinportenClientSettings` |
| Legacy roles endpoint only | SBL Bridge | `GeneralSettings:BridgeApiEndpoint` |

ADR [`altinn-auth/src/apps/Altinn.Authorization/docs/adr/0001-access-management-as-pip.md`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/docs/adr/0001-access-management-as-pip.md) documents this
design. **Access Management gets its role data** by syncing from Register v2
(`/register/api/v2/internal/parties/stream` and
`/register/api/v2/internal/parties/external-roles/assignments/events/stream`, see
[`src/libs/Altinn.Authorization.Integration/.../Register/*StreamEndpoint.cs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/Register),
[`AccessMgmt.Core/HostedServices/Services/{PartySyncService,RoleSyncService,ResourceSyncService}.cs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/HostedServices/Services)), into its
Postgres `authorizationdb`.

## 2. Provenance and drift of localtest files

History: the files were first copied in "Added PDP in local test platform" ([#3368](https://github.com/Altinn/altinn-studio/issues/3368), 2019-12). They were refreshed
in "Matching authorization" ([app-localtest#25](https://github.com/Altinn/app-localtest/issues/25), 2023-02). Since 2025 they carry localtest-only features: system
user ([#141](https://github.com/Altinn/app-localtest/issues/141)), instance delegation ([#146](https://github.com/Altinn/app-localtest/issues/146), [#152](https://github.com/Altinn/app-localtest/issues/152), [#154](https://github.com/Altinn/app-localtest/issues/154)), and test containers ([#136](https://github.com/Altinn/app-localtest/issues/136)). The localtest subtree was
imported into the monorepo in 2025-09 (e7f38bd6a7).

Comparison method: lines after trimming whitespace, counting the unique non-empty localtest lines that also appear
in the other file. "Archive" is [`.reference/altinn-authorization-archive/src/Authorization`](https://github.com/Altinn/altinn-authorization-archive/tree/40fcc2837ced4b536bf684195e438345a6ef33c3/src/Authorization). "Live" is
[`altinn-auth/.../Altinn.Authorization`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization). The earlier-session files `.reference/authz/DecisionController.cs` and
`.reference/pep/DecisionHelper.cs` are byte-identical in content to live altinn-auth main.

| Localtest file ([`src/Runtime/localtest/src/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src)) | Lines | Shared w/ archive | Shared w/ live | Nature of local change |
| --- | --- | --- | --- | --- |
| [`Controllers/Authorization/DecisionController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authorization/DecisionController.cs) | 680 | 81/393 (21 %) | 78/393 (20 %) | Rewritten. Keeps the XACML JSON/XML request handling. Delegation path replaced by: an in-memory synthesised policy for system users from [`testdata/authorization/systems`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/systems) (`AuthorizeUsingSystemUser`); instance delegations from `LocalInstanceDelegationsRepository` (JSON files); audit dump to `{storage}/authorization/*.json`. Production instead calls AM PIP, access lists, the access-package feature flags and OED. |
| [`Services/Authorization/Implementation/ContextHandler.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/ContextHandler.cs) | 432 | 171/216 (79 %) | 142/216 (66 %) | Closest to a true copy, but from before the move to Access Management as PIP. It calls `IRoles` (testdata) with `userId`/`partyId` ints. Live calls AM with party **UUIDs**, plus access packages, system users, OED and access lists. |
| [`.../PolicyRetrievalPoint.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/PolicyRetrievalPoint.cs) | 101 | 18/58 | 14/58 | Local. App policy fetched **live from the running app** (`{org}/{app}/api/v1/meta/authorizationpolicy`, 5 s cache, [`Services/LocalApp/Implementation/LocalAppHttp.cs:77`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/LocalApp/Implementation/LocalAppHttp.cs#L77)) with `[org]`/`[app]` placeholder replacement. Resource policies come from [`testdata/authorization/resources/policies/{id}.xml`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/resources/policies). |
| [`.../RolesWrapper.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/RolesWrapper.cs) | 40 | 12/24 | 12/24 | Reads [`testdata/authorization/roles/User_{u}/party_{p}/roles.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/roles) via `TestDataService`. |
| [`.../PolicyInformationRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/PolicyInformationRepository.cs) | 57 | 18/29 | 9/29 | Reads the instance from local storage (`IInstanceRepository`), not authinfo over HTTP. |
| [`.../PartiesService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/PartiesService.cs), [`ClaimsService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Implementation/ClaimsService.cs) | 46, 27 | – | – | Local only, from [`testdata/authorization/partylist`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/partylist), [`claims`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/claims). |
| [`Controllers/Authorization/PartiesController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authorization/PartiesController.cs) | 70 | 30/38 | 24/38 | Old `?userId=` contract backed by testdata. Live goes through AM. |
| [`Controllers/Authorization/RolesController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authorization/RolesController.cs) | 60 | 31/35 | 31/35 | Near copy. |
| [`Constants/Authorization/XacmlRequestAttribute.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Constants/Authorization/XacmlRequestAttribute.cs), [`Models/Authorization/XacmlResourceAttributes.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Authorization/XacmlResourceAttributes.cs) | 99, 59 | 43/44, 27/28 | 42/44, 26/28 | Archive copies. Live added about 40 lines each (party UUID, access restriction, via-party). |
| [`Services/Authorization/Constants/AltinnXacmlConstants.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authorization/Constants/AltinnXacmlConstants.cs) | 117 | 38/51 | 48/51 | Copy, behind live. |
| [`Constants/Authorization/AuthzConstants.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Constants/Authorization/AuthzConstants.cs) | 50 | 8/21 | 8/21 | Mostly local. |
| [`Models/Authorization/XacmlRequestApiModelBinder.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/Authorization/XacmlRequestApiModelBinder.cs) | 58 | 32/35 | 28/35 | Copy. |
| [`Controllers/AccessManagement/AppsInstanceDelegationController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/AccessManagement/AppsInstanceDelegationController.cs) | 178 | – | 23/75 vs live 315 | Emulation: route and DTO shapes only. No platform-token check and no delegation check. Saves JSON files. |
| [`Controllers/AccessManagement/AuthorizedPartiesController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/AccessManagement/AuthorizedPartiesController.cs) | 79 | – | 10/65 | Local emulation of `enduser/authorizedparties` from testdata `partylist`. Roles, packages and resources are always `[]`. |
| [`Services/AccessManagement/LocalInstanceDelegationsRepository.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/AccessManagement/LocalInstanceDelegationsRepository.cs) | 97 | – | – | Local file store. |
| [`Controllers/ResourceRegistry/ResourceController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/ResourceRegistry/ResourceController.cs) | 252 | – | 77/127 vs live 656 | Old subset (get, put, post, delete, policy, search). No access lists and no v2. |
| [`Services/ResourceRegistry/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/ResourceRegistry) | 394 | – | `ResourceRegistryService` 12/42, `IResourceRegistry` 22/42 | `PolicyRepositoryMock` and `RegisterResourceRepositoryMock` are adapted from **RR unit-test mocks** ([`test/Altinn.ResourceRegistry.Tests/Mocks/`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry/test/Altinn.ResourceRegistry.Tests/Mocks)). `PolicyRepositoryMock` still resolves `../../../Data/ResourcePolicies` relative to the assembly, a test-project path. |
| [`Models/ResourceRegistry/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Models/ResourceRegistry) | 293 | – | `ServiceResource` 20/45 | Old model subset. |

Package drift: localtest uses ABAC 0.1.1 and PEP 4.2.3, while current releases are 0.3.0 and 5.0.0. The worktree
[`.worktrees/studio-systemuser-authlevel`](https://github.com/ivarne-agent/altinn-studio/tree/feat/systemuser-authlevel-obligation) (commit 7b72c9d193) bumps both and changes `DecisionController`
(+48 lines) for the system-user authentication-level obligation. Main is unchanged.

The kinds of local modification fall into four groups:

- **Roles, parties and claims:** read from [`testdata/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata) JSON merged with the app's `testData.json`.
- **Delegations:** system-user policies are synthesised in memory, and instance delegations are kept as JSON files.
- **Policies:** app policies are fetched from the running app, and resource policies are read from testdata XML.
- **Storage:** instance data is read directly from local storage.

## 3. What running the real services as containers needs

### PDP (`altinn-authorization`): the realistic one

**Infrastructure:**

- **No database**, since [altinn-auth#4311](https://github.com/Altinn/altinn-auth/issues/4311) removed it.
- **Azure Blob, which Azurite can stand in for:** containers `metadata` and `delegationpolicies`, configured in
  `AzureStorageConfiguration`. appsettings.json already defaults to Azurite on `127.0.0.1:10000`.
- **Azure Storage Queue** for the audit log: only when `FeatureManagement:AuditLog=true`, which defaults to
  false.
- **Not required:**
  - Key Vault and Application Insights: skipped when `kvSetting` is empty ([`Program.cs:146`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L146)).
  - Azure App Configuration: only used through env vars.
  - Redis/Valkey: the PDP uses `IMemoryCache`.

**Inbound auth:**

- `decision` has no authorization attribute.
- `parties` and `roles` need a JWT cookie or bearer validated against `GeneralSettings:OpenIdWellKnownEndpoint`.
  Localtest already serves `authentication/api/v1/openid/.well-known/openid-configuration`, and HTTPS metadata is
  relaxed only when `ASPNETCORE_ENVIRONMENT=Development`.
- `policies` POST needs the `urn:altinn:app=studio.designer` claim.
- `accesslist` needs a platform access token (`AddPlatformAccessTokenSupport`); how the validation certificates
  are sourced locally is not confirmed.

**Outbound config:**

- `PlatformSettings__ApiRegisterEndpoint|ApiStorageEndpoint|ApiResourceRegistryEndpoint|ApiAccessManagementEndpoint|ApiProfileEndpoint`
- `GeneralSettings__OpenIdWellKnownEndpoint`, `BridgeApiEndpoint`, `OedAuthzApiEndpoint`, `PolicyCacheTimeout`,
  `RoleCacheTimeout`
- `OedAuthzMaskinportenClientSettings`: registered at startup with an empty client. Whether that breaks startup
  or only OED-role decisions is not confirmed.

**PIP shims localtest would need to serve from testdata** so the real PDP can decide for fake users. Localtest
already proxies by path, so these could be routes in localtest:

| Endpoint the PDP calls | Localtest today | Needed shim |
| --- | --- | --- |
| Profile `POST profile/api/v1/internal/user` `{UserId}` / `{Ssn}` | **missing** (only `GET users/{id}`, `users/current`) | Return `UserProfile` incl. `party.partyUuid` from [`testdata/Profile`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Profile). |
| Register `POST parties/partylist`, `POST parties/lookup`, `GET parties/{id}` | present | – |
| Register `POST parties/partylistbyuuid` | **missing** | Lookup by UUID. Testdata parties must carry stable `partyUuid`s. |
| AM `GET policyinformation/roles-and-accesspackages?from&to` | **missing** | Map UUIDs to ints, read [`testdata/authorization/roles/User_{u}/party_{p}`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/roles), and emit `urn:altinn:rolecode:{value}` (and optionally ccr URNs). The testdata keys roles by **userId**, while the PDP sends the subject's **party UUID**, so a userId to partyUuid map from Profile testdata is needed. Whether role-code comparison against app policies (`DAGL` vs AM's lowercase `dagl`) is case-insensitive in ABAC is not confirmed. |
| AM `POST policyinformation/getdelegationchanges` | **missing** | Return `[]`, or translate local instance and system-user delegations into delegation-change rows plus policy XML in Azurite `delegationpolicies` at a **blob version id**. Azurite support for blob versioning is not confirmed, which is a risk for delegations. |
| AM `GET authorizedparties`, `authorizedparty/{id}` (for `/parties` endpoints) | **missing** (only `enduser/authorizedparties`) | Same data as the existing emulation, in the internal DTO shape. |
| Storage `GET instances/{id}/process/authinfo` | present ([`Controllers/Storage/ProcessController.cs:411`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/ProcessController.cs#L411)) | – |
| RR `resource/{id}`, `resource/{id}/policy` | present (from testdata) | Verify DTO compatibility with the live `ServiceResource`. |
| RR `access-lists/memberships`, `access-lists/get-by-member` | **missing** | Return empty lists. |
| Blob `metadata/{org}/{app}/policy.xml` | n/a | Upload each app's policy on app registration or start, for example by calling PDP `POST policies?org&app` with a designer token. Localtest today does `[org]`/`[app]` placeholder replacement itself; whether Designer does this before upload in production is not confirmed. |

**Semantic loss compared with localtest today:**

- **Policy edits.** Edits to `policy.xml` no longer apply within 5 s: the PDP caches for whole minutes
  (`PolicyCacheTimeout` minimum 1 minute). A setting of 0 is likely invalid for `SetAbsoluteExpiration`; not
  confirmed.
- **System users.** Localtest's synthesised policies have no production equivalent. Production uses access packages
  and delegations from Access Management. The [`testdata/authorization/systems`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/systems) actions would have to become AM
  delegation rows or packages.
- **Audit files.** The localtest audit files would disappear. No consumer was found in the monorepo.

### Access Management: not realistic locally

**Requirements:**

- **Postgres `authorizationdb`** with roles `platform_authorization` and `platform_authorization_admin`, plus an
  init run for EF migrations and seed data (`--RunInitOnly=true`).
- **Azure Blob:** containers `metadata`, `delegationpolicies` and `resourceregistry`.
- **Service endpoints:** Authentication, Authorization, Profile, Register and Resource Registry, plus
  `OidcProviders__*` and `SblBridgeSettings`.
- **Hosted sync jobs** feeding its database from Register v2 streams and the Resource Registry
  (`AccessMgmt.Core.HostedServices.RegisterSync`, `ResourceRegistrySync`).
- **Platform access tokens** for the app-instance delegation endpoints.

**Blocker:** localtest's Register is v1 only. Without the v2 party and role-assignment streams, Access Management
knows no parties or roles, so seeding it from testdata means writing into its EF schema directly.

### Resource Registry

It needs Postgres ([`compose.yml`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.ResourceRegistry/compose.yml)), Azure Blob, the Register client, the org list from altinncdn, and `OidcProviders`.
That is feasible, but it brings little value: localtest only needs resource and policy lookups for a few testdata
resources (`altinn_keyrole_access`, `altinn_access_management`, `Appid_*`). It also has no public image (not
confirmed) and is not yet deployed through altinn-auth Flux.

## 4. App runtime callers

Paths are under [`altinn-studio/src/App/backend/src/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src) unless noted. `app-lib-dotnet` mirrors this layout.

| Call | Where | Hot path? |
| --- | --- | --- |
| PDP `decision` via PEP `AppAccessHandler` (`InstanceRead`, `InstanceWrite`, `InstanceDelete`, `InstanceInstantiate`, `InstanceComplete` policies) | [`Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:513-531`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs#L513-L531). Used on `DataController` (7 actions), `DataTagsController`, `InstancesController`, `ProcessController`, `UserDefinedMetadataController`, `OptionsController`, `DataListsController`, `FormBootstrapController`, `ExternalApiController` | **Yes:** one decision per authorized app API request. Localtest Storage's own controllers ([`Controllers/Storage/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage) `[Authorize(Policy=Instance*)]`, [`Startup.cs:114`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L114) `PDPAppSI`) call the PDP again, so a single app data save costs at least 2 decisions. There is no decision cache in the PEP. |
| `AuthorizeAction` (single) | [`Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs:137`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs#L137), from [`Internal/Process/ProcessEngineAuthorizer.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Process/ProcessEngineAuthorizer.cs) (process next: one call per candidate action, for example `write`, `confirm`, `sign`, `reject`, `complete`) and [`Internal/Data/DataElementAccessChecker.cs:293`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Data/DataElementAccessChecker.cs#L293) | Per process/next and per data-type access check. |
| `AuthorizeActions` (multi-decision) | [`AuthorizationClient.cs:174`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs#L174), from [`Internal/Process/ProcessStateEnricher.cs:75`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Process/ProcessStateEnricher.cs#L75), `ActionsController`, `InstancesController` | Every `GET process` and instance load in the frontend, so effectively per page. |
| `GetKeyRoleOrganizationParties` (multi-decision on resource `altinn_keyrole_access`) | [`AuthorizationClient.cs:201`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs#L201), from [`Features/Signing/Services/SigningService.cs:212`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Signing/Services/SigningService.cs#L212) | Signing tasks only. Needs [`testdata/authorization/resources/policies/altinn_keyrole_access.xml`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/authorization/resources/policies/altinn_keyrole_access.xml). |
| Authorized parties | Monorepo backend: AM `enduser/authorizedparties` ([`AuthorizationClient.cs:78`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs#L78), changed in [#20682](https://github.com/Altinn/altinn-studio/issues/20682) on 2026-10-07). app-lib-dotnet v8: `authorization/api/v1/parties?userid=` and `parties/{id}/validate` ([`app-lib-dotnet/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs:78,103`](https://github.com/Altinn/app-lib-dotnet/blob/4740bb28ea13e775c13ca2a2a19942877acf2be1/src/Altinn.App.Core/Infrastructure/Clients/Authorization/AuthorizationClient.cs#L78)) | Lazily per request in [`Features/Auth/AuthenticationContext.cs:130,149`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Auth/AuthenticationContext.cs#L130) (party selection, instantiation, current party). |
| Instance delegation (sign delegation) | [`Infrastructure/Clients/AccessManagement/AccessManagementClient.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/AccessManagement/AccessManagementClient.cs), [`Internal/AccessManagement/Helpers/UrlHelper.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/AccessManagement/Helpers/UrlHelper.cs) (`app/delegations/...`, `app/delegationrevoke/...`), from [`Features/Signing/Services/SigningDelegationService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Signing/Services/SigningDelegationService.cs) | Signing setup only. |
| `authorization/api/v1/roles` | No caller found in the app backend | No. |

## 5. Verdict

**(a) Subtree merge of `altinn-auth` and compiling the real code into localtest:**

- *PDP:* feasible but mid-sized.
  - You would take [`Altinn.Authorization`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization) (about 4.3k lines) plus its dependency closure: the [`Api.Contracts`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs/Altinn.Authorization.Api.Contracts)
    lib, and the ABAC and PEP packages, which could come from NuGet instead.
  - You would replace `IAccessManagementWrapper`, `IProfile`, `IRegisterService`, `IResourceRegistry`,
    `IInstanceMetadataRepository`, `IPolicyRepository` and `IOedRoleAssignmentWrapper` with testdata-backed
    implementations. They are all interfaces registered in [`Program.cs:190-222`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L190-L222), so this is clean.
  - Problems:
    - Subtree of a whole monorepo: altinn-auth is large, and a path-filtered subtree needs `git subtree split` on
      their side.
    - Namespace and package conflicts in one process: `Altinn.Platform.Authorization.*` types collide with
      localtest's forked copies, and `Microsoft.Extensions.Configuration.AzureKeyVault` is deprecated.
    - Localtest-only behaviour still has to live somewhere: system-user synthesis and instance delegation would
      become an `IAccessManagementWrapper` implementation.
    - Production's `DecisionController` is not designed as a library.
  - Effort: about 1-2 weeks for the PDP, plus ongoing merge pulls.
- *Access Management and Resource Registry:* not sensible. They are EF Core and Postgres heavy with sync jobs,
  and localtest uses less than 5 % of their surface.

**(b) Running real images through studioctl:**

- *PDP:* feasible.
  - Add an `authorization` component, the public image `ghcr.io/altinn/altinn-auth/altinn-authorization:<sha>`,
    plus an Azurite container.
  - Route `/authorization/` to it in the bound topology and in `ProxyMiddleware`.
  - Point its `PlatformSettings` back at localtest.
  - Implement the 6-7 shim endpoints in §3 in localtest from testdata.
  - Publish app policies to Azurite on app discovery.
  - Effort: about 1-2 weeks, plus pinning and testing image tags (only short SHAs are published, and there are
    no semver tags; not confirmed).
  - Debuggability is worse than in-process, and every decision becomes 3-6 extra HTTP hops on the hottest path in
    local dev (Profile, Register, AM PIP and Storage authinfo, mostly cached after the first call).
- *Access Management and Resource Registry as images:* **blocked** in practice:
  - Access Management needs Register v2 party and role streams and its own Postgres schema to know about any user
    or role.
  - The Resource Registry image is not publicly available (not confirmed).
  - Both need Postgres, Azurite and platform-token plumbing.

**Hard blockers and risks:**

1. The real PIP for roles is Access Management's database. Whatever approach is chosen, localtest must emulate
   `policyinformation/roles-and-accesspackages` and `getdelegationchanges` contracts from testdata, which are
   internal and unversioned APIs (`[ApiExplorerSettings(IgnoreApi = true)]`).
2. Testdata keys roles by userId and partyId ints, while production keys by party UUID. All testdata users and
   parties need stable UUIDs.
3. Localtest's system-user and instance-delegation semantics have no direct production analogue without Access
   Management delegation data and blob-versioned policies.
4. Policy hot reload (5 s today) becomes minute-granular.
5. The image tagging and visibility policy is not under Studio's control.

**Recommendation:** if anything, replace only the PDP. Prefer (b) with localtest-served PIP shims, which keeps
Register and the login page intact. As an intermediate step, update localtest's `ContextHandler` and
`DecisionController` toward the live PIP shape (UUIDs, access packages, ABAC 0.3.0 / PEP 5.0.0). Keep Access
Management and Resource Registry emulated.

## Unlinked references

- `.reference/authz/DecisionController.cs` and `.reference/pep/DecisionHelper.cs` are local copies from an earlier
  session and are not files in any repository.
