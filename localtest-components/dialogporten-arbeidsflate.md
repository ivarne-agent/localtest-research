# Localtest components: Dialogporten and Arbeidsflate

Research date: 2026-10-18. Read-only: nothing was built or run. Runtime behaviour inferred from code is marked
"not confirmed". Sources, all pinned (the `digdir/*` names redirect to the `Altinn` organisation):

| Repo | Commit | What |
| --- | --- | --- |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` (main, 2026-10-16) | Dialogporten: WebApi, GraphQL, Service, Janitor |
| [`Altinn/dialogporten-frontend`](https://github.com/Altinn/dialogporten-frontend/tree/0011d957ddbd9d77e31fcce842fc41887297942c) | `0011d95` (main, 2026-10-16) | Arbeidsflate: React SPA, Fastify BFF |
| [`Altinn/altinn-dialogporten-adapter`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/0a329856ba79101acdf54c29fd84a5063189a42d) | `0a32985` (main, 2026-10-08) | Syncs app instances in Storage to dialogs |
| [`Altinn/altinn-storage`](https://github.com/Altinn/altinn-storage/tree/1c594731249c9efb8ef41fab037780e47f659049) | `1c59473` | As vendored in `localtest/components/storage` |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2) | `392f957` | Authentication, as vendored in `localtest/components/authentication` |
| [`Altinn/altinn-correspondence`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) | `6dcf561` | Correspondence |
| [`Altinn/altinn-apiclient-maskinporten`](https://github.com/Altinn/altinn-apiclient-maskinporten/tree/15d3c63a1f320816acccefd1f221d936ccb286db) | `15d3c63` | Maskinporten client used by the .NET services |
| [`Altinn/altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) | `447e53e` | App libraries v9 and app frontend; `app-lib-dotnet` v8 checked by code search |

Clones: `/home/agent/code/.reference/{dialogporten,dialogporten-frontend,altinn-dialogporten-adapter}`.

## Summary

- **Apps do not talk to Dialogporten.** Neither app-lib v9 nor v8 calls it (only a correspondence reference type and
  a notification association field mention it). Dialogs for app instances come from a chain that starts in Storage:
  every instance mutation writes a row to Storage's Postgres outbox, Storage's `OutboxService` publishes a
  `SyncInstanceCommand` to the Azure Service Bus queue `altinn.dialogportenadapter.webapi`, and the
  **dialogporten-adapter** consumes it, reads the instance, events, application metadata and texts back from Storage,
  and creates or updates the dialog through Dialogporten's service-owner API. Events are not involved.
- **Correspondence calls Dialogporten directly** over HTTP (`POST dialogporten/api/v1/serviceowner/dialogs`, plus
  activities, transmissions, system labels) from a Hangfire job, with a Maskinporten token.
- **Arbeidsflate already logs in through Altinn Authentication as an OIDC provider**, not through ID-porten directly:
  the BFF discovers `{OIDC_PLATFORM_URL}/.well-known/openid-configuration`, defaulting to
  `platform.at23.altinn.cloud/authentication/api/v1/openid`. The vendored Authentication has those production
  endpoints (`authorize`, `token`, discovery, logout). Our picker already sits behind them as the upstream provider,
  so a local login can run on production code end to end. What is missing is three in-memory repositories (OIDC
  clients, authorization codes, refresh tokens) and one seeded client. No test-login seam is needed. The BFF's own
  `/api/init-session` seam exists for automated tests.
- **Recommendation: include both, in phases.** Dialogporten (WebApi, GraphQL, Service) plus the adapter first,
  then Arbeidsflate. It is feasible within the model: no directive in Storage (configuration and one fake handler, see 4.2), about 4 in
  Dialogporten, 4 in the adapter, two small patch files for the TypeScript BFF/SPA.
- **Prerequisites**, which are also the biggest risks: (1) a **Resource Registry** reachable over HTTP. Dialogporten's
  search authorization depends on its subject-to-resource mappings, and the BFF lists resources from it. Localtest
  has none today; Access Management carries an in-process stand-in. (2) A **Maskinporten** token endpoint and a
  shared `IMaskinportenService` fake. The .NET client hard-codes `https://*.maskinporten.no`. (3) **Postgres 18**: one
  Dialogporten migration uses the built-in `uuidv7()`. (4) A **Redis** container.
- Effort estimate for the whole chain, prerequisites included: about 3 to 4 weeks. Without Arbeidsflate (an inbox
  page on Dialogporten's end-user API instead), about 2 to 2.5 weeks. The saving is smaller than it looks, because
  the prerequisites stay.

## 1. How dialogs come to exist and what end users see

### 1.1 Altinn 3 apps: Storage, outbox, adapter

| Step | Where | Evidence |
| --- | --- | --- |
| Instance mutation (create, save, process change, substatus, delete) writes an outbox row in the same transaction, only if `WolverineSettings:EnableSending` is true | Storage `PgInstanceMutationRepository.BuildOutboxPayload`, `OutboxInsertRowFactory.TryBuild` | [`OutboxInsertRowFactory.cs:24-29`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/OutboxInsertRowFactory.cs#L24-L29) |
| Per-event delay: urgent 0 s (create, delete), high 5 s, low 60 s (save, process events) | `OutboxEventSyncPolicy`, `WolverineSettings` | [`WolverineSettings.cs:50-63`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Configuration/WolverineSettings.cs#L50-L63) |
| `OutboxService` (hosted, lease-based, polls its own table every `PollIdleTimeMs`=1 s) publishes `SyncInstanceToDialogportenCommand` | Storage | [`OutboxService.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Services/OutboxService.cs#L43-L60) |
| Outside Development: Wolverine sends to ASB queue `altinn.dialogportenadapter.webapi`. In Development: conventional local routing, sequential local queues | Storage `ConfigureWolverine` | [`Program.cs:398-450`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs#L398-L450) |
| Message identity `Altinn.DialogportenAdapter.SyncInstanceCommand` (AppId, PartyId, InstanceId, InstanceCreatedAt, IsMigration) | contract | [`SyncInstanceToDialogportenCommand.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Messages/SyncInstanceToDialogportenCommand.cs#L12-L20), [adapter `SyncInstanceCommand`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.Contracts/SyncInstanceCommand.cs) |
| Adapter listens on the ASB queue (and a history queue for migration) with retry policies for 409/410/412/422/401/5xx | adapter | [`ServiceCollectionExtensions.cs:61-177`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L61-L177) |
| The same sync is exposed as HTTP: `POST storage/dialogporten/api/v1/syncDialog` (body `SyncInstanceCommand`, scope `altinn:storage/instances.syncadapter`) | adapter | [`WebApplicationExtensions.cs:31-40`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L31-L40) |
| Sync: dialog id = UUIDv7 from instance id + created time; GET dialog, application, instance, instance events, Altinn CDN orgs; writes `dialog.id` back to the instance data values; creates/updates/deletes the dialog | adapter | [`ISyncInstanceToDialogService.cs:46-130`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/ISyncInstanceToDialogService.cs#L46-L130) |

Adapter outbound calls (all Maskinporten via `Altinn.ApiClients.Maskinporten`, exchanged to an Altinn token, scopes
`digdir:dialogporten.serviceprovider[.admin]`, `altinn:serviceowner/instances.read|write`,
`altinn:storage/instances.syncadapter`, `altinn:register/partylookup.admin`,
[appsettings.json](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/appsettings.json)):
Storage `instances/{p}/{i}`, `.../events`, `.../datavalues`, `sbl/instances` (delete), `applications/{org}/{app}`,
`applications/.../texts/{lang}`; Register `register/api/v1/dialogporten/parties/query` (the vendored Register serves
it, [`DialogPortenController`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs));
Dialogporten `api/v1/serviceowner/dialogs[...]`; `altinncdn.no/orgs/altinn-orgs.json` (internet).

The adapter also serves two front-channel endpoints the inbox calls with a **dialog token**: `DELETE
storage/dialogporten/api/v1/instance/{owner}/{guid}` (the dialog's delete action) and `GET .../receipt/{dialogId}/{transmissionId}`
(markdown receipt) ([`WebApplicationExtensions.cs:65-130`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L65-L130)).

Apps link back: the v9 app frontend builds "go to inbox" from `dialog.id` in the instance data values
([`urlHelper.ts:35-75`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/utils/urls/urlHelper.ts#L35-L75)),
but on `local.altinn.cloud` it always returns `http://{host}/`. A local inbox needs a small app-frontend change
(use `arbeidsflateDialogUrl` when configured, also locally).

### 1.2 Correspondence

`InitializeCorrespondencesHandler.CreateDialogportenDialog` (Hangfire job) calls
[`DialogportenService.CreateCorrespondenceDialog`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Dialogporten/DialogportenService.cs#L36-L80):
`POST dialogporten/api/v1/serviceowner/dialogs`, then activities (opened, confirmed, download started, purged), system
labels and transmissions. The HTTP client uses `PlatformGatewayUrl` and a raw Maskinporten token
([`DependencyInjection.cs:45-71,93-100`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L45-L100)),
scopes include `digdir:dialogporten.correspondence`, `.serviceprovider`, `.serviceprovider.admin`. If
`MaskinportenSettings:ClientId` is empty, **all** integrations switch to `*DevService` stubs at once, including
`DialogportenDevService`. Correspondence also validates dialog tokens from Arbeidsflate: JWKS at
`{DialogportenSettings:Issuer}/.well-known/jwks.json` ([`Auth/DependencyInjection.cs:75-82`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs#L75-L82)).
The correspondence resource must exist in the Resource Registry (type `CorrespondenceService`).

### 1.3 What the end user sees in Arbeidsflate

From the adapter's merger ([`StorageDialogportenDataMerger.cs`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs)):
one dialog per instance under the instance owner party. The title comes from the app's texts and metadata, the status
is derived from the process (awaiting input, awaiting signature, archived, rejected and so on). There is a "go to
service" GUI action (`{platform}/authentication/api/v1/authentication?goto={app}/#/instance/{id}`) and a delete
action through the adapter. Activities come from instance events, attachments from data elements (`selfLinks.platform`),
and submissions become transmissions with a markdown receipt (`EnableSubmissionTransmissions`). Correspondence
appears as messages with content fetched front-channel from Correspondence, plus read and confirm activities. The
inbox lists dialogs for all parties the user is authorized for, through Dialogporten's `parties` and search queries.

## 2. Dialogporten production structure

| Part | What | Evidence |
| --- | --- | --- |
| Hosts | **WebApi** (FastEndpoints, `UsePathBase("/dialogporten")`, service-owner and end-user REST, `.well-known/jwks.json`); **GraphQL** (HotChocolate, `/graphql` plus SSE `/graphql/stream`, end-user only; APIM maps `/dialogporten/graphql`); **Service** (MassTransit consumers of domain events); **Janitor** (Cocona console jobs: `sync-subject-resource-mappings`, `sync-resource-policy-information`, `reindex-dialogsearch`, cost metrics) | [`WebApi/Program.cs:207`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Program.cs#L207), [`GraphQL/Program.cs:169`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L169), [`Service/Program.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Service/Program.cs), [`Janitor/README.md`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/README.md) |
| Size | ~79k lines C# in `src` (excluding migrations); .NET 10, SDK pinned `10.0.401` with `rollForward: disable`, `TreatWarningsAsErrors` | `global.json`, `Directory.Build.props` |
| Postgres | EF Core, 141 migration files; extensions `pg_trgm`, `btree_gin`; **`uuidv7()` default (PostgreSQL 18 built-in)** in [`20260807085531_AddAuthorizationContext.cs:19`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Migrations/20260807085531_AddAuthorizationContext.cs#L19). Upstream compose and tests use `postgres:18.3`. `DevelopmentMigratorHostedService` migrates on start in Development; elsewhere a migration bundle image | [`docker-compose-db-redis.yml`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/docker-compose-db-redis.yml), [`InfrastructureExtensions.cs:122`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L122) |
| Redis | FusionCache L2 and backplane, GraphQL subscriptions (`AddRedisSubscriptions`) | [`InfrastructureExtensions.cs:179-180,482`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L179-L180) |
| Messaging | MassTransit with an **EF Core Postgres outbox**: WebApi and GraphQL write domain events to the outbox with delivery disabled; Service delivers and consumes. Transport is ASB, or **in-memory when `Development` and `UseInMemoryServiceBusTransport`** | [`InfrastructureExtensions.cs:436-510`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L436-L510) |
| Service consumers | Forward domain events to Altinn Events, maintain the free-text search index (`DialogSearchIndexer`), invalidate party-resource caches | [`AltinnForwarders`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/Common/Events/AltinnForwarders), [`DialogSearchIndexer.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/Common/Events/DialogSearch/DialogSearchIndexer.cs) |
| Outbound | Altinn Events `events/api/v1/events` (Maskinporten exchanged); Resource Registry `resourceregistry/api/v1/resource/{id}`, `resourcelist`, `updated` (subject-resource pairs), `{id}/policy` (anonymous); Access Management `accessmanagement/api/v1/resourceowner/authorizedparties`, `meta/info/roles`, `meta/info/accesspackages/export`; PDP `authorization/api/v1/authorize`; Register `register/api/v1/dialogporten/parties/query`; `altinncdn.no/orgs/altinn-orgs.json` | [`InfrastructureExtensions.cs:520-566`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L520-L566), [`Altinn/`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn) |
| Inbound auth | One JWT bearer scheme per `WebApi:Authentication:JwtBearerTokenSchemas` entry (Maskinporten, Altinn, Idporten by well-known URL); the scheme is chosen by matching the token's `iss` to the issuer from each well-known (`TokenIssuerCache`). End user: scope `digdir:dialogporten` or `.noconsent` and a `pid`; service owner: scope `digdir:dialogporten.serviceprovider` and a Maskinporten `consumer` claim | [`AuthenticationBuilderExtensions.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/AuthenticationBuilderExtensions.cs), [`TokenIssuerCache.cs:60`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs#L60), [`AuthorizationOptionsSetup.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authorization/AuthorizationOptionsSetup.cs), [`ClaimsPrincipalExtensions.cs:296-323`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Extensions/ClaimsPrincipalExtensions.cs#L296-L323) |
| Dialog tokens | Ed25519 compact JWS signed with `Application:Dialogporten:Ed25519KeyPairs` (primary and secondary), published at `/dialogporten/api/v1/.well-known/jwks.json`; consumed by Correspondence, the adapter and service-owner frontends | [`WebApi/appsettings.Development.json`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/appsettings.Development.json) |
| Search authorization | Authorized parties (Access Management) × subject-resource mappings (synced from the Resource Registry into Dialogporten's own tables by Janitor, and on start in Development) × dialogs; dialog details authorized by the PDP (`app_{org}_{app}` split into `urn:altinn:org`/`urn:altinn:app`) | [`AltinnAuthorizationClient.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L29-L30), [`DecisionRequestHelper.cs:58,371`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/DecisionRequestHelper.cs#L58) |
| Azure | Azure App Configuration and Key Vault only when `AZURE_APPCONFIG_URI` is set; telemetry via OTLP or console | [`AzureAppConfigurationExtensions.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Library.Utils.AspNet/AzureAppConfigurationExtensions.cs) |
| Local switches | `LocalDevelopment:*` flags, honoured only in `Development`, **all default to true**: local user, no auth, local resource registry, local authorization (allow everything), console event bus, local JWS generator, null cache, in-memory bus, and so on | [`LocalDevelopmentSettings.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/LocalDevelopmentSettings.cs), [`InfrastructureExtensions.cs:368-380`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L368-L380) |

No logical-replication or CDC consumer exists in `src`. The `wal_level=logical` in the upstream compose looks unused
(not confirmed). Feature toggles go through Application configuration (and App Configuration in Azure); nothing
needs a feature-flag service locally.

## 3. Arbeidsflate structure

| Part | What | Evidence |
| --- | --- | --- |
| Frontend | React SPA (Vite, ~24k lines), served by nginx with a strict CSP (`connect-src 'self' https:`); env detection by host name (`localhost` → local, `*.at23.altinn.cloud`, `*tt02*`, otherwise **prod**); a few runtime values injected into `index.html` by `envsubst` (`DIALOGPORTEN_STREAM_URL`) | [`nginx.conf:20`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/nginx.conf#L20), [`auth/url.ts:52-70`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/auth/url.ts#L52-L70), [`start.sh`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/start.sh) |
| BFF | Node 22 Fastify, ~7k lines, routes under `/api`. GraphQL schema stitching: Dialogporten's GraphQL as a remote subschema (proxied with the session's access token) plus BFF-owned types (profile, saved searches, service resources, notifications log, correspondence forward) | [`graphql/api.ts:15-45`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/api.ts#L15-L45) |
| Session store | Redis (`@fastify/session`, cookie `arbeidsflate`); Postgres via TypeORM for BFF-owned data (profiles, saved searches), with its own migrations (`MIGRATION_RUN`). Upstream compose uses `postgres:16` and `redis:6.2` | [`config.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/config.ts), [`compose.yml`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/compose.yml) |
| Login | Authorization code + PKCE + nonce against **Altinn Authentication's OIDC server**, discovered at `https://{OIDC_PLATFORM_URL}/.well-known/openid-configuration` (scheme hard-coded), client secret basic, scopes `digdir:dialogporten.noconsent openid altinn:portal/enduser altinn:correspondence.forward.check`, `acr_values=idporten-loa-substantial`, callback `{HOSTNAME}/api/cb`, refresh-token rotation, front-channel logout | [`oidc.ts:165,330-396`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L330-L396), [`verifyToken.ts:106`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L106) |
| Calls with the user token | Dialogporten GraphQL (dialogs, parties through Access Management inside Dialogporten); Profile `profile/api/v1/users/current`, `profilesettings`, `party-groups/favorites`, notification settings (errors are caught for `users/current` and favorites); Correspondence `correspondence/api/v1/correspondence/{id}` | [`profile/service.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/profile/service.ts) |
| Calls with its own Maskinporten client | `{MASKINPORTEN_ISSUER}token` (jwt-bearer, issuer configurable), exchanged through `authentication/api/v1/exchange/maskinporten`; used for Register `dialogporten/parties/query` and `set-username`, Notifications `future/log` | [`auth/maskinporten.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts) |
| Anonymous | Resource Registry `resourcelist?includeApps=true&includeMigratedApps=true` (cached in Redis) | [`serviceResources/service.ts:77`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/serviceResources/service.ts#L77) |
| Browser direct | Dialog content (`mainContentReference`, receipts, correspondence content) fetched with `Authorization: Bearer {dialogToken}`; SSE dialog updates from Dialogporten's `/graphql/stream` | [`MainContentReference.tsx:70-85`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/components/MainContentReference/MainContentReference.tsx#L70-L85) |
| Test seam | `POST /api/init-session {token}` (when `ENABLE_INIT_SESSION_ENDPOINT=true`) writes a session for a given access token and returns the cookie; no id_token or refresh token, so logout and refresh do not work | [`oidc.ts:202-245,394`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L202-L245) |

### 3.1 Local login: our Authentication is the OIDC provider

The vendored Authentication already contains the production OP:
[`OidcFrontChannelController`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcFrontChannelController.cs#L33)
(`authorize`, `upstream/callback`, `openid/logout`),
[`OidcTokenController`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcTokenController.cs)
(`token`), and a discovery document with `authorization_endpoint`, `token_endpoint`, `end_session_endpoint`
([`OpenIdController.cs:46-80`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OpenIdController.cs#L46-L80)).
The access token is issued by `{PlatformEndpoint}authentication/api/v1/openid/` (the same issuer as ordinary Altinn
tokens) and carries `pid`, `scope`, party and user claims
([`TokenService.cs:41-90`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L41-L90),
[`ClaimsPrincipalBuilder.cs:47-113`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L47-L113)).
Dialogporten therefore accepts it through its "Altinn" scheme as an end user. In production, Arbeidsflate's issuer
is that OP and the upstream is ID-porten. Locally the upstream is the picker, which the component already wires as
`IOidcProvider`.

The gap is persistence. The component keeps three OIDC tables in memory (unregistered clients, upstream login
transactions, OP sessions,
[`LocaltestAuthenticationExtensions.cs:116-119`](../localtest/components/authentication/fakes/LocaltestAuthenticationExtensions.cs)),
but `IOidcServerClientRepository`, `IAuthorizationCodeRepository` and `IRefreshTokenRepository` still resolve to the
Postgres implementations
([`PersistanceDependencyInjection.cs:108,117,126`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/Extensions/PersistanceDependencyInjection.cs#L108-L126)),
and there is no database. Concretely:

1. `fakes/Persistence/`: in-memory `IOidcServerClientRepository` (seeded from configuration), `IAuthorizationCodeRepository`
   (atomic `TryConsumeAsync`), `IRefreshTokenRepository`. Estimated 200 to 300 lines. An alternative is to move the
   component onto the shared Postgres with the production migrations and a seed row.
2. Seed client `localtest-arbeidsflate`: confidential, `client_secret_basic`, redirect
   `http://af.local.altinn.cloud:8000/api/cb`, post-logout `http://af.local.altinn.cloud:8000/`, front-channel logout
   `.../api/frontchannel-logout`, the four scopes above, PKCE S256, nonce required.
3. The picker must offer `acr` `idporten-loa-substantial` (level 3) for this flow. It already maps levels to acr
   values.

Not confirmed at runtime: that the registered-client branch of `upstream/callback` works with the picker exactly as the
unregistered `goto` branch does. Reading
[`OidcServerService.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs)
suggests it does (both load the same upstream transaction). The `init-session` seam is the fallback, and also the
right tool for Playwright: `GetTestUserToken`-style token, then `POST /api/init-session`.

## 4. Plan in the localtest model

### 4.1 Shared prerequisites (not Dialogporten-specific)

| # | What | Why | Shape |
| --- | --- | --- | --- |
| P1 | **Maskinporten token endpoint** in the authentication component: `POST authentication/api/v1/localtest/maskinporten/token` (jwt-bearer grant; signature not checked; `iss` client id → org/orgnumber from configuration; mints the same Maskinporten-shaped token `GetTestOrgToken` mints, with `consumer` and the requested `scope`) | The BFF (`MASKINPORTEN_ISSUER` is configurable) and every .NET client need raw Maskinporten tokens; the component already serves the matching well-known and JWKS under `localtest/maskinporten` | fake controller, ~100 lines |
| P2 | **`LocaltestMaskinportenService : IMaskinportenService`** in a shared fakes source folder, compiled into the adapter, Dialogporten and Correspondence | `Altinn.ApiClients.Maskinporten` hard-codes `https://{test,prod}.maskinporten.no/token` and the exchange URLs per environment ([`MaskinportenService.cs:416-445`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L416-L445)); it is registered with `TryAddSingleton` ([`ServiceCollectionExtensions.cs:170`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Extensions/ServiceCollectionExtensions.cs#L170)), so a replacement in DI is enough | calls P1, then the production `exchange/maskinporten` when `ExhangeToAltinnToken`; ~150 lines, no directive |
| P3 | **Resource Registry over HTTP**: `resource/{id}`, `resourcelist`, `updated` (subject-resource pairs with `since`/continuation), `{id}/policy` | Dialogporten validates `serviceResource` and resolves owner and type from it; search authorization needs the subject-resource mappings; the BFF lists resources; Correspondence needs its resources. Today only Access Management's in-process stand-in exists | Either vendor production Resource Registry (it reads apps from Storage's application list, [`ResourceController.cs:57,546`](https://github.com/Altinn/altinn-resource-registry/blob/a3422419579832833845e47d3be57c148364bed0/src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/Controllers/ResourceController.cs#L546); how app subject mappings get there is not confirmed) or a fake-only service built on Access Management's `LocaltestAppResources` logic (apps from Storage, subjects from the shared policy store, plus seeded generic and correspondence resources). Product-owner decision; it is the largest risk to the plan |
| P4 | **Postgres 18** | `uuidv7()` in a Dialogporten migration | Bump the shared container to 18 if Storage, Access Management (and Correspondence, Events) migrations pass on it (cheap to test); otherwise a second `postgres:18` service for Dialogporten. A `uuidv7()` shim function in PG16 would work but diverges from production |
| P5 | **Redis** container | Dialogporten GraphQL subscriptions, FusionCache; BFF sessions | `redis:7-alpine`, one database per consumer |
| P6 | Static **`orgs/altinn-orgs.json`** served by the ingress (a fake upstream for `altinncdn.no`) | Adapter and Dialogporten read service-owner names from it; offline otherwise | ingress location + file; configuration only |

### 4.2 Storage: turn the outbox on, deliver over HTTP

No directive. Configuration: `WolverineSettings:EnableSending=true`, `LowPriorityDelaySecs=1`,
`HighPriorityDelaySecs=0`, `TryGettingPollMasterIntervalSecs=1`. In `Development`, Storage's Wolverine uses local
sequential queues ([`Program.cs:430-446`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs#L430-L446)),
so a handler in `fakes/` (compiled into the host assembly, where Wolverine discovers handlers) receives each
`SyncInstanceToDialogportenCommand`. It gets an Altinn token with `altinn:storage/instances.syncadapter` (P2), POSTs to the
adapter's production `POST storage/dialogporten/api/v1/syncDialog` and retries transient failures. The local queue is
sequential, which keeps the adapter from racing itself into 412s. The outbox poll is Storage reading its own table,
as in production. Nothing polls across components. When the adapter is not running, the handler logs and drops the
message, and `EnableSending` can stay off.

### 4.3 Adapter component (`components/dialogporten-adapter`)

Vendor `src/Altinn.DialogportenAdapter.WebApi` and `Contracts` (7k lines). Directive sites:

1. `ServiceCollectionExtensions.AddInfrastructure`: `#if !LOCALTEST` around `opts.ConfigureAdapterDefaults(...)` and the two
   `ListenToAzureServiceBusQueue` calls ([lines 63-67, 171-176](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L61-L177)).
   `#else` uses a local queue, so the production error policies still apply if a fake enqueue endpoint is used later.
   `ConfigureAdapterDefaults` throws on an empty ASB connection string.
2. and 3. `Settings.cs` `GetAppUriForOrg` / `GetPlatformUri`
   ([lines 43-44](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Settings.cs#L41-L45)):
   production builds `{scheme}://{org}.apps.{host}/{appId}` and `platform.{host}`, which drops the port and assumes
   subdomains. `#else` returns `{BaseUri}/{appId}` and `{BaseUri}`, so links become
   `http://local.altinn.cloud:8000/authentication/api/v1/authentication?goto=http://local.altinn.cloud:8000/ttd/app/#/instance/...`.
4. `Program.cs`: `#if LOCALTEST` hook after `ConfigureDialogportenAdapterServices` → `LocaltestAdapter.Apply` (replace
   `IMaskinportenService`, `RequireHttpsMetadata=false` on the JWT bearer, http metadata for the dialog-token key fetch).

Configuration: `Altinn:BaseUri`/`InternalStorageEndpoint`/`InternalRegisterEndpoint`, `Dialogporten:BaseUri`, `Adapter:BaseUri`
(`.../storage/dialogporten`), `AltinnCdn` (P6), `Authentication:JwtBearerWellKnown`, `EnableSubmissionTransmissions=true`.
Ingress: `/storage/dialogporten/` → adapter (before the `/storage/` rule).

Known gaps: attachment URLs come from Storage's `selfLinks.platform`, which is hard-coded to
`https://platform.local.altinn.cloud/...` in the Storage interface package (see the Storage README). Attachments in the
inbox will not open without a fix there. Archived dialogs link to `platform/receipt/{id}` (the receipt app is not in
the stack).

### 4.4 Dialogporten component (`components/dialogporten`)

Vendor `src/` (minus the sample, tool and WebApiClient projects), `Directory.Build.props`, `global.json`. Build three
images from one tree: WebApi, GraphQL, Service. Optionally a fourth for Janitor, or run it as a one-shot command in
the Service image.

Run with `ASPNETCORE_ENVIRONMENT=Development`, which is required for the in-memory MassTransit transport and the
start-up migrator. Because every `LocalDevelopment` flag defaults to true, set them explicitly:

| Flag | Value | Reason |
| --- | --- | --- |
| `UseLocalDevelopmentUser`, `DisableAuth`, `UseLocalDevelopmentAltinnAuthorization`, `UseLocalDevelopmentResourceRegister`, `UseLocalDevelopmentNameRegister`, `UseLocalDevelopmentOrganizationRegister`, `UseLocalDevelopmentPartyNameRegistry`, `UseLocalDevelopmentCompactJwsGenerator` | `false` | Production code paths against the local Authentication, PDP, Access Management, Register, Resource Registry (P3), real Ed25519 dialog tokens |
| `UseInMemoryServiceBusTransport` | `true` | No ASB; the outbox stays in Postgres and the Service host delivers it in-process |
| `UseLocalDevelopmentCloudEventBus` | `true` until Events exists, then `false` | Dialog events to Altinn Events |
| `DisableCache` | `true` | Production caches authorized parties and PDP results for 15 minutes; testers change `testData.json` |
| `DisableSubjectResourceSyncOnStartup`, `DisablePolicyInformationSyncOnStartup` | `false` | Initial sync from P3 |
| `UseLocalMetricsAggregationStorage` | `true` | Cost-metrics job only |

Directive sites: one `#if LOCALTEST` hook per host `Program.cs` (WebApi, GraphQL, Service, Janitor if built), placed
after the existing Development replacements, calling `LocaltestDialogporten.Apply`:

- Replace `ITokenIssuerCache` with one that reads well-knowns over http. The production one uses an
  `OpenIdConnectConfigurationRetriever` with the default HTTPS-only document retriever
  ([`TokenIssuerCache.cs:60`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs#L60)).
- `PostConfigureAll<JwtBearerOptions>(o => o.RequireHttpsMetadata = false)`.
- Replace `IMaskinportenService` (P2).

The interfaces are reachable from `fakes/`, so no other code change is needed. Configuration:

- `Infrastructure:DialogDbConnectionString`, `Redis:ConnectionString`.
- `Altinn:BaseUri` and `EventsBaseUri` = `http://local.altinn.cloud:8000/`; `AltinnCdn:BaseUri` (P6).
- `Maskinporten` with a dummy JWK.
- `Warmup:Enabled=false`.
- `Application:Dialogporten:BaseUri` = `http://local.altinn.cloud:8000/dialogporten`, and a committed test
  `Ed25519KeyPairs` generated with dialogporten-utils.
- `WebApi:Authentication:JwtBearerTokenSchemas` = `Maskinporten` → the local Maskinporten well-known, `Altinn` → the
  Authentication well-known. Drop `Idporten`; Arbeidsflate's token comes from Altinn.
- GraphQL `Cors:AllowedOrigins` = `http://af.local.altinn.cloud:8000`.

Ingress:

- `/dialogporten/graphql` → GraphQL, rewritten to `/graphql` as APIM does.
- `/dialogporten/` → WebApi.
- Service has no route.

App-runner step: after the policy push and the Access Management refresh, run
`dialogporten-janitor sync-subject-resource-mappings` and `sync-resource-policy-information` once, so that a new or
changed app's role mappings reach search authorization. This is a push, not a timer. Production runs the same
commands as scheduled jobs.

### 4.5 Arbeidsflate component (`components/arbeidsflate`), TypeScript

`#if` is not available, so use the equivalent:

- `vendor/`: pristine copy of the repo at a pinned commit (packages `bff`, `frontend` and their workspace
  dependencies).
- `patches/*.patch`: applied in the Dockerfile with `git apply --check` first, so a patch that no longer applies
  fails the build loudly.
- Configuration through env.
- Our own nginx config, which is not a patch.

| Patch or config | What |
| --- | --- |
| `0001-bff-oidc-scheme.patch` (2 lines: [`oidc.ts:165`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L165), [`verifyToken.ts:106`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L106)) | Allow a scheme in `OIDC_PLATFORM_URL` instead of hard-coded `https://`. Alternative without a patch: an internal TLS listener on the ingress with the test CA in `NODE_EXTRA_CA_CERTS` |
| `0002-frontend-local-host.patch` ([`auth/url.ts:52-70,195-210`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/auth/url.ts#L52-L70)) | Treat `*.local.altinn.cloud` as `local`, so cookie domain and outbound links are not prod's. Optional, but otherwise links point to `altinn.no` |
| nginx config (ours) | Serve the built SPA at `af.local.altinn.cloud:8000/` with CSP `connect-src 'self' http://local.altinn.cloud:8000`; `/api` → BFF; `DIALOGPORTEN_STREAM_URL` = `http://local.altinn.cloud:8000/dialogporten/graphql/stream` |
| BFF env | `HOSTNAME=http://af.local.altinn.cloud:8000`, `OIDC_PLATFORM_URL=http://local.altinn.cloud:8000/authentication/api/v1/openid`, `OIDC_CLIENT_ID/SECRET` (seeded client), `PLATFORM_BASEURL=http://local.altinn.cloud:8000`, `DIALOGPORTEN_URL=http://local.altinn.cloud:8000/dialogporten`, `MASKINPORTEN_ISSUER=http://local.altinn.cloud:8000/authentication/api/v1/localtest/maskinporten/` + dummy JWK and client id (P1), `AUTH_CONTEXT_COOKIE_DOMAIN=.local.altinn.cloud`, `DB_CONNECTION_STRING` (shared Postgres, db `arbeidsflate`), `REDIS_CONNECTION_STRING`, `MIGRATION_RUN` as an init container, `ENABLE_INIT_SESSION_ENDPOINT=true` |

A separate host name is required because the SPA is built for `/` and the BFF owns `/api/*`, while
`local.altinn.cloud:8000/` is the picker's start page. The ingress strips the `Secure` cookie flag already, which the
BFF sets. HSTS from the SPA is ignored on http. Profile is a gap: there is no Profile service. `users/current` and
favorites fail soft; the profile and notification-settings pages break. The Profile stub that is already planned
covers this.

### 4.6 Ordering

1. P1, P2, P5, P6; P4 decided.
2. P3, the Resource Registry. Shared with Correspondence; agree with that strand.
3. Dialogporten (4.4), verified with service-owner calls using `GetTestOrgToken` scopes and end-user REST calls using
   a picker token with `digdir:dialogporten`.
4. Adapter plus the Storage switch (4.2, 4.3). App instances become dialogs.
5. Authentication OIDC repositories (3.1), then Arbeidsflate (4.5). Testers see the inbox.
6. Correspondence with real clients (ClientId set, P2) after step 3. Before that, Correspondence's DevServices skip
   Dialogporten entirely.
7. Events: flip `UseLocalDevelopmentCloudEventBus=false` when the Events component exists. Dialogporten publishes with
   scope `altinn:events.publish` to `events/api/v1/events`. Notifications later calls Dialogporten's notification
   condition endpoint (`NotificationConditionCheck` policy) for dialog-associated orders; that is relevant to the
   Notifications strand.

### 4.7 Effort and risk (estimates)

| Item | Effort | Risk |
| --- | --- | --- |
| P1 + P2 Maskinporten | 1-2 days | Low; reused by Correspondence and Events |
| P3 Resource Registry | 3-5 days | **High**: no component yet; the `updated` feed and how app subjects reach it are not confirmed |
| P4/P5/P6 | 0.5-1 day | Medium for P4 if the shared bump breaks another service |
| Dialogporten | 4-6 days | Medium: large tree, pinned SDK `10.0.401` with `rollForward: disable` (the image must carry that SDK), warnings as errors, many flags; search authorization needs P3 data that matches Access Management's roles and access packages |
| Adapter + Storage | 2-3 days | Medium: URL rewrites, attachment links, delete via `sbl/instances` not confirmed |
| Authentication OIDC repositories | 1-2 days | Low to medium: the registered-client path behind the picker is not confirmed |
| Arbeidsflate | 3-5 days | Medium: pnpm/turbo build (~2-4 min image builds, not confirmed); host-based env detection; Profile gaps; upstream UI churn means patches may need refresh |
| Total | **~3-4 weeks** | |

Resource cost: 3 Dialogporten hosts, adapter, BFF, nginx SPA and Redis add roughly 7 containers, and .NET plus Node
memory, probably 1.5 to 2.5 GB more (not confirmed). This should be an opt-in profile (`docker compose --profile
inbox`) rather than default.

### 4.8 Alternative: an inbox page without Arbeidsflate

A localtest page on Dialogporten's end-user REST API (`/dialogporten/api/v1/enduser/dialogs`, `.../{id}`) would show the
dialogs for the signed-in user. It would use the picker's token if `GetTestUserToken` can add the scope
`digdir:dialogporten`; the cookie token has no such scope today.

It is 1 to 2 days instead of 3 to 5 plus the OIDC work, and it avoids Node, Redis sessions and the patches. It shows
the data but not what end users actually see: layout, status texts, actions, receipts and attachment handling are
Arbeidsflate's. Since the product owner's goal is "see what an app does for the end user's inbox", and the production
login path is available, Arbeidsflate is the better call. The alternative is a fallback if P3 or the Arbeidsflate
build becomes a blocker. Dialogporten, the adapter and P1 to P6 are needed in both cases.

## 5. Open questions for the product owner

1. **Resource Registry**: vendor production Resource Registry as its own component, or extend the fake that Access
   Management carries into an HTTP service? Dialogporten, Arbeidsflate and Correspondence all need it.
2. **Postgres 18** for the shared container (preferred if the others pass), or a separate Postgres for Dialogporten?
3. Is an **opt-in profile** acceptable for the inbox stack, given about 7 extra containers?
4. **Host name**: `af.local.altinn.cloud:8000` for Arbeidsflate, which needs a hosts entry. Acceptable, or must
   everything stay on `local.altinn.cloud`? That would cost more patches.
5. **Authentication persistence**: add in-memory OIDC client, code and refresh repositories, or move Authentication
   onto the shared Postgres with production migrations?
6. Should the **app frontend** link to the local inbox (`arbeidsflateDialogUrl`) instead of `http://local.altinn.cloud/`?
   This is a change in the monorepo, outside localtest.
7. Is it acceptable to keep two small **TypeScript patches** applied at build time against the Arbeidsflate tree, or
   should we propose upstream changes (scheme in `OIDC_PLATFORM_URL`, a `local.altinn.cloud` host mapping)? The
   upstream changes are small and would remove both patches.
8. Are **attachments and the receipt link** in scope for the first iteration? Both depend on Storage self-link host
   names and the receipt app.
