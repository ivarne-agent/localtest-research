# Register seed

`testData.json` is the old localtest's built-in test data (`src/Runtime/localtest/testdata` in altinn-studio:
`Profile/User`, `Register/Party|Org|Person`, `authorization/partylist|roles|claims|systems`) converted to the
test-users format, with valid synthetic identifiers. Regenerate it with:

```sh
python3 -I generate-testdata.py /home/agent/code/altinn-studio/src/Runtime/localtest/testdata
```

The script is deterministic and prints the mapping below.

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
- The self-identified user 1004/510004 has `"ssn": ""` and `userType: "SelfIdentified"`.
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
| 1004 | 510004 | Selvregistrert Bruker | (self-identified) | - |
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

## Schema v2 fields (persons)

| Field | Meaning |
| --- | --- |
| `language` | Profile language (`nb`, `nn`, `en`); default `nb` (v1 already had it) |
| `preSelectedPartyId` | Profile: party preselected in the party picker; default 0 |
| `doNotPromptForParty` | Profile: skip the party picker; default false |
| `userType` | `Altinn.Platform.Profile.Enums.UserType` name or number; default `SSNIdentified` with `ssn`, `SelfIdentified` without |
| `accessPackages` | Access packages per party id (see the component README) |
