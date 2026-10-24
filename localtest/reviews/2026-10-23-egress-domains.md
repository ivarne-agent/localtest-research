# Egress domains: what an app, its browser and the localtest stack call (2026-10-23)

> **Status:** phase 1 (static inventory and measurement design) done 2026-10-23; phase 2 (observed on the localtest-v2
> stack, pin `sha-e2a9b82`) done 2026-10-24: the observed column in section 3, the run and what the inventory did not
> predict in [section 8](#8-observed-phase-2), the capture files in [`egress/`](egress/). Task 44 of [`../../TODO.md`](../../TODO.md). The owner's request: "track the domain name for
> all external network calls; I want that for security so we can block anything not on the list in production".

**Scope.** On the owner's instruction the app side is the **monorepo** app only: the backend
`altinn-studio/src/App/backend/src` (`Altinn.App.Core`, `Altinn.App.Api`, `Altinn.App.Clients.Fiks`) and the frontend
it serves, `altinn-studio/src/App/frontend` (with `src/common/ts`). app-lib 8 (`app-lib-dotnet`) and the released
app-frontend v4 are out of scope; phase 2's app flow uses the monorepo app `ttd/expression-validation-test` only. Then
pdf3 and the workflow engine (monorepo images), and the stack's own components (secondary: the stack is not deployed
to production as such). NuGet, npm and image pulls at build time are out of scope.

**Phase 1 read, not run.** Sources at altinn-studio `819ad7362e`, altinn-studio-charts `1a7c028`, this repository `3a74df96`
(manifest pins as in [`../CONVENTIONS.md`](../CONVENTIONS.md#tags)). The inventories were made with read-only
subagents and spot-checked; line numbers are from those commits. No Podman, no stack, no AppHost. The capture
scripts were tried only for the host side (a 10-second capture with `CTR=true`, `curl` to two hosts), not against a
stack. `brreg/altinn-studio-ops/altinn-studio-infra` (Azure DevOps) was **not** read: no access token here, so the
cluster's outbound type (NAT gateway, Azure Firewall) is not confirmed.

Paths: `S/` is `altinn-studio/`, `B/` is `S/src/App/backend/src/`, `F/` is `S/src/App/frontend/src/`, `L/` is this
repository's `localtest/`.

## 1. Answer in short

- **An app pod's egress in production** (default configuration, every app):
  - the platform host of its environment, `platform.altinn.no` (`platform.tt02.altinn.no`,
    `platform.{at22,at23,at24,yt01}.altinn.cloud`): Storage, Register, Profile, Authentication (also the OIDC
    metadata the JwtCookie handler fetches, and the Maskinporten token exchange), Authorization, Events,
    Notifications, Access Management, Correspondence, eFormidling;
  - in-cluster: the PDF service `pdf-generator.pdf.svc.cluster.local` (Linkerd routes it to pdf3's relay), the workflow
    engine `workflow-engine-app.runtime-workflow-engine-app.svc.cluster.local`, cluster DNS;
  - **`altinncdn.no`** (`/orgs/altinn-orgs.json`, hard-coded, cached 24 h): the org name and logo for the index
    page's bootstrap data, signing, Notifications, Fiks Arkiv;
  - the **Application Insights** ingestion host from the connection string the deploy pipeline sets (the
    default telemetry path), or an OTLP collector when `AppSettings:UseOpenTelemetry` is on;
  - the **Maskinporten** authority from the platform-provisioned secret file (`maskinporten.no`,
    `test.maskinporten.no`) when the app has a Maskinporten client: at start-up and every 12 h (well-known), and per
    token.
- **Per feature** an app adds: `studioprodsc.blob.core.windows.net` (shared library code lists, a default in code),
  Fiks IO `api.fiks.ks.no:443` and `io.fiks.ks.no:5671` (AMQP; `*.fiks.test.ks.no` outside production), the Nets
  payment API (`NetsPaymentSettings:BaseUrl`, no default), Azure Key Vault and Entra ID (only if the app opts in), its
  own public host `{org}.apps.<host>` (the Nets webhook moves the process through it), and anything its own code calls.
- **Only through platform services**, never from the app pod: ID-porten (browser redirects through Authentication),
  e-mail and SMS (Notifications), Dialogporten (Correspondence and Storage's adapter), the KRR, the Brreg and the
  population register (Register, Profile), subscriber webhooks (Events), the eFormidling integration point's own
  peers.
- **The browser** (a separate egress point, framed as a Content-Security-Policy in section 4): the app's origin,
  `altinncdn.no` (fonts, favicon, Altinn logo, org logos, postcodes; the bundle itself is now served by the app),
  `tile.openstreetmap.org` and `cache.kartverket.no` for the Map component's default layers, and whatever hosts an
  app's layouts, texts or `config/assets.json` name. The index page sets **no** CSP today.
- **pdf3** loads whatever URL the request names, with no allow-list in code; in production that is the app's own
  public host plus what the page loads (`altinncdn.no`). Its workers' Cilium policy allows the whole public internet
  and denies private ranges.
- **The workflow engine** calls the app back on a URL built from a template and the request's org and app, and its
  `webhook` command calls any absolute URI in the request; there is no SSRF guard.
- **The localtest stack** reaches the internet for `altinncdn.no` (six components fetch the org list; the Receipt and
  Arbeidsflate pages load fonts and logos from it), the Receipt page's Font Awesome and Google Fonts, and, on two
  self-identified-user paths, `at22.altinn.cloud` (Register's SBL bridge, Authentication's Access Management
  setting): those two are leaks to a shared test environment.
- **Enforcement today:** none for app pods. The deployment chart has no NetworkPolicy, the apps run in the `default`
  namespace, and the only egress policy in the monorepo is pdf3's worker policy (CIDR, not FQDN). The docs tell
  external parties to allow-list the apps' outbound IP prefixes, so egress is open by design today.
- **Observed (phase 2, section 8):** the inventory held, with three additions: the app's `Altinn.Codelists` library
  calls `data.ssb.no` (and by default `ws.geonorge.no`, `www.bring.no`), Dialogporten's hosts call
  `usage-tracking.masstransit.io` at start-up, and the Receipt page's Google Fonts pull from `fonts.gstatic.com`.
- **Measurement (phase 2):** packet capture of DNS and connection starts (with TLS SNI) on the stack's bridge and the
  host's uplink, attributed to containers and to host processes, as the ground truth; an OTLP collector for the app's
  client spans, which name the caller; Playwright traces for the browser. Scripts in
  [`../tools/egress/`](../tools/egress/).

## 2. What the app pods get in production

The deploy pipeline (`S/src/App/azure-pipelines/deploy-app.yaml:646-666`) gives every app
`envFrom: apps-runtime-common-config-env` and `apps-runtime-common-secrets-env`, and per app
`PlatformSettings__SubscriptionKey`, `ApplicationInsights__InstrumentationKey`, `ApplicationInsights__ConnectionString`
and `AppSettings__AppInsightsRoleName`. The chart (`altinn-studio-charts/charts/deployment/templates/deployment.yaml:71-89`)
adds `ALTINN_KEYS_DIRECTORY`, `OTEL_RESOURCE_ATTRIBUTES` and the secrets mount; it has **no** NetworkPolicy template and
no proxy variables. The config map is `S/infra/runtime/apps-config/`:

| Setting | Base (every environment) | prod | tt02 | at22, at23, at24, yt01 |
| --- | --- | --- | --- | --- |
| `PlatformSettings__Api{Storage,Register,Profile,Authentication,Authorization,Events,Notification,AccessManagement,Correspondence}Endpoint` | | `https://platform.altinn.no/<prefix>/api/v1/` | `https://platform.tt02.altinn.no/…` | `https://platform.<env>.altinn.cloud/…` |
| `PlatformSettings__ApiPdf2Endpoint`, `PdfGeneratorSettings__ServiceEndpointUri` | `http://pdf-generator.pdf.svc.cluster.local/pdf` | | | |
| `PlatformSettings__ApiWorkflowEngineEndpoint` | `http://workflow-engine-app.runtime-workflow-engine-app.svc.cluster.local/api/v1/` | | | |
| `AppSettings__OpenIdWellKnownEndpoint` | | `https://platform.altinn.no/authentication/api/v1/openid/` | `platform.tt02.altinn.no` | `platform.<env>.altinn.cloud` |
| `EFormidlingClientSettings__BaseUrl` | | `https://platform.altinn.no/eformidling/api/` | `platform.tt02.altinn.no` | not set |
| `GeneralSettings__ExternalAppBaseUrl` | `https://{org}.apps.{hostName}/{org}/{app}/` | | | |
| `GeneralSettings__HostName` | | `altinn.no` | `tt02.altinn.no` | `<env>.altinn.cloud` |
| `PlatformFrontendSettings__{PostalCodesUrl,AltinnLogoUrl,HelpCircleIllustrationUrl}` | `https://altinncdn.no/…` (browser only) | | | |
| `PlatformFrontendSettings__*Url` links | | `altinn.no`, `af.altinn.no`, `am.ui.altinn.no`, `platform.altinn.no` (navigations only) | `*.tt02.altinn.no` | `*.<env>.altinn.cloud` |
| `RUNTIME_APP_SECRETS_DIR` and file names | `/mnt/app-secrets`, `maskinporten-settings.json`, `app-codes.json` | | | |

Not set by the platform, so the code default or the app's own `appsettings` applies: `ApiResourceRegistryEndpoint`
(not used by the backend), `Altinn3LibraryApiEndpoint` (default `studioprodsc.blob.core.windows.net`), the
Maskinporten authority (from the secret file, which the operator writes), Fiks, Nets, Key Vault,
`AppSettings:UseOpenTelemetry` (off, so Application Insights).

**The PDF path.** `pdf-generator.pdf` is a Service without endpoints; Linkerd routes it to pdf3's relay
(`S/infra/runtime/syncroot/base/pdf.yaml:6-8`), which posts to
`https://studio.platform.<env>.altinn.cloud/pdf/v3/generate` (`S/src/Runtime/pdf3/infra/relay/prod/kustomization.yaml:19-20`;
tt02 and at24 use `studio.platform.tt02.altinn.cloud`). The shared pdf3 workers then load the app page from the
internet at `https://{org}.apps.<host>/…`. So the PDF hop leaves the apps cluster from the relay pod, not from the
app pod.

## 3. Static inventory per process

Columns: **Where** is the domain fixed: *code* (a literal), *config* (a setting; the production value from section 2),
*secret* (the platform-provisioned file), *data* (from a request, an instance or the app's files). **Via platform**
marks what an app reaches only through a platform service.

### 3.1 The app process (monorepo backend)

| Domain (production) | Client (file:line) | Purpose | When | Where | Observed (phase 2) |
| --- | --- | --- | --- | --- | --- |
| `platform.<env>` `/storage/` | `ApplicationClient`, `InstanceClient`, `DataClient`, `ProcessClient`, `InstanceEventClient`, `SignClient` (`B/Altinn.App.Core/Extensions/ServiceCollectionExtensions.cs:108-129,320`); `FiksArkivInstanceClient.cs:116-117` | instances, data, process, events, signatures | every request; workflow-engine callbacks; Fiks Arkiv | config `ApiStorageEndpoint` (code default `http://localhost:5101/storage/api/v1/`, `B/Altinn.App.Core/Configuration/PlatformSettings.cs:12`) | yes: instances, data, mutations, read status |
| `platform.<env>` `/register/` | `RegisterClient.cs:23,77`, `RegisterERClient.cs:47`, `AltinnPartyClient.cs:57`, `PersonClient.cs:49` | parties, persons, organisations | per request | config `ApiRegisterEndpoint` (:17) | yes: `parties/partylist`, `parties/lookup`, `organizations` |
| `platform.<env>` `/profile/` | `Infrastructure/Clients/Profile/ProfileClient.cs:26,71` | user profile | per request | config `ApiProfileEndpoint` (:22) | yes: `users/{id}` |
| `platform.<env>` `/authentication/` | `AuthenticationClient.cs:36`; `Features/Maskinporten/MaskinportenClient.cs:279-293` (`exchange/maskinporten`) | token refresh; Maskinporten token exchange | per request; when a service-owner token is used | config `ApiAuthenticationEndpoint` (:27) | yes: `refresh`; the exchange not exercised (service-owner tokens come from `/Home/GetTestOrgToken` on localtest, 47 calls) |
| `platform.<env>` `/authentication/api/v1/openid/` and its `jwks_uri` | JWTCookieAuthentication's metadata fetch, `MetadataAddress` at `B/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:576` | validate the runtime cookie and bearer tokens | first authenticated request, then periodic refresh | config `AppSettings:OpenIdWellKnownEndpoint` | yes: discovery and JWKS, 3 each |
| `platform.<env>` `/authorization/` | `AuthorizationClient.cs:73`; PEP `AuthorizationApiClient` (`B/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:132`) | PDP decisions | every authorized request | config `ApiAuthorizationEndpoint` (:32) | yes: `decision`, 127 |
| `platform.<env>` `/accessmanagement/` | `AuthorizationClient.cs:78` (`enduser/authorizedparties`, new in the monorepo); `AccessManagementClient` (`Internal/AccessManagement/Helpers/UrlHelper.cs:9,18`) | authorized parties; signing delegation and revocation | party selection; signing | config `ApiAccessManagementEndpoint` (:57) | yes: `authorizedparties`, 25; delegation not exercised (signing) |
| `platform.<env>` `/events/` | `EventsClient.cs:49,100` | publish instance events | workflow-engine command, only with `AppSettings:RegisterEventsWithEventsComponent` | config `ApiEventsEndpoint` (:37) | not exercised (`RegisterEventsWithEventsComponent` off in the app) |
| `platform.<env>` `/notifications/` | e-mail, SMS, order clients (`Core/Extensions/ServiceCollectionExtensions.cs:275-277`) | notification orders | Notifications feature | config `ApiNotificationEndpoint` (:47) | not exercised (no notification in the app) |
| `platform.<env>` `/correspondence/` | `Features/Correspondence/CorrespondenceClient.cs:427,436` | send correspondence | Correspondence feature, signing call-to-action | config `ApiCorrespondenceEndpoint` (:52) | not exercised (no correspondence in the app) |
| `platform.<env>` `/eformidling/` | in-tree `EFormidling/Implementation/EFormidlingClient.cs:57,102,134,153,167` | send and poll eFormidling messages | eFormidling service task | config `EFormidlingClientSettings:BaseUrl` (no default; start-up check throws) | not exercised (no eFormidling task) |
| `pdf-generator.pdf.svc.cluster.local` | `Infrastructure/Clients/Pdf/PdfGeneratorClient.cs:153-154` | generate a PDF (the service loads the app page from `ExternalAppBaseUrl`, `Internal/Pdf/PdfService.cs:142`) | PDF service task, preview | config `ApiPdf2Endpoint` (code default `http://localhost:5300/pdf`) | yes: `pdf.<origin>` `/pdf`, 3 |
| `workflow-engine-app.runtime-workflow-engine-app.svc.cluster.local` | `Internal/WorkflowEngine/Http/WorkflowEngineClient.cs:42-316,422` (registered `…/DependencyInjection/ServiceCollectionExtensions.cs:36-37`) | enqueue, list, cancel, resume | **every** `process/next` (new in the monorepo) | config `ApiWorkflowEngineEndpoint` (code default `http://localhost:9090/api/v1/`) | yes: `workflow-engine.<origin>`, 174 |
| **`altinncdn.no`** `/orgs/altinn-orgs.json` | `Internal/AltinnCdn/AltinnCdnClient.cs:65,112` | org name, org number, logo URL | index page bootstrap (cached 24 h), signing, Fiks Arkiv; Notifications uncached | **code** | **yes**: 2 (OTel and capture) |
| Maskinporten authority (`maskinporten.no`, `test.maskinporten.no`) | `MaskinportenClient.cs:233-236` (`/token`), `:573-575` (well-known); `MaskinportenWellKnownRefreshService.cs:24,51,60-64` | service-owner tokens | **start-up and every 12 h** (skipped on localtest), and per token for Correspondence, eFormidling, Fiks, `AddMaskinportenHttpMessageHandler` clients | **secret** (`MaskinportenSettings.Authority`, required, no default) | not exercised (refresh skipped on localtest; no client stored) |
| Application Insights ingestion (`*.in.applicationinsights.azure.com` or the host in the connection string; not in the repo) | `B/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:101-111,154-183` | telemetry | continuous, the default path | config `ApplicationInsights__ConnectionString` (deploy pipeline) | not exercised (OpenTelemetry on for the run) |
| Azure Monitor exporter, or an OTLP collector | same file `:186-301` | OpenTelemetry | continuous, only with `AppSettings:UseOpenTelemetry=true` | config (connection string, or `OTEL_EXPORTER_OTLP_ENDPOINT`) | yes: the loopback collector |
| `studioprodsc.blob.core.windows.net` | `Features/Options/Altinn3LibraryCodeList/Altinn3LibraryCodeListApiClient.cs:20` | shared library code lists | an options reference to the library | **code** default of `Altinn3LibraryApiEndpoint` (`PlatformSettings.cs:77-78`) | not exercised (no library code list) |
| `api.fiks.ks.no:443`, `io.fiks.ks.no:5671` (`*.fiks.test.ks.no` when not `IsProduction()`) | `B/Altinn.App.Clients.Fiks/FiksIO/FiksIOClient.cs:34-35,151-164,179` | Fiks IO send, AMQP receive | **start-up** (`FiksArkivSubscriber.cs:46-86`, health check and reconnect every 10 min) and per task | package default (KS.Fiks.IO.Client 7.0.3), config `FiksIOSettings:ApiHost`/`AmqpHost` | not exercised |
| Nets (`NetsPaymentSettings:BaseUrl`) | `Features/Payment/Processors/Nets/NetsClient.cs:24` (registered only when the section exists, `Core/Extensions/ServiceCollectionExtensions.cs:308-312`) | create, read, terminate payments | payment task | config (no default; `api.dibspayment.eu` is in no code) | not exercised |
| the app's own public host `{org}.apps.<host>` | `Features/Payment/Services/PaymentService.cs:403-417` | `process/next` on itself | Nets webhook | config `ExternalAppBaseUrl` | not exercised (no payment) |
| `*.vault.azure.net`, Entra ID | `B/Altinn.App.Api/Extensions/HostBuilderExtensions.cs:20-46` (reload every 5 min); `Infrastructure/Clients/KeyVault/SecretsClient.cs:25-72` | configuration secrets | only if the app calls the opt-in API (the v9 template does not) or injects `ISecretsClient` | config `kvSetting:SecretUri` | not exercised |
| anything | the app's own code: `IExternalApiClient`, options and data-list providers, typed clients | | | **data** (the app) | **yes: `data.ssb.no`** from the app's `Altinn.Codelists` (section 8) |

Only on localtest (Development and a `local.altinn.cloud` host): `Internal/LocaltestValidation.cs:16,172`
(`/Home/Localtest/Version` on the storage endpoint's authority, every 5 s at start-up) and
`Internal/Auth/AuthenticationTokenResolver.cs:38,97-100` (`/Home/GetTestOrgToken`), both on the public origin.
`StudioctlLocalConfiguration` starts a local process, no network. `HealthCheck` makes no call. There is no
Dialogporten client and no Altinn 2 bridge client; `Altinn2CodeListProvider` serves embedded JSON.

**Changed from app-lib 8** (for the record only; app-lib 8 is out of scope): new are the workflow engine client, the
12-hourly Maskinporten well-known refresh, `authorizedparties` on Access Management and the in-tree eFormidling client;
gone are the eFormidling start-up Events subscription, the status-check handlers that called the app's own host,
Key Vault for events and the `Altinn.ApiClients.Maskinporten` package (whose DLL embeds the Maskinporten and
platform exchange hosts per environment).

### 3.2 The browser (monorepo frontend), as a Content-Security-Policy

The monorepo backend serves its own bundle: `IndexPageGenerator` (`B/Altinn.App.Core/Internal/App/IndexPageGenerator.cs`)
writes the page with the bundle at `/{org}/{app}/altinn-app-frontend` on the app's origin (`:35`; `AppSettings:AppFrontendAssetBaseUrl`
overrides it in Development only, `B/Altinn.App.Api/Controllers/HomeController.cs:121-124`). An app that still has a
legacy `views/Home/Index.cshtml` gets that instead (`HomeController.cs:114-118`), which loads the v4 bundle from
`altinncdn.no/toolkits/altinn-app-frontend/4/`; such an app needs `altinncdn.no` in `script-src` and `style-src`.

| Origin | Directive | Feature | Where fixed | Observed (phase 2) |
| --- | --- | --- | --- | --- |
| the app's origin `{org}.apps.<host>` (`'self'`) | `script-src`, `style-src`, `connect-src`, `img-src`, `media-src` | bundle, APIs, texts, keep-alive, attachments, custom CSS and JS (`/custom-css`, `/custom-js`), layout schema in development | the page | yes |
| `https://altinncdn.no` | `img-src` | favicon (`IndexPageGenerator.cs:68`) | backend code | not recorded (Playwright traces leave out favicons) |
| `https://altinncdn.no` | `style-src`, `font-src` | Altinn DIN (`S/src/common/ts/form-component/src/styles/global.css:1`, font files under `altinncdn.no/fonts/altinn-din/`), Inter CSS (`:6`; its font files only when DevTools picks Inter) | bundle | yes: `altinn-din.css` and two `woff2`; Inter not (the bundle served predates it) |
| `https://altinncdn.no` | `connect-src` | Altinn logo SVG fetched and inlined (`F/queries/queries.ts:173-174`), postcodes for the Address component (`queries.ts:221-223`) | platform frontend settings `AltinnLogoUrl`, `PostalCodesUrl` (defaults on altinncdn, `B/…/Configuration/PlatformFrontendSettings.cs:12,17`) | logo yes (9); postcodes not exercised (no Address component) |
| `https://altinncdn.no` | `img-src` | help illustration on error pages (`PlatformFrontendSettings.cs:22`); the org logo (`orgLogoUrl` from `altinn-orgs.json`; all 77 logos there are on `altinncdn.no` today) when `logoOptions` is set | settings; data (the CDN's org list) | illustration yes (the error page during the run); org logo not exercised in the app |
| `https://tile.openstreetmap.org`, `https://cache.kartverket.no` | `img-src` | Map component's default layers (`F/layout/Map/utils.ts:15,19`) | bundle; replaced by the layout's `layers` | not exercised (no Map) |
| any | `img-src`, `media-src` | Image, Audio, Video, icons in Tabs and Options, `<img>` in text resources, custom map layers | **app data** (layouts, texts) | not exercised |
| any | `script-src`, `style-src` | `config/assets.json` external scripts and stylesheets (`IndexPageGenerator.cs:46,54`) | **app data** | not exercised |
| `data:` | `img-src` | the inlined Altinn logo | bundle | not recorded |
| `blob:` | `frame-src` | PDF preview iframe | bundle | not exercised |
| (none) | `frame-src` `'self'`/`srcdoc` | IFrame component (sandboxed `srcdoc`) | app data | not exercised |

Navigations, not fetches (no CSP fetch directive; `form-action`/`navigate-to` aside): `platform.<env>`
(login, logout, step-up), `af.<env>`, `am.ui.<env>`, `<env>` itself, the Nets hosted payment page (URL from the
backend), the text links (`info.altinn.no`, `docs.altinn.studio`, Slack) and the map attribution links. Not found in
the frontend: analytics (Matomo, Application Insights, Sentry, Hotjar), Google Fonts, Nets checkout JavaScript,
WebSockets or EventSource, and `altinn-orgs.json` (the monorepo moved that fetch to the backend).

**A CSP for an app page.** With the defaults, a page could carry `default-src 'self'; script-src 'self'
'nonce-<n>'; style-src 'self' 'unsafe-inline' https://altinncdn.no; font-src https://altinncdn.no; img-src 'self'
data: https://altinncdn.no; connect-src 'self' https://altinncdn.no; frame-src blob:; object-src 'none';
base-uri 'self'; frame-ancestors 'none'`, plus `img-src https://tile.openstreetmap.org https://cache.kartverket.no`
for an app with the default map, and the hosts of the app's own `assets.json`, layouts and texts. The generator
would have to emit the nonce on its inline bootstrap `<script>` (which carries `window.altinnAppGlobalData`) and add
the app's hosts from its files, which it already reads; `'unsafe-inline'` for styles stays until the bundle stops
injecting `<style>` (style injection and the Leaflet sprite fix); the runtime `<base href>` injection
(`features/baseurlinjection.ts`) needs `base-uri 'self'`. **Today no CSP is set** on the index page:
`SecurityHeadersMiddleware` sets `X-Frame-Options`, `X-Content-Type-Options`, `Referrer-Policy` and cache headers
only, and the one CSP in the backend is on the `/set-query-params` helper page (`HomeController.cs:237`).

### 3.3 pdf3 (`S/src/Runtime/pdf3`)

| Destination | Source (file:line) | Purpose | When | Where | Observed (phase 2) |
| --- | --- | --- | --- | --- | --- |
| the request's `url` | `internal/generator/browser_session.go:855` (`Page.navigate`; only `url.ParseRequestURI` checks it, `internal/types/pdf.go:264`; no request interception) | the page to print | every render | **data**; in production `https://{org}.apps.<host>/{org}/{app}/instance/…?pdf=1` from the app's `ExternalAppBaseUrl` | yes: the app page on the public origin |
| what the page loads | the page | bundle (app origin), `altinncdn.no` (favicon, fonts, logos), app `assets.json` hosts, map tiles | every render | data | **yes: `altinncdn.no`**, 9 connections |
| `PDF3_UPSTREAM_URL` (relay) | `cmd/relay/main.go:28,142-157`, `cmd/relay/relay.go:77-96` | relay to the shared pdf3 | every relayed request | config: `https://studio.platform.<env>.altinn.cloud/pdf/v3/generate` | n/a on localtest |
| `PDF3_WORKER_HTTP_ADDR` (proxy) | `cmd/proxy/main.go:68-76,334` | proxy to worker | every request | config, in-cluster | n/a on localtest |
| OTLP | `internal/telemetry/telemetry.go:147,176` | telemetry | continuous outside localtest | config (`pdf3-collector`, `otel-router`); off on localtest (no endpoint set) | none (off) |
| `https://altinn.studio/internal/observability/otlp` | `infra/core/base/collector-config.yaml:245-246` | the core collector's backhaul | continuous | config (collector pod, not the worker) | n/a |

Chromium runs with `--disable-background-networking`, `--disable-sync`, `--disable-default-apps`,
`--disable-extensions`, `--safebrowsing-disable-auto-update`, `--disable-breakpad` and the like
(`internal/browser/browser.go:230-273`), but not `--disable-component-update`; the binary is baked into the image
(`Dockerfile.worker:19`), nothing is downloaded at run time. The environment host table in
`internal/config/config.go:31-38` only labels telemetry; an unknown host is served the same. On localtest
(`PDF3_LOCALTEST_PUBLIC_BASE_URL={{origin}}`) a URL on that host gets the origin's scheme and port
(`cmd/worker/main.go:249-266,318-364`); other hosts pass unchanged.

### 3.4 The workflow engine (`S/src/Runtime/workflow-engine`, host `workflow-engine-app`)

| Destination | Source (file:line) | Purpose | When | Where | Observed (phase 2) |
| --- | --- | --- | --- | --- | --- |
| the app: `http://{Org}-{App}-deployment.default.svc.cluster.local/{Org}/{App}/instances/…/workflow-engine-callbacks/` | `workflow-engine-app/src/WorkflowEngine.App/Constants/Defaults.cs:11`, `Commands/AppCommand/AppCommand.cs:149,255-258` | app callbacks with a bearer callback token | every step | config template `AppCommandSettings:CommandEndpoint` + **data** (org and app from the request, inserted unescaped, `StringExtensions.cs:22-46`) | inside the stack (front door); nothing external |
| any absolute URI | `WorkflowEngine.Commands/Webhook/WebhookCommand.cs:51-60,79-83` (always registered) | `webhook` command | per step that uses it | **data** (the enqueuing app); no SSRF guard | not exercised |
| Azure Postgres (`workflow-engine-db-server-test.postgres.database.azure.com` in tt02) | `Program.cs:16-20`; `infra/runtime/syncroot/tt02/workflow-engine-app.yaml:62-63` | state | start-up (migrations), continuous | config | inside the stack |
| Entra ID (scope `ossrdbms-aad`) | `WorkflowEngine.Data/Extensions/ServiceCollectionExtensions.cs:34,46-65` | DB password token | start-up, every 55 min; not in Development | code (Azure.Identity), host from the workload-identity environment | n/a (Development) |
| `otel-router.runtime-obs.svc.cluster.local:4317` | `WorkflowEngine.Telemetry/Extensions/ServiceCollectionExtensions.cs:101,225,233` | telemetry | continuous; off on localtest (`EngineSettings__EnableTelemetry=false`) | config | none (off) |

On localtest the callback is `{{origin}}/{Org}/{App}/…` (manifest), so the engine reaches the app through the front
door.

### 3.5 The stack's components (localtest only)

Only what goes to the **internet** while active in the localtest build; calls between components and to the public
origin are in [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md)
section 2. Paths from `L/components/<name>/`. Every .NET image runs with `ASPNETCORE_ENVIRONMENT=Development`. No
manifest `envs` value is an internet, at22 or tt02 URL; no image fetches anything at container start (Arbeidsflate's
BFF runs pnpm through corepack at start; whether its update check reaches `registry.npmjs.org` is not known). No
component exports telemetry on localtest: the OTLP, Application Insights, App Configuration and Key Vault settings
are unset or forced empty by the fakes.

| Component | Domain | Client (file:line) | When | Where | Observed (phase 2) |
| --- | --- | --- | --- | --- | --- |
| Storage | `altinncdn.no/orgs/altinn-orgs.json` | `vendor/src/Storage/Repository/AltinnCdnOrganisationRepository.cs:72-76` (`appsettings.json:32`) | org number for `studio/instances/…` and `metrics/instances`; cached 600 s | config, not overridden by the fakes | not exercised (`studio/instances`, `metrics/instances` not called) |
| Authentication | `altinncdn.no/orgs/altinn-orgs.json` | `…/Altinn.Platform.Authentication/Services/OrganisationsService.cs:46,71` | `exchange/maskinporten` with a service-owner scope (the adapter, the BFF); cached 1 h | config | **yes**: 8 connections |
| Authentication | `platform.at22.altinn.cloud/accessmanagement/api/v1/` | `…/Integration/AccessManagement/AccessManagementClient.cs:87` (`appsettings.Development.json:59`) | `enduser/selfidentified/redeem-link` only | config, **a stack leak to at22** | not exercised, deliberately (it would call at22) |
| Authentication | at22 `OidcProviders:altinn`, `test.maskinporten.no` | `Services/OidcServerService.cs:1414-1423` | only a request with `iss=altinn` or `iss=https://test.maskinporten.no/` | config, dormant | not observed (dormant) |
| Register | `at22.altinn.cloud/sblbridge/profile/api/users/` | `…/Altinn.Register/Clients/SblProfileBridgeClient.cs:36,60` (`appsettings.json:44`) | `POST v2/internal/parties/self-identified` (an e-mail self-identified login) | config, **a stack leak to at22** | not exercised, deliberately (it would create a user in at22) |
| Resource Registry | `altinncdn.no/orgs/altinn-orgs.json`, on failure `raw.githubusercontent.com/Altinn/altinn-cdn/master/orgs/altinn-orgs.json` | `…/Altinn.ResourceRegistry.Integration/Clients/OrgListClient.cs:40-45` (`appsettings.json:20-21`) | `resource/orgs` on every request (no cache); the service-owner cache (1 h) for resource lists, `updated`, owner filters | config, kept on purpose (`fakes/LocaltestResourceRegistryExtensions.cs:57`) | **yes**: 4; the GitHub fallback not needed |
| Dialogporten (3 hosts) | `altinncdn.no/orgs/altinn-orgs.json` | `…/Infrastructure/InfrastructureExtensions.cs:536-538`, `ServiceOwnerNameRegistryClient.cs:46,57` | service-owner names per request, cached 24 h; also `/health/deep` (not the manifest's probe) | config | GraphQL **yes** (1); WebApi and Service no, but see `usage-tracking.masstransit.io` (section 8) |
| Dialogporten adapter | `altinncdn.no/orgs/altinn-orgs.json` | `…/Common/Extensions/ServiceCollectionExtensions.cs:305-311` | each instance sync; cached 5 min (fail-safe 2 h) | config | **yes**: 4 |
| Notifications | an order's `ConditionEndpoint` (any host) | `…/Altinn.Notifications.Integrations/Extensions/ServiceCollectionExtensions.cs:64` | an order with a send condition | **data**, passes through | no internet target seen |
| Events | subscriber webhooks | `vendor/src/Events/Services/WebhookService.cs:35,49` | delivery | data; the fake delivers only to dot-less names, `host.docker.internal` and the platform host and captures the rest (`fakes/LocaltestEventsOptions.cs:12`) | no internet target seen |
| Arbeidsflate BFF | `altinncdn.no/orgs/altinn-orgs.json` | `vendor/packages/bff/src/graphql/organizations/service.ts:60` | GraphQL `organizations` when the Redis key is missing (24 h) | code | **yes**: 1 |
| Receipt page (browser) | `altinncdn.no` (org list, favicon, logo), `use.fontawesome.com`, `use.fortawesome.com`, `fonts.googleapis.com` | `vendor/src/backend/Altinn.Receipt/Views/Receipt/receipt.cshtml:12-20`, `frontend/src/utils/receiptUrlHelper.ts:5-6` | page load | code | **yes**: `use.fontawesome.com`, `use.fortawesome.com`, `fonts.googleapis.com`, and `fonts.gstatic.com` (not predicted), org list and `ttd.svg`, `Altinn-logo-black.svg` |
| Arbeidsflate SPA (browser) | `altinncdn.no` (Inter CSS and fonts, org logos from the org list) | `vendor/packages/frontend/index.html:6`, `frontend/src/api/hooks/useDialogById.tsx:218` | page load, avatars | code; data. Application Insights is off (empty key); nginx's CSP (`nginx/nginx.conf:30`) allows `https:` in `connect-src` and `img-src` | **yes**: Inter CSS and three `woff2` |

Replaced or off under LOCALTEST (checked): Register's A2 bridge lookups (`ApiSource:Default=DB`) and NPR/SIRE jobs
(`Altinn:IsTest`); Profile's KRR (`test.kontaktregisteret.no`) and Brreg (`kof-*.ppe.brreg.no`) clients; Access
Management's and Authorization's SBL bridge, OED PIP and test token generator; Correspondence's Slack, Azure Resource
Manager, Maskinporten JWK rotation and tt02/ID-porten settings; Notifications' Dialogporten client; every
Maskinporten client (the localtest app's fake); Service Bus and Storage queues (local transports). The localtest app
makes no internet call. The workflow engine's and pdf3's telemetry is off (section 3.3, 3.4).

So a localtest stack needs the internet for **`altinncdn.no`** (six server-side org-list clients and two browser
pages), possibly `raw.githubusercontent.com` (the Resource Registry's fallback), the Receipt page's font hosts, and
**at22** on two self-identified-user paths, which are leaks from the stack to a shared test environment.

## 4. Where production enforcement would sit

What exists (monorepo `S/infra`, `S/src/Runtime`, the chart; the infra repository not read):

| Workload | Egress control today | Source |
| --- | --- | --- |
| app pods (`default` namespace, Linkerd-meshed) | **none**: the chart has no NetworkPolicy; nothing in `S/infra/runtime` selects app pods | `altinn-studio-charts/charts/deployment/templates/` |
| pdf3 workers (DIS Core) | CiliumNetworkPolicy: DNS, the collector, and `0.0.0.0/0`/`::/0`; `egressDeny` for private, link-local, metadata and Azure platform addresses and the host, remote-node and API-server entities | `S/src/Runtime/pdf3/infra/core/base/network-policies.yaml:43-90` |
| pdf3 collector | ingress only; the comment says `toFQDNs` needs Cilium's DNS proxy (on AKS: Advanced Container Networking Services), "not confirmed for DIS Core" | same file `:92-96` |
| workflow engine, gateway, pdf3 relay | ingress-only NetworkPolicies | `S/src/Runtime/workflow-engine-app/infra/kustomize/base/networkpolicy.yaml`, `S/src/Runtime/gateway/infra/kustomize/base/networkpolicy.yaml`, `S/src/Runtime/pdf3/infra/relay/base/relay.yaml:59-80` |
| observability | ingress and egress NetworkPolicies on the collectors | `S/infra/observability/` |
| the cluster's outbound path | per-org outbound public IP prefixes (`-prefix4`, `-prefix6`) that external parties allow-list | `altinn-studio-docs/content/altinn-studio/v9/manage-a-service/deployment/firewall/_index.en.md` (draft); the NAT or firewall is in the infra repository, **not confirmed** |

Where a list would go, and its format:

1. **Cilium FQDN policy per app namespace or for `default`** (`CiliumNetworkPolicy` with `toFQDNs: matchName/matchPattern`
   plus a DNS rule with `rules.dns` so the proxy sees the names): the closest fit, since the list is names, per pod
   label, enforced in the node. Needs the DNS proxy (ACNS on AKS), which the pdf3 comment says is unconfirmed. The
   list per app would be the platform host, `altinncdn.no`, the Application Insights ingestion host, the Maskinporten
   authority, plus the cluster services (`toEndpoints`/`toServices`) and per-app additions.
2. **Kubernetes NetworkPolicy**: CIDR only. Workable for in-cluster services and for a firewall's or proxy's address,
   not for `altinncdn.no` (a CDN with changing addresses) or `platform.altinn.no` (behind Azure Front Door or a WAF).
3. **Azure Firewall (or another egress firewall) on the cluster's outbound route**: application rules with FQDNs and
   wildcards for HTTP/S (SNI and Host), network rules for others (Fiks AMQP on 5671 needs an FQDN network rule, which
   needs the firewall's DNS proxy). Per cluster, not per app, unless rules use source IP groups per node pool or org.
4. **An egress proxy** (Squid, Envoy, or Linkerd's egress with `HTTPS_PROXY` in the app pods): an FQDN allow-list,
   logs per CONNECT host, per app if the proxy sees the app identity (mTLS from the mesh). .NET's `HttpClient`, the
   Azure SDKs and Go honour `HTTP(S)_PROXY`; the Fiks AMQP client (RabbitMQ) does not, so 5671 would need a network
   rule or a TCP proxy.

The list itself has three parts, and should be stated as such: **the platform's** (the same for every app in an
environment: platform host, in-cluster services, `altinncdn.no`, telemetry, Maskinporten), **per feature** (switched on
by app configuration the platform can read: Fiks, Nets, eFormidling, the library code lists), and **the app's own**
(declared by the app; open question 1).

## 5. Measurement design for phase 2

### 5.1 The methods compared

| | OpenTelemetry (client spans) | DNS and connection capture | Logging forward proxy | Deny-by-default network |
| --- | --- | --- | --- | --- |
| What it sees | every `HttpClient` call (`System.Net.Http` spans carry `server.address`, `url.full`), Npgsql, the caller's `service.name` | every DNS query and every connection start, from any process or container; TLS SNI and HTTP `Host` name the destination | every request through the proxy, by CONNECT host | every attempt that is refused |
| Attribution | the service, the instrumentation scope, the path | container by bridge address; host process through an `ss -p` poll | by client address only (all host processes share one) | by container; host processes not at all |
| Misses | uninstrumented clients (JWT metadata through IdentityModel's static client, Fiks AMQP, raw sockets), the browser, DNS; every container in the stack, because no image exports OTLP on localtest (pdf3, workflow engine: off; the .NET components: no endpoint) | names resolved before the capture and IP literals (both show as "unnamed": the address is still recorded), connections shorter than the poll for the process, encrypted ClientHello (not used by these clients) | anything that ignores `HTTP(S)_PROXY`: Node's `fetch` (Arbeidsflate's BFF), Chromium unless started with `--proxy-server`, AMQP; service names and the public origin must be in `NO_PROXY`; containers would need the variables, which the manifest cannot carry today (`envs` are literals for the service, and a runner flag would be new code) | it only says what breaks, not what is used; needs runner changes |
| Cost now | a collector container on loopback; the app run with `AppSettings__UseOpenTelemetry=true` | `tcpdump`, `tshark` (installed), `ss`; root | a proxy and environment changes for every process | studioctl and Aspire changes |

**Chosen:** the **capture** as the ground truth for every process (it cannot be bypassed and covers containers,
Node, Chromium and DNS), and **OpenTelemetry for the app** because it names the caller and the path, which makes
the reconciliation with section 3.1 direct. The **browser** is recorded by Playwright traces of the flow
(`resource-snapshot` entries in the `.network` file), which give the origin and the resource type, i.e. the CSP
directive. The proxy is not used: it misses the BFF, Chromium and AMQP, and containers cannot get the variables
without new runner code.

**Deny-by-default on localtest, later (not now).** The way to keep the list honest over time: a Podman network with
`internal: true` (no route out) for the stack, the front door as the only container also on an outbound network, and
an allow-list in a forward proxy for what genuinely needs the internet (today `altinncdn.no` for the components' org
lists, section 3.5; better a local copy of `altinn-orgs.json` served by the front door, which would empty the list). studioctl would need a network option per container in `containers.go` and a proxy
container in the manifest (a new key, since `envs` cannot name runner-provided proxies); Aspire the same through
`WithContainerRuntimeArgs`/a second network. The app on the host is outside the Podman network and would need the
proxy variables from the app runner. A separate task.

### 5.2 The scripts (`localtest/tools/egress/`, shellcheck clean)

| Script | Does |
| --- | --- |
| [`capture.sh`](../tools/egress/capture.sh) `start [DIR]` / `stop` (root) | `tcpdump` on the stack's bridge (read from `/etc/containers/networks/localtest-v2_network.json`: interface and subnet; started as soon as the network exists, so the capture can start before `env up`) for DNS to Aardvark and connection starts that leave the subnet; on the default-route interface for DNS to the host's resolver, SYNs, TLS handshakes and plain-HTTP requests (snap length 512); an `ss -tunp` poll every 0.5 s for (process, peer); a poll of container addresses every 10 s. Stops by pid file, no `pkill -f`. |
| [`summarize.sh`](../tools/egress/summarize.sh) `DIR [OUT]` | `tshark` over both captures: `dns-containers.tsv` (container, name, type, count), `dns-host.tsv`, `out-containers.tsv` and `out-host.tsv` (destination, port, SNI or Host or DNS name, count), `processes.tsv` (host process, peer, name, first and last seen), `unnamed.tsv` (addresses without any name: IP literals or names resolved before the capture). Private ranges are left out unless `EGRESS_KEEP_PRIVATE=1`. |
| [`otel.sh`](../tools/egress/otel.sh) `start DIR` / `stop` / `summarize DIR` | an OpenTelemetry Collector (`otel/opentelemetry-collector-contrib:0.161.0`, the version studioctl pins) on `127.0.0.1:4317`/`4318`, which is where the app runner's `OTEL_EXPORTER_OTLP_ENDPOINT=http://otel.local.altinn.cloud:4317` points (the hosts file maps the name to loopback; localtest-v2 has no collector of its own); writes spans to `traces.jsonl`; `summarize` lists the CLIENT spans per service, scope, `server.address`, port, method and normalised path. |
| [`browser-hosts.sh`](../tools/egress/browser-hosts.sh) `TRACE.network…` | origin, resource type, status and count from Playwright traces. |

The host-side half was tried on 2026-10-23 without a stack (`CTR=true`, `curl` to `altinncdn.no` and
`example.com`): both names came out with their addresses in `dns-host.tsv` and `out-host.tsv`, the `ss` poll
attributed host connections to `claude`, `dotnet`, `gh` and `curl` (shown as `-` for a connection shorter than the
poll), and an API connection opened before the capture landed in `unnamed.tsv`, as designed. The browser script was
checked on a trace of a page that loads an `altinncdn.no` image. Nothing has run against the stack.

### 5.3 The phase 2 run

1. `podman ps` empty; `env up -r localtest-v2 --with everything` on the pins (`/home/agent/sh`, the dev studioctl).
2. `otel.sh start` and `capture.sh start` **before** the app starts, so start-up calls are seen; then the app
   `ttd/expression-validation-test` with `AppSettings__UseOpenTelemetry=true` in its environment (the app runner keeps
   values the app's environment sets; to confirm, since `studioctl app env` prints the effective set).
3. The suites: the root suite, each component suite that has one, a browser flow under a Playwright trace (sign-in,
   instantiate, fill in, upload an attachment, submit, receipt, PDF, back to the inbox with the arbeidsflate group), a
   Correspondence send, a Notifications order, a Dialogporten sync, a PDF through pdf3; then five minutes idle for
   the background refreshers.
4. `capture.sh stop`, `otel.sh stop`, `summarize.sh`, `otel.sh summarize`, `browser-hosts.sh`; the small `.tsv` files
   to `localtest/reviews/egress/`, the pcaps stay outside the repository.
5. Reconcile: every observed name must be a row in section 3 with its caller; every row not observed gets "not
   exercised" and the feature that would exercise it. Host noise (this sandbox's own agents and tools) is named and
   left out.
6. `env down`, leftovers by pid, `podman ps` empty, the four `localtest-v2-*` volumes untouched, `bin`/`obj` cleaned.

## 6. Open questions for the owner

1. **The app's own third-party calls**: should an app declare them (for example a list of hosts in
   `applicationmetadata.json` or in `config/`), so the platform can build its egress rule and the page's CSP from one
   list, and localtest can enforce the same list?
2. **Localtest egress-denied by default?** (Section 5.1, last paragraph.) It keeps the list honest, at the cost of
   runner work and of breaking the components' `altinncdn.no` fetches until those are served locally.
3. **`altinncdn.no` from the app pod**: the org list is one JSON file; should the platform serve it (Register's org
   data, or the cluster) so app pods need no CDN at all? The browser still needs the CDN for fonts and logos.
4. **The Application Insights path**: the default telemetry goes straight to Azure Monitor's ingestion; with
   `UseOpenTelemetry` and the in-cluster `otel-router` the app pod would need no Azure endpoint. Is the switch planned?
5. **The workflow engine's `webhook` command and pdf3's URL** are open by data: should the engine validate webhook
   targets against the same list, and should pdf3 refuse a host that is not the requesting app's?
6. **Cilium FQDN policies**: is Cilium's DNS proxy (ACNS) available in the apps clusters? That decides between
   per-app `toFQDNs` policies and a firewall or proxy per cluster.

7. **The stack's own internet calls**: should the two at22 paths (Register's SBL bridge for self-identified users,
   Authentication's Access Management endpoint) be pointed at the stack or failed fast, and should the front door
   serve `altinn-orgs.json` (and the Resource Registry's GitHub fallback be dropped), so a stack needs no internet?

## 7. Not verified

- The cluster's outbound path (NAT gateway, Azure Firewall, user-defined routes) and any egress rules outside the
  monorepo: the infra repository was not read.
- The Application Insights ingestion host names (they come from each connection string, not from code).
- Whether Chromium in pdf3 makes component-update calls despite `--disable-background-networking`.
- Altinn.Authorization.ServiceDefaults (Register, Authentication, Resource Registry, Access Management) was read from
  its DLL strings, not source; the Arbeidsflate npm packages (`@altinn/altinn-components`) were not read.
- At run time (phase 2): the not-exercised rows in section 3 (each names its feature); the stack's two at22 paths were
  left alone on purpose.

## 8. Observed (phase 2)

**The run** (2026-10-24, wall clock 04:09 to 19:38: the VM was suspended for long stretches between steps, which
also expired sign-in tokens mid-run; see below). `/home/agent/sh` (dev studioctl from main, pin `sha-e2a9b82`, task
40's service-name settings in every component), `env up -r localtest-v2 --with everything` (22 containers). The
collector (`otel.sh start`) and the capture (`capture.sh start`) were started **before** `env up`, so the containers'
start-up is in the capture. Then, all under capture:

- the app `ttd/expression-validation-test` from an `altinn-studio` worktree at `819ad7362e` (project references to
  `src/App/backend`, built before the capture), `studioctl run -d` with `AppSettings__UseOpenTelemetry=true`, started
  twice: once plain, once with `--dev-frontend` and the monorepo frontend's built `dist` (from 2026-10-17) served on
  port 8080, because a project-reference build carries no bundle (the first page load got 404 for
  `altinn-app-frontend.js`);
- the suites with a verified history, in this order: root 26/26, Authentication 34/34, Authorization 31/31,
  Dialogporten 60/60, Correspondence 78/78 (a send), Events 24/30, Notifications 61/64 (orders),
  Register 11/14, the localtest app 65/67. The failures are not egress: Events and Notifications got `401` on tokens
  that had expired across a VM suspension (the suites took 48 and 26 minutes of wall clock); Register's three see
  this run's own app registered; the localtest app's two are the known `FRONTEND_DIST` check and the 503 check for a
  service that is off (everything was on). Timeline: [`egress/suites-timeline.txt`](egress/suites-timeline.txt);
- a browser flow under a Playwright trace: sign-in as Sophie Salt, instantiate, fill in, upload a PDF attachment with
  its document type, submit (the PDF service task: pdf3 rendered the page, a 90 kB `application/pdf` came back), the
  app's receipt, the platform Receipt page, Arbeidsflate (its own sign-in at level 3), the inbox and the dialog (the submitted instance had become a dialog through the adapter: the Dialogporten sync);
- five idle minutes, then `capture.sh stop`, `otel.sh stop`, the summaries.

**Per process, what left the machine or the stack** (host noise named last):

| Process | Observed | Inventory row |
| --- | --- | --- |
| the app (`dotnet`, OTel `service.name` `expression-validation-test`) | the public origin (552 client spans), `pdf.<origin>` (3), `workflow-engine.<origin>` (174); **`altinncdn.no`** `/orgs/altinn-orgs.json` (2); **`data.ssb.no`** `/api/klass/v1/classifications/{n}/codesAt` (2) | 3.1; `data.ssb.no` was **not predicted** |
| the browser on the app page | the app origin, `app-frontend.<origin>` (the served bundle), `altinncdn.no` (Altinn DIN CSS and fonts, the logo SVG, the help illustration) | 3.2 |
| the browser on Receipt and Arbeidsflate | `altinncdn.no` (org list, `orgs/ttd/ttd.svg`, `Altinn-logo-black.svg`, Inter CSS and fonts), `use.fontawesome.com`, `use.fortawesome.com`, `fonts.googleapis.com`, **`fonts.gstatic.com`** | 3.5; `fonts.gstatic.com` not predicted (Google Fonts' CSS points there) |
| pdf3 | `altinncdn.no` (9 connections: what the app page loads) | 3.3 |
| Authentication, Resource Registry, Dialogporten adapter, Dialogporten GraphQL, Arbeidsflate BFF | `altinncdn.no` (8, 4, 4, 1, 1 connections) | 3.5 |
| **Dialogporten WebApi, Service and GraphQL** | **`usage-tracking.masstransit.io`** (one connection each, at start-up) | **not predicted** |
| the workflow engine, Storage, Register, Profile, Authorization, Access Management, Events, Notifications, Correspondence, Receipt (server), the localtest app, Postgres, Redis, Arbeidsflate's nginx | nothing outside the stack | 3.4, 3.5 |

DNS from the containers matched the connections (the only other name: Access Management's reverse lookups of the
Postgres address, `5.3.89.10.in-addr.arpa`, answered inside). `unnamed.tsv` is empty: every outbound connection had
an SNI or a DNS name. No IPv6, no QUIC from the stack. The host asked the upstream resolver once for
`local.altinn.cloud` AAAA (the hosts file has only the IPv4 line).

**Not predicted by the static inventory:**

1. **`data.ssb.no` from the app.** The sample app calls `services.AddAltinnCodelists()` (`Program.cs:39`), and the
   monorepo's `Altinn.Codelists` library (`S/src/App/codelists`) fetches SSB classifications
   (`SSB/Clients/ClassificationSettings.cs:11`, `https://data.ssb.no/api/klass/v1/classifications/`); its other
   providers default to `https://ws.geonorge.no/kommuneinfo/v1/` (Kartverket) and
   `https://www.bring.no/postnummerregister-ansi.txt` (Posten). It is an app-side library, not the backend, so it
   belongs to "the app's own" part of the list, but it is a library the platform ships: a production list for apps
   that use it needs `data.ssb.no`, `ws.geonorge.no` and `www.bring.no`.
2. **`usage-tracking.masstransit.io`** from Dialogporten's three hosts at start-up: MassTransit's usage telemetry
   (Dialogporten pins MassTransit `[8.5.7, 9.0.0)`; nothing in its code or the fakes calls `DisableUsageTelemetry`). Upstream's production Dialogporten would send the same
   unless it turns it off; for the stack it is a call out that a fake should switch off.
3. **`fonts.gstatic.com`** behind the Receipt page's Google Fonts stylesheet (the inventory had only
   `fonts.googleapis.com`).
4. A leftover **MSBuild** node of the app's build held a connection to `altinncdn.no` when the capture started; it
   was opened before the capture, so what fetched it is not known (build time is out of scope).

**Host noise**, not the app or the stack: the Playwright Chromium's own Google traffic (`www.google.com`,
`content-autofill.googleapis.com`, `mtalk.google.com`, `update.googleapis.com`, `clients2.google.com`,
`android.clients.google.com`, `accounts.google.com`; Chrome for Testing has no `--disable-background-networking`
here), Podman pulling the `tt02` pdf3 and workflow-engine images (`ghcr.io`, `pkg-containers.githubusercontent.com`),
`playwright-cli` (`registry.npmjs.org`), and this sandbox's agents (`api.anthropic.com`, Datadog intake,
`api.github.com`).

**Not exercised**, with the feature that would exercise it: in the app, Events publishing
(`RegisterEventsWithEventsComponent`), Notifications and Correspondence from the app, eFormidling, signing
(delegation), the Maskinporten authority (a stored client), Application Insights (the default telemetry), library
code lists (`studioprodsc.blob.core.windows.net`), Fiks, Nets and the own-host webhook, Key Vault; in the browser,
postcodes (an Address component), map tiles (a Map component), layout-supplied hosts, the PDF preview; in the stack,
Storage's org list (`studio/instances`, `metrics/instances`), the Resource Registry's GitHub fallback (only when
`altinncdn.no` fails), the workflow engine's `webhook` command, Notifications' condition URLs and Events' webhooks to
an internet host, and the two **at22** paths (an e-mail self-identified login, and `redeem-link`), which were left
alone because they would reach, and in Register's case create a user in, a shared test environment.

**The capture files**, small text tables from the scripts ([`egress/`](egress/); the pcaps stayed outside the
repository): `capture-*.tsv` (`summarize.sh`: DNS and outbound connections per container, the host's DNS and
connections, host processes), `otel-client-spans.tsv` and `otel-addresses.tsv` (`otel.sh summarize`),
`browser-hosts.tsv` and `browser-third-party-urls.tsv` (from the traces), `suites-timeline.txt`.

**A fix to the scripts from the run**: the containers' address poller ran with `errexit` from the script and stopped
after its first round (a probe container gone between `ps` and `inspect`), so the containers that started later
showed as bare addresses; the pollers now run without `errexit`, `stop` takes a last snapshot, and the bridge capture
waits for the network, so a capture can start before `env up`. The container names in the tables come from
the last snapshot `stop` takes, before `env down`.

**Clean-up**: the app stopped, `env down` (the network removed), the collector removed, `podman ps -a` empty, the five
`localtest-v2-*` volumes and the hosts entries untouched, the static server, the browser session and studioctl-server
stopped by pid, the `altinn-studio` worktree (with its `bin`/`obj`) removed.

So the **final list per process**, observed or predicted, for production:

| Process | Always | Per feature or data |
| --- | --- | --- |
| app pod (monorepo backend) | `platform.<env>`, in-cluster PDF and workflow engine, `altinncdn.no`, Application Insights ingestion (or the cluster's OTLP collector) | Maskinporten authority, `studioprodsc.blob.core.windows.net`, Fiks (`api.`/`io.fiks[.test].ks.no`), Nets, own host, Key Vault and Entra ID; with `Altinn.Codelists`: `data.ssb.no`, `ws.geonorge.no`, `www.bring.no`; the app's own |
| browser (CSP) | app origin, `altinncdn.no` | `tile.openstreetmap.org`, `cache.kartverket.no` (Map), hosts from layouts, texts and `assets.json` |
| pdf3 worker | the app's public host, `altinncdn.no` | whatever the page loads |
| workflow engine | the app (in-cluster), Postgres, Entra ID, OTLP | `webhook` targets (data) |
| localtest stack | `altinncdn.no` (six components, Receipt and Arbeidsflate pages), `usage-tracking.masstransit.io` (Dialogporten), Receipt's `use.fontawesome.com`, `use.fortawesome.com`, `fonts.googleapis.com`, `fonts.gstatic.com` | `raw.githubusercontent.com` (fallback), at22 (two self-identified paths) |
