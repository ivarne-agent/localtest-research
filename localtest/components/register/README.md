# register

Production Altinn Register (`vendor/`, see `UPSTREAM.md`) running on an in-memory register seeded from
`testdata/testData.json` plus the `testData.json` of registered apps. Until task 14 it also answered Profile's
endpoints; production Profile is now its own component ([`../profile/`](../profile/README.md)), which reads the test
data through this component's localtest API (`test-users`, `organizations` and the KRR stand-in).

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except `#if LOCALTEST` blocks (`git diff` against the baseline commit shows them all); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, link the fakes project |
| `fakes/Altinn.Register.Localtest/` | In-memory `IPartyPersistence`, `IPartyExternalRolePersistence` over a swappable `RegisterStore` snapshot (`RegisterStoreProvider`), test-data merging, static role definitions, rate limiter, signing keys from the shared test certificate, the localtest-only controllers, DI composition |
| `testdata/testData.json` | Seed: the old localtest's built-in users with valid synthetic identifiers (see [`testdata/README.md`](testdata/README.md) for the conversion, the old→new identifier mapping and what v3 added). The format, shared with apps' `testData.json`, is documented in [`../../testdata/`](../../testdata/README.md) with its JSON Schema (v3) |
| `smoke-test.sh` | Curl checks against a running register, with a stub app for registration: the seed, the party list, the KRR stand-in (14 checks; the Profile checks moved to `../profile/smoke-test.sh`) |
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
| `GET test-users` | Every user: ids, `ssn` (`""` for self-identified), `displayName`, `userUuid`, `partyType`, `profile` (`language`, `preSelectedPartyId`, `doNotPromptForParty`, `userType`, `userTypeName`), `customClaims`, `selfRoleCodes`, `parties` and `actingFor` (below), `reserved` (the test data's KRR reservation, default false) |
| `GET organizations` | Every organisation (what the Profile component seeds organisations' official notification addresses from) with `parentPartyId`, `unitType` (a missing `unitType` in a testData.json becomes `AS`, `BEDR` for sub-units, with a load warning: the production Register always has one and Access Management's import rejects an organisation without it) and `notificationAddresses` (`{ email, mobileNumber }`, one of them set, as in the test data) |
| `GET apps` | Registered apps, their test-data status (`TestData`, `NoTestData`), merge conflicts and load warnings |
| `PUT apps/{org}/{app}` with the app's `testData.json` as the body (empty body: no test data) | Register an app with the posted test data (replaces earlier data) and rebuild; a body that does not parse is a 400 and changes nothing |
| `DELETE apps/{org}/{app}` | Unregister and rebuild |
| `POST apps/refresh` | Re-read the seed and rebuild with the posted app data |
| `POST krr/rest/v2/personer` | The KRR stand-in for Correspondence and Profile (below) |

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

`/profile` is routed to the Profile component. The three Profile controllers this component had
(`LocaltestProfileController`, `LocaltestProfileContactPointsController`, `LocaltestProfileCorrespondenceController`:
users, the four contact point lookups of Notifications, the two `correspondence/` lookups) were deleted in task 14;
production Profile serves those routes with its own semantics (see [`../profile/README.md`](../profile/README.md)).

## KRR stand-in

`POST register/api/localtest/krr/rest/v2/personer` (`LocaltestKrrController`, anonymous) answers in the shape of the contact
and reservation register (`kontaktregisteret.no`) for Altinn Correspondence's production client, which checks every person
recipient against it and fails without an answer (`GeneralSettings:ContactReservationRegistryBaseUrl` points here), and for
the Profile component's KRR change log, which turns its answer for every test person into the change log production
Profile's KRR sync reads (so Profile's `email`, `phoneNumber` and `isReserved` come from here). Body
`{ "personidentifikatorer": [...] }`; answer `{ "personer": [{ personidentifikator, reservasjon, status: "AKTIV",
varslingsstatus, kontaktinformasjon: { epostadresse, mobiltelefonnummer } }] }`, one entry per seeded person with the test
data's e-mail and number as stored. `reservasjon` is `"JA"` (and `varslingsstatus` `"KAN_IKKE_VARSLES"`) for a person with
`reserved: true` (Rik Forelder, 1003, in the seed), `"NEI"` (`"KAN_VARSLES"`) otherwise, which Profile's `isReserved`
becomes through its KRR sync; national identity numbers not in the test data get no entry, which the client reads as not reserved. Production KRR
wants a raw Maskinporten token with `krr:global/kontaktinformasjon.read`; nothing checks it here.

## Verified (schema v3)

Task 14 (2026-10-21, with production Profile on `/profile`): `smoke-test.sh` through the proxy 14 of 14.

Before task 14 (the Profile checks were still here), 2026-10-21, on main 902ddfc with this branch on top, `studioctl env up -r localtest-v2 --dev register` (the other
components from the pinned `sha-3c9b03f` images; Notifications unchanged): `smoke-test.sh` 27 of 27; Notifications'
smoke test 64 of 64 with person, reserved-person and organisation recipients resolved here; the platform regression 44 of
44. Details in [`../../testdata/README.md`](../../testdata/README.md#verified).

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
./smoke-test.sh http://127.0.0.1:5020
# InternalOrPlatformAccess: either a platform access token signed with ../../certs/localtest-accesstoken.pfx
# (set AccessTokenSettings__AccessTokenSigningCertificateFullPath on the register too), or a bearer token with the
# scope altinn:register/partylookup.admin, e.g. from the localtest app's /Home/GetTestOrgToken?scopes=...
curl -H "Authorization: Bearer $TOKEN" -H Content-Type:application/json \
  -X POST 'http://127.0.0.1:5020/register/api/v1/parties/partylist?fetchSubUnits=true' -d '[500000]'
```
