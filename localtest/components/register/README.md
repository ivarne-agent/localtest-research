# register

Production Altinn Register (`vendor/`, see `UPSTREAM.md`) running on an in-memory register seeded from
`testdata/testData.json` plus the `testData.json` of registered apps. It also answers the Profile user endpoints.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except `#if LOCALTEST` blocks (`git diff` against the baseline commit shows them all) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, link the fakes project |
| `fakes/Altinn.Register.Localtest/` | In-memory `IPartyPersistence`, `IPartyExternalRolePersistence` over a swappable `RegisterStore` snapshot (`RegisterStoreProvider`), test-data merging, static role definitions, rate limiter, lenient platform-token handler, localtest-only and Profile controllers, DI composition |
| `testdata/testData.json` | Seed: the old localtest's built-in users with valid synthetic identifiers (see [`testdata/README.md`](testdata/README.md) for the conversion, the old→new identifier mapping and the schema v2 fields) |
| `smoke-test.sh` | Curl checks against a running register, with a stub app for registration |
| `Dockerfile` | Publishes the vendored host with the overrides |

Directive sites in `vendor/`: one, in `RegisterHost.cs` (`AddLocaltestRegister(builder)` after the production
persistence block). Everything else is replaced through DI or switched off by configuration
(`Altinn:IsTest=true` skips MassTransit, jobs, leases and CCR federation; `Altinn:register:ApiSource:Default=DB`
makes the v1 endpoints answer from persistence).

## Endpoints added by the fakes

Localtest-only (anonymous), under `register/api/localtest/`:

| Endpoint | What |
| --- | --- |
| `GET test-users` | Every user: ids, `ssn` (`""` for self-identified), `displayName`, `userUuid`, `partyType`, `profile` (`language`, `preSelectedPartyId`, `doNotPromptForParty`, `userType`, `userTypeName`), `customClaims`, `selfRoleCodes`, `parties` and `actingFor` (below) |
| `GET organizations` | Every organisation with `parentPartyId` and `unitType` |
| `GET apps` | Registered apps, their test-data status (`TestData`, `NoTestData`, `Unreachable`, `Stale`, `Pending`), merge conflicts and load warnings |
| `POST apps` `{ "org": "ttd", "app": "frontend-test" }` | Register an app (idempotent), re-fetch all apps' test data and rebuild |
| `DELETE apps/{org}/{app}` | Unregister and rebuild |
| `POST apps/refresh` | Re-read the seed, re-fetch all apps' test data and rebuild |

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

| Endpoint | Auth |
| --- | --- |
| `GET users/{userId}` | anonymous |
| `GET users/byuuid/{userUuid}` | anonymous |
| `POST users`, body `"<ssn>"` (what app-lib sends) or `{ "ssn" \| "userId" \| "userUuid" \| "username": ... }` | anonymous |
| `POST internal/user`, same body (what Authentication calls) | anonymous |
| `GET users/current` | `AltinnStudioRuntime` cookie or bearer, validated like every other user token; user from `urn:altinn:userid` |

## App test data

`POST register/api/localtest/apps` registers an app; the register then fetches
`{Localtest:Register:AppsBaseUrl}{org}/{app}/testData.json` (the app's `App/wwwroot/testData.json`; 404 means the
app has none) for every registered app and builds a new `RegisterStore`, which replaces the current one atomically.
Merge rule, as the old localtest: if every registered app serves test data, only the apps' users exist; otherwise the
apps' users are added to the seed. Identical duplicates (same JSON) are fine; an entry that reuses a user id, party
id, fødselsnummer or organisation number with a different definition is skipped (first definition wins) and listed
under `conflicts`. An app that cannot be reached keeps the data from its last successful fetch (`Stale`).

| Setting | Default | |
| --- | --- | --- |
| `Localtest:Register:TestDataPath` | `/testdata/testData.json` | Seed file |
| `Localtest:Register:AppsBaseUrl` | `http://local.altinn.cloud:8000/` | Where apps are reached |
| `Localtest:Register:Apps` | (none) | Comma list `org/app,...` registered in the background at startup |

## Self-identified users

A person without `ssn` becomes a `SelfIdentifiedUserRecord` (`Legacy` type, external URN
`urn:altinn:person:legacy-selfidentified:<userName>`). The user name is kept out of the record's user names because
the v1 mapping names a self-identified party after its user name, and apps show the party name ("Selvregistrert
Bruker"); `usernames` lookups still find it through the test data.

Run locally without Docker:

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5020 \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath=$PWD/../../certs/localtest-accesstoken.pfx \
  Localtest__Register__TestDataPath=$PWD/testdata/testData.json \
  Localtest__Register__AppsBaseUrl=http://127.0.0.1:18093/ \
  dotnet run --no-launch-profile --project vendor/src/apps/Altinn.Register/src/Altinn.Register
./smoke-test.sh http://127.0.0.1:5020 18093
# InternalOrPlatformAccess: either a platform access token signed with ../../certs/localtest-accesstoken.pfx
# (set AccessTokenSettings__AccessTokenSigningCertificateFullPath on the register too), or a bearer token with the
# scope altinn:register/partylookup.admin, e.g. from the authentication component's /Home/GetTestOrgToken?scopes=...
curl -H "Authorization: Bearer $TOKEN" -H Content-Type:application/json \
  -X POST 'http://127.0.0.1:5020/register/api/v1/parties/partylist?fetchSubUnits=true' -d '[500000]'
```
