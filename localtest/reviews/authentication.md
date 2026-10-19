# Authentication review (2026-10-19, updated 2026-10-20)

Component: [`../components/authentication/`](../components/authentication/). The review led to the move described in
[`../../planned-components/authentication/localtest-app-identity.md`](../../planned-components/authentication/localtest-app-identity.md);
fixes and the move landed in `28d8fe1`, `1485935` and `180fd3c`.

## Summary

The localtest Authentication was production Authentication plus about 1,600 lines of fakes, most of them standing in
for systems outside Authentication: ID-porten (with unsigned tokens and a validator that skipped the signature),
Maskinporten, Profile and the old localtest's `/Home` helpers. Those moved out: Profile to the register component's
`internal/user`, ID-porten, Maskinporten and `/Home` to the localtest app. Authentication's fakes are now 7 files
(configuration, key providers, the level table, three in-memory repositories), and its own OIDC client, token
validation, Profile client and token exchanges run.

## Vendored code

- Pin: `Altinn/altinn-auth` `392f9578`. `main` is 6 commits ahead, none touching the vendored paths (checked
  2026-10-20).
- Fidelity: `git archive 392f9578` of the copied paths (the app, `src/pkgs/JWTCookieAuthentication`, `.editorconfig`,
  `global.json` and the `Directory.*` files at `src/`, `src/apps/`, `src/pkgs/`), `diff -rq`: only `UPSTREAM.md` and
  the two directive files differ. Upstream has a file named `TokenClientAuthType .cs` (with a space), which breaks
  unquoted path lists; `UPSTREAM.md` warns about it.
- Directive sites:
  1. `AuthenticationHost.cs`: `#if LOCALTEST AddLocaltestAuthentication(builder)` before the persistence layer.
  2. `Controllers/AuthenticationController.cs`, `IsSafeSameOrSubdomainHttps`: production first, `#else` also accepts
     plain http for `goTo`.

## Dockerfile

Mirrors upstream's `src/apps/Altinn.Authentication/Dockerfile` since `28d8fe1`: pinned Alpine digests, publish from
the host folder, the non-root `dotnet` user (uid 1000), only `vendor/src` in the build as upstream copies `src/`.
Deviations marked `localtest:`; the service listens on 5101, the port the stack uses (upstream exposes 5040).
altinn-auth's `docker-bake.hcl` builds from shared `containers/` files, but its default target list is empty, so the
classic Dockerfile is the reference. Whether production images come from it is decided in Azure DevOps: not verified.

## What the fakes replace now

| Production | Localtest |
| --- | --- |
| Postgres (sessions, upstream login transactions, unregistered client requests) | `fakes/Persistence/`, in memory |
| Postgres health check | Removed from the health checks |
| Key Vault keys for incoming platform tokens | The shared test certificate for every issuer |
| `SigningKeysRetriever` (https only, 12 h cache) | `LocaltestSigningKeysRetriever`: plain http, no cache |
| ID-porten | Configuration → the localtest app's fake ID-porten (signed tokens, PKCE) |
| Maskinporten metadata | Configuration → the localtest app's fake Maskinporten |
| Profile | Configuration → the register component's `internal/user` |

## Differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Identity provider | ID-porten (or another configured provider) | The fake ID-porten's picker; `amr` is `TestID`; `sub` is a stable pairwise hash |
| Sessions | Postgres | Memory: a restart signs everyone out |
| Org lookup in `exchange/maskinporten` | `altinncdn.no` | The same; needs internet |
| Platform token keys | Per issuer | One certificate for every issuer |
| ServiceDefaults environment | From the deployment | `UNKNOWN`, deliberately |
| `ASPNETCORE_ENVIRONMENT` | Production | `Development`: developer exception page, http metadata allowed |

## Findings

All fixed unless marked open.

1. The `Altinn:LocalDev` default was dead (same as Register). Removed.
2. The Dockerfile did not mirror upstream (unpinned, root, dead HEALTHCHECK). Fixed.
3. The fake ID-porten handed back unsigned tokens and a fake validator read them with a nonce check only, so
   production's token request and validation never ran. Fixed by the move: the fake ID-porten signs RS256 tokens
   and checks PKCE; production `OidcProviderService` and `UpstreamTokenValidator` run.
4. The `IProfile` fake duplicated what the register component already answered. Removed; production `ProfileService`
   runs (checked with the exact lookup bodies it posts).
5. `UPSTREAM.md` had no sync instructions. Added. The README said "Profile replaced by the register seed". Rewritten.
6. Ignored `src/` and `test/` build leftovers from the dropped old-localtest copy. Deleted (local only).
7. The level-to-acr table is duplicated: Authentication's `Login/LocaltestAuthLevels.cs` (configuration) and the
   localtest app's `IdPorten/AuthLevels.cs` (picker). Open, deliberate; each file points at the other.
8. `exchange/maskinporten` needs `altinncdn.no` for the org code. Open; a local copy of the org list served by the
   localtest app would remove the internet dependency for Authentication, Storage and Events.

## Verification

- The localtest app smoke test (54 checks: proxy, identity providers, `/Home` at every level, party cookies, token
  helpers), the root smoke test (23) and the register smoke test (20) pass on the stack after the move.
- The fake's token endpoint refuses a wrong PKCE verifier and a spent code.
- Cypress: four folders (service-task, signering-brukerstyrt, signing-test, stateless-app) passed after step 1; the full
  run after steps 2 and 3 is recorded in the plan.
