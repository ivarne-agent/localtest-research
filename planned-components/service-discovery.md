# Service discovery for the components

**Superseded 2026-10-23 by task 27:** the runtime side is built (task 26: the localtest container's resolver without pass-through; task 27: manifest v2 without placeholders or host ports, every container aliased by its name and given `services__<name>__http__0` for every running HTTP container), so the `{{…}}` placeholders, `hostPort` and `Proxy__Upstreams__*` quoted below describe the v1 manifest; the per-component URL migrations of sections 6 and 9 are done since task 40 (below); see [`localtest/CONVENTIONS.md`](../localtest/CONVENTIONS.md).

**Status 2026-10-23 (task 40, phase 1, branch `feat/service-names`):** every component calls the others by service name
and runs without the pass-through provider, through one shared registration (`localtest/components/shared/fakes/ServiceDiscovery/`:
core, the configuration provider, and an explicit fallback for real hosts, because the configuration provider refuses
every host it has no entry for, altinncdn.no and the public origin included). The shared bases of Dialogporten and
Correspondence stay public and their requests are moved to the owning service by `LocaltestPrefixRouting`; flagged site
1 (Access Management's `ResourceRegistryClient`) goes through the factory; the BFF reaches the platform through the front
door by name. Proven on a stack on 2026-10-23 (phase 2): dev builds of the fifteen changed images with `--with
everything`: root 26/26, Register 14, Profile 52, Authentication 34, Authorization 31, Resource Registry 43, Events 27,
Notifications 64, Dialogporten 60, the adapter 39, Correspondence 78 (after the fix below), Receipt 27, Arbeidsflate 59,
shared 12, the localtest app 65 of 67 (the 503-for-an-off-group and `FRONTEND_DIST` checks, which need an off group and
the bundle); a browser instantiate of the sample app (sign-in, party, form, receipt) with its dialog in Dialogporten
(`Awaiting`, one transmission, three activities). `--minimal` with `--dev-localtest --dev-storage`: root 23 + 3 skipped,
a browser instantiate, and Storage's HttpClient log showing `POST http://authorization/…/decision` sent to
`http://localtest:8080/…` and `POST http://register/…/parties/lookup` likewise; its outbox drops the sync with "The
endpoint collection contains no endpoints". One fix came out of it: Correspondence's dialog-token key fetch used the
synchronous `HttpClient.Send`, which the resolving handler does not override, so the name went to DNS on port 80; it
uses `GetAsync` now. The tables below describe the state before.

## Status (2026-10-22)

- **Research done, prototype on a branch**, task 25: can every vendored component resolve the other services by name
  (`http://storage/`, `http://authentication/`, ...) through `Microsoft.Extensions.ServiceDiscovery`, so that the runtime
  injects `services__<name>__http__0` instead of a hand-written URL per setting.
- Written against this repository at `7583be9b` (main). The prototype is on branch `research/service-discovery`
  (commits from `d0d109c2` on). Package versions are from nuget.org on 2026-10-22: Microsoft.Extensions.ServiceDiscovery,
  `.Abstractions` and `.Yarp` **10.11.0** latest (published 2026-10-14); the prototype pins **10.8.0**, the version
  `Altinn.Authorization.ServiceDefaults` 6.0.0 already brings into Authentication and the Resource Registry.
- **Built and run:**
  - The prototype registration in every .NET component without one. Each was compile-checked with SDK 10.0.401, one
    build at a time, in a scratch copy of the Dockerfile's build context with the component's `localtest.build.props`
    and `.targets` (see [Build results](#5-build-results)). The scratch copies were deleted after each build.
  - The localtest app was also published Native AOT (linux-arm64, glibc; the image builds musl) with
    `TreatWarningsAsErrors`.
  - A resolver probe ([`service-discovery-probe/`](service-discovery-probe/), output in
    [`output.txt`](service-discovery-probe/output.txt)): a console app that drives a factory `HttpClient` and the YARP
    destination resolver against in-memory `services:*` configuration, with a capturing handler instead of the network.
- **Read only:** the component sources, Dockerfiles and the manifest (the per-component inventory used six read-only
  subagents; the line numbers were checked against the branch); the decompiled `Microsoft.Extensions.ServiceDiscovery`
  10.8.0, `.Yarp` 10.11.0, `Altinn.Authorization.ServiceDefaults` 5.5.0 and 6.0.0, `JWTCookieAuthentication` 4.0.4,
  `Altinn.AspNet.HealthChecks` 0.4.0 and `Aspire.Hosting` 13.6.2.
- **Not run:** no image builds, no Podman, no stack, no Aspire AppHost, no request between real containers (see
  [Not verified](#11-not-verified)).
- **Supersedes nothing.** One correction to the manifest's notes: Arbeidsflate's nginx resolves the BFF on every request
  (`nginx-start.sh` writes a `resolver`), not once at start.

## 1. Answer in short

- **The mechanism works for every .NET component**, but only for `HttpClient`s that come from `IHttpClientFactory`. That
  covers almost every server-to-server call in the stack.
- **Four components already have it in production code.** Register, Authentication, Access Management and the Resource
  Registry call `AltinnHost.CreateWebApplicationBuilder`. That runs `AddAltinnServiceDefaults`, which registers
  `AddServiceDiscovery()` and `ConfigureHttpClientDefaults(http => { AddStandardResilienceHandler(); AddServiceDiscovery(); })`.
  They need configuration only. The other ten .NET hosts and the localtest app got a three-line `#if LOCALTEST` block
  on this branch, and every one compiles.
- **What stops "only `services__*` in the environment" today is not the resolver; it is the settings.**
  - **Shared base URLs.** Most components derive every URL, public and internal, from one `Localtest__<X>__PlatformEndpoint`
    in their fakes. Three production settings serve several services at once and are also issuers or links:
    Correspondence's `AltinnOptions:PlatformGatewayUrl`, Dialogporten's `Infrastructure:Altinn:BaseUri` and the BFF's
    `PLATFORM_BASEURL`. Each has to be split before its clients can go direct.
  - **Metadata fetches cannot go direct.** Every token-metadata fetch (JwtCookie, JwtBearer, `HttpDocumentRetriever` in
    fakes) bypasses the factory. The document it fetches names a public `jwks_uri` anyway, so these stay on the public
    origin.
  - **Non-.NET clients need DNS names.** Node, nginx, the data stores and the clients that bypass the factory resolve
    through DNS. They need real network aliases and ports, not `services__*`.
- **One manifest assumption breaks.** "The runtime only injects `services__*` for `dependsOn`" does not hold, because the
  call graph has cycles: Storage ⇄ Authorization, Authorization ⇄ Access Management, Register ⇄ Authorization. The
  runtime must inject entries for every component it runs (or a separate `references` list), with `dependsOn` kept for
  start order.

## 2. Inventory per component

Classes used in the tables:

- **(a)** A typed or named client through `IHttpClientFactory`, including an `HttpClient` injected into a constructor,
  which resolves to the factory's default client.
- **(b)** A client the factory never sees: `new HttpClient`, a static client, or `HttpDocumentRetriever`.
- **(c)** Another transport: Npgsql, Redis, a bus, an Azure SDK client, an exporter, or a JWT metadata fetch.

"direct" means server-to-server with a platform token or no token, which can become `http://<service>/<prefix>/...`.
"public" means it must stay on `http://local.altinn.cloud:8000/` (`{platform}`), with the reason given.

Rules that apply to every component:

- **Prefixes are kept.** The front door forwards paths unchanged; only Dialogporten GraphQL has a rewrite. Each
  component serves under its own prefix through route attributes, without a `PathBase`, so a direct URL keeps the
  prefix: `http://register/register/api/v1/`.
- **Most settings live in the fakes.** Most URL settings are defaults that the component's fakes compute from
  `Localtest__<X>__PlatformEndpoint`. The manifest sets little more than that one value.
- **JwtCookie is public-only everywhere.** The `JwtCookie` metadata fetch (`GeneralSettings:OpenIdWellKnownEndpoint` or
  `OidcProviders:altinn:*`) is a (b) in every component that has it. JWTCookieAuthentication 4.0.4's
  `JwtCookiePostConfigureOptions` builds `new ConfigurationManager(..., new HttpDocumentRetriever())`, which uses
  IdentityModel's own static client, and `JwtCookieOptions` has no backchannel. Authentication builds its discovery
  document's `issuer` and `jwks_uri` from `GeneralSettings`, not from the request host
  (`authentication/.../Controllers/OpenIdController.cs:50-67`). A direct fetch would still return a public `jwks_uri`, so
  the key hop stays public in any case. The rows below call this "JwtCookie (public)".

### Register

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `RegisterHost.cs:187` → `Clients/AuthorizationClient.cs` | PDP `parties/{id}/validate` | `PlatformSettings:ApiAuthorizationEndpoint` (manifest `{platform}authorization/api/v1/`) | direct |
| a | `RegisterHost.cs:184-186` → `OrganizationClient`, `PersonClient`, `PartiesClient` | SBL bridge at22 (`PersonLookupService.cs:97` still reaches it) | `GeneralSettings:BridgeApiEndpoint` | external |
| a | `RegisterHost.cs:197-203` SystemUserImport, AccessManagementClient | `https+http://altinn-authentication/...`, `https+http://altinn-access-management/...` | hard-coded service-discovery names | direct; jobs off (`Altinn:IsTest`) |
| a | `RegisterHost.cs:214-224` NPR/SIRE, Maskinporten | external | hard-coded | external, jobs off |
| b | not found | | | |
| c | `RegisterHost.cs:142-148` JwtCookie | metadata | `GeneralSettings:OpenIdWellKnownEndpoint` (manifest) | public |
| c | Npgsql, MassTransit, Storage Queues, App Configuration, OTLP | | | off in localtest (in-memory fakes, `Altinn:IsTest`) |

Register's production code already uses service-discovery names (`https+http://altinn-authentication/`), the shape this
task proposes. Path: `localtest/components/register/vendor/src/apps/Altinn.Register/src/Altinn.Register/`.

### Profile

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Altinn.Profile.Integrations/ServiceCollectionExtensions.cs:57` → `Register/RegisterClient.cs` | `v2/internal/parties/*`, `v1/parties/identifiers` | `RegisterSettings:ApiRegisterEndpoint` (fakes `{platform}register/api/`) | direct |
| a | `:60` → `Notifications/NotificationsClient.cs` | `v1/future/orders/instant/*` | `NotificationsSettings:ApiNotificationsEndpoint` (fakes) | direct |
| a | `:61` and `Program.cs` `AddHttpClient<AuthorizationApiClient>` | PDP `parties/{id}/validate`, `decision` | `PlatformSettings:ApiAuthorizationEndpoint` (fakes) | direct |
| a | `fakes/Register/LocaltestRegisterTestData.cs:26,29` | Register `register/api/localtest/test-users`, `organizations`, `krr/...` | `Localtest:Profile:PlatformEndpoint` | direct, needs its own key |
| a | `:110` Maskinporten KRR client, `:120-121` brreg client | external | | replaced by fakes |
| b | not found | | | |
| c | `Program.cs:187` JwtCookie | | fakes `{platform}authentication/api/v1/openid/` | public |
| c | EF/Npgsql | Postgres | `PostgreSqlSettings:*` (manifest, alias) | data store |

Path: `localtest/components/profile/vendor/src/`.

### Authentication

`localtest/components/authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`. The
fakes force their values with `AddInMemoryCollection`, which beats the environment
(`fakes/LocaltestAuthenticationExtensions.cs:94`). `{internal}` is `Localtest:Authentication:InternalPlatformEndpoint`,
which defaults to `{platform}`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `AuthenticationHost.cs:161` → `Services/ProfileService.cs` | Profile `internal/user` | `PlatformSettings:ApiProfileEndpoint` (`{internal}profile/api/v1/`) | direct |
| a | `:139-160` → `PartiesClient`, `RegisterUserProvisioningClient` | Register v1 and v2 internal | `ApiRegisterEndpoint`, `ApiRegisterInternalEndpoint` (`{internal}`) | direct |
| a | `:163-188` → `AltinnNotificationClient` | `future/orders` | `ApiNotificationsEndpoint` (`{internal}`) | direct |
| a | `:138` → `ResourceRegistryClient` | Resource Registry | `ApiResourceRegistryEndpoint` (`{internal}`) | direct |
| a | `:136` PEP `AuthorizationApiClient` | PDP `decision` | `ApiAuthorizationEndpoint` (`{internal}`) | direct |
| a | `:137` → `AccessManagementClient` | system users | `AccessManagementSettings:ApiAccessManagementEndpoint` (**at22**, not overridden) | direct once pointed at the stack |
| a | `:197` → `Services/OidcProviderService.cs:230,242,261` | the localtest app's ID-porten token endpoint | `OidcProviders:idporten:TokenEndpoint` (`{internal}localtest/api/idporten/token`) | direct; the `redirect_uri` in the body stays public |
| a | `:162` → `Clients/OidcDownstreamLogoutClient.cs:53` | a client's front-channel logout URL | client registration (`http://af.local.altinn.cloud:8000/...`) | public (registered URL) |
| a | `:135` → `Services/OrganisationsService.cs` | altinncdn.no | | external |
| a | `fakes/Keys/LocaltestKeyProviders.cs:23-47` | ID-porten and Maskinporten metadata, then `jwks_uri` | `OidcProviders:idporten:WellKnownConfigEndpoint` (`{internal}`), Maskinporten well-known (`{platform}`) | ID-porten metadata direct-capable (the issuer check uses the configured issuer, `UpstreamTokenValidator.cs:115-150`); Maskinporten public (`AuthenticationController.cs:866-868` requires the URL to contain the token's `iss`) |
| b | JWTCookieAuthentication `JwtCookiePostConfigureOptions.cs:36-39`, `JwtCookieHandler.cs:275-278` (vendored package source under `vendor/src/pkgs/`) | own metadata, per-issuer metadata | `GeneralSettings:OpenIdWellKnownEndpoint` (`{internal}`, a loop through the front door) | public |
| b | `Helpers/ConfigurationMangerHelper.cs:25-28` | | | dead under LOCALTEST (its only user is replaced) |
| c | Npgsql (system-user repositories, `localhost`), Azure Queue, Key Vault, Blob | | | off or unused |
| c | Cookie domain, redirects, system-user confirm URLs, self-identified links | | `GeneralSettings:HostName`, `BaseUrl`, `PlatformEndpoint` | public |

### Authorization (PDP)

`localtest/components/authorization/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Program.cs` `AddHttpClient<AccessManagementClient>` | AM `policyinformation/*`, `authorizedparties` | `PlatformSettings:ApiAccessManagementEndpoint` (fakes `{platform}accessmanagement/api/v1/`) | direct |
| a | `Program.cs` → `RegisterService`, `ProfileClient`, `ResourceRegistryClient` | Register, Profile, Resource Registry | `ApiRegisterEndpoint`, `ApiProfileEndpoint`, `ApiResourceRegistryEndpoint` (fakes) | direct |
| a | `Repositories/InstanceMetadataRepository.cs:27,31` (a singleton holding the default client) | Storage `instances/{id}/process/authinfo` | `ApiStorageEndpoint` (fakes) | direct |
| a | `fakes/Controllers/LocaltestDecisionController.cs:74-75` | its own `decision` (self-call) | the inbound `Request.Host` | direct (could be loopback) |
| a | `RolesClient` (SBL bridge), `OedAuthzClient` + Maskinporten | | | dead or replaced |
| b | `Services/Implementation/DevAccessTokenGenerator.cs:42` `new HttpClient()` | test-tool token generator | `TokenGeneratorSettings:Url` | inactive (replaced in fakes) |
| b | JwtCookie, `Program.cs:242` | | `GeneralSettings:OpenIdWellKnownEndpoint` (fakes) | public |
| c | Azure Blob, Queue, Key Vault, App Insights | | | replaced or off |

### Access Management

`localtest/components/access-management/vendor/src/apps/Altinn.AccessManagement/src/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Altinn.AccessManagement/AccessManagementHost.cs:233` PEP `AuthorizationApiClient` | PDP `decision` | `PlatformSettings:ApiAuthorizationEndpoint` (fakes) | direct |
| a | `Altinn.AccessManagement.Integration/Extensions/IntegrationDependencyInjectionExtensions.cs:32-35` → `PartiesClient`, `ProfileClient`, `AccessListAuthorizationClient`, `AuthenticationClient` | Register, Profile, PDP, Authentication | `ApiRegisterEndpoint`, `ApiProfileEndpoint`, `ApiAuthorizationEndpoint`, `ApiAuthenticationEndpoint` (fakes) | direct |
| a | `libs/Altinn.Authorization.Integration/.../Register/AltinnRegisterClient.cs:20`, `ResourceRegistry/AltinnResourceRegistryClient.cs:17` | Register streams, RR `orgs`, `updated`, `resource/*` | `Platform:Register:Endpoint`, `Platform:ResourceRegistry:Endpoint` (fakes `{platform}`) | direct; the paginator follows the server's absolute `links.next` |
| a | `fakes/Sync/LocaltestRefreshService.cs:84-85` | Register `register/api/localtest/test-users` | `Localtest:AccessManagement:PlatformEndpoint` | direct, needs its own key |
| a | SBL bridge, Notifications, test-tool clients | | | dead or replaced |
| **b** | **`Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs:25`** `private readonly HttpClient _httpClient = new();` (base address `:40`; singleton at `IntegrationDependencyInjectionExtensions.cs:37`) | RR `v1/resource/{id}`, `search`, `resourcelist`, `bysubjects` (consent, requests, instance delegation) | `PlatformSettings:ApiResourceRegistryEndpoint` (fakes `{platform}resourceregistry/api/`) | direct-capable, **bypasses the resolver** |
| b | `fakes/Infrastructure/LocaltestOidcMetadata.cs:32` `HttpDocumentRetriever` | metadata | `OidcProviders:altinn:WellKnownConfigEndpoint` | public (the document's issuer must equal `iss`; `jwks_uri` public) |
| b | JwtCookie fallback `AccessManagementHost.cs:356` | | `OidcProviders:altinn:Issuer` | public (it is the issuer) |
| c | Npgsql and EF (`Localtest:AccessManagement:DatabaseHost`, default `postgres`), Blob, Key Vault | | | data store or replaced |
| c | Browser links `https://am.ui.{Hostname}/…` (`Core/Services/ConsentService.cs:862`) | | `GeneralSettings:Hostname` | public |

### Resource Registry

`localtest/components/resource-registry/vendor/src/apps/Altinn.ResourceRegistry/src/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `fakes/Applications/LocaltestApplicationsClient.cs:28,43` (replaces `ApplicationsClient`) | Storage `applications` | `PlatformSettings:StorageApiEndpoint` (fakes) | direct |
| a | `Altinn.ResourceRegistry.Integration/ResourceRegistryIntegrationDependencyInjectionExtensions.cs:28-35` → `RegisterClient` + `PlatformAccessTokenHandler` | Register `parties/identifiers` | `ApiRegisterEndpoint` (fakes) | direct |
| a | `Altinn.ResourceRegistry/ResourceRegistryHost.cs:97` → `OrgListClient` | altinncdn.no | | external |
| b | `fakes/Infrastructure/LocaltestOidcMetadata.cs:32` `HttpDocumentRetriever` | metadata | `OidcProviders:altinn:WellKnownConfigEndpoint` | public |
| c | Npgsql | Postgres | manifest (alias) | data store |

Links handed out: `ResourceController.cs:575,626` builds `links.next` with `Url.Link` from the request host. A direct
caller gets a direct-host link, which is right for a server-side paginator.

### Storage

`localtest/components/storage/vendor/src/Storage/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Program.cs` PEP `AuthorizationApiClient` | PDP `decision` | `PlatformSettings:ApiAuthorizationEndpoint` (fakes `LocaltestStorageExtensions.cs:53`) | direct |
| a | `Program.cs` → `Services/RegisterService.cs` | Register `parties/*` | `RegisterServiceSettings:ApiRegisterEndpoint` (fakes `:55`) | direct |
| a | `fakes/Dialogporten/LocaltestDialogportenAdapterClient.cs:38,53` | adapter `storage/dialogporten/api/localtest/enqueue` | `Localtest:Storage:DialogportenAdapterEndpoint` (fakes `:65`) | direct (an unreachable adapter is already "not running") |
| a | `Clients/OndemandClient.cs`, `PdfGeneratorClient.cs`, `PartiesWithInstancesClient.cs`, `CorrespondenceClient.cs` | A2 only | | dead locally (`DisableA2Endpoints=true`) |
| a | `Repository/AltinnCdnOrganisationRepository.cs:74-76` | altinncdn.no | | external |
| b | not found | | | |
| c | JwtCookie `Program.cs:285` | | `GeneralSettings:OpenIdWellKnownEndpoint` (fakes) | public |
| c | Npgsql; Blob, Queue, Key Vault, Service Bus | | | data store; the rest replaced or off |

Public settings: `GeneralSettings:Hostname`, which builds the self links that are stored and handed out
(`InstancesController.cs:79`, `DataController.cs:87`). `BuildRequestLink` (`InstancesController.cs:1465-1471`,
LOCALTEST) uses the request host, so a direct caller gets a direct-host "next" link.

### Events

`localtest/components/events/vendor/src/Events/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Program.cs` PEP `AuthorizationApiClient` | PDP | `PlatformSettings:ApiAuthorizationEndpoint` (fakes) | direct |
| a | `Program.cs` → `Services/RegisterApiClient.cs` | Register v1 and v2 internal | `PlatformSettings:RegisterApiBaseAddress` (fakes) | direct |
| a | `Services/WebhookService.cs:35,49` + `fakes/.../LocaltestWebhookDeliveryHandler` | subscriber webhooks (absolute URLs from subscriptions) | subscription data | must pass through unchanged (see [section 3](#3-the-registration)) |
| b | not found | | | |
| c | JwtCookie `Program.cs:220` | | fakes | public |
| c | Npgsql; Queues, Service Bus, Key Vault, App Insights | | | data store; the rest replaced or off |

Public settings:

- `GeneralSettings:BaseUri`: the "next" links.
- `PlatformSettings:ApiEventsEndpoint`: the CloudEvent `source`, which is stored and sent.
- `PlatformSettings:AppsDomain`: a host comparison.

### Notifications

`localtest/components/notifications/vendor/components/api/src/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Altinn.Notifications.Integrations/Extensions/ServiceCollectionExtensions.cs:43,44,58` → Profile, Register, PEP | Profile contact points, Register names, PDP | `ApiProfileEndpoint`, `ApiRegisterEndpoint`, `ApiAuthorizationEndpoint` (fakes) | direct |
| a | `:46,47` SMS and e-mail clients | its own host | `http://localhost:5090/...` | loopback, unchanged |
| a | `:64` `SendConditionClient` + Maskinporten | the apps' condition endpoints (stored order URLs) | order data | must pass through unchanged |
| a | `fakes/Maskinporten/LocaltestMaskinportenService.cs:57-60,106-115` | the localtest app's fake Maskinporten `token`; Authentication `exchange/maskinporten` | `Localtest:Maskinporten:Issuer` (used only as the token URL), `ExchangeEndpoint` | direct (the token's `iss` comes from the localtest app's own configuration, `localtest/Maskinporten/MaskinportenApi.cs:159-164`) |
| a | `:45` Dialogporten client | | | off (`Enabled=false`) |
| b | not found | | | |
| c | JwtCookie `Program.cs:213` | | fakes | public |
| c | Npgsql; Service Bus, Key Vault, OTLP | | | data store; the rest replaced or off |

Public settings: `GeneralSettings:BaseUri` builds the response links.

### Receipt

`localtest/components/receipt/vendor/src/backend/Altinn.Receipt/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Program.cs:226-228` → `RegisterWrapper`, `StorageWrapper`, `ProfileWrapper`; `fakes/LocaltestReceipt.cs:31` | Register, Storage, Profile with the user's JWT and a platform token | `PlatformSettings:ApiRegisterEndpoint`, `ApiStorageEndpoint`, `ApiProfileEndpoint` (manifest) | direct |
| b | `Clients/HttpClientAccessor.cs:78` `new HttpClient { BaseAddress = ... }` | Storage, Register, Profile | `PlatformSettings:Api*Endpoint` | dead code (`IHttpClientAccessor` is never registered) |
| b | JwtCookie `Program.cs:207` | | `GeneralSettings:OpenIdWellKnownEndpoint` (manifest) | public |
| c | Browser redirect to Authentication | | `PlatformSettings:ApiAuthenticationEndpoint` (manifest) | public |
| c | `fakes/SelfLinksStorage.cs` rewrite | | `Localtest:Receipt:PlatformEndpoint`, `GeneralSettings:Hostname` | public (browser links) |

### Correspondence

`localtest/components/correspondence/vendor/src/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Altinn.Correspondence.Integrations/DependencyInjection.cs:61-69` via `RegisterAltinnHttpClient` `:93-101` (Maskinporten handler + Polly retry) | PDP, Resource Registry, Register, Profile, AM, Events, Notifications, Dialogporten, Storage: nine clients | `AltinnOptions:PlatformGatewayUrl` (one base for all; fakes `{platform}`) | direct, but the same setting is the OIDC authority and the expected issuer (see below) |
| a | `Integrations/Altinn/ResourceRegistry/ResourceRegistryService.cs:26` sets `BaseAddress` in its constructor | Resource Registry | `PlatformGatewayUrl` | direct; a factory override cannot replace it |
| a | `Integrations/Altinn/Notifications/AltinnNotificationService.cs:97,110` | follows `Links.Self` from Notifications | Notifications' `BaseUri` | follows a public link |
| a | `DependencyInjection.cs:70,143-154` KRR | Register's KRR stand-in | `GeneralSettings:ContactReservationRegistryBaseUrl` (fakes) | direct |
| a | `Altinn.Correspondence.API/Auth/EdDsaSecurityKeysCacheService.cs:61-63`; `fakes/Auth/LocaltestDialogTokenKeys.cs:27,64` | Dialogporten JWKS | `PlatformGatewayUrl` + path | direct (the issuer is checked against `DialogportenSettings:Issuer`, not the URL) |
| a | `fakes/Maskinporten/LocaltestMaskinportenService.cs` | fake Maskinporten, exchange | as in Notifications | direct |
| b | `Integrations/Slack/SlackDevClient.cs:12` `httpClient ?? new HttpClient()` | nothing (`SlackUrl` empty) | | inert |
| b | `Integrations/Azure/AzureResourceManagerService.cs:312` `new HttpClient()` | management.azure.com | | inert (replaced) |
| b | `API/Auth/IdportenTokenValidator.cs:27,30` `HttpDocumentRetriever` | Authentication metadata and JWKS | derived from `PlatformGatewayUrl` | public (shares the authority with the browser flow) |
| b/c | JwtBearer ×3 and OpenIdConnect backchannels (`API/Auth/DependencyInjection.cs:35-112`) | metadata and JWKS; token and userinfo | `AltinnOptions:OpenIdWellKnown`, `DialogportenSettings:Issuer`, `PlatformGatewayUrl` | public (issuers, `redirect_uri`, cookies) |
| c | Npgsql + Hangfire, Redis | | manifest (aliases) | data stores |
| c | Stored and handed-out links: dialogs, event source, notification condition URL, blob URLs | | `GeneralSettings:CorrespondenceBaseUrl`, `Localtest:Correspondence:PlatformEndpoint` | public |

### Dialogporten (WebApi, GraphQL, Service)

`localtest/components/dialogporten/vendor/src/`. All three hosts call `AddInfrastructure` and get every client in this
table. Only WebApi and GraphQL have JWT metadata.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs:525-531` → `AltinnEventsClient` | Events | `Infrastructure:Altinn:EventsBaseUri` (fakes `{platform}`) | direct |
| a | `:532-564` → Resource Registry, AM metadata, Register names, PDP + AM authorized parties (Polly v7 policies, Maskinporten handler) | four services | `Infrastructure:Altinn:BaseUri` (fakes `{platform}`), **one base for four services** | direct-capable, but the same setting builds links returned to clients (`MetadataLinkProvider.cs:14-24`) |
| a | `:536-542` → altinncdn.no | | | external |
| a | `fakes/Infrastructure/Maskinporten/LocaltestMaskinportenService.cs` | fake Maskinporten, exchange | `Localtest:Maskinporten:Issuer`, `ExchangeEndpoint` | direct |
| a | `Digdir.Library.Utils.AspNet/DialogportenHealthCheckExtensions.cs:70-94` URI probes (`/health/deep` only) | altinncdn, AM, the JWT well-known URLs | as configured | follows its settings |
| b | JwtBearer backchannel (`WebApi/Common/Authentication/AuthenticationBuilderExtensions.cs:36-38`, GraphQL `:41-43`) | metadata and `jwks_uri` | `*:Authentication:JwtBearerTokenSchemas:*:WellKnown` (fakes) | public |
| b | `fakes/WebApi/Authentication/LocaltestTokenIssuerCache.cs:58`, `fakes/GraphQL/Authentication/LocaltestTokenIssuerCache.cs:56` `HttpDocumentRetriever` | the same well-known documents | same | public in effect (`jwks_uri` public) |
| c | Npgsql, Redis (cache, backplane, subscriptions) | | fakes `Host=postgres`, `redis:6379` | data stores |
| c | MassTransit (in-memory), OTLP, App Configuration | | | off |

Public settings:

- `Application:Dialogporten:BaseUri`: the dialog-token `iss` and the event source.
- `Infrastructure:Altinn:BaseUri`: the links it builds.
- `WebApi:OpenApi:*`.
- The GraphQL CORS origin.

### Dialogporten adapter

`localtest/components/dialogporten-adapter/vendor/src/Altinn.DialogportenAdapter.WebApi/`.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Common/Extensions/ServiceCollectionExtensions.cs:263-282` Refit `IStorageApi`, `IApplicationsApi` | Storage | `DialogportenAdapter:Altinn:InternalStorageEndpoint` (fakes `{platform}`) | direct (upstream already separates it from the public base) |
| a | `:284-292` Refit `IDialogportenApi`; `:256-261` `AddDialogportenClient` (package, Refit, JWKS) | Dialogporten WebApi | `DialogportenAdapter:Dialogporten:BaseUri` (fakes `{platform}dialogporten`) | direct |
| a | `:294-303` Refit `IRegisterApi` | Register | `DialogportenAdapter:Altinn:InternalRegisterEndpoint` | direct |
| a | `:305-311` altinncdn | | | external |
| a | `fakes/Maskinporten/LocaltestMaskinportenService.cs` | fake Maskinporten, exchange | as above | direct |
| b | JwtBearer backchannel `:326-339` | Authentication metadata | `DialogportenAdapter:Authentication:JwtBearerWellKnown` | public |
| c | Wolverine (local queue under LOCALTEST), Azure Monitor, Key Vault | | | off |

Public settings: `DialogportenAdapter:Altinn:BaseUri` and `DialogportenAdapter:Adapter:BaseUri`, the links stored in
dialogs.

### The localtest app

`localtest/localtest/` (own code, Native AOT).

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| a | `Program.cs` named `localtest-authentication` → `Home/AuthenticationClient.cs:53-92`, `Authentication/CodeFlow.cs:73-77` | Authentication sign-in, exchange, token | `Proxy:Upstreams:authentication` (manifest `{{url:app}}`) | direct: the client sets `Host` to the public host itself (`AuthenticationClient.cs:124`), and the resolver keeps an explicit `Host` |
| a | `IdPorten/TestUsers.cs:18,35-36` | Register `register/api/localtest/test-users` | `Proxy:Upstreams:register` | direct |
| **b** | **`Proxy/Forwarder.cs:15`** `private static readonly HttpMessageInvoker Client = new(new SocketsHttpHandler{...})`, used at `:76` | every forwarded request (its own proxy mode, not under studioctl) | `Proxy:Upstreams:*`, app routes | bypasses the resolver |
| c | Issuers and `redirect_uri` checks (ID-porten, Maskinporten) | | `Localtest:PlatformEndpoint` | public |

### Arbeidsflate (SPA behind nginx, Node BFF)

`localtest/components/arbeidsflate/vendor/packages/bff/src/`. Node has no resolver: every call is plain `axios` or
`fetch` against a URL built from an environment variable, resolved through container DNS.

| Kind | Site | Calls | Setting (value today) | Class |
| --- | --- | --- | --- | --- |
| HTTP | `auth/oidc.ts:123`, `auth/verifyToken.ts:104-107`, `azure/HealthChecks.ts:77` (patch 0001) | discovery document | `OIDC_PLATFORM_URL` (`{platform}authentication/api/v1/openid`) | fetch direct-capable; the endpoints in the document are public |
| HTTP | `auth/oidc.ts:266`, `auth/verifyToken.ts:24` | token endpoint from discovery | none | public unless patched |
| redirect | `auth/oidc.ts:157-160,357-360`; `redirect_uri` `${HOSTNAME}/api/cb` | browser | `HOSTNAME` | public |
| HTTP | `graphql/api.ts:22-24` and others | Dialogporten GraphQL with the user's token | `DIALOGPORTEN_URL` + `/graphql` | direct as `http://dialogporten-graphql:8080/graphql` (no prefix) |
| HTTP | `azure/HealthChecks.ts:91` | Dialogporten `/health` | `DIALOGPORTEN_URL` + `/health` | direct as `http://dialogporten-webapi:8080/dialogporten/health`; this differs from the GraphQL URL, so the variable must be split |
| HTTP | Profile, Notifications, Register, Correspondence, Resource Registry, Authentication `exchange/maskinporten` (service files under `graphql/`, `auth/maskinporten.ts:95`) | six services | `PLATFORM_BASEURL` (`{{url:app}}`), **one base** | direct-capable only by path routing |
| HTTP | `auth/maskinporten.ts:69` | fake Maskinporten `token` | `MASKINPORTEN_ISSUER` (also the assertion's `aud`, which the fake ignores) | direct |
| c | ioredis, pg/TypeORM | | `REDIS_CONNECTION_STRING`, `DB_CONNECTION_STRING` (aliases) | already direct |
| nginx | `nginx/nginx.conf:37-38` `/api/` → `http://arbeidsflate-bff:80` through a variable, resolver from `nginx-start.sh:9-11` | BFF | | already direct |
| SPA | `window.dialogportenStreamUrl` (upstream `start.sh:7`) | the browser's SSE stream | `DIALOGPORTEN_STREAM_URL` | public |

Also public: `AUTH_CONTEXT_COOKIE_DOMAIN` and the CSP's hard-coded `http://local.altinn.cloud:8000`
(`nginx.conf:29`).

### Not built here: pdf3 and the workflow engine

- **pdf3** (Go, `altinn-studio/src/Runtime/pdf3` at `447e53eb97`) has no HTTP client. Headless Chromium navigates to the
  app page with the user's `local.altinn.cloud` cookies (`internal/generator/browser_session.go:856`). It stays on
  `PDF3_LOCALTEST_PUBLIC_BASE_URL={{url:app}}` and needs `local.altinn.cloud` to resolve to the host in its container.
- **The workflow engine** (vendor pin `447e53eb`) already registers `ConfigureHttpClientDefaults`
  (`WorkflowEngine.Core/Extensions/ServiceCollectionExtensions.cs:37-44`).
  - One `.AddServiceDiscovery()` there would cover both its clients: the app callback and webhooks.
  - Its only configured target is the app (`AppCommandSettings:CommandEndpoint`), a process on the host, so nothing
    changes until the app has a service name.
  - Its webhook URIs come from the enqueuing app.

### Flagged: HTTP clients outside the factory

These never see the resolver. Per the brief, none was changed. Each has a proposed minimal change.

| # | Site (from `localtest/`) | Calls | Status | Proposed minimal change |
| --- | --- | --- | --- | --- |
| 1 | `components/access-management/vendor/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs:25` | Resource Registry (consent, requests, instance delegation) | **active** | Two directive sites in the same file. The field becomes `#if !LOCALTEST private readonly HttpClient _httpClient = new(); #else private readonly HttpClient _httpClient; #endif`. The constructor (`:37`) gets `IHttpClientFactory httpClientFactory` under `#else` and sets `_httpClient = httpClientFactory.CreateClient(nameof(ResourceRegistryClient));` before `:40`. The singleton registration (`IntegrationDependencyInjectionExtensions.cs:37`) stays; the container can resolve the factory. |
| 2 | `localtest/Proxy/Forwarder.cs:15` (own code) | everything the localtest app's own proxy forwards | active only without studioctl | Wrap the handler: `new HttpMessageInvoker(new ResolvingHttpDelegatingHandler(resolver, options, new SocketsHttpHandler{...}))`, with `HttpServiceEndpointResolver` from DI. Or let the later YARP front door replace this forwarder with `AddServiceDiscoveryDestinationResolver` ([section 8](#8-the-front-doors-resolver)). |
| 3 | `components/correspondence/vendor/src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs:27,30` | Authentication metadata and JWKS | active | None: the authority is shared with the browser flow, and `jwks_uri` is public. |
| 4 | JwtCookie metadata in Register, Profile, Authentication, Authorization, Access Management, Resource Registry, Storage, Events, Notifications, Receipt (JWTCookieAuthentication 4.0.4, package code; vendored under `components/authentication/vendor/src/pkgs/`) | Authentication metadata and JWKS | active | None. Keep `{platform}`. A factory client would only move the metadata hop, because the document's `jwks_uri` is public. |
| 5 | `components/access-management/fakes/Infrastructure/LocaltestOidcMetadata.cs:32`, `components/resource-registry/fakes/Infrastructure/LocaltestOidcMetadata.cs:32`, `components/dialogporten/fakes/WebApi/Authentication/LocaltestTokenIssuerCache.cs:58`, `components/dialogporten/fakes/GraphQL/Authentication/LocaltestTokenIssuerCache.cs:56` (fakes) | metadata | active | None, for the reason in row 4. If wanted, `new HttpDocumentRetriever(factory.CreateClient())` is a one-line fake change. |
| 6 | JwtBearer and OpenIdConnect backchannels in Dialogporten WebApi and GraphQL, the adapter and Correspondence (framework `new HttpClient(BackchannelHttpHandler ?? new HttpClientHandler())`) | metadata, token, userinfo | active | None, for the reason in row 4. |
| 7 | `components/receipt/vendor/src/backend/Altinn.Receipt/Clients/HttpClientAccessor.cs:78` | Storage, Register, Profile | dead (never registered) | None |
| 8 | `components/correspondence/vendor/src/Altinn.Correspondence.Integrations/Slack/SlackDevClient.cs:12`, `.../Azure/AzureResourceManagerService.cs:312` | Slack, Azure Resource Manager | inert or replaced | None |
| 9 | `components/authorization/vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/DevAccessTokenGenerator.cs:42` | test-tool token generator | replaced in the fakes | None |
| 10 | `components/authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ConfigurationMangerHelper.cs:25-28` | metadata | dead under LOCALTEST | None |

## 3. The registration

### What plugs in where

The recipe is the one Aspire's service defaults use, and the one `AddAltinnServiceDefaults` already runs (decompiled
`Altinn.Authorization.ServiceDefaults` 5.5.0 and 6.0.0, `AltinnServiceDefaultsExtensions.AddAltinnServiceDefaults`):

```csharp
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
```

Both calls are in `Microsoft.Extensions.ServiceDiscovery` 10.8.0, as decompiled:

- **`AddServiceDiscovery()`** is `AddServiceDiscoveryCore()` plus two endpoint providers:
  - the **configuration provider**, which reads section `Services:<name>`;
  - the **pass-through provider**, which returns the name itself for DNS.
- **`IHttpClientBuilder.AddServiceDiscovery()`** adds a `ResolvingHttpDelegatingHandler`.
  - The handler resolves the request URI's authority, sends to the endpoint it picks, and restores the original URI
    after the call.
  - It sets `Host` only when the request has none and the endpoint carries a host-name feature. The configuration
    provider attaches that feature only when `ShouldApplyHostNameMetadata` says so (default: false). In practice the
    `Host` header comes from the resolved URI, except where the caller set one: the localtest app's Authentication
    client relies on this.

### Configuration shape

`services__<name>__<scheme>__<index>` in the environment is configuration key `Services:<name>:<scheme>:<index>`; keys
are case-insensitive. The value is a URI, which may carry a path. The probe
([`service-discovery-probe/output.txt`](service-discovery-probe/output.txt)), against 10.8.0, showed:

| Request | Configuration | Sent to |
| --- | --- | --- |
| `http://storage/storage/api/v1/applications` | `services:storage:http:0 = http://localtest-v2-storage:5010` | `http://localtest-v2-storage:5010/storage/api/v1/applications` |
| `http://envform/x` | environment `services__envform__http__0=http://envform-host:5` | `http://envform-host:5/x` |
| `http+https://both/x` | `http` and `https` entries | the `http` one (the first scheme listed that has an entry wins) |
| `https+http://both/x` | `http` and `https` entries | the `https` one |
| `http+https://onlyhttps/x` | `https` entry only | `https://onlyhttps-host:3/x` |
| `http://onlyhttps/x` | `https` entry only | unchanged, `http://onlyhttps/x` (pass-through) |
| `http://withpath/storage/x` | `http://withpath-host:4/base` | `http://withpath-host:4/base/storage/x` (the endpoint's path is prepended) |
| `http://local.altinn.cloud:8000/storage/...` | none | unchanged (pass-through), so public URLs keep working through the same clients |

Recommendation: write `http://<service>/...` in the images' settings and inject `services__<service>__http__0`. The
`http+https://` form only matters once the stack serves https inside the network, which it does not; see
[`aspire/https.md`](aspire/https.md).

Two cautions about names:

- **Hyphens.** Service names such as `resource-registry` and `dialogporten-webapi` contain hyphens, so the variable is
  `services__resource-registry__http__0`. .NET reads it, and Aspire uses the same form. I did not run Podman with it.
- **Name collisions.** A name that equals an existing DNS name is resolved by configuration first.

### A name with no entry

| Registration | What happens | When | Type |
| --- | --- | --- | --- |
| `AddServiceDiscovery()` (configuration + pass-through), what every component on this branch uses | The name passes through unchanged, so the request goes to `http://register:80` through DNS. Without a DNS name or a listener on 80 it fails like any unreachable host. | At the request, not at start; resolution is lazy and cached per authority. | Whatever `SocketsHttpHandler` throws: `HttpRequestException`. Not run against a real network. |
| `AddServiceDiscoveryCore()` + configuration provider only | No endpoint. The handler throws (probe: `System.InvalidOperationException: The endpoint collection contains no endpoints`). | At the request | `InvalidOperationException` |
| The YARP resolver with configuration only | The cluster resolves to **0 destinations** (probe) | When the proxy configuration loads, and again on configuration change | none; see [section 8](#8-the-front-doors-resolver) |
| The YARP resolver with pass-through | One destination `http://register/` (probe) | | DNS failure at the proxied request |

So for the components, an optional group that is off behaves like today's "not running":

- Storage's adapter client already treats `HttpRequestException` as "not running"
  (`fakes/Dialogporten/LocaltestDialogportenAdapterClient.cs`).
- Clients that do not expect it log their usual failure.

For the front door, register only the configuration provider, so that an absent group is an empty cluster, not a DNS
lookup.

Configuration reloads through change tokens. The 60-second `RefreshPeriod` applies to providers that poll; the
configuration provider uses the configuration's reload token.

### Package and versions

Every vendored host targets **net10.0**. The multi-targeted `net8.0`/`net9.0` projects are libraries under `pkgs/`.
`Microsoft.Extensions.ServiceDiscovery` 10.x ships `net8.0`, `net9.0`, `net10.0`, `netstandard2.0` and `net462` assets,
so one version serves all.

- Its `net10.0` dependency is `Microsoft.Extensions.Http` ≥ 10.0.8 (10.8.0) or ≥ 10.0.13 (10.11.0). Notifications pins
  `Microsoft.Extensions.Http` 10.0.12 centrally, so 10.11.0 would need that pin raised there; 10.8.0 does not.
- The prototype uses **10.8.0**. That is what Authentication and the Resource Registry already get from
  ServiceDefaults 6.0.0.
- Register, Authorization and Access Management get **9.5.2** from ServiceDefaults 5.5.0. Authorization's new block
  compiles against that 9.5.2; nothing was added to its packages.

### How the package reference is added

`#if` does not work in a `.csproj`, and no component added a package before this task. The fakes enter through
`localtest.build.targets` (`DirectoryBuildTargetsPath`). The package goes in the same file, in the host's existing
`ItemGroup`, conditioned on `MSBuildProjectName`. There are three cases:

| Case | Components | How |
| --- | --- | --- |
| No central package management, no lock file | Storage, Events, Profile, Receipt, Correspondence, the adapter | `<PackageReference Include="Microsoft.Extensions.ServiceDiscovery" Version="10.8.0" />` |
| Central package management | Notifications (`CentralPackageTransitivePinningEnabled`) | the same with `VersionOverride="10.8.0"` (`Version` would be NU1008) |
| Locked-mode restore | Dialogporten (`RestorePackagesWithLockFile`, Dockerfiles `restore --locked-mode`) | Adding the reference fails the locked restore with NU1004 (reproduced). Locked mode is off instead: `RestoreLockedMode=false` in the targets (our pin is the upstream commit, so the lock files guard nothing here), and the three Dockerfiles drop `--locked-mode` from their restore with a `localtest:` comment, because the command-line flag is a global property that beats the targets (reproduced: NU1004 with the property alone). `RestorePackagesWithLockFile=false` cannot be used: with the vendored lock files present it fails with NU1005 (reproduced). The restore rewrites the host's lock file inside the build stage only. |
| Already present | Register, Authentication, Access Management, Resource Registry (registration and package); Authorization (package only, transitively from ServiceDefaults 5.5.0) | nothing, or code only |

The localtest app is our own code, so it gets a plain `PackageReference` in `Localtest.csproj` and no directive.

### Conflicts with what the components register

- **Double registration.** In the four `AltinnHost` components, a second `ConfigureHttpClientDefaults(...AddServiceDiscovery())`
  would add a second resolving handler. That is why they got no edit.
- **`AddStandardResilienceHandler`** comes from ServiceDefaults in those four, ordered before discovery as Aspire does.
  Register (`RegisterHost.cs:191,207`) and Access Management (`IntegrationDependencyInjectionExtensions.cs:48,63`) replace
  the resilience handler on some clients; the discovery handler is untouched. The probe ran resilience followed by
  discovery and resolved normally.
- **Polly v7 policy handlers** (Dialogporten `AddPolicyHandlerFromRegistry`, Correspondence `AddStandardRetryPolicy`),
  **Maskinporten token handlers**, the adapter's `FourHundredLoggingDelegatingHandler` and the Resource Registry's
  `PlatformAccessTokenHandler` are all added per client.
  - `ConfigureHttpClientDefaults` runs before per-client configuration, so the resolving handler is the outermost.
  - The inner handlers, retries included, see the resolved URI, and a retry does not re-resolve. Nothing in those
    handlers depends on the host name, as far as I read.
- **Events' webhooks are the one case to decide.** `LocaltestWebhookDeliveryHandler` (fakes) captures and decides
  delivery by the request URI. Behind the resolver it sees the resolved URI. A subscriber URL whose single-label host
  equals a configured service name would be rerouted. Options:
  - accept this, since subscriber URLs are `local.altinn.cloud` or `host.docker.internal`;
  - or register the webhook client's own handler chain without discovery: a named-client
    `ConfigureAdditionalHttpMessageHandlers` that removes `ResolvingHttpDelegatingHandler`.
- **`ConfigurePrimaryHttpMessageHandler`** (the localtest app's Authentication client, Correspondence's APIM bypass) is
  compatible: the resolver is a delegating handler.

## 4. The prototype

Branch `research/service-discovery`. The block, placed directly after `WebApplication.CreateBuilder(args)` (inside the
existing `#if LOCALTEST` block in the three Dialogporten hosts):

```csharp
#if LOCALTEST
// localtest: resolve other components by name (http://<service>/...) from services__<service>__http__0; names
// without an entry pass through to DNS (planned-components/service-discovery.md).
builder.Services.AddServiceDiscovery();
builder.Services.ConfigureHttpClientDefaults(http => http.AddServiceDiscovery());
#endif
```

| Component | File (under `localtest/components/<c>/`) | Package |
| --- | --- | --- |
| storage | `vendor/src/Storage/Program.cs:58-63` | targets, `Version` |
| events | `vendor/src/Events/Program.cs:59-64` | targets, `Version` |
| profile | `vendor/src/Altinn.Profile/Program.cs:54-59` | targets, `Version` |
| receipt | `vendor/src/backend/Altinn.Receipt/Program.cs:36-41` | targets, `Version` |
| correspondence | `vendor/src/Altinn.Correspondence.API/Program.cs:44-49` | targets, `Version` |
| notifications | `vendor/components/api/src/Altinn.Notifications/Program.cs:40-45` | targets, `VersionOverride` |
| authorization | `vendor/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs:53-58` | none (transitive 9.5.2) |
| dialogporten-adapter | `vendor/src/Altinn.DialogportenAdapter.WebApi/Program.cs:31-36` | targets, `Version` |
| dialogporten (WebApi, GraphQL, Service) | `vendor/src/Digdir.Domain.Dialogporten.{WebApi,GraphQL,Service}/Program.cs`, inside the existing `#if LOCALTEST` after `Apply(builder)` | targets (`Version`, `RestoreLockedMode=false`) + the restore line of each Dockerfile |
| localtest app | `localtest/localtest/Program.cs:42-45` (no directive) | `Localtest.csproj` |
| register, authentication, access-management, resource-registry | none: `AltinnHost.CreateWebApplicationBuilder` registers it in production code | already there |

Each changed component's `vendor/UPSTREAM.md` lists the new site with the moved line numbers of the later sites, and
every component README with a site list mentions the registration in one line (Authentication, Access Management and
the Resource Registry say they need no site).

Not changed: **the call sites.** No setting was pointed at a service name. With no `services__*` injected, every request
goes where it goes today, through pass-through. So the branch changes no behaviour until the runtime and the settings
move.

## 5. Build results

Each check copied the paths the Dockerfile's build stage copies into a scratch directory, set `DirectoryBuildPropsPath`
and `DirectoryBuildTargetsPath` as the Dockerfile does, ran `dotnet build -c Release` of the host project with SDK
10.0.401 (`global.json` pinned, because the machine's default SDK is an 11.0 preview), checked that
`Microsoft.Extensions.ServiceDiscovery.dll` is in the output, and deleted the copy. The script is
`/home/agent/code/.scripts/sd-build.sh` on this machine. One build ran at a time.

| Component | Result | Warnings |
| --- | --- | --- |
| storage | built, 0 errors | 4 |
| events | built, 0 errors | 0 |
| profile | built, 0 errors | 0 |
| receipt | built, 0 errors | 0 |
| correspondence | built, 0 errors | 24 |
| notifications | built, 0 errors | 0 |
| authorization | built, 0 errors | 22 |
| dialogporten-adapter | built, 0 errors | 0 |
| dialogporten Service | as `Dockerfile.service`: `dotnet restore "src/Digdir.Domain.Dialogporten.Service/Digdir.Domain.Dialogporten.Service.csproj"` in `vendor/`, then `dotnet publish "Digdir.Domain.Dialogporten.Service.csproj" -c Release -o <scratch>/publish /p:UseAppHost=false --no-restore`: both exit 0, ServiceDiscovery in the output | — |
| dialogporten WebApi, GraphQL | built (0 errors) in the first round, against lock files that were dropped later; not rebuilt after the switch to `RestoreLockedMode=false` | 2, 0 |
| dialogporten with upstream's `--locked-mode` kept | NU1004, with or without `RestoreLockedMode=false` in the targets | — |
| localtest app | built, 0 warnings (`TreatWarningsAsErrors`, `IsAotCompatible`); `dotnet publish -r linux-arm64` Native AOT succeeded | 0 |
| register, authentication, access-management, resource-registry | not built: no file changed | — |
| workflow-engine, pdf3, arbeidsflate | not built: not .NET hosts we build, or not .NET | — |

The warnings are the components' existing ones. The diagnostics were not compared with a build of `main`.

## 6. What moves into the images, and what stays in `envs`

**Where "baked in" lives.** Most of these settings are already baked into the images: they are the defaults the fakes
compute from `Localtest__<X>__PlatformEndpoint`. So the change is per component, in its fakes' configuration
defaults: replace `platform + "register/api/v1/"` with the constant `"http://register/register/api/v1/"`. No new
appsettings file is needed. The fakes put their defaults at different priorities:

- **Register:** lowest, below `appsettings.json`.
- **Most others:** above `appsettings.json` and below the environment.
- **Authentication:** above the environment (`AddInMemoryCollection`).

An `appsettings.Localtest.json` in the image would therefore not win everywhere, which is another reason to change the
fakes themselves.

| Component | Becomes a constant with a service name | Must stay runtime-injected |
| --- | --- | --- |
| register | `PlatformSettings:ApiAuthorizationEndpoint = http://authorization/authorization/api/v1/` | `GeneralSettings:OpenIdWellKnownEndpoint` (public) |
| profile | `RegisterSettings:ApiRegisterEndpoint = http://register/register/api/`, `NotificationsSettings:ApiNotificationsEndpoint = http://notifications/notifications/api/`, `PlatformSettings:ApiAuthorizationEndpoint`, a new key for the fakes' Register test-data URL | `Localtest__Profile__PlatformEndpoint` (JwtCookie metadata) |
| authentication | Profile, Register (v1 and v2 internal), Notifications, Resource Registry, Authorization endpoints; `AccessManagementSettings:ApiAccessManagementEndpoint = http://access-management/accessmanagement/api/v1/` (at22 today); ID-porten token and metadata URLs `http://localtest/localtest/api/idporten/...`. This means splitting `InternalPlatformEndpoint`, which serves all of them. | `GeneralSettings__PlatformEndpoint` (issuer, `BaseUrl`, cookie domain, ID-porten issuer and browser endpoints, Maskinporten well-known URLs) |
| authorization | Register, Storage, Resource Registry, Access Management, Profile endpoints | `Localtest__Authorization__PlatformEndpoint` (JwtCookie) |
| access-management | Authentication, Authorization, Profile, Register, Resource Registry endpoints; `Platform:Register:Endpoint = http://register/`, `Platform:ResourceRegistry:Endpoint = http://resource-registry/`; a new key for the refresh's test-users URL. `ApiResourceRegistryEndpoint` takes effect only with flagged site 1. | `Localtest__AccessManagement__PlatformEndpoint` (`OidcProviders:altinn:Issuer`, `GeneralSettings:Hostname`) |
| resource-registry | `PlatformSettings:StorageApiEndpoint = http://storage/storage/api/v1/`, `ApiRegisterEndpoint` | `Localtest__ResourceRegistry__PlatformEndpoint` (issuer) |
| storage | `PlatformSettings:ApiAuthorizationEndpoint`, `RegisterServiceSettings:ApiRegisterEndpoint`, `Localtest:Storage:DialogportenAdapterEndpoint = http://dialogporten-adapter/storage/dialogporten/` | `GeneralSettings__PlatformEndpoint` (`Hostname`, self links, JwtCookie) |
| events | `PlatformSettings:ApiAuthorizationEndpoint`, `PlatformSettings:RegisterApiBaseAddress = http://register/register/api/` | `Localtest__Events__PlatformEndpoint` (`BaseUri`, the CloudEvent source, `AppsDomain`, JwtCookie) |
| notifications | Profile, Register, Authorization endpoints; Maskinporten `ExchangeEndpoint = http://authentication/authentication/api/v1/exchange/maskinporten` and the token URL `http://localtest/localtest/api/maskinporten/` (today under the misnamed key `Localtest:Maskinporten:Issuer`; split into a `TokenEndpoint`) | `Localtest__Notifications__PlatformEndpoint` (`BaseUri`, JwtCookie) |
| receipt | `PlatformSettings:ApiStorageEndpoint = http://storage/storage/api/v1/`, `ApiRegisterEndpoint`, `ApiProfileEndpoint` (in the manifest today; they move into the fakes) | `ApiAuthenticationEndpoint` (browser redirect), `OpenIdWellKnownEndpoint`, `GeneralSettings__Hostname`, `Localtest__Receipt__PlatformEndpoint` |
| correspondence | `ContactReservationRegistryBaseUrl`, Maskinporten token and exchange URLs, the dialog JWKS URL (needs its own key), and the nine typed clients. Those clients need `PlatformGatewayUrl` split: per-client `ConfigureHttpClient(c => c.BaseAddress = new("http://register/"))` in a fake hook registered after upstream's, plus a directive at `ResourceRegistryService.cs:26`, which sets the base in its constructor. | `Localtest__Correspondence__PlatformEndpoint` (`PlatformGatewayUrl` as authority and expected issuer, `CorrespondenceBaseUrl`, blob links) |
| dialogporten | `Infrastructure:Altinn:EventsBaseUri = http://events/`, Maskinporten token and exchange URLs. The four clients on `Infrastructure:Altinn:BaseUri` need it split, because `MetadataLinkProvider` builds client-facing links from it: per-client base overrides in the fakes, or a fake handler that rewrites `{platform}<prefix>/` to `http://<service>/<prefix>/`. | `Localtest__Dialogporten__PlatformEndpoint` (dialog-token issuer, JWT well-known URLs, `BaseUri` links) |
| dialogporten-adapter | `InternalStorageEndpoint = http://storage/`, `InternalRegisterEndpoint = http://register/`, `Dialogporten:BaseUri = http://dialogporten-webapi/dialogporten`, Maskinporten token and exchange URLs. Upstream already separates internal from public, so this is the easiest component. | `Localtest__Adapter__PlatformEndpoint` (`Altinn:BaseUri`, `Adapter:BaseUri` dialog links, JwtBearer well-known) |
| localtest app | `Proxy__Upstreams__authentication = http://authentication`, `Proxy__Upstreams__register = http://register` (these also feed its own proxy, flagged site 2) | `Localtest__PlatformEndpoint`, `Localtest__AccessTokenCertificatePath` |

**Values that could also become constants:**

- **The certificate path** (`AccessTokenSettings__AccessTokenSigningCertificateFullPath = {{certPath}}`) could become a
  constant if the mount point (`/certs/localtest-accesstoken.pfx`, manifest `certificate.containerDir`) is part of the
  image contract. The mount itself stays a runtime `volumes` entry.
- **The Postgres and Redis connection strings** (`Host={{alias:postgres}};Port={{port:postgres}}`) could become
  `Host=postgres;Port=5432` constants if both runtimes guarantee those network aliases and ports. The manifest declares
  `networkAliases: ["postgres"]` and `["redis"]` today; I did not check the Aspire package's names. Npgsql and
  StackExchange.Redis use DNS, not the resolver.

**`envs` per .NET component after the change:**

- One public-origin value: `Localtest__<X>__PlatformEndpoint`, or `GeneralSettings__PlatformEndpoint` in Storage and
  Authentication.
- Receipt also keeps `GeneralSettings__Hostname`.
- The certificate path and connection strings, unless they become constants as above.
- Plus the `services__*` entries the runtime adds.

**Node, nginx, pdf3 and the workflow engine have no resolver**, so the runtime must still substitute these values or
provide these names:

- **arbeidsflate-bff**
  - `HOSTNAME`, `OIDC_PLATFORM_URL`, `AUTH_CONTEXT_COOKIE_DOMAIN`, `MASKINPORTEN_ISSUER` (public, or a DNS name of the
    localtest app) and `PLATFORM_BASEURL`.
  - `PLATFORM_BASEURL` serves six services by path, so it can only go direct by pointing at an in-network path router:
    the future front door by its network alias (for example `http://localtest:8080`), or one variable per service in a
    new `localtest:` patch.
  - `DIALOGPORTEN_URL` must be split into GraphQL (`http://dialogporten-graphql:8080`) and WebApi health
    (`http://dialogporten-webapi:8080/dialogporten`) by a patch.
  - DNS aliases for every name it calls. Today only `postgres`, `redis` and `arbeidsflate-bff` have one; the containers
    are named `localtest-v2-<name>`.
- **arbeidsflate SPA (nginx):** `DIALOGPORTEN_STREAM_URL` stays public; its `/api/` proxy already uses the BFF's alias.
- **pdf3:** `PDF3_LOCALTEST_PUBLIC_BASE_URL` stays public.
- **workflow engine:** `AppCommandSettings__CommandEndpoint` stays `{{url:app}}`, because the app is on the host.

**What the runtime injects.** For every .NET component, `services__<name>__http__0 = http://<container DNS name>:<port>`
for each component the stack runs.

- **Under studioctl** the container name `localtest-v2-<name>` resolves on the user-defined network. That is Podman
  behaviour I did not test here.
- **Under Aspire** a container-to-container reference injects the same shape. Not verified in 13.6.2.
- **Not only `dependsOn`.** The call graph has cycles:
  - Storage → Authorization → Storage;
  - Authorization → Access Management → Authorization;
  - Register → Authorization → Register.

  `dependsOn`, which orders start-up, cannot carry it. Two options:
  - inject the entries for all running components;
  - or add a separate `references` list next to `dependsOn` in the manifest.
- **Optional groups.** Leave out the entries of a group that is off. The pass-through then fails at the request, as in
  section 3.

## 7. Port and health convention

Container port today and the change for 8080. In the .NET images, removing `ASPNETCORE_URLS` lets the base image's own
`ASPNETCORE_HTTP_PORTS=8080` apply. That is the documented default of the .NET 8+ images; I did not inspect each pinned
base image.

| Image | Listens on today | Health today | Change for 8080 + `/health` |
| --- | --- | --- | --- |
| register | `ASPNETCORE_URLS=http://+:5020` | `/health` (`MapDefaultAltinnEndpoints`) | drop `ASPNETCORE_URLS`, `EXPOSE 8080` |
| profile | 5030 | `/health` (`Program.cs:309`) | same |
| authentication | 5101 (`EXPOSE 5040` is stale) | `/health` | same |
| authorization | 5070 (`EXPOSE 5050` is stale) | `/health` | same |
| storage | 5010 | `/health` (`Program.cs:497`) | same |
| access-management | 5080 (the runtime stage already says 8080 and is overridden) | `/health` | drop the final `ASPNETCORE_URLS` |
| events | 5080 | `/health` | same as register |
| resource-registry | 5100 | `/health` | same |
| notifications | 5090. Its SMS and e-mail clients call `http://localhost:5090/...` (fakes `LocaltestNotificationsExtensions.cs:56-57`). | `/health` | same, **and** change those two loopback URLs to 8080 |
| receipt | 5060 | `/health` | same as register |
| correspondence | 2525 | `/health` (DB check), `/healthz` liveness | same |
| dialogporten-adapter | 5011 | `/health` (`WebApplicationExtensions.cs:25`) | same |
| localtest app | `ASPNETCORE_HTTP_PORTS=8000` | `/health` | `8080`. Its host port 8000 is the front door's public port, which is a separate decision. |
| dialogporten WebApi, GraphQL, Service | 8080 (base image default; only WebApi says `EXPOSE 8080`) | `/health/readiness`; `Altinn.AspNet.HealthChecks` 0.4.0 also maps `/health` (decompiled default), liveness pinned to `/health/liveness` | none for the port; point the manifest's `healthPath` at `/health` if the convention is literal (not run) |
| arbeidsflate-bff | `PORT=80` (`Dockerfile.bff:41`) | `/api/readiness` (also `/api/liveness`, `/api/health`) | `ENV PORT=8080`; `/health` needs a route (a patch) or the convention must accept `/api/readiness` |
| arbeidsflate SPA (nginx) | `listen 80` (`nginx/nginx.conf:7`) | `/` | `listen 8080;` plus `location = /health { return 200; }` in our `nginx.conf` (not upstream's) |
| workflow-engine | 9090 (not ours) | `/api/v1/health/ready`; `/health*` redirect | not our image; the manifest keeps its values |
| pdf3 | 5031 (not ours) | `/health/ready` | as above |

The port is also in the manifest (`containerPort`, `hostPort`) and in every `services__*` value, so the port change and
the injection belong in the same runtime change.

**Aspire 13.6.2 and the image's `HEALTHCHECK`.** No: `WaitFor` does not use it. Read in the decompiled `Aspire.Hosting`
13.6.2, not run:

- `ResourceNotificationService.WaitForResourceHealthyAsync` documents that a resource without `HealthCheckAnnotation`s
  "will be considered healthy once it reaches a Running state".
- DCP's `ContainerSpec` and `ContainerStatus` models carry no health field. Only `ExecutableSpec` has `healthProbes`.
- So the container's own health is ignored. A container is healthy for `WaitFor` only through `WithHttpHealthCheck`
  (or another health-check annotation), which the Aspire package adds from the manifest's `healthPath`.
- In addition, Podman builds OCI images, which drop `HEALTHCHECK`, as every Dockerfile here notes.

## 8. The front door's resolver

`Microsoft.Extensions.ServiceDiscovery.Yarp` is 10.11.0 latest (`net8.0`, `net9.0`, `net10.0`; depends on
`Yarp.ReverseProxy` ≥ 2.3.0). `AddServiceDiscoveryDestinationResolver()` on `IReverseProxyBuilder` registers
`ServiceDiscoveryDestinationResolver` as YARP's `IDestinationResolver` and calls only `AddServiceDiscoveryCore()`, so
the caller must add the providers. The resolver works like this (decompiled 10.11.0; behaviour run with 10.8.0 in the
probe):

- It resolves each destination's `scheme://authority` through `ServiceEndpointResolver`.
- It emits one destination per endpoint, named `<original>[<endpoint>]`, with the endpoint's scheme, host and port and
  the original path. `http://storage/storage` became `http://localtest-v2-storage:5010/storage`.
- It rewrites a destination's `Health` address the same way, and keeps an explicit `Host`.
- It follows the configuration's change token, so a changed `services__*` value re-resolves.
- For a name with no entry it returns:
  - with the configuration provider alone: **zero destinations** (probe);
  - with `AddServiceDiscovery()`: a pass-through destination `http://register/`, which fails per request in DNS.

For the predictable 503 of a group that is off, the front door should register
`AddServiceDiscoveryCore().AddConfigurationServiceEndpointProvider()` and not the pass-through provider. YARP answers
503 for a cluster without available destinations. That is its documented behaviour; I did not run a proxy here.

## 9. Recommendation

1. **Merge the registration** (this branch). It changes no behaviour on its
   own (section 4) and makes every later step a configuration change.
2. **Runtime: port, health, `services__*`.** Do these in one change to the manifest, studioctl and the Aspire package.
   - Move the images to 8080 and `/health` (section 7; the Notifications loopback URLs with it).
   - Inject `services__<name>__http__0` for every running component, not only `dependsOn` (section 6).
   - Give every container a network alias equal to its service name, for the non-.NET clients and the data stores.
3. **Per component, change the fakes' defaults to service names**, easiest first:
   - **Simple:** the adapter (internal and public are already separate); Storage, Events, Notifications, Profile,
     Receipt, Authorization, the Resource Registry, Register.
   - **Then:** Access Management (with the flagged `ResourceRegistryClient` `#else`), Authentication (split
     `InternalPlatformEndpoint`).
   - **Last:** Dialogporten (split `Infrastructure:Altinn:BaseUri`) and Correspondence (split `PlatformGatewayUrl`).
   - Run each component's smoke test after its step. The public-origin values stay as they are throughout.
4. **The YARP front door** with the configuration-only resolver (section 8), the later task. Then the BFF's
   `PLATFORM_BASEURL` can point at the front door's in-network name instead of the host.
5. **Leave every metadata fetch on the public origin** (flagged rows 3-6). Nothing is gained while `jwks_uri` is public,
   and the containers need `local.altinn.cloud` for the browser-facing values anyway.

## 10. Effort estimate

An estimate from what was read, not measured:

| Step | Estimate |
| --- | --- |
| Registration and its site documentation (done here) | 0.5 day |
| Runtime: ports and health in 13 Dockerfiles and the manifest, `services__*` injection and aliases in studioctl and the Aspire package, their tests | 2-3 days |
| Fakes defaults, simple components (8) | 2-3 days in all, mostly smoke-test runs |
| Access Management (including the `ResourceRegistryClient` site), Authentication | 1.5 days |
| Dialogporten, Correspondence (base splits, per-client overrides, one directive) | 2 days |
| BFF patch (split variables, `/health`, port) and nginx | 1 day |
| **Total** | **about 9-11 days** |

## 11. Not verified

- **Real network behaviour.** No request ran between real containers. The pass-through failure mode
  (`HttpRequestException` for an unknown name), that `localtest-v2-<name>` resolves on the Podman network, and that a
  hyphenated `services__resource-registry__http__0` reaches the process under Podman are expectations, not observations.
- **Aspire.** That Aspire 13.6.2 injects `services__*` for container-to-container references in this shape: not run. Its
  ignoring of the image `HEALTHCHECK` is read from decompiled code, not run.
- **YARP's 503** for a cluster with zero destinations: documented behaviour, not run.
- **The `.NET` base images' `ASPNETCORE_HTTP_PORTS=8080`** in each pinned digest: not inspected.
- **Dialogporten's `/health`:** from the decompiled defaults of `Altinn.AspNet.HealthChecks` 0.4.0, not requested.
- **The localtest app's AOT publish** ran for linux-arm64 glibc. The image publishes linux-musl, which was not built.
- **Register, Authentication, Access Management and the Resource Registry** were not rebuilt (no change). That their
  service discovery is active at runtime rests on reading `AltinnServiceDefaultsExtensions`, not on a request.
- **Build warnings** were not compared with a build of `main`.
- **No smoke test ran.** The branch is not in an image, and the stack was not started.
