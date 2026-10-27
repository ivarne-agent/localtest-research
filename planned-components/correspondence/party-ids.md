# Party ids: Correspondence's 8-or-10-digit rule and the stack's 6-digit seed

## Status (2026-10-27)

- **Closed.** Option (c) landed upstream as Altinn/altinn-correspondence#2157 (`d2dc912`, 2026-10-22):
  `CreateDecisionRequest` and `CreateUserContactPointDecisionRequest` pass `urn:altinn:partyid:{id}`. Correspondence was
  synced to `d2dc912` and the `XacmlRequestFactory.cs` site of option (b) removed in task 58's follow-up
  ([`../../TODO.md`](../../TODO.md) task 58); the smoke test's app-lib and recipient checks prove the 6-digit ids pass
  without it. The rest of this page is the history.

### Earlier status (2026-10-21)

- Recommendation (b) **approved 2026-10-21 with a change** (the site accepts any 64-bit integer party id, not only
  unsigned 32-bit) and **in progress** as task 7b (worktree `.worktrees/task7b-party-ids`, branch
  `task/correspondence-party-ids`). The seed is not renumbered.
- (c), the upstream issue, and questions 2 to 5 of section 4 are open.

Research date: 2026-10-21 (Europe/Oslo), follow-up to task 7 of [`../../TODO.md`](../../TODO.md). Read-only: source,
`git log`/`gh` history and the task 7 branch's README and smoke test. Nothing was built and no container or `studioctl`
command was run. Claims that come from reading code and were not observed are marked "not verified".

Links go to [`Altinn/altinn-correspondence@6dcf561`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89). Paths
without a link are relative to `src/` at that commit (`/home/agent/code/.reference/altinn-correspondence`, which is a
shallow clone, so the history below comes from the GitHub API).

**Outcome (task 7b, 2026-10-21):** option (b) is implemented, decided with a 64-bit range instead of the unsigned 32-bit one
proposed below (`long.TryParse`, positive). The site is `XacmlRequestFactory.cs:31-36` in
[`localtest/components/correspondence/vendor/`](../../localtest/components/correspondence/vendor/UPSTREAM.md); the results are
in the component README's "Verified". Option (c), the upstream issue, is open question 2 below.

## Summary

- **The rule is a format guess, not a guard.** `StringExtensions.IsPartyId` (`Altinn.Correspondence.Common/Helpers/StringExtensions.cs:54-61`)
  accepts a string that starts with `urn:altinn:partyid`, or that is all digits **and** 8 or 10 characters long.
  `XacmlRequestFactory.CreateResourceCategory` (`Integrations/Altinn/Authorization/XacmlRequestFactory.cs:16-39`) uses it
  to decide which XACML resource attribute a party becomes. Anything that is not an organisation number, a national
  identity number or a party id by that test throws `InvalidOperationException` "RecipientId is not a valid organization
  number, person number or party id", and the API answers 500.
- **Correspondence feeds that guess a value whose type it already knows.** Every PDP request resolves the party in
  Register first and passes on the bare number `registerParty.GetPartyId().ToString()` (`AltinnAuthorizationService.cs:212-222`).
  The organisation-contact-point check does the same with an `int` (`AltinnTokenXacmlMapper..cs:46`). A 6-digit party id
  from Register is therefore rejected on Correspondence's own internal path, not on API input. The API never accepts a
  party id from a caller (section 1.3).
- **History.** On 2025-12-12 the rule went from "URN prefix only" to "or 8 digits", as "Fixed IsPartyId to be more
  lenient" inside an unrelated PR ([#1595](https://github.com/Altinn/altinn-correspondence/pull/1595)). On 2026-02-26
  every PDP request started going through the Register resolution
  ([#1762](https://github.com/Altinn/altinn-correspondence/pull/1762)). On 2026-06-30 the rule gained "or 10 digits"
  ([#2042](https://github.com/Altinn/altinn-correspondence/pull/2042), issue [#2041](https://github.com/Altinn/altinn-correspondence/issues/2041)
  "PartyId har i A3 blitt utvidet til å kunne ha 10 tegn"), because Altinn 3 Register now creates party ids from a
  sequence that starts at 1 000 000 001 ([altinn-register#858](https://github.com/Altinn/altinn-register/pull/858)).
  The same day, [#2043](https://github.com/Altinn/altinn-correspondence/pull/2043) let the factory recognise a
  `urn:altinn:partyid:` prefix ("It used to come in without it, but now it should come with it"). The resolution still
  passes the bare number. No test covers `IsPartyId` (`git grep IsPartyId -- Test` finds nothing).
- **Blast radius in the stack.** Every Correspondence request that reaches the PDP fails for a seeded party, and so
  does one notification background path:
  - app-lib's credential on all five calls it makes (`signering-brukerstyrt` cannot message its signees);
  - the recipient's dialog-token path (`content`, downloads, `markasread`, `confirm`, `purge`);
  - Altinn and ID-porten tokens used as a recipient;
  - organisation-recipient notifications with custom recipients that match a user-registered address (not verified).

  The service-owner bypass is the only path that works today. No other vendored component has a length rule: the
  typed URN parsers in Register, Authorization, Access Management and the Resource Registry take any `uint`/`int`.
  Every other smoke test and the Cypress run are green on the 6-digit seed (section 2.2).
- **Recommendation: (b) now, (c) upstream, no seed change.** Option (b) is one `#if !LOCALTEST` site at
  `XacmlRequestFactory.cs:31` that accepts any unsigned 32-bit number in the party-id branch. That branch is reached only
  after the organisation-number and national-identity-number checks, so production-shaped ids behave exactly as
  upstream's. Option (c) is an upstream change: Correspondence passes the resolved id as `urn:altinn:partyid:{id}`, the
  completion of #2043, after which the site goes away.
- **Is (a), an 8-digit seed, the "right" long-term fix because production is 8 digits? No.** Production is not "8
  digits". It is 8-digit Altinn 2 ids (`5xxxxxxx` in every docs example) plus 10-digit Altinn 3 ids from 1 000 000 001
  upwards. Register's contract has no length. The length rule exists only in Correspondence.
  - **Renumbering costs:** this repository (about 175 references in 23 files), the monorepo's Cypress config and the old
    localtest's data, the docs' `testData.json` examples, and every app `testData.json` that keys roles by `500000`.
  - **It still fixes nothing for app-supplied parties:** `signering-brukerstyrt`, the only app that sends correspondence,
    brings its own 6-digit parties (600000-700002).

## 1. The rule

### 1.1 Where it is and what it accepts

| Site | What it does with a party |
| --- | --- |
| [`Common/Helpers/StringExtensions.cs:54-61`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Common/Helpers/StringExtensions.cs#L54-L61) `IsPartyId` | `StartsWith("urn:altinn:partyid")`, or `Length` 8 or 10 and all digits. Note the missing colon: `urn:altinn:partyid…` is matched as a prefix |
| [`StringExtensions.cs:173-205`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Common/Helpers/StringExtensions.cs#L173-L205) `WithUrnPrefix` | Order: already a URN → organisation number (`^\d{9}$` or `0192:`) → national identity number (11 digits, mod 11) → `IsPartyId` → e-mail → UUID → **throws `ArgumentException`**. Used by `AltinnRegisterService.QueryParties` (`:154`) on every Register lookup |
| [`Integrations/Altinn/Authorization/XacmlRequestFactory.cs:16-39`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs#L16-L39) `CreateResourceCategory` | Order: `urn:altinn:organization:identifier-no` prefix or 9 digits → `urn:altinn:person:identifier-no` prefix or valid NIN → `urn:altinn:partyid` prefix or `IsPartyId` → **throws `InvalidOperationException`** (`:38`). The only place a party becomes the PDP request's resource attribute. Callers: the Altinn-token mapper (`AltinnTokenXacmlMapper..cs:22`), the user-contact-point mapper (`:46`), the dialog-token mapper (`Dialogporten/Mappers/DialogTokenXacmlMapper.cs:34`) and the ID-porten mapper (`Idporten/IdPortenXacmlMapper.cs:126`) |
| [`AltinnAuthorizationService.cs:210-238`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Authorization/AltinnAuthorizationService.cs#L210-L238) `CreateDecisionRequest` | `if (!party.IsPartyId())`: look the party up in Register and replace it with `registerParty.GetPartyId().ToString()`, the **bare** number (`:220`). Every non-bypassed access check goes through here (`CheckUserAccess`, `:127-143`) |
| [`AltinnAuthorizationService.cs:94-124`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn/Authorization/AltinnAuthorizationService.cs#L94-L124) `AuthorizeUserIdsForResource` | Takes `int partyId` (from Profile's `units/contactpoint/lookup`) and passes `partyId.ToString()` to the factory |
| `Integrations/Altinn/Register/AltinnRegisterDevService.cs:53` | Upstream's dev stub (not used in the stack). Its Digdir is party `50952483` (`:16`) |

`AltinnXacmlUrns.PartyId` (PEP) is `urn:altinn:partyid` (`altinn-auth/src/pkgs/Altinn.Authorization.PEP/.../Constants/AltinnXacmlUrns.cs:11`),
the same string as `UrnConstants.Party`.

### 1.2 When and why (history from the GitHub API)

| Date | Commit / PR | Author | What changed for party ids |
| --- | --- | --- | --- |
| 2024-11-21 | `50f82fd` [#511](https://github.com/Altinn/altinn-correspondence/pull/511) | CelineTrammi | `StringExtensions` created: organisation number and NIN by length only |
| 2025-12-10 | `d5752fd` [#1574](https://github.com/Altinn/altinn-correspondence/pull/1574) "Use end user party for activity and status logs" | Ceredron | `IsPartyId` introduced: **URN prefix only** |
| 2025-12-11 | `52d16d9` [#1594](https://github.com/Altinn/altinn-correspondence/pull/1594) | Roar Mjelde | `WithUrnPrefix` uses `IsPartyId` |
| 2025-12-12 | `09a9f2b` [#1595](https://github.com/Altinn/altinn-correspondence/pull/1595) "Report purged activities synchronously" | Ceredron, Roar Mjelde | "Fixed IsPartyId to be more lenient": **or 8 digits**. No issue or discussion about the length; CodeRabbit's summary: "Added support for numeric party ID format (8-digit numbers)" |
| 2026-02-13 | `1bcfdc7` [#1736](https://github.com/Altinn/altinn-correspondence/pull/1736) "Support for SI-users" | axely123 | `CreateDecisionRequest` resolves **e-mail parties** to a bare party id through Register; the factory gets its `IsPartyId` branch |
| 2026-02-26 | `79f020e` [#1762](https://github.com/Altinn/altinn-correspondence/pull/1762) "Support legacy selfidentified users" | axely123 | The condition becomes `!party.IsPartyId()`: **every** organisation and person is resolved to a bare party id. From here on every PDP request names the party by its party id |
| 2026-06-01 | `a5ade65` [#1973](https://github.com/Altinn/altinn-correspondence/pull/1973) | axely123 | Register v2 only (`GetPartyId()`) |
| 2026-06-29 | issue [#2041](https://github.com/Altinn/altinn-correspondence/issues/2041) | Ceredron | "PartyId har i A3 blitt utvidet til å kunne ha 10 tegn. Vi må derfor endre valideringen vår her" |
| 2026-06-30 | `44b6453` [#2042](https://github.com/Altinn/altinn-correspondence/pull/2042) "increased partyid length to 10" | MagnusHAa | **or 10 digits**. The PR template ticks "automated test added"; no test touches `IsPartyId` |
| 2026-06-30 | [#2043](https://github.com/Altinn/altinn-correspondence/pull/2043) "Use URN prefix if available" | Ceredron | Factory: the `urn:altinn:partyid` prefix is matched first ("It used to come in without it, but now it should come with it") |
| 2026-05-08 | [altinn-register#858](https://github.com/Altinn/altinn-register/pull/858) "feat: create party-id" | Aleksander Heintz | `register.party_inc_id_seq` `START WITH 1000000001` (`Migration/v0.55-mk-party/01-party-inc-id-seq.sql`): parties created in Altinn 3 get 10-digit ids. This is the cause of #2041 |

Reading of the authors' reasoning: nobody chose "8 or 10" as a policy. The length test is the narrowest pattern that
matched the ids the team saw: Altinn 2 ids in production and tt02 (`5xxxxxxx`), then Altinn 3's
`1 000 000 001`+. It was widened when production broke. Related history:

- [#432](https://github.com/Altinn/altinn-correspondence/issues/432) (closed, 2024) planned to use the party UUID
  internally.
- [#1334](https://github.com/Altinn/altinn-correspondence/issues/1334) moved away from party id as the Events subject.

### 1.3 Validation convenience or ambiguity guard?

A convenience. Evidence:

- **API input never contains a bare party id.** Recipients must match an organisation number (`0192:` or
  `urn:altinn:organization:identifier-no:` + 9 digits), a NIN (11 digits, mod 11), an ID-porten e-mail URN or a legacy
  self-identified URN (`API/ValidationAttributes/RecipientListAttribute.cs:39-49`). The sender is the resource's
  competent authority from the Resource Registry. A bare party id reaches the factory only from Correspondence's own
  Register resolution or from Profile's `partyId` field, where the type is known.
- **Lengths do not overlap.** An organisation number is 9 digits and a NIN is 11 digits with two control digits.
  Altinn party ids are `int` in Correspondence and `uint` in Register (`Altinn.Register.Contracts/PartyUrn.cs:17-19`,
  keys `altinn:party:id` and `altinn:partyid`), so at most 10 digits. Both identifier branches come **before** the
  party-id branch in the factory and in `WithUrnPrefix`, so the length test adds no protection against misreading an
  organisation number or a NIN.
- **The only real ambiguity, a 9-digit party id, is untouched by the rule.** Such an id would take the
  organisation-number branch whatever `IsPartyId` says. Neither range produces one: Altinn 2 ids are 8 digits and
  Altinn 3 ids start at 10.
- **The Register's 8-digit organisation numbers are no counter-example.** The Register's Altinn 2 import pads them with
  a leading zero (`A2PartyImportService.cs:786-797`, "there are some organization numbers that are 8 in length"). That
  is an import concern: Correspondence's input regex requires 9 digits, so an 8-digit organisation number never
  reaches `IsPartyId`.

## 2. Blast radius

### 2.1 Correspondence paths a 6-digit party id breaks

The test data's parties: Digdir 500006 is the competent authority of all three seeded Correspondence resources. Sophie
Salt is 501337. `signering-brukerstyrt`'s own parties are 600000-600003 and 700000-700002. The bypass
(`EvaluateBypassConditions`, `AltinnAuthorizationService.cs:146-187`) skips the PDP only for a token with
`altinn:serviceowner` whose `consumer` is the resource's competent authority.

| Path | Who | Party in the PDP request | Result | Evidence |
| --- | --- | --- | --- | --- |
| `POST attachment`, upload, `GET attachment/{id}`, `POST correspondence`, `GET correspondence/{id}/details` | app-lib's credential (`GetTestOrgToken`, no `altinn:serviceowner`) | sender Digdir → 500006 | 500 | **Observed** on the stack: task 7's smoke test, `KNOWN` for `POST correspondence` (`origin/task/correspondence:localtest/components/correspondence/smoke-test.sh:111-120`). The other four calls share `CheckAccessAsSender` (read, not run) |
| `content`, `attachment/{id}/download`, `downloadall`, `markasread`, `confirm`, recipient `purge`, `forward` | dialog token from Dialogporten | recipient → 501337 | 500 | **Observed** for `content` (`smoke-test.sh:206-215`); the rest are skipped by the smoke test and share `CheckAccessAsRecipient` (read) |
| `GET correspondence` (list), `GET …/{id}`, overview, `confidential-reminders` | an Altinn or ID-porten token of a recipient (Arbeidsflate BFF, API users) | the recipient or `onBehalfOf` → party id | 500 | read, not run |
| Attachment download through the OIDC cascade | browser, Arbeidsflate | recipient | 500 after the login | read; deferred to task 11 anyway |
| `CreateNotificationOrder` → `CustomRecipientDeduplicationHelper.cs:180` → `AuthorizeUserIdsForResource(unit.PartyId, …)` | Hangfire job; organisation recipient with official addresses **and** custom recipients whose address equals a user-registered contact point of that organisation | `unit.PartyId` from Profile (500000 for DDG Fitness) | job throws, Hangfire retries | read, not run. Reachable even with the service-owner bypass |
| Everything with the service-owner token | the smoke test's sender | (bypassed) | works | observed: the main flow to `Published`, the dialog, the e-mail |

Register lookups and Dialogporten party URNs are not affected:

- Register accepts `urn:altinn:partyid:` and `urn:altinn:party:id:` with any `uint`.
- `WithUrnPrefix` passes a prefixed URN through untouched.
- Dialogs are created with the recipient's organisation or NIN URN.
- `GetDialogActivityParty` (`DialogportenService.cs:1395-1421`) looks up a prefixed URN.
- Profile, KRR and Notifications are called with organisation numbers and NINs.

### 2.2 Other components and 6-digit party ids

No other component in the stack checks the length:

- **Code search.** In the vendored sources under `localtest/components/*/vendor` and the reference clones (app-lib,
  Storage, Dialogporten, the adapter, Notifications, Register, Authorization/Access Management, Profile, Events, Resource
  Registry, Authentication, dialogporten-frontend, app-frontend-react), a search for party-id length comparisons,
  `\d{8}`/`\d{10}` patterns and `IsPartyId` definitions finds only:
  - typed URN parsers with `int`/`uint` (`PartyUrn.IsPartyId(out uint)` in Register, `ConsentPartyUrn` in
    Authorization/Access Management, `PartyUrn` in the Resource Registry);
  - integer parsing of `urn:altinn:partyid` claims (Dialogporten `ClaimsPrincipalExtensions.cs:211`);
  - `\d+` in the adapter's storage URN regex (`InstanceReceipt.cs:240`).
- **Runs on the 6-digit seed.** The same seed passes the Register smoke test (27/27), Notifications (64/64), the root
  smoke test (23/23) and Authorization (30/30, PDP decisions on `urn:altinn:partyid` 500000, 500005, 501337). It also
  passes `run-platform-regression.sh` (44/44 Cypress tests): sign-in, party selection, Storage, Events and the workflow
  engine (`localtest/testdata/README.md`, "Verified", 2026-10-21). The Dialogporten, adapter, Storage, Receipt and Events
  smoke tests use the same parties.
- **The old localtest.** It passed 6-digit ids everywhere for years.

## 3. Options

### (a) The seed moves to 8-digit party ids

| What | Where |
| --- | --- |
| This repository | `components/register/testdata/generate-testdata.py` (a mapping table; party UUIDs derive from the id, so they change too), `testData.json`, the identifier tables in `components/register/testdata/README.md`, `localtest/testdata/README.md` and the schema's example (`500000`). About 175 literal references in 23 files outside `vendor/`: the authorization (33), register (11), receipt (9), events, storage, dialogporten-adapter, authentication and root smoke tests, READMEs and reviews, plus the task 7 branch |
| Monorepo Cypress | `src/App/frontend/test/e2e/config/localtest.json`: 8 `userId.partyId` pairs. `integration/frontend-test/party-selection.ts:75` sets the `AltinnPartyId` cookie to 510001. `pageobjects/party-mocks.ts` and the instance ids in `on-entry.ts`, `instantiation.ts` and `title-tag-updates.ts` are intercepted mocks and could stay. `support/auth.ts` already has a display-name login (`localLogin`, for Tenor users), so the pairs could be dropped instead |
| The old localtest | `src/Runtime/localtest/testdata` keeps 6-digit ids unless it is changed too. Otherwise `localtest.json` must differ by runtime. Changing it orphans every developer's stored local instances, which are keyed by party id |
| Docs | `altinn-studio-docs` v8 and v9 `testing/local/testusers` pages and their `testData.json` examples (6-digit ids, roles keyed by `500000` and `501337`) |
| Apps in the wild | App files copied from the docs key `partyRoles` by seed ids (for example `FinnurO/forer-legeerklaering`, `"500000": [...]`). After a renumbering those keys name parties that no longer exist (skipped with a warning) or redefine them (conflicts). Apps also define their own parties with any id: `signering-brukerstyrt` uses 600000-700002 for its signees, so the recipient's dialog-token path stays broken for the one app that sends correspondence |

Effort 2 to 4 days across three repositories and the docs, plus a migration note for app developers. Risk: medium (wide
churn, Cypress coupling, two runtimes diverging). It affects every app developer whose `testData.json` names seed
parties.

A narrower variant, **(a2) renumber only Digdir (500006)**, for example to upstream's dev-stub id 50952483, is cheap:
the generator table, the seed, two READMEs and the notification-address key. No Cypress spec uses 500006. It fixes
app-lib's sender path only. The recipient path (501337 and app-supplied ids) stays broken.

### (b) One `#if !LOCALTEST` site in Correspondence (recommended now)

At `XacmlRequestFactory.cs:31`, the party-id branch condition:

```csharp
#if !LOCALTEST
        else if (party.StartsWith(AltinnXacmlUrns.PartyId) || partyWithoutPrefix.IsPartyId())
#else
        // localtest: the test data's party ids have 6 digits; production accepts only 8 or 10 (IsPartyId).
        // Reached only after the organisation-number and national-identity-number branches.
        else if (party.StartsWith(AltinnXacmlUrns.PartyId) || uint.TryParse(partyWithoutPrefix, NumberStyles.None, CultureInfo.InvariantCulture, out _))
#endif
```

- **What it covers.** One file and one site covers all four mappers, including the notification job's
  `AuthorizeUserIdsForResource`. A 9-digit value takes the organisation branch before this one, an 11-digit value cannot
  parse as `uint`, and an 8- or 10-digit id passes exactly as upstream. So the behaviour differs from production only
  for ids production never produces.
- **What it does not touch.** `IsPartyId` itself stays as upstream, so `WithUrnPrefix` and `CreateDecisionRequest`'s
  guard are unchanged. Relaxing `IsPartyId` instead would make `CreateDecisionRequest` treat a bare 9-digit
  organisation number, which `InitializeAttachmentHandler.cs:45` passes, as a party id. That changes the production
  request shape, so the site belongs in the factory, not in `IsPartyId`.
- **Effort and verification.** Half a day: the site, `vendor/UPSTREAM.md`, the README's directive-site list, and the
  smoke test's two `KNOWN` turning into `ok` with the rest of the recipient path run. Then `signering-brukerstyrt` under
  `--with correspondence`.
- **Risk.** Low. It is the component's first `#if !LOCALTEST`; the plan promised zero, and `TODO.md` asks for a report
  before a third site, which this is. It affects only the `correspondence` group.

### (c) Upstream change (recommended in parallel)

The smallest upstream fix completes #2043: Correspondence keeps the type it already knows.

- `AltinnAuthorizationService.cs:220`: `resolvedParty = $"{UrnConstants.Party}:{registerParty.GetPartyId()}";`
- `AltinnTokenXacmlMapper..cs:46`: `CreateResourceCategory(resourceId, $"{UrnConstants.Party}:{partyId}", …)`.

The factory's prefix branch then takes it whatever its length, and the attribute value it emits stays the bare number,
so the PDP sees the same request. Upstream's own test
`CheckAccessAsAny_WhenPartyIsIdportenEmailUrn_ResolvesToPartyIdInResourceCategory`
(`Test/.../AltinnAuthorizationServiceTests.cs:206-249`) asserts that value and keeps passing (not run). Optional: make
`IsPartyId`'s bare-number branch "digits that parse as a positive `int`" and add the missing tests. A larger
alternative, the party UUID as the internal pivot (#432), is out of proportion for this.

Draft for an upstream issue (in Norwegian, as the team writes its issues):

> **PDP-forespørsler feiler for parter med partyId som ikke har 8 eller 10 sifre**
>
> `AltinnAuthorizationService.CreateDecisionRequest` slår opp parten i Register og sender videre
> `registerParty.GetPartyId().ToString()` uten prefiks. `XacmlRequestFactory.CreateResourceCategory` må da gjette typen
> på nytt med `IsPartyId`, som bare godtar 8 eller 10 sifre (#1595, #2042). Typen er allerede kjent på dette punktet, og
> Register har ingen lengdegrense for partyId (`uint`). Lengderegelen har måttet utvides én gang da A3 begynte å lage
> partyId fra 1 000 000 001 (#2041), og den har ingen tester.
>
> Forslag: send `urn:altinn:partyid:{id}` fra `CreateDecisionRequest` (linje 220) og fra
> `AltinnTokenXacmlMapper.CreateUserContactPointDecisionRequest` (linje 46), slik #2043 la til rette for. Forespørselen til
> PDP blir uendret (attributtverdien er fortsatt tallet uten prefiks).
>
> Bakgrunn: lokale testmiljøer (localtest) har testdata med 6-sifrede partyId som er i bruk i apper og
> Cypress-tester. Der feiler alle kall som går til PDP med 500 "RecipientId is not a valid organization number, person
> number or party id".

Effort upstream: about an hour of code and a test. Ours: filing it. Risk: low, because the request to the PDP is
unchanged. Owner: the Correspondence team. Timing is theirs; once a release with it is vendored, site (b) is deleted.

### (d) Callers pass UUIDs instead

Not possible:

- The API accepts no party id or party UUID as recipient (`RecipientListAttribute.cs`).
- The sender is not caller input.
- Whatever form a party arrives in, `CreateDecisionRequest` resolves it through Register to the party id before the
  factory runs.

App-lib could change nothing that reaches this code.

### Recommendation

1. **(b)** in task 7 before it merges, so that `signering-brukerstyrt` and the recipient path work, with the smoke
   test's `KNOWN` checks turned into real checks.
2. **(c)**: file the upstream issue above; delete the site when a fixed pin is vendored.
3. **Keep the seed as it is.** Renumbering (a) is not worth its cost, and it would not cover app-supplied test data. If
   production-shaped ids are wanted for coverage, add a few extra seed parties with 8- and 10-digit ids rather than
   renumbering, so nothing downstream moves. That is a separate, unapproved idea.

## 4. Open questions for the product owner

1. Approve (b), the `correspondence` component's first `#if !LOCALTEST` site, at `XacmlRequestFactory.cs:31`?
2. Who files the upstream issue (c), and should it link this repository? The agent's token can read but not open pull
   requests in `Altinn/*`.
3. Keep 6-digit party ids in the seed as a documented localtest property (a sentence in `localtest/testdata/README.md`:
   "party ids are any positive integer; production has 8-digit Altinn 2 and 10-digit Altinn 3 ids")?
4. Add 8- and 10-digit parties to the seed for coverage, without renumbering (would have caught this in the smoke test)?
5. Should the monorepo's Cypress config drop the `userId.partyId` pairs in favour of the existing display-name login, so
   the seed's ids stop being a cross-repository contract? Not needed for this fix.

## 5. Not verified

- **(b) and (c) were not built or run.** In particular, it was never observed that the PDP permits `ttd` once the
  request is built: no request has reached that point. The policies were seeded for it in task 7.
- **Paths read but not run:** the four sender calls after `POST correspondence` with app-lib's credential, the
  recipient calls after `content`, Altinn and ID-porten recipient tokens, and the notification deduplication path.
- **Party ids in tt02 and production** come from documentation examples (all 8 digits, `50…`/`51…`), upstream's tests
  and dev stub (Digdir `50952483`, environment not stated) and the Register migration (Altinn 3 ids from 1 000 000 001).
  There was no live lookup in tt02 (it needs a login). No documented minimum length was found in
  `altinn-studio-docs` or the Register contracts, and none could be confirmed elsewhere. It is not known whether
  Altinn 2 ever issued ids shorter than 8 digits.
- **Apps-only mode.** When every running app posts its own test data, the seed (and Digdir) is left out. Then the sender
  lookup in Register finds nothing, `resolvedParty` stays the organisation number, and the factory takes the
  organisation branch: read, not run.
- **Apps in the wild.** Only five public `testData.json` files with `partyRoles` were found on GitHub
  (`gh search code`). Most app repositories live in Gitea and were not searched.
