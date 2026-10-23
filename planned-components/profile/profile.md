# Profile beyond the Register

**Superseded 2026-10-23 by task 27:** the studioctl wiring described here (a component entry in `components.go`, a host port, an image spec with a `STUDIOCTL_IMAGE_LOCALTEST_V2_*` override, a route in studioctl-server) is gone: a component is now one entry in manifest v2 without a host port, and its route is in the localtest container's `PlatformRoutes.cs`; see [`localtest/CONVENTIONS.md`](../../localtest/CONVENTIONS.md).

Research date: 2026-10-20 (task 14 of [`../../TODO.md`](../../TODO.md)), written against this repository at `a3776b3`.
The work was read-only apart from one scratch build and two scratch starts of the Profile host (section 4, "What was run"). Nothing
was built as an image. No Postgres, podman or studioctl command was run. The running stack on
`local.altinn.cloud:8000` was only read: GETs, plus read-only Register queries and Profile lookups. Claims that come
from reading code, not from running it, are marked "not verified".

## Summary

- **The premise of the task is partly stale.** At the pin, production Profile has **no SBL bridge or Altinn 2
  client**: a grep of `src/` finds only two comments that mention it. It has no Service Bus, MassTransit, Kafka or
  Redis. Its runtime dependencies are:
  - Postgres (EF Core, seven schemas)
  - the Register (three endpoints)
  - the PDP (two calls)
  - Notifications (instant orders for address verification codes)
  - Authentication's OpenID metadata
  - two Maskinporten clients (KRR and brreg) used only by sync jobs that are **off by default**, plus a write client
    to brreg used only when an organisation's notification address is changed
- **The stack already serves every dependency, and this was shown against the running stack.** The scratch host was
  built with the vendoring model's props and targets and two additive hooks. With a test user's token it called the
  stack's Register (`v2/internal/parties/query`, with a platform access token signed by the shared certificate) and the
  PDP (`parties/{id}/validate`). It let user 1337 through to party 500000, and turned user 1001 away from party 500600
  with 403. The handlers only failed where they reached the database, which was not there on purpose.
- **Consumers.** Apps, Authentication, Authorization, Access Management, Notifications, Receipt and Correspondence
  together call 11 Profile endpoints. The Register fakes serve all 11 today, Correspondence's two on the task 7
  branch. Arbeidsflate's BFF makes **12 more** calls, all under `users/current/…` or
  `organizations/{orgNumber}/notificationaddresses/…`: favourites, profile settings, professional notification
  settings, address verification and organisation notification addresses. None of them is served, and one of them
  (`notificationsettings/private/phonenumber`) does not exist in Profile at the pin.
- **The 11 served endpoints answer in shapes that are close to production but have different semantics.** Production
  takes a person's e-mail, phone number and `isReserved` from its KRR mirror, so `phoneNumber` is the KRR mobile
  number, not the Register's telephone number. Its `units/contactpoint/lookup` returns only addresses that users have
  registered for the organisation, not every holder of a role there. Its `users/contactpoint/lookup` returns
  `userId: 0` and the `…IsOutdated` flags.
- **Recommendation: Option A.** Vendor `Altinn/altinn-profile` at `d1f9871` into the default stack with:
  - two additive `#if LOCALTEST` hooks in `Program.cs` and no `#if !LOCALTEST`
  - about 600 lines of fakes
  - `postgres-init/80-profile.sh`
  - host port 8036 and route `/profile`, taken over from the Register component, whose three Profile controllers are
    then deleted

  Test users' profile data reaches Profile through **production code paths**:
  - the KRR sync job reads a fake KRR change log built from the Register's KRR stand-in (task 7)
  - profile settings are seeded once per user when the user has no row

  **Effort: 5 to 6 days.**
- **Verdict: straightforward** by the owner's three tests. There are 2 additive directives and no `#if !LOCALTEST`. Every
  dependency is already in the stack and was exercised. The auth model is the one the stack already runs (JwtCookie,
  platform access tokens, scope checks, PDP). Three things make it bigger than a filler, and the brief settles each one
  with a proposed default (section 8):
  - it adds a container to the default login path
  - it deletes the Register's Profile fakes, which Notifications and Correspondence use today through `/profile`
  - two answers change to production semantics: the phone number, and the unit contact points

## 1. Pins

| Repo | Commit | What was read |
| --- | --- | --- |
| [`Altinn/altinn-profile`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24) | `d1f9871` (= `origin/main`, 2026-10-19 11:14 +0200, "Document the notification settings DELETE response without a body (#1064)"). The reference clone's checkout is at `de3e9fe` (the pin of the Correspondence plan). This report read `d1f9871` through `git archive` into the scratchpad; the clone was not modified | all of `src/`, `Dockerfile`, `docker-compose.yml`, `dbsetup.sh`, appsettings |
| [`Altinn/dialogporten-frontend`](https://github.com/Altinn/dialogporten-frontend/tree/b04bb5d37da4773412b239bf19e3c15cb72db57d) | `b04bb5d` (= `origin/main`, 2026-10-20). The Arbeidsflate plan read `0011d95` | `packages/bff/src/graphql/{profile,notifications}/service.ts` |
| [`Altinn/app-lib-dotnet`](https://github.com/Altinn/app-lib-dotnet/tree/f7085936) | `f708593` (= `origin/main`, 2026-10-20) | `ProfileClient`, the prefill of `UserProfile`, language and party selection |
| [`Altinn/app-frontend-react`](https://github.com/Altinn/app-frontend-react/tree/acb618f) | `acb618f` (= `origin/main`, 2026-10-20) | profile and `doNotPromptForParty` use, Cypress |
| `Altinn/altinn-correspondence`, `altinn-receipt`, `altinn-dialogporten-adapter`, `dialogporten` | `6dcf561`, `babc0ed`, `0a32985`, `0946da8` (the plans' pins) | grepped for Profile calls |
| Vendored components in this repository | as in each `vendor/UPSTREAM.md` | Profile clients of Authentication, Authorization, Access Management, Notifications, Events, Storage, Register's controllers |
| `Altinn/altinn-studio` | `447e53e` (2026-10-16) | the old localtest's Profile stand-in (reference only) |

## 2. Profile's API at the pin, and who calls it

Routes are under `profile/api/v1/`. Authentication is `JwtCookie` (cookie `AltinnStudioRuntime` or a bearer token,
validated against `GeneralSettings:OpenIdWellKnownEndpoint`,
[`Program.cs:173-193`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs#L173-L193)).
The policies are defined at
[`Program.cs:195-204`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs#L195-L204):

| Policy | Meaning |
| --- | --- |
| *user* | `[Authorize]`: any valid Altinn token |
| *PAT* | `PlatformAccess`: a platform access token |
| *enduser~* | `PortalEndUserAccess`: scope `altinn:portal/enduser`, but **feature-toggled**. With `PortalAccessSettings:EnforceAccessCheck=false` (the default) it only logs |
| *party* | `UserPartyAccess`: Register looks up the party id, then the PDP's `parties/{partyId}/validate?userid=` |
| *corr* | scope `altinn:profile/correspondence.notificationsettings.read` |
| *orgres* | PDP decision on resource `altinn-profil-api-varslingsdaresser-for-virksomheter`, action read or write |
| *notE* | `DenyIdportenEpostAuthentication` |
| *dash* | scope `altinn:profile.support.admin` |
| *anon* | no attribute: internal controllers, anonymous by design |

Column "Register fakes": **yes** means the Register component serves the endpoint today
(`LocaltestProfileController`, `LocaltestProfileContactPointsController`); **7** means the task 7 branch adds it
(`LocaltestProfileCorrespondenceController`, commit `b6afdef`); **no** means `/profile` answers 404.

| # | Method and route | Auth | Callers in the stack | Register fakes | Same as production? |
| --- | --- | --- | --- | --- | --- |
| 1 | `GET users/{userId}` | user + PAT | app-lib `ProfileClient.GetUserProfile(int)`, Receipt `ProfileWrapper` | yes | shape yes; values differ (2.1) |
| 2 | `GET users/byuuid/{userUuid}` | user + PAT | app-lib | yes | as 1 |
| 3 | `GET users/current` | user + enduser~ | Arbeidsflate BFF `getUserFromCore`, old localtest | yes | as 1 |
| 4 | `POST users` (body: SSN string) | user + PAT | app-lib `GetUserProfile(string ssn)` | yes (also takes object bodies) | as 1 |
| 5 | `POST internal/user` (`{userId \| userUuid \| username \| ssn}`) | anon | Authentication `ProfileService`, Authorization `ProfileWrapper`, Access Management `ProfileClient` | yes | as 1 |
| 6 | `PUT users/current/profilesettings` | user + enduser~ | none | no | |
| 7 | `PATCH users/current/profilesettings` | user + enduser~ | Arbeidsflate (preselected party, show client units, language) | no | |
| 8 | `POST users/contactpoint/lookup` | anon | Notifications `ProfileClient:46` | yes | differs: fake fills `userId` and omits `mobileNumberIsOutdated` and `emailIsOutdated`; production drops contact points older than 18 months unless `useStaleContactInfo` |
| 9 | `POST users/contactpoint/lookupsi` | anon | Notifications `:93` | yes | differs: production answers every `idporten-email` URN (e-mail from the URN, phone from `self_identified_users`); the fake answers known users only |
| 10 | `POST users/contactpoint/availability` | anon | none | no | |
| 11 | `POST units/contactpoint/lookup` | anon | Notifications `:70` | yes | **semantics differ**: production returns only addresses users registered for the party (and resource) through endpoint 21; the fake returns every user holding a role or package there |
| 12 | `POST organizations/notificationaddresses/lookup` | anon | Notifications `:120` | yes | shape yes; the fake always answers empty lists; production reads its brreg mirror and falls back to the main unit |
| 13 | `POST correspondence/units/contactpoint/lookup` | user + corr | Correspondence `AltinnProfileService` | 7 | as 11 |
| 14 | `POST correspondence/organizations/notificationaddresses/lookup` | user + corr | Correspondence | 7 | as 12 |
| 15 | `GET users/current/party-groups/favorites` | user + enduser~ | Arbeidsflate `getFavoritesFromCore` | no | |
| 16 | `PUT, DELETE users/current/party-groups/favorites/{partyUuid}` | user + enduser~ | Arbeidsflate | no | |
| 17 | `GET, POST users/current/party-groups`, `GET, PATCH, DELETE …/{groupId}`, `PUT, DELETE …/{groupId}/associations/{partyUuid}` (7) | user + enduser~ | none at `b04bb5d` | no | |
| 18 | `GET users/current/notificationsettings/parties` | user + enduser~ | Arbeidsflate `getNotificationsettingsForCurrentUser` | no | |
| 19 | `GET users/current/notificationsettings/parties/{partyUuid}` | user + party + enduser~ | none | no | |
| 20 | `GET users/current/notificationsettings/parties/byorgnumber/{orgNumber}` | user + party + scope `altinn:portal/enduser` or `altinn:profile/enduser:notificationsettings.read` (not toggled) | none | no | |
| 21 | `PUT, PATCH users/current/notificationsettings/parties/{partyUuid}` | user + party + enduser~ | Arbeidsflate (PATCH) | no | |
| 22 | `DELETE users/current/notificationsettings/parties/{partyUuid}` | user + enduser~ | Arbeidsflate | no | |
| 23 | `PUT users/current/notificationsettings/private/phonenumber` | – | Arbeidsflate `updateSIPrivatePhoneNumber` | no | **not in Profile at the pin**. The BFF is ahead of Profile, so this call is a 404 in production test environments too until Profile gains the route (not verified upstream) |
| 24 | `GET users/current/verification/verified-addresses` | user + enduser~ | Arbeidsflate | no | |
| 25 | `POST users/current/verification/send`, `POST …/verify` | user + enduser~ + notE | Arbeidsflate | no | `send` orders the code through Notifications' instant orders |
| 26 | `GET organizations/{orgNumber}/notificationaddresses/mandatory` | user + enduser~ + orgres(read) | Arbeidsflate `getNotificationAddressByOrgNumber` | no | needs the resource's policy in the Resource Registry (section 3) |
| 27 | `GET …/mandatory/{id}`, `POST …/mandatory`, `PUT, DELETE …/mandatory/{id}` | user + enduser~ + orgres | none at `b04bb5d` | no | writes go to brreg (section 3) |
| 28 | `dashboard/…` (9 GETs) | user + dash | none (the support dashboard) | no | |
| 29 | `GET /health`, `profile/api/v1/error` | anon | studioctl health | Register's own `/health` | |

Nobody else in the stack calls Profile:

- Events has `ApiProfileEndpoint` in its settings but no code reads it.
- Storage, Dialogporten core, the adapter and the app frontend do not call Profile. The app frontend calls the app's own
  `/{org}/{app}/api/v1/profile/user`, which app-lib answers through endpoint 1.
- The old localtest served only 1 and 3 (`src/Controllers/Profile/UsersController.cs`).

### 2.1 What production's user profile is made of (endpoints 1 to 5)

`UserProfileService.GetUserAndEnrich`
([`UserProfileService.cs:92-160`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Core/User/UserProfileService.cs#L92-L160))
builds the answer in four steps:

1. **Register v2.** `POST register/api/v2/internal/parties/query?fields=person,party,user,si,si.email` with a URN
   (user id, party UUID, username or person identifier). The `person` or self-identified party is mapped to
   `UserProfile` and a v1 `party` by
   [`UserProfileMapper`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Core/User/UserProfileMapper.cs#L43-L95).
   `party.person.telephoneNumber` and `mobileNumber` are always `""`.
2. **`user_preferences.profile_settings`.** This step sets:
   - `language`
   - `doNotPromptForParty`
   - `preselectedPartyUuid`, plus `preSelectedPartyId`, looked up through Register
   - `showClientUnits`, `shouldShowSubEntities`, `shouldShowDeletedEntities`

   With no row the defaults apply: `nb`, `doNotPromptForParty=false`, `shouldShowSubEntities=true`, the rest false.
3. **`contact_and_reservation.person`**, the KRR mirror, for persons. It sets `phoneNumber` from the KRR mobile number,
   plus `email` and `isReserved`. A person with no KRR row has no e-mail and no phone number.
4. **`user_preferences.self_identified_users`** for self-identified users.

What this changes for consumers:

- **The phone number.** In the seed, users 1001 to 1003, 1337 and 12345 have `telephoneNumber` `12345678` and a
  different `mobileNumber`. The Register fake answers `phoneNumber` = `telephoneNumber ?? mobileNumber`, so
  `12345678` (checked on the stack for 1337). Production would answer the KRR mobile number (`87654321` for 1337).
  App prefill of `UserProfile.PhoneNumber` (for example `src/test/apps/payment-test`) and anything shown from it
  change accordingly.
- **`doNotPromptForParty`.** The app frontend uses it to skip the party prompt (`Entrypoint.tsx:95`), and the seed
  sets it `true` for six of eight users. Without seeded `profile_settings` rows every user would be prompted, so the
  seed must write these rows (section 6).
- **`language`.** App-lib reads it for the UI language, notifications and signing (`Authenticated.cs:76`,
  `NotificationService.cs:424`), and user 1337 has `nn`. It also needs the seeded row.

## 3. What production Profile needs, and what the stack has

| Dependency | Production | In the stack today | Plan |
| --- | --- | --- | --- |
| **Postgres** | Npgsql + EF Core, schemas `lease`, `user_preferences`, `contact_and_reservation`, `organization_notification_address`, `professional_notification_settings`, `address_verifications` (6 migrations, [`Migrations/`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/Migrations)); applied **only** by `dotnet Altinn.Profile.dll --run-db-migrations` with the admin connection string ([`Program.cs:65-71`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs#L65-L71), a Kubernetes pre-upgrade hook). Migrations `GRANT … TO platform_profile` by name. Startup throws without both a connection string and a password ([`ServiceCollectionExtensions.cs:46-50`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/ServiceCollectionExtensions.cs#L46-L50)). The legacy Yuniql folder `Migration/` (39 files) is still copied by the Dockerfile but nothing reads it | shared Postgres 18.3 | `80-profile.sh`: roles `platform_profile_admin` (owner of `profiledb`, not superuser) and `platform_profile`, password `Password` as in upstream's appsettings. A fakes hosted service runs `MigrateAsync` with the admin connection before Kestrel listens (section 5.3). Not verified on Postgres 18 |
| **Register** | `POST v2/internal/parties/query` (`fields=person,party,user,si,si.email` and `fields=id,uuid,org-id`), `POST v2/internal/parties/main-units`, `GET v1/parties/identifiers?orgs=|uuids=`, each with a platform access token `platform`/`profile` ([`RegisterClient.cs:91, 227, 272, 296`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/Register/RegisterClient.cs#L227)); ids cached in memory for `CoreSettings:ProfileCacheLifetimeSeconds` (600). **No streams**: Profile does not consume Register's party or role streams | vendored Register: all three endpoints exist under policy `InternalOrPlatformAccess`. **Verified:** run by hand on the stack (user id, SSN, UUID, username and organisation URNs; `main-units`; `identifiers`), and by the scratch host with its own platform token | nothing new |
| **Authorization** | `GET authorization/api/v1/parties/{partyId}/validate?userid=` with the user's token ([`AuthorizationClient.cs:47`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/Authorization/AuthorizationClient.cs#L47)) for the *party* policy. PEP `PDPAppSI` decision for *orgres* | the component; `PartiesController` kept production. **Verified** through the scratch host (200 path for 1337 and 500000, 403 for 1001 and 500600) | the Resource Registry seed gains `altinn-profil-api-varslingsdaresser-for-virksomheter` with its tt02 policy, for endpoints 26 and 27. Its rules grant read, write and delete to the ER role codes `dagl`, `lede`, `innh`, `best`, `hadm` and others, and to the access packages `hovedadministrator` and `starte-drive-endre-avvikle-virksomhet`; fetched from tt02 on 2026-10-20. Without it, endpoint 26 answers 403 (the BFF treats that as soft) |
| **Notifications** | `POST notifications/api/v1/future/orders/instant/{sms\|email}` with a platform access token `platform`/`profile` ([`NotificationsClient.cs:91-105`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/Notifications/NotificationsClient.cs#L91-L105)), for address verification codes and change notices | the component. `InstantOrdersController` accepts `CreateScopeOrAccessTokenRequirement` | nothing; codes land in the Notifications inboxes (the tester reads the code there) |
| **Authentication** | OpenID metadata for token validation; `RequireHttpsMetadata=false` **only in `Development`** ([`Program.cs:189-192`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs#L189-L192)) | the component's `openid/` over http | run with `ASPNETCORE_ENVIRONMENT=Development` (as the scratch start did) |
| **KRR (Kontaktregisteret)** | Mirror in `contact_and_reservation.person`, filled only by `KrrSyncJob` → `ContactRegisterUpdateJob` → `IContactRegisterHttpClient` (Maskinporten `krr:global/…`, change log `hentEndringer`), **off by default** (`JobSettings:KrrSyncEnabled=false`, [`JobsServiceCollectionExtensions.cs:75-96`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Extensions/JobsServiceCollectionExtensions.cs#L75-L96)). No live KRR call on a request | Register's KRR stand-in `POST register/api/localtest/krr/rest/v2/personer` (task 7 branch) | **fake** `IContactRegisterHttpClient` that turns the stand-in's answer for every seeded person into a change log; the production job and upsert run (section 5.3). The job is turned on with a 1-minute interval and also triggered at start and on demand |
| **brreg (KOF notification addresses)** | Read: `OrgSyncJob` mirrors the change log into `organization_notification_address` (off by default). Write: endpoints 27 call `IOrganizationNotificationAddressUpdateClient` (brreg `kof-reception`, Maskinporten) on every create, update or delete | nothing | **fake** update client that returns a generated registry id (the row is still written by production code). Read sync stays off; no official addresses are seeded (question 3) |
| **Maskinporten** | `AddMaskinportenHttpClient<SettingsJwkClientDefinition, IContactRegisterHttpClient, …>` at registration; the JWK is only used when a token is requested | the shared fake exists | **not needed**: both Maskinporten users are replaced. The settings sections must stay non-empty (startup throws otherwise, [`ServiceCollectionExtensions.cs:104-117`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile.Integrations/ServiceCollectionExtensions.cs#L104-L117)); upstream appsettings already fill them |
| **Key Vault** | `config.AddAzureKeyVault` only when `kvSetting:SecretUri` is set ([`Program.cs:95-100`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs#L95-L100)); `PublicSigningKeyProvider` for incoming platform tokens | | **off** by configuration; **fake** `IPublicSigningKeyProvider` returning the shared certificate (copy of Notifications'). Outgoing platform tokens: the production `SigningCredentialsResolver` reads `AccessTokenSettings:AccessTokenSigningCertificateFullPath`, the variable studioctl already sets (verified in the scratch start) |
| **Application Insights** | only with `ApplicationInsights:ConnectionString` | | **off** |
| **Leases and jobs** | `PostgresqlLeaseProvider` in schema `lease`; `RecurringJobHostedService` (vendored `ServiceDefaults.Jobs`); jobs wait one interval before their first run | | run unchanged on Postgres |
| **Caches** | `IMemoryCache` for Register ids (600 s) | | unchanged; `CoreSettings:ProfileCacheLifetimeSeconds=10` locally (faster than production: renamed or re-registered app test users show at once) |
| **SBL bridge, Altinn 2, Service Bus, Kafka, Redis** | none at the pin (grep) | | |

## 4. Build chain

| Question | Finding |
| --- | --- |
| Projects | Host `Altinn.Profile` (`Microsoft.NET.Sdk.Web`), `Core`, `Integrations`, `Models`, and two vendored libraries `ServiceDefaults.Jobs` and `ServiceDefaults.Leases` (copies of altinn-authorization-utils). All six are needed |
| Size (C#, lines) | host 4 936, Core 5 002, Integrations 5 919 (+876 in migrations), Models 171, Jobs 1 391, Leases 716: about **18 100** |
| SDK | no `global.json`; the Dockerfile pins `sdk:10.0.402-alpine3.24@sha256:230c3fb…`, the **same digest** as the Notifications component's build stage |
| Package management | plain `PackageReference` with versions in each csproj: **no CPM, no `Directory.Build.props/.targets`, no lock files**. StyleCop only in `Debug`. `GenerateDocumentationFile=true` on the host. No `TreatWarningsAsErrors` |
| `localtest.build.props`/`.targets` | **Apply unchanged** (Notifications' two files with the project name changed). There is no upstream `Directory.Build.props` to import, so the `GetPathOfFileAbove` import is a no-op. `fakes/**/*.cs` compile into the host |
| Dockerfile | build context = repository root; restore from the four main csproj files (the two ServiceDefaults projects are not copied at that layer, so `dotnet publish` restores again after `COPY src`; not checked in a scratch restore); `dotnet publish -c Release`; runtime `aspnet:10.0.13-alpine3.24@sha256:819bd36…` (same digest as Notifications); copies `Migration/`; user `dotnet` 1000/3000; `mkdir /tmp/logtelemetry`; `EXPOSE 5030` but **no `ASPNETCORE_URLS`**: upstream's compose file sets `http://+:5030`, so studioctl must set it |

### What was run (all in the scratchpad; the reference clone was not modified)

1. `git archive d1f9871 src Dockerfile` into a scratch `vendor/`. Notifications' `localtest.build.props` and `.targets`
   (project name changed). One scratch fakes file with the two entry points, the signing-key provider, a no-op brreg
   update client and a migration hosted service. Two `#if LOCALTEST` blocks in `Program.cs`, after lines 57 and 220.
   `global.json` pinned to SDK 10.0.401, because without it the machine's 11.0 preview SDK was picked up.
   `dotnet build -c Release`: **succeeded, 0 warnings, 0 errors, 8 s**. The host assembly contains the fake types.
   `bin/` and `obj/` were deleted afterwards.
2. Started on `127.0.0.1:15036` in `Development` with `PostgreSqlSettings__EnableDBConnection=false`, so the
   migration step was skipped. Development DI validation passed. Results:
   - `GET /health`: 200 `Healthy`
   - `GET users/current`, `GET users/current/party-groups/favorites` and `POST correspondence/units/contactpoint/lookup`
     without a token: 401
   - `swagger/v1/swagger.json`: 200
3. Started again with a connection string to a closed port (`127.0.0.1:1`), `AccessTokenSettings__AccessTokenSigningCertificateFullPath`
   pointing at `localtest/certs/localtest-accesstoken.pfx`, and a user token from the stack's
   `/Home/GetTestUserToken`:
   - `GET users/current/notificationsettings/parties/{DDG Fitness}` as 1337 got through JwtCookie (the stack's OpenID
     metadata over http), the party policy (Register `v2/internal/parties/query` with Profile's own platform token,
     then the PDP's `parties/500000/validate?userid=1337`) and into the repository, where it failed on the database
     (500, by design).
   - As 1001 for EAS Health Consulting (500600, not in 1001's party list): **403**.
   - `GET users/current` as 1337 reached `EnrichWithProfileSettings`. The Register answer had therefore been
     deserialised and mapped to a person.

## 5. Options

### Option A (recommended): vendor Profile into the default stack, fakes for KRR, brreg and Key Vault

Production Profile on the shared Postgres, with every endpoint above. The Register component stops answering
`/profile`, and its three Profile controllers are deleted.

### Option B: keep extending the Register fakes

The Arbeidsflate plan already sketches this (five routes, about 150 lines over an in-memory map, 1 day). Production
semantics need much more:

| What | Lines (estimate) | What still breaks |
| --- | --- | --- |
| 12 Arbeidsflate routes (favourites, settings, notification settings, verification, org addresses) with validation and problem details as Profile has them | 700 to 900 | invented response shapes for errors; the BFF already calls a route Profile does not have (23) and a fake would serve it, masking the gap |
| A mutable store that survives the Register's snapshot rebuilds (it rebuilds on every app registration) and restarts (file-backed) | 200 to 300 | a second persistence model inside the Register component, which owns nothing of Profile's |
| Consistency: profile-setting writes merged into 1 to 5; notification settings feeding 11 and 13; verified addresses gating notification settings | 150 to 250 | every merge rule is a re-implementation of `UserProfileService`, `UnitContactPointService` and `AddressVerificationService` |
| The verification flow: either "always verified" (no production semantics) or codes ordered through Notifications | 100 to 200 | |
| The PDP check for org addresses and the party check | 50 to 100 | |

That comes to 1 200 to 1 750 lines and 4 to 5 days, about the cost of Option A, and the result is a fake. Every
upstream change to Profile becomes a manual diff with no merge to help. Choose Option B only if the owner wants the
profile page not to error and nothing more; that is the Arbeidsflate plan's 1-day variant.

### Option C: a hybrid

- **C1: vendored Profile in an optional group (`profile`, implied by a future `arbeidsflate` group); the Register
  fakes keep `/profile` in the default stack.** `/profile` would then belong to two components depending on the group.
  The route table and the 503 front door (task 16) assume one owner per prefix. Two implementations of the same
  contract would drift. A tester's language or favourite would change behaviour depending on `--with`. Rejected.
- **C2: vendored Profile only for `users/current/*` and `organizations/*/notificationaddresses/mandatory`; the Register
  keeps the lookups.** This splits state: a language set through Arbeidsflate (Profile's Postgres) would never show in
  `users/{id}` (Register). Rejected.

### Recommendation

Choose **Option A**:

- Production code serves every consumer, including the three written after the Register fakes (Correspondence,
  Arbeidsflate, the verification flow through Notifications).
- The seam is the smallest of any vendored component so far: two additive hooks and no `#if !LOCALTEST`.
- The only dependencies Profile has are components the stack already runs and that answered the scratch host.

## 6. Option A in detail

### 6.1 What to copy

`git archive d1f9871 src Dockerfile .dockerignore docker-compose.yml .editorconfig stylecop.json` into
`localtest/components/profile/vendor/`. The first commit is unmodified. `vendor/UPSTREAM.md` records the pin and the
two hook sites. `test/` is not copied.

### 6.2 Directive sites (two, both additive; zero `#if !LOCALTEST`)

[`src/Altinn.Profile/Program.cs`](https://github.com/Altinn/altinn-profile/blob/d1f9871c40bf00eda949338957ce4259e4ae5a24/src/Altinn.Profile/Program.cs):

```csharp
SetConfigurationProviders(builder.Configuration);                 // line 57
#if LOCALTEST
Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestConfiguration(builder.Configuration);
#endif
```

```csharp
    services.AddSyncJobs(config);                                  // line 220
#if LOCALTEST
    Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestServices(services, config);
#endif
```

The first hook must sit before `ConfigureServices` (line 61). Three settings there are read at registration time
rather than through options: `AddRegisterService` reads the connection string, `AddMaskinportenClient` binds its
settings, and `AddSyncJobs` reads `JobSettings`. The second hook must come after `AddRegisterService` and
`AddMaskinportenClient` (lines 213 and 214), because it `RemoveAll`s what they register. Both sites were built and run
in the scratch build.

### 6.3 Fakes (`fakes/`, namespace `Altinn.Profile.Localtest`, compiled into the host)

| File | Replaces | Lines (estimate) |
| --- | --- | --- |
| `LocaltestProfileExtensions.cs` | the two entry points. Configuration defaults sit just below the environment-variable source, as Notifications does. Forced values: `kvSetting:SecretUri` and `ApplicationInsights:ConnectionString` empty. Defaults: OpenID, Register, PDP and Notifications endpoints from `Localtest:Profile:PlatformEndpoint`; `JobSettings:KrrSyncEnabled=true` with a 1-minute interval; `OrgSyncEnabled=false`; `CoreSettings:ProfileCacheLifetimeSeconds=10` | 120 |
| `Auth/LocaltestPublicSigningKeyProvider.cs` | the Key Vault `IPublicSigningKeyProvider` (copy of Notifications') | 40 |
| `Krr/LocaltestContactRegisterClient.cs` | `IContactRegisterHttpClient`: `GET register/api/localtest/test-users` for the persons, then `POST register/api/localtest/krr/rest/v2/personer` for their e-mail, mobile number and reservation. Maps to `ContactRegisterChangesLog` with `LastUpdated` = now (the 18-month cut-off in `UserContactPointService.cs:19` would otherwise hide them). Emits a change only when the content hash changes since the last change number. `ContactRegisterUpdateJob` and `PersonRepository.SyncPersonContactPreferencesAsync` (production) write the rows | 150 |
| `Brreg/LocaltestOrganizationNotificationAddressRegistry.cs` | `IOrganizationNotificationAddressUpdateClient`: returns `lt-<guid>` as registry id; the production service writes the row | 30 |
| `Startup/LocaltestDatabaseStartup.cs` | the `--run-db-migrations` pre-upgrade hook: `MigrateAsync` with the admin connection string as an `IHostedService` registered before the web host starts, then one KRR sync and the settings seed (6.6). Optional `Localtest:Profile:ResetOnStart` truncates the user schemas | 120 |
| `Seed/LocaltestProfileSettingsSeeder.cs` | nothing in production. For every seeded user without a `profile_settings` row, it inserts `language`, `doNotPromptForParty` and `preselectedPartyUuid` (from `preSelectedPartyId` through Register) through `IProfileSettingsRepository`. User changes are never overwritten (question 4) | 80 |
| `Inspection/LocaltestProfileController.cs` | `profile/api/localtest/` (anonymous): `GET status` (applied migrations, last KRR change number, last sync), `POST sync` (KRR sync and settings seed now; the app runner may call it after registering test data, question 5), `GET users/{userId}` (the user's rows: settings, KRR row, favourites and groups, professional addresses, verified addresses, open verification codes without the code) | 120 |

That is about **660 lines**.

### 6.4 Register component changes

- `register/api/localtest/test-users` already lists `ssn`, `email` and the profile settings; nothing else is needed
  there.
- Delete `LocaltestProfileController`, `LocaltestProfileContactPointsController` and (after task 7 merges)
  `LocaltestProfileCorrespondenceController`, with their registrations, README section and smoke checks. The checks
  move to `components/profile/smoke-test.sh`.
- `LocaltestKrrController` (task 7) becomes the single source of KRR answers for Correspondence and Profile.
- Delete the `/profile` route from `registerComponent` (`components.go:286`).

### 6.5 Postgres

`localtest/components/profile/postgres-init/80-profile.sh`, modelled on `60-notifications.sh`:

- roles `platform_profile_admin` (LOGIN, **not** superuser) and `platform_profile` (LOGIN), both with password
  `Password`
- `CREATE DATABASE profiledb OWNER platform_profile_admin`
- one `COPY` line in `localtest/postgres/Dockerfile`

The migrations create their schemas as the owner and grant `platform_profile` usage and DML, as in production. There
is no reset on start. `env reset` drops the volume as for the other components.

Not verified:

- the six migrations on Postgres 18.3
- whether the admin role needs `CREATEROLE` or anything beyond ownership. The migrations only `GRANT` to an existing
  role.

### 6.6 Test-data mapping (answers question 5 of the brief)

The Register's `testdata/testData.json`, plus the apps' posted test data, stays the single seed. Profile reads it
through the Register's localtest API, never from the file.

| Profile data | Source | Path into Profile |
| --- | --- | --- |
| e-mail and mobile number of persons, `isReserved` | seed `email` and `mobileNumber` (`telephoneNumber` as fallback, as the KRR stand-in does); reservation `NEI` for everyone | fake KRR change log → production KRR sync → `contact_and_reservation.person` |
| language, `doNotPromptForParty`, preselected party | seed `language`, `doNotPromptForParty`, `preSelectedPartyId` | seeder → `user_preferences.profile_settings`, insert if missing |
| self-identified users' e-mail | Register `si.email` (production reads it from Register) | none needed |
| favourites, groups, professional notification addresses, verified addresses | not seeded; created through the API (Arbeidsflate or curl) and kept in Postgres across restarts | production |
| official organisation notification addresses | not seeded (unchanged from today: empty lists) | question 3 |
| `reserved` | no seed field yet (task 6 decision: no reserved flag yet). When it comes, one optional `reserved` on persons is read by the Register's KRR stand-in, which feeds both Correspondence and Profile | |

*2026-10-21: the "official organisation notification addresses" and "`reserved`" rows are superseded: both are seeded since tasks 18 and 14 (`components/profile/README.md`, "The seed").*

App test data registered after start reaches Profile in one of two ways: at the next KRR job run (at most 1 minute
later), or at once when the app runner calls `POST profile/api/localtest/sync` (question 5). Register lookups (names,
ids) are live, so a newly registered app user can log in at once. Only their e-mail, phone number and settings can lag.

### 6.7 Dockerfile

The Dockerfile mirrors upstream (same digests, restore layers, publish, `Migration/` copy, `dotnet` user,
`/tmp/logtelemetry`). `localtest:` deviations:

- the build context is the component folder
- `ENV DirectoryBuildPropsPath/DirectoryBuildTargetsPath`
- `COPY localtest.build.* fakes/`
- `ASPNETCORE_URLS=http://+:5030`, either as an `ENV` line or from studioctl

### 6.8 studioctl and images (`localtest/studioctl/src/cli/`)

- Component `profile`: image spec, container port 5030, **host port 8036**, health `/health`, route `/profile`
  (moved from `register`), env:
  - `certSettingEnv`
  - `ASPNETCORE_ENVIRONMENT=Development`
  - `Localtest__Profile__PlatformEndpoint`
  - `PostgreSqlSettings__AdminConnectionString` and `__ConnectionString` (`Host=postgres`)
- Deps: `postgres`, `register`, `authorization`, `authentication`.
- Components that call Profile gain a dep on `profile`: `authentication`, `authorization`, `access-management`,
  `notifications`, and `correspondence` and `receipt` where they exist.
  - Authentication only needs Profile at login, so a start-order dep is enough. Profile only needs Authentication's
    metadata at the first request, so there is no start cycle.
- Default stack (not a group): login goes through `internal/user`.
- Topology test, `.github/workflows/images.yml` matrix entry, `Images.LocaltestV2.Profile`.

### 6.9 Verification plan (`components/profile/smoke-test.sh`, through the proxy)

1. `GET /profile/…/health` 200; `GET profile/api/localtest/status` shows six migrations applied and a KRR change
   number > 0.
2. **Parity with today's Register fakes** (the moved checks):
   - `users/1337` with a user token and platform token is 200 with `party.ssn` and `language` `nn`
   - 401 without a user token, 403 without a platform token
   - `POST users` with an SSN string
   - `internal/user` for 1004 has `userType` 2 and `party.name` `epost:…`
   - `users/byuuid/…510001` has `doNotPromptForParty` true
   - `users/current` with a cookie token
3. **Production semantics:**
   - `phoneNumber` of 1337 is `87654321` and `isReserved` is false
   - `users/contactpoint/lookup` for 1337 has e-mail and mobile number, and `userId` 0
   - `lookupsi` for an unknown `idporten-email` URN answers that e-mail
   - `organizations/notificationaddresses/lookup` gives empty lists for 897069644
4. **Arbeidsflate paths with a user token for 1337:**
   - favourites `PUT`, `GET` (contains 500000), `DELETE`
   - `PATCH profilesettings` `{language:"en"}`, then `users/1337` says `en`, then reset to `nn`
   - `verification/send` for an e-mail address: the code appears in the Notifications inbox for that address;
     `verify` with it; `verified-addresses` lists it
   - `PUT notificationsettings/parties/{DDG}`, after which `units/contactpoint/lookup` for 897069644 with any
     resource returns 1337's address
   - `DELETE` the setting
   - `notificationsettings/parties/{EAS}` as 1001 is 403
5. **Org addresses** (after the Resource Registry seed): `GET organizations/897069644/notificationaddresses/mandatory`
   as 1337 (DAGL there) is 404 or 200 with no addresses, not 403. `POST` one; the brreg fake gives a registry id; the
   lookup (12) now returns it.
6. **Correspondence scope** (`--with correspondence`): `correspondence/units/contactpoint/lookup` is 401 without a
   token, 403 without the scope, 200 with Correspondence's token.
7. **Restart persistence:** the favourite and setting from step 4 survive `env down` and `env up`.
8. **Regression:**
   - the Register, Authentication, Authorization, Access Management and Notifications suites (Notifications' person
     recipient still reaches an inbox through `users/contactpoint/lookup`)
   - `run-platform-regression.sh`
   - one Cypress app run that logs in as 1337 and is not prompted for a party
   - `payment-test` prefill shows the mobile number

### 6.10 Effort and risk

| Piece | Days |
| --- | --- |
| Vendor copy, props/targets, hooks, Dockerfile | 0.5 |
| Fakes (6.3) | 1.5 |
| Postgres init and the migration run on 18.3 | 0.5 |
| Register deletions, the Resource Registry seed of the org-address resource | 0.5 |
| studioctl component, route move, deps, image matrix, topology test | 1 |
| Smoke test, regression suites, one Cypress run | 1 to 1.5 |
| **Total** | **5 to 6** |

| Risk | Note |
| --- | --- |
| The migrations on Postgres 18.3 with a non-superuser owner | not run; the only grants are `GRANT … TO platform_profile` |
| The login path gains a container and a database read | every `internal/user` now does a Register query and two small Postgres reads instead of an in-memory lookup. Not measured |
| Phone number and unit contact points change for existing consumers | sections 2.1 and 7; Cypress and the Notifications inbox checks are the evidence |
| Ordering with tasks 7 and 11 | task 7 adds Profile controllers to the Register that this task deletes; dispatch after 7 merges |
| Memory | one more .NET host (about 150 to 250 MB) on a 7.8 GB machine; not measured |

## 7. Known differences from production (after Option A)

1. The KRR mirror is filled from the seed, not from Kontaktregisteret. Every contact point is "just updated"
   (`LastUpdated` = sync time), so the 18-month staleness rule never triggers.
2. Nobody is reserved until a seed flag exists.
3. brreg never sees an organisation notification address change; the registry id is local
   (`has_registry_accepted` stays as production sets it before the brreg answer; not verified which value that is).
4. There is no `OrgSyncJob`: official organisation addresses exist only when created through endpoint 27.
5. `PortalAccessSettings:EnforceAccessCheck` stays `false`, as upstream's default. Tokens without `altinn:portal/enduser`
   are logged, not refused.
6. Register ids are cached for 10 s, not 600 s.
7. Migrations run at start, not in a pre-upgrade hook.
8. `notificationsettings/private/phonenumber` (endpoint 23) is a 404, as it would be against Profile at the pin.

## 8. Open questions for the product owner (each with the default the brief uses)

1. **Default stack or group?** Default: in the default stack, because login needs `internal/user`. The alternative,
   C1, is rejected in section 5.
2. **Unit contact points.** Production semantics, so `units/contactpoint/lookup` returns only addresses users
   registered. Today the Register fake returns every holder of a role or package. Default: production semantics. A
   tester registers a professional address through Arbeidsflate or curl.
   - I cannot confirm whether today's role-holder answer ever reaches an inbox. The Notifications follow-up records
     422 for organisation recipients.
3. **Official organisation notification addresses.** Seed them from the organisations' `eMailAddress` and
   `mobileNumber` through a fake brreg change log (this would also settle the Notifications follow-up "organisation
   recipients always 422"), or leave them empty. Default: empty, unchanged from today. The seed fields are Register
   contact data, not KOF addresses.
4. **Seeded settings vs user changes.** Should a seed value overwrite a setting the user changed through Arbeidsflate
   (seed wins on every sync), or should user changes win (insert if missing)? Default: user changes win. `env reset`
   restores the seed.
5. **Should the app runner call `POST profile/api/localtest/sync` after registering test data?** Default: no. The
   1-minute KRR job covers it; the call is a one-line follow-up if testers trip over the lag.
6. **Phone number change.** `phoneNumber` becomes the seed's mobile number (production semantics) instead of the
   telephone number. Default: accept. Check `payment-test` and the Cypress suite (section 6.9).
7. **Endpoint 23.** Raise upstream that the BFF calls a Profile route that does not exist at `d1f9871`, or leave it
   to task 11? Default: note it in task 11.

## 9. Not verified

- **The migrations and every database path.** No Postgres was used: no migration was applied, no row written, no KRR
  sync run, and no seed or inspection endpoint written.
- **The response shapes of production Profile against each consumer's DTOs.** They were read from the models, not
  called end to end. This covers Authentication's `UserProfile` deserialisation (the `party.ssn` issue in the
  Arbeidsflate plan) and app-lib's.
- Whether the stack's PDP decides `altinn-profil-api-varslingsdaresser-for-virksomheter` for a seeded DAGL. The
  resource is not seeded.
- The address verification order through Notifications, the instant e-mail capture, and the BFF reading the code.
- Image build, image size and start time. Memory use of the extra host.
- The scratch start ran only in `Development` with the database off or unreachable.
- Profile's behaviour under Arbeidsflate's tokens (task 2 code-flow tokens with `altinn:portal/enduser`). Only cookie
  tokens from `GetTestUserToken` were used.
- Whether `has_registry_accepted` and the org-address write path behave with the brreg fake.
