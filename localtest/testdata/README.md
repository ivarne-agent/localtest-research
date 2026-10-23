# Test data (`testData.json`)

The format of the test users, organisations and systems the localtest stack runs with. The same format serves two kinds
of file:

- the built-in seed, [`components/register/testdata/testData.json`](../components/register/testdata/testData.json)
  (how it was made and its identifier mapping: [`components/register/testdata/README.md`](../components/register/testdata/README.md));
- every app's `App/wwwroot/testData.json`, which the app runner posts to the Register
  ([`../README.md`](../README.md#what-the-app-runner-pushes), step 1).

| File | What |
| --- | --- |
| `README.md` | This reference |
| [`test-users.schema.v3.json`](test-users.schema.v3.json) | JSON Schema (2020-12) for both kinds of file, with titles and descriptions for editors |

Only the Register component reads the file. Every other component sees the data through the Register's APIs, the way
production components see the population register, the CCR and Profile:

| Reader | Through | Gets |
| --- | --- | --- |
| Register | the file | parties (persons, self-identified users, organisations, system users), CCR role assignments, sub-units |
| Profile (production Profile, `components/profile/`, since task 14) | `test-users`, `organizations` and the KRR stand-in, through its KRR and brreg sync jobs and a settings seed | persons' e-mail, mobile number and reservation (KRR mirror), preferences (once per user), organisations' official addresses (once per organisation); names and ids live from the Register |
| Authentication | Profile `internal/user`, Register | the signed-in user (user id, `pid`, party) |
| Login picker (localtest app) | `register/api/localtest/test-users` | users, their parties |
| Access Management | Register streams (production import), `test-users` (its refresh) | parties and CCR roles; Altinn 2 role codes and access packages |
| Authorization (production PDP) | Access Management, Register, Profile | roles, access packages, parties (no file of its own) |
| Notifications | Profile contact point lookups | persons' e-mail, mobile number and reservation, organisations' official addresses; for an organisation also the addresses users registered for it in Profile (none from the test data) |
| Correspondence | Profile `correspondence/` lookups, KRR stand-in | as Notifications; persons' reservation straight from the KRR stand-in |

## Pointing at the schema

```json
{ "$schema": "../../../testdata/test-users.schema.v3.json", "persons": [], "orgs": [] }
```

- The seed uses the **relative path** above: it resolves in an editor opened on this repository and always matches the
  schema of the same commit. The loader ignores `$schema`.
- An app's file, outside this repository, uses the **raw GitHub URL**
  `https://raw.githubusercontent.com/ivarne-agent/localtest-research/main/localtest/testdata/test-users.schema.v3.json`
  (the repository is public). A relative path would depend on where the app is checked out.
- The schema's `$id` is `https://altinncdn.no/schemas/json/test-users/test-users.schema.v3.json`, the name it would get
  in the published line (v1 is there). Nothing is published to the CDN from here, and that URL answers 404 today; it is an
  identifier, not a location.

Validate files (any JSON Schema 2020-12 validator; with Python):

```sh
python3 -m venv /tmp/venv && /tmp/venv/bin/pip install jsonschema
/tmp/venv/bin/python -c 'import json,sys,jsonschema as j; s=json.load(open(sys.argv[1])); v=j.Draft202012Validator(s, format_checker=j.FormatChecker()); [print(f, "/".join(map(str, e.absolute_path)), e.message) for f in sys.argv[2:] for e in v.iter_errors(json.load(open(f, encoding="utf-8-sig")))]' \
  localtest/testdata/test-users.schema.v3.json localtest/components/register/testdata/testData.json
```

## Strictness: schema and loader

The schema is stricter than the loader, on purpose: it is what an editor checks while a tester writes the file.

- **Unknown properties**: the schema rejects them everywhere (`additionalProperties: false`, as v1 did), so a misspelt
  field is flagged. The loader (`System.Text.Json`, web defaults) ignores unknown properties and matches names
  case-insensitively.
- **`null`**: every optional field accepts `null`, which means the same as leaving it out. Exports from the old localtest
  (its Tenor page, `DebugUsers`) write `null` for every empty field, and the loader reads a `null` list as empty. The
  required fields (`persons`, `orgs`, and those listed below) are not nullable.
- **Identifiers**: patterns for national identity numbers (11 digits, or empty for self-identified), organisation numbers
  (9 digits), party UUIDs and mobile numbers (`+`, 8 to 15 digits). The Register additionally checks the mod-11 control
  digits and skips a person or organisation that fails, with a load warning (`GET register/api/localtest/apps`). It
  does not check mobile numbers or e-mail addresses.
- **Uniqueness** (user ids, party ids, national identity numbers, organisation numbers) cannot be expressed in the
  schema; the Register reports clashes as merge conflicts (below).
- Nothing is required that v1 did not require.

Every field may be set in an app's file as well as in the seed; there is no seed-only field.

## Top level

| Field | Type | Default | Required | Notes |
| --- | --- | --- | --- | --- |
| `$schema` | string | | | Ignored by the loader |
| `persons` | array of person | | yes | |
| `orgs` | array of organisation | | yes | |
| `systems` | array of system, or null | `[]` | | |

## Persons

A person with an 11-digit `ssn` is a person from the national population register. A person with `"ssn": ""` is a
self-identified user as ID-porten registers them today: identified by `email` (`urn:altinn:person:idporten-email:<email>`,
lower-cased; also the display name), no names, no user name. A self-identified user without `email` is skipped with a
warning. Legacy username self-identified users are not modelled.

| Field | Type | Default | Since | Read by |
| --- | --- | --- | --- | --- |
| `ssn` (required) | string, 11 digits or `""` | | v1 | Register (person identifier, date of birth); Profile lookups by national identity number; Authentication (`pid`); Notifications' person recipients; KRR stand-in |
| `userId` (required) | integer ≥ 1 | | v1 | Register (user id); Profile; login picker; tokens' `urn:altinn:userid`; Access Management refresh |
| `partyId` (required) | integer ≥ 1 | | v1 | Register; everything that names a party. Cypress depends on the `userId.partyId` pairs |
| `partyUuid` | UUID | party id zero-padded into a UUID | v1 | Register; Access Management; Profile `userUuid` |
| `firstName`, `middleName`, `lastName` | string | `lastName` `""` | v1 | Register (name parts and display name = first, middle, last joined); Profile `party`; person lookups match `lastName` |
| `userName` | string | none | v1 | Register (user name); Profile `userName` and lookup by user name |
| `email` | string, e-mail | none | v1 | KRR stand-in, and through it Profile's KRR mirror: Profile `email` and the contact point lookups (Notifications); the identity of a self-identified user |
| `language` | `nb`, `nn`, `en` | `nb` | v1 | Profile `profileSettingPreference.language` (seeded once per user; a user's own change wins) |
| `preSelectedPartyId` | integer | `0` | v2 | Profile `profileSettingPreference.preSelectedPartyId` (as above, stored by the party's UUID) |
| `doNotPromptForParty` | boolean | `false` | v2 | Profile `profileSettingPreference.doNotPromptForParty` (as above) |
| `telephoneNumber` | string | none | v1 | Register; the KRR stand-in's mobile number when `mobileNumber` is absent |
| `mobileNumber` | string, `+` and 8 to 15 digits | none | v1, form tightened in v3 | KRR stand-in, and through it Profile's KRR mirror: `phoneNumber` in user profiles, the contact point lookups (Notifications' SMS) |
| `reserved` | boolean | `false` | v3 | `test-users`; the KRR stand-in's `reservasjon` (Correspondence), and through it Profile's KRR mirror: `isReserved` in `users/contactpoint/lookup` (Notifications) |
| `addressStreetName`, `addressHouseNumber`, `addressHouseLetter`, `addressPostalCode`, `addressCity`, `addressMunicipalNumber`, `addressMunicipalName` | string | none | v1 | Register (residential address) |
| `mailingAddress`, `mailingPostalCode`, `mailingPostalCity` | string | none | v1 | Register (mailing address) |
| `partyRoles` (required) | object: party id → array of role | | v1 | Register, Access Management, login picker (below). Not Profile's `units/contactpoint/lookup` since task 14: production answers only addresses users registered for the organisation |
| `accessPackages` | object: party id → array of package | `{}` | v2 | `test-users` → Access Management refresh → PDP |
| `customClaims` | array of claim | `[]` | v1 | `test-users` only; no component adds them to tokens yet |
| `profile` | object, no fields | | v3 (reserved) | nothing (below) |

The Profile `userType` is not a field: a person with `ssn` is `SSNIdentified` (1), one without is `SelfIdentified` (2).

### Mobile numbers

Write mobile numbers in full international form, `+4799315829`. Production Notifications answers `400` to a direct SMS
recipient without a country code, and for numbers it gets from Profile adds `+47` only to eight-digit numbers starting
with 4 or 9, then validates with libphonenumber; anything else is dropped (`Failed_RecipientNotIdentified`). The
Register returns numbers exactly as written, so a number that would not reach a phone in production does not reach the
localtest inboxes either. The schema checks the form, not validity: `+4787654321` passes the pattern but is not a valid
Norwegian number for libphonenumber (Norwegian mobile numbers start with 4 or 9).

### `reserved`

Reservation in KRR, the contact and reservation register, against digital communication from the public sector.
Production Correspondence checks every person recipient against KRR; the Register's KRR stand-in
(`register/api/localtest/krr/rest/v2/personer`) answers `reservasjon: "JA"` for a person with this flag and `"NEI"` for
everyone else. Correspondence then refuses a correspondence whose only recipient is reserved (`422`, `CORR-01030`), gives a
reserved recipient among several the status `Reserved`, and sends anyway with `ignoreReservation`. Profile's `isReserved` in `users/contactpoint/lookup` is this flag: production
Profile's KRR sync copies the stand-in's answer into its KRR mirror (every minute, or at once with `POST
profile/api/localtest/sync`), as it does from KRR in production, and the person's e-mail and mobile number are still returned. Production Notifications then
accepts an order to the person (a reserved person is not "missing contact information") and ends the notification
`Failed_RecipientReserved` without sending anything, unless the order sets `ignoreReservation`. The flag means nothing
for a self-identified user.

### `partyRoles`

Keys are party ids (as strings); values are roles `{ "type": "altinn", "value": "<code>" }` (`type` is required by
the schema as in v1 and not read). The key may be:

- an **organisation**: CCR codes (`DAGL`, `LEDE`, `MEDL`, `INNH`, `REGN`, `REVI`) or CCR identifiers (`daglig-leder`)
  become Register external role assignments (and, through the Register's streams, Access Management roles); every code,
  CCR or not (`REGNA`, `A0239`, ...), is passed verbatim to Access Management as an Altinn 2 role code. The sub-units
  of the organisation follow (`parentPartyId`);
- the person's **own party**: codes such as `PRIV`, `REGNA`, `DAGL` that policies grant on; listed first under
  `parties` in `test-users`;
- an empty list: the person may act for the party without any role (as the old `authorization/partylist`).

A key that is not a loaded party is skipped with a warning.

### `accessPackages`

Keys are party ids of the organisations the packages are held for; values are package identifiers
(`regnskapsforer-med-signeringsrettighet`) or URNs (`urn:altinn:accesspackage:lonn`). The Register does not use them;
Access Management's refresh assigns them and the production PDP reads them from there. An unknown package is reported
by the refresh.

### `customClaims`

`{ "type", "value", "valueType" }`, all required (v1). Kept from the old localtest, which added them to the user's
token. Exposed in `test-users`; no component of the new stack uses them yet.

### `profile` (reserved)

Task 14 vendored production Profile and seeds only what the fields above carry. Favourites, party groups, professional
notification settings and verified addresses are not seeded; testers create them through Arbeidsflate or Profile's API,
and they persist in Profile's database. Fields for them would go in this object on the person. Until then only `{}`
validates and the loader ignores it. The existing preferences (`language`, `preSelectedPartyId`,
`doNotPromptForParty`) stay at the top level, where v1 and v2 put them.

## Organisations

| Field | Type | Default | Since | Read by |
| --- | --- | --- | --- | --- |
| `partyId` (required) | integer ≥ 1 | | v1 | Register; party lists; Cypress (`500000`) |
| `partyUuid` | UUID | party id zero-padded into a UUID | v1 | Register; Access Management |
| `orgNumber` (required) | string, 9 digits | | v1 | Register (organisation identifier); Profile lookups; system users' owner |
| `parentPartyId` | integer | none (main unit) | v1 | Register: the sub-unit gets the CCR `hovedenhet` role to its main unit, so `partylist?fetchSubUnits`, `actingFor` and Access Management see it; Profile falls back to the main unit's notification addresses |
| `name` | string | the organisation number | v1 | Register (display name) |
| `unitType` | string | `AS`, or `BEDR` for a sub-unit, with a warning | v1 | Register; Access Management's import rejects an organisation without one |
| `unitStatus` | string | `N` | v1 | Register |
| `businessAddress`, `businessPostalCode`, `businessPostalCity` | string | none | v1 | Register (business address) |
| `mailingAddress`, `mailingPostalCode`, `mailingPostalCity` | string | none | v1 | Register (mailing address) |
| `eMailAddress` | string, e-mail | none | v1 | Register (CCR e-mail). Not a notification address |
| `internetAddress`, `telephoneNumber`, `faxNumber` | string | none | v1 | Register |
| `mobileNumber` | string, `+` and 8 to 15 digits | none | v1, form tightened in v3 | Register (CCR mobile). Not a notification address |
| `notificationAddresses` | array of address | `[]` | v3 | `register/api/localtest/organizations`, from which Profile seeds its mirror of official addresses once per organisation: `organizations/notificationaddresses/lookup` (Notifications, and Correspondence's `correspondence/` variant) |

A sub-unit is an organisation with `parentPartyId`, conventionally with `unitType` `BEDR`. Roles held for the main unit
reach its sub-units the way production derives them; roles can also be given for the sub-unit directly.

### `notificationAddresses`

The organisation's official notification addresses ("varslingsadresser" in the contact register for organisations), what
Notifications sends to when an order names an organisation. Each entry is either an e-mail address or a mobile number:

```json
"notificationAddresses": [
  { "email": "varsling@ddgfitness.no" },
  { "mobileNumber": "+4799500000" }
]
```

Why this shape: production Profile keeps one record per address, each typed e-mail or SMS, with the full address
(country code and number for SMS); its lookup splits them into `emailList` and `mobileNumberList`. One entry per address
keeps that model (and the order), and the full international number is the same form as `mobileNumber` everywhere else
in the file, instead of Profile's write API's separate `phone` and `countryCode`.

What the lookup answers, as `OrgNotificationAddressController` does: one entry per requested organisation that has
addresses, `{ organizationNumber, addressOrigin, partyId: 0, mobileNumberList, emailList }`. A sub-unit without addresses
gets its main unit's, with `addressOrigin` set to the main unit's number; an organisation without addresses either way
is left out. Without addresses, an order to the organisation alone is rejected at registration (`422 NOT-00001`), as in
production: Notifications adds users' own contact points for an organisation only while processing an order.

## Systems

| Field | Type | Default | Read by |
| --- | --- | --- | --- |
| `id` (required) | string, conventionally `<organisation number>_<name>` | | kept for reference |
| `name` (required) | string | | Register (system users' display name) |
| `systemUsers` (required) | array | | |
| `systemUsers[].id` (required) | UUID | | Register: a system-user party with this UUID (`urn:altinn:systemuser:uuid`), party ids from 900000000 |
| `systemUsers[].orgNumber` (required) | 9 digits | | Register: the owner; an unknown organisation skips the system user with a warning |
| `systemUsers[].actions` (required) | array of string | | not read by the new stack (the old localtest's system user tokens) |

## Seed and app files together

When an app is registered with the front door, the localtest container posts the app's file as is (provisioning step 1,
`PUT register/api/localtest/apps/{org}/{app}`; an empty body registers an app without test data; see
[What the app runner pushes](../README.md#what-the-app-runner-pushes)). On every registration, unregistration and refresh the Register builds a new store from the seed and
the posted files ([`components/register/README.md`](../components/register/README.md#app-test-data)):

- if **every** registered app posted test data, only the apps' data is loaded (the seed is left out);
- otherwise the apps' data is added to the seed, the seed first.

Entries are merged by identifier: persons by `userId`, `partyId` and `ssn`; organisations by `partyId` and `orgNumber`;
systems by `id`; a `partyId` is unique across persons and organisations. An identical duplicate (same JSON after
loading) is fine. An entry that reuses an identifier with a different definition is skipped, the first definition wins,
and the clash is listed under `conflicts` of `GET register/api/localtest/apps`. An app that copied a seed user from an
older seed (for example Sophie Salt with the old bare mobile number) therefore gets a conflict for that user in the
seed-and-apps mode, and the seed's definition is used.

## Regenerating the seed

```sh
python3 -I localtest/components/register/testdata/generate-testdata.py /home/agent/code/altinn-studio/src/Runtime/localtest/testdata
```

The script converts the old localtest's built-in data and applies the tables at its top (unit types, the self-identified
e-mail user, access packages, mobile numbers, the reserved person, notification addresses). It is deterministic: a run
reproduces the committed file byte for byte, so `git diff` after a run shows only intended changes.

## Verified

2026-10-21, on main 902ddfc with this branch on top, `studioctl env up -r localtest-v2 --dev register` (the other
components from the pinned `sha-3c9b03f` images; Notifications unchanged):

- `components/register/smoke-test.sh`: 27 of 27 (reserved only on 1003, organisations' addresses, `isReserved` for 1003,
  the organisation lookup with its main-unit fallback and left-out organisations).
- `components/notifications/smoke-test.sh`: 64 of 64, twice: Sophie Salt's e-mail and SMS to `+4799001337`, Rik
  Forelder's order accepted and failed `Email_Failed_RecipientReserved` with nothing captured (and delivered with
  `ignoreReservation`), DDG Fitness's e-mail and SMS delivered to its official addresses, DDG Fitness Oslo through the
  main-unit fallback, `422 NOT-00001` for EAS Health Consulting.
- Root `smoke-test.sh` 23 of 23, `components/authorization/smoke-test.sh` 30 of 30 (Access Management has no smoke test
  of its own; the PDP checks go through it).
- `run-platform-regression.sh --no-apps`: 44 of 44 Cypress tests in the six specs, no retries (apps with `studioctl run
  --dev-frontend`, the monorepo's built frontend served on 8080 with `Access-Control-Allow-Origin: *`).
  `signering-brukerstyrt` registered its v1 file with nulls; the Register loaded it with three `unitType` warnings and
  no conflicts.

Not verified here: Correspondence and task 7's KRR stand-in reading `reserved` (the branch is not merged).

Superseded 2026-10-21: task 7 merged and its smoke test checks the reserved person (`422 CORR-01030` alone, `Reserved`
beside another recipient; `../components/correspondence/README.md`). Task 14 moved the Profile checks out of the
Register's smoke test, which now has 14 checks; they are in `../components/profile/smoke-test.sh` (52), and Profile's
`isReserved` comes from its KRR sync of the stand-in instead of the Register's Profile fakes.

## History

| Version | Where | What |
| --- | --- | --- |
| v1 | Published: `https://altinncdn.no/schemas/json/test-users/test-users.schema.v1.json`; source [`Altinn/altinn-cdn` `schemas/json/test-users/test-users.schema.v1.json`](https://github.com/Altinn/altinn-cdn/blob/master/schemas/json/test-users/test-users.schema.v1.json) (added 2023-01 in altinn-cdn#118; `partyUuid` and `systems` 2025-04 in #204; `systems` optional 2025-06 in #205) | The old localtest's format: persons, orgs, systems; no draft declared, strict (`additionalProperties: false`), no `null`s |
| v2 | Never published; the URL `…/test-users.schema.v2.json` answers 404. Only this repository's seed declared it | v1 plus `accessPackages`, `preSelectedPartyId`, `doNotPromptForParty` on persons; in practice also explicit `unitType` and self-identified users as e-mail registrations |
| v3 | This folder | v2 plus `reserved` on persons, `notificationAddresses` on organisations, mobile numbers in international form, the reserved `profile` object; `null` allowed for optional fields; patterns and formats for identifiers; JSON Schema 2020-12 with titles and descriptions |

Who uses the v1 URL today: the old localtest writes it as `$schema` in the files it exports (`AppTestDataModel`, its
Tenor and `DebugUsers` pages), the docs' test user pages (`altinn-studio-docs`, v8 and v9) link it and declare it in their
example `testData.json`, and test apps declare it (`src/test/apps/signering-brukerstyrt` in altinn-studio). Nothing
validates against it at run time: the old localtest deserialises leniently, app-lib and Designer do not read
`testData.json`, and studioctl's v8→v9 `$schema` migration deliberately leaves `testData.json` alone. It is an editor
aid only, and so is v3.

### What fails against v3

- **v1 files**: everything v1 accepted is accepted, except mobile numbers without `+` (the old localtest's
  `87654321`-style numbers). Files with `null`s that v1 rejected (old localtest exports) pass v3.
- **The docs' example** (altinn-studio-docs v9 `testusers/testData.json`, a copy of the old built-in data): bare mobile
  numbers, and `"partyId": -1` on Kari Consulting, the old localtest's marker for an organisation without a party (party
  ids are positive here). The file's old identifiers also fail the Register's mod-11 checks, which the schema cannot see;
  the seed's README maps them to valid ones.
- **The v2 seed**: only its bare mobile numbers.
