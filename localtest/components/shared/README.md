# Shared fakes

Source for fakes that more than one .NET consumer component needs. Nothing here is built into an image.

Consumers of the Maskinporten fake (copies in their own `fakes/`): `components/dialogporten/fakes/Infrastructure/Maskinporten/` (compiled into Dialogporten's Infrastructure project, so all three hosts use it). Re-copy after changing the canonical files here.

| Path | What |
| --- | --- |
| `fakes/Maskinporten/` | `LocaltestMaskinportenService` (an `IMaskinportenService` for `Altinn.ApiClients.Maskinporten` 10.1.0), its options (`Localtest:Maskinporten`: `Issuer`, `ExchangeEndpoint`) and `AddLocaltestMaskinporten` |
| `Maskinporten.Smoke/` | A net10.0 console host that compiles those three files against the package and asks the fake for a token |
| `smoke-test.sh` | Runs the host twice against the running stack |

## The rule

**Copy these files into a consumer's own `fakes/` folder when that consumer is vendored; do not reference this folder
from a build.** A component's Docker build context is its own folder, so there is no shared build context for now
(TODO.md task 15 is the long-term replacement). Change the canonical copy here, then re-copy it into each consumer. Consumers so far: `components/notifications/fakes/Maskinporten/` (the condition-check client).

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

## Smoke test

```
./smoke-test.sh [BASE]        # default http://local.altinn.cloud:8000
```

Needs the stack running and restores `Altinn.ApiClients.Maskinporten` 10.1.0 from the NuGet cache or nuget.org. The host
registers a `SettingsJwkClientDefinition` (client id 991825827, scope `altinn:serviceowner/instances.read`,
`ExhangeToAltinnToken`), prints the claims of the Maskinporten and the exchanged Altinn token, and exits non-zero unless the
Maskinporten token has `consumer.ID` `0192:991825827` and the Altinn token has `urn:altinn:org` `ttd` (`UseAltinnTestOrg`
true) or `digdir` (false). Authentication looks the organisation code up at altinncdn.no, so it needs internet access.
