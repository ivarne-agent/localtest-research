# Maskinporten package: altinn-apiclient-maskinporten#92 against our change (task 47, 2026-10-24)

The question: does [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92)
("feat: make Maskinporten and token exchange endpoints configurable", `olebhansen`, draft, opened 2026-10-23 19:49
UTC, head `4e0946e`, closes [#91](https://github.com/Altinn/altinn-apiclient-maskinporten/issues/91)) serve task 15 of
[`../../TODO.md`](../../TODO.md), and how does it compare with our own change for the same purpose: branch
`feat/configurable-endpoints` at `bca2659` on `ivarne-agent/altinn-apiclient-maskinporten`, never opened as a PR
(hand-over `/home/agent/code/pr-maskinporten-configurable-endpoints.md`). Both are one commit on the same base, `main`
at `15d3c63`. #92 has no comments or reviews yet. Nothing was posted on GitHub and neither branch was changed; #92 was
fetched into the reference clone as the local branch `pr-92`.

## Recommendation

**Support #92 and withdraw ours** (it was never opened, so withdrawing means only not opening it). #92 does what task
15 needs with the same two setting names we chose (`TokenEndpoint`, `TokenExchangeEndpoint`), so the consumer
follow-up in the hand-over applies unchanged. It is non-breaking where ours is not (default interface members), it has
a public issue with a second concrete consumer (Notifications' Aspire stack), and it comes from a Digdir developer, so
it is the one the maintainers are likely to take. Ours is better on four small points (empty values, validation,
`test=true` after a query, the audience without `Environment`); none of them blocks us. They are offered as review
suggestions below, for the owner to post if wanted; the PR is fine to merge without them.

## Evidence

- Diffs read: `git diff origin/main...pr-92` and `git diff origin/main...feat/configurable-endpoints` in
  `/home/agent/code/.reference/altinn-apiclient-maskinporten`; #91 and #92 through `gh`.
- Both test suites run from exported trees (`git archive`, no checkout of either branch): #92 **12 passed**, ours
  **25 passed** (`dotnet test`, net10.0).
- A probe (scratch console app, the package built from each tree by project reference) against a local server shaped
  like the stack: a token endpoint answering JSON and an `[ApiController]` exchange action returning `Ok(string)`, as
  Authentication's `exchange/{tokenProvider}` does. Throwaway RSA `EncodedJwk`, `disableCaching`.

| Probe | #92 | Ours |
| --- | --- | --- |
| A. `Environment=test`, `TokenEndpoint` on plain `http` | token | token |
| B. `TokenEndpoint` only, no `Environment`, no audience | `ArgumentException: Invalid environment setting. Valid values: prod, ver1, ver2, test` | token (`aud` = token URL up to its last `/`) |
| C. `TokenEndpoint=""`, `Environment=test` (an empty configuration value) | `UriFormatException: Invalid URI: The URI is empty.` | treated as unset: goes to `https://test.maskinporten.no/token` |
| D. Exchange against `Ok(string)`, `UseAltinnTestOrg=true` | `altinn` token, `test=True` reached the action | the same |
| E. Exchange endpoint answering a JSON string | the same | the same |
| F. `TokenExchangeEndpoint` with a query (`...?x=1`) and `UseAltinnTestOrg` | request to `?x=1?test=true` (test flag lost) | request to `?x=1&test=true` |

D needs no special handling: the package's `HttpClient` sends `Accept: application/json` (constructor of
`MaskinportenService`), so `Ok(string)` answers a JSON string, which `PerformRequest<string>` reads. (Our shared fake's
comment "Authentication answers the token as plain text" holds only for its own request, which sends no `Accept`; it
goes with the fake.)

## 1. What each does

| | #92 | Ours (`bca2659`) |
| --- | --- | --- |
| Settings added | `TokenEndpoint`, `AssertionAudience`, `TokenExchangeEndpoint` (all `string`, default `null`) | `TokenEndpoint`, `TokenExchangeEndpoint` (`string`, default `null`) |
| Token endpoint | `TokenEndpoint ?? GetTokenEndpoint(Environment)` | `TokenEndpoint` if not null or whitespace, else from `Environment` |
| Exchange endpoint | `TokenExchangeEndpoint ?? GetTokenExchangeEndpoint(TokenExchangeEnvironment ?? Environment)` | the same with "not null or whitespace" |
| Assertion `aud` | `AssertionAudience ?? GetAssertionAud(Environment)` | from `Environment` when set; else `TokenEndpoint` up to and including its last `/` |
| Precedence | override wins over the environment, each independently | the same |
| Empty values | used as is: `""` fails in `new Uri` (probe C) | count as unset |
| Validation | none beyond `new Uri` (relative URL: `UriFormatException`) | absolute `http`/`https` required, else `ArgumentException` naming the setting |
| `http` | accepted | accepted |
| `Environment` needed | unless both `TokenEndpoint` and `AssertionAudience` are set | not when `TokenEndpoint` is set |
| When resolved | after the token cache lookup, as before | token endpoint and `aud` before the cache lookup in `GetToken(IClientDefinition)`; exchange endpoint only when exchanging, as before |
| `UseAltinnTestOrg` | unchanged: appends `?test=true` (wrong after a query, probe F) | appends `?` or `&` |
| Enterprise user exchange | uses the override | uses the override |
| Public overloads with `environment` | unchanged | unchanged |
| `IMaskinportenSettings` | default members `{ get => null; set { } }`: custom implementations keep compiling; `MaskinportenSettings` has real properties | plain members: **breaking** for direct implementers (precedent: `Kid` in #83, released as minor `v10.1.0`) |
| Refactoring | small: private `PerformTokenExchange` with an endpoint parameter behind `ExchangeToAltinnToken`; two optional parameters on the private `GetToken` | larger: private `GetToken` takes endpoint and audience instead of the environment; `ExchangeToAltinnTokenAt`; three `internal static` resolvers; `InternalsVisibleTo` for tests |
| Tests | new `test/Altinn.ApiClients.Maskinporten.Tests` (in the solution, `IsPackable=false`, `IsTestProject`), 12 tests through `GetToken(IClientDefinition)` with a recording handler: defaults for all four environments, each override alone and together, the exchange override with `?test=true`, configuration binding, a settings class written against the old interface | the same project path, 25 tests: resolver unit tests (defaults, overrides, empty values, invalid URLs, the audience rule) and three through `GetToken(IClientDefinition)` |
| README | new section with an example and **the contract a mock must meet** (grant form, RS256 assertion valid 10 s, token JSON, exchange answer as a JSON string) | new "Custom endpoints" section with localtest URLs and the rules |
| Versioning, changelog | none (the repository has neither; releases are tags) | none |
| Unrelated changes | none | none |
| Linked issue | #91, with a second consumer (Notifications' local Aspire stack) | none |

Both leave the same gap: no workflow runs the tests on pull requests (both PR texts say so).

## 2. Does #92 serve our need?

Yes, as it is. Each localtest consumer (Dialogporten, Notifications, the Dialogporten adapter, Correspondence; see the
hand-over's table) would set, in the section its Maskinporten clients bind (`{platform}` is the consumer's platform
origin with the trailing `/`):

- `TokenEndpoint` = `{platform}localtest/api/maskinporten/token`
- `TokenExchangeEndpoint` = `{platform}authentication/api/v1/exchange/maskinporten`
- `Environment` = `test`, kept (some consumers validate it as non-empty). The `aud` is then
  `https://test.maskinporten.no/`, which the localtest token endpoint does not check (`TryReadAssertion` reads the
  payload only). Alternatively `AssertionAudience` = `{platform}localtest/api/maskinporten/` (the fake's issuer), which
  also allows dropping `Environment`. With #92, dropping `Environment` without setting `AssertionAudience` fails
  (probe B); our change would not.
- `EncodedJwk` = a real throwaway RSA private JWK, base64 (the package always signs; the fakes' placeholders fail).
- The exchange flag (the package spells it `Exh…ToAltinnToken`) and `UseAltinnTestOrg`: as today; `?test=true` reaches Authentication (probe D).

Nothing is missing for us: the exchange URL is configurable, `http` is accepted, the test-org flag works on our URL (it
has no query), and the exchange answer is read. The follow-up then is the hand-over's: bump the package once a release
has #92, set the values above, delete the `IMaskinportenService` fakes and `shared/fakes/Maskinporten/`. Task 15's
status line in `TODO.md` should change from "owner opens the PR" to "supporting #92" (for the coordinator; this task
does not edit `TODO.md`).

## 3. Differences that matter

**For existing users.** #92 changes nothing unless a new setting is set, and keeps custom `IMaskinportenSettings`
implementations compiling (it has a test for that). Ours breaks direct implementers at compile time, and resolves the
token endpoint before the cache lookup (the same exception, earlier; no practical change).

**Quality.** #92: explicit `AssertionAudience` is more general than our derivation (a mock whose issuer is not the token
URL's directory) at the cost of one more setting; tests go through the public path only, plus binding and
compatibility; the README's mock contract is useful to anyone writing a fake (it would have saved us the unsigned
assertion and the plain-text guess in our fakes). Ours: empty values as unset (relevant for environment variables and
container configuration, where an empty value is easy to produce), validation with an error naming the setting instead
of `UriFormatException`, the correct query join for `test=true`, and an `aud` default that lets `Environment` go.
Naming: both follow the package's style; the two shared names are identical.

**Worth adopting from ours into #92** (all optional, none needed by us): empty or whitespace as unset; the absolute
`http`/`https` check with `ArgumentException` naming the setting; `&` when the exchange endpoint has a query. Deriving
the audience from `TokenEndpoint` is a design choice the author made differently on purpose (#91 names
`AssertionAudience` explicitly); at most, a clearer error when `TokenEndpoint` is set and neither `Environment` nor
`AssertionAudience` is (today: "Invalid environment setting").

**Nothing in #92 to adopt into ours**, since ours is withdrawn.

## 4. Draft review comment (for the owner to post if wanted)

```text
Thanks, this covers our case too: localtest-v2 points the package at its Maskinporten fake and Authentication's
exchange on plain http, and with TokenEndpoint/TokenExchangeEndpoint (Environment kept) it works without our fakes.
Three small suggestions, none blocking:
- Treat empty or whitespace values as unset: an empty env var binds as "" and currently gives UriFormatException.
- Validate the overrides as absolute http/https URLs and throw an ArgumentException naming the setting.
- With UseAltinnTestOrg, append "&test=true" when TokenExchangeEndpoint already has a query (now "?x=1?test=true").
Possibly also a clearer error when TokenEndpoint is set without Environment or AssertionAudience; today it says
"Invalid environment setting".
```
