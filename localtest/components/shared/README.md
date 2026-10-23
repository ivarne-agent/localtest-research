# Shared fakes

Source for fakes that more than one .NET consumer component needs. Nothing here is built into an image.

Consumers of the Maskinporten fake (copies in their own `fakes/`, identical to the files here; checked with `diff -r` on
2026-10-21). None of them sets `UseAltinnTestOrg`, so each acts as Digdir:

| Copy | Used by | Client id | Acts as |
| --- | --- | --- | --- |
| `components/dialogporten/fakes/Infrastructure/Maskinporten/` | Dialogporten's Infrastructure project, so all three hosts (events, party names, authorization clients) | `localtest-dialogporten` | `digdir` (exchanged) |
| `components/notifications/fakes/Maskinporten/` | Notifications' condition-check client (`SendConditionClient`) | `localtest-notifications` | Digdir's raw Maskinporten token (991825827, no exchange) |
| `components/dialogporten-adapter/fakes/Maskinporten/` | every client of the adapter (Storage, Register, Dialogporten); its `.editorconfig` silences two `Recommended` analyzer rules for the copy | `localtest-dialogporten-adapter` | `digdir` (exchanged) |
| `components/correspondence/fakes/Maskinporten/` | every platform client of Correspondence, and KRR (raw token) | `991825827` | `digdir` (exchanged) |

Profile has no copy: its two Maskinporten clients (KRR and brreg) are replaced whole by its own fakes. The Resource
Registry and Receipt have none either. Re-copy after changing the canonical files here.

Consumers of the cookie policy (`fakes/Cookies/`, copies identical to the files here; checked with `diff -r` on
2026-10-21), task 21b, see [`../../reviews/cookies.md`](../../reviews/cookies.md):

| Copy | Used by | Registered from |
| --- | --- | --- |
| `components/authentication/fakes/Cookies/` | the runtime, party and session cookies and their expiries (hard-coded `Secure`) | `AddLocaltestAuthentication` |
| `components/correspondence/fakes/Cookies/` | the retry path's expiry of the OIDC correlation and nonce cookies (hard-coded `Secure`, `SameSite=None`) | `AddLocaltestServices` |

| Path | What |
| --- | --- |
| `fakes/Maskinporten/` | `LocaltestMaskinportenService` (an `IMaskinportenService` for `Altinn.ApiClients.Maskinporten` 10.1.0), its options (`Localtest:Maskinporten`: `Issuer`, `ExchangeEndpoint`) and `AddLocaltestMaskinporten` |
| `Maskinporten.Smoke/` | A net10.0 console host that compiles those three files against the package and asks the fake for a token |
| `fakes/Cookies/` | `LocaltestCookiePolicy`: `AddLocaltestCookiePolicy()` registers an `IStartupFilter` that puts ASP.NET Core's cookie policy middleware in front of the host's pipeline; on plain http it removes `Secure` and turns `SameSite=None` into `Lax` |
| `Cookies.Smoke/` | A net10.0 host on ASP.NET Core's in-memory `TestServer` that compiles that file and checks the cookies it writes over http and https |
| `smoke-test.sh` | Runs `Cookies.Smoke` (no stack needed), then the Maskinporten host twice against the running stack |

## The rule

**Copy these files into a consumer's own `fakes/` folder when that consumer is vendored; do not reference this folder
from a build.** A component's Docker build context is its own folder, so there is no shared build context for now
(TODO.md task 15 is the long-term replacement). Change the canonical copy here, then re-copy it into each consumer (the table above).

## `IMaskinportenService` fake

The package hard-codes `https://*.maskinporten.no` and the exchange hosts per environment, and
`MaskinportenSettings` cannot override them, so the consumer's DI gets this service instead. Call
`services.AddLocaltestMaskinporten(configuration)` after the consumer's own Maskinporten registrations (the package adds
its service with `TryAdd`).

- `GetToken(IClientDefinition ...)` (both overloads, the ones `MaskinportenTokenHandler` calls) posts an unsigned
  `jwt-bearer` assertion (`iss` = client id, `scope`, `consumer_org` when set) to `{Issuer}token`, the localtest app's token
  endpoint. When `ExhangeToAltinnToken` is true it exchanges the result through `ExchangeEndpoint`
  (production Authentication's `exchange/maskinporten`), with `?test=true` only when `UseAltinnTestOrg` is true, as the
  package does. Digdir's organisation number (the default) therefore becomes `ttd` with it and `digdir` without.
- The other three `GetToken` overloads throw `NotSupportedException`.
- Tokens are cached in the package's `ITokenCacheProvider` for `expires_in` minus 5 seconds.
- Client id to organisation number is the token endpoint's rule (`localtest/localtest/README.md`): a nine-digit client id
  is the organisation number, `ConsumerOrgNo` overrides it, anything else is Digdir.

## Cookie policy (`fakes/Cookies/`)

Production code hard-codes `Secure = true` on cookies (Authentication's runtime, party and session cookies, Correspondence's
retry-path expiry) or `SameSite=None` with it. The stack is plain http, where a browser refuses both, and since task 21b the
front doors no longer remove `Secure` from a platform component's `Set-Cookie`. Call `services.AddLocaltestCookiePolicy()` from
the consumer's additive hook; no vendored file changes.

- The middleware runs first in the pipeline (an `IStartupFilter`), so it sees every cookie the host appends or deletes through
  `HttpResponse.Cookies`, framework handlers (OpenID Connect's correlation and nonce cookies) included. A cookie written as a
  raw `Set-Cookie` header is not seen; grep for one when re-pinning a consumer.
- On a request that is not https (`HttpRequest.IsHttps`): `Secure` off, and `SameSite=None` becomes `Lax` (a browser refuses
  `None` without `Secure`). Everything else is the host's.
- On https nothing changes, so production's attributes hold wherever TLS is in front. The consumers do not honour
  `X-Forwarded-Proto`, so behind the stack's front door every request is http.
- The options go to `UseCookiePolicy` directly; a host's own `CookiePolicyOptions` (and its own `UseCookiePolicy`, if any) are
  untouched.

## Smoke test

```
./smoke-test.sh [BASE]        # default http://local.altinn.cloud:8000
```

`Cookies.Smoke` runs first and needs no stack (it restores `Microsoft.AspNetCore.TestHost` 10.0.8): over http `Secure` is
gone from an appended and a deleted cookie and `SameSite=None` is `Lax`; over https all three are unchanged. Then the
Maskinporten host; it needs the stack running and restores `Altinn.ApiClients.Maskinporten` 10.1.0 from the NuGet cache or nuget.org. The host
registers a `SettingsJwkClientDefinition` (client id 991825827, scope `altinn:serviceowner/instances.read`,
`ExhangeToAltinnToken`), prints the claims of the Maskinporten and the exchanged Altinn token, and exits non-zero unless the
Maskinporten token has `consumer.ID` `0192:991825827` and the Altinn token has `urn:altinn:org` `ttd` (`UseAltinnTestOrg`
true) or `digdir` (false). Authentication looks the organisation code up at altinncdn.no, so it needs internet access.
