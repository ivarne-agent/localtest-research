# Resource Registry for the inbox stack

**Superseded 2026-10-23 by task 26:** the provisioner, step 3b included, is the localtest container (`PUT localtest/api/apps/{org}/{app}`), not studioctl-server; see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

## Status (2026-10-21)

- **Implemented** as task 5 (2026-10-20): 5a `e0cba40..b6f4c47` (component, seed, studioctl runtime component and
  provisioning call), 5b `9b7278d..aaa099e` (Access Management and the PDP on the production clients, stand-ins
  removed), in [`../../localtest/components/resource-registry/`](../../localtest/components/resource-registry/README.md).
  Smoke test 43 ok on 2026-10-21. Option A as recommended, with two additive hooks in `ResourceRegistryHost.cs`.
- Decided otherwise (task 5): the 10-minute application cache is gone locally (a fake replaces the Storage
  applications client) instead of a `POST resourceregistry/api/localtest/refresh` endpoint; non-app resources are a
  built-in seed in the registry's fakes, applied at start, not pushed by the runner; a separate `resourceregistrydb` on
  the shared Postgres 18; the org list read from altinncdn.no (task 3 won't do); the registry is the only source of
  `urn:altinn:resource` policies. Later seed additions: Correspondence's resources (task 7) and Profile's
  organisation-address resource (task 14).

Superseded statements:

- Sections 3.4 to 3.8: `LocaltestRefreshController`, the refresh call in `run-app.sh`, and compose services; the
  provisioner is studioctl-server (step 3b, `localtest/README.md`, "What the app runner pushes").
- Section 3.10, the 10-minute application cache row and Dialogporten's 20-minute cache "unless `DisableCache=true`":
  no cache locally, and Dialogporten's caches are synced and cleared by provisioning step 5 (task 9b).
- Postgres 16 throughout: the shared server is Postgres 18.

Research date: 2026-10-20. Strand `resource-registry` of the Dialogporten vendoring plan (shared brief:
`localtest-components/dialogporten-arbeidsflate.md`, 2026-10-18, re-verified here). Read-only: nothing was built or run;
the only runtime evidence is read-only HTTP against the running stack and `podman ps`. Claims inferred from code but not
observed are marked "not verified". This report decides and plans the Resource Registry only; Dialogporten, the
adapter and Arbeidsflate are planned by other strands, and section 6.2 states what they get from the recommended option.

## Pins

| Repo | Commit | What was read |
| --- | --- | --- |
| [`Altinn/altinn-resource-registry`](https://github.com/Altinn/altinn-resource-registry/tree/a3422419579832833845e47d3be57c148364bed0) | `a342241` (= `origin/main`, 2026-09-25) | Controllers, `ResourceRegistryService`, repositories, migrations, host, Dockerfile, CI |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` (main, 2026-10-16; `origin/main` is 5 commits ahead, none touch the files below: `git diff HEAD origin/main --stat` on the Resource Registry client, the sync commands and the Janitor is empty) | Resource Registry client, local-dev stand-in, sync commands, Janitor, search authorization |
| [`Altinn/dialogporten-frontend`](https://github.com/Altinn/dialogporten-frontend/tree/0011d957ddbd9d77e31fcce842fc41887297942c) | `0011d95` (5 ahead; only `packages.lock.json` changed under `serviceResources`) | BFF service-resource fetch, filters, refresh |
| [`Altinn/altinn-dialogporten-adapter`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/0a329856ba79101acdf54c29fd84a5063189a42d) | `0a32985` | Grep for Resource Registry use (none), `ttd` handling |
| [`Altinn/altinn-correspondence`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) | `6dcf561` | `ResourceRegistryService` and its DTOs |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810` (the pin of the vendored `access-management`, `authorization` and `authentication` components) | Access Management's two Resource Registry clients and `ResourceSyncService`, the PDP's `ResourceRegistryWrapper`, Authentication's `ResourceRegistryClient` |
| [`Altinn/altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) | `447e53e` | Designer's policy deploy client (the production push), the test apps' policies |
| This repo | worktree `research/dialogporten-vendoring` | `localtest/` (compose, runner, proxy routes, the Access Management and PDP stand-ins, reviews) |

## Summary

- **Every consumer is a production HTTP client of the same six anonymous endpoints**:
  `resource/resourcelist`, `resource/{id}`, `resource/{id}/policy`, `resource/{id}/policy/rights`, `resource/updated` and
  `resource/orgs` under `resourceregistry/api/v1/`. Dialogporten adds nothing exotic; its search authorization
  depends on the `updated` feed, a paginated stream of `(subjectUrn, resourceUrn, updatedAt, deleted)` pairs with an
  inclusive `since` and an opaque continuation token (section 1.2).
- **Production app resources are not stored in the Resource Registry.** `resourcelist` and `resource/app_{org}_{app}`
  are computed on request from Storage's application list (the earlier claim is confirmed, with the nuance that the
  app *policy* comes from the shared `metadata` blob container, not from Storage). The subject pairs behind the
  `updated` feed exist only after Altinn Studio Designer calls
  `GET resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` at deploy time (section 2). There is no sync job.
- **`ttd` has no organisation number on the CDN, and the production Resource Registry itself substitutes Digdir's**
  (`991825827`) for it ([`ServiceOwnerLookup.cs:42-46`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/ServiceOwners/ServiceOwnerLookup.cs#L42-L46)).
  The stack's convention (`ttd` ↔ `991825827` in Access Management's stand-in and in `/Home/GetTestOrgToken`) is
  therefore production behaviour, not a local invention.
- **Recommendation: Option A, vendor production `altinn-resource-registry` as `localtest/components/resource-registry/`.**
  It is a small, Aspire-free host (4 projects, about 19 800 lines of C#, ServiceDefaults 6.0.0 like Register, Yuniql
  migrations like Events, Postgres 16 works), with two directive sites, five small fakes and one shared volume: the
  `policy-store` volume the PDP and Access Management already share **is** its `metadata` container, so app policies
  need no extra push. The `updated` feed's semantics (monotonic timestamps, tombstones, continuation tokens) and the
  policy-rights flattening are exactly what a fake would have to re-implement, against five production clients. It
  retires about 310 lines of stand-ins in Access Management and 60 in the PDP. Effort 4 to 6 days.
- The app runner gains one production call after the policy push (what Designer does), and a seed step for the
  non-app resources Correspondence needs.

## 1. The consumer contract

### 1.1 Endpoints by consumer

All paths are relative to the platform base, `http://local.altinn.cloud:8000/` locally. "Anonymous" means the
production controller action carries no `[Authorize]`; the host has no fallback policy
([`ResourceRegistryHost.cs:97-116`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L97-L116)
defines named policies only).

| Consumer | Call | Auth | Response | What the consumer does with it |
| --- | --- | --- | --- | --- |
| **Dialogporten** `ResourceRegistryClient` ([L20](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L20), base `Infrastructure:Altinn:BaseUri`, [registered L532-534](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L532-L534)) | `GET resourceregistry/api/v1/resource/resourcelist?includeMigratedApps=true` ([L209-237](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L209-L237)) | anonymous | `List<ServiceResource>` ([model](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Models/ServiceResource.cs)); Dialogporten reads `identifier`, `resourceType`, `hasCompetentAuthority.{organization,orgcode}`, `title`, `description`, `delegable`, `status` and **drops entries whose `organization` or `orgcode` is empty or whose type is not `GenericAccessResource`/`AltinnApp`/`MigratedApp`/`CorrespondenceService`** ([L222-224](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L222-L224), [`Constants.SupportedResourceTypes`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/Constants.cs#L36-L42)). FusionCache `ResourceRegistry`, 20 min ([L192-198](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L192-L198)) | (a) `SetResourceType` on every dialog create/update: an unknown `serviceResource` fails validation ([`IServiceResourceAuthorizer.cs:91-108`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/IServiceResourceAuthorizer.cs#L91-L108)); (b) service-owner ownership: the token's `consumer` org number must own the resource ([`IUserResourceRegistry.cs:55-64`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/IUserResourceRegistry.cs#L55-L64)) unless the token has `digdir:dialogporten.serviceprovider.admin`; (c) org short name from org number ([L66-107](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/IUserResourceRegistry.cs#L66-L107), warns when one number has two short names, which `ttd`/`digdir` do); (d) `correspondenceservice` unopened-content rule ([`DialogUnopenedContent.cs:28-29`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/Common/DialogUnopenedContent.cs#L28-L29)); (e) the service-resource metadata catalogue the GraphQL `serviceResources` query and the BFF read |
| Dialogporten | `GET resourceregistry/api/v1/resource/updated?since={ISO-8601 "O"}&limit={batch}` then `links.next` as path+query until `data` is empty ([L90-111](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L90-L111)) | anonymous | `{ "links": { "next": url-or-null }, "data": [ { "subjectUrn", "resourceUrn", "updatedAt", "deleted" } ] }` ([`UpdatedResourceSubject`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Models/UpdatedResourceSubject.cs#L6-L26), [`Paginated<T>`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Models/Paginated.cs#L33-L45), `data` from [`ListObject<T>`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Models/ListObject.cs#L58-L59)) | `SyncSubjectMapCommand` merges the pairs into its `SubjectResources` table ([L45-91](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/ResourceRegistry/Commands/SyncSubjectMap/SyncSubjectMapCommand.cs#L45-L91)); search authorization intersects them with the roles and packages Access Management reports (section 1.2). Run by Janitor `sync-subject-resource-mappings [-s since] [-b batchSize]` ([`Commands.cs:19-32`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Commands.cs#L19-L32)) and, in `Development` with `LocalDevelopment:DisableSubjectResourceSyncOnStartup=false`, once at start of every host ([`DevelopmentSubjectResourceSyncHostedService.cs:27-38`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Development/DevelopmentSubjectResourceSyncHostedService.cs#L27-L38)) |
| Dialogporten | `GET resourceregistry/api/v1/resource/{id}/policy` for every resource in `resourcelist` except `se_*` ([L145-151](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L145-L151), [L179-189](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L179-L189); 15 concurrent by default) | anonymous | XACML 3.0 XML (`text/xml`); Dialogporten keeps the integer of the first obligation's `urn:altinn:minimum-authenticationlevel` assignment ([L191-207](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L191-L207)); a 404 or parse error is logged and the resource skipped | `SyncPolicyCommand` → `ResourcePolicyInformation` (minimum authentication level per resource), used to deny end users below the level ([`SyncPolicyCommand.cs:41-80`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/ResourceRegistry/Commands/SyncPolicy/SyncPolicyCommand.cs#L41-L80)). Janitor `sync-resource-policy-information [-s] [-c]`, and the matching Development hosted service. **The `since` is ignored**: "Until we have an API in RR to fetch updated resources only, we have to fetch them all" |
| **Arbeidsflate BFF** ([`service.ts:77`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/service.ts#L77)) | `GET {PLATFORM_BASEURL}/resourceregistry/api/v1/resource/resourcelist?includeAltinn2=false&includeApps=true&includeMigratedApps=true` (`includeAltinn2` is not a parameter upstream and is ignored) | anonymous (plain `fetch`, no token) | the same list, typed in [`registryTypes.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/registryTypes.ts); reduced to `{id, title, org (orgcode), resourceType, status}` | Fetched at start and every 6 hours ([`refresh.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/refresh.ts#L30-L40)), stored in Redis for 24 h, filtered to the four resource types, org codes `acn`/`bft` excluded, identifiers that are not valid resource URNs excluded, plus an environment exclusion list that defaults to `at23TestIds` for any platform URL that is neither prod nor tt02 ([`config.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/config.ts#L10-L33); 7 ids, none `app_ttd_*`). Feeds the inbox's service and organisation filters and the resource titles. An error yields an empty list and a log line, never a failure |
| **Adapter** | none. `grep -ri resourceregist src` is empty at `0a32985`. It only names `urn:altinn:resource:app_{org}_{app}` as the dialog's `serviceResource`, which Dialogporten validates against `resourcelist`; it maps `ttd` to `digdir`'s org number for the service-owner check ([`StorageDialogportenDataMerger.cs:327`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L327)) | | | |
| **Correspondence** `ResourceRegistryService` ([registered unconditionally](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L36), base `AltinnOptions:PlatformGatewayUrl`) | `GET resourceregistry/api/v1/resource/{id}` (id without the `urn:altinn:resource:` prefix, [L88](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs#L88)) | bearer Maskinporten token sent, not required | [`GetResourceResponse`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/GetResourceResponse.cs): `resourceType` (must be `CorrespondenceService`), `hasCompetentAuthority.{organization,orgcode,name}`, `title`; 404/204 → null; 10-minute hybrid cache | sender validation (`InitializeCorrespondenceValidationHelper`: type, owner org number), attachment init, notification texts (`GetResourceTitle`, `GetServiceOwnerOrgCode`) |
| Correspondence | `GET resourceregistry/api/v1/resource/{id}/policy/rights` ([L156](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs#L156)) | same | `List<PolicyRightsDTO>` ([upstream DTO](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Models/PolicyRightsDTO.cs), [Correspondence's view](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/GetResourceRightsResponse.cs)): per right, `subjects[].subjectAttributes[{type,value}]` | confidentiality: access package `post-til-virksomheten-med-taushetsbelagt-innhold` or `eksplisitt` among the subjects ([L175-195](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs#L175-L195)) |
| Correspondence | `GET resourceregistry/api/v1/resource/{id}/policy` ([L199-227](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs#L199-L227)) | same | XACML; **every** integer `AttributeValue` under an `ObligationExpression`, minimum taken; **throws when none** ([parser](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/ResourceRegistry/GetResourcePolicyAuthLevelResponse.cs#L9-L21)) | minimum authentication level of the correspondence; a seeded correspondence policy must carry the obligation |
| **Access Management**, production sync client `AltinnResourceRegistryClient` (Integration.Platform, `Platform:ResourceRegistry:Endpoint` = the proxy already) | `GET resourceregistry/api/v1/resource/orgs` ([`ServiceOwnersEndpoints.cs:14-36`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/ResourceRegistry/ServiceOwnersEndpoints.cs#L14-L36)) | anonymous | the CDN org list `{ "orgs": { "<code>": { name{en,nb,nn}, logo, orgnr, homepage, environments } } }`; the client sets `Id = MD5(code)` | `ResourceSyncService.SyncResourceOwners` → `dbo.provider` rows (`RefId` = orgnr, `Code`) ([L42-90](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/HostedServices/Services/ResourceSyncService.cs#L42-L90)) |
| Access Management, sync | `GET resourceregistry/api/v1/resource/updated?limit=100&since=yyyy-MM-ddTHH:mm:ss.fffzzz` (epoch when the lease has no `since`), then `links.next` verbatim ([`ResourceUpdatedEndpoint.cs:12-34`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/ResourceRegistry/ResourceUpdatedEndpoint.cs#L12-L34)) | anonymous | `PageStream` = `{stats, links{next}, data[]}` ([`ResourceUpdatedModel`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/ResourceRegistry/ResourceUpdatedEndpoint.cs#L40-L65); `stats` is optional for the deserializer) | `SyncResources`: per pair, `GET resourceregistry/api/v1/resource/{id}` ([`ResourceGetEndpoint.cs:12-26`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/ResourceRegistry/ResourceGetEndpoint.cs#L12-L26)) → upsert `dbo.resource` (needs a provider whose `Code` equals the resource's lower-cased `orgcode`, [L315-335](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/HostedServices/Services/ResourceSyncService.cs#L315-L335)); `urn:altinn:rolecode:*` subjects → `dbo.roleresource` by `ILIKE` on the role's `LegacyUrn`/`Urn`, `urn:altinn:accesspackage:*` → `dbo.packageresource`, other subjects ignored ([L158-170](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/HostedServices/Services/ResourceSyncService.cs#L158-L170)). The `app_{org}_{app}` row is what app instance delegations (signing) look up |
| Access Management, request-time `ResourceRegistryClient` (`PlatformSettings:ApiResourceRegistryEndpoint` = `.../resourceregistry/api/`) | `GET v1/resource/{id}`, `GET v1/resource/search?…`, `GET v1/resource/resourcelist`, `POST v1/resource/bysubjects` (body: list of subject URNs), `GET v2/resource/{id}/policy/rights` with `Accept-Language`, consent templates ([lines 52, 67, 88, 103, 143](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs#L52)) | anonymous | `ServiceResource`, `Paginated<SubjectResources>` ([model](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Models/SubjectResources.cs)), rights | delegation services; instance delegation uses `resourcelist` (the signing path verified in the Access Management plan) |
| **PDP** `ResourceRegistryWrapper` (`PlatformSettings:ApiResourceRegistryEndpoint` = `.../resourceregistry/api/v1/`) | `GET resource/{id}` ([L49-65](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/ResourceRegistryWrapper.cs#L49-L65)), `GET resource/{id}/policy` ([L68-89](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/ResourceRegistryWrapper.cs#L68-L89)) | anonymous | `ServiceResource`; XACML | the policy of a `urn:altinn:resource:*` decision (not apps: app policies come from the PDP's own store); the resource's `accessListMode` and party types in `ContextHandler` |
| PDP | `GET access-lists/memberships?party={urn}&resource={urn}`, `GET access-lists/get-by-member?party={urn}` ([L92-130](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/ResourceRegistryWrapper.cs#L92-L130)) | platform access token `platform`/`authorization` ([controller](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/AccessListMembershipsController.cs#L52-L53), [L131-132](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/AccessListMembershipsController.cs#L131-L132)) | `{ "data": [...] }` | access-list (RRR) decisions; empty locally |
| **Authentication** `ResourceRegistryClient` (`PlatformSettings:ApiResourceRegistryEndpoint`, [registered](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/AuthenticationHost.cs#L138)) | `GET resource/{id}` ([L58](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Integration/ResourceRegister/ResourceRegistryClient.cs#L58)), `GET resource/{id}/policy/rights` ([L84](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Integration/ResourceRegister/ResourceRegistryClient.cs#L84)) | anonymous | as above | system register and system-user change requests validate the rights they ask for ([`SystemRegisterService.cs:142`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/SystemRegisterService.cs#L142)). Not faked today: the proxy answers 404 |
| Storage, Events, Register | none (`grep -ril resourceregistry` over the vendored `src` is empty for all three) | | | |
| **Designer** (production push, for reference) | `GET resourceregistry/api/v1/resource/app_{org}_{app}/policy/subjects?reloadFromXacml=true` after deploying a policy ([`AltinnAuthorizationPolicyClient.cs:66`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Designer/backend/src/Designer/TypedHttpClients/AltinnAuthorization/AltinnAuthorizationPolicyClient.cs#L66)); `POST .../resource/bysubjects` in the resource admin ([`ResourceRegistryService.cs:499`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Designer/backend/src/Designer/Services/Implementation/ResourceRegistryService.cs#L499)) | anonymous | `Paginated<AttributeMatchV2>` | this is the step that makes an app's role subjects exist in the registry at all (section 2) |

Not in the stack and not planned by any strand, for completeness: Notifications and Profile also read the registry
(not checked here).

### 1.2 The `updated` feed and search authorization

How Dialogporten decides which app dialogs a user may list
([`AltinnAuthorizationClient.cs:247-292`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L247-L292),
[`AuthorizationHelper.cs:33-149`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AuthorizationHelper.cs#L33-L149)):

1. `POST accessmanagement/api/v1/resourceowner/authorizedparties?includeRoles=true&includeAccessPackages=true&includeResources=true&includeInstances=true`
   with the end user's identifier. The vendored Access Management serves it
   ([`AuthorizedPartiesController.cs:24-55`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Api.ServiceOwner/Controllers/AuthorizedPartiesController.cs#L24-L55);
   policy `POLICY_RESOURCEOWNER_AUTHORIZEDPARTIES`, feature gate `RightsDelegationApi`, which the stack already turns on).
   For each party it lists `authorizedRoles` as **both** the new code and the legacy code (`daglig-leder` and
   `dagl`, [`AuthorizedPartiesServiceEf.cs:411-418`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.Core/Services/AuthorizedPartiesServiceEf.cs#L411-L418),
   [`RoleConstants.cs:560-568`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.PersistenceEF/Constants/RoleConstants.cs#L560-L568)),
   `authorizedAccessPackages` as package codes, `authorizedResources` as resource ids, `authorizedInstances`.
2. Dialogporten turns them into subject URNs: `urn:altinn:rolecode:{code lower}` and
   `urn:altinn:accesspackage:{code lower}`
   ([`AuthorizedPartiesHelper.cs:89-97`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AuthorizedPartiesHelper.cs#L89-L97)).
3. It loads every `SubjectResource` row (`Subject`, `Resource`, case-insensitive) and, per party, unions the
   resources of the party's subjects, adds the party's direct `authorizedResources`, prunes to resources that some
   dialog of that party references, and the search filters dialogs by `(party, serviceResource)` pairs. Instance
   delegations (`authorizedInstances`) resolve to dialog ids separately and bypass the subject map.

The `SubjectResource` rows come only from the `updated` feed. For user U to list the dialogs of app `ttd/x` for party P:

- Access Management must grant U a role code or package on P (the stack does this from `testData.json` via the
  refresh), and
- the feed must have delivered the pair `subjectUrn = urn:altinn:rolecode:{that code}` (or `urn:altinn:accesspackage:…`),
  `resourceUrn = urn:altinn:resource:app_ttd_x`, `deleted = false`, exactly as the production registry derives it from
  the app policy's subject matches: every `urn:altinn:rolecode` and `urn:altinn:accesspackage` value in any rule's
  subject match, lower-cased ([`GetResourceSubjects`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Services/ResourceRegistryService.cs#L374-L412)).
  `urn:altinn:org:ttd` subjects are delivered too and harmlessly match no party.

Feed semantics, from [`ResourceController.cs:546-592`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceController.cs#L546-L592)
and [`ResourceRegistryRepository.cs:463-496`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/ResourceRegistryRepository.cs#L463-L496):

- `since` is a `DateTimeOffset`, inclusive: the SQL is `(updated_at, resource_urn, subject_urn) > (@since, @resource, @subject)`
  with empty URNs when there is no token, ordered by that tuple, `LIMIT limit + 1`. `limit` is 1 to 1000, default 1000.
- When more rows exist, `links.next` is an **absolute** URL built with `Url.Link` from the request's host
  (`?since={last.updatedAt:O}&token={opaque}&limit={limit}`), where the token is an opaque encoding of the last
  `(resourceUrn, subjectUrn)` pair so that rows sharing one `updated_at` are not skipped. Dialogporten follows only
  `PathAndQuery` ("to work around internal (non-APIM) URLs"), Access Management follows the URL as is; behind the
  proxy both work because the host is `local.altinn.cloud:8000` either way.
- Rows are `(resource_urn, subject_urn)` with `updated_at` (set to `NOW()` on insert, undelete and soft delete) and
  `deleted`. Removing a subject from a policy soft-deletes the pair; it comes back as `deleted: true` and Dialogporten
  marks its row deleted. Timestamps must be monotonic for Dialogporten's `since` (its max `UpdatedAt` + 1 µs,
  [`SubjectResourceRepository.cs:86-98`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Repositories/SubjectResourceRepository.cs#L86-L98);
  `0001-01-01` on an empty table) to be a correct cursor across registry restarts. This is the property a fake must
  persist (section 4).

### 1.3 Stale points in the 2026-10-18 report

- "How app subject mappings get there is not confirmed": now confirmed, Designer triggers it (section 2).
- "`ResourceController` reads apps from Storage": confirmed for metadata; the app policy comes from the metadata
  blob container (the PDP's), not from Storage.
- The report lists `resourcelist`, `updated`, `{id}/policy` and `resource/{id}` as the needed endpoints; `orgs`,
  `policy/rights`, `bysubjects`, `access-lists/*` and `policy/subjects?reloadFromXacml` must be added for the
  components already in the stack and for the runner.

## 2. How production gets app resources

Confirmed from the pinned source:

| Question | Answer | Evidence |
| --- | --- | --- |
| Where do `app_{org}_{app}` resources come from? | Computed on request. `resourcelist?includeApps=true` calls Storage `GET {PlatformSettings:StorageApiEndpoint}applications` (10-minute memory cache), drops `/a1-` and `/a2-` apps unless `includeMigratedApps`, and maps each `Application` to a `ServiceResource`; apps also registered as resources in the database (`app_` identifiers) win over the Storage copy | [`ResourceRegistryService.GetResourceList`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Services/ResourceRegistryService.cs#L196-L303), [`ApplicationsClient.cs:40-74`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Integration/Clients/ApplicationsClient.cs#L40-L74) |
| The mapping | `identifier = app_{org}_{app}`; `resourceType` `AltinnApp`, or `MigratedApp` when the id contains `_a1-`/`_a2-`; `title` = the app's title dictionary; `resourceReferences` = `[Altinn3/ApplicationId/{org}/{app}]`; `authorizationReference` = `urn:altinn:org`, `urn:altinn:app`; `hasCompetentAuthority.orgcode` lower-cased, `organization` and `name` from the CDN org list; `a1-` apps not delegable or visible | [`MapApplicationToApplicationResource`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Services/ResourceRegistryService.cs#L310-L347) |
| `GET resource/app_{org}_{app}` | Database first; on a miss, Storage `applications/{org}/{app}` directly (uncached, "so a freshly published app is returned immediately") | [`ResourceController.cs:89-117`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceController.cs#L89-L117), [`ApplicationsClient.cs:77-101`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Integration/Clients/ApplicationsClient.cs#L77-L101) |
| `GET resource/app_{org}_{app}/policy` | Reads `{org}/{app}/policy.xml` from the **metadata** blob container, the container the PDP's policy endpoint writes to | [`ResourceController.cs:260-305`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceController.cs#L260-L305), [`PolicyRepository.GetAppPolicyAsync`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/PolicyRepository.cs#L59-L66) |
| Where do the subject pairs come from? | Table `resourceregistry.resourcesubjects` (primary key `(resource_urn, subject_urn)`, `updated_at`, `deleted`). Written by `SetResourceSubjects`, which is called (a) when a policy is stored through `POST/PUT resource/{id}/policy` (scope `altinn:resourceregistry/resource.write` or `.admin`), and (b) by `GET resource/{id}/policy/subjects?reloadFromXacml=true`, which for an unregistered `app_{org}_{app}` reads the app policy from the metadata container and upserts the pairs, soft-deleting removed ones. **Designer calls (b) at every policy deploy.** There is no job, no Storage sync and no PDP hook | [migration v0.06](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Migration/v0.06-resourcesubjects/01-setup-tables.sql), [v0.08](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Migration/v0.08-resourcesubjects-timestamps-softdelete/01-add-columns.sql), [`SetResourceSubjects`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/ResourceRegistryRepository.cs#L541), [`UpdateResourceSubjectsFromAppPolicy`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Services/ResourceRegistryService.cs#L160-L181), [`ResourceController.cs:307-342`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceController.cs#L307-L342), Designer above |
| `resource/orgs` | The CDN `altinn-orgs.json` passed through unchanged (so `ttd.orgnr` is empty there) | [`ResourceOwnerController.cs`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceOwnerController.cs#L10-L33), [`OrgListClient`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Integration/Clients/OrgListClient.cs) |
| `ttd` | The CDN entry has `orgnr: ""` (fetched today, 108 orgs). `ServiceOwnerLookup.Create` substitutes `digdir`'s number for `ttd` when empty, so `app_ttd_*` resources carry `organization = 991825827, orgcode = ttd` and pass Dialogporten's filter | [`ServiceOwnerLookup.cs:42-46`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/ServiceOwners/ServiceOwnerLookup.cs#L42-L46) |

Consequence for the stack: Access Management's `LocaltestAppResources` already reproduces the mapping (Storage list +
policy subjects from `metadata/{org}/{app}/policy.xml` in the policy store). Vendoring the registry replaces that
reproduction with the code it copies.

## 3. Option A: vendor production `altinn-resource-registry`

### 3.1 What to copy, size, build chain

- Paths (relative layout preserved under `components/resource-registry/vendor/`): `src/apps/Altinn.ResourceRegistry/src/`
  (4 projects: host 7 935 lines, `Core` 7 694, `Persistence` 3 691 incl. the embedded `Migration/**/*.sql`,
  `Integration` 498), `src/apps/Altinn.ResourceRegistry/src/Directory.Build.props`, `src/Directory.Build.props`,
  `src/Directory.Build.targets`, `src/Directory.Packages.props` (central package management, like Register), `src/stylecop.json`,
  the root `Settings.StyleCop`, `global.json` (SDK `10.0.401`, `rollForward: latestMajor`, so the `sdk:10.0-alpine`
  image's SDK is fine), and `src/apps/Altinn.ResourceRegistry/Dockerfile` for reference. Not copied: `test/`,
  `compose.yml`, `conf.json`, `.github/`.
- No Aspire. The host is `AltinnHost.CreateWebApplicationBuilder("resource-registry", args)` from
  `Altinn.Authorization.ServiceDefaults` 6.0.0
  ([`ResourceRegistryHost.cs:45`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L45)),
  the same package the Register plan verified as configuration-only (Key Vault only with `kvSetting:SecretUri`,
  App Configuration only with `Altinn:AppConfiguration:Endpoint`, Azure Monitor only with a connection string).
- Postgres through `Altinn.Authorization.ServiceDefaults.Npgsql` + `.Yuniql` 6.0.0: `MapPostgreSqlConfiguration`
  turns `PostgreSQLSettings:{EnableDBConnection,AdminConnectionString,ConnectionString,AuthorizationDbAdminPwd,AuthorizationDbPwd}`
  into `ConnectionStrings:resource-registry_db`, `…_db_migrate` and `Altinn:Npgsql:resource-registry:Migrate:Enabled`
  ([L191-214](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L191-L214)),
  and `AddDatabase` registers the data source with the `resourceregistry.resourcetype` enum and the embedded Yuniql
  workspace ([`ResourceRegistryDependencyInjectionExtensions.cs:83-103`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Extensions/ResourceRegistryDependencyInjectionExtensions.cs#L83-L103)).
  Migrations run at start, as in Events. The grants target role `platform_authorization` and the migrator role
  `${YUNIQL-USER}` ([v0.00 grants](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Migration/v0.00-resource-registry/03-setup-grants.sql),
  [v0.05](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Migration/v0.05-default-privileges/01-setup-privileges.sql)),
  the same role names Access Management uses, and upstream's dev database is also `authorizationdb`
  ([`appsettings.json`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/appsettings.json#L23-L30)):
  in production the two services share one database and keep to their own schemas. Locally use a separate
  `resourceregistrydb`: both services run Yuniql with the default version table (the ServiceDefaults
  `VersionTableOptions` has null name and schema, so Yuniql's own default applies; sharing a database would make the
  two version histories collide; not verified by running it). Postgres 16 is enough: upstream's tests use
  `postgres:16.1-alpine` ([`DbFixture.cs:41`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/test/Altinn.ResourceRegistry.TestUtils/DbFixture.cs#L41))
  and no migration uses `uuidv7()` or an extension (`grep -ri 'uuidv7\|CREATE EXTENSION' Migration` is empty).
- Analyzers: StyleCop via `Directory.Build.props`/`stylecop.json`. `TreatWarningsAsErrors=false` in the props file,
  as for the other components; the fakes get `NoWarn` for `SA*`/`CS1591` (the host documents everything).

### 3.2 Dependencies

| Dependency | Production | Localtest | Note |
| --- | --- | --- | --- |
| Postgres | Yuniql migrations (10 versions: resources with version table, access lists, `resourcesubjects`, change feed) | **container**: shared Postgres 16, database `resourceregistrydb`, roles `platform_authorization_admin` (migrator) and `platform_authorization` (app), created by `postgres-init/40-resource-registry.sh` in the events script's style, idempotent | The roles already exist (Access Management's `LocaltestDatabase` creates them as superusers); the script must tolerate that. ServiceDefaults can also create the database itself from `ConnectionStrings:resource-registry_db_cluster` and an `Altinn:Npgsql:resource-registry:Create` section (seen in the decompiled package), not verified; the init script is the known path. No reset on start: the data is derived (apps) or seeded (resources), and `SetResourceSubjects` is idempotent |
| Azure Blob, two containers (`resourceregistry` for resource policies with versions and leases, `metadata` for app policies) | `PolicyRepository : IPolicyRepository`, 13 methods ([interface](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/IPolicyRepository.cs)) | **mock** `LocaltestFilePolicyRepository` on the shared `policy-store` volume: app policies at `{PolicyStorePath}/metadata/{org}/{app}/policy.xml`, **exactly where the PDP's production policy endpoint already writes them**; resource policies at `{PolicyStorePath}/resourceregistry/{id}/resourcepolicy.xml` plus `.versions/{versionId}` as Access Management's file repository does; lease = process-wide lock; `PolicyExists`, `TryDelete`, `DeleteVersion` on files | Yes, the `policy-store` volume backs its app policy reads with no extra push. Mount read-write (the production `POST resource/{id}/policy` for seeded resources writes there). The `Response<BlobContentInfo>` return types are Azure SDK types: build them with `Response.FromValue(BlobsModelFactory.BlobContentInfo(...), new LocaltestRawResponse(201))` where `LocaltestRawResponse : Azure.Response` is a 30-line stub. `AzureStorageConfiguration` is validated only when `IOptions` is resolved ([L72-78](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Persistence/Extensions/ResourceRegistryDependencyInjectionExtensions.cs#L72-L78)); keep the appsettings dummies |
| Storage (app list) | `IApplications → ApplicationsClient`, `PlatformSettings:StorageApiEndpoint`, anonymous | **container** (vendored Storage via the proxy): `StorageApiEndpoint = http://local.altinn.cloud:8000/storage/api/v1/` | The 10-minute list cache means a newly pushed app reaches `resourcelist` up to 10 minutes late (production behaviour; `resource/{id}` is immediate). Add a localtest-only `POST resourceregistry/api/localtest/refresh` that removes the `applications` memory-cache key, called by the runner |
| CDN org list | `IOrgListClient` → `ResourceRegistrySettings:OrgListEndpoint` (altinncdn.no), 1 h cache | **off**: point `OrgListEndpoint` at a copy of `altinn-orgs.json` served by the localtest app (the earlier report's P6; the adapter and Dialogporten read the same file) | The copy may keep `ttd.orgnr` empty: the registry substitutes Digdir's number itself. The `orgs` endpoint passes the file through, so Access Management's `dbo.provider` row for `ttd` gets `RefId = ""` as in production; its `LocaltestAltinnResourceRegistry` stand-in used to put `991825827` there. Nothing in the signing path reads `RefId` (not verified for every path) |
| Register (access lists) | `IRegisterClient` at `PlatformSettings:ApiRegisterEndpoint` with a platform access token `platform`/`authorization` ([`PlatformAccessTokenHandler.cs:32`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Integration/PlatformAccessTokenHandler.cs#L32)) | **container** (vendored Register). Signing uses `AccessTokenSettings:AccessTokenSigningCertificateFullPath`, already the compose `x-access-token-cert` | Only access-list endpoints use it; not on the inbox path. Which Register endpoint it calls was not checked |
| Key Vault | `kvSetting` → `KeyVaultSettings`; `IPublicSigningKeyProvider → PublicSigningKeyProvider` (Key Vault) for incoming platform tokens ([L57-67](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L57-L67)) | **mock**: the shared-certificate `IPublicSigningKeyProvider` (copy of the PDP's/Access Management's, 40 lines); `kvSetting:SecretUri` empty | `IKeyVaultService` exists in Core but has no registration (grep) |
| User tokens | `AddJwtCookie` with `OidcProviders:altinn`; `RequireHttpsMetadata=false` only in Development ([L83-90](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L83-L90)) | `ASPNETCORE_ENVIRONMENT=Development`; the `JWTCookieAuthentication` package fetches metadata with an HTTPS-only retriever, so copy Access Management's `LocaltestOidcMetadata` (35 lines) | Not verified that `JWTCookieAuthentication` 4.0.4 has the same retriever as the version Access Management uses; the write endpoints are the only ones that need it |
| Scopes on write endpoints | `ScopeAccessHandler` (`Altinn.Common.PEP`): `altinn:resourceregistry/resource.write` or `.admin` ([`AuthzConstants.cs:51-61`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry.Core/Constants/AuthzConstants.cs#L51-L61)) | **off**: `GET /Home/GetTestOrgToken?scopes=altinn:resourceregistry/resource.admin` mints a Maskinporten-shaped token and exchanges it; the seed step uses it | No PDP call in the host (`IPDP` is not registered; `Altinn.Common.PEP` supplies the scope handler only, not verified exhaustively) |
| Application Insights, OTel | packages referenced; ServiceDefaults wires Azure Monitor only with a connection string | **off** | |
| `RequestForwarderLogMiddleware`, `RdfOutputFormatter` (dotNetRdf) | logging; `export` endpoint | unchanged | |

### 3.3 Directive sites

Both are pure additions (`#if LOCALTEST … #endif`), no production line is replaced:

1. [`ResourceRegistryHost.cs:45`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L45),
   right after `AltinnHost.CreateWebApplicationBuilder(...)`: `builder.AddLocaltestConfiguration();` (configuration
   defaults inserted above the JSON files and below the environment, as Access Management does), because
   `MapPostgreSqlConfiguration` reads `PostgreSQLSettings` eagerly on the next line.
2. [`ResourceRegistryHost.cs:187`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs#L187),
   right before `return builder.Build();`: `builder.AddLocaltestResourceRegistry();` (DI replacements and the
   localtest controller).

`Program.cs` needs nothing: `app.UseAuthentication()`/`MapControllers()` pick up the fakes' controller.

### 3.4 Fakes (`fakes/`, namespace `Altinn.ResourceRegistry.Localtest`, compiled into the host)

| File | Replaces | Size |
| --- | --- | --- |
| `LocaltestResourceRegistryExtensions.cs` | the two hooks: configuration defaults (`PostgreSQLSettings:*` on the shared container, `PlatformSettings:{StorageApiEndpoint,ApiRegisterEndpoint,ApiAccessManagementEndpoint}`, `ResourceRegistrySettings:OrgListEndpoint`, `OidcProviders:altinn:*` on the authentication component, `AzureStorageConfiguration` dummies, `kvSetting:SecretUri=""`), then `Replace<IPolicyRepository>`, `Replace<IPublicSigningKeyProvider>`, `LocaltestOidcMetadata.AllowHttpProviders` | 150 |
| `LocaltestResourceRegistryOptions.cs` | `Localtest:ResourceRegistry:{PlatformEndpoint,PolicyStorePath,Database*}` | 30 |
| `Policies/LocaltestFilePolicyRepository.cs` (+ `LocaltestRawResponse`) | `IPolicyRepository` over the `policy-store` volume (section 3.2) | 250 |
| `Infrastructure/LocaltestPublicSigningKeyProvider.cs`, `Infrastructure/LocaltestOidcMetadata.cs` | Key Vault signing keys; http OIDC metadata | 75 (copies) |
| `Controllers/LocaltestRefreshController.cs` | `POST resourceregistry/api/localtest/refresh`: evicts the `applications` cache key and the `ServiceOwnerService` is left alone (1 h stale is fine for a static file) | 30 |

About 540 lines. The `postgres-init/40-resource-registry.sh` script is 20 lines.

### 3.5 Build files

`localtest.build.props`: the storage file verbatim (vendored-project detection, import of the nearest vendored
`Directory.Build.props`, `LOCALTEST`, `TreatWarningsAsErrors=false`, `NoWarn` `CS1591;SA1600;SA1633`), no CSharpier bypass needed (not referenced upstream).
`localtest.build.targets`:

```xml
<ItemGroup Condition="'$(MSBuildProjectName)' == 'Altinn.ResourceRegistry'">
  <Compile Include="$(MSBuildThisFileDirectory)fakes/**/*.cs" LinkBase="Localtest" />
</ItemGroup>
```

Nothing to `Compile Remove` and no `PackageReference Remove`: the Azure Blob client is only constructed inside
`PolicyRepository`, which the DI replacement never resolves, and the Key Vault packages are only used by ServiceDefaults
behind configuration. Central package management reaches the fakes because they are compiled into the host, so their
`using`s must stay within the host's package closure (they do: `Azure.Storage.Blobs`, `Altinn.Common.AccessToken`,
`Microsoft.Extensions.*`).

### 3.6 Dockerfile

Mirror [`src/apps/Altinn.ResourceRegistry/Dockerfile`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/Dockerfile)
(CI builds it with the repo root as context, [`container-scan.yml:21`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/.github/workflows/container-scan.yml#L21)):
`sdk:10.0-alpine@sha256:d8ee3981…` build stage, `COPY src/ .`, `dotnet publish -c Release -o /app` from the host
folder; runtime `aspnet:10.0-alpine` (no digest upstream; keep it that way), `EXPOSE 5100`, group/user `dotnet`
3000/1000, `USER dotnet`, `/tmp/logtelemetry`, `ENTRYPOINT ["dotnet", "Altinn.ResourceRegistry.dll"]`. Deviations, each
marked `localtest:`: `COPY vendor/src/ .` plus `COPY localtest.build.props localtest.build.targets fakes/ …`, the two
`ENV DirectoryBuildPropsPath/DirectoryBuildTargetsPath` lines before `dotnet publish`, and in the runtime stage
`ENV ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://+:5100`. No `HEALTHCHECK`.

### 3.7 Compose, Postgres, ingress

```yaml
resource-registry:
  <<: *platform-host
  build: ./components/resource-registry
  image: localhost/localtest-resource-registry:dev
  environment:
    <<: *access-token-cert
    Localtest__ResourceRegistry__PlatformEndpoint: http://local.altinn.cloud:8000/
    PostgreSQLSettings__AdminConnectionString: Host=postgres;Port=5432;Username=platform_authorization_admin;Password={0};Database=resourceregistrydb
    PostgreSQLSettings__ConnectionString: Host=postgres;Port=5432;Username=platform_authorization;Password={0};Database=resourceregistrydb;Include Error Detail=True
    PostgreSQLSettings__AuthorizationDbAdminPwd: localtest
    PostgreSQLSettings__AuthorizationDbPwd: localtest
  volumes:
    - ./certs:/certs:ro
    - policy-store:/policy-store
  depends_on:
    postgres: { condition: service_healthy }
    storage: { condition: service_started }
```

Postgres: `./components/resource-registry/postgres-init/40-resource-registry.sh` mounted into
`/docker-entrypoint-initdb.d/` (and run by hand on an existing volume, as the README explains for Events). Passwords:
Access Management's roles use `localtest`, so the script creates the database only and leaves the roles to whichever
component gets there first (both scripts must agree on the password; today Access Management's is `localtest`, Events'
roles are different names).

Ingress: `("/resourceregistry/", "resource-registry")` in `PlatformRoutes.Prefixes` and `["resource-registry"] = "http://resource-registry:5100"`
in `DefaultUpstreams` (`localtest/localtest/Proxy/PlatformRoutes.cs`). Today the proxy answers 404 for that prefix
(checked: `GET /resourceregistry/api/v1/resource/resourcelist` → 404).

The three stack components are already configured for it: Access Management's `Platform:ResourceRegistry:Endpoint`
and `PlatformSettings:ApiResourceRegistryEndpoint`, the PDP's and Authentication's `PlatformSettings:ApiResourceRegistryEndpoint`
all point at the proxy (`fakes/Localtest*Extensions.cs` in each).

### 3.8 App runner and seeding

`run-app.sh`, after step 3 (the policy push to the PDP) and before step 4 (the Access Management refresh):

```sh
# 3b. Resource Registry: derive the app's subjects from the policy, as Designer does on deploy, and drop the app-list cache.
curl -s -o /dev/null -w "resource registry subjects: HTTP %{http_code}\n" \
  "$PLATFORM/resourceregistry/api/v1/resource/app_$ORG_$APP/policy/subjects?reloadFromXacml=true"
curl -s -o /dev/null -X POST "$PLATFORM/resourceregistry/api/localtest/refresh"
```

The first call is the production endpoint and the production trigger; the second is the only localtest hook. Step 4
then runs Access Management's production `ResourceSyncService` against the real registry (`orgs`, `updated`,
`resource/{id}`); with the no-op lease it re-reads the feed from the epoch on every refresh, which is idempotent.

Non-app resources (the correspondence resources the signing app names, `apps-correspondence-integrasjon2/4`, and any
generic resource a test wants): a `localtest/seed/resourceregistry/{id}/resource.json` + `policy.xml` folder pushed
with the production endpoints, `POST resourceregistry/api/v1/resource` and `POST resourceregistry/api/v1/resource/{id}/policy`
(multipart `policyFile`), with an admin-scope token from `/Home/GetTestOrgToken`. Who pushes is an open question
(section 6.5); the natural owner is the app runner (`run-app.sh` pushes the whole folder idempotently, 409 on an
existing resource followed by `PUT`), since "the app runner pushes" is the rule. The PDP's `components/authorization/policies/resourceregistry/`
folder, today's seed for `urn:altinn:resource` decisions, moves there in the production layout (`resource.json` is
already the `ServiceResource` JSON).

### 3.9 Changes in Access Management and the PDP

- Access Management: delete `fakes/ResourceRegistry/` (`LocaltestAppResources`, `LocaltestResourceRegistryClient`,
  `LocaltestAltinnResourceRegistry`, 310 lines) and the two `Replace<…>` lines plus the `AddHttpClient` in
  `LocaltestAccessManagementExtensions.cs`; the production `ResourceRegistryClient` (singleton, registered by the
  host) and `AltinnResourceRegistryClient` take over. `LocaltestRefreshService` keeps calling `SyncResourceOwners` and
  `SyncResources`. Compose: `access-management` depends on `resource-registry` instead of `storage`.
- PDP: delete `fakes/Pip/LocaltestResourceRegistry.cs` and its `Replace<IResourceRegistry, …>`; the production
  `ResourceRegistryWrapper` runs. The `/policies` mount stays for app-policy fallbacks only if still used (check
  `LocaltestPolicyRepository`); the `resourceregistry/` subfolder moves to the seed. Access-list calls need the
  platform token the wrapper already signs with the shared certificate.
- Authentication: nothing to change; its client starts getting 200s.

### 3.10 Effort and risk

4 to 6 days to a green smoke test (section 6.4), of which about one day is the file policy repository and its Azure
return types, one day database and build chain, one day the runner and seed, one day retiring the stand-ins and
re-running the authorization and signing suites.

| Risk | Note |
| --- | --- |
| Yuniql on the shared container | New database, same role names as Access Management; the version table default is not verified. If the ServiceDefaults migrator insists on creating roles, use its `Create` section instead of the init script |
| Azure SDK types in `IPolicyRepository` | `Response<BlobContentInfo>` and `Response` are constructible (`Response.FromValue`, `BlobsModelFactory`), but a `Response` subclass is needed; mechanical, 30 lines |
| 10-minute application cache | The refresh endpoint evicts it; without it, `resourcelist` lags. Dialogporten adds its own 20-minute cache unless `DisableCache=true` (the Dialogporten strand's setting) |
| `JWTCookieAuthentication` http metadata | Same fix as Access Management, not verified for this package version; affects only the seed's write calls |
| Upstream pace | Small repo, one Dockerfile, two host hooks far from active code; `ResourceController` changes often but holds no directive |
| `orgs` gives `ttd` an empty `RefId` in Access Management | Production parity; unknown whether any path reads it |

*2026-10-21: the 10-minute application cache row is superseded: no application cache locally (task 5), and Dialogporten's caches are synced and cleared by provisioning step 5 (task 9b).*

## 4. Option B: a fake-only HTTP Resource Registry

A small ASP.NET service `components/resource-registry/` without `vendor/` (the authentication fake's model), or
endpoints inside the localtest app. The localtest app is Native AOT with source-generated JSON and is the front door;
a registry needs XACML parsing (`Altinn.Authorization.ABAC`, not AOT-tested) and a growing DTO surface, so it should be
its own service.

Endpoints it must implement, from section 1.1: `resource/resourcelist` (with `includeApps`, `includeMigratedApps`),
`resource/{id}`, `resource/{id}/policy`, `resource/{id}/policy/subjects?reloadFromXacml`, `resource/{id}/policy/rights`
(v1 and v2, flattening rules into rights with their subjects, which is `PolicyHelper` logic), `resource/updated`
(`since`, `token`, `limit`, `links.next`, `deleted`), `resource/orgs`, `POST resource/bysubjects`,
`access-lists/memberships` and `access-lists/get-by-member` (empty), and either `POST resource` + `POST resource/{id}/policy`
or a seed folder for the non-app resources. Data: apps from Storage's `applications` and the policy store, exactly
`LocaltestAppResources` moved out of Access Management, plus seeded resources, plus the served org list.

The hard part is the feed: Dialogporten's cursor is the max `updatedAt` it has stored, so the fake must hand out
timestamps that stay monotonic across its own restarts and must remember removed pairs as tombstones. Policy file
mtimes in the volume give persistent timestamps for additions; deletions need a state file (`resourcesubjects.json` on
a volume) that records every pair ever seen. That is a reimplementation of the `resourcesubjects` table and the
`SetResourceSubjects` upsert in memory, which the stack's rule ("SQL is not reimplemented in memory") was written to
avoid.

Consistency with Access Management: it should call the fake over HTTP through its production clients (already
configured), and `fakes/ResourceRegistry/` is deleted, so the duplication disappears either way. The PDP likewise.

Size: 700 to 900 lines (controllers 250, app/subject derivation 160 reused, feed and state 200, rights flattening
150, seed 60). Effort 3 to 5 days. Risk: medium, concentrated in contract drift against five production clients
(property casing, `Paginated` shape, `PolicyRightsDTO`, continuation tokens) and in the feed semantics; every later
need (consent templates for Access Management, access lists, the v2 rights with translations, the `changes` feed)
is another re-implementation.

## 5. Option C: Dialogporten's `UseLocalDevelopmentResourceRegister` and the BFF's local path

`LocalDevelopmentResourceRegistry` ([L11-121](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/LocalDevelopmentResourceRegistry.cs#L11-L121))
answers any resource id with a synthetic `ServiceResourceInformation` (type `LocalResourceType`, owner `742859274`/`ttd`,
status `Active`), lists an org's resources from the dialogs that already exist, and returns an **empty** subject-resource
stream and **no** policy information. It is `internal`, keeps a static cache, and takes no seed. Honest evaluation:

- Dialog creation by the adapter works (the admin scope bypasses the ownership check, and the type check accepts
  anything but `correspondenceservice`).
- Search authorization cannot work with production authorization: `SubjectResources` stays empty, so no app dialog
  is listable through roles; only direct `authorizedResources` and instance delegations would show. It only "works"
  together with `UseLocalDevelopmentAltinnAuthorization=true`, which allows everything, which is precisely the thing
  the inbox stack is meant to exercise (users see the dialogs their Access Management roles grant, and not others).
- The minimum-authentication-level check never applies.
- The BFF has no local path for resources: it always fetches `resourcelist`, logs the error and shows no service or
  organisation filters and no resource titles from the registry (Dialogporten's GraphQL `serviceResources` is also
  empty, since it is built from the same `IResourceRegistry`).
- Correspondence, Access Management, the PDP and Authentication get nothing from it.

Usable as a stepping stone while Option A is built (it is configuration only, and the Dialogporten strand sets the
flags anyway), not as the plan.

## 6. Recommendation

### 6.1 Option A, with rationale

| | A: vendor | B: fake service | C: Dialogporten local path |
| --- | --- | --- | --- |
| Tests search authorization for app dialogs | yes, on production data flow | yes, if the feed is right | no |
| Serves Correspondence, Access Management, PDP, Authentication | yes, all endpoints | the subset implemented | no |
| New code | ~540 lines of fakes + script | ~800 lines of contract code | 0 |
| Vendored code | ~19 800 lines, 2 additive hooks | 0 | 0 |
| Containers | +1 (shares Postgres) | +1 | 0 |
| Effort | 4 to 6 days | 3 to 5 days | 0.5 day |
| Risk | low to medium: build chain and database, all with precedent in Register, Events and Access Management | medium: contract drift, feed semantics | the test goal is lost |

The deciding points: the `updated` feed is the product of a table with timestamps, tombstones and a keyed cursor,
and the rights endpoints are XACML flattening; both are what Option B would re-implement, against clients we do not
control. The production service is small, Aspire-free, on the ServiceDefaults and Yuniql patterns the stack already
handles, and its two Azure touchpoints (blob policies, Key Vault) are the same two every other vendored component
replaced. The shared `policy-store` volume is its metadata container, so app policies cost nothing extra. The stack
also gets rid of its largest emulation (Access Management's stand-ins).

### 6.2 What the other strands get from Option A

- **Dialogporten**: `http://local.altinn.cloud:8000/resourceregistry/api/v1/resource/resourcelist` (apps with
  `organization = 991825827`, `orgcode = ttd`, type `AltinnApp`, plus seeded resources), `updated` with real
  pagination, `{id}/policy` (app policies as pushed; a policy without the authentication-level obligation yields no
  policy information, which is production behaviour). Set `UseLocalDevelopmentResourceRegister=false`,
  `DisableSubjectResourceSyncOnStartup=false`, `DisablePolicyInformationSyncOnStartup=false`, and run the Janitor
  sync commands after the runner's `policy/subjects` call (the push, not a timer). A service-owner token for `ttd`
  must carry `consumer` `0192:991825827` (what `/Home/GetTestOrgToken` mints by default) or the admin scope; the
  `GetCurrentUserOrgShortName` warning about `ttd`/`digdir` sharing a number is expected (production has it).
- **Adapter**: nothing to configure; its `ttd` → `digdir` mapping matches.
- **Arbeidsflate BFF**: `PLATFORM_BASEURL=http://local.altinn.cloud:8000` is enough; the at23 exclusion list does not
  hit `app_ttd_*`.
- **Correspondence** (later): `resource/{id}`, `policy/rights`, `policy` for seeded `CorrespondenceService` resources
  whose policy carries a `urn:altinn:minimum-authenticationlevel` obligation with an integer value.
- **Access Management, PDP, Authentication**: the production clients, already configured, start working.

### 6.3 Test-data mapping

Subjects in the test apps' policies (`src/test/apps/*/App/config/authorization/policy.xml`, read today), as the registry
derives them (lower-cased; `[ORG]` is replaced by the runner):

| App | Subject URNs in the feed |
| --- | --- |
| most apps (`frontend-test` adds `seln`; `component-library`, `subform-test`, `stateless-app`, `anonymous-stateless-app`, `payment-test`, `expression-validation-test`, `multiple-datamodels-test`, `navigation-test-subform`, `smoke/apps/ttd/testapp`) | `urn:altinn:rolecode:dagl`, `urn:altinn:rolecode:regna`, `urn:altinn:org:ttd` |
| `process-transition-test`, `service-task` | `urn:altinn:rolecode:dagl`, `urn:altinn:rolecode:priv`, `urn:altinn:org:ttd`, `urn:altinn:org:digdir` |
| `signing-test` | `urn:altinn:rolecode:dagl`, `a0237`, `a0238`, `a0239`, `a0240`, `a0241` |
| `signering-brukerstyrt` | `dagl`, `regna`, plus `urn:altinn:resource:delegation:app_ttd_signering-brukerstyrt` (ignored by the subject map; instance delegations go through `authorizedInstances`) |

Role codes Access Management grants (from `register/api/localtest/test-users` on the running stack, applied by the
refresh; legacy codes are what the registry's subjects use):

| User | Party | Role codes relevant to the policies | Expected: dialogs listable for that party in |
| --- | --- | --- | --- |
| Sophie Salt (1337) | DDG Fitness AS (500000) and its three BEDR | DAGL, REGNA, HADM, … | every app |
| Sophie Salt | own party (501337) | PRIV, REGNA | every app except `signing-test` |
| Sophie Salt | Skog og Fjell Consulting (500004) | DAGL | every app |
| Sophie Salt | EAS Health Consulting (500600) | LEDE, REGNA | apps with `regna` (not `signing-test`, not the `priv` apps) |
| Pengelens Partner (1001) | own party | regna, dagl, priv | every app |
| Pengelens Partner | DDG Fitness AS | A0239, A0240, A0241, packages | `signing-test` only (packages are not in any policy) |
| Gjentagende Forelder (1002) | DDG Fitness AS | A0237, A0238 | `signing-test` only |
| 1004 (self-registered) | own party | seln | `frontend-test` only |
| Ola Nordmann (12345), Rik Forelder (1003), MultiParty Prompt (2001), Kari Nordvik (2002) | own party | regna, dagl, priv | every app |
| 10000-10003 (app-provided test data) | 600000-600003 (REGNA), 700000-700002 (DAGL/LEDE) | | `regna`/`dagl` apps |

Service owners: `ttd` and `digdir` both resolve to `991825827`; `brg`, `skd`, `nav`, `ssb` keep their CDN numbers. The
Register seed's Testdepartementet (`405003309`) is unrelated to `ttd` the service owner.

### 6.4 Verification plan (`components/resource-registry/smoke-test.sh`, through the proxy)

1. `GET resource/resourcelist?includeMigratedApps=true` lists `app_ttd_testapp` with `resourceType: AltinnApp`,
   `hasCompetentAuthority.organization: 991825827`, `orgcode: ttd`, and the seeded correspondence resource with type
   `CorrespondenceService`.
2. `GET resource/app_ttd_testapp` → 200 immediately after a fresh push (the uncached path); unknown id → 404.
3. `GET resource/app_ttd_testapp/policy` returns the XACML the runner pushed (byte-equal to the policy-store file).
4. `GET resource/app_ttd_testapp/policy/subjects?reloadFromXacml=true` → 200 with `urn:altinn:rolecode:dagl` and
   `regna`; `GET resource/updated?since=1970-01-01T00:00:00Z&limit=1` → one row and a `links.next`; following it to
   the end yields every pair exactly once; re-pushing a policy with a subject removed yields that pair with
   `deleted: true` and a newer `updatedAt`.
5. `GET resource/orgs` returns the served org list; `POST resource/bysubjects` with `["urn:altinn:rolecode:dagl"]`
   returns `app_ttd_testapp`.
6. Access Management refresh: `psql` on `authorizationdb` shows `dbo.resource` rows with `refid = app_ttd_testapp` and
   `dbo.roleresource` links for `dagl`/`regna`; `components/authorization/smoke-test.sh` stays 30/30 and the
   `signering-brukerstyrt` Cypress spec green with the stand-ins deleted.
7. `GET resource/{seeded}/policy/rights` lists the seeded policy's subjects with their access packages;
   `GET access-lists/get-by-member?party=urn:altinn:party:uuid:…` with a platform token → 200 and empty.
8. With the Dialogporten strand: the Janitor `sync-subject-resource-mappings` log reports the pair count, and the
   search as Sophie Salt for DDG Fitness AS returns the app's dialog while Pengelens Partner's search for the same
   party does not.

### 6.5 Open questions for the product owner

1. **Who seeds non-app resources** (correspondence, generic): the app runner from a `localtest/seed/resourceregistry/`
   folder, a one-shot compose job, or a localtest-only startup seed inside the registry? The runner matches the
   stack's rule; it means every `run-app.sh` re-pushes the seed.
2. **Database layout**: a separate `resourceregistrydb` (proposed) or production's shared `authorizationdb` with
   Access Management, once the Yuniql version-table question is settled?
3. **The `orgs` file**: serve a committed copy of `altinn-orgs.json` from the localtest app for all consumers
   (registry, adapter, Dialogporten), or let the registry's `IOrgListClient` read a mounted file? The served copy is
   one file for three strands.
4. Is a 10-minute lag of `resourcelist` after an app push acceptable when the runner's refresh hook is skipped
   (studioctl later), or should the localtest configuration shorten the production cache? Shortening it needs a
   directive or a replaced `IApplications` (60 lines).
5. Should the PDP keep reading `resourceregistry/{id}/policy.xml` from `/policies` as a fallback, or is the registry
   the only source for `urn:altinn:resource` policies from now on (recommended: only source)?

### 6.6 Not verified

- Nothing was built or run: the props/targets mechanism against this repo's `Directory.Packages.props` and StyleCop,
  the Yuniql run on Postgres 16 (upstream tests use 16.1, which is strong evidence), the ServiceDefaults `Create`
  section, and the Yuniql default version table name.
- That `JWTCookieAuthentication` 4.0.4 needs the http-metadata workaround.
- Which Register endpoint `RegisterClient` calls (access lists only).
- Whether any Access Management path reads `dbo.provider.RefId` for `ttd` (empty after the switch to the real `orgs`).
- That `ScopeAccessHandler` is the only `Altinn.Common.PEP` use and no PDP call hides in the host.
- The exact `PolicyRightsDTO` flattening for policies with several rules per subject (only read, not exercised).
- Dialogporten's behaviour was inferred from code at `0946da8`; the five newer upstream commits do not touch these
  files, but runtime behaviour was not observed.
