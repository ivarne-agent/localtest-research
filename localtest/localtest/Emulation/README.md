# Minimal mode: the platform emulated in the front door

The localtest container answers for the platform services itself, in memory, so that `studioctl env up -r
localtest-v2 --minimal` runs four containers (`postgres`, `storage`, `workflow-engine`, `localtest`) instead of the
default group's thirteen. Storage and the workflow engine are the real images. The design, its reasons and what is lost
are in the spec, [`planned-components/minimal-emulation/README.md`](../../../planned-components/minimal-emulation/README.md);
this page is the code's side: what this folder holds, how it plugs into the rest of the image, and the points the spec
left open. Task 35 built it (phase 1) and proved it on the stack (phase 2, [Verified](#verified-2026-10-23-task-35-phase-2)).

**The owner's rule: this folder is separate, and nothing else depends on it.** With no `services__<name>__http__0`
equal to the front door's own address, [`Emulation.Add`](Emulation.cs) registers nothing and `Emulation.Map` maps
nothing, and the default stack runs exactly as before (`tests/Emulation/SeamTests.cs` checks it; the other test classes
run unchanged).

## How the mode switches on

- **A service is emulated when its address is the front door's own** (the spec's option C):
  `services__<name>__http__0` equal to `Localtest__SelfAddress`, default `http://localtest:8080`, compared by scheme,
  host and port ([`EmulationMode.cs`](EmulationMode.cs)). studioctl's `--minimal` sets it for every HTTP container of
  `default` that `minimal` does not start: `register`, `profile`, `authentication`, `authorization`,
  `access-management`, `events`, `resource-registry`, `notifications`, `pdf3`.
- **No entry** is still 503 "not running"; **a foreign address** is still forwarded. Only the services the front door
  routes to count: its own entry, `services__localtest__http__0=http://localtest:8080`, which every runtime sets in
  both modes, is not a service it could forward to itself.
- **A self-address for a service this folder cannot emulate** stops the start with an error naming it, instead of a
  forwarding loop.
- **The mode is read from the builder's configuration** (the container's environment), unlike the rest of the app,
  which reads the built configuration. `Add` must know it to register the services. A self-address that only the built
  configuration has (a test host's `ConfigureAppConfiguration`) makes `Map` refuse to start, rather than loop.
  Tests pass the settings with `UseSetting` ([`tests/Emulation/MinimalFrontDoor.cs`](../tests/Emulation/MinimalFrontDoor.cs)).

`GET localtest/api/routes` reports `emulated` as the address of an emulated service. Every emulated answer carries
`X-Localtest-Emulated: <service>`, and the start page shows the spec's banner: "Minimal mode: Register, Profile,
Authentication, Authorization (permits everything), Access Management, Events, Notifications and PDF are emulated. Do
not report platform bugs from this mode", with a link here.

With telemetry on (`OTEL_EXPORTER_OTLP_ENDPOINT` set), each emulated answer is also a span named after its component
(`Register GET /register/api/v1/parties/{partyId:int}`, `peer.service` `register`), so a minimal-mode trace still shows
Register and Authorization ([`../README.md`](../README.md#telemetry)).

## What is emulated

Each service maps minimal APIs under its public prefix, ahead of the forwarder. Any other path under an emulated prefix
answers `501` problem details naming the path and the mode (spec risk 2), so a call the emulation lags behind fails by
name.

| Service | Folder (lines) | Endpoints | Notes |
| --- | --- | --- | --- |
| Register | [`Register/`](Register/) (370) | `v1/parties/{id}`, `parties/byuuid/{uuid}`, `POST parties/lookup`, `POST parties/partylist?fetchSubUnits=`, `POST apps/parties/query`, `POST access-management/parties/query`, `persons` (`X-Ai-NationalIdentityNumber`, `X-Ai-LastName`), `organizations/{orgNr}`; the Register fake's `localtest/test-users` and `localtest/organizations` | v1 models as app-lib reads them (camelCase, `partyTypeName` a number). Persons: a failed lookup counts per user (the token's `urn:altinn:userid`) for an hour; the third after two failures answers `429`, as production's `PersonLookupSettings`. The last name matches case-insensitively and exactly (production allows a small edit distance) |
| Profile | [`Profile/`](Profile/) (64) | `v1/users/{id}`, `POST v1/users` (the number as a JSON string), `v1/users/byuuid/{uuid}` | `UserProfile` with the embedded party and `profileSettingPreference` from the test data; `userType` 1 or 2 |
| Authentication | [`Authentication/`](Authentication/) (627) | `v1/openid/.well-known/openid-configuration` and its `/jwks`, `v1/authentication?goTo=`, `v1/upstream/callback`, `v1/logout`, `v1/refresh`, `v1/exchange/{maskinporten,altinnstudio,id-porten}` | See [Tokens](#tokens) |
| Authorization | [`Authorization/`](Authorization/) (245) | `POST v1/decision`, `v1/parties?userId=`, `v1/parties/{id}/validate?userId=`, `POST v1/policies?org=&app=`; the fake's `localtest/decision` | Permit for every request: one result for a single request, one per `multiRequests.requestReference`, each echoing as `category` the referenced categories' `includeInResult` attributes (XML Schema data types, as the real PDP answers). No obligations. Request property names are read case-insensitively (the PEP writes camelCase, the fake's helper PascalCase) |
| Access Management | [`AccessManagement/`](AccessManagement/) (155) | `v1/enduser/authorizedparties` (the signed-in user from the token), `POST v1/app/delegations/resource/{r}/instance/{i}`, `POST v1/app/delegationrevoke/…`; `localtest/refresh` (no-op) | Own party (role codes, `PRIV` when none), then the parties held for with their sub-units. A party the test data grants with an empty role list gets the resource `urn:altinn:resource:localtest-acting-for`, because the monorepo backend drops a party without a role, package or resource. Delegations are echoed with `status` `Delegated` or `Revoked` and kept (the last 1,000), never read |
| Events | [`Events/`](Events/) (233) | `POST v1/app` (answers the event id as text), `POST v1/subscriptions` (`201`, echoed with an id); `GET`/`DELETE localtest/events`, `localtest/events/{id}`, `localtest/subscriptions`, `localtest/stream` | The last 1,000 events. The inspection shape is the Events component's (`items`, `next`; per event `sequenceNo`, `id`, `type`, `source`, `org`, `app`, `instance`, `status`, `cloudEvent`). The stream only keeps the connection alive in phase 1; nothing is delivered |
| Notifications | [`Notifications/`](Notifications/) (429) | `POST v1/orders/email`, `orders/sms` (`202`, `orderId`), `POST v1/future/orders` (`201`, `notificationOrderId`, `notification.shipmentId`, `reminders`), `PUT v1/orders/{id}/cancel`, `v1/orders/{id}`, `orders/{id}/status`, `orders/{id}/notifications/{email,sms}`, `future/shipment/{id}`; `localtest/inboxes`, `localtest/inboxes/{address}`, `GET`/`DELETE localtest/messages` | Recipients from the test data: a person's e-mail and mobile number, an organisation's notification addresses or else its main unit's; `channelSchema` as a number or a name. Everything is `Delivered` (`Completed`) at once, a cancelled order `Cancelled`. The last 1,000 messages, in the component's inspection shape |
| Resource Registry | [`ResourceRegistry/`](ResourceRegistry/) (19) | none | Every path `501` "not emulated in minimal mode": nothing in the mode calls it |
| pdf3 | [`Pdf/`](Pdf/) (61) | the `pdf.local.altinn.cloud` host: `POST /pdf`; `GET /`, `/pdf` (a text page naming the mode); `/health/ready`, `/health/live` | Every PDF is [`minimal.pdf`](Pdf/minimal.pdf): PDF 1.4, one A4 page, "Minimal PDF without content" in Helvetica, 602 bytes, embedded in the binary. [`make-minimal-pdf.py`](Pdf/make-minimal-pdf.py) regenerates it byte for byte (`python3 -I make-minimal-pdf.py`). Other paths `404`, as pdf3 (so `/health` on that host is `404`, not the front door's) |

The rest: [`Emulation.cs`](Emulation.cs) (172, `Add`, `Map`, the header, the PDF host, the catch-all, the banner),
[`EmulationMode.cs`](EmulationMode.cs) (76), [`EmulationResults.cs`](EmulationResults.cs) (64, problem details and
bodies), [`EmulationJsonContext.cs`](EmulationJsonContext.cs) (26, source-generated JSON for the DTOs; the dynamic
shapes, such as the PDP's echo, are written with `JsonNode`), [`InProcessProvisioning.cs`](InProcessProvisioning.cs)
(66) and [`TestData/TestDataStore.cs`](TestData/TestDataStore.cs) (348). About 2,950 lines of application code without
the two copied files (the spec estimated 2,000), and 960 lines of tests in
[`../tests/Emulation/`](../tests/Emulation/).

## Tokens

[`TokenMinter`](Authentication/TokenMinter.cs) signs every token with the shared test certificate
(`Localtest__AccessTokenCertificatePath`, `/certs/localtest-accesstoken.pfx`), the key the discovery document's
`jwks` publishes: user, org, Studio and platform tokens alike, as in the default stack. Storage validates the user and
org tokens against that JWKS and its platform access tokens against the same certificate. The issuer is production's,
`http://local.altinn.cloud:8000/authentication/api/v1/openid/`, with a 30-minute lifetime (production's
`JwtValidityMinutes`). The claim sets are production Authentication's, from the vendored code
(`components/authentication/vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/`):

| Token | Claims | Production |
| --- | --- | --- |
| User (sign-in) | `sub` (`urn:altinn:party:uuid:…`), `sid`, `iss`, `urn:altinn:party:uuid`, `jti`, `urn:altinn:partyid`, `urn:altinn:userid`, `urn:altinn:username` (with a user name), `originaliss` (`idporten`), `email` and `urn:altinn:party:external-identifier` (self-identified), `acr`, `actual_iss` (`localtest`), `urn:altinn:authlevel`, `amr` (`["TestID"]`), `urn:altinn:authenticatemethod` (`IdportenTestId`), `auth_time`, `scope` (the portal scopes), `nbf`, `exp`, `iat` | `Helpers/ClaimsPrincipalBuilder.cs:169-280` with `isAuthCookie`; `actual_iss` is the provider claim localtest configures (`fakes/LocaltestAuthenticationExtensions.cs:81`); `Controllers/AuthenticationController.cs:908-934` the lifetime |
| Org (`exchange/maskinporten`) | the Maskinporten token's claims less `aud`, `iss`, `client_amr`, `sid`; `urn:altinn:orgNumber`, `urn:altinn:authenticatemethod` (`maskinporten`), `urn:altinn:authlevel` 3, `urn:altinn:org` for an `altinn:serviceowner` scope, `iss`, `sid`, the lifetime | `AuthenticationController.cs:430-532, 825-833` |
| Studio (`exchange/altinnstudio`) | the presented token's claims unchanged (`iss` `studio`, `urn:altinn:app` `studio.designer`, its lifetime) | `AuthenticationController.cs:374-417` |
| `exchange/id-porten` | `nameid`, `urn:altinn:userid`, `urn:altinn:username`, `urn:altinn:partyid`, `urn:altinn:party:uuid`, method and level, the ID-porten token's claims less `aud`, `iss`, `at_hash`, `sid`, `sub`; `iss`, `sid` | `AuthenticationController.cs:726-768`; needs an `altinn:` scope (`:559-565`) |
| `refresh` | the presented token's claims with a new lifetime | `AuthenticationController.cs:258-308` (production's JWT handler also renames `sub` to `nameid` on the way; the emulation keeps the claims as they were) |

`tests/Emulation/AuthenticationTests.cs` holds the tables of claim types with these references and checks the minted
user, self-identified, org and Studio tokens against them: when production's claim set changes, the table and the
minter change together.

**Sign-in.** A browser signs in as against production: `authentication?goTo=` redirects to the fake ID-porten's
picker (`localtest/api/idporten/authorize`) with state, nonce and an S256 PKCE challenge, the picker redirects to
`upstream/callback`, and the callback redeems the code in process (with the PKCE check), sets production's four cookies
(`AltinnPartyId`, `AltinnPartyUuid` with the preselected party from the test data when there is one,
`AltinnStudioRuntime`, `altinnsession`; `domain=local.altinn.cloud`, no `Secure` on the stack's plain http) and
redirects to `goTo`. A `goTo` off the platform host goes to the start page. Home's `/Home/*` and the provisioning's
Studio token go through the same code in process ([seams](#the-seams)). The org code for the exchange's `urn:altinn:org`
comes from a table of the test organisations (`991825827` is `ttd` with `test=true`, else `digdir`; `405003309` is
`ttd`), where production reads the CDN's list. The exchanges answer the raw JWT (app-lib's Maskinporten client parses
the body as is).

## Test data

The store ([`TestData/TestDataStore.cs`](TestData/TestDataStore.cs)) is built from the seed plus every registered
app's `testData.json`, with the merge rules of [`localtest/testdata/README.md`](../../testdata/README.md): when every
registered app has test data only the apps' data is loaded, otherwise the apps' data is added to the seed. Registration
rebuilds it; deprovisioning removes the app's contribution. Persons and organisations whose identifiers fail the
mod-11 checks are skipped with a warning, as the Register does. The emulated Register, Profile, Authorization, Access
Management, Notifications, the picker and Home read it on every request.

**The format's files are copies.** `TestData/TestDataFile.cs`, `TestData/TestDataMerger.cs` and
`TestData/testData.json` are byte-identical copies of the Register component's
`components/register/fakes/Altinn.Register.Localtest/TestData/TestDataFile.cs`, `…/TestDataMerger.cs` and
`components/register/testdata/testData.json`. The image's build context is `localtest/localtest/` only, so the
spec's linked files would not reach the image build, and widening the context is more blast radius than two copies.
`tests/Emulation/TestDataTests.cs` reads the copies and their originals from the repository and fails when they differ,
saying which to update. **Change the original, then copy it over the copy**
(`cp localtest/components/register/fakes/Altinn.Register.Localtest/TestData/TestDataFile.cs localtest/localtest/Emulation/TestData/`, and so on);
never edit a copy alone. The seed is embedded in the binary (its `$schema` path is the original's, which the loader
ignores).

For the copy to compile under Native AOT, task 35 moved the originals from the reflection serializer to a
source-generated context (`TestDataJsonContext`, in `TestDataFile.cs`) with the same options. A test checks that the
canonical JSON the merger compares is what the reflection serializer wrote, for every entry of the seed.

## The seams

Outside this folder only these change, each small and each working with this folder gone:

| Seam | Where | What |
| --- | --- | --- |
| The two calls | [`Program.cs`](../Program.cs) | `Emulation.Add(builder)` after the registrations; `Emulation.Map(app)` right after `Build()`, before the dedicated-host middleware (so the PDF host is answered here) and so before the fallback forwarder |
| Provisioning target | [`Provisioning/IProvisioningTarget.cs`](../Provisioning/IProvisioningTarget.cs), `LocaltestProvisioner.cs`, `AppProvisioning.cs` | Steps 1, 3, 3b, 4 and the removals go through `IProvisioningTarget`. `HttpProvisioningTarget` is the HTTP code moved out of `LocaltestProvisioner` unchanged; `AppProvisioning` takes an optional in-process target, which this folder registers ([`InProcessProvisioning.cs`](InProcessProvisioning.cs)). **One interface, not one per step:** the step list stays in one place, and which services are emulated is the in-process target's decision per call (a step whose service is not emulated goes over HTTP). Storage's steps stay HTTP in both modes. The Studio token is the next seam's |
| Test users | [`IdPorten/TestUsers.cs`](../IdPorten/TestUsers.cs) | `ITestUserSource`: `TestUsers` (the Register over HTTP, unchanged) asks an in-process source when one is registered ([`InProcessTestUsers`](Register/RegisterEmulation.cs)) |
| Tokens | [`Home/AuthenticationClient.cs`](../Home/AuthenticationClient.cs) | `IInProcessAuthentication`: `AuthenticationClient.SignIn` and `Exchange` (production over HTTP, unchanged) go to an in-process implementation when one is registered ([`InProcessAuthentication`](Authentication/AuthenticationEmulation.cs)). Home's sign-in, `/Home/GetTestOrgToken` (the fake Maskinporten's token exchanged) and the Studio token (`StudioToken` signs Designer's token with the certificate and exchanges it) use it without change. The pages and the `/Home/*` contract do not change |
| Banner | [`Home/HomeApi.cs`](../Home/HomeApi.cs) | `HomeBanner`: the start page shows it when one is registered |
| Routes | [`Proxy/RoutesApi.cs`](../Proxy/RoutesApi.cs), [`Proxy/AppRoutes.cs`](../Proxy/AppRoutes.cs) | `IEmulatedServices`: `GET localtest/api/routes` reports `emulated`. An empty `Proxy__AppRoutesFile` (studioctl sets it in the mode) keeps the app routes in memory |
| Project | [`Localtest.csproj`](../Localtest.csproj) | The seed and the PDF as embedded resources, kept out of the published content |

The in-process implementations are passed as optional constructor parameters of the classes the rest of the image
already registers (`TestUsers`, `AuthenticationClient`, `AppProvisioning`), so `Program.cs` keeps its registrations.

## Decisions where the spec left room

- **Mode at start, per service.** The emulation registers its stores and in-process seams once, for the services that
  are emulated; phase 1 tests the full set only. With a partial set (a real Register beside an emulated PDP) the
  in-process sign-in still reads this store, not the real Register: that combination is not supported yet.
- **The routes file.** With `Proxy__AppRoutesFile=` the app routes are in memory. The frontend dev server's route file
  is derived from it in `Program.cs` and falls back to `/data/frontend-dev-server.json`, which is a tmpfs in the mode, so
  it dies with the container as well; changing that would mean a third line in `Program.cs`.
- **The smoke gates.** `localtest/smoke-test.sh` reads the mode from `GET localtest/api/routes` (or
  `LOCALTEST_MINIMAL=1`) and skips three checks by name: the PDP's `NotApplicable`, the instance refused by the PDP
  and the upload refused for want of a role. `localtest/localtest/smoke-test.sh` uses Storage (`storage:5010`) as its
  dummy app and its file-like path, which runs in both modes, and adds one check per emulated service in the mode.
  `components/register/smoke-test.sh` runs its six read checks against the emulation (the seeded users, reserved flags,
  role codes and acting-for, organisations' addresses, the two party lists) and skips the KRR stand-in and the
  registration API.
- **Bounds.** Events, Notifications' messages and Access Management's delegations keep the last 1,000 each.

## Adding a service

1. Add its name to `EmulationMode.Emulatable` (the start-up check then accepts its self-address).
2. A folder with a static `Map(WebApplication app, string prefix)` mapping minimal APIs under the prefix; call it from
   `Emulation.Map`'s switch. The catch-all `501` comes with it.
3. DTOs in `EmulationJsonContext`; answer with `Results.Json(value, EmulationJsonContext.Default.X)` or `JsonNode`;
   read bodies with `EmulationJson.Read`. `dotnet build` runs the AOT and trim analysers with warnings as errors.
4. State in a singleton registered in `Emulation.Add`; data from `TestDataStore.Current`.
5. Tests in `tests/Emulation/` with `MinimalFrontDoor`; a check in `localtest/localtest/smoke-test.sh`'s minimal section;
   the table above, and the spec's section 5 if fidelity is lost.

## Verified (2026-10-23, task 35 phase 2)

On this machine (arm64, Podman), studioctl from this branch, the pinned `sha-6d928ad` images, the front door built from
this folder (`--dev-localtest=localtest/localtest`):

- **`studioctl env up -r localtest-v2 --minimal --dev-localtest=localtest/localtest`**: four containers, healthy in
  65.5 s on the first run (the AOT image build and a re-pull of the workflow engine's `tt02`), 5.9 and 6.1 s with the
  image cached, 36.7 s once when the `tt02` digest check was slow. No volume is created (tmpfs everywhere). `env status`:

  ```
  localtest-v2 is running (minimal mode)

  Container                     Status   Image
  localtest-v2-localtest        running  04f314f5f547
  localtest-v2-postgres         running  ghcr.io/ivarne-agent/localtest-research/localtest-postgres:sha-6d928ad (86ac6f312885)
  localtest-v2-storage          running  ghcr.io/ivarne-agent/localtest-research/localtest-storage:sha-6d928ad (797099538a61)
  localtest-v2-workflow-engine  running  ghcr.io/altinn/altinn-studio/runtime-workflow-engine-app:tt02 (ada436f9b197)
  register, profile, authentication, authorization, access-management,
  events, resource-registry, notifications, pdf3: emulated in localtest
  ```

- **`studioctl run`** of `localtest/aspire/samples/app/App` (`ttd/testapp`): registered and provisioned (metadata and
  texts in Storage, test data and policy in memory), answering through 8000. With the app's `testData.json` only its two
  users load (the apps-only rule).
- **Suites**: the root `smoke-test.sh` 23 ok, the 3 decision checks skipped by name (the app stopped first, so the seed's
  users are back); `localtest/smoke-test.sh` 89 of 90 (the app-frontend bundle needs `FRONTEND_DIST`, as in the default
  stack), the 9 emulated-service checks among them; the Register suite's 6 read checks ok, 8 skipped by name.
- **Browser** (`playwright-cli`, Chromium): the start page with the banner, party selection from the emulated parties,
  the form filled in and submitted, the receipt with the PDF; the downloaded PDF is the 602-byte `minimal.pdf`, its text
  "Minimal PDF without content" ([`../docs/minimal/`](../docs/minimal/)). The sample app makes no PDF of its own, so its
  metadata was given `enablePdfCreation` and a `ref-data-as-pdf` data type for the run (not committed). Events received
  `app.instance.created` and `app.instance.process.completed` (the app run with
  `--AppSettings:RegisterEventsWithEventsComponent=true`); an e-mail order to the app's user by national identity
  number reached `aspire.utvikler@example.com`, `Delivered`, in `notifications/api/localtest/inboxes`.
- **Clean slate**: after `env down` and `env up --minimal` again, Storage has no instance of the app (5 before, 0
  after), no app is registered, no event is kept.
- **The default stack unchanged**: `env up -r localtest-v2 --dev-localtest=localtest/localtest` (no `--minimal`, from
  the running minimal stack: it stopped the four first, then started the thirteen, 23.2 s) on empty named volumes; the
  root suite 26 of 26, `localtest/smoke-test.sh` 80 of 81 (the same `FRONTEND_DIST` check), the Register suite 14 of 14;
  `GET localtest/api/routes` has no `emulated`, no `X-Localtest-Emulated` header, no banner.

Two fixes came out of it: the minimal tmpfs mounts are `mode=1777` (Storage, a non-root user, could not create
`/data/blobs/ttd` on a root-owned tmpfs), and the health probes, which run in the Postgres image, mount a tmpfs at its
declared `VOLUME /var/lib/postgresql` (each probe left an anonymous volume behind, in both modes).
