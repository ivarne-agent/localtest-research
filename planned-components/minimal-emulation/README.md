# Minimal mode: the platform emulated in the localtest container

Research for task 33 ([`../../TODO.md`](../../TODO.md)), 2026-10-23. Design only: nothing here is built. **Scope as the
owner decided it on 2026-10-23**, replacing the brief's "one container, everything in memory":

- **Storage and the workflow engine are not emulated.** They are too central to apps to fake, so the real `storage`
  and `workflow-engine` images run, with `postgres` for their two databases.
- **Postgres and every read/write volume are ephemeral in this mode.** There are no named volumes, so stopping the
  mode leaves a clean slate for Storage and the workflow engine too.
- **pdf3 is not in the set.** The localtest container answers the PDF endpoint with one static file whose single page
  says "Minimal PDF without content".

So the minimal set is **four containers (postgres, storage, workflow-engine, localtest)** instead of the `default`
group's thirteen. The localtest container emulates Register, Profile, Authentication, Authorization (permit-all),
Access Management, Events, the Resource Registry, Notifications and the PDF service, in memory. The real components
stay the default for fidelity; the minimal mode is for quick work and an app's own tests.

Paths: in this repository relative to this file; in other repositories as `app-lib-dotnet:`, `altinn-studio:` (the
monorepo) or `app-frontend-react:` followed by the path inside that repository.

## Recommendation in short

1. **The mode is one manifest group, `minimal` (`postgres`, `storage`, `workflow-engine`, `localtest`).** It replaces
   `default` and does not add to it. The emulated set is derived, not listed: every HTTP container of `default` that
   `minimal` does not start. For each of those, the runtime injects `services__<name>__http__0=http://localtest:8080`.
   The manifest format does not change.
2. **The front door emulates a service when that service's address is its own.** It maps the service's prefix as real
   endpoints ahead of the forwarder, so it never forwards to itself. With no address, the 503 "not running" stays as it
   is today.
3. **The runners express "ephemeral" in code**: a tmpfs mount in place of each manifest volume, and session lifetime in
   Aspire. The manifest does not carry it. `env reset` has nothing to remove in this mode.
4. **The emulation is a rewrite in minimal-API style**, about 2,000 lines of application code plus tests, in
   `localtest/localtest/Emulation/<Service>/`. The test-data format files (`TestDataFile`, `TestDataMerger`) are
   linked from the Register component. Nothing of the monorepo's MVC emulation is vendored; its route list and
   response shapes serve as the reference.
5. **Phase 1** covers the basic flow (Register, Profile, Authentication, permit-all PDP with party lists, Access
   Management, Events, Notifications as lists, the static PDF), with the root smoke test and a Cypress subset as the
   gate. The policy-evaluating PDP is a later option.

## 1. What runs, and who calls what

```
browser / app / tests ──► localtest:8080 (published as :8000)
                            ├─ /{org}/{app}/            → the app (registered)
                            ├─ /storage/…               → storage:5010        (real, Postgres + tmpfs blobs)
                            ├─ host workflow-engine.…   → workflow-engine:9090 (real, Postgres)
                            ├─ host pdf.…               → static PDF           (emulated)
                            └─ /register /profile /authentication /authorization
                               /accessmanagement /events /resourceregistry /notifications → emulated in-process
storage ──► http://local.altinn.cloud:8000 (host gateway) ──► the same front door
workflow-engine ──► the app's callbacks, through the front door
```

The 13 containers of `default` are listed in the
[manifest](../../localtest/studioctl/src/cli/internal/localtestv2/manifest/manifest.json); `storage` and
`workflow-engine` depend only on `postgres`, so the roots `{localtest, storage, workflow-engine}` close to exactly the
four containers.

### 1.1 The calls Storage and the workflow engine make

These are the owner's added question: the real services must find everything they call in the localtest container.

| # | Caller | Call | Target | When | What the emulation answers |
| --- | --- | --- | --- | --- | --- |
| S1 | Storage, JwtCookie ([`Program.cs:280-285`](../../localtest/components/storage/vendor/src/Storage/Program.cs)), `MetadataAddress` from [`LocaltestStorageExtensions.cs:46`](../../localtest/components/storage/fakes/LocaltestStorageExtensions.cs) | `GET authentication/api/v1/openid/.well-known/openid-configuration`, then its `jwks_uri` | Authentication | First authenticated request, then cached | The discovery document and a JWKS with the key that signs the emulation's `AltinnStudioRuntime` tokens. Storage validates neither issuer nor audience, only the key and the lifetime (no clock skew) |
| S2 | Storage, `AccessTokenHandler` with the localtest key provider ([`LocaltestAuth.cs:19-40`](../../localtest/components/storage/fakes/Auth/LocaltestAuth.cs)) | none: reads `/certs/localtest-accesstoken.pfx` | — | `PlatformAccess` endpoints | Nothing. The emulation signs its Studio and platform tokens with the same certificate |
| S3 | Storage PEP (`Altinn.Common.PEP` 5.0.0): [`StorageAccessHandler.cs`](../../localtest/components/storage/vendor/src/Storage/Authorization/StorageAccessHandler.cs), [`AuthorizationService.cs:103-133, 213, 253, 293-299, 331-336`](../../localtest/components/storage/vendor/src/Storage/Authorization/AuthorizationService.cs) | `POST authorization/api/v1/decision`, anonymous (subscription-key header only) | PDP | Every instance, data and process endpoint (cached 5 min), instantiate, instance lists | `Permit` per request. **For a multi-request, one result per combination, each echoing the request's `category[].attribute[]`**: Storage maps results back to instances by the instance-id and action-id attributes (`:121-133`), so a single bare `Permit` filters every instance out of a list |
| S4 | Storage `RegisterService.PartyLookup` ([`RegisterService.cs:97-110`](../../localtest/components/storage/vendor/src/Storage/Services/RegisterService.cs)) | `POST register/api/v1/parties/lookup` `{ssn \| orgNo}` | Register | Only `GET instances` with `X-Ai-InstanceOwnerIdentifier` | The party, or 404 (read as "no instances") |
| S6 | Storage outbox ([`LocaltestDialogportenAdapterClient.cs:38-80`](../../localtest/components/storage/fakes/Dialogporten/LocaltestDialogportenAdapterClient.cs)) | `POST storage/dialogporten/api/localtest/enqueue` | Dialogporten adapter | Each instance mutation with events | 503 "not running" from the front door, as today with the group off: Storage logs it and drops the command |
| S7 | Storage ([`AltinnCdnOrganisationRepository.cs:72-80`](../../localtest/components/storage/vendor/src/Storage/Repository/AltinnCdnOrganisationRepository.cs)) | `GET https://altinncdn.no/orgs/altinn-orgs.json` | Internet | Studio instance listing and metrics | Not ours: internet, as today |
| W1 | Workflow engine `AppCommand` ([`AppCommand.cs:153-159, 237-257`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/workflow-engine-app/src/WorkflowEngine.App/Commands/AppCommand/AppCommand.cs)) | `POST {AppCommandSettings__CommandEndpoint}{commandKey}`, the app's `workflow-engine-callbacks` through the front door | The app | Each workflow step | Not emulated: an app route |
| W2 | Workflow engine `WebhookCommand` | Any absolute URI a workflow names | Whatever it names | Webhook steps only | Not emulated |

Storage's `RegisterService.GetParty` (`parties/{id}`) has no caller. Storage makes no call to Events, the Resource Registry, Profile or Access Management. The workflow engine makes no
platform call at all: it validates no token and calls only the app (its `Program.cs` is 35 lines: Postgres and the
callbacks). So the emulation must serve **four** things for the real services: the OIDC discovery and JWKS, the PDP's
`decision` (with echoed categories), `parties/lookup`, and a 503 on the adapter's path.

### 1.2 How their calls reach the localtest container

**Today no component reads `services__*` for its URLs.** Every factory client still calls the public origin through
pass-through, and Storage derives its URLs from one `GeneralSettings__PlatformEndpoint=http://local.altinn.cloud:8000/`
([`LocaltestStorageExtensions.cs:35-66`](../../localtest/components/storage/fakes/LocaltestStorageExtensions.cs);
[`CONVENTIONS.md`, "Service discovery"](../../localtest/CONVENTIONS.md#service-discovery), rule 4). That name resolves
to the host gateway and so to the front door's published port. So in minimal mode **Storage needs no setting
changed**: its three calls already arrive at the front door on `/authentication`, `/authorization` and `/register`.

The runtime still injects, as the owner asked, `services__<name>__http__0=http://localtest:8080` for every emulated
name (`register`, `profile`, `authentication`, `authorization`, `access-management`, `events`, `resource-registry`,
`notifications`, `pdf3`), into every container. The resulting paths are unchanged:

- When Storage's settings migrate to service names (`http://register/register/api/v1/`,
  [`service-discovery.md`](../service-discovery.md)), the resolver turns them into
  `http://localtest:8080/register/api/v1/…`.
- The front door treats any host name other than its four dedicated ones as the main host and routes by path prefix
  ([`PlatformRoutes.cs:90-116`](../../localtest/localtest/Proxy/PlatformRoutes.cs)), so `Host: localtest` reaches the
  same emulated endpoints. No prefix collides: they are the public paths, with `/storage/dialogporten` matched before
  `/storage`.
- `GeneralSettings__OpenIdWellKnownEndpoint` stays on the public origin, by the rule that issuers and `jwks_uri` are
  what a browser sees ([`CONVENTIONS.md`](../../localtest/CONVENTIONS.md#the-public-origin-and-host-names)).

**Self-loop.** With `services__register__http__0=http://localtest:8080` and no emulated `/register` route, the
forwarder would send a `/register` request to itself, forever
([`Forwarder.cs`](../../localtest/localtest/Proxy/Forwarder.cs), the fallback at
[`Program.cs:126`](../../localtest/localtest/Program.cs)). Two guards prevent this:

- The emulated prefixes are mapped as real endpoints before the fallback.
- A start-up check refuses a `services__<x>` that names the front door itself while `x` has no emulation.

The front door's own provisioning and Home code also call `http://register/…` and `http://authentication/…`
([`LocaltestProvisioner.cs:52-227`](../../localtest/localtest/Provisioning/LocaltestProvisioner.cs),
[`AuthenticationClient.cs:29-91`](../../localtest/localtest/Home/AuthenticationClient.cs)). In minimal mode these go
to the in-process stores directly (section 3.3), not back over HTTP.

## 2. Inventory: what an app calls, and the minimal answer

App-lib 8 is `app-lib-dotnet:src/Altinn.App.Core` (`CORE` below; its clients under `Infrastructure/Clients/`, `CL`); the monorepo
backend is `altinn-studio:src/App/backend/src/Altinn.App.Core` (`M`). The monorepo emulation is
`altinn-studio:src/Runtime/localtest/src` (`OLD`). **F** marks a call the frontend makes directly. Storage and
workflow-engine rows are in the table to show the flow; they are served by the real images.

### 2.1 Basic flow

| Endpoint | Client | Service | Flow | Old emulation | Minimal behaviour |
| --- | --- | --- | --- | --- | --- |
| `GET authentication/api/v1/openid/.well-known/openid-configuration` and `…/jwks` | JwtCookie in the app (`app-lib-dotnet:src/Altinn.App.Api/Extensions/ServiceCollectionExtensions.cs:571-596`, `AppSettings__OpenIdWellKnownEndpoint`), Storage S1 | Authentication | Every authenticated request (cached) | `OLD/Controllers/Authentication/OpenIdController.cs:49,85` | Discovery and JWKS of the emulation's signing key |
| `GET authentication/api/v1/authentication?goto=` (**F**: redirect when not signed in) | `M:…/Altinn.App.Api/Controllers/HomeController.cs:137-151`; app-frontend `appUrlHelper.ts:137-167` | Authentication | Start page, login | `OLD/…/AuthenticationController.cs:55` (redirect to `/?goto=`) | 302 to the localtest test-user picker (Home), as today |
| `POST /Home/LogInTestUser`, `GET /Home/GetTestUserToken`, `/Home/GetTestOrgToken`, `/Home/GetTestStudioToken` | Browser, tests, provisioning; `AuthenticationTokenResolver.cs:77-81` in localtest | localtest Home | Login | `OLD/Controllers/HomeController.cs:147-331` | Same contract; tokens minted in-process (section 3.3) |
| `GET authentication/api/v1/refresh` | `CL/Authentication/AuthenticationClient.cs:46-48` (keepAlive through the app, **F** every 10 min via the app) | Authentication | Keep-alive | `OLD/…/AuthenticationController.cs:42` | New token from the presented one, same claims, new lifetime |
| `GET authentication/api/v1/exchange/maskinporten` | `CORE/Features/Maskinporten/MaskinportenClient.cs:239-252` | Authentication | App acting as org (Correspondence, Storage as org) | none | Exchange a Maskinporten token from the in-image fake for an org token (the fake's signature checked) |
| `GET authorization/api/v1/parties?userid=`, `GET …/parties/{id}/validate?userid=` | `CL/Authorization/AuthorizationClient.cs:78-82, 103-106` (app-lib 8) | Authorization | Party selection, selected party | `OLD/Controllers/Authorization/PartiesController.cs:32,57` | The user's acting-for list from the test data (the Register fake's `actingFor` shape), `validate` = membership |
| `GET accessmanagement/api/v1/enduser/authorizedparties` | `M/Infrastructure/Clients/Authorization/AuthorizationClient.cs:78` (monorepo backend) | Access Management | Party selection | `OLD/…/AuthorizedPartiesController.cs:19` | Same list, in Access Management's DTO |
| `POST authorization/api/v1/decision` | PEP `PDPAppSI` (`Altinn.Common.PEP`), callers `CL/Authorization/AuthorizationClient.cs:132-210`, `Altinn.App.Api/Controllers/InstancesController.cs:1492`, `StatelessDataController.cs:426`; Storage S3 | PDP | Instantiate, read/write, process next, actions, sign | `OLD/Controllers/Authorization/DecisionController.cs:72` (ABAC on the app's policy) | Permit every request; echo categories per multi-request; no obligations (section 4.4) |
| `GET register/api/v1/parties/{id}` | `CL/Register/AltinnPartyClient.cs:69-72` | Register | Party, instantiate | `OLD/Controllers/Register/PartiesController.cs:35` | From the test-data store |
| `POST register/api/v1/parties/lookup` | `AltinnPartyClient.cs:105-110`; Storage S4 | Register | Instantiate by SSN/org number, signing | `…/PartiesController.cs:56` | By SSN or org number; 404 when absent |
| `POST register/api/v1/parties/partylist?fetchSubUnits=true` | `CORE/Internal/Registers/RegisterClient.cs:99-112` | Register | Instantiate, prefill | `…/PartiesController.cs:81` | By ids, with sub-units |
| `POST register/…/apps/parties/query?fields=id,user.id`, `POST …/access-management/parties/query` | `AltinnPartyClient.cs:137-145, 181-189` | Register | Instantiate by URN; Notifications' party UUID | none | The two fields asked for, from the store |
| `GET register/api/v1/persons` (`X-Ai-NationalIdentityNumber`, `X-Ai-LastName`) | `CL/Register/PersonClient.cs:67-73` | Register | Lookup component | `OLD/…/PersonsController.cs:44` | Match SSN and last name; 404 otherwise |
| `GET register/api/v1/organizations/{orgNr}` | `CL/Register/RegisterERClient.cs:72-76` | Register | Lookup, instantiate | `OLD/…/OrganizationsController.cs:37` | From the test data |
| `GET profile/api/v1/users/{id}`, `POST profile/api/v1/users` (SSN), `GET …/users/byuuid/{uuid}` | `CL/Profile/ProfileClient.cs:95-172` | Profile | Current user, language, pre-selected party | `OLD/Controllers/Profile/UsersController.cs:34,50` | `UserProfile` from the test data (the Register fake's `profile` block: language, `preSelectedPartyId`, `doNotPromptForParty`, user type) |
| `storage/api/v1/applications`, `…/texts`, `instances`, `data`, `process`, `lock`, `sign`, `mutations` | `CL/Storage/*`; `M/…/DataClient.cs:564-576` (`mutations`) | Storage | Everything | `OLD/Controllers/Storage/*` | **Real Storage** |
| `api/v1/{ns}/workflows…`, `mailboxes…` | `M/Internal/WorkflowEngine/Http/WorkflowEngineClient.cs` | Workflow engine | Process next (monorepo backend) | none | **Real workflow engine** |
| `POST {ApiPdf2Endpoint}` (`http://pdf.local.altinn.cloud:8000/pdf`) | `CL/Pdf/PdfGeneratorClient.cs:144` | pdf3 | PDF at task end, receipt, signing, preview | none (the old runtime ran a PDF container) | The static PDF (section 4.9) |

### 2.2 Optional flows

| Endpoint | Client | Service | Minimal behaviour |
| --- | --- | --- | --- |
| `POST events/api/v1/app` | `CL/Events/EventsClient.cs:94-107` (instance and process events) | Events | Append to an in-memory list; 200. `GET events/api/localtest/events` reads it back (the Events component's inspection path) |
| `POST events/api/v1/subscriptions` | `CL/Events/EventsSubscriptionClient.cs:63-76` (eFormidling start-up) | Events | Echo the request with an id, as `OLD/Services/Events/Implementation/SubscriptionService.cs:29-32`; nothing is delivered |
| `POST notifications/api/v1/orders/email`, `…/orders/sms`, `…/future/orders`; `PUT …/orders/{id}/cancel` | `CORE/Features/Notifications/*` | Notifications | Store the order, answer 202 with an order id; cancel marks it. `GET notifications/api/localtest/messages` (the component's inspection path) lists them for tests |
| `POST accessmanagement/api/v1/app/delegations/resource/{r}/instance/{i}`, `…/delegationrevoke/…` | `CL/AccessManagement/AccessManagementClient.cs:59,95` (signing delegation) | Access Management | Record and echo a `DelegationResponse` with the rights marked delegated; no effect on decisions (everything is permitted anyway) |
| `GET /Home/Localtest/Version` | `CORE/Internal/LocaltestValidation.cs:171-174` | localtest | Unchanged (already in the image) |
| Correspondence `correspondence/api/v1/…` | `CORE/Features/Correspondence/CorrespondenceClient.cs`; used by the signing receipt and call-to-action (`CORE/Features/Signing/Services/SigningReceiptService.cs:71`, `SigningCallToActionService.cs:113`) when the app configures a correspondence resource | Correspondence | 503 "not running" (not emulated; the group is unavailable in minimal mode, section 5). An app whose signing sends receipts by Correspondence fails that step in minimal mode; open question 11 |
| Resource Registry | none from the app, Storage or the workflow engine | Resource Registry | Only provisioning step 3b called it, and that step is skipped in minimal mode. The prefix answers 501 problem details "not emulated in minimal mode" for any path |
| Arbeidsflate links, Dialogporten | navigation only (**F**, `app-frontend-react:src/utils/urls/urlHelper.ts:20-108`; `M/Configuration/PlatformFrontendSettings.cs`) | — | The runners leave the Arbeidsflate links unset in minimal mode, so "back to inbox" goes to the start page (as with the group off) |

**What the frontend fetches itself.** app-frontend-react sends every request to the app backend under `/{org}/{app}/`
(`app-frontend-react:src/utils/urls/appUrlHelper.ts:5-26`): profile, parties, keepAlive, texts. Its direct platform
use is navigation only: the login redirect, the auth-level upgrade and logout. It also fetches from altinncdn.no
(postcodes, logo, `altinn-orgs.json`). In the monorepo backend the asset bundle is served by the app itself
(`M/…/Internal/App/IndexPageGenerator.cs:35`); app-lib 8 apps load it from the CDN or the front door's
`/app-frontend`. Nothing here adds an emulated endpoint.

## 3. Fit in the image

### 3.1 Where the code lives

The front door is a Native AOT minimal API (`CreateSlimBuilder`, `PublishAot`, `TreatWarningsAsErrors`; 36.5 MB
image, [`localtest/README.md`](../../localtest/localtest/README.md)). The emulation follows the image's existing
feature folders (`IdPorten/`, `Maskinporten/`, `Home/`):

```
localtest/localtest/Emulation/
  EmulationMode.cs           which services are emulated (3.2), the start-up self-loop check
  TestData/                  the store built from the seed and the registered apps' testData.json
  Register/ Profile/ Authentication/ Authorization/ AccessManagement/
  Events/ Notifications/ ResourceRegistry/ Pdf/   one static Map(app) per service, minimal APIs
  EmulationJsonContext.cs    source-generated JSON for every DTO (no reflection; AOT)
```

Each service module maps its routes under its public prefix (`app.MapGroup("/register/api/v1")`), only when that
service is emulated. The fallback forwarder stays last. No MVC, no Newtonsoft, no `JWTCookieAuthentication`, no PEP
package: the emulation validates the tokens it receives itself (`System.IdentityModel.Tokens.Jwt` is already used by
the image's token code), and only where the real service would.

### 3.2 How the front door chooses between forwarding and emulating

| Option | How | 503 behaviour | Verdict |
| --- | --- | --- | --- |
| A. One switch `Localtest__Emulate=true` | Emulate the whole set; forward nothing in it | Unchanged for services outside the set | Simple, but a second source of truth beside `services__*`: the switch and the runtime's variables can disagree |
| B. Emulate whatever has no `services__` entry | Absence means "emulate" | **Lost**: an optional group that is off would be emulated silently instead of "not running" | Rejected: it hides the "not running" answer that tasks 25 and 26 introduced |
| **C. Emulate a service whose `services__<name>__http__0` is the front door's own address** | The runtime already decides every address; `http://localtest:8080` (the front door's alias and port) means "here" | Unchanged: no entry is still 503 "not running", a foreign address is still forwarded | **Recommended** |

Option C keeps one decision in one place, the runtime's variables, which is what the owner asked the runtime to set.
It also needs no new configuration for Storage. It allows per-service emulation later (for example a real Register
with an emulated PDP), but phase 1 tests only the full set. The front door knows its own address through
`Localtest__SelfAddress`, default `http://localtest:8080`. `GET localtest/api/routes` shows `emulated` as a service's
address, so `env status` and a person can see the mode.

### 3.3 Registration provisions into memory

`PUT localtest/api/apps/{org}/{app}` runs the same `LocaltestProvisioner` steps
([`localtest/README.md`, "What the app runner pushes"](../../localtest/README.md#what-the-app-runner-pushes)). Each
step's target is either an HTTP call or an in-process store, depending on whether the service is emulated:

| Step | Default stack | Minimal mode |
| --- | --- | --- |
| 1 Register test data | `PUT register/api/localtest/apps/{org}/{app}` | The in-memory test-data store rebuilds (seed plus every registered app's file), with the merge rules of [`testdata/README.md`](../../localtest/testdata/README.md) |
| 2 metadata and texts | Storage, Studio token | **Unchanged: real Storage.** The Studio token is minted in-process instead of exchanged |
| 3 policy | PDP policy store | Kept in memory per app (read by nothing in phase 1; read by the policy PDP if phase 3 is done) |
| 3b Resource Registry subjects | HTTP | Skipped |
| 4 Access Management refresh | HTTP | Skipped: authorized parties are computed from the store on each request |
| 5 Dialogporten sync | only with the group | Skipped (no group) |

So one interface with two implementations (HTTP and in-process) per step. The step list and the app source
(`RunningAppSource`) stay shared, and there is no second provisioning path. Deprovisioning removes the app's test
data and policy from memory.

**Test data.** The Register seed
([`components/register/testdata/testData.json`](../../localtest/components/register/testdata/testData.json), 8
persons, 13 organisations, 1 system, 30 KB) is copied into the localtest image at build time, as the Register image
does. The parser and merger (`TestDataFile.cs`, 322 lines; `TestDataMerger.cs`, 111 lines, in
[`components/register/fakes/…/TestData/`](../../localtest/components/register/fakes/Altinn.Register.Localtest/TestData/))
use only System.Text.Json. They are compiled into the localtest project as linked files, with their types added to
the source-generated context, so the file format has one implementation. The store on top of them is new: plain
records keyed by party id, user id, SSN and organisation number. The Register's `RegisterStore` builds Register.Core
`PartyRecord`s, which pull in Urn generators and MVC, so it is not reused. The front door's own `TestUser` record
([`IdPorten/TestUsers.cs:51-62`](../../localtest/localtest/IdPorten/TestUsers.cs)) and the Register fake's
`actingFor` list ([`LocaltestTestUsersController.cs`](../../localtest/components/register/fakes/Altinn.Register.Localtest/Controllers/LocaltestTestUsersController.cs))
are the shapes the store serves. `TestUsers` reads the store in-process instead of over HTTP.

**Sign-in.** Today Home drives production Authentication over HTTP: `goTo`, the upstream callback, and
`exchange/{provider}?test=true` ([`AuthenticationClient.cs:29-91`](../../localtest/localtest/Home/AuthenticationClient.cs)),
and `StudioToken` exchanges through `exchange/altinnstudio`. In minimal mode a `TokenMinter` builds the same claim
sets directly and signs them with the emulation's key:

- the user's `urn:altinn:userid`, `partyid`, `authlevel` and `pid`;
- the org's `urn:altinn:org` and `orgNumber`;
- Studio's `urn:altinn:app=studio.designer`, which Storage requires for application pushes
  ([`Program.cs:333-338`](../../localtest/components/storage/vendor/src/Storage/Program.cs)).

The claim sets come from the production Authentication component's `exchange` and the old emulation's
`AuthenticationService.GenerateTokenForProfile`/`ForOrg` (`OLD/Services/Authentication/Implementation/AuthenticationService.cs:99-203`).
The key can be the shared test certificate
([`certs/`](../../localtest/certs/README.md)), so one key signs both user and platform tokens.

### 3.4 Image size and start-up

- **Size.** The emulation is managed code in the existing AOT binary. 2,000 lines of minimal APIs and source-generated
  JSON add well under 2 MB; the static PDF is 602 bytes and the seed 30 KB. Estimate: under 40 MB, from 36.5 MB. Not
  measured.
- **Start.** The front door is healthy in 36 to 180 ms today (studioctl log, 2026-10-22). Parsing the 30 KB seed adds
  milliseconds.
- **The mode's start time** is set by Postgres, Storage and the workflow engine (section 6.2).

## 4. What "minimal" means per service

### 4.1 Register

It serves parties, persons, organisations and lookups from the test-data store, with the endpoints in 2.1 plus the
front door's own needs (the test-user list for the picker). It answers:

- party selection: the user's own party, the parties with roles, and their sub-units, as the old
  `authorization/partylist` defined it;
- instantiation: lookup by SSN and organisation number, the party list with sub-units, URN queries;
- the lookup components: `persons` with SSN and last name, and `organizations/{orgNr}`.

The old emulation answered 429 after two failed person lookups (`OLD/Services/Register/…/PersonLookupService.cs`);
production does too, so it is cheap to keep. The KRR stand-in (`register/api/localtest/krr`) is not needed: Profile's
sync is gone.

### 4.2 Profile

It serves `UserProfile` per user from the store: id, UUID, SSN, e-mail and mobile number from the test data, the
embedded `party` (app-lib's `Authenticated.LoadDetails` throws when the profile or its party is missing,
`CORE/Features/Auth/Authenticated.cs:298`), and `profileSettingPreference` (language, `preSelectedPartyId`, `doNotPromptForParty`) from the v2 fields the seed
already carries. No settings writes, no favourites, no contact points: the app does not call them, and the emulated
Notifications does not look addresses up.

### 4.3 Authentication

The emulation serves the following, under `authentication/api/v1/`:

- `openid/.well-known/openid-configuration` and its `jwks`;
- `authentication?goto=` (redirect to the picker) and `logout` (clear the cookie, back to the start page);
- `refresh`;
- `exchange/maskinporten`, verified against the in-image Maskinporten fake's key;
- `exchange/altinnstudio` and `exchange/id-porten`, for callers that still use the HTTP path.

Tokens are minted in-process (3.3). `/Home/GetTestOrgToken` matters more than it looks: app-lib's token resolver uses
it on localtest instead of the Maskinporten exchange (`CORE/Internal/Auth/AuthenticationTokenResolver.cs:77-81`). In the
monorepo backend every workflow-engine callback runs as the service owner through it
(`M/Internal/Auth/AuthenticationTokenResolver.cs:50-59`), so the emulated Register, Profile, Events and PDP accept
org tokens wherever they accept user tokens. `/Home/Localtest/Version` (already in the image) must keep answering at
least 6, the monorepo backend's minimum (`CORE/Internal/LocaltestValidation.cs`); the app stops at start-up otherwise. The ID-porten and Maskinporten fakes stay as they are. Production Authentication's
OIDC provider for seeded clients (the Arbeidsflate and Dialogporten clients, `components/authentication`) is not
emulated: it serves the optional groups only.

### 4.4 Authorization (permit-all)

- **`decision`** answers `Permit` for every request:
  - **A single request** gets exactly one result: `ValidatePdpDecision` refuses any other count.
  - **A multi-request** gets one result per `multiRequests.requestReference`. Each result carries back, as
    `category[]`, the attributes of the referenced categories that the request marks `includeInResult`, which is
    what the real PDP does.
  - **The callers depend on that echo:**
    - Storage maps results to instances by it (S3).
    - App-lib's `MultiDecisionHelper` reads the action id from it (`CORE/Helpers/MultiDecisionHelper.cs:55-77`) to fill
      the process state's actions.
    - The signing key-role check reads the echoed organisation number (`CL/Authorization/AuthorizationClient.cs:190-210`).
  - A bare `{"response":[{"decision":"Permit"}]}` breaks the app: a null `category` throws in
    `MultiDecisionHelper`, so the process state answers 500, and an empty one turns every action off (a read-only
    form).
  - It returns **no obligations**: `DecisionHelper.ValidatePdpDecision`/`ValidateDecisionResult` treat a `Permit`
    without an authentication-level obligation as allowed for any `urn:altinn:authlevel`.
- **`parties`** and **`parties/{id}/validate`** come from the store (2.1).
- **`roles`** (old `OLD/Controllers/Authorization/RolesController.cs`) is not called by app-lib or the monorepo
  backend and is not emulated.

**What "always permit" changes in the app:**

- The process state's `actions` come from `AuthorizeActions` over the task's BPMN actions, so every action of the
  current task is offered to every signed-in user. A user without the role sees the write, sign and confirm buttons.
- The frontend's read-only states, its "missing roles" and "no valid parties" pages, and 403s never appear.
- An app policy's minimum authentication level is not enforced, so sign-in at level 0 or 1 works where the policy asks
  for 2 or 3.

**The policy-evaluating PDP is the cheapest faithful alternative.** It would evaluate the app's own `policy.xml`
(already pushed at step 3) against the user's role codes and access packages from the test data, as
`OLD/Controllers/Authorization/DecisionController.cs:221-246` does. The XACML engine `Altinn.Authorization.ABAC`
(0.1.1 used by the old runtime, 0.3.0 current; 96 to 125 KB) parses with hand-written `XmlReader` code, with no
`XmlSerializer`, `Activator` or `Reflection.Emit` (from the DLL's strings). It is probably AOT-compatible, but that is
**not verified**: it carries no `IsAotCompatible` metadata, and its JSON-profile POCOs would need the source-generated
context. The context handler around it (role and party enrichment, about 430 lines in the old runtime) would be
rewritten on the store. This is phase 3, and only if users ask for role tests in the minimal mode; the default stack
already gives the real PDP.

### 4.5 Access Management

- `enduser/authorizedparties` (the monorepo backend's party list) is answered from the store in Access Management's
  DTO shape.
- The instance delegation and revoke endpoints record the request and echo a successful `DelegationResponse`, so
  app-lib's signing delegation completes.
- Nothing reads the delegations back: with permit-all there is nothing for them to grant.

### 4.6 Events

`POST events/api/v1/app` appends the cloud event, with an id and time, to a bounded in-memory list (the last 1,000)
and returns 200. `POST subscriptions` echoes. The inspection API keeps the Events component's paths
(`GET`/`DELETE events/api/localtest/events`), so a test written against either mode reads events the same way. There
is no delivery to subscribers, no webhooks and no SSE stream in phase 1.

### 4.7 Notifications

The emulation stores e-mail, SMS and `future/orders` orders in memory, answers 202 with an order id and the
response shape app-lib reads, and supports cancel. Recipients are resolved from the test data (person e-mail and
mobile number; organisations' notification addresses), so the stored message shows where it would have gone. The
inspection API keeps the component's paths (`notifications/api/localtest/inboxes`, `…/messages`, `DELETE`), bounded
as Events. Order status and shipment queries answer `Delivered` at once.

### 4.8 Resource Registry

No caller in this mode (2.2). The prefix answers 501 problem details naming the minimal mode, so a tool that needs it
learns why.

### 4.9 PDF (static file)

**Endpoint and request.** App-lib posts JSON to `PlatformSettings__ApiPdf2Endpoint`, which the runners set to
`http://pdf.local.altinn.cloud:8000/pdf`
([`studioctl app env.go:111`](../../localtest/studioctl/src/cli/internal/cmd/app/env.go)). The request is
`app-lib-dotnet:src/Altinn.App.Core/Infrastructure/Clients/Pdf/PdfGeneratorClient.cs:75-144`, with the body
`PdfGeneratorRequest` (`Models/Pdf/PdfGeneratorRequest.cs`):

```json
{ "url": "http://local.altinn.cloud:8000/ttd/app/#/instance/…?pdf=1", "setJavaScriptEnabled": true,
  "waitFor": "#readyForPrint",
  "options": { "headerTemplate": "<div/>", "footerTemplate": "<div/>", "displayHeaderFooter": false,
               "printBackground": true, "format": "A4", "margin": { "top": "0.75in", "…": "…" } },
  "cookies": [ { "name": "AltinnStudioRuntime", "value": "<user token>", "domain": "local.altinn.cloud", "sameSite": "Lax" } ] }
```

**Response.** The client checks only `IsSuccessStatusCode` (`:146`) and returns the body stream (`:157`). It reads no
header: `PdfService` stores the stream as a data element with its own content type `application/pdf`
(`CORE/Internal/Pdf/PdfService.cs:47,179,226`). The emulation answers `POST /pdf` on the `pdf.` host with 200,
`Content-Type: application/pdf` and the file. A `GET` on that host answers a short text page naming the mode, and
`/health/ready` answers 200 for tools that probe pdf3.

**The flows still work.** The flows that request a PDF all go through `PdfService`:

- the PDF service task at task end;
- the subform PDF task;
- the receipt's "download PDF";
- the preview;
- payment's receipt;
- signing, which signs the data elements of the task, including a generated PDF when the app has one.

Each needs only a successful response and bytes to store. Storage stores them as any blob, and the signing hash
(`SignController` in Storage, SHA-256 over the blob) is computed over whatever bytes are there. So the receipt lists
the PDF, the download opens a valid one-page PDF, and signing records a hash. **Lost:** the content. Every PDF is the
same page, so an app's PDF layout (`pdfLayoutName`, the `?pdf=1` view, footers) cannot be checked in this mode.

**The file** is a hand-written PDF 1.4: one A4 page, Helvetica 24 pt (a standard Type 1 font, so nothing embedded),
the text "Minimal PDF without content", five objects with a correct cross-reference table. It is **602 bytes**,
produced by a 20-line Python script without dependencies. A prototype in the research scratchpad parses in pypdf's
strict mode and extracts the text. Recommendation:

- Commit the file as `localtest/localtest/Emulation/Pdf/minimal.pdf`, with the script beside it for regeneration.
- Embed the file in the binary as an embedded resource, so there is no file at run time.
- Generate nothing at build time: no browser, no PDF library in the image.

## 5. Fidelity lost, and how the user is told

| Lost | Effect | Where the real thing is |
| --- | --- | --- |
| Authorization rules | Everything is permitted; no 403, no read-only state, no missing-role pages; all task actions shown; authentication levels not enforced | `default`: production PDP on the app's policy |
| Register validations and data | No CCR roles, access packages or Altinn 2 role codes behind decisions; no streams; party data is the test data as written | `default`: production Register |
| Profile | No settings writes, favourites, KRR sync, address verification | `default` |
| Authentication | No OIDC provider for seeded clients; no ID-porten upstream round trip through production code; claim sets are reconstructed, so a production change to them is not seen | `default` |
| Access Management | Delegations recorded, never enforced; no authorized-parties filtering by rights | `default` |
| Events | No subscriptions, delivery or retries | `default` |
| Notifications | No validation of orders beyond shape, no sending pipeline, no reservation check | `default` |
| PDF | Content: always the same page | `default`: pdf3 |
| Optional groups | Dialogporten, Arbeidsflate, Correspondence, Receipt are unavailable: their containers depend on the real Register, Authentication and others | `default --with …` |
| Persistence | Every stop is a clean slate (6.1) | `default` (named volumes) |

**How the user is told:**

- **On the Home page**, a banner: "Minimal mode: Register, Profile, Authentication, Authorization (permits
  everything), Access Management, Events, Notifications and PDF are emulated. Do not report platform bugs from this
  mode", with a link to this page's successor in the docs.
- **On every emulated response**, a header `X-Localtest-Emulated: <service>`, visible in the browser's network tab
  and in app logs of failed calls.
- **In `env up` and `env status`**: a mode line and an `emulated` status for those services (section 6).
- **In the docs**, a short page (`altinn-studio-docs`, localtest section) beside the stack description, with this
  table.

## 6. The runtime surface

### 6.1 Ephemeral storage

**What "ephemeral" needs.** Leaving the manifest's `volumes` unmounted is not enough:

- The Postgres image declares `VOLUME /var/lib/postgresql`, and the localtest image `VOLUME /data`
  ([`Dockerfile:26`](../../localtest/localtest/Dockerfile)). An unmounted declared volume becomes an **anonymous
  volume**.
- studioctl removes containers without removing their anonymous volumes: `podman rm` without `-v`
  (`devenv/pkg/container/podman/podman.go:767-771`), Docker `RemoveOptions` without `RemoveVolumes`
  (`dockerapi/docker_engine_api.go:856-858`).
- So anonymous volumes would pile up, one set per start.

The mode therefore mounts **tmpfs** at each path the manifest names a volume for, in the containers it starts:

| Container | Path | Default stack | Minimal mode |
| --- | --- | --- | --- |
| postgres | `/var/lib/postgresql` | `postgres-data` | tmpfs |
| storage | `/data/blobs` | `storage-blobs` | tmpfs |
| localtest | `/data` | `localtest-data` (app routes, dev-server route) | tmpfs, and the routes kept in memory (below) |

A tmpfs over a declared volume's path suppresses the anonymous volume, and it disappears with the container.

- **studioctl.** `devenv`'s `VolumeMountType` has `bind` and `volume` only
  (`studioctl/src/Runtime/devenv/pkg/container/types/types.go:228-229`). Add `tmpfs`: Podman `--tmpfs <path>`, Docker
  `HostConfig.Tmpfs`. In minimal mode `containerVolumes`
  ([`containers.go:265-296`](../../localtest/studioctl/src/cli/internal/cmd/env/localtestv2/containers.go)) emits a
  tmpfs mount for each manifest volume of the container instead of the named volume. About 40 lines with tests.
- **Aspire.** `container.WithContainerRuntimeArgs("--tmpfs", path)` (already used for `--add-host`,
  [`LocaltestStack.cs:175`](../../localtest/aspire/Altinn.App.Aspire/src/LocaltestStack.cs)) in place of
  `WithVolume`. The package creates persistent containers (`<name>-<hash>`), so minimal mode uses
  `ContainerLifetime.Session` for its four. Otherwise a persistent container would keep its tmpfs across AppHost
  restarts while running, and the clean slate would depend on whether the container was replaced.
- **The manifest** does not change. Ephemeral is runner code that applies to every volume of the mode, like the
  `services__` injection; the golden snapshot gets a `minimal` case that shows the tmpfs mounts.
- **Size.** tmpfs is memory. Storage's blobs are the app's uploads (the front door allows 200 MB per request). A size
  option on the mount (`--tmpfs /data/blobs:size=1g`) bounds a runaway test. Postgres on tmpfs skips `fsync` to disk,
  which makes `initdb` and the migrations faster, not slower.

**The front door's `localtest-data`.** It holds `app-routes.json` and `frontend-dev-server.json`
([`Program.cs:40-44`](../../localtest/localtest/Program.cs)). In minimal mode the registrations are in memory: with
`Proxy__AppRoutesFile` empty, `AppRoutes` and `DevServerRoute` neither load nor save, and the tmpfs on `/data` only
prevents the anonymous volume. The registered app's test data, policy and route live and die together with the
emulated stores, which is consistent. A front-door restart without a re-registration loses the app route. studioctl's
`run` registers on start, and studioctl-server (task 32, proven 2026-10-23) asks the front door's
`GET localtest/api/apps` on every refresh and registers again any running app it no longer lists, within about 5 s;
so a front-door restart in this mode re-registers the running apps without anything new. Aspire registers on the
app's ready event only; a front-door restart under Aspire needs the dashboard's `provision` command or an app restart
(open question 4).

**`env reset` in this mode** has nothing to remove: no named volumes. It stops the mode, as `env down` does, and says
so ("minimal mode keeps no data; nothing to reset"). It does not touch the `localtest-v2-*` volumes of the default
stack: switching from `default` to `minimal` and back finds the default stack's data intact, because minimal never
mounts it.

### 6.2 Start-up time

Measured on this machine with studioctl on 2026-10-22, every image already pulled. The times are from the
`healthy after` lines; each container is measured from its own start:

| Container | Existing volumes | Empty volumes |
| --- | --- | --- |
| postgres | 0.33 s | 3.21 s (initdb plus nine init scripts) |
| storage (Yuniql, migrations v0.00 to v0.38) | 1.26 s | 2.67 s |
| workflow-engine (EF, 9 migrations) | 1.57 s | 2.75 s |
| localtest | 0.04 to 0.18 s | same |
| the default stack, `env up` to healthy | about 20 s | 21.6 s ([`localtest/README.md`](../../localtest/README.md)); Access Management alone 10.4 s |

In minimal mode every start is an empty-volume start. Storage and the workflow engine wait for Postgres and start in
parallel, so the critical path is **Postgres 3.2 s, then the slower of Storage and the workflow engine, 2.8 s: about
6 s to healthy on studioctl**, plus the front door's health check and the pulls. Under Aspire on empty volumes, the
2026-10-22 measurement with all 18 containers ([Aspire README](../../localtest/aspire/Altinn.App.Aspire/README.md))
had Postgres at 6 s and Storage and the workflow engine at 9 and 10 s, measured from the AppHost's start. Expect
about 10 s there, against 32 s for the default stack. On tmpfs it should be faster. **These are estimates from
per-container times measured beside other containers**; phase 1 measures the mode itself.

The workflow engine's `tt02` tag is pulled again on every start (its digest check), as today
([`CONVENTIONS.md`, "Tags"](../../localtest/CONVENTIONS.md#tags)). pdf3's pull is gone from the mode.

**A Postgres image with the databases pre-initialised is not worth it.** At most it saves the initdb and the two
migration runs, about 2 to 4 s of a 6 to 10 s start. Against that:

- **Schema drift.** The baked schema is tied to one Storage pin and one workflow-engine build, and the engine's `tt02`
  floats, so it migrates at start anyway. A stale bake is a new failure mode that a fresh database never has.
- **The data path.** The bake must live outside the declared `VOLUME` path (Docker discards writes to it during a
  build), so the image needs `PGDATA` elsewhere and a copy into the tmpfs at start, which costs back part of the gain.

Revisit it if the measured start exceeds 10 s on studioctl. A cheaper step first: a minimal start runs only init
scripts 10 (Storage) and 20 (workflow engine). Postgres runs every script in `/docker-entrypoint-initdb.d`, so this
needs an environment switch in the scripts that the runners set, or nothing: the seven extra `CREATE DATABASE` cost
well under a second.

### 6.3 Selecting the mode

**The manifest.** One new group entry, nothing else:

```json
{ "name": "minimal", "containers": ["localtest", "storage", "workflow-engine"] }
```

`postgres` comes in through `dependsOn`. The runners treat `minimal` as a constant name, as they treat `default` and
`everything`. It is the one group that **replaces** `default`, and with it:

- **The emulated set is derived.** It is every HTTP container of `default` that `minimal` does not start: register,
  profile, authentication, authorization, access-management, events, resource-registry, notifications and pdf3. The
  runners inject `services__<each>__http__0=http://localtest:8080`, beside the usual entries for the four running
  containers.
- **No new key.** The owner's "a list of images" holds. If a later image joins `default` without a front-door
  emulation, the front door's start-up check (1.2) names it, instead of looping.

**studioctl.**

- `studioctl env up -r localtest-v2 --minimal`. It is exclusive with `--with` (refused: the optional groups' closures
  reach the emulated services) and refused with a `--dev-<container>` for an emulated container. `--dev-storage` and
  `--dev-workflow-engine` work.
- The code changes: `selected()` ([`containers.go:97-99`](../../localtest/studioctl/src/cli/internal/cmd/env/localtestv2/containers.go))
  takes `minimal` instead of `default`; `serviceVariables` (`:149-158`) adds the emulated addresses; `containerVolumes`
  mounts tmpfs.
- `State` (`localtestv2/state.go`) records the mode, and `GroupsChanged` treats a mode switch as a change: down, then
  up.
- `studioctl run` against minimal leaves the Arbeidsflate links unset.

**`env status`:**

```
localtest-v2 is running (minimal mode)
Container        Status    Image
postgres         running   localtest-postgres (sha-…)
storage          running   localtest-storage (sha-…)
workflow-engine  running   runtime-workflow-engine-app (tt02)
localtest        running   localtest-localtest (sha-…)
register, profile, authentication, authorization, access-management,
events, resource-registry, notifications, pdf3: emulated in localtest
```

**Aspire.** *Owner's decision of 2026-10-23, built by task 37:* `builder.AddAltinnLocaltestMinimal()` on a resource
type of its own, with no method that enables another service and no `reset` command
([package README](../../localtest/aspire/Altinn.App.Aspire/README.md#minimal-mode)); what follows is the design it
replaced. `AddAltinnLocaltest(o => o.Minimal = true)` or `.WithMinimal()`, beside `WithGroup`
([`AltinnLocaltestExtensions.cs:141`](../../localtest/aspire/Altinn.App.Aspire/src/AltinnLocaltestExtensions.cs)).
It is exclusive with `With…()` (an exception at build time), and its parts are:

- `StackSpec.Resolve` ([`StackSpec.cs:138-153`](../../localtest/aspire/Altinn.App.Aspire/src/Manifest/StackSpec.cs))
  selects `minimal` and adds the emulated addresses;
- `LocaltestStack` mounts tmpfs and uses session lifetime;
- the `reset` command reports "nothing to reset" in this mode.

The parity golden gets the `minimal` case.

**Apps' tests without studioctl.** Four containers no longer fit one `docker run`. The shortest form is a network and
four `run`s with the same names, environment and mounts as the golden snapshot. As a sketch only, with the tags and
the full environment taken from the golden file:

```sh
docker network create lt
docker run -d --rm --network lt --name postgres --tmpfs /var/lib/postgresql -e POSTGRES_PASSWORD=postgres -e TZ=UTC ghcr.io/ivarne-agent/localtest-research/localtest-postgres:<tag>
docker run -d --rm --network lt --name storage --tmpfs /data/blobs -v ./certs:/certs:ro --add-host local.altinn.cloud:host-gateway <storage envs from the manifest> …/localtest-storage:<tag>
docker run -d --rm --network lt --name workflow-engine <envs> ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:tt02
docker run -d --rm --network lt --name localtest -p 8000:8080 --tmpfs /data -v ./certs:/certs:ro -e services__register__http__0=http://localtest:8080 … …/localtest-localtest:<tag>
```

That is what `studioctl env up --minimal` does, and a CI job would rather call studioctl or Aspire. The
recommendation: add `studioctl env up --minimal --print docker` (the resolved commands from the golden) instead of
documenting the list by hand, so it never drifts.

## 7. Reuse versus rewrite, and the phased plan

**Rewrite** in minimal-API style. The monorepo emulation (`OLD`, 32,000 lines) is MVC with Newtonsoft (17 files),
`JWTCookieAuthentication`, the PEP package's policy handlers, FluentValidation and file storage. The services in
scope here are its small part (Register 768 lines, Profile 133, Authentication 1,746, Authorization 2,104 with the
XACML context handler, Access Management 354, Events 761, Notifications 4,115 with a copied production core, Resource
Registry 1,007). Its route list and response shapes are the reference; its code is not carried over. The component
fakes under `localtest/components/*/fakes` are DI replacements around production pipelines and do not lift, except
the two test-data files (3.3). The inspection API paths of Events and Notifications are copied so that tools work in
both modes.

**Estimate**, application code without tests:

| Part | Lines |
| --- | --- |
| Mode, self-loop check, routes listing, banner, header | 200 |
| Test-data store (on the linked `TestDataFile`/`TestDataMerger`) | 350 |
| Register endpoints | 300 |
| Profile | 100 |
| Authentication: discovery, JWKS, token minter, refresh, exchanges, goto, logout; Home and provisioning switched in-process | 400 |
| Authorization: permit-all with echoed multi-requests, parties, validate | 200 |
| Access Management | 100 |
| Events | 120 |
| Notifications | 200 |
| Resource Registry 501, PDF | 60 |
| **Total** | **about 2,000** |
| Tests (in-process, the existing `FrontDoor` fixture) | about 800 |
| studioctl (mode, tmpfs mount type, status, golden case) | about 300 with tests |
| Aspire (option, tmpfs, lifetime, golden comparison) | about 200 with tests |

### Phases

| Phase | Scope | Gate (smoke) |
| --- | --- | --- |
| **1. Basic flow** | Everything in sections 3 and 4 except the policy PDP; studioctl `--minimal` with tmpfs; the static PDF | Root [`smoke-test.sh`](../../localtest/smoke-test.sh): the sign-in, token, push, instance, data and process checks (about 20 of 26); the PDP's NotApplicable and the two 403 checks are skipped in minimal mode. [`localtest/smoke-test.sh`](../../localtest/localtest/smoke-test.sh): routing, registration, ID-porten, Maskinporten, Home (its dummy upstream `register:5020` moves to `storage:5010`). The Register suite's read checks against the emulated paths. The Cypress subset of an app-lib 8 app and a monorepo-backend app through instantiate, fill in, sign, submit, receipt. The measured start time |
| **2. Aspire and docs** | `AddAltinnLocaltestMinimal()` (was `WithMinimal()`), session lifetime, the parity golden case, the docs page | The same suites against the Aspire stack |
| **3. Optional, on demand** | The policy-evaluating PDP (ABAC under AOT, verified first); Events delivery to subscribers; Notifications status flow | The root suite's 403 and NotApplicable checks; the Authorization suite's decision checks |

**Suites that run against minimal mode:**

- in part: the root, `localtest`, Register (reads), Events (publish and inspection) and Notifications (order and
  capture inbox);
- unchanged: `workflow-engine`, which is standalone.

**Suites that do not:**

- Profile: migrations, sync and settings;
- Authentication: the OIDC provider;
- Authorization: real decisions;
- the Resource Registry;
- every optional group's suite;
- any check that reads a container's log or runs `psql`. Postgres and Storage are there, but the checks are written
  for the default stack.

## 8. Risks and open questions for the owner

| # | Question or risk | Recommendation |
| --- | --- | --- |
| 1 | Permit-all hides authorization bugs in apps, and users may mistake the mode for proof | The banner and header (5); the docs say "authorization is not tested in minimal mode"; the policy PDP stays phase 3, on demand |
| 2 | Drift: production adds a call or changes a response shape, and the emulation lags | The phase-1 gate runs an app-lib 8 and a monorepo app in CI against both modes; the emulation answers 501 with the path for any unknown route under an emulated prefix, so drift shows as a named failure |
| 3 | Claim sets reconstructed in the token minter differ from production Authentication's | Take them from the vendored production `exchange` code and test them against the default stack's tokens (same claim types) |
| 4 | Re-registration after a front-door restart (routes now in memory) | studioctl: already done by task 32 (the server re-registers what the front door no longer lists). Aspire: register again when the localtest container's health goes from unhealthy to healthy, or document the dashboard's `provision` command |
| 5 | Ephemeral Postgres pays initdb and migrations on every start | Accept (estimated 6 to 10 s); measure in phase 1; pre-initialised image only above 10 s (6.2) |
| 6 | tmpfs uses memory; large uploads in tests | A size option on the blob tmpfs (1 GB); documented |
| 7 | Optional groups unavailable in minimal mode | Accept for phase 1; `--with` refused with a message pointing at the default stack |
| 8 | Should `minimal` become the default for `studioctl run` in a new app? | No: the owner keeps the real components as the default for fidelity; offer it in the `run` hint when the stack is not up |
| 9 | Native AOT and `Microsoft.Extensions.ServiceDiscovery` in the image were not verified in task 26 | Phase 1 builds and runs the AOT image with the emulation before anything else |
| 10 | The brief asked for one container; the owner's decisions make it four | Recorded at the top; the single-container variant would need Storage and the workflow engine in memory, which the owner ruled out |
| 11 | Signing receipts and call-to-action by Correspondence fail (503) in minimal mode | Phase 3 candidate: accept and record correspondences in memory as Notifications does (about 200 lines); until then documented as a gap |
