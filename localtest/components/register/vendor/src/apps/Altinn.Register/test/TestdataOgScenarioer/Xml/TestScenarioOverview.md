# Test Scenarios — CCR XML

Each `ScenarioN.xml` is a `batchAjourholdXML` document (the format the CCR
flat-file processor emits per organization). For a detailed description of
the XML format itself — element catalog, field semantics, and what kinds of
data the documents carry — see [CcrXmlFormat.md](./CcrXmlFormat.md). The
authoritative source for the underlying flat-file format is BR's own spec,
[`batch-ajourhold - formatbeskrivelse pr.25.04.2025.doc`](./batch-ajourhold%20-%20formatbeskrivelse%20pr.25.04.2025.doc),
shipped alongside this directory.

The XML is derived from real production samples; identifying data has been
replaced with synthetic values that follow the same validation rules as the
real format:

- **Org numbers** are 9-digit Norwegian organisasjonsnummer with a valid mod-11
  check digit. They are picked from the `316289xxx` synthetic test range that
  the rest of the test corpus already uses.
- **Person SSNs** (when present) are 11-digit Norwegian fødselsnummer
  generated using the **Skatteetaten Tenor synthetic convention**: the
  month part has **80 added** (so MM becomes 81–92, an invalid real
  month → unambiguously synthetic). Day and 2-digit year are kept, the
  individual number is fully randomized inside the year-appropriate
  range (000–499 / 900–999 for years 1900–1999, 500–999 for years
  2000–2039), and K1/K2 are recomputed to be mod-11 valid. **Note:**
  this is *not* the day+40 convention — day+40 produces real D-numbers
  (Dummernummer, used for foreign workers without a permanent ID), not
  synthetic test data.
- **Names** (organization and person) are obviously fake test names. When
  the original carried a multi-word `fornavn`, the synthetic name keeps a
  multi-word `fornavn` so that branch stays covered.
- **Addresses** are replaced with synthetic street names + neutral
  postcodes when they appear. The shared-vs-distinct address structure is
  preserved (e.g., if three of four persons in the original lived at the
  same address, three of four still share an address in the synthetic
  version).
- **Same person → same fake SSN** across all references within a scenario.
  Role-transition records (e.g., a person vacating MEDL while taking up
  LEDE in the same update) only stay coherent if the SSN matches across
  records.

## Scenario 1 — New accountant (regnskapsfører) registered for a NUF

**Change type:** New `REGN` connection (`samendringer data="D" felttype="REGN" endringstype="N" type="K"`)
— a previously-existing NUF gets a new accountant relation registered.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | NUF (Norskregistrert utenlandsk foretak) | `316289177` | Founded 2013-03-19, not first transfer |
| New accountant | Organization (REGN connection) | `316289347` | `knytningFratraadt=N` (active) |

**No persons, names, or addresses are part of this scenario** — it is a pure
organization-to-organization (`type="K"`) connection update.

## Scenario 2 — Two new accountants (regnskapsfører) registered for an AS

**Change type:** Two new `REGN` connections in a single update
(`samendringer data="D" felttype="REGN" endringstype="N" type="K"`, twice) —
an existing AS gets two accountant relations registered at once. The first
connection carries `knytningRekkefoelge=1`; the second is emitted without an
explicit order, exercising the "missing `knytningRekkefoelge`" branch.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS (Aksjeselskap) | `316289134` | Founded 2013-04-09, not first transfer |
| New accountant #1 | Organization (REGN connection) | `316289029` | `knytningRekkefoelge=1`, active |
| New accountant #2 | Organization (REGN connection) | `316289045` | No `knytningRekkefoelge` element, active |

**No persons, names, or addresses are part of this scenario** — pure
organization-to-organization (`type="K"`) connection updates.

## Scenario 3 — Board reorganization in an ESEK (eierseksjonssameie)

**Change type:** A mixed bag of `type="R"` (Rolle/person-role) `samendringer`
representing a board change in a condominium-owners' association: a new
chairperson takes over (with the previous chair stepping down), three
board-member positions turn over, and one current deputy is promoted to
full board member.

The eight `samendringer` records (in the original order) are:

| # | Felttype | endringstype | Subject | Notes |
| --- | --- | --- | --- | --- |
| 1 | `LEDE` | `N` (new) | Anne Testperson (`02895823468`) | New chair (`rolleRekkefoelge=1`) |
| 2 | `LEDE` | `U` (utgår) | SSN `28825934504` only | Outgoing chair, identifier-only record |
| 3 | `MEDL` | `U` | SSN `02895823468` only | Same person as #1 — vacates her old MEDL seat to become LEDE |
| 4 | `MEDL` | `N` | Ola Test Testperson (`07855812899`) | New member (`rolleRekkefoelge=3`); two-word `fornavn` |
| 5 | `MEDL` | `U` | SSN `10877345640` only | Outgoing member |
| 6 | `MEDL` | `N` | Kari Marit Testperson (`10886039447`) | New member, **no `rolleRekkefoelge`**; two-word `fornavn`; different address |
| 7 | `MEDL` | `N` | Per Testperson (`15817928703`) | New member (`rolleRekkefoelge=2`) |
| 8 | `VARA` | `U` | SSN `15817928703` only | Same person as #7 — vacates deputy seat to become MEDL |

**Subject organization:** ESEK (eierseksjonssameie / condominium owners'
association) `316289118`, founded 2013-04-13, not first transfer.

**Persons** (six unique individuals, two of whom appear in two records):

| # | Synthetic SSN | Synthetic name | Born (from SSN) | Roles in this update |
| --- | --- | --- | --- | --- |
| P1 | `02895823468` | Anne Testperson | 1958-09-02¹ | LEDE-N, MEDL-U (promotion to chair) |
| P2 | `28825934504` | _(name not supplied)_ | 1959-02-28¹ | LEDE-U |
| P3 | `07855812899` | Ola Test Testperson | 1958-05-07¹ | MEDL-N |
| P4 | `10877345640` | _(name not supplied)_ | 1973-07-10¹ | MEDL-U |
| P5 | `10886039447` | Kari Marit Testperson | 1960-08-10¹ | MEDL-N |
| P6 | `15817928703` | Per Testperson | 1979-01-15¹ | MEDL-N, VARA-U (promotion to member) |

¹ Day-of-month decoded from `DD` directly (Tenor synthetic convention adjusts the *month* by +80, not the day).

All persons have `personstatus="L"` (Levende/alive). All `N` records carry
`rolleFratraadt="N"` (the new role is currently active).

**Addresses** (preserving the original 3-share-1-distinct structure):

| Address | Postcode | Used by |
| --- | --- | --- |
| `Testveien 1` | `1234` | P1, P3, P6 (the ESEK's registered address) |
| `Testgata 14` | `5678` | P5 (lives elsewhere) |

**Coverage value:** mixes new and ending records; includes identifier-only
`U` records; the same SSN appears in two `samendringer` (a person changing
role within the same organization in the same update); covers
`rolleRekkefoelge` = 1, 2, 3 and one missing-rekkefoelge case; covers
two-word `fornavn`; covers two distinct addresses (three persons share the
same address, one lives elsewhere).

## Scenario 4 — VAT-register status flipped to "not registered" on an AS

**Change type:** A bare `<infotype>` element (no `<samendringer>` wrapper)
with `felttype="R-MV"` (REGMV — Registreringsstatus MVA-register / VAT
register registration status) and `endringstype="U"`. The carried value is
`<opplysning>N</opplysning>`, i.e. the org is no longer registered in the
VAT register.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS (Aksjeselskap) | `316289215` | `undersakstype="EBTC"`, founded 2013-07-17, not first transfer |

No second org, no persons, no names, no addresses — purely a status flag
flipped on the subject org itself.

**Coverage value:** the first scenario in this corpus that exercises a
top-level `<infotype>` element (rather than `samendringer`), and the only
one that exercises the **R-MV / REGMV boolean-length-value branch** in
the parser (`INFOTYPE_VALUE_LENGTH_BOOL`, distinct from the
default-length simple infotypes used by `R-FV`, `R-FR`, `R-SR`, `RSKP`,
etc.). Also covers `endringstype="U"` at the top-level infotype layer.

## Scenario 5 — New industry code (NACE) registered for a BEDR

**Change type:** A bare `<infotype felttype="naeringskode" endringstype="N">`
on a sub-enterprise (BEDR), registering a NACE/næringskode of `68.320`
(real-estate management on contract) effective from `2013-01-01`,
flagged as not a hjelpeenhet (`<hjelpeenhet>N</hjelpeenhet>`).

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | BEDR (sub-enterprise / local unit) | `316289096` | `undersakstype="EBTC"`, founded 2013-08-14, not first transfer |

No second org, no persons, no names, no addresses. The NACE code itself
(`68.320`) is a public classification, not PII, so it is retained as-is.

**Coverage value:** the only scenario hitting the **dedicated
`naeringskode` branch** in the parser (`WriteNaeringskode`), which emits
three child elements (`<naeringskode>`, `<gyldighetsdato>`,
`<hjelpeenhet>`) — structurally distinct from the single-`<opplysning>`
output of Scenario 4's `R-MV` branch even though both scenarios share the
"bare `<infotype>` under `<enhet>`" outer shape. Also the only scenario
that exercises `organisasjonsform="BEDR"`.

## Scenario 6 — Two REGN connections on an AS, one already Fratraadt

**Change type:** Two `samendringer data="D" felttype="REGN" endringstype="N" type="K"`
on the same AS in one update. The first connection is registered with
`knytningFratraadt="F"` (already stepped down) and `knytningRekkefoelge=1`;
the second carries `knytningFratraadt="N"` (active) and no
`knytningRekkefoelge`.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289525` | Founded 2021-01-25, `datoSistEndret` is one day earlier than `head/dato` |
| Accountant #1 | Organization (REGN connection) | `316289533` | `knytningFratraadt=F`, `knytningRekkefoelge=1` |
| Accountant #2 | Organization (REGN connection) | `316289644` | `knytningFratraadt=N`, no `knytningRekkefoelge` |

**Coverage value (marginal):** structurally near-identical to Scenario 2
(AS / two REGN K samendringer / rekkefoelge 1+missing). The only novel
field-value is `knytningFratraadt="F"` on the first connection. The
parser does not branch on this value, so this scenario primarily adds
snapshot/regression coverage rather than a new code path. Useful as a
"recorded transition" shape (existing accountant stepping down while a
new one is registered, in the same update).

## Scenario 7 — Minimal new business address (FADR) on an ENK

**Change type:** A bare `<infotype felttype="FADR" endringstype="N">`
registering a new forretningsadresse (business address) on an ENK
(enkeltpersonforetak / sole proprietorship). Only `adresse1` is
populated — `adresse2` and `adresse3` are absent.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | ENK (Enkeltpersonforetak) | `316289479` | Founded 2023-06-27, `datoSistEndret` is two days earlier than `head/dato` |

**Address (synthetic, replaces a real one):**

| Field | Value |
| --- | --- |
| `postnr` | `5678` |
| `landkode` | `NO` |
| `kommunenr` | `4601` |
| `adresse1` | `Testgata 2` |
| `adresse2` | _(absent)_ |

**Coverage value:** shares the `WriteAddress` branch with Scenario 8, but
exercises a different sub-case of that branch — the **minimal-FADR
path** with `adresse2` absent, hitting the `WriteOptionalTextElementNode`
IsEmpty short-circuit for the secondary address line. Also the **only
scenario covering `organisasjonsform="ENK"`** (sole proprietorship). No
`c/o` indirection in `adresse1` (plain street address), complementing
Scenario 8's c/o pattern.

## Scenario 8 — New business address (FADR) on an ESEK, with c/o person

**Change type:** A bare `<infotype felttype="FADR" endringstype="N">`
registering a new forretningsadresse (business address) on an ESEK. The
address has both `<adresse1>` and `<adresse2>` populated, with the
`adresse1` line carrying a `c/o` reference to a contact person.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | ESEK | `316289304` | Founded 2021-05-08, not first transfer |

**Address (synthetic, replaces a real one):**

| Field | Value |
| --- | --- |
| `postnr` | `1234` |
| `landkode` | `NO` |
| `kommunenr` | `0301` |
| `adresse1` | `c/o Test Testperson` |
| `adresse2` | `Testveien 29` |

The original `c/o` line carried a real person's name, replaced with
"Test Testperson"; street name and postcode/kommune were also synthesized.

**Coverage value:** the only scenario hitting the shared
`WriteAddress` branch (used for both `FADR` and `PADR`), exercising all
five address sub-elements (`postnr`, `landkode`, `kommunenr`, `adresse1`,
`adresse2` — only `adresse3`, `poststed` are absent). Also the only
scenario covering a `c/o` pattern embedded in `adresse1` and
`organisasjonsform="ESEK"` outside the person-role context of Scenario 3.

## Scenario 9 — Single REGN connection on an AS, marked Fratraadt

**Change type:** A single `samendringer data="D" felttype="REGN" endringstype="N" type="K"`
with `knytningFratraadt="F"` (the connection is registered as already
stepped down) and `knytningRekkefoelge=4`.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289312` | Founded 2021-06-11, not first transfer |
| Accountant (REGN) | Organization (REGN connection) | `316289428` | `knytningFratraadt=F`, `knytningRekkefoelge=4` |

**Coverage value (marginal):** hits the same parser branch as Scenarios
1, 2 (REGN/`type="K"`) but is the only scenario combining all of:
single-samendring + AS form + `knytningFratraadt="F"` +
`knytningRekkefoelge=4`. No new code path; only field-value variation
relative to existing scenarios. Useful for snapshot/regression coverage
if a future change makes `knytningFratraadt` value-sensitive.

## Scenario 10 — BEDR moves between main enterprises, with name change and event date

**Change type:** A sub-enterprise (BEDR) is reassigned from one main
enterprise to another, given a new business name, and stamped with an
event date — all in one enhet update. The file mixes both
`<samendringer>` and `<infotype>` records under a single `<enhet>`.

The four records (in the original order):

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `samendringer` (`type="K"`) | `BEDR` | `N` | New main-enterprise connection (`knytningFratraadt=N`, `rekkefoelge=1`) |
| 2 | `samendringer` (`type="K"`) | `BEDR` | `U` | Old main-enterprise connection expiring — record carries only `<knytningOrganisasjonsnummer>`, all other K-data fields are absent |
| 3 | `infotype` | `EDAT` | `N` | Event date `20260401` (date-only branch) |
| 4 | `infotype` | `NAVN` | `N` | New business name; only `<navn1>` and `<rednavn>` populated |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | BEDR (sub-enterprise) | `316289703` | `undersakstype="EBTC"`, founded 2015-09-01 |
| New main enterprise (BEDR-N) | Organization (BEDR connection) | `316289800` | `knytningFratraadt=N`, active |
| Old main enterprise (BEDR-U) | Organization (BEDR connection) | `316289916` | Connection being expired |

**Synthetic name:** `TESTSPORT OUTLET TESTBY` (replaces a real
BR-registered name; same value used for both `<navn1>` and `<rednavn>`,
matching the original).

**Coverage value (high):** the only scenario that hits any of the
following parser branches, and the most branch-rich single file in the
corpus:

- `samendringer felttype="BEDR"` (sub-enterprise → main-enterprise
  connection) — first non-REGN K-samendring; same parser code path
  as REGN but different `felttype` value.
- `samendringer endringstype="U"` on `type="K"` — first scenario
  expiring a *connection* (Scenario 3's `U` records are only on
  `type="R"`). Exercises every `WriteOptionalTextElementNode` IsEmpty
  short-circuit in the K-data path (`knytningAnsvarsandel`,
  `knytningFratraadt`, `knytningValgtav`, `knytningRekkefoelge`,
  `korrektOrganisasjonsnummer` are all absent in the U record).
- `<infotype felttype="EDAT">` — the EDAT/BDAT/NDAT date-only branch
  (`if (!value.IsEmpty)` short-circuit), distinct from the always-emit
  default-length branch.
- `<infotype felttype="NAVN">` (`WriteName`) — first organization name
  change in the corpus. Tests the `<navn1>`-only + `<rednavn>` subset
  (no `navn2`–`navn5`).
- **Mixed `<samendringer>` + `<infotype>` under one `<enhet>`** — the
  only scenario that combines both record families in the same enhet,
  exercising the `ParseOrganization` switch across record-type
  transitions.

## Scenario 11 — Initial registration of an FLI (forening) with full board, name, purpose, and signing rule

**Change type:** Initial registration (`foersteOverfoering="J"`,
`hovedsakstype="N"`, `undersakstype="NY"`) of a brand-new association
(FLI / forening). The record carries the org's first business address,
purpose statement (split across three FORM records), expected member
count, four board members, language form, business name, signing-rule
text, and an event date — thirteen records under one `<enhet>`.

The thirteen records (in order):

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `infotype` | `FADR` | `N` | Business address with `c/o` referencing the chair |
| 2 | `infotype` | `FORM` | `N` | Purpose, line 1 |
| 3 | `infotype` | `FORM` | `N` | Purpose, line 2 (continued from #2) |
| 4 | `infotype` | `FORM` | `N` | Purpose, line 3 (continued) |
| 5 | `infotype` | `ISEK` | `N` | Expected member count (`7000`) |
| 6 | `samendringer` (`type="R"`) | `LEDE` | `N` | Chair (`rolleRekkefoelge=4`) |
| 7 | `samendringer` (`type="R"`) | `MEDL` | `N` | Board member with `<mellomnavn>` (`rolleRekkefoelge=3`) |
| 8 | `samendringer` (`type="R"`) | `MEDL` | `N` | Board member, no mellomnavn (`rolleRekkefoelge=2`), separate address |
| 9 | `samendringer` (`type="R"`) | `MEDL` | `N` | Board member with `<mellomnavn>` (`rolleRekkefoelge=1`); sibling of #7 |
| 10 | `infotype` | `MÅL` | `N` | Language form (`B` = bokmål) |
| 11 | `infotype` | `NAVN` | `N` | Business name |
| 12 | `samendringer` (`type="S"`, `data="T"`) | `SIGN` | `N` | **Free-text** signing rule (no `<samendringer>` record before this in any scenario used `data="T"`) |
| 13 | `infotype` | `STID` | `N` | Event date `20251118` |

**Subject organization:** FLI (Forening / lag / innretning) `316289371`,
**registered today** (`datoFoedt = datoSistEndret = head/dato`),
`foersteOverfoering="J"`.

**Synthetic name:** `TESTBY FYRFORENING` (used as both `<navn1>` and
`<rednavn>`). The original was the BR-registered name of an actual
lighthouse-preservation association; the FORM purpose text was likewise
edited to remove the real lighthouse name (`Hellisøy fyrstasjon` →
`Testfyrstasjon`).

**Persons** (four, with one chair / three members):

| # | Synthetic SSN | Synthetic name | Born (from SSN) | Notes |
| --- | --- | --- | --- | --- |
| P1 | `22837242561` | Berit Testperson | 1972-03-22¹ | Chair (LEDE), `rolleRekkefoelge=4`. Also referenced in FADR `c/o`. |
| P2 | `03850672341` | Bjørn Test Testperson | 2006-05-03¹ | MEDL, `rolleRekkefoelge=3`. Has `<mellomnavn>`. Sibling of P4. |
| P3 | `15837924771` | Geir Testperson | 1979-03-15¹ | MEDL, `rolleRekkefoelge=2`. Lives separately. |
| P4 | `22880761599` | Birk Test Testperson | 2007-08-22¹ | MEDL, `rolleRekkefoelge=1`. Has `<mellomnavn>`. Sibling of P2. |

¹ Day-of-month decoded from `DD` directly (Tenor synthetic convention adjusts the *month* by +80, not the day).

The sibling structure (P2 + P4) is preserved by giving them the same
`<mellomnavn>`, the same `<slektsnavn>`, and the same address.

**Addresses** (preserving the original 1-shared-by-3 + 1-distinct
structure, plus the chair-also-c/o-reference link):

| Address | Postcode | Used by |
| --- | --- | --- |
| `Testveien 97` | `1234` | FADR `adresse2` + P1 (chair). The FADR `c/o` line names P1 by their synthetic name |
| `Testgata 55` | `1234` | P2 + P4 (siblings) |
| `Testbergvegen 11` | `5678` | P3 |

The FADR also carries `kommunenr=5001` (different from Scenarios 7 / 8
to keep each FADR scenario in a different synthetic geography).

**Coverage value (very high):** the most branch-rich single file in the
corpus. Closes the following gaps from the audit:

- **`foersteOverfoering="J"`** — first initial-registration scenario.
- **`organisasjonsform="FLI"`** — first forening (no scenario used this
  form before).
- **Default-length simple-infotype branch** (`WriteSimpleInfoType` via
  the default-length value path) — first scenario hitting this branch.
  Exercised four times here with different felttypes: `FORM` (×3),
  `ISEK`, `MÅL`, `STID`. Same writer, different values.
- **`<mellomnavn>` in person samendring** — Scenario 3 had multi-word
  `fornavn` but no `mellomnavn`; this is the first.
- **`samendringer data="T"` with `type="S"`** — first free-text
  samendring, hitting `CreateSamendringerNodeText` case `"S"`. Emits
  `<plassering>` and `<samendringfritTekstlinje>`.
- **Repeated infotype of the same felttype** (3 × FORM) — the parser
  treats each as a distinct record; tests that the writer doesn't have
  hidden "merge same felttype" logic.
- **A `c/o` line in FADR pointing at a person who is also a board
  member of the same org** — coherence test across the FADR/role
  boundary (the synthetic c/o name matches the synthetic chair name and
  the synthetic FADR adresse2 matches the synthetic chair adresse1).

## Scenario 12 — Isolated VEDT (statutes-adopted date) on an AS

**Change type:** A single `<infotype felttype="VEDT" endringstype="N">`
on an AS, carrying a date `<opplysning>20260325</opplysning>` (the date
on which the org's statutes were adopted).

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289770` | Founded 2016-11-08, not first transfer |

The `20260325` value is an event date, not PII, retained as-is.

**Coverage value (marginal):** hits the same parser branch as the FORM /
ISEK / MÅL / STID records in Scenario 11 (`WriteSimpleInfoType` via the
default-length value path). The parser doesn't switch on the felttype
string, so this is the same code path with a different felttype label.
Useful as an isolated single-infotype-under-enhet test of the
simple-default-length branch (Scenario 11 only exercises that branch as
part of a 13-record composite); pure parser-branch coverage is no
different from Scenario 11.

## Scenario 13 — New business address (FADR) and deleted postal address (PADR) on an ESEK

**Change type:** Two address records under one `<enhet>` — a new
forretningsadresse (FADR) is registered and the existing postadresse
(PADR) is deleted in the same update. The PADR record is emitted as a
self-closing `<infotype felttype="PADR" endringstype="U" />` because all
its source fields are empty.

The two records:

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `infotype` | `FADR` | `N` | New business address. Only `adresse1` populated; no `adresse2`/`adresse3`/`poststed` |
| 2 | `infotype` | `PADR` | `U` | Postal address deleted. **Self-closing element** — every `WriteOptionalTextElementNode` short-circuits because all source fields are empty |

**Subject organization:** ESEK `316289398`, founded 2014-07-24, not
first transfer.

**Address (synthetic, replaces a real one):**

| Field | Value |
| --- | --- |
| `postnr` | `1234` |
| `landkode` | `NO` |
| `kommunenr` | `4601` |
| `adresse1` | `Testgata 12B` |

The `B` suffix on the street number is retained from the original — it
exercises non-numeric trailing characters in the address-line text.

**Coverage value:** the only scenario hitting `<infotype felttype="PADR"`,
which closes the largest remaining gap in the address-branch coverage
(`WriteAddress` is shared between FADR and PADR, but each is emitted with
a different `recordType` string and produces a different `felttype`
attribute on the output `<infotype>`). Also the only scenario with an
empty / self-closing `<infotype>` element — exercises the case where every
sub-field is absent so all `WriteOptionalTextElementNode` calls
short-circuit and the writer still produces a well-formed open/close
element pair. Mixed `FADR-N` + `PADR-U` under one enhet is also unique to
this scenario.

## Scenario 14 — Email + URL added, mobile deleted on a BEDR

**Change type:** Three contact-info `<infotype>` records under one
`<enhet>` — a new email (EPOS), a new URL (IADR), and a delete of the
existing mobile phone (MTLF, self-closing).

The three records:

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `infotype` | `EPOS` | `N` | New email |
| 2 | `infotype` | `IADR` | `N` | New URL |
| 3 | `infotype` | `MTLF` | `U` | Mobile phone deleted; self-closing element |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | BEDR | `316289827` | Founded 2013-09-16, not first transfer |

**Synthetic contact info (replaces real values):**

| Field | Value |
| --- | --- |
| EPOS `<opplysning>` | `test.testperson@example.com` (RFC 2606 reserved domain) |
| IADR `<opplysning>` | `example.com/consultant/test-testperson` (preserves the original `domain/category/personal-slug` URL shape) |
| MTLF `<opplysning>` | _(absent — self-closing element)_ |

The original email was a real personal address; the original URL
embedded a real person's name as a path slug.

**Coverage value:** closes three of the contact-info gaps from the
audit in one file. First scenario hitting any of `EPOS` / `IADR` /
`MTLF`. The three parser cases all have dedicated `ParserConsts`
offsets so they are distinct on the flat-file side, even though the
emitted XML structure is the same `<infotype><opplysning>...` shape
funnelled through `WriteSimpleInfoType`. The `MTLF-U` self-closing
record adds a second instance of the empty-infotype edge case
(complementing Scenario 13's `PADR-U`).

`TFON` (telefon) and `TFAX` (telefax) are still uncovered — they would
go through the same writer path but exercise their own dedicated
parser cases.

## Scenario 15 — Isolated free-text signing rule (data="T", type="S") on an AS

**Change type:** A single `<samendringer data="T" felttype="SIGN" endringstype="N" type="S">`
on an AS, registering a short signing-rule text.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289142` | Founded 2022-08-06, not first transfer |

The free-text content `Styrets leder alene.` is generic Norwegian
("The chair alone") — not PII; retained as-is.

**Coverage value (marginal):** hits the same parser branch as the
`samendringer data="T" type="S"` record in Scenario 11
(`CreateSamendringerNodeText` case `"S"`). Same writer, same emitted XML
(`<plassering>` + `<samendringfritTekstlinje>`). The parser doesn't
switch on the felttype string or text length, so the only difference
from Scenario 11's record #12 is that this one is the sole record under
`<enhet>` rather than embedded in a 13-record composite. Useful as an
isolated focused test of the free-text-S branch; for unique parser
branches it adds nothing over Scenario 11.

The `data="T"` types `R` (free-text role) and `K` (free-text knytning)
remain uncovered and would be higher-value additions in the same
`CreateSamendringerNodeText` family.

## Scenario 16 — Status SKRR removed on an AS

**Change type:** A single self-closing `<status felttype="SKRR" endringstype="U" />`
on an AS — the `SKRR` (Probate court from RR) status flag is being
removed. No body, no children.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289061` | `undersakstype="EBTC"`, founded 2014-06-20, not first transfer |

**Coverage value (high):** the only scenario in the corpus that emits a
`<status>` element. Covers the **no-kjennelsesdato branch** of
`WriteStatus` — the writer's `if (endringsType is "N" && !kjennelsesDato.IsWhiteSpace())`
guard short-circuits, so only `felttype` and `endringstype` attributes
are written. This single fixture exercises the writer path shared by
~33 different status record types (`AKKO`, `BRSL`, `BRKO`, `BROP`,
`FIFO`, `FIPL`, `FITA`, `FLYT`, `FUTA`, `FUPL`, `IPF`, `OMPL`, `OPFI`,
`OPFU`, `OPPL`, `OSDL`, `OSED`, `OSBA`, `OSEF`, `OSEV`, `OSKA`, `OSKP`,
`OSRE`, `OSST`, `SKRR`, `TVBA`, `TVDL`, `TVKA`, `TVOV`, `TVRE`, `TVRR`,
`TVST`, `USL`, `USYS`) — they all share one parser case body and one
writer path; the only differentiation is the `recordType` string used
as the `felttype` attribute value.

The **`KONK` (bankruptcy) status** has its own parser case that reads a
`kjennelsesdato` slice and is the only record type that triggers the
`<kjennelsesdato>` child element in `WriteStatus`. That branch is still
uncovered.

## Scenario 17 — Daglig leder leaves, samendring expires, status flips to disbanded

**Change type:** A coherent dissolution-flow story across three records
under one `<enhet>`: the daglig leder (CEO) is expired, the related
samendring is marked as utgår, and the org's status flips to OPPL
(disbanded). The enhet itself carries `undersakstype="OPPL"`.

The three records:

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `samendringer` (`type="R"`) | `DAGL` | `U` | Daglig leder expired; identifier-only record (only `<rolleFoedselsnr>`) |
| 2 | `samendringUtgaar` | `SAMU` | _(n/a)_ | The samendring itself is being marked as utgår; `<samendringstype>DAGL</samendringstype>` references the role being expired |
| 3 | `status` | `OPPL` | `N` | Status "disbanded" added; self-closing element |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AS | `316289444` | `undersakstype="OPPL"` (only scenario in the corpus to use this undersakstype value) |
| Outgoing daglig leder | Person (DAGL-U) | SSN `02845328733` only | Born 1953-04-02¹; identifier-only record (no name, no address) |

¹ Day-of-month decoded from `DD` directly (Tenor synthetic convention adjusts the *month* by +80, not the day).

**Coverage value (very high):** closes two prominent gaps from the
audit:

- **`<samendringUtgaar>` (SAMU)** — the only record type emitted by
  `WriteSamendringutgaar`. Previously uncovered. Tests the writer's
  `<samendringstype>` child and its hardcoded `felttype="SAMU"`
  attribute.
- **`<status>` with `endringstype="N"` and no `<kjennelsesdato>`** —
  Scenario 16 covered the `endringstype="U"` path of `WriteStatus`;
  this scenario covers the `endringstype="N"` path while still hitting
  the `kjennelsesDato.IsWhiteSpace()` short-circuit in the writer's
  guard (no `<kjennelsesdato>` child emitted). Together S16 + S17 cover
  both halves of the `if (endringsType is "N" && !kjennelsesDato.IsWhiteSpace())`
  guard.

Also adds a new role felttype value (`DAGL`) to the corpus —
Scenario 3 had `LEDE` / `MEDL` / `VARA` — and tells a recognizable
semantic story (CEO leaves → samendring expires → org dissolves) in
three coherent records.

The **`KONK` status with `<kjennelsesdato>`** remains the only
WriteStatus sub-case still uncovered.

## Scenario 18 — Email and phone deleted on an AAFY

**Change type:** Two self-closing contact-info `<infotype>` records
under one `<enhet>` — both EPOS (email) and TFON (phone) are deleted
in the same update.

The two records:

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `infotype` | `EPOS` | `U` | Email deleted; self-closing |
| 2 | `infotype` | `TFON` | `U` | Phone deleted; self-closing |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | AAFY (Underenhet til foretak/lag) | `316289126` | Founded 2019-11-05; `datoSistEndret` is one day before `head/dato` |

No persons, no contact-info content (both records are empty
self-closing — exercise the all-empty `WriteOptionalTextElementNode`
short-circuits).

**Coverage value:** the only scenario hitting the **`TFON` parser case**
— TFON has its own dedicated `ParserConsts` offsets even though the
writer (`WriteTelefon`) is a thin wrapper around `WriteSimpleInfoType`.
Also the only scenario that uses `organisasjonsform="AAFY"` (the parser
doesn't switch on form, so this is documentation/snapshot value).

The EPOS-U record is a variant of Scenario 14's EPOS-N: same parser
case, same writer path, but here the source value is empty so it
becomes a self-closing element. Together with the MTLF-U in
Scenario 14 and the PADR-U in Scenario 13, this gives three different
felttypes (EPOS, MTLF, PADR — and now TFON-flavoured) all exercising
the empty-self-closing-infotype shape.

`TFAX` and `FMVA` are still uncovered contact-info felttypes; they
share the same writer path and would only cover their dedicated
parser cases.

## Scenario 19 — VAT-relation type (FMVA) registered on an ENK

**Change type:** A single `<infotype felttype="FMVA" endringstype="N">`
on a sole proprietorship, registering a VAT-relation type code
(`<opplysning>BFLA</opplysning>`).

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | ENK | `316289010` | `undersakstype="EBTC"`, founded 2013-08-15; `datoSistEndret` is two days before `head/dato` |

The `BFLA` value is a public Skatteetaten VAT-art classification code,
not PII; retained as-is.

**Coverage value:** the only scenario hitting the **`FMVA` parser case**
— FMVA has its own dedicated `ParserConsts` offsets (`FMVA_STATUS_OFFSET`,
`FMVA_TYPE_OFFSET`) and its own writer wrapper (`WriteFmva`). The
emitted XML structure is the same `<infotype felttype="FMVA"><opplysning>...`
as other simple infotypes — `WriteFmva` is essentially a hardcoded-type
wrapper around the same `WriteInfoElementStart` + `WriteInfoElementValue("opplysning", ...)`
body that `WriteSimpleInfoType` uses — so the value-add is the parser
case, not a new writer output shape.

## Scenario 20 — Foreign-register reference (UREG) on a UTLA, pointing at the Finnish PRH

**Change type:** A single `<infotype felttype="registrertHjemlandetsRegister" endringstype="N">`
on a UTLA (foreign enterprise registered in Norway), recording which
foreign business register the org is registered in. Six of the writer's
nine optional children are populated; `registerNavn3`, `postadresse2`,
and `postadresse3` short-circuit on IsEmpty.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | UTLA (Utenlandsk foretak) | `316289568` | `undersakstype="KORR"` (correction batch), `datoFoedt=20260428`, `datoSistEndret=20260504` |

**Foreign-register reference:**

| Field | Value | Note |
| --- | --- | --- |
| `registernr` | `9999999-9` | Synthetic Y-tunnus shape (replaces a real Finnish business ID) |
| `registerNavn1` | `Patentti - Ja Rekisterihallitus` | Real public institutional name of Finland's Patent and Registration Office (PRH); retained as public metadata, like the NACE code in Scenario 5 |
| `registerNavn2` | `Kaupparekisterijärjestelmä` | Real public name of the PRH's trade register system; retained |
| `landkode` | `FI` | Country code; retained |
| `utenlandskPoststed` | `00010 Helsinki` | Postcode synthesized; city `Helsinki` retained (city is not PII) |
| `postadresse1` | `Testikatu 1 A` | Synthetic Finnish-flavoured street name; preserves the original "X letter" suffix shape |

**Coverage value (high):** closes two prominent gaps from the audit:

- **`<infotype felttype="registrertHjemlandetsRegister"` (UREG)** —
  first scenario hitting `WriteUreg`, the writer with the largest
  number of optional children (nine). This fixture populates six of
  them and short-circuits the other three on IsEmpty.
- **`organisasjonsform="UTLA"`** — distinct from NUF (Scenario 1).
  UREG is normally only emitted on UTLA / NUF, so this scenario is
  the natural carrier.

Also adds `undersakstype="KORR"` (correction batch) as a new value in
the corpus — parser doesn't switch on it, but documents a real
production batch type.

**`<infotype felttype="underlagtHjemlandetsLovgivning"` (ULOV)** is the
sister-branch writer (`WriteUlov`, four children) and remains the only
foreign-org-specific writer still uncovered.

## Scenarios 21 + 22 — Bankruptcy declaration on an AS, paired with the creation of its konkursbo

These two scenarios are deliberately **linked**: Scenario 21 declares
the AS bankrupt, and Scenario 22 registers the resulting konkursbo
(bankruptcy estate). The estate's `KDEB` knytning in Scenario 22 points
at the same `organisasjonsnummer` as the bankrupt AS's enhet in
Scenario 21 — the synthetic numbers are intentionally shared across the
two files so the relationship survives anonymization.

### Scenario 21 — KONK status (with kjennelsesdato) on an AS

**Change type:** A single `<status felttype="KONK" endringstype="N">`
with `<kjennelsesdato>20260504</kjennelsesdato>`. The court has
declared the AS bankrupt.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization (bankrupt AS) | AS | `316289185` | `undersakstype="EBTC"`, founded 2022-11-11. Same number as the `KDEB` knytning target in Scenario 22 |

**Coverage value (high):** the only scenario hitting the
**`<status felttype="KONK"` parser case + the `<kjennelsesdato>` writer
branch**. KONK is the only record type in the parser that reads a
kjennelsesDato slice (offset 8, length 8), and the only one that
satisfies both clauses of the `WriteStatus` guard
`if (endringsType is "N" && !kjennelsesDato.IsWhiteSpace())`. With this
scenario, all four corners of that guard are covered:

- N + non-empty kjennelsesDato → S21 (this one) — emits `<kjennelsesdato>`
- N + empty kjennelsesDato → S17 (`status felttype="OPPL" endringstype="N"`) — short-circuits
- not-N + empty kjennelsesDato → S16 (`status felttype="SKRR" endringstype="U"`) — guard false on first clause
- not-N + non-empty kjennelsesDato — never produced by the parser (kjennelsesDato is only sliced for the KONK case)

### Scenario 22 — Initial registration of the konkursbo (KBO) for the AS in Scenario 21

**Change type:** Initial registration (`foersteOverfoering="J"`,
`hovedsakstype="N"`, `undersakstype="NY"`) of a brand-new bankruptcy
estate. Nine records under one `<enhet>`: a Bobestyrer (estate
trustee), business address, purpose, the link to the bankrupt AS
(KDEB), language code, name, postal address (mail via the trustee),
NACE, and an event date.

The nine records (in order):

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `samendringer` (`type="R"`) | `BOBE` | `N` | Bobestyrer (estate trustee). `rolleRekkefoelge=10`. Has `<mellomnavn>` |
| 2 | `infotype` | `FADR` | `N` | Estate's business address (4 fields, no `adresse2`) |
| 3 | `infotype` | `FORM` | `N` | Purpose: `Konkursbo.` |
| 4 | `samendringer` (`type="K"`) | `KDEB` | `N` | **Knytning to the bankrupt company** — `knytningOrganisasjonsnummer` matches the enhet org of Scenario 21. `rolleRekkefoelge=30` |
| 5 | `infotype` | `MÅL` | `N` | Language code (`B` = bokmål) |
| 6 | `infotype` | `NAVN` | `N` | Estate name with the canonical `… AS KONKURSBO` suffix |
| 7 | `infotype` | `PADR` | `N` | Mailing address routed via the trustee — `adresse1` carries `v/Adv. <name>` (synth: same name as the BOBE person) |
| 8 | `infotype` | `naeringskode` | `N` | NACE `53.200` (freight transport) — **no `<gyldighetsdato>` element**, exercises the optional-field short-circuit in `WriteNaeringskode` |
| 9 | `infotype` | `STID` | `N` | Event date `20260504` |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization (the konkursbo) | KBO (Konkursbo) | `316289192` | Registered today |
| Bankrupt AS (KDEB target) | Organization (KDEB connection) | `316289185` | Same number as Scenario 21's enhet org |
| Bobestyrer / trustee (BOBE) | Person | SSN `28816327530`, born 1963-01-28¹, `Frida Test Testperson` | Same person also referenced by name in PADR `adresse1` |

¹ Day-of-month decoded from `DD` directly (Tenor synthetic convention adjusts the *month* by +80, not the day).

**Synthetic name:** `TESTSELSKAP AS KONKURSBO` (replaces a real
BR-registered name; preserves the canonical `[bankrupt-AS-name] KONKURSBO`
suffix structure used for all Norwegian konkursbo names).

**Addresses:**

| Field | Synthetic | Original (replaced) | Used by |
| --- | --- | --- | --- |
| BOBE adresse1 | `Testveien 62 B`, postnr `1234` | Holmenkollveien 62 B, 0784 | Trustee's home |
| FADR adresse1 | `Testgata 33B`, postnr `5678`, kommunenr `0301` | Nordåssløyfa 33B, 1251, kommunenr 0301 | Estate's business address |
| PADR adresse1 + adresse2 | `v/Adv. Frida Test Testperson` + `Postboks 1234`, postnr `1234`, kommunenr `0301` | `v/Adv. Fredrik Astrup Borch` + `Postboks 1371 Vika`, 0114 | Estate's mailing address (via the trustee's postbox); `adresse1` references the trustee by name |

The `B` suffix on the BOBE and FADR street numbers is preserved.
Kommunenr `0301` is retained as a non-PII public number.

**Coverage value (very high):** closes four prominent gaps and
introduces the corpus's first **cross-scenario reference**:

- **`organisasjonsform="KBO"`** — first konkursbo enhet.
- **`samendringer felttype="BOBE"` (type=R)** — first non-LEDE/MEDL/VARA/DAGL
  role-felttype on a `type="R"` samendring.
- **`samendringer felttype="KDEB"` (type=K)** — first non-REGN/BEDR
  knytning-felttype on a `type="K"` samendring.
- **`<infotype felttype="naeringskode"` without `<gyldighetsdato>`** —
  variant of Scenario 5; exercises the optional-`gyldighetsdato`
  short-circuit in `WriteNaeringskode` (`if (IsNewOrUpdateChange(status))`
  controls whether `gyldighetsdato` is read).
- **First non-empty `<infotype felttype="PADR">`** — Scenario 13 had
  the empty `PADR-U` self-closing form; this is `PADR-N` with full
  content, including a `v/Adv.` prefix (variant of Scenario 8's `c/o`).

**Cross-scenario relationship:** the `<knytningOrganisasjonsnummer>`
in record #4 (`316289185`) is **the same synthetic number** as the
enhet org in Scenario 21. Tests of CCR data ingestion that span
multiple events should be able to recognise the link "this estate
belongs to that bankrupt AS". The bobestyrer-name match between BOBE
record (full identity) and PADR `adresse1` (`v/Adv. <name>`) is the
intra-scenario equivalent of Scenario 11's chair-as-c/o coherence.

## Scenario 23 — Initial registration of a UTLA with foreign FADR, ULOV, UREG, and Norwegian PADR

**Change type:** Initial registration (`foersteOverfoering="J"`,
`hovedsakstype="N"`, `undersakstype="NY"`) of a foreign enterprise
(UTLA — Finnish OY) registered for Norwegian purposes. Seven records
under one `<enhet>`: foreign business address, purpose, language code,
name, Norwegian mailing address, foreign-law subject, and
foreign-register reference.

The seven records (in order):

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `infotype` | `FADR` | `N` | Foreign business address (Finland). **No `<postnr>`** — uses `<poststed>` instead. `<adresse1>` carries `c/o <foreign-org-name>` |
| 2 | `infotype` | `FORM` | `N` | Purpose: `datter i konsern` |
| 3 | `infotype` | `MÅL` | `N` | Language code (`B` = bokmål) |
| 4 | `infotype` | `NAVN` | `N` | Foreign business name |
| 5 | `infotype` | `PADR` | `N` | Norwegian mailing address. `<adresse1>` carries `c/o <Norwegian-org-name>` |
| 6 | `infotype` | `underlagtHjemlandetsLovgivning` (ULOV) | `N` | Foreign-law-subject info: `<foretaksform>`, `<beskrivelseForetaksformHjemland>`, `<beskrivelseForetaksformNorsk>`, `<landkode>` |
| 7 | `infotype` | `registrertHjemlandetsRegister` (UREG) | `N` | Foreign-register reference (Finnish PRH); same metadata pattern as Scenario 20 |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | UTLA | `316289363` | Finnish OY, registered in Norway today |

**Synthetic name:** `TEST EIENDOM OY` (replaces a real bilingual
Finnish/Swedish company name; preserves the `OY` suffix indicating
Finnish company form).

**Addresses:**

| Field | Synthetic | Original (replaced) | Note |
| --- | --- | --- | --- |
| FADR `<adresse1>` | `c/o Testifirma Oy` | `c/o Azets Insight Oy` | The c/o references **another organization**, not a person |
| FADR `<adresse2>` | `Testikatu 5B` | `Elielinauko 5B` | Finnish-flavoured synthetic street; preserves the `B` suffix |
| FADR `<poststed>` | `012345 Helsinki` | `000100 Helsinki` | Foreign-postcode-with-city format; preserves the leading-zero shape; city `Helsinki` retained |
| FADR `<landkode>` | `FI` | _kept_ | Country code |
| PADR `<adresse1>` | `c/o Test Eiendom AS` | `c/o Ragde Eiendom AS` | Norwegian c/o reference, also to an organization |
| PADR `<adresse2>` | `Testgata 2` | `Sannergata 2` | Synthetic Norwegian street |
| PADR `<postnr>` / `<kommunenr>` | `1234` / `0301` | `0557` / `0301` | Postnr synthesized; kommunenr `0301` retained as non-PII |
| UREG `<postadresse1>` | `Testikatu 1 A` | `Arkadiankatu 6 A` | Synthetic — **same value as Scenario 20** so both UREG-Finnish-PRH references in the corpus stay consistent |
| UREG `<utenlandskPoststed>` | `00010 Helsinki` | `00100 Helsinki` | Same as Scenario 20 |
| UREG `<registernr>` | `1234567-8` | `2053792-1` | Synthetic Y-tunnus, distinct from Scenario 20's `9999999-9` |
| UREG `<registerNavn1>` / `<registerNavn2>` / `<landkode>` | _kept_ | (same) | Public PRH metadata, retained |
| ULOV `<foretaksform>` / `<beskrivelseForetaksformHjemland>` / `<beskrivelseForetaksformNorsk>` / `<landkode>` | _kept_ | (same) | Public Finnish company-form metadata |

**Coverage value (very high):** closes the last foreign-org-specific
writer gap and exercises a previously-untested sub-case of
`WriteAddress`:

- **`<infotype felttype="underlagtHjemlandetsLovgivning"` (ULOV)** —
  first scenario hitting `WriteUlov`. **The last foreign-org-specific
  writer is now covered.** Four children populated.
- **`WriteAddress` with `<poststed>` populated and `<postnr>` empty** —
  Scenarios 8/11/13/22 all emitted Norwegian addresses with `<postnr>`
  and the `<poststed>` `WriteOptionalTextElementNode` short-circuit
  active. This is the inverse: foreign FADR with `<poststed>`
  populated and `<postnr>` short-circuiting.
- **`c/o <organization-name>`** in both FADR and PADR `<adresse1>` —
  variant of the existing person-c/o pattern (S8 / S11) and lawyer-v/Adv.
  pattern (S22). Tests that the address writer doesn't impose any
  expectation about whether the c/o target is a person or an
  organization.
- **Mixed foreign + Norwegian addresses on one enhet** — first
  scenario combining a foreign FADR with a Norwegian PADR.

**All foreign-org-specific writer branches are now covered**:
`WriteAddress` foreign sub-case (S23), `WriteUlov` (S23), `WriteUreg`
(S20, S23).

## Scenario 24 — ENK with proprietor (INNH), daglig leder, contact info, deleted PAAT

**Change type:** A composite update on a sole proprietorship (ENK).
The proprietor is registered (INNH), a daglig leder is registered
(DAGL), email/phone are added, business name is registered as the
proprietor's name (the canonical ENK pattern), an existing paategning
(PAAT) is deleted, and an existing telefon (TFON) is deleted.

The seven records (in order):

| # | Record | felttype | endringstype | Notes |
| --- | --- | --- | --- | --- |
| 1 | `samendringer` (`type="R"`) | `DAGL` | `N` | Daglig leder. Has `<mellomnavn>` |
| 2 | `infotype` | `EPOS` | `N` | New email |
| 3 | `samendringer` (`type="R"`) | `INNH` | `N` | **Innehaver (sole proprietor)** — first scenario hitting this role-felttype |
| 4 | `infotype` | `MTLF` | `N` | New mobile |
| 5 | `infotype` | `NAVN` | `N` | Business name = proprietor's name (canonical ENK convention) |
| 6 | `infotype` | `paategning` | `U` | **First scenario hitting `WritePaategning`** — self-closing, all five children short-circuit |
| 7 | `infotype` | `TFON` | `U` | Phone deleted; self-closing |

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | ENK (Enkeltpersonforetak) | `316289649` | Founded 2008-01-02 |
| Daglig leder | Person (DAGL) | SSN `26917031273`, born 1970-11-26¹, `Erik Test Testperson` (with `<mellomnavn>`) | Lives at `Testveien 9`, postnr `1234` |
| Innehaver / proprietor | Person (INNH) | SSN `12864421537`, born 1944-06-12¹, `Elise Testperson` | Lives at `Testveien 29`, postnr `1234`. Same surname as the daglig leder (sibling/relative — ENK with family member as DAGL) |

¹ Day-of-month decoded from `DD` directly (Tenor synthetic convention adjusts the *month* by +80, not the day).

The two persons share `Testperson` surname and the same postcode +
street name (different house numbers `9` and `29`) — preserves the
"relatives at neighbouring addresses" structural property of the
original.

**Synthetic NAVN:** `ELISE TESTPERSON` (uppercase, matches the original
ENK convention of `<navn1>` = proprietor's full name in capitals).
Same name appears in both `<navn1>` and `<rednavn>`, and the personal
name on the INNH record matches it case-insensitively — coherence
across NAVN ↔ INNH preserved.

**Synthetic contact info:**

| Field | Value |
| --- | --- |
| EPOS `<opplysning>` | `elise.testperson@example.com` (RFC 2606 reserved domain; first-name matches the proprietor's synthetic name) |
| MTLF `<opplysning>` | `+4799999999` (clearly synthetic Norwegian-format mobile) |

**Coverage value:** closes two prominent gaps:

- **`<infotype felttype="paategning"` (PAAT)** — first scenario
  hitting `WritePaategning`. The fixture is the self-closing
  `endringstype="U"` form, so all five children (`<infotype>`,
  `<register>`, three `<tekstlinje>`s) short-circuit on IsEmpty and
  the writer emits an empty `<infotype felttype="paategning"
  endringstype="U" />`. The writer's basic plumbing
  (`WriteInfoElementStart` + `WriteInfoElementEnd`) is now exercised;
  a future PAAT-N fixture would add coverage of the populated child
  elements.
- **`samendringer felttype="INNH"` (Innehaver, type=R)** — first
  scenario covering the proprietor role-felttype, completing the
  collection alongside `LEDE` / `MEDL` / `VARA` (S3),
  `DAGL` (S17, S24), `BOBE` (S22).

Also adds:

- `samendringer felttype="DAGL" endringstype="N"` (with full person
  record) — variant of S17's identifier-only DAGL-U.
- A new instance of the cross-record name coherence pattern (NAVN ↔
  INNH on the same person, in the canonical ENK shape).
- A second instance of the relative/sibling structural pattern (after
  S11's Bjørn + Birk siblings on the same address).

## Scenario 25 — Isolated R-FV (Frivillighetsregisteret-status) on an FLI

**Change type:** A single `<infotype felttype="R-FV" endringstype="N">`
on an FLI (forening), carrying a single-character flag value
`<opplysning>J</opplysning>` (`J` = Ja / Yes — registered in
**Frivillighetsregisteret**, the Norwegian register of voluntary
organizations). `R-FV` is the Frivillighetsregister-flag — distinct
from `R-FR` (Foretaksregisteret), `R-MV` (MVA-registeret), and `R-SR`
(Stiftelsesregisteret) which all have their own felttype codes. `R-FV`
naturally appears on FLI subjects since FLIs are the form most
commonly registered in Frivillighetsregisteret.

**Parties:**

| Role | Type | Identifier | Note |
| --- | --- | --- | --- |
| Subject organization | FLI | `316289320` | Founded 2026-02-27; `datoSistEndret` is two days before `head/dato` |

**Coverage value (marginal):** hits the same parser branch as the
default-length simple-infotype records in Scenario 11 (FORM, ISEK, MÅL,
STID) and Scenario 12 (VEDT). The parser doesn't switch on the
felttype string, so this is the same code path with a different
felttype label. Useful as an isolated single-infotype-under-enhet test
covering the `R-FV` value specifically; for unique parser-branch
coverage it adds nothing over Scenarios 11 / 12.

# Coverage status

This section maps the parser/writer surface area against the current
scenario corpus. "Covered" means at least one scenario triggers the
named code path; "uncovered" means no scenario does.

The format-spec doc ([CcrXmlFormat.md](./CcrXmlFormat.md)) describes
each writer's output structure in full; this section is purely about
which writers are exercised by fixtures.

## Writer methods (`CcrFlatFileProcessor.CcrXmlWriter`)

| Writer | Output element | Status | Scenarios |
| --- | --- | --- | --- |
| `WriteHead` | `<head>` | ✅ covered | All |
| `WriteFooter` | `<trai>` | ✅ covered | All |
| `WriteOrganizationStart` / `WriteOrganizationEnd` | `<enhet>` | ✅ covered | All |
| `WriteSimpleInfoType` (default-length value) | `<infotype><opplysning>` | ✅ covered | S11 (FORM, ISEK, MÅL, STID), S12 (VEDT), S22 (FORM, MÅL, STID), S23 (FORM, MÅL), S24 (—), S25 (R-FV) |
| `WriteSimpleInfoType` (boolean-length value) | `<infotype felttype="R-MV"><opplysning>` | ✅ covered | S4 |
| `WriteEpostAddresse` | `<infotype felttype="EPOS"><opplysning>` | ✅ covered | S14, S18, S24 |
| `WriteInternettAdresse` | `<infotype felttype="IADR"><opplysning>` | ✅ covered | S14 |
| `WriteMobiltelefon` | `<infotype felttype="MTLF"><opplysning>` | ✅ covered | S14, S18, S24 |
| `WriteTelefon` | `<infotype felttype="TFON"><opplysning>` | ✅ covered | S18, S24 |
| `WriteTelefax` | `<infotype felttype="TFAX"><opplysning>` | ❌ **uncovered** | — |
| `WriteFmva` | `<infotype felttype="FMVA"><opplysning>` | ✅ covered | S19 |
| `WriteAddress` (Norwegian — postnr populated) | `<infotype felttype="FADR\|PADR">…<postnr>…` | ✅ covered | S7, S8, S11, S13, S22, S23 |
| `WriteAddress` (foreign — poststed populated, no postnr) | `<infotype felttype="FADR">…<poststed>…` | ✅ covered | S23 |
| `WriteAddress` (empty / self-closing on endringstype="U") | `<infotype felttype="PADR\|MTLF\|EPOS\|TFON\|paategning" endringstype="U" />` | ✅ covered | S13 (PADR), S14 (MTLF), S18 (EPOS, TFON), S24 (paategning, TFON) |
| `WriteName` | `<infotype felttype="NAVN"><navn1>…` | ✅ covered | S10, S11, S22, S23, S24 |
| `WriteNaeringskode` (with `<gyldighetsdato>`) | `<infotype felttype="naeringskode"><naeringskode><gyldighetsdato>…` | ✅ covered | S5 |
| `WriteNaeringskode` (without `<gyldighetsdato>`) | `<infotype felttype="naeringskode"><naeringskode><hjelpeenhet>…` | ✅ covered | S22 |
| `WritePaategning` (empty / `endringstype="U"`) | `<infotype felttype="paategning" endringstype="U" />` | ✅ covered | S24 |
| `WritePaategning` (with content) | `<infotype felttype="paategning"><infotype><register><tekstlinje>…` | ❌ **uncovered** | — |
| `WriteUlov` | `<infotype felttype="underlagtHjemlandetsLovgivning">…` | ✅ covered | S23 |
| `WriteUreg` | `<infotype felttype="registrertHjemlandetsRegister">…` | ✅ covered | S20, S23 |
| `WriteKatg` | `<infotype felttype="KATG">…` | ❌ uncovered (parser ignores KATG records — writer is dead code) |
| `WriteTkn` | `<infotype felttype="TKN">…` | ❌ uncovered (parser ignores TKN records — writer is dead code) |
| `WriteSamendringStart` / `WriteSamendringEnd` (type="K", data="D") — knytning data | `<samendringer type="K" data="D">…<knytningOrganisasjonsnummer>…` | ✅ covered | S1 (REGN), S2 (REGN×2), S6 (REGN×2), S9 (REGN), S10 (BEDR-N + BEDR-U), S22 (KDEB) |
| `WriteSamendringStart` / `WriteSamendringEnd` (type="R", data="D") — rolle data | `<samendringer type="R" data="D">…<rolleFoedselsnr>…` | ✅ covered | S3 (LEDE/MEDL/VARA), S11 (LEDE/MEDL), S17 (DAGL), S22 (BOBE), S24 (DAGL, INNH) |
| `WriteSamendringStart` / `WriteSamendringEnd` (type="S", data="T") — free-text samendring | `<samendringer type="S" data="T"><plassering><samendringfritTekstlinje>` | ✅ covered | S11 (SIGN), S15 (SIGN) |
| `WriteSamendringStart` / `WriteSamendringEnd` (type="R", data="T") — free-text rolle | `<samendringer type="R" data="T"><rollefritFoedselsnr><rollefritTekstlinje>` | ❌ **uncovered** | — |
| `WriteSamendringStart` / `WriteSamendringEnd` (type="K", data="T") — free-text knytning | `<samendringer type="K" data="T"><knytningfritOrganisasjonsnummer><knytningfritTekstlinje>` | ❌ **uncovered** | — |
| `WriteSamendringStart` / `WriteSamendringEnd` (default fallback — unknown type, data="T") | `<samendringer …>` empty | ❌ uncovered (only triggered by an unrecognized `type` value, plus a warning log) |
| `WriteSamendringutgaar` | `<samendringUtgaar felttype="SAMU"><samendringstype>` | ✅ covered | S17 |
| `WriteStatus` (no `<kjennelsesdato>`, `endringsType="U"`) | `<status felttype="X" endringstype="U" />` | ✅ covered | S16 (SKRR), S18 (—), S24 (TFON-U is on `<infotype>`, not status) |
| `WriteStatus` (no `<kjennelsesdato>`, `endringsType="N"`) | `<status felttype="X" endringstype="N" />` | ✅ covered | S17 (OPPL) |
| `WriteStatus` (with `<kjennelsesdato>`, `endringsType="N"`) | `<status felttype="KONK" endringstype="N"><kjennelsesdato>` | ✅ covered | S21 |
| `WriteStatus` (`endringsType="N"` but kjennelsesDato whitespace) | covered by S17 (OPPL-N: guard's first clause true, second clause false) | ✅ covered | S17 |
| `WriteOptionalTextElementNode` IsEmpty short-circuit | (every writer that takes optional fields hits this) | ✅ covered | Many |

## Parser cases (`CreateSamendringerNodeData`, `CreateSamendringerNodeText`)

The flat-file parser has dedicated cases per record type. The XML
`felttype` attribute value comes from the parser's `recordType` string,
so the felttype values seen in scenarios indicate which parser cases
have been exercised.

### `samendringer type="R"` (rolle / person) felttypes seen

| Felttype | Meaning | First seen |
| --- | --- | --- |
| `LEDE` | Leder / chair | S3 |
| `MEDL` | Medlem / member | S3 |
| `VARA` | Vararepresentant / deputy | S3 |
| `DAGL` | Daglig leder / CEO | S17 |
| `BOBE` | Bobestyrer / estate trustee | S22 |
| `INNH` | Innehaver / sole proprietor | S24 |

The parser routes ~50 different role-type record codes through the
same `CreateSamendringerNodeData`/`type="R"` body, so these six are
representative of the writer code path. Other role-type codes
(e.g. `STYR`, `PROK`, `REVI`, `SIGN`, `KOMP`, `KONT`, `NEST`, etc.)
share the same parser case body and writer path.

### `samendringer type="K"` (knytning / org-to-org) felttypes seen

| Felttype | Meaning | First seen |
| --- | --- | --- |
| `REGN` | Regnskapsfører / accountant | S1 |
| `BEDR` | Bedrift / sub-enterprise → main-enterprise link | S10 |
| `KDEB` | Konkursdebitor / debtor (link from konkursbo to bankrupt org) | S22 |

As above, ~30 different connection-type record codes share the same
parser body and writer path.

### `samendringer data="T"` (free-text) felttypes seen

| Type | Felttype | Meaning | First seen |
| --- | --- | --- | --- |
| `S` | `SIGN` | Free-text signing-rule paragraph | S11 |
| `R` | _(any)_ | Free-text role line | ❌ **uncovered** |
| `K` | _(any)_ | Free-text knytning line | ❌ **uncovered** |

### Status felttypes seen

| Felttype | Branch | Scenario |
| --- | --- | --- |
| `KONK` | with `<kjennelsesdato>` | S21 |
| `OPPL` | no kjennelsesdato, `endringstype="N"` | S17 |
| `SKRR` | no kjennelsesdato, `endringstype="U"` | S16 |

The parser routes ~33 status record types through the no-kjennelsesdato
path; KONK is the only one with the kjennelsesdato slice. All
representative WriteStatus sub-cases are covered; the remaining
felttype labels (`AKKO`, `OPFI`, `OSDL`, `TVKA`, etc.) would only
exercise different `recordType` strings, not new code paths.

### Direct infotype felttypes seen

| Felttype | Writer | Scenario |
| --- | --- | --- |
| `EPOS` | `WriteEpostAddresse` | S14, S18, S24 |
| `IADR` | `WriteInternettAdresse` | S14 |
| `MTLF` | `WriteMobiltelefon` | S14, S18, S24 |
| `TFON` | `WriteTelefon` | S18, S24 |
| `TFAX` | `WriteTelefax` | ❌ **uncovered** |
| `FMVA` | `WriteFmva` | S19 |
| `FADR` | `WriteAddress` | S7, S8, S11, S13, S22, S23 |
| `PADR` | `WriteAddress` | S13, S22, S23 |
| `NAVN` | `WriteName` | S10, S11, S22, S23, S24 |
| `naeringskode` (NACE / SN25) | `WriteNaeringskode` | S5, S22 |
| `paategning` (PAAT) | `WritePaategning` | S24 (empty form only — content sub-case still uncovered) |
| `underlagtHjemlandetsLovgivning` (ULOV) | `WriteUlov` | S23 |
| `registrertHjemlandetsRegister` (UREG) | `WriteUreg` | S20, S23 |
| `EDAT` / `BDAT` / `NDAT` (date-only) | `WriteSimpleInfoType` (with empty-skip) | S10 (EDAT) |
| `KATG`, `TKN ` | parser-ignored | n/a |
| `KAPI` | parser-ignored | n/a |
| Fullmaktsnoder (`FMKA`, `FMAK`, `FMAP`, `FMKL`, `FMUU`, `FSTR`, `TRAK`, `KLAN`) | parser-ignored | n/a |

## Organisasjonsform values seen

`AS`, `AAFY`, `BEDR`, `ENK`, `ESEK`, `FLI`, `KBO`, `NUF`, `UTLA`. The
parser does not branch on form, so this is documentary coverage only.

## Endringstype / status values seen

| Value | Meaning | Status |
| --- | --- | --- |
| `N` | Ny / new | ✅ covered |
| `U` | Utgår / expired | ✅ covered |
| `K` | Kopi av tidligere sendt opplysning / retransmission of previously-sent record | ❌ **uncovered** — controls the `IsNewOrUpdateChange` guard inside NACE / PAAT / ULOV / UREG cases (returns true). Used in full-snapshot deliveries where every current record is re-sent as a "copy"; payload-wise identical to `N`. The `IsNewOrUpdateChange("K")` branch has never been hit by a scenario |

## Cross-record / cross-scenario structural coverage

| Pattern | Status | Scenarios |
| --- | --- | --- |
| Same SSN appearing in two records of one enhet (role transition) | ✅ covered | S3 (P1: LEDE-N + MEDL-U; P6: MEDL-N + VARA-U) |
| Multi-word `<fornavn>` | ✅ covered | S3 (Ola Test, Kari Marit) |
| `<mellomnavn>` populated | ✅ covered | S11, S22, S24 |
| Persons sharing an address (preserved synthetic shared-vs-distinct) | ✅ covered | S3, S11, S24 |
| `c/o <person>` in address adresse1 | ✅ covered | S8, S11 |
| `v/Adv. <person>` (lawyer reference) in address adresse1 | ✅ covered | S22 |
| `c/o <organization>` in address adresse1 | ✅ covered | S23 |
| Self-closing `<infotype>` (all sub-fields short-circuit) | ✅ covered | S13, S14, S18, S24 |
| Self-closing `<status>` | ✅ covered | S16, S17 |
| Mixed `<infotype>` + `<samendringer>` under one enhet | ✅ covered | S10, S11, S17, S22, S23, S24 |
| Multiple infotype records with same felttype (long text split) | ✅ covered | S11 (3× FORM) |
| Person referenced in two different node families (full record + name in c/o) | ✅ covered | S11 (chair as FADR c/o), S22 (BOBE as PADR v/Adv.), S24 (INNH as NAVN) |
| `foersteOverfoering="J"` (initial registration) | ✅ covered | S11, S22, S23 |
| `datoSistEndret` differing from `head/dato` | ✅ covered | S6, S7, S9, S18, S19, S25 |
| Multiple `<enhet>` records in one batch file | ❌ **uncovered** — every scenario is single-enhet. The `while (await Peek)` loop's per-org reset and the `<trai antallEnheter>` count are only exercised at N=1 |
| Cross-scenario reference (one scenario's enhet org appears as another's knytning) | ✅ covered | S21 ↔ S22 |

## Header attributes

| Pattern | Status |
| --- | --- |
| `head type="A"` (Ajourhold / update batch) | ✅ covered (every scenario) |
| `head type` ≠ `"A"` (e.g. dump / full-load) | ❌ **uncovered** |
| `head` with multiple HEAD records in the file | ❌ **uncovered** — parser tolerates "we ignore any header records after the first one" but no scenario tests it |
| Parsing past the `<trai>` trailer | ❌ **uncovered** — parser comment: "to keep legacy behavior, we will continue parsing until the end of the input" — dead path |

## Top remaining gaps, in priority order

1. **`<samendringer data="T" type="R">`** — free-text role with
   `<rollefritFoedselsnr>` + `<rollefritTekstlinje>`. New writer
   sub-case.
2. **`<samendringer data="T" type="K">`** — free-text knytning with
   `<knytningfritOrganisasjonsnummer>` + `<knytningfritTekstlinje>`.
   New writer sub-case.
3. **`<infotype felttype="paategning">` with content** — exercises
   the populated form of `WritePaategning` (`<infotype>`, `<register>`,
   three `<tekstlinje>`s). The empty/U form is in S24.
4. **`endringstype="K"` (Kopi av tidligere sendt opplysning)** — used
   for full-snapshot retransmissions; payload-wise identical to `N`,
   but exercises the second arm of `IsNewOrUpdateChange`. Gates the
   conditional optional-field reads inside NACE / PAAT / ULOV / UREG
   parser cases. No existing scenario carries a `K` record.
5. **`<infotype felttype="TFAX">`** — telefax. Same writer family as
   TFON / EPOS / etc.; only the dedicated parser case is missing.
6. **Multiple `<enhet>` records in a single batch file** — the
   `while (await Peek)` loop and `<trai antallEnheter>` accumulator
   only run at N=1 today.
7. **`<head type>` ≠ `"A"`** — dump / full-load batch types.
8. **Repeated `<head>` after the first** — parser swallows them
   silently; would catch a future regression that started enforcing
   single-header.
9. **`<infotype felttype="BDAT">` / `"NDAT"`** — date-only branch is
   covered for `EDAT` (S10); the same code path applies to BDAT and
   NDAT.

### Record types the parser skips (de-facto out of scope for DB-import tests)

The parser is a port of an older Altinn-2 implementation, and several
record families have been ignored at the parser switch *for many
years*. These are not active TODOs — Altinn-register has never made
use of them — but knowing the list is useful because the
parser+XML-conversion silently drops these records before any DB code
sees them. **A scenario that "exercises" one of these records cannot
meaningfully assert anything on the DB side**, so they're effectively
out of scope for `CcrPartialXmlUpdateTests` and the DB-import flow as
it stands.

Treat the list as a signal of which BR record types are *not relevant*
to test against the import pipeline:

| Record type | Status | Purpose in BR spec |
| --- | --- | --- |
| `KAPI` | parser explicit `// altinn doesn't use these, so we ignore them` | Kapital opplysninger (currency, paid-in / bound capital, free-text descriptions, up to 4×70 chars) |
| `KATG` / `TKN ` | parser explicit `// no longer in use, so we ignore it`; `WriteKatg` / `WriteTkn` writer methods exist but are dead code | Legacy categorization records |
| `FMKA`, `FMAK`, `FMAP`, `FMKL`, `FMUU`, `FSTR`, `TRAK`, `KLAN` | parser explicit `// not in use, ignored` | Fullmaktsnoder — capital-related authorizations (kapitalforhøyelse, egne aksjer, avtalepant, konvertibelt lån, utbytte, finansielle instrumenter, klausuler) |
| `INST` | not in parser switch | Sektorkode (3-char) — officially deprecated in the BR spec (`Utgår 1.1.2012. Erstattes av ISEK`); ISEK (4-char, handled) is the replacement |
| `MANR` | not in parser switch | Matrikkelnummer-records (one per registered cadastral parcel) |
| `INSO` | not in parser switch | "Under insolvensbehandling" — recently-added insolvency status |

If at any point Altinn-register decides to take in (for example)
`INSO` insolvency status, the work is to add the parser case + writer
method first; only then can XML-driven scenarios meaningfully test
the downstream DB behavior. A fixture without parser support will
just silently get dropped at parse time.

The same logic applies to the per-record fields the parser drops on
records it *does* handle (`Endret av`, `Korrekt orgnr`, `Type
overføring`, `Linjenummer` / `Vegadresseid` on addresses, `Dato reg.
i MVA` on R-MV records, `Antall records` on the trailer) — see the
[Felter parser-en ikke beholder](./CcrXmlFormat.md#felter-parser-en-ikke-beholder-i-xml-en)
section in the format spec for the full inventory.
