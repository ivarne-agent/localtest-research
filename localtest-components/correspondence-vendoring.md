# Correspondence in localtest: research and vendoring plan

Status: research, 2026-10-18. Nothing built. Sources pinned:

- `Altinn/altinn-correspondence` at [`6dcf5619`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) (2026-10-16), cloned to `.reference/altinn-correspondence`. Links below marked **C** are relative to it.
- `Altinn/altinn-studio` at [`447e53eb`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) (app-lib v9, the signing test app). Links marked **S**.
- `Altinn/app-lib-dotnet` at [`8d059b8c`](https://github.com/Altinn/app-lib-dotnet/tree/8d059b8c5e92df15905f273fa1c22dda91113da4) (v8). Links marked **L8**.
- `Altinn/altinn-apiclient-maskinporten` at [`15d3c63a`](https://github.com/Altinn/altinn-apiclient-maskinporten/tree/15d3c63a1f320816acccefd1f221d936ccb286db) (the Maskinporten client Correspondence uses, package 10.1.0).

**Recommendation:** vendor it. Correspondence is a good fit for the model. It already ships in-process "Dev" stand-ins for
every outbound integration, selected by one setting. It runs EF migrations and a simulated malware scan when
`ASPNETCORE_ENVIRONMENT=Development`. Its Azure dependencies sit behind interfaces, except Redis. Expect 2 directive sites
in `Program.cs`, roughly 6 small fakes, and one new piece of shared infrastructure: a file-backed Resource Registry
stand-in for `CorrespondenceService` resources. That stand-in is the real gap.

## 1. What app-lib calls

The client is the same in v9 and v8: [`CorrespondenceClient.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs) (S),
[v8 copy](https://github.com/Altinn/app-lib-dotnet/blob/8d059b8c5e92df15905f273fa1c22dda91113da4/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs).
The base URL is `PlatformSettings.ApiCorrespondenceEndpoint`. `run-app.sh` already sets it to
`http://local.altinn.cloud:8000/correspondence/api/v1/`. Nothing serves that path today, so the ingress returns 404.

| Step | Call | Production endpoint (**C**) | Policy |
| --- | --- | --- | --- |
| per attachment | `POST attachment` (JSON `InitializeAttachmentRequest`) → `Guid` ([L203](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L203)) | [`AttachmentController` L46-53](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Controllers/AttachmentController.cs#L46-L53) | `Sender` |
| | `POST attachment/{id}/upload` (octet-stream) ([L222](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L222)) | [L89-100](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Controllers/AttachmentController.cs#L89-L100) | `Sender` |
| | `GET attachment/{id}` once a second, at most 60 times, until `Published` ([L235-236](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L235-L236), [L270](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L270)) | [L141-148](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Controllers/AttachmentController.cs#L141-L148) | `Sender` |
| send | `POST correspondence` (JSON `InitializeCorrespondencesRequest`, not the multipart `/upload` variant) ([L285](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L285)) | [`CorrespondenceController` L120-130](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Controllers/CorrespondenceController.cs#L120-L130) | `Sender` |
| status (`GetStatus`, public API, not used by signing) | `GET correspondence/{id}/details` ([L110](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Correspondence/CorrespondenceClient.cs#L110)) | [L330-338](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Controllers/CorrespondenceController.cs#L330-L338) | `SenderOrRecipient` |

The client sends the attachment status poll itself. Correspondence never polls the app back.

**Authentication.** `CorrespondenceAuthenticationMethod.Default()` asks for a service-owner token: `altinn:serviceowner`,
`altinn:serviceowner/instances.read`, `altinn:serviceowner/instances.write` plus `altinn:correspondence.write`
([`AuthenticationMethod.cs` L38-43, L112-118](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/AuthenticationMethod.cs#L112-L118)).
In production that is a Maskinporten token exchanged to an Altinn token (`GetAltinnExchangedToken`). On the localtest
platform it is `GET /Home/GetTestOrgToken?org={org}&orgNumber=991825827&authenticationLevel=3&scopes=…`
([`AuthenticationTokenResolver.cs` L97](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Auth/AuthenticationTokenResolver.cs#L97); same in v8 at L78).
The localtest Authentication component already serves this through the production `exchange/maskinporten`, and that
exchange copies every original claim, including `consumer` and `scope`
([vendored `AuthenticationController.cs` ~L460-L530](../localtest/components/authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs)).
The client sends no platform access token, only the bearer token and the APIM subscription key header.

The `Sender` policy accepts an issuer containing `altinn` with scope `altinn:correspondence.write`
([Auth `DependencyInjection.cs` L280-308](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs#L280-L308)).
The local issuer `http://local.altinn.cloud:8000/authentication/api/v1/openid/` contains `altinn`, so the production
policy passes unchanged (not confirmed at runtime).

**Main user: signing.**
- [`SigningCallToActionService.cs` L104-120](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Signing/Services/SigningCallToActionService.cs#L104-L120) sends one correspondence per signee. It has no attachments, an optional notification (`WithNotificationIfConfigured`), and `ResourceId` taken from the task's `<altinn:correspondenceResource>`.
- [`SigningReceiptService.cs` L71-83](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Signing/Services/SigningReceiptService.cs#L71-L83) sends a receipt with the signed documents as attachments. This is the attachment init/upload/poll path. It also calls `https://altinncdn.no/orgs/altinn-orgs.json`, which is an external dependency outside this strand.
- The test app uses the resources `apps-correspondence-integrasjon4` and `apps-correspondence-integrasjon2` ([`process.bpmn` L56, L92](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/test/apps/signering-brukerstyrt/App/config/process/process.bpmn#L56)). These are tt02 Resource Registry resources.
- The Cypress spec accepts either `venter på signering` or `varsling mislyktes` ([`signing.ts` L100-112](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/test/e2e/integration/signering-brukerstyrt/signing.ts#L100)). The second state is what today's 404 produces.
- The app's `appsettings.Development.json` points `ApiCorrespondenceEndpoint` at tt02. `run-app.sh` overrides it.

## 2. Production structure

| Area | What it is | Evidence (**C**) |
| --- | --- | --- |
| Hosts | One ASP.NET host, `Altinn.Correspondence.API` (port 2525 in the upstream image). It runs the API **and** the Hangfire servers in the same process. Libraries: Core, Common, Application (handlers), Integrations, Persistence (EF Core). | [`Program.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs), [`Dockerfile`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/Dockerfile) |
| Postgres | EF Core with schema `correspondence`, 139 migration files, and notification templates seeded by a migration. Migrations run at startup **only in Development** (`MigrateWithLock`). Azure AD token auth applies only when the connection string has no password. | [`Program.cs` L71-80, L179-199](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs#L71-L80), [`Persistence/DependencyInjection.cs` L40-58](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Persistence/DependencyInjection.cs#L40-L58), [`add_Notification_templates`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Persistence/Migrations/20240927122054_add_Notification_templates.cs) |
| Background jobs | Hangfire on the same Postgres (`Hangfire.PostgreSql` 1.21.1). It has two servers (default queue, plus migration queues when `MigrationWorkerCountPerReplica>0`) with 5 s schedule polling. Every side effect is a job: Dialogporten dialog → publish (continuation) → notification order → events. Recurring daily jobs: IP-restriction update (Azure), daily report (Blob), bruksmønster cleanup. JWK rotation is off unless enabled. | [`Hangfire/DependencyInjection.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Hangfire/DependencyInjection.cs), [`RecurringJobRegistration.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Helpers/RecurringJobRegistration.cs), [`InitializeCorrespondencesHandler.cs` L140-268](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/InitializeCorrespondences/InitializeCorrespondencesHandler.cs#L140-L268) |
| Redis | `AddStackExchangeRedisCache` (the HybridCache L2), plus **eager** `ConnectionMultiplexer.Connect` for DataProtection keys. The host does not start without Redis. | [`Program.cs` L135-143](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs#L135-L143) |
| Messaging | None besides Hangfire. There is no Service Bus or Wolverine. | |
| Attachments | Azure Blob through `IStorageRepository` (Core). Each service owner gets a per-owner storage account from `ServiceOwnerEntity`. The fallback is `AttachmentStorageOptions:ConnectionString`. `GenerateSasUrl` is used for composed (forwarding) e-mails. | [`StorageRepository.cs` L45-80](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Persistence/Repositories/StorageRepository.cs#L45-L80), [`IStorageRepository.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Core/Repositories/IStorageRepository.cs) |
| Malware scan | Defender for Storage sends Event Grid to the `webhooks/malwarescanresults` endpoint. **In Development** the upload enqueues `SimulateMalwareScanResult`, which runs the production `MalwareScanResultHandler` with a clean result. There is also `GeneralSettings:MalwareScanBypassWhiteList` (per resource). | [`AttachmentHelper.cs` L85-93, L265-290](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/Helpers/AttachmentHelper.cs#L85-L93) |
| Azure Resource Manager | Provisions service-owner storage accounts (skipped in Development), IP restrictions, and container-app refresh. | [`AzureResourceManagerService.cs` L86-92](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Azure/AzureResourceManagerService.cs#L86-L92) |
| Key Vault | Not in configuration bootstrap. Only `KeyVaultSecretStore` uses it, for the Maskinporten JWK rotation, which is off by default. | [`Integrations/DependencyInjection.cs` L37-42](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L37-L42) |
| Outbound auth | Its own Maskinporten client (`Altinn.ApiClients.Maskinporten`, `SettingsJwkClientDefinition`), exchanged to an Altinn token (`ExhangeToAltinnToken: true`). Scopes are listed in [`appsettings.json`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/appsettings.json). Every platform client uses `AltinnOptions:PlatformGatewayUrl` as its base address. | [`Integrations/DependencyInjection.cs` L93-101](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L93-L101) |
| **Switch** | `MaskinportenSettings:ClientId` empty registers upstream's own Dev services for Events, Notifications, Dialogporten, PDP, Register, Profile, Access Management, KRR and Storage (SBL bridge). Otherwise it registers the real HTTP clients. | [`Integrations/DependencyInjection.cs` L45-71](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L45-L71) |
| Feature flags | No FeatureManagement. Settings: `GeneralSettings:DisableCallsToAltinn2`, `EnableDownloadAll`, `MalwareScanBypassWhiteList`, `MigrationWorkerCountPerReplica`, `SlackUrl` (empty means `SlackDevClient`), `ApplicationInsightsConnectionString` (empty means no Azure Monitor), and `MaskinportenJwkRotationSettings:Enabled`. | [`GeneralSettings.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Core/Options/GeneralSettings.cs) |

Inbound authentication ([Auth `DependencyInjection.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs)):

- `Bearer`: Altinn token, metadata from `AltinnOptions:OpenIdWellKnown`.
- `Maskinporten`: metadata **hard-coded** to `test.maskinporten.no` or `maskinporten.no` (L57-64). Used by the `NotificationCheck` and `Maintenance` policies.
- `DialogportenScheme`: EdDSA keys fetched once at startup and then every 12 h by `EdDsaSecurityKeysCacheService` from `{PlatformGatewayUrl}/dialogporten/api/v1/.well-known/jwks.json`.
- OIDC against the Altinn ID provider, used for the browser attachment download.
- Token lifetime is not validated in Development.

Outbound calls with the real clients, all relative to `PlatformGatewayUrl`. The paths are collected from
[`Integrations/Altinn`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn) and [`Dialogporten`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Dialogporten).

| Target | Calls | In localtest today |
| --- | --- | --- |
| Resource Registry | `resourceregistry/api/v1/resource/{id}`, `…/policy/rights`, `…/policy`. Used always, including in Dev mode (the real client is registered unconditionally, [L36](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L36)). It supplies the resource type (must be `CorrespondenceService`), the competent authority (org code and number), access packages for confidentiality, and the minimum authentication level. | **No HTTP service.** The PDP and Access Management have in-process stand-ins only (`components/authorization/fakes/Pip/LocaltestResourceRegistry.cs` reads `policies/resourceregistry/{id}/{policy.xml,resource.json}`). |
| PDP | `authorization/api/v1/authorize`. Senders are skipped when the token's `consumer` org equals the resource's competent authority and the token has `altinn:serviceowner` ([`AltinnAuthorizationService.cs` L146-190](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Authorization/AltinnAuthorizationService.cs#L146-L190)). Recipients always go through the PDP. | Vendored PDP. |
| Register | `register/api/v1/correspondence/parties/{query, main-units, {uuid}/roles/correspondence-roles}`. Needs scope `altinn:register/partylookup.admin`. | Vendored Register serves all three ([`CorrespondenceController.cs`](../localtest/components/register/vendor/src/apps/Altinn.Register/src/Altinn.Register/Controllers/CorrespondenceController.cs), policy `InternalOrPlatformAccess`). |
| Notifications | `notifications/api/v1/future/orders`, `…/future/orders/composed-email`, `…/future/shipment/{id}`, `orders/{id}/{cancel,status}`. Delivery is polled from 5 minutes after the send time, with backoff ([`SendNotificationOrderHandler.cs` L28, L150-154](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/SendNotificationOrder/SendNotificationOrderHandler.cs#L150-L154), [`CheckNotificationDeliveryHandler.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/CheckNotificationDelivery/CheckNotificationDeliveryHandler.cs#L228-L250)). Orders carry `ConditionEndpoint = {CorrespondenceBaseUrl}/correspondence/api/v1/correspondence/{id}/notification/check[/main]`, or null if the host is `localhost` ([`CreateNotificationOrderHandler.cs` L378-389](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/CreateNotificationOrder/CreateNotificationOrderHandler.cs#L378-L389)). | Missing (parallel strand). |
| Events | `events/api/v1/events` (cloud events `correspondence.*`, `attachment.*`, `notification.*`). | Missing (parallel strand). |
| Dialogporten | `dialogporten/api/v1/serviceowner/dialogs[...]` (create dialog or transmission, activities, system labels, purge). Publishing is a Hangfire **continuation of the dialog job**. | Missing (parallel strand). |
| Profile | `profile/api/v1/correspondence/units/contactpoint/lookup`, `…/organizations/notificationaddresses/lookup`. | Missing. |
| KRR | `{ContactReservationRegistryBaseUrl}/rest/v2/personer`, using a plain Maskinporten token. External. | n/a (external) |
| Storage SBL bridge | `storage/api/v1/sblbridge/*` (Altinn 2). Skipped when `DisableCallsToAltinn2=true` ([`AltinnStorageService.cs` L26](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Storage/AltinnStorageService.cs#L26)). | Off |
| Access Management | `resourceowner/authorizedparties`. The interface is registered but has no caller in `src` (grep). | Unused |
| Slack | Webhook. `SlackDevClient` is used when `SlackUrl` is empty. | Off |

## 3. Vendoring plan

Layout: `localtest/components/correspondence/{vendor/, fakes/, localtest.build.props, localtest.build.targets, Dockerfile, smoke-test.sh}`.

- `vendor/` is a pristine copy of `src/` (plus the root `Dockerfile` for reference) at `6dcf5619`, committed before any edit. The upstream repo has no root `Directory.Build.props`.
- The targets file compiles `fakes/**/*.cs` into `Altinn.Correspondence.API`. The interfaces live in Core, Application and Integrations, which the API host references, so the fakes could also be a separate project. Compiling into the host matches Storage.
- Image: built from the vendored API project, as in [`components/storage/Dockerfile`](../localtest/components/storage/Dockerfile). Runs with `ASPNETCORE_ENVIRONMENT=Development` on `http://+:2525`.
- Ingress: route `/correspondence/` to `correspondence:2525` without stripping the prefix. The controllers are routed with `correspondence/api/v1/...`.

### Directive sites (expected: 2, both in `Program.cs`)

| # | Where | Why | `#else` |
| --- | --- | --- | --- |
| 1 | [`Program.cs` L135-143](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs#L135-L143) | The Redis cache and an eager `ConnectionMultiplexer.Connect` for DataProtection are hard-wired outside DI. | `AddDistributedMemoryCache()` and `AddDataProtection().SetApplicationName(...)`. Single replica, so nothing has to be shared. |
| 2 | After `ConfigureServices(...)`, [`Program.cs` L51](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Program.cs#L51) | The fakes entry point. | `#if LOCALTEST Localtest.Correspondence.LocaltestOverrides.Apply(builder.Services, builder.Configuration); #endif` (additive, the same form as Storage's `#if LOCALTEST`). |

Not a directive:

- Migrations, the simulated scan and skipped Azure provisioning all come from `Development`.
- The Azure DB token is avoided by putting a password in the connection string.
- The Altinn 2 calls are switched off by setting.

Alternative to directive 1: a `redis:7-alpine` service in compose gives zero code change. It is rejected only because the
cache is trivial to replace and the container would be one more moving part. The product owner can choose
(open question 4).

### Fakes in `fakes/`, all reached through DI `Replace`

| Fake | Replaces | Behaviour |
| --- | --- | --- |
| `LocaltestMaskinportenService : IMaskinportenService` | `MaskinportenService` (registered with `TryAddSingleton`, [`ServiceCollectionExtensions.cs` L170](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Extensions/ServiceCollectionExtensions.cs#L170)) | Calls `GET authentication/api/v1/Home/GetTestOrgToken?org=digdir&orgNumber=991825827&scopes={clientDefinition.ClientSettings.Scope}`, which goes through the production exchange, and returns it as the `TokenResponse`. It caches until expiry. Every real platform client (`RegisterAltinnHttpClient`) then runs unchanged against `PlatformGatewayUrl=http://local.altinn.cloud:8000/`. |
| `LocaltestResourceRegistryService : IResourceRegistryService` | The HTTP client to `resourceregistry/api/v1` | Reads `resourceregistry/{id}/resource.json` (the Resource Registry `ServiceResource` JSON: `resourceType`, `hasCompetentAuthority`, `title`) and `policy.xml` from the **same folder the PDP's stand-in reads** (mounted read-only). Rights come from the policy rules, with access packages derived from the subjects. The minimum authentication level comes from the obligation. These 7 methods are the whole interface ([`IResourceRegistryService.cs`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Core/Services/IResourceRegistryService.cs)). If a real Resource Registry component appears later, delete this fake and point the HTTP client at it. |
| `FileStorageRepository : IStorageRepository` | Azure Blob | Stores files under `/data/attachments/{id}` with an MD5 checksum, as production computes it. Reports go under `/data/reports`. `GenerateSasUrl` returns the localtest download URL from section 4. |
| `NoopResourceManager : IResourceManager`, `IContainerAppRefreshService`, `IKeyVaultSecretStore` | Azure RM and Key Vault | No-ops, so the daily IP-restriction job succeeds instead of failing and retrying. |
| Hangfire storage | `JobStorage` from `ConfigureHangfire` | The same `PostgreSqlStorage`, but with `QueuePollInterval = 1 s`. Upstream's own test factory does the same ([`CustomWebApplicationFactory.cs` L60-84](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/Test/Altinn.Correspondence.Tests/Helpers/CustomWebApplicationFactory.cs#L60-L84)). The library default is 15 s (not confirmed for 1.21.1). Each correspondence runs 3 to 5 chained jobs, so the default would delay publishing by up to about a minute. |
| Auth options | `JwtBearerOptions` and `OpenIdConnectOptions` | `ConfigureAll(o => o.RequireHttpsMetadata = false)`. This must be `Configure`, not `PostConfigure`, because the framework's post-configure throws on http metadata. It also sets the `Maskinporten` scheme's `MetadataAddress` to the Authentication component's `authentication/api/v1/localtest/maskinporten/.well-known/oauth-authorization-server`. Then the condition-check endpoint accepts locally minted Maskinporten tokens without a directive around the hard-coded URL. |
| Per absent component (configuration-driven, see below) | `IEventBus`, `IAltinnNotificationService`, `IDialogportenService`, `IAltinnProfileService`, `IContactReservationRegistryService` | **Upstream's own Dev classes**, which are production source: `ConsoleLogEventBus`, `AltinnDevNotificationService`, `DialogportenDevService`, `AltinnProfileDevService`, `ContactReservationRegistryDevService`. No new fake code. KRR stays on the Dev class permanently because it is an external party. |

The approach does **not** use the global Dev switch (empty `ClientId`), because that would also replace the PDP and
Register with stubs that allow everything (`AltinnAuthorizationDevService` returns `true`) and with hard-coded Digdir
parties. Instead, `ClientId=localtest` turns on the real clients, and the fakes then pick, per integration, either
the real client against the stack or the upstream Dev class.

The selection is a configuration list, e.g. `Localtest__Correspondence__Integrations__Events=platform|dev` (likewise
for Notifications, Dialogporten and Profile). The defaults are `dev` until the matching component exists. Switching a
target to `platform` is a configuration change in `docker-compose.yml` and needs no code.

### How it talks to the other components once they exist

- **Events**: the real `AltinnEventBus` sends `POST events/api/v1/events` with the Altinn token (`altinn:events.publish`). This needs nothing beyond the Events component accepting that token. The event types are documented in the Events strand.
- **Notifications**: the real client sends `POST notifications/api/v1/future/orders`. Notifications then calls the condition endpoint back. Dependency: Notifications must call it with a token that has scope `altinn:system/notifications.condition.check`, issued by the local Maskinporten issuer, so that the reconfigured `Maskinporten` scheme accepts it ([`DependencyInjection.cs` L267](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs#L267)). Correspondence also **polls** `future/shipment/{id}` from 5 minutes after sending. That is production behaviour, kept as is (open question 2).
- **Dialogporten**: the real client calls the service-owner API with an Altinn-exchanged token that has the `digdir:dialogporten.serviceprovider*` scopes. Dependencies:
  - Dialogporten must accept that token.
  - Dialogporten must expose `dialogporten/api/v1/.well-known/jwks.json` at the ingress before Correspondence starts, because the key cache fetches once and then waits 12 h. Use compose `depends_on: service_healthy`.
  - `DialogportenSettings:Issuer` must equal Dialogporten's local issuer.
  - Risk: a failing dialog job holds back publishing until Hangfire's 10 default retries are used up (hours). The publish job is a continuation of the dialog job, and Hangfire only deletes it after the attempts run out ([`InitializeCorrespondencesHandler.cs` L210-219](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Application/InitializeCorrespondences/InitializeCorrespondencesHandler.cs#L210-L219)). Use `dev` until Dialogporten is proven.
- **Register and PDP**: real, now. Senders that match the resource owner skip the PDP. Recipient APIs go to the PDP with the resource policy from the shared folder.

### Configuration (compose environment, sketch)

```yaml
ASPNETCORE_ENVIRONMENT: Development
DatabaseOptions__ConnectionString: Host=postgres;Database=correspondence;Username=correspondence;Password=…
AltinnOptions__PlatformGatewayUrl: http://local.altinn.cloud:8000/
AltinnOptions__OpenIdWellKnown: http://local.altinn.cloud:8000/authentication/api/v1/openid/.well-known/openid-configuration
AltinnOptions__ArbeidsflateOriginsCommaSeparated: http://local.altinn.cloud:8000
MaskinportenSettings__ClientId: localtest            # real clients on; token from LocaltestMaskinportenService
DialogportenSettings__Issuer: http://local.altinn.cloud:8000/dialogporten/api/v1
GeneralSettings__CorrespondenceBaseUrl: http://local.altinn.cloud:8000/
GeneralSettings__ArbeidsflateBaseUrl: http://local.altinn.cloud:8000/
GeneralSettings__DisableCallsToAltinn2: "true"
GeneralSettings__MigrationWorkerCountPerReplica: "0"
GeneralSettings__WorkerCountPerReplica: "5"
GeneralSettings__EnableDownloadAll: "true"
Localtest__Correspondence__ResourcesPath: /policies   # same files as the PDP
```

Also needed:

- A Postgres init script for the `correspondence` database and its role, as `components/storage/postgres-init` does. Hangfire's schema comes from the EF migration `Hangfire_Setup`.
- Volumes for `/data`.
- The run-app step that publishes the app's correspondence resources (open question 1).

### Effort and risk

Effort: about 2 to 3 days to a green smoke test, which sends with attachments as the signing receipt does, reaches
`Published`, and reads back. Of that, about 1 day is the Resource Registry stand-in and resource seeding, and about
half a day is getting the auth options right. Wiring Events, Notifications and Dialogporten later is configuration
plus that component's own work.

| Risk | Note |
| --- | --- |
| Correspondence resources do not exist locally | Production resources are created in Studio's resource admin. The signing app names tt02 resource ids. Without seeding, every send fails with `InvalidResource`. |
| Hangfire latency and retries | 1 s polling (fake) and 5 s schedule polling, so publishing takes several seconds (not confirmed). When an integration fails, the default retries hide the failure for hours. Treat the logs and the GUI's job status as the signal. |
| Recipient policies | For recipient reads, the PDP needs a policy that permits the signee's role on the resource. The policy template has to be written, and it may differ from the real tt02 resource (not confirmed). |
| Upstream pace | 139 migrations and an active repository. Directive 1 sits in `Program.cs`, which changes often; expect merge conflicts there. |
| `AltinnCdnClient` in app-lib | The signing receipt fails offline, whatever Correspondence does. This is outside this component. |

Fake-only is the wrong call here. A contract-shaped stub would need the same four endpoints, but it would skip the
validation app developers hit in production:

- resource type, owner and confidentiality
- recipient lookup in Register
- notification template and content rules
- idempotency
- the attachment status machine the client polls

It would also leave the GUI with a home-grown data model. The vendoring cost is concentrated in the Resource Registry
stand-in, and that stand-in is needed for a stub too, unless the stub ignores resources entirely.

## 4. Inspection seam for testers

What the production APIs give a GUI:

- **As sender** (service-owner token from `GetTestOrgToken` with `altinn:correspondence.write`):
  - `GET correspondence/api/v1/correspondence?resourceId=…&role=Sender[&sendersReference=…]` lists the ids.
  - `GET …/{id}/details` returns content, the full status history, and notification orders with their recipients and status as Correspondence knows them.
  - `GET correspondence/api/v1/attachment/{id}/download` returns the file.
  - This requires knowing the resource ids, which the GUI can read from the same resources folder. This is exactly what app-lib's `GetStatus` uses.
- **As recipient**:
  - `GET correspondence/api/v1/correspondence?…&role=Recipient`, `…/{id}/content`, `markasread`, `confirm`.
  - These need a user Altinn token with scope `altinn:correspondence.read` ([`DependencyInjection.cs` L311-345](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/Auth/DependencyInjection.cs#L311)), or a Dialogporten dialog token.
  - The picker's portal tokens carry `DefaultPortalScopes` (`openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read`), so **today's user tokens are refused**.
  - Two fixes: the Authentication component mints an ID-porten-shaped token with that scope and runs it through the production `exchange/id-porten` (an end-user system path), or Dialogporten issues dialog tokens. Attachment download already accepts `altinn:portal/enduser`.

Neither path gives "everything published" without knowing the resources or the users. So the proposal is a small
read-only localtest API in `fakes/`, the same pattern as `register/api/localtest/*` and
`accessmanagement/api/localtest/*`:

- It is anonymous and served only by the localtest build.
- It reads through the production repositories and `ApplicationDbContext`, never its own store.
- It writes nothing, so it cannot change behaviour.

```
GET correspondence/api/localtest/correspondences?since={iso}&resourceId=&recipient=&sender=
  -> [{ id, resourceId, sender, recipient, sendersReference, created, requestedPublishTime,
        currentStatus, title, notificationOrders: n, attachments: n }]
GET correspondence/api/localtest/correspondences/{id}
  -> { ...above, content: { language, title, summary, body (markdown) }, statuses: [{status, at, text}],
       attachments: [{ id, fileName, status, size, downloadUrl }],
       notifications: [{ template, channel, emailSubject, emailBody, smsBody, recipients[], requestedSendTime,
                         isReminder, orderId, shipmentId, status }],   // the order request as sent to Notifications
       externalReferences: [{ type, value }],                           // dialog id when Dialogporten is wired
       pendingJobs: [{ job, state, attempts, lastError }] }             // from Hangfire tables: why is it not Published?
GET correspondence/api/localtest/attachments/{id}/content -> file stream through IStorageRepository
GET correspondence/api/localtest/stream                     -> text/event-stream of {correspondenceId, status, at}
```

`stream` comes from a DI decorator around `ICorrespondenceStatusRepository.AddCorrespondenceStatus` that publishes to
an in-process channel. It needs no directive, and the GUI does not poll. The decorator also covers statuses Correspondence
sets from Hangfire jobs, because all status writes go through that repository (not confirmed for bulk paths).
The notification texts shown here are what Correspondence **ordered**. What Notifications actually rendered and "sent"
belongs to the Notifications component's own capture API. The GUI should join the two on `orderId` and `shipmentId`.

## 5. Open questions for the product owner

1. **Correspondence resources.** Where do they come from? Options:
   - (a) A committed set of resource folders (`resource.json` plus `policy.xml`) for the ids test apps use.
   - (b) The app runner generating a default `CorrespondenceService` resource, owned by the app's org with orgnr 991825827, for every `<altinn:correspondenceResource>` in the app's BPMN.
   - (c) A real Resource Registry component.

   (b) matches "the runner pushes what Designer would have created", but it invents resources that exist only in tt02.
2. **Polling.** Is production-internal polling acceptable (Hangfire's own Postgres queue, Correspondence polling Notifications' shipment status 5 minutes after sending), given that "no polling between components" was about localtest's own wiring? Removing the shipment poll needs a directive or a fake `IAltinnNotificationService` wrapper.
3. **Recipient view.** Should the Authentication component issue user tokens with `altinn:correspondence.read` (production `exchange/id-porten`), so the GUI and testers can use the real recipient API? Or is the localtest read API in section 4 enough for the MVP?
4. **Redis.** Use a `#else` in-memory replacement (recommended), or a Redis container with zero code change?
5. **Dialogporten.** Should publishing be allowed to depend on it? If it fails, publishing stalls for hours on Hangfire retries. The proposal is `dev` until Dialogporten is proven, then switch by configuration.
6. **Cypress.** Should `signing.ts` stop accepting `varsling mislyktes` once Correspondence is in the stack, so a regression fails the test?
