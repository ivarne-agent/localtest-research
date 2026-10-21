# Register seed

`testData.json` is the old localtest's built-in test data (`src/Runtime/localtest/testdata` in altinn-studio:
`Profile/User`, `Register/Party|Org|Person`, `authorization/partylist|roles|claims|systems`) converted to the
test-users format, with valid synthetic identifiers, then extended to schema v3. The format (every field, who reads it,
the JSON Schema) is documented in [`../../../testdata/`](../../../testdata/README.md). Regenerate the file with:

```sh
python3 -I generate-testdata.py /home/agent/code/altinn-studio/src/Runtime/localtest/testdata
```

The script is deterministic (a run reproduces the committed file byte for byte) and prints the mapping below. Hand edits
belong in its tables, not in the JSON.

## What was kept

- User ids, party ids, party UUIDs (`00000000-0000-0000-0000-000000<partyId>`), names, user names, e-mail,
  addresses and profile preferences are unchanged. The Cypress suite (`test/e2e/config/localtest.json`) relies on
  the `userId.partyId` pairs and display names.
- `partyRoles` are the old `authorization/roles/User_<userId>/party_<partyId>` files, codes verbatim, including
  roles on the person's **own** party (`regna`, `dagl`, `priv` for most persons, `seln` for the self-identified
  user, `PRIV`, `UTINN`, ... for Sophie Salt). Register turns the CCR codes for organisations (`DAGL`, `LEDE`, `MEDL`,
  `INNH`, `REGN`, `REVI`) into external role assignments; every code, CCR or not (`REGNA`, `LOPER`, `A0239`, ...), is
  exposed verbatim in `test-users` for the PDP fake's `urn:altinn:rolecode`.
- Parties a user could act for in `authorization/partylist` without any role file (Oslos Vakreste Borettslag for
  2001 and 2002) are kept as an empty role list (`"500700": []`).
- Sub-units: DDG Fitness 500000 has 500001-500003 (from the partylist `childParties`); EAS Health Consulting 500600
  has 500800-500802 (the old data only listed them side by side).
- The self-identified user 1004/510004 has `"ssn": ""`.
- `customClaims` (12345 and 1337) and the `913312465_sbs` system with its system user for 950474084.

Dropped: `Register/Org/910423185.json`, a second "EAS Health Consulting Svolvær" record without a party.
Added: Kari Consulting (900000001 in `Register/Org`, no party before) as party 500900. Organisation names are the
`Register/Party` names ("DDG Fitness", where the old partylist said "DDG Fitness AS").

## Identifier mapping

Persons get Tenor-style synthetic numbers (month + 80) with the old birth date; the individual number is the
nearest one (same century) that gives valid control digits. Numbers that already were valid synthetic numbers are
unchanged. Organisation numbers are the nearest valid mod-11 number that is not another organisation's old number.

| userId | partyId | name | old fødselsnummer | new fødselsnummer |
| --- | --- | --- | --- | --- |
| 1001 | 510001 | Pengelens Partner | 01899699552 | 01899699552 |
| 1002 | 510002 | Gjentagende Forelder | 17858296439 | 17858296439 |
| 1003 | 510003 | Rik Forelder | 08829698278 | 08829698278 |
| 1004 | 510004 | selvregistrert.bruker@example.com | (self-identified, e-mail registration) | - |
| 1337 | 501337 | Sophie Salt | 01039012345 | 01839012345 |
| 2001 | 512001 | MultiParty Prompt | 01899699001 | 01899699048 |
| 2002 | 512002 | Kari Nordvik | 12889898001 | 12889898044 |
| 12345 | 512345 | Ola Nordmann | 01017512345 | 01817512212 |

| partyId | name | old org number | new org number |
| --- | --- | --- | --- |
| 500000 | DDG Fitness | 897069650 | 897069644 |
| 500001 | DDG Fitness Oslo | 897069651 | 897069660 |
| 500002 | DDG Fitness Bergen | 897069652 | 897069652 (unchanged) |
| 500003 | DDG Fitness Trondheim | 897069653 | 897069636 |
| 500004 | Skog og Fjell Consulting | 043871668 | 043871668 (unchanged) |
| 500005 | Testdepartementet | 405003309 | 405003309 (unchanged) |
| 500006 | Digitaliseringsdirektoratet | 991825827 | 991825827 (unchanged) |
| 500600 | EAS Health Consulting | 897069631 | 897069628 |
| 500700 | Oslos Vakreste Borettslag | 950474084 | 950474084 (unchanged) |
| 500800 | EAS Health Consulting Sortland | 910457292 | 910457292 (unchanged) |
| 500801 | EAS Health Consulting Stokmarknes | 910471120 | 910471120 (unchanged) |
| 500802 | EAS Health Consulting Svolvær | 910423495 | 910423495 (unchanged) |
| 500900 | Kari Consulting | 900000001 | 900000006 |

## Schema v3 data

Field definitions are in [`../../../testdata/README.md`](../../../testdata/README.md); this is what the seed sets.

- **Mobile numbers** in international form: `+47` added to every number without a country code, digits kept
  (organisations: `9201000x` → `+479201000x`, Kari Consulting `1234578` → `+471234578`), except the old
  `87654321` of persons 1002, 1003, 1337 and 12345: `+4787654321` is not a valid Norwegian number for libphonenumber,
  so production Notifications would drop it. They get valid mobile numbers made from the user id: `+4799001002`,
  `+4799001003`, `+4799001337`, `+4799012345`. Fixed-line `telephoneNumber` and `faxNumber` are unchanged.
- **`reserved: true`** on Rik Forelder (1003, 08829698278) only. Nothing in the Cypress suite logs in as him or reads
  him from the stack (the person-lookup tests that name him intercept the lookup with canned answers), and he holds no
  role for an organisation, so his reservation cannot change an organisation recipient's result.
- **`notificationAddresses`** on DDG Fitness (500000, 897069644: `varsling@ddgfitness.no`, `+4799500000`) and
  Digitaliseringsdirektoratet (500006, 991825827: `varsling@digdir.no`, `+4799500006`): the organisations the
  Notifications smoke test and the Cypress suite use. DDG Fitness's sub-units (500001-500003) get its addresses through
  Profile's main-unit fallback. Every other organisation has none.

The Profile `userType` is not a field: it is set when the persons are loaded, `SSNIdentified` with an `ssn` and
`SelfIdentified` without, as Profile reports them, and listed under `profile` in `register/api/localtest/test-users`.
Since task 14 the Register serves no Profile endpoint; production Profile (`../../profile/`) has its own user types. A
`userType` in an app's `testData.json` is ignored.
