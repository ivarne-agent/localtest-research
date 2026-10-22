# Service discovery for the components

## Status (2026-10-22)

- **Research done, prototype on a branch**, task 25: can every vendored component resolve the other services by name
  (`http://storage/`, `http://authentication/`, ...) through `Microsoft.Extensions.ServiceDiscovery`, so that the runtime
  injects `services__<name>__http__0` instead of a hand-written URL per setting.
- Written against this repository at `7583be9b` (main). The prototype is on branch `research/service-discovery`
  (`d0d109c2..`, see the final commit in the git log). Package versions are from nuget.org on 2026-10-22: Microsoft.Extensions.ServiceDiscovery,
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
