# CCR XML format reference

This document describes the structure and semantics of the
`batchAjourholdXML` documents that the CCR flat-file processor
([`CcrFlatFileProcessor`](../../../src/Altinn.Register.Integrations.Ccr.FileImport/CcrFlatFileProcessor.cs))
emits. One document is produced per Norwegian organization (`<enhet>`)
that appears in a CCR (Central Coordinating Register / Enhetsregisteret
+ Foretaksregisteret) batch update file. Each `ScenarioN.xml` in this
directory is one such document.

> **Authoritative source.** The canonical reference for the flat-file
> format is BR's own spec, shipped alongside this directory as
> [`batch-ajourhold - formatbeskrivelse pr.25.04.2025.doc`](./batch-ajourhold%20-%20formatbeskrivelse%20pr.25.04.2025.doc)
> (Brønnøysundregistrene, "Formatbeskrivelse for batch ajourhold fra
> Enhetsregisteret", 2025-04-25). When this document and the BR spec
> disagree, the BR spec wins. Discrepancies between the BR spec and
> our parser implementation are noted inline (see "Spec discrepancy"
> callouts and the [Felter parser-en ikke beholder](#felter-parser-en-ikke-beholder-i-xml-en) section).

> **Source format.** The XML is *output* from the parser, not input.
> The actual transport format from Brønnøysundregistrene (BR) is a
> Latin-9 encoded fixed-width flat-file with one record per line. Each
> line starts with a 4-character record-type code (`HEAD`, `ENH `,
> `EPOS`, `LEDE`, `KONK`, `TRAI`, etc.) followed by columns at fixed
> offsets. The flat-file parser reads those columns and translates
> them into the XML structure described here, so all of the
> "uncovered" / "unknown" / "trimmed" semantics in this document are
> ultimately driven by what BR puts in the original flat-file lines.

## Top-level structure

```xml
<?xml version="1.0" encoding="utf-8"?>
<batchAjourholdXML xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                   xsi:noNamespaceSchemaLocation="batchAjourholdXML_versjon2_1.xsd">
  <head ... />
  <enhet ...>
    ... records ...
  </enhet>
  <trai ... />
</batchAjourholdXML>
```

Every document has exactly one `<head>`, one `<enhet>`, and one
`<trai>`. The schema reference (`batchAjourholdXML_versjon2_1.xsd`) is
written verbatim — there is no actual XSD bundled with the parser, the
attribute is a marker.

> **Multiple enheter in one transport file.** The flat-file format
> supports multiple `ENH ` blocks in a single physical file (and a
> single `HEAD` / `TRAI` pair). The parser splits each `ENH ` block
> into its own `batchAjourholdXML` output document — so although the
> source flat-file may contain N enheter, downstream consumers see N
> XML documents, each shaped exactly as above.

## `<head>` — batch metadata

```xml
<head avsender="ER" dato="20260504" kjoerenr="05783" mottaker="ALT" type="A" />
```

Self-closing element. All five attributes are always present.

| Attribute | Length | Meaning | Typical / observed values |
| --- | --- | --- | --- |
| `avsender` | 2 | Sender code | `ER` (Enhetsregisteret) |
| `dato` | 8 | Batch transmission date | `YYYYMMDD` |
| `kjoerenr` | 5 | Run / job number for the day | `05783` (zero-padded counter) |
| `mottaker` | 3 | Receiver code | `ALT` (Altinn) |
| `type` | 1 | Batch type | **`A`** = Ordinær ajourholdsdata-utveksling (incremental update — every batch in the test corpus is type `A`). **`S`** = Data bestilt via "SKD-knappen" (manual one-off export, with several ENH-fields blanked out — see "Felter parser-en ikke beholder" below). **`K`** = Knytningsfil med begrensede enhetsdata (separate file for receivers that don't have all entities, containing only `HEAD + ENH + NAVN + FADR + PADR + TRAI`). The parser doesn't switch on this attribute |

The flat-file parser:

- Tolerates a second `HEAD` record after the first (silently ignored).
- Continues reading records past the `TRAI` trailer until end of input
  ("to keep legacy behavior" per the source comment).

## `<enhet>` — the organization being updated

```xml
<enhet organisasjonsnummer="316289177"
       organisasjonsform="NUF"
       hovedsakstype="E"
       undersakstype="EN"
       foersteOverfoering="N"
       datoFoedt="20130319"
       datoSistEndret="20260504">
  ... change records ...
</enhet>
```

| Attribute | Length | Meaning | Notes |
| --- | --- | --- | --- |
| `organisasjonsnummer` | 9 | Norwegian org-number | mod-11 valid, the unique identifier in BR |
| `organisasjonsform` | 4 | Organization-form code | See [Organization forms](#organization-forms) below. The parser does not branch on this attribute |
| `hovedsakstype` | 1 | Main case type | **`N`** = Ny enhet, **`E`** = Endring på enhet, **`S`** = Sletting av enhet, **`L`** = Slettet enhet vekket til live. See [Case types](#case-types) |
| `undersakstype` | 4 | Sub case type | E.g. `EN`, `EBTC`, `OPPL`, `KORR`, `NY`. Free-text field, parser does not switch on it. Special values: `ETYP` or `OMDA` indicates the enhetstype itself changed (only on `hovedsakstype="E"`); `DUBL` / `SMSL` indicates this enhet is being deleted as a duplicate / merged — see "Korrekt orgnr" under [Felter parser-en ikke beholder](#felter-parser-en-ikke-beholder-i-xml-en) below |
| `foersteOverfoering` | 1 | First transfer flag | **`J`** = first transfer (initial registration of this org to Altinn), **`N`** = not first transfer (incremental update), **`L`** = was deleted, now revived (used when an entity that was previously deleted is "vekket til live" — the parser doesn't branch on this) |
| `datoFoedt` | 8 | Founded / established date | `YYYYMMDD`. For initial registrations this often matches `head/@dato`; for older orgs being updated, it's the historical founding date |
| `datoSistEndret` | 8 | Last-changed date | `YYYYMMDD`. Usually equal to `head/@dato` but can be earlier if BR processed the change on a previous day |

### Organization forms

Observed in the corpus (parser does not branch on form, so the list is
documentary):

| Form | Meaning |
| --- | --- |
| `AAFY` | Underenhet til foretak / lag |
| `AS` | Aksjeselskap (limited company) |
| `BEDR` | Bedrift / sub-enterprise |
| `ENK` | Enkeltpersonforetak (sole proprietorship) |
| `ESEK` | Eierseksjonssameie (condominium owners' association) |
| `FLI` | Forening / lag / innretning (association) |
| `KBO` | Konkursbo (bankruptcy estate) |
| `NUF` | Norskregistrert utenlandsk foretak (Norwegian-registered foreign branch) |
| `UTLA` | Utenlandsk foretak (foreign enterprise itself) |

Other BR-defined forms (e.g. `ANS`, `DA`, `STAT`, `KOMM`, `STI`, `SA`,
`PERS`, `BBL`, `BRL`, …) follow the same parser path — they have the
same attribute structure.

### Case types

The combination of `hovedsakstype` + `undersakstype` indicates *what
kind* of change this enhet update represents. Fixtures cover:

| `hovedsakstype` | `undersakstype` | Pattern |
| --- | --- | --- |
| `E` | `EN` | Endring av næringskode-klassifisering / standard incremental change |
| `E` | `EBTC` | Endring som påvirker BTC-felter / incremental change touching specific fields |
| `E` | `OPPL` | Endring relatert til oppløsning / dissolution-related change |
| `E` | `KORR` | Korreksjon av tidligere overført data / correction of previously transferred data |
| `N` | `NY` | Nyregistrering (used together with `foersteOverfoering="J"` — initial registration of a new org) |

`hovedsakstype` can also be `S` (Sletting av enhet) or `L` (Slettet
enhet vekket til live) per the BR spec. Neither appears in the test
corpus but both are documented in `batch-ajourhold - formatbeskrivelse pr.25.04.2025.doc`.
On `S`-deletion of a `BEDR` (sub-enterprise), the format spec mandates
that an `NDAT` record (with the closing date) is sent alongside the
`<enhet>` — the parser handles `NDAT` via the simple-default-length
date-only branch.

The parser does not branch on these values; they are documentary
metadata that downstream consumers can use to classify the event.

## Samendret / atomic-replacement semantics

The BR format spec defines a **samendring rule**: certain
multi-component opplysninger are sent as a *complete set* whenever any
single component changes. Receivers must therefore replace the entire
set on `endringstype="N"` records, not merge field-by-field.

**Multi-field opplysninger that follow the samendring rule:**

| Opplysning | Behavior on change |
| --- | --- |
| Forretningsadresse (`FADR`) | One record carrying all sub-fields (postnr, landkode, kommunenr, poststed, adresse1, adresse2, adresse3) — when any sub-field changes, the whole record is re-sent |
| Postadresse (`PADR`) | Same as FADR |
| Navn (`NAVN`) | One record carrying all five `navn1..navn5` lines + `rednavn` — long names spill over and the entire set is re-sent on any name change |
| Kapital (`KAPI`) | Up to 4 records (one per text line) re-sent in order on any change. Parser ignores KAPI |
| Næringskode (`NACE` / `SN25`) | When any næringskode changes, **all current næringskoder are re-sent**. Receiver should delete existing and replace with the new set |
| Formål (`FORM`) | Multi-line text (up to N records, 70 chars each) — when any line changes, the entire text is re-sent. Same applies to `VFOR` (vedtektsfestet formål) |
| Påtegning (`PAAT`) | When any påtegning is added/changed/removed, **all current påtegninger are re-sent**. Removal of all påtegninger is signalled with a single `PAAT U` |

**Role/connection families that follow the samendring rule** (when one
record in the family changes, the entire family is replaced —
historical entries go to BR's history, not to the batch):

| Samendringstype | Member roles/connections |
| --- | --- |
| `STYR` (Styre / board) | `LEDE` (Leder), `NEST` (Nestleder), `MEDL` (Medlem), `OBS ` (Observatør) |
| `DELT` (Deltakere) | `DTSO` (Deltaker med solidarisk ansvar), `DTPR` (Deltaker med proratarisk ansvar) |
| `SIGN` (Signatur) | `SIGN`, `SIFE` (signatur i fellesskap), `SIHV` (signatur hver for seg) |
| `PROK` (Prokura) | `PROK`, `KENK` (eneprokura), `KGRL` (felles-/grupperprokura) |

The full samendringstype-to-rolletype map is defined in BR's
"kodeoversikt" (not shipped with the format spec).

**Why this matters for testing:**

- A scenario showing a single `LEDE-N` (new chair) implies the spec
  expects every other member of `STYR` (Nestleder, Medlem, Observatør)
  to be re-sent in the same batch — fixtures that show only LEDE
  without the rest of the board may be incomplete relative to real
  production batches.
- Receivers must implement set-replacement, not merge, on these
  felttypes.
- A `SAMU` record (samendringUtgaar) signals that an entire family is
  being expired — every related role/knytning/fritekst is then
  re-sent with `endringstype="U"` in the same batch.

**Status records are NOT samendret.** Status records (`KONK`, `OPPL`,
`SKRR`, etc.) are independent flags — multiple statuses can be active
on the same enhet at the same time. The spec also notes that the
*same status code can be reported multiple times* with `endringstype="N"`,
so receivers must be **idempotent** on status records (a "new"
incoming status that already exists in the DB is not an error).

## Record families inside `<enhet>`

The parser recognises four structural element families as direct
children of `<enhet>`:

1. `<infotype>` — direct attributes of the org (name, address, contact
   info, NACE code, foreign-register link, etc.)
2. `<samendringer>` — *related changes*: roles (people) and connections
   (org-to-org), in two flavours: data records (`data="D"`) and
   free-text records (`data="T"`)
3. `<status>` — lifecycle / legal-state flags (bankruptcy, dissolution,
   probate court, etc.)
4. `<samendringUtgaar>` — pointer telling consumers that an entire
   `<samendringer>` family is being expired

Records can appear in any order under `<enhet>` and there can be any
number of any family. A single enhet update can mix all four families
(see [Scenario 17](./Scenario17.xml), [Scenario 22](./Scenario22.xml),
[Scenario 23](./Scenario23.xml) for examples).

## `<infotype>` — direct enhet info

Every `<infotype>` carries two attributes:

| Attribute | Meaning |
| --- | --- |
| `felttype` | Identifies which information field this record describes (e.g. `EPOS`, `NAVN`, `FADR`, `naeringskode`, `R-MV`, …) |
| `endringstype` | The change-type for this record: `N` (Ny eller endret / new or changed), `U` (Utgått / expired), `K` (**Kopi av tidligere sendt opplysning** / copy of previously-sent info). For `U` records the source flat-file has all value columns blank, and every optional child element short-circuits — so `<U>` records are commonly emitted as self-closing tags |

The `endringstype="K"` (Kopi) variant is used for *retransmissions* —
typically in full-snapshot deliveries where every current record is
re-sent as a "copy" rather than a delta. Payload-wise it is identical
to an `N` record. The parser's `IsNewOrUpdateChange` guard returns
true for both `"N"` and `"K"`, so the conditional optional-field
parsing in the multi-field infotypes (`naeringskode`, `paategning`,
`ULOV`, `UREG`) treats both the same.

There are five sub-shapes of `<infotype>`, corresponding to five
writer methods:

### A. Simple infotype (`<infotype><opplysning>`)

Single-value records. Output:

```xml
<infotype felttype="X" endringstype="Y">
  <opplysning>VALUE</opplysning>
</infotype>
```

This shape is used by the contact-info felttypes (`EPOS`, `IADR`,
`MTLF`, `TFON`, `TFAX`, `FMVA`) and by the long list of *default-length
simple infotypes* whose felttype value is just the 4-character record
code (sometimes trimmed of its trailing space):

| Felttype | Meaning |
| --- | --- |
| `EPOS` | Email (epostadresse) — value up to 150 chars |
| `IADR` | Internet address (URL) |
| `MTLF` | Mobile phone |
| `TFON` | Phone |
| `TFAX` | Fax |
| `FMVA` | Forenklet MVA-melding registration type (Skatteetaten VAT-art code, e.g. `BFLA`) |
| `R-MV` | Registrert i MVA-registeret. **Tri-state:** `J` = registered, `N` = was registered (no longer is), blank = never registered. Uses the boolean-length value branch in the parser; the spec also defines a "Dato reg. i MVA" date field at offset 10 that the parser does not currently extract |
| `R-FR` | Registrert i Foretaksregisteret. Tri-state `J` / `N` / blank with the same semantics as `R-MV` |
| `R-SR` | Registrert i Stiftelsesregisteret. Tri-state `J` / `N` / blank |
| `R-FV` | Registrert i Frivillighetsregisteret. Two-state `J` / `N` (the spec doesn't define a blank state for this record) |
| `RSKP` | Regnskapsinfo / accounts |
| `MÅL` | Målform (language form: `B` = bokmål, `N` = nynorsk) |
| `KTO` | Konto / account |
| `BFOR` | (BR-internal field) |
| `ARBG` | Arbeidsgiver-felt |
| `VEDT` | Vedtekter / statutes (typically a date) |
| `ISEK` | Innsendt egenkapital / member count |
| `PLFR` | Plikt til forsikring |
| `STID` | Stiftelsesdato / founding date (event date) |
| `SLFR` | Slettedato fra register |
| `NYFR` | (BR-internal field) |
| `EVDT` | Eventuell virksomhetsdato |
| `FVRP` | (BR-internal field) |
| `FVRR` | (BR-internal field) |
| `GRDT`, `GRUN`, `KJRP`, `MPVT`, `UVNO`, `UENO`, `RVFG`, `VFOR`, `FORM` | Various BR-internal classification / purpose fields. `FORM` carries the org's purpose statement, often split across multiple infotype records when long |

The parser also has cases for `EDAT`, `BDAT`, `NDAT` which use the
same `WriteSimpleInfoType` writer but **only emit if the date value is
non-empty** — i.e. an `<infotype felttype="EDAT" endringstype="X">`
with no `<opplysning>` child indicates a deleted date, not "date with
empty value".

### B. Address (`FADR` / `PADR`)

```xml
<infotype felttype="FADR" endringstype="N">
  <postnr>1234</postnr>          <!-- Norwegian postcode (4 digits) -->
  <landkode>NO</landkode>        <!-- ISO country code -->
  <kommunenr>0301</kommunenr>    <!-- BR kommune number (4 digits) -->
  <poststed>012345 Helsinki</poststed> <!-- Foreign postcode + city; only set for foreign addresses -->
  <adresse1>Testveien 1</adresse1>
  <adresse2>...</adresse2>
  <adresse3>...</adresse3>
</infotype>
```

| `felttype` | Meaning |
| --- | --- |
| `FADR` | Forretningsadresse (registered business address) |
| `PADR` | Postadresse (postal / mailing address) |

Both felttypes share the same `WriteAddress` writer; the only
difference between them is the `felttype` attribute value on the
output `<infotype>` element. All seven children are optional and short-circuit
on IsEmpty (`WriteOptionalTextElementNode`):

- `<postnr>` is used for Norwegian addresses; `<poststed>` is used for
  foreign addresses (carries the foreign postcode + city in one
  string). It's possible — though unusual — for both to be empty
  (deleted address) or both to be populated.
- `<landkode>` is `NO` for Norwegian addresses, ISO 2-letter code for
  foreign.
- `<kommunenr>` is BR's 4-digit kommune number, used for Norwegian
  addresses only. After the 2024 kommune-reform some codes were
  reassigned; the parser does not validate them.
- `<adresse1>` commonly carries a `c/o <person-name>` (S8 / S11),
  `v/Adv. <person-name>` (lawyer reference, S22), `c/o <org-name>`
  (S23), or just a plain street address (S7 / S13).
- `<adresse2>` and `<adresse3>` are populated when a single line is
  insufficient (street + postbox, postbox + sub-line, etc.).

A delete-this-address record is typically emitted as a self-closing
`<infotype felttype="PADR" endringstype="U" />` with all sub-fields
blank in the source flat-file (S13).

### C. Name (`NAVN`)

```xml
<infotype felttype="NAVN" endringstype="N">
  <navn1>...</navn1>
  <navn2>...</navn2>
  <navn3>...</navn3>
  <navn4>...</navn4>
  <navn5>...</navn5>
  <rednavn>...</rednavn>  <!-- "redigert navn" — the BR-curated display name -->
</infotype>
```

The org's registered business name. Up to five `navnN` lines (most
orgs only have `navn1`); `rednavn` is the BR-curated display version
(often identical to `navn1`, sometimes shortened/normalized for
display).

For an ENK (sole proprietorship) the convention is that `navn1` is the
proprietor's full name in capitals (S24 — `ELISE TESTPERSON`). For a
KBO (bankruptcy estate) the convention is `[bankrupt-AS-name] KONKURSBO`
(S22).

### D. NACE / industry code (`naeringskode`)

```xml
<infotype felttype="naeringskode" endringstype="N">
  <naeringskode>53.200</naeringskode>     <!-- 5-character NACE rev.2 code -->
  <gyldighetsdato>20130101</gyldighetsdato> <!-- effective-from date -->
  <hjelpeenhet>N</hjelpeenhet>             <!-- J = hjelpeenhet (auxiliary unit), N = ordinary -->
</infotype>
```

The two record codes `NACE` and `SN25` (Standard Næring 2025) both
flow through the same `WriteNaeringskode` writer and emit
`felttype="naeringskode"`. `<gyldighetsdato>` is optional; absent when
the source flat-file column is blank (S22).

### E. Påtegning (`paategning`)

```xml
<infotype felttype="paategning" endringstype="N">
  <infotype>...</infotype>     <!-- inner "infotype" — sub-classification of the påtegning -->
  <register>...</register>      <!-- which register it applies to -->
  <tekstlinje>...</tekstlinje> <!-- text line 1 -->
  <tekstlinje>...</tekstlinje> <!-- text line 2 -->
  <tekstlinje>...</tekstlinje> <!-- text line 3 -->
</infotype>
```

Free-text legal/regulatory annotations on the org. Used for things
like court-ordered notes, special legal status statements, etc. Note
that the inner `<infotype>` element is a child string element, distinct
from the outer `<infotype>` wrapper element (the parser/writer reuses
the name).

### F. Foreign-org-specific infotypes (UTLA / NUF)

These two felttypes are emitted only on foreign organizations
(`organisasjonsform="UTLA"` or `"NUF"`).

#### `underlagtHjemlandetsLovgivning` (ULOV)

```xml
<infotype felttype="underlagtHjemlandetsLovgivning" endringstype="N">
  <foretaksform>OY</foretaksform>
  <beskrivelseForetaksformHjemland>Yksityinen osakeyhtiö / Privat aktiebolag</beskrivelseForetaksformHjemland>
  <beskrivelseForetaksformNorsk>Aksjeselskap</beskrivelseForetaksformNorsk>
  <landkode>FI</landkode>
</infotype>
```

Records that the org is subject to its home country's company-law for
form `<foretaksform>` (e.g. Finnish `OY`, German `GmbH`, etc.). The
two `beskrivelse…` elements give the local-language and Norwegian
descriptions of that form.

#### `registrertHjemlandetsRegister` (UREG)

```xml
<infotype felttype="registrertHjemlandetsRegister" endringstype="N">
  <registernr>1234567-8</registernr>      <!-- the org's ID in its home register -->
  <registerNavn1>Patentti - Ja Rekisterihallitus</registerNavn1>
  <registerNavn2>Kaupparekisterijärjestelmä</registerNavn2>
  <registerNavn3>...</registerNavn3>
  <landkode>FI</landkode>
  <utenlandskPoststed>00010 Helsinki</utenlandskPoststed>
  <postadresse1>Testikatu 1 A</postadresse1>
  <postadresse2>...</postadresse2>
  <postadresse3>...</postadresse3>
</infotype>
```

Records the org's identifier in its home country's business register
(e.g. Finland's PRH / Patentti- ja rekisterihallitus, Sweden's
Bolagsverket, Germany's Handelsregister). The address fields point at
the home register's HQ, not the org's own address (the org's address
is in `FADR`).

## `<samendringer>` — related changes (roles + connections)

A `<samendringer>` element represents a single change to a *related
entity* — either a person fulfilling a role (board member, CEO,
proprietor, trustee, etc.) or a connection from this org to another
org (accountant, auditor, parent enterprise, debtor link, etc.).

Common attributes on every `<samendringer>`:

| Attribute | Meaning |
| --- | --- |
| `data` | `D` = data record (carries structured fields), `T` = text record (carries free-form text) |
| `felttype` | The role-type or connection-type code (`LEDE`, `MEDL`, `DAGL`, `INNH`, `BOBE`, `REGN`, `BEDR`, `KDEB`, `SIGN`, etc.) |
| `endringstype` | `N` / `U` / `K` (same semantics as on `<infotype>`) |
| `type` | `R` = Rolle (person), `K` = Knytning (org-to-org connection), `S` = Samendring (free-text paragraph) |

There are six combinations of `data` × `type`. The parser handles
five; `data="T" type="K"` and `data="T" type="R"` are valid but absent
from the test corpus.

### Data records — structured

#### `data="D" type="R"` — person-role record

```xml
<samendringer data="D" felttype="LEDE" endringstype="N" type="R">
  <rolleAnsvarsandel>...</rolleAnsvarsandel>     <!-- liability share -->
  <rolleFratraadt>N</rolleFratraadt>               <!-- N = currently active, F = fratraadt / stepped down -->
  <rolleValgtav>...</rolleValgtav>                 <!-- elected by -->
  <rolleRekkefoelge>1</rolleRekkefoelge>           <!-- ordinal in role list -->
  <rolleFoedselsnr>02895823468</rolleFoedselsnr>   <!-- 11-digit Norwegian fødselsnummer -->
  <fornavn>Anne</fornavn>
  <mellomnavn>Test</mellomnavn>                    <!-- middle name, often absent -->
  <slektsnavn>Testperson</slektsnavn>
  <postnr>1234</postnr>
  <adresse1>Testveien 1</adresse1>
  <adresse2>...</adresse2>
  <adresse3>...</adresse3>
  <adresseLandkode>NO</adresseLandkode>
  <personstatus>L</personstatus>                   <!-- L = Levende (alive), D = Død (deceased) -->
</samendringer>
```

All children are optional. A common pattern for `endringstype="U"`
(role being expired) is that only `<rolleFoedselsnr>` is populated — a
minimal "remove the person with this SSN from this role" record (S3 —
LEDE-U / MEDL-U / VARA-U).

The same SSN can appear in multiple `<samendringer>` records on one
enhet — for example a person being promoted from MEDL to LEDE will
have one MEDL-U record (vacating the old role) and one LEDE-N record
(taking the new one). The records *must* match on SSN for downstream
consumers to recognize it as a single role transition.

Role felttypes seen in the corpus:

| Felttype | Meaning |
| --- | --- |
| `LEDE` | Leder / chair |
| `MEDL` | Medlem / board member |
| `VARA` | Vararepresentant / deputy |
| `DAGL` | Daglig leder / CEO |
| `INNH` | Innehaver / sole proprietor (used for ENK) |
| `BOBE` | Bobestyrer / bankruptcy-estate trustee (used for KBO) |

The parser knows ~50 different role-codes that all flow through the
same code path (e.g. `STYR`, `PROK`, `REVI`, `SIGN`, `KOMP`, `KONT`,
`NEST`, `BEST`, `OBS`, `HLED`, `HMDL`, `HNST`, `HVAR`, `FFØR`, `FGRP`,
`HFOR`, `HLSE`, `KIRK`, `KMOR`, `STFT`, `READ`, `REPR`, …). The list
of recognized codes lives in [`CcrFlatFileProcessor.cs`](../../../src/Altinn.Register.Integrations.Ccr.FileImport/CcrFlatFileProcessor.cs)
and matches BR's standard role catalog.

#### `data="D" type="K"` — knytning (org-to-org connection)

```xml
<samendringer data="D" felttype="REGN" endringstype="N" type="K">
  <knytningAnsvarsandel>...</knytningAnsvarsandel>       <!-- liability share -->
  <knytningFratraadt>N</knytningFratraadt>                 <!-- N = active, F = fratraadt -->
  <knytningOrganisasjonsnummer>316289347</knytningOrganisasjonsnummer>
  <knytningValgtav>...</knytningValgtav>
  <knytningRekkefoelge>1</knytningRekkefoelge>
  <korrektOrganisasjonsnummer>000000000</korrektOrganisasjonsnummer>  <!-- usually 000000000 -->
</samendringer>
```

`<korrektOrganisasjonsnummer>` is BR's mechanism for correcting a
previously-recorded knytning that pointed at the wrong target org —
the field carries the *new* correct org-number, while
`<knytningOrganisasjonsnummer>` keeps the original (wrong) one. The
sentinel value `000000000` means "no correction".

For `endringstype="U"` (connection being expired), commonly only
`<knytningOrganisasjonsnummer>` is populated — every other field
short-circuits on IsEmpty (S10).

Knytning felttypes seen:

| Felttype | Meaning |
| --- | --- |
| `REGN` | Regnskapsfører / accountant |
| `BEDR` | Bedrift link (sub-enterprise → main-enterprise) |
| `KDEB` | Konkursdebitor (link from konkursbo to bankrupt org) |

Other recognized codes include `REVI` (auditor), `RFAD` (forretnings-/postadresse-knytning),
`READ`, `HFOR`, `KENK`, `KGRL`, `KMOR`, `OPMV`, `ORGL`, `EIKM`, `ESGR`,
`FISJ`, `FUSJ`, `DTPR`, `DTSO` — same parser body, same writer.

### Text records — free-form

Same outer shape, different body. The parser branches on `type`:

#### `data="T" type="S"` — free-text samendring paragraph

```xml
<samendringer data="T" felttype="SIGN" endringstype="N" type="S">
  <plassering>H</plassering>                       <!-- H = Heading (text placed BEFORE the roles), T = Trailer (text placed AFTER the roles) -->
  <samendringfritTekstlinje>Styrets leder alene.</samendringfritTekstlinje>
</samendringer>
```

Used for the org's *signaturrett* (signing rule) text and similar
free-form paragraphs.

#### `data="T" type="R"` — free-text role line

```xml
<samendringer data="T" felttype="..." endringstype="N" type="R">
  <rollefritFoedselsnr>...</rollefritFoedselsnr>
  <rollefritTekstlinje>...</rollefritTekstlinje>
</samendringer>
```

Free-form text annotation tied to a person. Currently no scenario in
the corpus exercises this branch.

#### `data="T" type="K"` — free-text knytning line

```xml
<samendringer data="T" felttype="..." endringstype="N" type="K">
  <knytningfritOrganisasjonsnummer>...</knytningfritOrganisasjonsnummer>
  <knytningfritTekstlinje>...</knytningfritTekstlinje>
</samendringer>
```

Free-form text annotation tied to an organization connection. No
fixture yet.

If a text record (`data="T"`) carries a `type` value that the parser
doesn't recognize, the parser still emits an empty
`<samendringer …>…</samendringer>` element pair and logs a warning.

## `<status>` — lifecycle / legal-state flags

```xml
<status felttype="KONK" endringstype="N">
  <kjennelsesdato>20260504</kjennelsesdato>  <!-- only emitted for KONK + N + non-empty -->
</status>
```

A status record marks an org as having entered (or left) a particular
legal state — bankruptcy, dissolution, probate-court takeover, merge
plan, etc. There is exactly one writer (`WriteStatus`) with a guard:

```csharp
if (endringsType is "N" && !kjennelsesDato.IsWhiteSpace())
{
    WriteOptionalTextElementNode("kjennelsesdato", kjennelsesDato);
}
```

Only `KONK` (bankruptcy) is read with a `<kjennelsesdato>` slice from
the flat-file (offset 8, length 8). Every other status record types
the parser knows about emits a `<status>` element with no
`<kjennelsesdato>` child — typically self-closing if `endringstype="U"`.

Status felttypes the parser recognizes:

| Felttype | Meaning |
| --- | --- |
| `KONK` | Konkurs / bankruptcy (with kjennelsesdato) |
| `AKKO` | Åpnet akkord (opened accord) |
| `BRSL` | Hovedforetaket slettet i hjemlandet |
| `BRKO` | Hovedforetaket konkurs/tvangsavviklet i hjemlandet |
| `BROP` | Hovedforetaket under avvikling i hjemlandet |
| `FIFO` | Finansforetak |
| `FIPL` / `FITA` | Fisjonsplan / fisjonstaker |
| `FUPL` / `FUTA` | Fusjonsplan / fusjonstaker |
| `FLYT` | Vedtak om flytting over landegrense |
| `IPF ` | Er en IPF |
| `OMPL` | Mottatt omdannelsesplan |
| `OPFI` / `OPFU` | Overdragende selskap i fisjon / fusjon |
| `OPPL` | Status oppløst |
| `OSDL`, `OSED`, `OSBA`, `OSEF`, `OSEV`, `OSKA`, `OSKP`, `OSRE`, `OSST` | Skifteretten (probate court) takeovers, classified by reason (CEO, EØFG, cooperative, capital, accountant, board, …) |
| `SKRR` | Skifteretten fra Regnskapsregisteret |
| `TVBA`, `TVDL`, `TVKA`, `TVOV`, `TVRE`, `TVRR`, `TVST` | Tvangsavviklet (forcibly disbanded), classified by reason |
| `USL ` | Status — being deleted |
| `USYS` | Status — unmanned organization |

All of these (except KONK) flow through the same parser body and emit
the same XML structure with different `felttype` attribute values.

## `<samendringUtgaar>` — pointer to expire a related-change family

```xml
<samendringUtgaar felttype="SAMU">
  <samendringstype>DAGL</samendringstype>
</samendringUtgaar>
```

Tells the consumer that the entire `samendringer` family of type
`<samendringstype>` (e.g. all DAGL records) on this enhet is being
marked as expired. Used in the dissolution flow (S17) — paired with a
`<samendringer felttype="DAGL" endringstype="U">` record that
identifies *which* DAGL person is going, plus a `<status>` change.

The `felttype` attribute on the wrapper is hardcoded to `SAMU`; the
inner `<samendringstype>` is the actual record code being expired.

## `<trai>` — batch trailer

```xml
<trai antallEnheter="1" avsender="ER" />
```

| Attribute | Meaning |
| --- | --- |
| `antallEnheter` | Count of `<enhet>` records in this batch. Always `1` in the per-org output documents. |
| `avsender` | Same as `head/@avsender` |

## Encoding and character handling

The source flat-file is decoded by the parser using
[`LegacyEncodings.Latin9`](../../../src/Altinn.Register.Integrations.Ccr.FileImport/LegacyEncodings.cs)
= **ISO-8859-15** before writing UTF-8 XML output. Norwegian
characters (`æ`, `ø`, `å`, `Æ`, `Ø`, `Å`) and the Latin-9-specific
characters (`€`, `Š`, `š`, `Ž`, `ž`, `Œ`, `œ`, `Ÿ`) round-trip
cleanly; Finnish/Swedish `ä`, `ö` used in foreign-org names also
round-trip.

> **Spec discrepancy.** The official BR format spec
> (`batch-ajourhold - formatbeskrivelse pr.25.04.2025.doc`) states
> *"Tegnsett for utvekslingsfil skal være ISO 8859.1 (windows)"*,
> i.e. **ISO-8859-1**, not the ISO-8859-15 the parser uses. The two
> overlap on every byte except a handful (notably `€` is `0xA4` in
> 8859-15 but `¤` in 8859-1). If BR really delivers 8859-1, any `€`
> character would decode incorrectly. This has not been observed
> in practice in the test corpus, but is worth flagging if a future
> incident shows mojibake on these bytes.

Each input line is padded to a minimum of 500 characters before
slicing — fields beyond the actual line length are read as
whitespace and trim to empty.

## Felter parser-en ikke beholder i XML-en

The flat-file format defines several fields per record that the
current parser **reads past but does not propagate to the XML
output**. Listed here so that anyone investigating "why doesn't the
XML have field X?" can confirm it's intentionally dropped:

### Per-record (every infotype / status / samendringer / SAMU record)

| Flat-file field | Position | Description | Why dropped |
| --- | --- | --- | --- |
| `Endret av` | 6–8 (3 chars) | Code for the avsender that last changed the record (`ER`, `FR`, `MVA`, `BB`, etc.) | Audit trail at the source — Altinn doesn't currently track per-field provenance |

### `<enhet>` extras

| Flat-file field | Position | Description | Why dropped |
| --- | --- | --- | --- |
| `Korrekt orgnr` | 40–48 (9 chars) | When this enhet is being deleted as a duplicate (`undersakstype="DUBL"`) or merged (`SMSL`), this field carries the *surviving* orgnr that the duplicate's data should be merged into | Duplicate/merge handling not implemented in the parser today; would be necessary for a future re-mapping flow |
| `Type overføring` | 49 (1 char) | Blank for ordinary delivery, `I` = Innførte data via SKD-knappen, `J` = Journaldata via SKD-knappen | The SKD-knappen subtype only matters for `head/@type="S"` batches, which the parser doesn't differentiate |

### `<infotype felttype="FADR\|PADR">` extras

| Flat-file field | Position | Description | Why dropped |
| --- | --- | --- | --- |
| `Linjenummer` | 170 (1 char) | `1` / `2` / `3` — which `<adresseN>` line carries the Matrikkel-link | Matrikkel-cross-reference not used downstream |
| `Vegadresseid` | 171–185 (15 chars) | Vegadresseid from the Matrikkel cadastral system | Same reason |

### `<infotype felttype="R-MV">` extras

| Flat-file field | Position | Description | Why dropped |
| --- | --- | --- | --- |
| `Dato reg. i MVA` | 10–17 (8 chars, `YYYYMMDD`) | Date the org was registered in the VAT register | The boolean-length-value parser branch only reads `<opplysning>` and stops; the date is in the line but not sliced |

### `<trai>` extras

| Flat-file field | Position | Description | Why dropped |
| --- | --- | --- | --- |
| `Antall records` | 15–23 (9 chars) | Total record count in the file (sanity check) | Parser doesn't enforce trailer-checksum validation; legacy lenient behavior |

### Whole-record families dropped (parser ignores at the switch level)

| Record type | What it is | Status |
| --- | --- | --- |
| `KAPI` | Kapitalopplysninger — share/equity capital with currency code, paid-in / bound, and free-text description (up to 4 lines × 70 chars) | Parser explicit: `// altinn doesn't use these, so we ignore them` |
| `KATG`, `TKN ` | Legacy categorization records | Parser explicit: `// no longer in use, so we ignore it`. The corresponding `WriteKatg` / `WriteTkn` writer methods exist but are dead code |
| `FMKA`, `FMAK`, `FMAP`, `FMKL`, `FMUU`, `FSTR`, `TRAK`, `KLAN` | Fullmaktsnoder (capital-related authorizations: kapitalforhøyelse, egne aksjer, avtalepant, konvertibelt lån, utbytte, finansielle instrumenter, klausuler) | Parser explicit: `// not in use, ignored` |
| `INST` | Sektorkode (3-char) — institutional sector code | Officially deprecated in the BR spec (`Utgår 1.1.2012. Erstattes av ISEK`); ISEK (4-char) is the replacement and is handled |
| `MANR` | Matrikkelnummer-records (one per registered cadastral parcel) | Documented in the BR spec but **not in the parser switch** — currently falls through to the `Log.UnknownOrganizationRecordType` warning |
| `INSO` | "Under insolvensbehandling" — recently-added insolvency status | Documented in the BR spec but **not in the parser switch** — currently falls through to the warning |

> **Why these gaps exist.** The current `CcrFlatFileProcessor` is a
> direct port of an older Altinn-2 implementation. The "ignored"
> records have been ignored for many years — Altinn-register has
> never made use of them, so they were never wired up. They are not
> active TODOs.
>
> **What this means for testing.** From a database-import-test
> perspective, these record types are de-facto out of scope: the
> parser+XML conversion will skip them silently before any DB code
> sees them, so a test scenario that "exercises a `KAPI` record" or
> "exercises a `MANR` record" cannot meaningfully assert anything on
> the DB side. Treat the "ignored" list as a signal of which BR
> record types are *not relevant* to the import pipeline as it stands.
>
> If at any point Altinn-register starts caring about (for example)
> `INSO` insolvency status, the work is to add the parser case +
> writer method first; only then can XML-driven scenarios test the
> downstream DB behavior.

## PII content reference

Documents derived from real production data may contain:

- **Org numbers** (9 digits, mod-11 valid) — public BR data, but
  combined with private fields they identify specific real
  enterprises.
- **Norwegian fødselsnummer** (11 digits) in person `<rolleFoedselsnr>`
  fields — strong identifier (DOB + sex + check digits + individual
  number).
- **Person names** in `<fornavn>` / `<mellomnavn>` / `<slektsnavn>`
  and in `<adresse1>` `c/o <name>` / `v/Adv. <name>` patterns.
- **Personal home addresses** in person samendringer
  (`<adresse1>` / `<postnr>` etc.).
- **Personal contact info** (email, mobile) in `EPOS` / `MTLF`
  infotypes — for ENK orgs especially, these are typically the
  proprietor's personal address/phone/email.
- **Org names** that may identify the enterprise — for `ENK` the org
  name *is* the proprietor's full name; for `KBO` it embeds the
  bankrupt-org name (`[ORG] KONKURSBO`).

For test fixtures: see [TestScenarioOverview.md](./TestScenarioOverview.md)
"anonymization conventions" section for the synthesis rules used to
replace each PII category while preserving the structural properties
that matter for parser/writer regression tests.

## Cross-references between documents

Several CCR events reference *other* organizations or persons by
identifier:

- A `samendringer type="K"` record's `<knytningOrganisasjonsnummer>`
  points at another org (the accountant, the parent enterprise, the
  bankrupt debtor, etc.). The target org is **not** included in the
  same XML document — consumers must resolve it from a separate event
  about that org, or from a snapshot of BR data.
- A person SSN appearing in a `samendringer type="R"` record may
  appear again on the same person in another enhet's update (e.g. the
  same lawyer being trustee of multiple konkursbo).
- The `c/o <name>` / `v/Adv. <name>` patterns inside `<adresse1>` are
  free-text references to a person or org — not structurally linked,
  but the name is meaningful.

In the test corpus, [Scenario 21](./Scenario21.xml) and
[Scenario 22](./Scenario22.xml) deliberately share an organization
number across files (the bankrupt AS in S21 = the KDEB target in S22)
to give consumers a multi-event story to test against.
