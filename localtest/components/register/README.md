# register

Production Altinn Register (`vendor/`, see `UPSTREAM.md`) running on an in-memory register seeded from
`testdata/testData.json` plus the `testData.json` of registered apps. It also answers the Profile user endpoints.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except `#if LOCALTEST` blocks (`git diff` against the baseline commit shows them all); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, link the fakes project |
| `fakes/Altinn.Register.Localtest/` | In-memory `IPartyPersistence`, `IPartyExternalRolePersistence` over a swappable `RegisterStore` snapshot (`RegisterStoreProvider`), test-data merging, static role definitions, rate limiter, signing keys from the shared test certificate, localtest-only and Profile controllers, DI composition |
| `testdata/testData.json` | Seed: the old localtest's built-in users with valid synthetic identifiers (see [`testdata/README.md`](testdata/README.md) for the conversion, the old→new identifier mapping and the schema v2 fields) |
| `smoke-test.sh` | Curl checks against a running register (and the authentication component for a user token), with a stub app for registration |
| `Dockerfile` | Mirrors the upstream `vendor/Dockerfile` (same pinned Alpine images, build and publish, tzdata, non-root `dotnet` user); each difference is marked `localtest:` |

Directive sites in `vendor/`: one, in `RegisterHost.cs` (`AddLocaltestRegister(builder)` after the production
persistence block). Everything else is replaced through DI or switched off by configuration
(`Altinn:IsTest=true` skips MassTransit, jobs, leases and CCR federation; `Altinn:register:ApiSource:Default=DB`
makes the v1 endpoints answer from persistence). These defaults are added inside `RegisterHost`, after `AltinnHost`
has read its own settings, so ServiceDefaults runs in environment `UNKNOWN` (not `LOCAL`) with the descriptor's
`IsTest` off. That is intended: `LOCAL` would get platform tokens from the remote test token generator instead of
signing them with the shared certificate. A setting ServiceDefaults must see has to come from the environment.

## Endpoints added by the fakes

Localtest-only (anonymous), under `register/api/localtest/`:

| Endpoint | What |
| --- | --- |
| `GET test-users` | Every user: ids, `ssn` (`""` for self-identified), `displayName`, `userUuid`, `partyType`, `profile` (`language`, `preSelectedPartyId`, `doNotPromptForParty`, `userType`, `userTypeName`), `customClaims`, `selfRoleCodes`, `parties` and `actingFor` (below) |
| `GET organizations` | Every organisation with `parentPartyId` and `unitType` (a missing `unitType` in a testData.json becomes `AS`, `BEDR` for sub-units, with a load warning: the production Register always has one and Access Management's import rejects an organisation without it) |
| `GET apps` | Registered apps, their test-data status (`TestData`, `NoTestData`), merge conflicts and load warnings |
| `PUT apps/{org}/{app}` with the app's `testData.json` as the body (empty body: no test data) | Register an app with the posted test data (replaces earlier data) and rebuild; a body that does not parse is a 400 and changes nothing |
| `DELETE apps/{org}/{app}` | Unregister and rebuild |
| `POST apps/refresh` | Re-read the seed and rebuild with the posted app data |

Per user in `test-users`:

- `parties`: the parties the user holds something for directly. The **own party comes first** (`orgNumber` null,
  `roleCodes` = the codes on oneself, such as `regna`, `dagl`, `priv`, `seln`), then other parties with `roles`
  (CCR identifiers such as `daglig-leder`), `roleCodes` (Altinn codes verbatim as in the seed: `DAGL`, `REGNA`,
  `A0239`, ...; a CCR identifier given in the seed shows up as its code), `accessPackages` and `unitType`. Derived
  sub-units are not listed here.
- `actingFor`: everything the user may act for, as the old `authorization/partylist` defined it: self, the parties
  above, then the sub-units of those organisations (`parentPartyId` set, empty role lists when the roles are only held
  for the main unit). Each entry has `partyType` (`person`, `organization`, `self-identified`), `orgNumber`/`ssn`,
  `unitType`.

Profile (`profile/api/v1/`, route the `/profile` prefix to this container). Responses have the
`Altinn.Platform.Profile.Models.UserProfile` shape: `userId`, `userUuid` (the party UUID), `userName`, `phoneNumber`,
`email`, `partyId`, `party` (the register's own v1 party, so `party.person` / `party.name` match
`register/api/v1/parties`), `userType` (enum number), `profileSettingPreference` (`language`, `preSelectedPartyId`,
`doNotPromptForParty`).

Authorization follows Profile's own controllers: a user or org token (`AltinnStudioRuntime` cookie or bearer,
validated like every other token) on every `users` route, and a platform access token (`PlatformAccessToken` header,
checked against the shared certificate) on the lookups apps make, which app-lib always sends.

| Endpoint | Auth |
| --- | --- |
| `GET users/{userId}` | user token and platform access token |
| `GET users/byuuid/{userUuid}` | user token and platform access token |
| `POST users`, body `"<ssn>"` (what app-lib sends) or `{ "ssn" \| "userId" \| "userUuid" \| "username": ... }` | user token and platform access token |
| `POST internal/user`, same body (what Authentication, Authorization and Access Management call) | anonymous, as Profile's internal controller |
| `GET users/current` | user token; user from `urn:altinn:userid`. Profile's feature-toggled `altinn:portal/enduser` scope check is not modelled |

## App test data

The app runner (`../../run-app.sh`) registers an app with `PUT register/api/localtest/apps/{org}/{app}` and the app's
`App/wwwroot/testData.json` as the body, as is (an empty body registers an app without test data). The register
fetches nothing from apps. On every registration, unregistration and refresh it builds a new `RegisterStore` from the
built-in seed and the posted data, which replaces the current one atomically. Merge rule, as the old localtest: if
every registered app posted test data, only the apps' users exist; otherwise the apps' users are added to the seed.
Identical duplicates (same JSON) are fine; an entry that reuses a user id, party id, fødselsnummer or organisation
number with a different definition is skipped (first definition wins) and listed under `conflicts`.

| Setting | Default | |
| --- | --- | --- |
| `Localtest:Register:TestDataPath` | `/testdata/testData.json` | Seed file |

## Self-identified users

A person without `ssn` is a self-identified user as ID-porten registers them today: identified by `email`, no
username, type `idporten-email`, external URN `urn:altinn:person:idporten-email:<email>`, display name = the e-mail
(what `register.get_or_create_email_user` does in production). The v1 party `name` is therefore the e-mail (or
`epost:<email>` when the lookup includes the self-identified fields, as production's mapper does); apps show that.
Legacy username users are not modelled; a person without `ssn` and without `email` is skipped with a warning.

Run locally without Docker:

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5020 \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath=$PWD/../../certs/localtest-accesstoken.pfx \
  Localtest__Register__TestDataPath=$PWD/testdata/testData.json \
  dotnet run --no-launch-profile --project vendor/src/apps/Altinn.Register/src/Altinn.Register
AUTH=http://local.altinn.cloud:8000 ./smoke-test.sh http://127.0.0.1:5020   # AUTH: the authentication component, for a user token
# InternalOrPlatformAccess: either a platform access token signed with ../../certs/localtest-accesstoken.pfx
# (set AccessTokenSettings__AccessTokenSigningCertificateFullPath on the register too), or a bearer token with the
# scope altinn:register/partylookup.admin, e.g. from the authentication component's /Home/GetTestOrgToken?scopes=...
curl -H "Authorization: Bearer $TOKEN" -H Content-Type:application/json \
  -X POST 'http://127.0.0.1:5020/register/api/v1/parties/partylist?fetchSubUnits=true' -d '[500000]'
```
