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

Correspondence had a copy for one cookie expiry until task 63, which made it a directive in its `ExpireCookie`
(`components/correspondence/README.md`, "Directive sites").

Consumers of the service-discovery registration (`fakes/ServiceDiscovery/`, task 40; copies identical to the files here):

| Copy | Used by | Registered from |
| --- | --- | --- |
| `LocaltestServiceDiscovery.cs` in `components/{storage,events,profile,notifications,receipt,authorization,dialogporten-adapter,correspondence,dialogporten}/fakes/ServiceDiscovery/` | the hosts without service discovery of their own (Dialogporten: compiled into each of its three hosts) | `AddLocaltestServiceDiscovery(builder.Services)` in the `#if LOCALTEST` block after `WebApplication.CreateBuilder` |
| the same in `components/{authentication,access-management,resource-registry}/fakes/ServiceDiscovery/` and `components/register/fakes/Altinn.Register.Localtest/ServiceDiscovery/` | the four `AltinnServiceDefaults` hosts, which register `AddServiceDiscovery()` themselves | `AddLocaltestServiceEndpointProviders(services)` in the fakes' hook: removes the pass-through provider |
| `LocaltestPrefixRouting.cs` in `components/dialogporten/fakes/Infrastructure/ServiceDiscovery/` and `components/correspondence/fakes/ServiceDiscovery/` | the clients on one shared public base (Dialogporten's four on `Infrastructure:Altinn:BaseUri`, Correspondence's nine on `PlatformGatewayUrl`) | `LocaltestPrefixRouting.AddTo(...)` in `LocaltestInfrastructure` / `AddLocaltestServices` |

The localtest app's tests compile both files (`localtest/localtest/tests/ComponentServiceDiscoveryTests.cs`).

Consumers of the OTLP helper (`fakes/Otlp/`, TODO.md task 53): the front door, `localtest/localtest/Telemetry/LocaltestOtlp.cs`
(a copy, since its Docker build context is its own folder; called from `FrontDoorTelemetry.Add`); Authorization
(`AddLocaltestOtlp`, from `fakes/LocaltestAuthorizationTelemetry.cs`); and the other components the audit in task 53
lists (per component, where the call goes and in which batch). No consumer calls `AddLocaltestOtlpResource` on its own
since task 61 (2026-10-28): Authentication, Access Management, Register and the Resource Registry had it, and dropped it
with their copies, because their own `service.name` is already the manifest name (TODO.md section 61).

| Path | What |
| --- | --- |
| `fakes/Maskinporten/` | `LocaltestMaskinportenService` (an `IMaskinportenService` for `Altinn.ApiClients.Maskinporten` 10.1.0), its options (`Localtest:Maskinporten`: `TokenEndpoint`, `ExchangeEndpoint`, by service name since task 40) and `AddLocaltestMaskinporten` |
| `Maskinporten.Smoke/` | A net10.0 console host that compiles those three files against the package and asks the fake for a token |
| `fakes/Cookies/` | `LocaltestCookiePolicy`: `AddLocaltestCookiePolicy()` registers an `IStartupFilter` that puts ASP.NET Core's cookie policy middleware in front of the host's pipeline; on plain http it removes `Secure` and turns `SameSite=None` into `Lax` |
| `fakes/ServiceDiscovery/` | `LocaltestServiceDiscovery`: core, the configuration provider and a provider for real hosts (a dot, an IP, `localhost`; the configuration provider alone refuses any host it has no entry for), no pass-through, so a service name without `services__<name>__http__0` fails at once; `NoAddress` recognises that failure. `LocaltestPrefixRouting`: a handler that moves a request on the public origin to the service owning its path prefix, by the front door's table |
| `fakes/Otlp/` | `LocaltestOtlp`: `AddLocaltestOtlp(configuration)` on a component's `IOpenTelemetryBuilder` turns on OTLP for traces, metrics and logs (`UseOtlpExporter()`) exactly when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and lets `OTEL_SERVICE_NAME` win over a hard-coded `service.name`; `AddLocaltestOtlpResource(configuration)` is only the naming half (the helper calls it; no host calls it alone since task 61) |
| `Otlp.Tests/` | xunit tests of that file against a fake collector on a loopback port (http/protobuf and grpc, with and without the variable); the OpenTelemetry version is a property (`-p:OtelVersion=1.15.3`) |
| `Cookies.Smoke/` | A net10.0 host on ASP.NET Core's in-memory `TestServer` that compiles that file and checks the cookies it writes over http and https |
| `smoke-test.sh` | Runs `Cookies.Smoke` and `Otlp.Tests` (no stack needed), then the Maskinporten host twice against the running stack |

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
  `jwt-bearer` assertion (`iss` = client id, `scope`, `consumer_org` when set) to `TokenEndpoint` (`http://localtest/localtest/api/maskinporten/token`), the localtest app's token
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

## OTLP helper (`fakes/Otlp/`)

The stack's rule (TODO.md task 53): a component sends traces, metrics and logs over OTLP exactly when
`OTEL_EXPORTER_OTLP_ENDPOINT` is set, with `OTEL_EXPORTER_OTLP_PROTOCOL` and `OTEL_SERVICE_NAME`, and sends nothing
otherwise. Copy `fakes/Otlp/LocaltestOtlp.cs` into the consumer's `fakes/`, add `OpenTelemetry.Exporter.OpenTelemetryProtocol`
in its `localtest.build.targets` where the host does not have it yet (at the host's OpenTelemetry version; with central
package management the version goes in `VersionOverride`), and call it from the `#else` branch where production adds Azure
Monitor or Application Insights:

```csharp
#if !LOCALTEST
    AddAzureMonitorTelemetryExporters(services, config);
#else
    // localtest: OTLP from the standard variables when OTEL_EXPORTER_OTLP_ENDPOINT is set, nothing otherwise.
    services.AddOpenTelemetry().AddLocaltestOtlp(config);
#endif
```

- `services.AddOpenTelemetry()` may be called again: it returns a builder on the same providers, so the call adds to the
  component's own instrumentation, sources, meters and processors.
- The variable is read from the host's `IConfiguration` (the environment in every component); an empty or blank value counts
  as unset. Without it the call registers nothing. A per-signal endpoint (`OTEL_EXPORTER_OTLP_TRACES_ENDPOINT` and the
  others) alone does not count: `UseOtlpExporter()` always turns on all three signals, and a signal without an endpoint
  goes to the SDK's default `localhost:4317`. With the general endpoint set, a per-signal one overrides it for its signal
  (used as it is, no `/v1/<signal>` appended).
- With it, `UseOtlpExporter()` turns on all three signals (it adds the OpenTelemetry logger provider too), reads endpoint,
  protocol and headers from the standard variables and appends `/v1/traces`, `/v1/metrics` and `/v1/logs` for
  `http/protobuf`. Without `OTEL_EXPORTER_OTLP_PROTOCOL` the .NET SDK uses `grpc`.
- `UseOtlpExporter()` can be called once per service collection and never next to a signal-specific `AddOtlpExporter`
  (`NotSupportedException` when the providers are built). So the helper is not for a host that already exports OTLP:
  the `Altinn.Authorization.ServiceDefaults` hosts, Dialogporten and the workflow engine take neither (the
  ServiceDefaults hosts took `AddLocaltestOtlpResource` until task 61; their own name is the manifest's).
- Nearly every component hard-codes `service.name` (`AddService` or `AddAttributes`), and a later resource wins over the
  SDK's environment detector, so `OTEL_SERVICE_NAME` alone is ignored (a test without the detector fails). The helper adds
  the environment detector after the component's resource, so call it after the component's `ConfigureResource`.
- With it, the helper also filters `System.Net.Http.HttpClient.Otlp*` below Warning. A host with `IHttpClientFactory` (nearly
  every component) hands the exporters factory clients, whose handlers log each export at Information: over
  `http/protobuf` 4 events (8 console lines) per export, about 8 lines a minute from the metrics alone on an idle
  Receipt (measured 2026-10-26: 40 lines in 5 minutes, none in the exported logs). Over `grpc`, what studioctl gives the
  containers, the exporters log nothing either way. The hosts without the helper keep their own logging.
- The tests flush the providers before stopping the host: when the host is only disposed, the exporter's `HttpClient` can be
  disposed before the log provider's last batch (an `ObjectDisposedException` in the exporter's event source, the batch lost).

## Smoke test

```
./smoke-test.sh [BASE]        # default http://local.altinn.cloud:8000
```

`Cookies.Smoke` and `Otlp.Tests` run first and need no stack. `Cookies.Smoke` (it restores `Microsoft.AspNetCore.TestHost`
10.0.8): over http `Secure` is gone from an appended and a deleted cookie and `SameSite=None` is `Lax`; over https all
three are unchanged. `Otlp.Tests` (OpenTelemetry 1.19.1 by default): with the variable all three signals reach a fake
collector over `http/protobuf` and traces over `grpc`, named by `OTEL_SERVICE_NAME` over a hard-coded name, and a
per-signal endpoint overrides the general one, and the exporters' own HTTP calls are not logged; without it (unset, empty, blank, or only a per-signal endpoint) nothing is
registered. The tests clear inherited `OTEL_*` variables first and only talk to the fake collector on an ephemeral
loopback port. Then the
Maskinporten host; it needs the stack running and restores `Altinn.ApiClients.Maskinporten` 10.1.0 from the NuGet cache or nuget.org. The host
registers a `SettingsJwkClientDefinition` (client id 991825827, scope `altinn:serviceowner/instances.read`,
`ExhangeToAltinnToken`), prints the claims of the Maskinporten and the exchanged Altinn token, and exits non-zero unless the
Maskinporten token has `consumer.ID` `0192:991825827` and the Altinn token has `urn:altinn:org` `ttd` (`UseAltinnTestOrg`
true) or `digdir` (false). Authentication looks the organisation code up at altinncdn.no, so it needs internet access.
