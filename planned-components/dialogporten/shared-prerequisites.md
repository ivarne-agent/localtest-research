# Shared prerequisites: Maskinporten token endpoint, `IMaskinportenService` fake, Authentication as OIDC provider, Postgres 18, Redis, altinncdn.no, inbox links

## Status (2026-10-21)

- **Partly implemented.** Section 1, the Maskinporten token endpoint, and section 2, the shared
  `IMaskinportenService` fake: task 4 (2026-10-20, `798f18e`), in `localtest/localtest/` and
  [`../../localtest/components/shared/fakes/Maskinporten/`](../../localtest/components/shared/README.md). Section 3,
  Authentication as an OIDC provider: task 2 (2026-10-20, `4b9b5f7..ddd0ecf`; Authentication smoke 30, 32 with the
  `dialogporten` group, on 2026-10-21). Section 4, Postgres 18: `84514a3` (`localtest/postgres/`). Section 5, Redis:
  task 9, in the `dialogporten` group.
- Section 6, the committed `altinn-orgs.json`: **not done**, task 3 is won't do; the stack reads altinncdn.no.
- Section 7, the app-frontend link to the inbox: **open**, task 11.
- Decided otherwise: client ids map to organisation numbers by `consumer_org`, then a nine-digit id, then 991825827,
  with no client table (task 4); the fake's exchange keeps production semantics, `test=true` only with
  `UseAltinnTestOrg` (task 4); the fake is **copied** into each consumer's `fakes/`, not shared through a build context
  (task 4); two OIDC clients are seeded, `localtest-smoke` and `arbeidsflate-localtest`, and the code-flow helper is
  `localtest/api/authentication/token`, not a `/Home` helper (task 2).

Superseded statements:

- Section 2, "exchanging with `test=true`" and Digdir becoming `ttd`: production semantics (task 4).
- Section 2, the compose `additional_contexts` build wiring: each consumer carries a copy of the fake.
- Section 3, the missing `pid`, the pepper and the logout 500: fixed by task 2.
- Section 6: superseded by task 3's won't do.

Research date: 2026-10-20. Read-only: nothing was built or started; the one scratch action was downloading and
decompiling the `Altinn.ApiClients.Maskinporten` 10.1.0 NuGet package (deleted afterwards). Every behaviour below is
"verified by reading" unless the Verified section says otherwise. This plan covers the cross-cutting pieces the
Dialogporten, adapter and Arbeidsflate strands (and later Correspondence) assume; each item is a concrete change to an
existing part of the stack. Supersedes section 4.1 (P1, P2, P4, P5, P6) of
[`../../localtest-components/dialogporten-arbeidsflate.md`](../../localtest-components/dialogporten-arbeidsflate.md);
what that research got wrong is listed at the end.

## Pins

| Repo | Commit | What was read | Drift since the pin |
| --- | --- | --- | --- |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` | `InfrastructureExtensions.cs`, `InfrastructureSettings.cs`, migrations, host `Program.cs` files, compose files, `LocalDevelopmentSettings.cs`, CDN client | `origin/main` `1a7c584` (5 ahead): package bumps, workflows, lock files; none of the files cited here |
| [`Altinn/dialogporten-frontend`](https://github.com/Altinn/dialogporten-frontend/tree/0011d957ddbd9d77e31fcce842fc41887297942c) | `0011d95` | `packages/bff/src/auth/*`, `config.ts`, `server.ts`, `redisClient.ts`, `graphql/organizations`, `compose.yml`, `packages/frontend/src/pages/routes.ts` | `origin/main` `73feeb9` (5 ahead): alert banner, mock stories, docs; nothing under `bff/src/auth`, `config.ts`, `server.ts` |
| [`Altinn/altinn-dialogporten-adapter`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/0a329856ba79101acdf54c29fd84a5063189a42d) | `0a32985` | `Settings.cs`, `ServiceCollectionExtensions.cs`, `appsettings.json`, `Infrastructure/AltinnCdn/*` | `origin/main` `86364b8` (3 ahead): `Microsoft.AspNetCore.OpenApi` 10.0.11 → 10.0.13 and two telemetry-sampling lines; the Maskinporten package stays 10.1.0 |
| [`Altinn/altinn-apiclient-maskinporten`](https://github.com/Altinn/altinn-apiclient-maskinporten/tree/15d3c63a1f320816acccefd1f221d936ccb286db) | `15d3c63` (shallow clone, 2026-06-25) | `MaskinportenService.cs`, `IMaskinportenService.cs`, `MaskinportenSettings.cs`, `ServiceCollectionExtensions.cs`, handler and factory | The consumers reference NuGet **10.1.0**, built from `d99906b` (nuspec). Decompiled with `ilspycmd`: the interface, the three URL tables, `MaskinportenSettings` and the `TryAddSingleton` registration are identical to the clone |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810` (the vendored Authentication pin in `localtest/components/authentication/vendor/UPSTREAM.md`, = `origin/main`) | Authentication: OIDC controllers and services, repository interfaces, Postgres implementations, migrations `v0.23`; Access Management infra and migrations for the Postgres question | The earlier research cites `392f957`; `src/apps/Altinn.Authentication` is byte-identical between the two |
| [`Altinn/altinn-correspondence`](https://github.com/Altinn/altinn-correspondence/tree/6dcf561945f73e8f28f0a95747ee4fafa012aa89) | `6dcf561` | `Integrations/DependencyInjection.cs`, `API/appsettings.json` | not checked |
| [`Altinn/altinn-storage`](https://github.com/Altinn/altinn-storage/tree/9e72a9f128debd7b9815b615d842a2d1a6d343c5) | `9e72a9f` (vendored pin) | CDN organisation repository, compose, CI | – |
| [`Altinn/altinn-events`](https://github.com/Altinn/altinn-events/tree/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6) | `1a4a80c` (vendored pin) | migrations, test container images | – |
| [`Altinn/altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) | `447e53e` (vendored workflow engine pin; app frontend and app backend) | `urlHelper.ts`, `PlatformFrontendSettings.cs`, `AltinnCdnClient.cs`, workflow-engine compose, `images.go` | – |

Local stack read: `localtest/localtest/` (Maskinporten, IdPorten, Home, `Program.cs`, `LocaltestJsonContext.cs`),
`localtest/components/authentication/` (fakes, README, review), `localtest/docker-compose.yml`, every component's
`postgres-init/` and `localtest.build.targets`, `localtest/run-app.sh`.

## Summary

1. **Maskinporten token endpoint** (section 1): `POST localtest/api/maskinporten/token` in the localtest app, jwt-bearer
   grant, assertion decoded but not verified, `client_id` → organisation number by a small configuration table with
   a convention fallback, response `{access_token, token_type, expires_in, scope}`. About 120 lines plus 4 DTO lines
   and one `token_endpoint` field in the discovery document. Serves the BFF unchanged (its token URL is configurable).
2. **`IMaskinportenService` fake** (section 2): necessary, because the .NET client hard-codes `https://*.maskinporten.no`
   and the exchange URLs per environment, and nothing in `MaskinportenSettings` 10.1.0 overrides them. All three .NET
   consumers reference exactly 10.1.0, the interface has six methods, and `TryAddSingleton` makes a plain DI
   replacement enough. One source file in `localtest/components/shared/fakes/Maskinporten/`, compiled into each host
   by its `localtest.build.targets`, carried into the image build with compose `additional_contexts` (podman-compose
   1.5.0 on this machine supports it). ~150 lines.
3. **Authentication as OIDC provider** (section 3): the vendored Authentication already has the whole provider
   (`authorize`, `upstream/callback`, `token`, `openid/logout`, discovery, JWKS), and a registered client takes the
   same upstream path to the picker as the `goTo` flow. Missing: in-memory `IOidcServerClientRepository` (seeded from
   configuration), `ILoginTransactionRepository`, `IAuthorizationCodeRepository`, `IRefreshTokenRepository`; four files,
   about 350 lines. One bug to fix on the way: the localtest `OidcRefreshTokenPepper` is not base64, and production
   decodes it with `Convert.FromBase64String` for client-secret and refresh-token hashing.
4. **Postgres 18** (section 4): move the shared container to `postgres:18-alpine`. Dialogporten needs it (`uuidv7()`
   default); nothing in Storage, Access Management, Events or the workflow engine uses anything PG 17/18 removed, the
   workflow engine already runs on 18 upstream and in studioctl, and a second Postgres costs a container, memory and a
   second set of init conventions for one consumer. Two mechanical changes: a new volume (a 16 data directory does
   not start on 18) and the mount path (`/var/lib/postgresql`, the 18 image's layout).
5. **Redis** (section 5): one `valkey/valkey:8-alpine` (or `redis:7-alpine`) container, logical db 0 for Dialogporten
   (all three hosts connect unconditionally), db 1 for the BFF (sessions, token cache, CDN and resource caches,
   front-channel logout map; it has a session fallback but imports the Redis client unconditionally).
6. **altinncdn.no** (section 6): five consumers read `orgs/altinn-orgs.json` (Authentication's exchange, Storage,
   the adapter, Dialogporten, the BFF). Serve a committed copy from the localtest app at
   `localtest/api/cdn/orgs/altinn-orgs.json` and point each consumer's configurable base URL at it; the BFF hard-codes
   the URL and needs a one-line patch (the Arbeidsflate strand).
7. **Inbox link** (section 7): the app frontend returns `http://{host}/` for `local.altinn.cloud` before it looks at
   `arbeidsflateInboxUrl`; the minimal monorepo change is three lines in `urlHelper.ts` plus two env lines in
   `run-app.sh`. Nothing in app-lib v8 or the backend links to the inbox.

Effort for 1 to 6 together: about 4 to 5 days; 7 is half a day in the monorepo. The largest risk is the Postgres
move (shared with two other sessions and every component; mechanical but disruptive); the second is the
registered-client OIDC path, verified by reading only.

## 1. Maskinporten token endpoint in the localtest app

### 1.1 What the clients post and expect

| Client | Token URL | Request | Assertion | Expects | Exchange |
| --- | --- | --- | --- | --- | --- |
| `Altinn.ApiClients.Maskinporten` 10.1.0 (adapter, Dialogporten, Correspondence) | [`GetTokenEndpoint`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L416): `prod`/`ver1`/`ver2`/`test` → `https://…maskinporten.no/token`, anything else throws | `application/x-www-form-urlencoded`: `grant_type=urn:ietf:params:oauth:grant-type:jwt-bearer&assertion=…` ([`GetUrlEncodedContent`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L357)), `Accept: application/json` | RS256 over the JWK or certificate; claims `aud` (per environment), `scope`, `iss` = client id, `exp` = now+10 s, `iat`, `jti`, optional `resource`, `consumer_org`, `authorization_details` for system users ([`GetJwtAssertion`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L206)) | `TokenResponse` `{access_token, expires_in, scope, token_type}`; on non-2xx `{error, error_description}` ([`ErrorReponse`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Models/ErrorResponse.cs)), cached for `expires_in` − 5 s | When `ExhangeToAltinnToken`: `GET {GetTokenExchangeEndpoint(TokenExchangeEnvironment ?? Environment)}` with the Maskinporten token as bearer, `?test=true` only when `UseAltinnTestOrg` ([`ExchangeToAltinnToken`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L127), [L161](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L161), [`GetTokenExchangeEndpoint`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L428): `prod`, `tt02`, `at21`-`at24`, `yt01`, hard-coded hosts) |
| Arbeidsflate BFF ([`maskinporten.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts)) | `${MASKINPORTEN_ISSUER}token` ([L69](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts#L69)); default `https://test.maskinporten.no/` ([`config.ts:52`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/config.ts#L52)), **configurable, any scheme** | same form body, `Content-Type` form | RS256 with `MASKINPORTEN_JWK` (base64 JSON, must carry `kid`), `iss` = `MASKINPORTEN_CLIENT_ID`, `aud` = the issuer, `scope` = `MASKINPORTEN_SCOPE` (default `altinn:register/partylookup.admin altinn:serviceowner/notifications.create`), 100 s lifetime ([L22-L52](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts#L22-L52)) | `{access_token, token_type, expires_in, scope}`, cached in Redis for `expires_in` − 30 s | `GET {PLATFORM_BASEURL}/authentication/api/v1/exchange/maskinporten` ([L20](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/maskinporten.ts#L20)), never `test=true`; the exchanged token's `exp` sets the cache TTL |
| Correspondence's own `MaskinportenTokenService` (JWK rotation admin only) | `https://{test.,}maskinporten.no/token` hard-coded | – | – | – | Not on the integration path; the integrations use the NuGet client above. Out of scope |

Nothing validates the assertion signature against a published client key except real Maskinporten; the fake does
not either.

What production Authentication's exchange checks on the Maskinporten token
([`AuthenticateMaskinportenToken`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs#L434-L453)):
signature against the JWKS of `MaskinportenWellKnownConfigEndpoint`, lifetime (10 s skew), the well-known URL must
contain the token's `iss`, a `consumer` claim with an organisation number, and, when the scope string contains
`altinn:serviceowner` ([`HasServiceOwnerScope`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs#L825-L835)),
the org code from the CDN list, Digdir becoming `ttd` with `?test=true`
([L477-L483](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs#L477-L483)).
`aud` is not validated. The fake's `Mint` already produces exactly this shape (`iss`, `scope`, `client_id`,
`token_type`, `jti`, `consumer {authority, ID}`, `iat`, `nbf`, `exp` +30 min), and the smoke test proves the exchange
accepts it. The token endpoint is therefore `Mint` behind an HTTP contract.

### 1.2 Contract

`POST {Localtest:PlatformEndpoint}localtest/api/maskinporten/token`, anonymous.

Request (`application/x-www-form-urlencoded`, read with `ReadFormAsync` as the fake ID-porten's `token` does):

| Field | Rule |
| --- | --- |
| `grant_type` | must be `urn:ietf:params:oauth:grant-type:jwt-bearer`, else `400 {"error":"unsupported_grant_type"}` |
| `assertion` | a compact JWT. Split on `.`, Base64Url-decode the payload, `JsonDocument.Parse`. The header and signature are ignored (an unsigned `alg: none` assertion from the .NET fake is accepted). Missing or undecodable: `400 {"error":"invalid_request"}`. No `exp`/`aud`/`iss`-format check: real Maskinporten rejects an expired assertion, but a lenient fake keeps clock skew between containers out of local testing |
| payload `iss` | the client id. Required, else `400 invalid_request` |
| payload `scope` | echoed as the token's `scope` and the response `scope`, unchecked. Empty → empty string |
| payload `consumer_org` | when present, this organisation number becomes the `consumer` (production: a delegated token on behalf of that org; the fake does not emit `supplier`) |
| payload `authorization_details` | ignored in the first version (system-user tokens are not needed by any consumer in this plan; the adapter's `ActivityDtoTransformer` references the type only for Dialogporten actors) |

Client id → organisation number, in order:

1. `consumer_org` in the assertion, if present.
2. `Maskinporten:Clients:{iss}` in the localtest app's configuration (env `Maskinporten__Clients__<client-id>=<orgnumber>`),
   a flat table so a tester can register `my-test-client=974760673` and get `brg` tokens.
3. If `iss` is nine digits, `iss` itself (convention: "client id = organisation number", which lets a compose file
   configure a consumer without touching the table).
4. Otherwise `Maskinporten:DefaultOrgNumber`, default `991825827` (Digdir), the number `/Home/GetTestOrgToken`
   uses, which the exchange turns into `ttd` with `test=true`. Unknown clients are not refused: a local tool is
   more useful permissive, and the log line names the client id and the mapping used.

Response `200 application/json`:

```json
{ "access_token": "<RS256 JWT from Mint>", "token_type": "Bearer", "expires_in": 1800, "scope": "<echoed scope>" }
```

The JWT is `MaskinportenApi.Mint(key, settings, orgNumber, scope)` with one change: `client_id` becomes the
assertion's `iss` instead of the constant `localtest` (keep the constant for `/Home/GetTestOrgToken`). `expires_in`
must match `Mint`'s 30 minutes (the .NET client caches for `expires_in` − 5 s, the BFF for − 30 s; a mismatch gives
stale or needlessly refreshed tokens). Errors are `400` with `{error, error_description}`, the two property names
both clients read.

Discovery: `GET .well-known/oauth-authorization-server` gains `token_endpoint` (`{Issuer}token`) and
`token_endpoint_auth_methods_supported: ["private_key_jwt"]`. Authentication's localtest key retriever reads only
`jwks_uri` and `issuer` from this document (`fakes/Keys/LocaltestKeyProviders.cs`), so the extra fields are inert
there; nothing else reads it today (the BFF and the .NET client never fetch discovery).

The `consumer` claim stays `{"authority":"iso6523-actorid-upis","ID":"0192:<orgnumber>"}`; the exchange's
`GetOrganisationNumberFromConsumerClaim` parses that form.

### 1.3 Implementation in the localtest app

| File | Change |
| --- | --- |
| `localtest/localtest/Maskinporten/MaskinportenApi.cs` | `api.MapPost("/token", …)` (~70 lines: form read, grant check, assertion decode, mapping, `Mint`, `Results.Json(…, LocaltestJsonContext.Default.MaskinportenTokenResponse)`); `Mint` gets a `clientId` parameter; `AuthorizationServerMetadata` gets `TokenEndpoint` and `TokenEndpointAuthMethodsSupported`; `MaskinportenSettings` gets `Clients` (`Dictionary<string,string>`) and `DefaultOrgNumber`, bound in `FromConfiguration` by iterating `configuration.GetSection("Maskinporten:Clients").GetChildren()` (no reflection binding, Native AOT) |
| `localtest/localtest/Maskinporten/MaskinportenApi.cs` (records) | `MaskinportenTokenResponse(access_token, token_type, expires_in, scope)` with `JsonPropertyName`; errors reuse the existing `TokenErrorResponse` from `IdPorten/IdPortenApi.cs` (move it to `Tokens/` so both features share it) |
| `localtest/localtest/LocaltestJsonContext.cs` | `[JsonSerializable(typeof(MaskinportenTokenResponse))]`; `JsonDocument` for the assertion needs no context entry |
| `localtest/localtest/README.md` | Maskinporten table: the token endpoint, the mapping rules, the `Maskinporten__Clients__*` setting; drop "There is no token endpoint" |
| `localtest/localtest/smoke-test.sh` | section "identity providers": (a) token for an unsigned assertion with `iss=localtest-smoke`, `scope=altinn:serviceowner/instances.read` → 200, payload `consumer.ID == 0192:991825827`, `scope` echoed, `client_id == localtest-smoke`; (b) `GET authentication/api/v1/exchange/maskinporten?test=true` with it → 200 and `urn:altinn:org=ttd` (reuse the existing org-token check); (c) `iss=974760673` → `consumer.ID == 0192:974760673`; (d) `Maskinporten__Clients__smoke-brg=974760673` is a compose-time setting, so test it only when set; (e) `grant_type=client_credentials` → 400 `unsupported_grant_type`; (f) missing assertion → 400 `invalid_request`; (g) discovery has `token_endpoint` |

Compose: `localtest` service gets `Maskinporten__Clients__dialogporten-localtest=991825827` and
`Maskinporten__Clients__arbeidsflate-localtest=991825827` only if the consumers' client ids are not numeric; with the
convention in rule 3 the Dialogporten and Arbeidsflate strands can simply set `ClientId=991825827` and nothing is
needed here. Decide one way in the strands; this document assumes the convention.

## 2. `IMaskinportenService` fake for the .NET consumers

### 2.1 The interface and who calls what

All three consumers reference exactly `Altinn.ApiClients.Maskinporten` **10.1.0**
([Dialogporten Infrastructure csproj](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Digdir.Domain.Dialogporten.Infrastructure.csproj#L4),
[adapter csproj](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj#L5),
[Correspondence Integrations csproj](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/Altinn.Correspondence.Integrations.csproj#L18)),
all target `net10.0`. The interface at 10.1.0 (decompiled; identical to
[`IMaskinportenService.cs`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Interfaces/IMaskinportenService.cs#L8)
in the clone):

```csharp
Task<TokenResponse> GetToken(JsonWebKey jwk, string environment, string clientId, string scope, string resource, string consumerOrgNo = null, bool disableCaching = false);
Task<TokenResponse> GetToken(X509Certificate2 cert, string environment, string clientId, string scope, string resource, string consumerOrgNo = null, bool disableCaching = false);
Task<TokenResponse> GetToken(string base64EncodedJWK, string environment, string clientId, string scope, string resource, string consumerOrgNo = null, bool disableCaching = false);
Task<TokenResponse> GetToken(IClientDefinition clientDefinition, bool disableCaching = false);
Task<TokenResponse> GetToken(IClientDefinition clientDefinition, MaskinportenRequestContext requestContext, bool disableCaching = false);
Task<TokenResponse> ExchangeToAltinnToken(TokenResponse tokenResponse, string environment, string userName = null, string password = null, bool disableCaching = false, bool isTestOrg = false);
```

None of the consumers calls the service directly. Each registers client definitions and attaches the package's
`MaskinportenTokenHandler` to typed or Refit HTTP clients; the handler calls
[`GetToken(clientDefinition, requestContext)`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Handlers/MaskinportenTokenHandler.cs#L58)
and, on a 401, the same with `disableCaching: true`. The handler factory takes `IMaskinportenService` from DI
([`MaskinportenHttpMessageHandlerFactory.cs:19`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Factories/MaskinportenHttpMessageHandlerFactory.cs#L19)),
and the production service is registered with
[`TryAddSingleton`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Extensions/ServiceCollectionExtensions.cs#L170).
So the fake only needs the two `IClientDefinition` overloads to work; the other four can throw
`NotSupportedException` with a message, and `ExchangeToAltinnToken` should work because the fake's own `GetToken`
calls it.

| Consumer | Registration | Settings that reach the fake | Exchange |
| --- | --- | --- | --- |
| Dialogporten | [`AddMaskinportenHttpClient<…, SettingsJwkClientDefinition>`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L525-L557) for `ICloudEventBus`, `IPartyNameRegistry`, `IAltinnAuthorization`, each `ExhangeToAltinnToken = true`; one `Infrastructure:Maskinporten` section for all ([L688-L700](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L688-L700)) | validator requires non-empty `ClientId`, `Scope`, `Environment`, `EncodedJwk` ([`MaskinportenSettingsValidator`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs#L224-L231)); `TokenExchangeEnvironment` per environment file | yes, no `test=true` |
| Adapter | [`RegisterMaskinportenClientDefinition<SettingsJwkClientDefinition>`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L232-L237) once, `AddMaskinportenHttpMessageHandler` on four Refit clients; also `AddDialogportenClient` (the WebApiClient package, same `IMaskinportenService`) | [`appsettings.json`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/appsettings.json#L9-L17): `Environment`, `TokenExchangeEnvironment` from env, `ExhangeToAltinnToken: true`, scopes incl. `altinn:storage/instances.syncadapter` | yes |
| Correspondence | [`RegisterAltinnHttpClient`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L95-L103) per integration, only when `MaskinportenSettings:ClientId` is set ([L45-L71](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.Integrations/DependencyInjection.cs#L45-L71)) | [`appsettings.json`](https://github.com/Altinn/altinn-correspondence/blob/6dcf561945f73e8f28f0a95747ee4fafa012aa89/src/Altinn.Correspondence.API/appsettings.json#L14-L18): `ExhangeToAltinnToken: true` | yes |

### 2.2 Why configuration alone cannot do it

`MaskinportenSettings` 10.1.0 has `ClientId`, `Scope`, `Resource`, `Environment`, certificate and JWK fields, `Kid`,
`ConsumerOrgNo`, enterprise user fields, `ExhangeToAltinnToken`, `TokenExchangeEnvironment`, `UseAltinnTestOrg`,
`EnableDebugLogging`, `OverwriteAuthorizationHeader`
([`MaskinportenSettings.cs`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Config/MaskinportenSettings.cs)).
No token URL, no exchange URL, no custom environment: both `switch` tables throw `ArgumentException` for an unknown
environment. The `HttpClient` injected into `MaskinportenService` is the default one from `AddHttpClient()`; a
primary handler that rewrites `maskinporten.no` to the localtest app would work for the token call but not for the
exchange host, and would bypass the test organisation switch. The fake is the honest path.

### 2.3 The fake

`LocaltestMaskinportenService : IMaskinportenService`, one file, ~150 lines:

- `GetToken(IClientDefinition d, MaskinportenRequestContext ctx, bool disableCaching)`:
  build an unsigned assertion (`{"alg":"none","typ":"JWT"}` header, payload `iss = d.ClientSettings.ClientId`,
  `scope = d.ClientSettings.Scope`, `consumer_org = d.ClientSettings.ConsumerOrgNo` when set, `jti`, `iat`, `exp`), `POST`
  it as `grant_type=…jwt-bearer&assertion=…` to `{Localtest:Maskinporten:Issuer}token`, deserialise `TokenResponse`.
  Then, as production does: if `ExhangeToAltinnToken == true`, call `ExchangeToAltinnToken(response, environment,
  isTestOrg: d.ClientSettings.UseAltinnTestOrg ?? true)`. The default `true` is the localtest deviation: every
  consumer here is Digdir's number and wants to come out as `ttd`. Enterprise-user settings are ignored (production
  Authentication answers 410 for them anyway).
- `GetToken(IClientDefinition d, bool disableCaching)` → the above with `null` context.
- `ExchangeToAltinnToken(...)`: `GET {Localtest:Maskinporten:ExchangeEndpoint}{?test=true}` with the bearer, body is the
  Altinn JWT string, wrapped in a `TokenResponse { TokenType = "altinn", ExpiresIn = response.ExpiresIn, Scope }` as
  production does.
- Caching: inject the package's `ITokenCacheProvider` (always registered) and cache under the same keys
  (`clientId+scope+resource+consumerOrg`, MD5 of the token for the exchange) for `expires_in` − 5 s, so a consumer
  does not hit the localtest app on every request. ~30 of the 150 lines.
- The other four overloads: `throw new NotSupportedException("localtest Maskinporten fake: use a client definition")`.
- Options `LocaltestMaskinportenOptions { Issuer = "http://local.altinn.cloud:8000/localtest/api/maskinporten/",
  ExchangeEndpoint = "http://local.altinn.cloud:8000/authentication/api/v1/exchange/maskinporten" }`, bound from
  `Localtest:Maskinporten` (both overridable, the exchange one for a Correspondence container that reaches
  Authentication by service name).

Where it lives and how it reaches each build:

```
localtest/components/shared/fakes/Maskinporten/LocaltestMaskinportenService.cs
localtest/components/shared/fakes/Maskinporten/LocaltestMaskinportenOptions.cs
localtest/components/shared/fakes/Maskinporten/LocaltestMaskinportenExtensions.cs   // AddLocaltestMaskinporten(services, config): RemoveAll<IMaskinportenService>() + AddSingleton
localtest/components/shared/README.md
```

Each consumer's `localtest.build.targets` compiles it into the project that references the package (the pattern the
authentication and events components use for their own `fakes/`):

```xml
<ItemGroup Condition="'$(MSBuildProjectName)' == 'Digdir.Domain.Dialogporten.Infrastructure'">   <!-- adapter: Altinn.DialogportenAdapter.WebApi; Correspondence: Altinn.Correspondence.Integrations -->
  <Compile Include="$(MSBuildThisFileDirectory)shared/fakes/Maskinporten/*.cs" LinkBase="Localtest/Shared/Maskinporten" />
</ItemGroup>
```

A compiled-in source file is version-proof: it builds against whatever package version the host references, so a
consumer that moves to 10.2 or 11 does not need a second copy as long as the two `IClientDefinition` overloads keep
their shape (the interface has been stable from 9.x through 10.1.0; verify at every re-pin).

The build context: compose builds each component with `build: ./components/<name>`, and a path outside that
context cannot be copied. Use the compose spec's `additional_contexts`, which podman-compose 1.5.0 (installed here)
and docker compose both support:

```yaml
dialogporten-webapi:
  build:
    context: ./components/dialogporten
    additional_contexts:
      shared: ./components/shared
```

and in the component Dockerfile, marked `localtest:`:

```dockerfile
# localtest: the fakes shared by the Maskinporten clients, from the compose additional context "shared".
COPY --from=shared fakes/ shared/fakes/
```

with `$(MSBuildThisFileDirectory)shared/` resolving because the Dockerfile copies `localtest.build.*` and `shared/`
side by side under `/src`. Checked: podman-compose's source handles `additional_contexts` and passes `--build-context`.
Not checked: an actual build (see Not verified).

DI: each component's `#if LOCALTEST` hook calls `services.AddLocaltestMaskinporten(configuration)` after the production
`AddInfrastructure`/`AddHttpClients`, since `RemoveAll` must run after `TryAddSingleton`. No directive is needed
around the registration itself, only the hook the other strands already plan.

Configuration the consumers set so the production validators pass: `ClientId = 991825827`, `Scope` as upstream,
`Environment = test`, `TokenExchangeEnvironment = at23` (any valid value; the fake never reads them), `EncodedJwk` =
a committed dummy (`eyJrdHkiOiJSU0EifQ==`, `{"kty":"RSA"}`; `SettingsJwkClientDefinition.GetClientSecrets` is never
called by the fake), `ExhangeToAltinnToken = true`.

The BFF needs no fake: `MASKINPORTEN_ISSUER=http://local.altinn.cloud:8000/localtest/api/maskinporten/`,
`MASKINPORTEN_CLIENT_ID=991825827`, `MASKINPORTEN_JWK` = any RSA private JWK with a `kid` (the BFF signs; the fake
ignores the signature). The BFF exchanges without `test=true`; its scope `altinn:serviceowner/notifications.create`
contains `altinn:serviceowner`, so the exchanged token carries `urn:altinn:org=digdir`, which is right for Register's
`partylookup.admin` (no org check) and does not matter for Notifications' `future/log` (no Notifications component).

## 3. Authentication as an OIDC provider for a registered client (Arbeidsflate's BFF)

### 3.1 What the BFF does

[`oidc.ts`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts):

- Discovery at `https://${OIDC_PLATFORM_URL}/.well-known/openid-configuration` ([L165-L166](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L165-L166), again in [`verifyToken.ts:106-107`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/verifyToken.ts#L106-L107)); scheme hard-coded, the Arbeidsflate strand's patch.
- `/api/login`: `response_type=code`, `scope=digdir:dialogporten.noconsent openid altinn:portal/enduser altinn:correspondence.forward.check`, `acr_values=idporten-loa-substantial` (or `-high`), `state`, `nonce`, PKCE S256, `redirect_uri={HOSTNAME}/api/cb` ([L343-L353](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L343-L353)).
- `/api/cb`: `POST token_endpoint` with `client_secret_basic`, form `grant_type=authorization_code, client_id, code_verifier, code, redirect_uri` ([L266](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L266)); reads `id_token` (decoded, not signature-verified: `nonce`, `sid`, `pid`, `locale`), `access_token`, `refresh_token`, `expires_in`, `refresh_token_expires_in` ([L282](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L282)); stores `idp-sid:{sid}` → session in Redis ([L311](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L311)).
- Refresh: `grant_type=refresh_token` at the same endpoint (`verifyToken.ts`); logout: `end_session_endpoint?id_token_hint=` ([L157](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L157)); front-channel logout `GET /api/frontchannel-logout?iss&sid` ([L180-L194](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/auth/oidc.ts#L180-L194)). No `userinfo`, no JWKS fetch.

So the provider must give: discovery with `issuer`, `authorization_endpoint`, `token_endpoint`,
`end_session_endpoint`; a code flow with PKCE, nonce and `acr_values`; a `token` endpoint accepting
`client_secret_basic`, answering `id_token` with `nonce`, `sid`, `pid`, plus `refresh_token` and
`refresh_token_expires_in`; a refresh grant; an end-session endpoint; front-channel logout to the client.

### 3.2 The provider side in the vendored Authentication

All production, all present in `vendor/`:

| Step | Code | Stores touched |
| --- | --- | --- |
| Discovery | [`OpenIdController.GetOpenIdConfigurationAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OpenIdController.cs#L46-L80): `issuer`, `authorization_endpoint`, `token_endpoint`, `jwks_uri`, `end_session_endpoint`, `subject_types_supported: pairwise`, `id_token_signing_alg_values_supported: RS256`; URLs from `GeneralSettings.AltinnOidc*` derived from `PlatformEndpoint` ([`GeneralSettings.cs:119`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Configuration/GeneralSettings.cs#L119)). Quirk: `response_types_supported` is `["token"]`; the BFF does not read it | none |
| JWKS | [`openid-configuration/jwks`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OpenIdController.cs#L89-L90) from the certificate provider (the localtest one serves the shared test certificate) | none |
| `GET authentication/api/v1/authorize` for a registered client | [`OidcFrontChannelController.Authorize`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcFrontChannelController.cs#L33-L34) → [`OidcServerService.Authorize`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L100-L104): client lookup, [basic validation](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Services/Validation/AuthorizeRequestValidator.cs#L39-L47) (`response_type=code`, scope contains `openid`), [client binding](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Services/Validation/AuthorizeClientPolicyValidator.cs#L9) (exact `redirect_uri` string match, every requested scope in `AllowedScopes`), [`PersistLoginTransaction`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1244-L1268), session reuse if the browser already has a session with enough acr, else [`CreateUpstreamLoginTransaction`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1161) and a redirect to the upstream provider chosen by [`ChooseProvider`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1387-L1404): acr → provider key, else the default `idporten`, the same provider the `goTo` flow uses, with the upstream `acr_values` mapped through the acr catalogue | `IOidcServerClientRepository.GetClientAsync` (**missing**), `ILoginTransactionRepository.InsertAsync` (**missing**), `IOidcSessionRepository` (in memory), `IUpstreamLoginTransactionRepository` (in memory) |
| Picker | The localtest app's fake ID-porten `authorize` preselects the level from `acr_values` (`IdPorten/IdPortenApi.cs:50`); `idporten-loa-substantial` is level 3 in both level tables | – |
| `GET upstream/callback` | [`HandleUpstreamCallback`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L251): shared up to session creation, then branches on [`upstreamTx.RequestId != null`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L385-L418): registered client → [`ValidateDownstreamCallbackState`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L903) loads the login transaction, [`CreateDownstreamAuthorizationCode`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1272-L1278) inserts the code, 302 to the client's `redirect_uri?code&state` with the same four cookies the `goTo` branch sets; unregistered → redirect to `goTo`. The two branches share everything before the `if`, which is what the stack exercises today | `ILoginTransactionRepository.GetByRequestIdAsync` (**missing**), `IAuthorizationCodeRepository.InsertAsync` (**missing**) |
| `POST token` | [`OidcTokenController.Token`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/OidcTokenController.cs#L27-L29) → [`TokenService.ExchangeAuthorizationCodeAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L41): client auth (`client_secret_basic`/`post`, [L372-L389](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L372-L389): pepper = `Convert.FromBase64String(OidcRefreshTokenPepper)`, `ClientSecrets.Verify` against `ClientSecretHash`, legacy PBKDF2 hashes upgraded through `TryUpgradeClientSecretHashAsync`), code lookup, PKCE, [`TryConsumeAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L71), session lookup, then [`TryIssueInitialRefreshAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L444-L480) (family per client+subject+sid, HMAC lookup key with the same pepper). Response [`TokenResponseDto`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Models/Oidc/TokenResponseDto.cs#L5-L12): `access_token`, `token_type`, `expires_in`, `id_token`, `scope`, `refresh_token`, `refresh_token_expires_in`, exactly what the BFF reads | `IOidcServerClientRepository.GetClientAsync`, `.TryUpgradeClientSecretHashAsync` (**missing**); `IAuthorizationCodeRepository.GetAsync`, `.TryConsumeAsync` (**missing**); `IRefreshTokenRepository.GetOrCreateFamilyAsync`, `.InsertAsync` (**missing**); `IOidcSessionRepository.GetBySidAsync` (in memory) |
| `POST token` refresh | [`RefreshAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L92): lookup by HMAC key, reuse detection revokes the family, rotation = insert new row + `MarkUsedAsync` ([L142-L143](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L142-L143)) | `IRefreshTokenRepository.GetByLookupKeyAsync`, `.InsertAsync`, `.MarkUsedAsync`, `.RevokeAsync`, `.RevokeFamilyAsync` (**missing**) |
| `GET openid/logout` | [`EndSessionAsync`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L469): `id_token_hint` → client lookup for `post_logout_redirect_uri`, session delete, revoke every family of the `sid` ([L593-L598](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L593-L598)), front-channel logout iframes to each client in the session ([`GetClientsPartOfSession`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1506)) | `IOidcServerClientRepository` (**missing**), `IRefreshTokenRepository.GetFamiliesByOpSidAsync`, `.RevokeFamilyAsync` (**missing**), `IAuthorizationCodeRepository.GetClientsPartOfSession` (**missing**) |
| `userinfo` | does not exist; the BFF does not call it | – |

Repository registration is production's
[`PersistanceDependencyInjection.AddPersistanceLayer`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/Extensions/PersistanceDependencyInjection.cs#L27-L50),
every repository `TryAddTransient`, so the localtest singletons registered before it win
(`fakes/LocaltestAuthenticationExtensions.cs` does this for three of them today).

### 3.3 Gaps

1. Four repositories resolve to the Postgres implementations with no database: `IOidcServerClientRepository`
   ([interface](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/RepositoryInterfaces/IOidcServerRepository.cs#L9),
   3 methods), `ILoginTransactionRepository`
   ([2 methods](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/RepositoryInterfaces/ILoginTransactionRepository.cs#L8-L20)),
   `IAuthorizationCodeRepository`
   ([4 methods](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/RepositoryInterfaces/IAuthorizationCodeRepository.cs#L8-L29)),
   `IRefreshTokenRepository`
   ([7 methods](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/RepositoryInterfaces/IRefreshTokenRepository.cs#L8-L44)).
   The earlier research missed `ILoginTransactionRepository`; it is on the registered-client path twice.
2. No registered client. Production inserts rows into `oidcserver.client`
   ([`v0.23/02-setup-client-table.sql`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/Migration/v0.23/02-setup-client-table.sql#L1-L21))
   by operations; no seed exists in the migrations.
3. **Pepper bug.** `fakes/LocaltestAuthenticationExtensions.cs` sets `GeneralSettings:OidcRefreshTokenPepper` to
   `localtest-pepper`. Production decodes it with `Convert.FromBase64String` in three places
   ([`TokenService.cs:204`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L204),
   [L379](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L379),
   [L460](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/TokenService.cs#L460));
   `-` is not a base64 character, so `client_secret_basic` authentication answers `invalid_client "Server
   configuration error"` and no refresh token is ever issued. Nothing noticed because the `goTo` flow never reaches
   the token endpoint. Fix: `bG9jYWx0ZXN0LXBlcHBlcg==` (base64 of the same string). One line.

### 3.4 Fakes to write (`localtest/components/authentication/fakes/Persistence/`)

| File | Interface | Mirrors | Responsibility | Size |
| --- | --- | --- | --- | --- |
| `InMemoryOidcServerClientRepository.cs` | `IOidcServerClientRepository` | [`OidcServerClientRepository.cs`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/RepositoryImplementations/OidcServer/OidcServerClientRepository.cs) (373 lines, SQL + row mapping) | `ConcurrentDictionary<string, OidcClient>` seeded from `Localtest:Authentication:OidcClients` at construction; `GetClientAsync` returns enabled clients; `InsertClientAsync` adds (so a test can register at runtime); `TryUpgradeClientSecretHashAsync` replaces the hash when it matches (production upgrades PBKDF2 to HMAC on first use; seed HMAC hashes directly and this becomes a no-op) | ~110 |
| `InMemoryLoginTransactionRepository.cs` | `ILoginTransactionRepository` | [`LoginTransactionRepository.cs`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/RepositoryImplementations/OidcServer/LoginTransactionRepository.cs) (345) | `InsertAsync` builds a `LoginTransaction` from `LoginTransactionCreate` with a new `RequestId`, `Status = "pending"`, `CreatedAt`, `ExpiresAt` (the Postgres default lifetime; copy the interval from the SQL); `GetByRequestIdAsync`; prune expired rows on insert, as the in-memory upstream repository does | ~60 |
| `InMemoryAuthorizationCodeRepository.cs` | `IAuthorizationCodeRepository` | [`AuthorizationCodeRepository.cs`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/RepositoryImplementations/OidcServer/AuthorizationCodeRepository.cs) (224) | rows keyed by code ([`AuthCodeRow`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Models/Oidc/AuthCodeRow.cs#L11-L36) over [`OidcBindingContextBase`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Models/Oidc/OidcBindingContextBase.cs#L12-L40): client, subject, session, party ids, scopes, acr, amr, nonce, auth time); `TryConsumeAsync` atomic under a lock: unused, unexpired, same client id and redirect URI → mark used; `GetClientsPartOfSession` = distinct client ids of codes with that session id | ~90 |
| `InMemoryRefreshTokenRepository.cs` | `IRefreshTokenRepository` | [`RefreshTokenRepository.cs`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Persistance/RepositoryImplementations/OidcServer/RefreshTokenRepository.cs) (325) | families keyed by `(clientId, subjectId, opSid)` → `Guid`; rows ([`RefreshTokenRow`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Models/Oidc/RefreshTokenRow.cs#L11-L86)) keyed by `TokenId`, index by `LookupKey` (byte array; compare with `SequenceEqual` or key on `Convert.ToHexString`); `MarkUsedAsync` sets `Status = "used"`, `RotatedToTokenId`; `RevokeAsync`/`RevokeFamilyAsync` set `Status = "revoked"`, `RevokedAt`, `RevokedReason`; `GetFamiliesByOpSidAsync`. The rows are `init`-only records, so updates replace the row with a copy (`with`-less class: write a small `Copy(status, …)` helper) | ~110 |
| `LocaltestAuthenticationExtensions.cs` (edit) | – | – | register the four singletons next to the existing three; fix the pepper; bind `Localtest:Authentication:OidcClients` | +15 |
| `LocaltestAuthenticationOptions.cs` (edit) | – | – | `OidcClients: List<LocaltestOidcClient>` | +25 |

Total about 350 lines plus 40 of configuration classes. The alternative, Authentication on the shared Postgres with
its production migrations, means a fourth Postgres consumer, superuser init scripts, and a seed row written by SQL;
the four fakes are smaller than that and keep the component's "no database" property. Revisit if system-user or
self-identified-user flows (another six repositories) are ever wanted.

### 3.5 Client seeding

Configuration shape (`Localtest:Authentication:OidcClients:0:*`, env-overridable; one client in the component's
`appsettings` defaults so compose only sets the secret if it wants a different one):

```json
{
  "ClientId": "arbeidsflate-localtest",
  "ClientName": "Arbeidsflate (localtest)",
  "ClientSecret": "arbeidsflate-localtest-secret",
  "ClientType": "Confidential",
  "TokenEndpointAuthMethod": "ClientSecretBasic",
  "RedirectUris": ["http://af.local.altinn.cloud:8000/api/cb"],
  "PostLogoutRedirectUris": ["http://af.local.altinn.cloud:8000/"],
  "FrontchannelLogoutUri": "http://af.local.altinn.cloud:8000/api/frontchannel-logout",
  "AllowedScopes": ["openid", "digdir:dialogporten.noconsent", "altinn:portal/enduser", "altinn:correspondence.forward.check"],
  "RequirePkce": true,
  "RequireNonce": true,
  "SubjectType": "Pairwise"
}
```

The fake maps this to the [`OidcClient`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Models/Oidc/OidcClient.cs#L37)
constructor, hashing the plain secret with
[`ClientSecretHasher.HashHmac(secret, pepper)`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication.Core/Helpers/ClientSecretHasher.cs#L25)
with the base64-decoded `OidcRefreshTokenPepper`, so the plain secret lives only in localtest configuration and the
production verifier runs unchanged. Token lifetimes are not per client: `GeneralSettings.JwtValidityMinutes` (access
token and sliding refresh), `MaxSessionTimeInMinutes` (absolute refresh), `OidcRefreshTokenValidityMinutes` (30);
leave production's values. Scopes must be lower-case (the constructor lower-cases allowed scopes and the validator
compares requested scopes as sent; the BFF sends lower-case). The redirect URI must match the BFF's `HOSTNAME`
exactly, including the port; the host name is the Arbeidsflate strand's decision (this plan assumes
`af.local.altinn.cloud:8000`).

The BFF is then configured with `OIDC_CLIENT_ID=arbeidsflate-localtest`, `OIDC_CLIENT_SECRET=…`,
`OIDC_PLATFORM_URL=local.altinn.cloud:8000/authentication/api/v1/openid` (scheme patched to http). The issuer the
BFF compares in front-channel logout is `{PlatformEndpoint}authentication/api/v1/openid/`, which is what discovery
answers.

### 3.6 Verification plan

`localtest/components/authentication/smoke-test.sh` (new) or a section in `localtest/smoke-test.sh`, with `curl` and
the seeded client, no BFF needed: (1) discovery has the four endpoints; (2) `authorize` with the BFF's exact
parameters → 302 to the picker with `acr_values`; (3) post the picker for a user → 302 to `upstream/callback` → 302
to `http://af.local.altinn.cloud:8000/api/cb?code&state` with the four cookies; (4) `token` with
`client_secret_basic`, code and verifier → 200 with `id_token` (`nonce`, `sid`, `pid`, `aud` = client id), `refresh_token`,
`refresh_token_expires_in`; (5) a second `token` with the same code → 400; (6) `refresh_token` grant → new pair, the
old one → family revoked on reuse; (7) `openid/logout?id_token_hint` → 302 to the post-logout URI and the
front-channel iframe to `/api/frontchannel-logout`; (8) wrong secret → `invalid_client`; (9) unregistered
`redirect_uri` → `invalid_request`. Then Cypress's sign-in specs must still pass (the `goTo` branch is untouched).

## 4. Postgres 18

### 4.1 Who needs what

Dialogporten: `AuthorizationContext.Id` defaults to `uuidv7()` in
[`20260807085531_AddAuthorizationContext.cs:19`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Migrations/20260807085531_AddAuthorizationContext.cs#L19)
and in the model
([`AuthorizationContextConfiguration.cs:13`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Configurations/Dialogs/AuthorizationContexts/AuthorizationContextConfiguration.cs#L13)),
so every insert into that table calls it at runtime, not only the migration. `uuidv7()` is built in from PostgreSQL
18. Everything else is `gen_random_uuid()` (PG 13+), `pg_trgm` and `btree_gin`
([`DialogDbContext.cs:168-169`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/DialogDbContext.cs#L168-L169),
contrib, in the alpine image), `tsvector` columns and plpgsql indexer functions, per-table autovacuum settings; 18
raw-SQL migrations, none using a PG 17/18-only statement apart from `uuidv7()`. Upstream runs
[`postgres:18.3`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/docker-compose-db-redis.yml#L3)
in compose and in the integration tests
([`DialogApplication.cs:61`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/tests/Digdir.Domain.Dialogporten.Application.Integration.Tests/Common/DialogApplication.cs#L61)).
A `CREATE FUNCTION uuidv7()` shim on 16 would work but is a divergence from production SQL, which the model forbids.

Arbeidsflate's BFF: TypeORM migrations, upstream `postgres:16`
([`compose.yml:87`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/compose.yml#L87)); nothing version-specific found (not scanned in depth, the Arbeidsflate strand's job).

### 4.2 The existing consumers on 18

Scanned (migrations, init scripts, runtime SQL) by a sub-task; details per consumer:

| Consumer | Migrations | Version-sensitive SQL | Upstream version |
| --- | --- | --- | --- |
| Storage | Yuniql `v0.00`-`v0.38` at start; init `components/storage/postgres-init/10-storage.sh` + vendored `infra/postgres_init.sql` | `gen_random_uuid()`, identity columns, `COLLATE pg_catalog."default"`, `pg_advisory_xact_lock`, `ALTER SYSTEM SET max_connections`; no extensions | [`postgres:16`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/docker-compose.yml#L25) in compose and [CI](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/.github/workflows/build-and-analyze.yml#L27) |
| Access Management | EF Core `MigrateAsync` on every start (own `LocaltestDatabase.cs`, no init script) | `MERGE` (PG 15+), `CREATE EXTENSION hstore`, a schema-qualified `dbo.uuid_generate_v7()` built on `gen_random_uuid()` ([`ActivityLogFunctions.sql:6-23`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessMgmt.PersistenceEF/Migrations/ActivityLogFunctions.sql#L6-L23); no clash with the built-in `uuidv7()`), range partitioning, `SECURITY DEFINER` with explicit `search_path`, `CREATE INDEX CONCURRENTLY` | [`postgres_version = "16"`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/infra/main.tf#L227) in Azure; tests [`16.1-alpine`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/libs/Altinn.Authorization.Testing/src/Altinn.Authorization.Testing/PostgresTestEngine.cs#L166); the Resource Registry in the same repo already uses [`18.6-alpine`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.ResourceRegistry/compose.yml#L4) |
| Events | Yuniql `v0.00`-`v0.59` at start; `components/events/postgres-init/30-events.sh` | `CREATE EXTENSION pg_trgm` ([`v0.04`](https://github.com/Altinn/altinn-events/blob/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6/src/Events/Migration/v0.04/01-setup-index.sql#L1)), range partitioning with dynamic `PARTITION OF`, GIN `jsonb_path_ops` | [`postgres:17`](https://github.com/Altinn/altinn-events/blob/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6/test/Altinn.Platform.Events.IntegrationTests/container-images.json#L6) in integration tests |
| Workflow engine | EF Core at start under an advisory lock; `localtest/postgres-init/20-workflow-engine.sh` (`CREATE EXTENSION pg_stat_statements`) | `gen_random_uuid()`, `FOR UPDATE SKIP LOCKED`, `LISTEN/NOTIFY`, `pg_advisory_lock`, GIN; v7 ids are made client-side | [`postgres:18`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/workflow-engine/docker-compose.yaml#L3) in compose, [`18.3` in studioctl](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/cli/internal/config/images.go#L78), 18 in every Testcontainers fixture |

_2026-10-29 (tasks 60 and 62): Storage's init is `10-storage.sh` alone (no vendored `infra/postgres_init.sql`), and
Access Management's `LocaltestDatabase.cs` became the init script `45-access-management.sh`
(`localtest/postgres/Dockerfile`); every role's password is generated per installation._

Nothing uses a feature PG 17 or 18 removed; the only extensions are `pg_stat_statements`, `pg_trgm`, `hstore`
(contrib, present in `postgres:18-alpine`). Every role is created with a plain `PASSWORD '…'` and nothing sets
`password_encryption`, so the default `scram-sha-256` applies (the PG 18 md5 deprecation does not fire). Npgsql is
10.0.3 everywhere, Yuniql.PostgreSql 1.3.15. Not run on 18: the Yuniql and EF migrations themselves; the risk is
low (standard SQL) but the first `up` after the bump is the test.

### 4.3 Recommendation: one shared `postgres:18-alpine`

Reasons: the workflow engine team already runs 18 everywhere, Events tests on 17, the altinn-auth repository is
moving (Resource Registry on 18.6), and studioctl's manifest already pins a `postgres` 18.3 image, so a second
container is a step away from where the stack is heading; it would also need its own init-script convention,
healthcheck, volume and ~50 MB of RAM for the one consumer that needs 18 today and the BFF that would join it.

Changes in `localtest/docker-compose.yml`:

```yaml
postgres:
  image: docker.io/library/postgres:18-alpine          # was 16-alpine
  volumes:
    - postgres18-data:/var/lib/postgresql               # new volume name; the 18 image keeps PGDATA under /var/lib/postgresql/18/docker
```

- **Volume.** A 16 data directory does not start under 18 and nothing in the stack is worth migrating (every
  database is rebuilt from pushes and seeds; Access Management re-imports; Storage instances are test data). A new
  volume name makes the switch explicit; the old one is removed by hand. The README's "init scripts run only on an
  empty volume" note applies.
- **Mount path.** The `postgres:18` image moved its default `PGDATA` to `/var/lib/postgresql/18/docker` and declares
  the volume at `/var/lib/postgresql`; mounting at `/var/lib/postgresql/data` as today would leave the real data
  directory in the container layer (lost on recreate). The workflow engine's upstream compose and studioctl's
  component already mount `/var/lib/postgresql` for the same reason. Keep `shared_preload_libraries=pg_stat_statements`.
- Update the four other places that spell `postgres:16-alpine`: `components/storage/smoke-test.sh:37`,
  `components/workflow-engine/smoke-test.sh:25` (now `smoke/workflow-engine.sh`), `components/storage/README.md`, `localtest/README.md`.
- Dialogporten's own compose adds `wal_level=logical`; no consumer in `src` uses it (the earlier research's
  finding, still true), so do not add it.

Verification: fresh volume, `up`, then the existing smoke tests in order (`localtest/smoke-test.sh`, root
`smoke-test.sh`, `components/events/smoke-test.sh`, `components/authorization/smoke-test.sh`) and
`run-platform-regression.sh`. Coordination: the stack is shared with two other sessions; do the bump as its own commit
and `down`/`up` at an agreed time.

## 5. Redis

| Consumer | How it uses Redis | Without Redis |
| --- | --- | --- |
| Dialogporten WebApi, GraphQL, Service (and Janitor) | `AddStackExchangeRedisCache` + FusionCache backplane ([`InfrastructureExtensions.cs:179-180`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L179-L180)) for every host; HotChocolate `AddRedisSubscriptions` in both [`AddPubSubCapabilities`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L482) (Service) and [`AddPubCapabilities`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L514) (WebApi, GraphQL); the WebApi publishes dialog-updated topics from the EF save interceptor ([`ConvertDomainEventsToOutboxMessagesInterceptor.cs:16`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Interceptors/ConvertDomainEventsToOutboxMessagesInterceptor.cs#L16)) and the GraphQL host's SSE `/graphql/stream` subscribes, so the two processes need the same pub/sub | `Redis:ConnectionString` is required by the validator ([`InfrastructureSettings.cs:235-240`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs#L235-L240)). The cache degrades (circuit breaker, [`TryWithRegisteredBackplane`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L680-L683), health `Degraded`), `DisableCache=true` swaps in `NullFusionCache` ([L379](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L379)), but the subscription `ConnectionMultiplexer.Connect` has no fallback: the first dialog mutation in the WebApi would throw. An in-memory subscription provider would need a directive in `AddPub*Capabilities`, and cross-process SSE would still not work. Not worth it |
| Arbeidsflate BFF | Session store `RevocableRedisStore` ([`server.ts:71-78`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/server.ts#L71-L78)), `idp-sid:*` logout map, Maskinporten and Altinn token cache, organisations (24 h) and service-resources (24 h) caches, all through one `ioredis` client created at import ([`redisClient.ts:5`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/redisClient.ts#L5)) | The session store has an in-memory fallback when `REDIS_CONNECTION_STRING` is empty, but the default is set and `redisClient.ts` connects at import regardless; login (`idp-sid`), Maskinporten tokens and the organisations query all `await redisClient.*` without a fallback. Required |

Plan: one container, one logical database per consumer.

```yaml
redis:
  image: docker.io/valkey/valkey:8-alpine        # or docker.io/library/redis:7-alpine, see below
  command: ["valkey-server", "--save", "", "--appendonly", "no", "--maxmemory", "128mb", "--maxmemory-policy", "allkeys-lru"]
  healthcheck: { test: ["CMD", "valkey-cli", "ping"], interval: 2s, retries: 30 }
```

- Dialogporten: `Infrastructure__Redis__ConnectionString=redis:6379,defaultDatabase=0` (StackExchange.Redis syntax; the
  HotChocolate subscriptions use the same string and therefore the same db, which is what the topic prefix expects).
- BFF: `REDIS_CONNECTION_STRING=redis://redis:6379/1`.
- Correspondence later: db 2 (it uses Redis for Hangfire? not checked; the Correspondence strand decides).
- No persistence (`--save ""`): sessions and caches are rebuilt; a restart signs Arbeidsflate users out, as
  Authentication's in-memory sessions already do. 128 MB cap: the BFF caches the whole resource list (several MB) and
  Dialogporten caches authorized parties per user; well within it for a developer machine.

Image: upstream pins `redis:6.0-alpine` ([Dialogporten](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/docker-compose-db-redis.yml#L30)) and `redis:6.2-alpine`
([BFF](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/compose.yml#L55)), Azure Cache for Redis in production. Both clients (StackExchange.Redis, ioredis 5) speak RESP2
to anything 6+. `valkey/valkey:8-alpine` is the BSD-licensed fork and a drop-in (Redis moved to RSALv2/SSPL at 7.4,
AGPL at 8); the monorepo has no Redis today, so nothing constrains the choice. Pick Valkey unless the product owner
prefers the name developers know; `redis:7.2-alpine` is the last BSD Redis. Either way pin a digest at
implementation, as every other image in the stack is.

## 6. altinncdn.no

Who reads `https://altinncdn.no/orgs/altinn-orgs.json` (shape: `{"orgs": {"<org>": {"name": {nb,nn,en}, "logo",
"emblem", "orgnr", "homepage", "environments"}}}`, 108 orgs on 2026-10-20; `ttd` is absent and the stack maps it to
Digdir's `991825827`, see `components/access-management/fakes/ResourceRegistry/LocaltestAppResources.cs`):

| Consumer | Where | Configurable? |
| --- | --- | --- |
| Authentication `exchange/maskinporten` | [`OrganisationsService`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OrganisationsService.cs#L65-L105), orgnr → org code, cached 1 h, throws when the list is empty or unreachable (the exchange then answers 401) | yes: [`GeneralSettings:OrganisationRepositoryLocation`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/appsettings.json#L28) (full URL) |
| Storage | [`AltinnCdnOrganisationRepository`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/AltinnCdnOrganisationRepository.cs#L18) (org names for A2 correspondence and instance listings) | yes: [`GeneralSettings:OrganisationsUrl`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/appsettings.json#L32) (full URL) |
| Adapter | Refit [`IAltinnCdnApi` `GET /orgs/altinn-orgs.json`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/AltinnCdn/IAltinnCdnApi.cs#L6-L9) on base `Altinn:AltinnCdn`, reads `name` and `orgnr` only | yes: [`DialogportenAdapter:Altinn:AltinnCdn`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/appsettings.json#L24) (base URL) |
| Dialogporten | [`ServiceOwnerNameRegistryClient`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/OrganizationRegistry/ServiceOwnerNameRegistryClient.cs#L57), relative `orgs/altinn-orgs.json` on `AltinnCdn:BaseUri` ([settings](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs#L85-L88)); the `UseLocalDevelopmentOrganizationRegister` flag is declared ([`LocalDevelopmentSettings.cs:15`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/LocalDevelopmentSettings.cs#L15)) but never applied, so this client always runs | yes: `Infrastructure:AltinnCdn:BaseUri` (base URL, must end with `/`) |
| Arbeidsflate BFF | [`organizations/service.ts:60`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/bff/src/graphql/organizations/service.ts#L60), `fetch('https://altinncdn.no/orgs/altinn-orgs.json')`, cached 24 h in Redis | no: hard-coded; one-line patch to an env var in the Arbeidsflate strand (`ALTINN_CDN_URL`), or leave it on the internet |
| App backend (on the developer host) | [`AltinnCdnClient.cs:65`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/AltinnCdn/AltinnCdnClient.cs#L65), hard-coded, cached | no; runs on the host with internet, out of scope |
| Events, Register, Authorization, Access Management | none (Access Management's stand-in has its own small table) | – |

Four of five in-stack consumers take a URL from configuration, so the smallest shared change is a static file served
by the localtest app, not a proxy rule or a per-consumer fake:

- `localtest/localtest/Cdn/altinn-orgs.json`: a committed copy of the real file, trimmed to the orgs the stack and the
  test apps use (`digdir`, `brg`, `skd`, `nav`, `ssb`, `ttd`-as-Digdir is not an entry: Digdir's number maps to
  `digdir`, and `test=true` makes it `ttd`, exactly as in production) plus a `localtest.md` note on how to refresh it
  (`curl -o`). Keep the full file (about 50 KB) rather than trimming if the Arbeidsflate strand wants real logos in
  the inbox; logos are absolute `https://altinncdn.no/...` URLs the browser fetches itself.
- `localtest/localtest/Cdn/CdnApi.cs` (~25 lines): `GET /localtest/api/cdn/orgs/altinn-orgs.json` from an embedded
  resource (`<EmbeddedResource Include="Cdn/altinn-orgs.json" />`, `Results.Bytes(…, "application/json")`,
  `Cache-Control: no-cache`); Native AOT needs no JSON context because the bytes are passed through. Listed in
  `localtest/README.md` and the `localtest/api/{…}` 404 hint.
- Configuration: `authentication`: `GeneralSettings__OrganisationRepositoryLocation=http://local.altinn.cloud:8000/localtest/api/cdn/orgs/altinn-orgs.json`
  (set in `fakes/LocaltestAuthenticationExtensions.cs` as a forced value like the other platform URLs, derived
  from `PlatformEndpoint`; closes finding 8 of `reviews/authentication.md` and removes the internet dependency the
  root smoke test documents); `storage`: `GeneralSettings__OrganisationsUrl=…` the same URL; adapter:
  `DialogportenAdapter__Altinn__AltinnCdn=http://local.altinn.cloud:8000/localtest/api/cdn`; Dialogporten:
  `Infrastructure__AltinnCdn__BaseUri=http://local.altinn.cloud:8000/localtest/api/cdn/`.
- Smoke: the root smoke test's org-token step already fails without the org list; after the switch, run it with the
  authentication container's outbound network cut (`podman network disconnect` is not allowed on the shared stack;
  the implementer verifies on a private `up`).

A proxy route for the host name `altinncdn.no` was considered and rejected: it needs TLS and DNS interception inside
every container. The path under `localtest/api/cdn/` keeps the real path shape (`/orgs/altinn-orgs.json`) so a base-URL
override is enough for the Refit and relative-path clients.

## 7. App frontend and backend links to the inbox

App frontend ([`urlHelper.ts`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/utils/urls/urlHelper.ts#L35-L38)):
`buildArbeidsflateRedirectUrl` returns `http://${host}/` whenever
[`isLocalEnvironment`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/utils/urls/urlHelper.ts#L17-L19)
matches `local.altinn.cloud(:port)`, before it reads `GlobalData.platformFrontendSettings.arbeidsflateInboxUrl` /
`arbeidsflateDialogUrl`. Those settings come from the app backend's `PlatformFrontendSettings` section
([`PlatformFrontendSettings.cs:53-59`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Configuration/PlatformFrontendSettings.cs#L53-L59),
bound in [`ServiceCollectionExtensions.cs:205`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Extensions/ServiceCollectionExtensions.cs#L205),
sent to the browser by [`BootstrapGlobalService.cs:91`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Features/Bootstrap/BootstrapGlobalService.cs#L91)),
so the backend already carries them; only the local branch ignores them. Used by the instantiate header's inbox link
and the receipt's "go to archive"
([`ReceiptContainer.tsx:180`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/features/receipt/ReceiptContainer.tsx#L180)),
which passes the instance's `dialog.id` data value, the adapter's link back.

Minimal monorepo change (one pull request, outside localtest):

1. `urlHelper.ts`, in `buildArbeidsflateRedirectUrl`: in the local branch, if `settings.arbeidsflateInboxUrl` is set,
   fall through to the template logic (`arbeidsflateDialogUrl` with `{dialogId}`, no party switch when
   `accessManagementChangeAndRedirectUrl` is unset), else keep `http://${host}/`. Three lines; the tests at
   [`urlHelper.test.ts:30-32`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/utils/urls/urlHelper.test.ts#L30-L32)
   keep passing (no settings → front page) and gain one case with a configured inbox URL on the local host.
2. `localtest/run-app.sh`: export `PlatformFrontendSettings__ArbeidsflateInboxUrl=http://af.local.altinn.cloud:8000/`
   and `PlatformFrontendSettings__ArbeidsflateDialogUrl=http://af.local.altinn.cloud:8000/inbox/{dialogId}`
   (Arbeidsflate's route is [`/inbox/:id`](https://github.com/Altinn/dialogporten-frontend/blob/0011d957ddbd9d77e31fcce842fc41887297942c/packages/frontend/src/pages/routes.ts#L3)),
   only when the inbox profile is up (an `INBOX_URL` variable, empty by default). The same two lines go into
   studioctl's app environment later.

App-lib v8 (`/home/agent/code/app-lib-dotnet`) and the v9 backend: no inbox link anywhere; the only Dialogporten
references are the correspondence reference type and the notification association field, as the earlier research
found. Nothing to change on the backend.

## 8. Order, effort, risk, open questions

| # | Item | Depends on | Effort | Risk |
| --- | --- | --- | --- | --- |
| 1 | Token endpoint in the localtest app (1) | – | 0.5 day | Low: `Mint` and the exchange already work; AOT JSON is the only new surface |
| 2 | Shared `IMaskinportenService` fake + `additional_contexts` build wiring (2) | 1 | 1 day | Low-medium: the build-context mechanism is unverified in podman-compose; fallback is `context: ./components` with prefixed `COPY` paths |
| 3 | altinncdn.no file + settings (6) | – | 0.5 day | Low |
| 4 | Redis container (5) | – | 0.25 day | Low |
| 5 | Postgres 18 (4) | agreed downtime | 0.5 day + a full regression run | Medium: shared stack, new volume, every consumer's first migration on 18 |
| 6 | Authentication OIDC repositories, pepper fix, seeded client, smoke test (3) | – | 1.5 days | Medium: registered-client path verified by reading only; `TokenService` details (PKCE verify, pairwise `sub`) not traced line by line |
| 7 | App frontend inbox link + `run-app.sh` (7) | Arbeidsflate host name | 0.5 day | Low |

Order: 3 and 4 first (no dependents, unblock everyone), then 1 and 2 together (the adapter and Dialogporten strands
need them to start), 5 when the stack owners agree, 6 before the Arbeidsflate strand's first login test, 7 last.

Open questions for the product owner:

1. Postgres: one shared 18 (recommended) or a second container? The shared bump touches every session on this
   machine once.
2. Redis image: Valkey (BSD) or Redis 7.2 (last BSD Redis)? Either works; this is a licence and naming choice.
3. Maskinporten client ids: adopt "client id = organisation number" as the stack convention (no table entries
   needed), or require every client in `Maskinporten__Clients__*`?
4. Should the fake's exchange default to `test=true` (Digdir → `ttd`) for the .NET consumers, as this plan proposes?
   It matches `/Home/GetTestOrgToken` but differs from production, where the adapter and Dialogporten are `digdir`.
5. Authentication client seeding by configuration (recommended) versus Authentication on the shared Postgres with a
   seed row; the latter is larger and not needed by anything else.
6. The committed `altinn-orgs.json`: full copy (logos resolve to the real CDN in the browser) or a trimmed list?
7. The monorepo change in 7 is small; who owns it, and should studioctl pass the same two settings?

## Not verified

- No build or run of anything in this plan. In particular: `additional_contexts` end to end with podman-compose
  (only its source was checked), the shared fake compiling inside each host's analyzer and warnings-as-errors
  configuration (Dialogporten has `TreatWarningsAsErrors`; the props file relaxes it), and a PG 18 `up` of the four
  existing consumers.
- The registered-client OIDC path: `Authorize`, `HandleUpstreamCallback`, `ExchangeAuthorizationCodeAsync`,
  `RefreshAsync` and `EndSessionAsync` were read; `TokenService.ValidateTokenRequest` (PKCE verification, pairwise
  `sub` derivation, `id_token` claims) and `CreateUpstreamLoginTransaction`'s acr mapping were skimmed, not traced.
  The pepper bug is from reading `Convert.FromBase64String` on a string containing `-`; not reproduced.
- That `Altinn.ApiClients.Maskinporten` 10.1.0 is what the `WebApiClient` package the adapter uses
  (`AddDialogportenClient`) resolves at restore; its csproj pins 10.1.0 too, but transitive resolution was not run.
- The BFF's behaviour with `OIDC_PLATFORM_URL` over http (needs the Arbeidsflate strand's patch) and whether it
  validates `id_token` signatures anywhere else (it decodes without verifying in `oidc.ts`; `verifyToken.ts` was read
  only around the discovery call).
- Redis memory numbers (128 MB cap) are an estimate, not a measurement.
- The real `altinn-orgs.json` was fetched once to confirm the shape and that `ttd` is absent; the trimmed list of
  orgs the test apps use was not compiled.
- Correspondence's Redis use and Postgres version were not checked (its strand).

## What the earlier research (2026-10-18) got wrong or stale

- P1 placed the token endpoint under `authentication/api/v1/localtest/maskinporten/token` in the authentication
  component; since 2026-10-19 the fake Maskinporten lives in the localtest app, so it is `localtest/api/maskinporten/token`.
- It cited `altinn-auth` `392f957`; the vendored pin is `3d51810` (identical for Authentication).
- It listed three missing OIDC repositories; `ILoginTransactionRepository` is a fourth, on the registered-client path.
- It did not see the pepper bug.
- P2's "~150 lines, no directive" holds; the build-context problem for a shared source folder was not covered.
- P4 said "bump if the others pass (cheap to test)"; the test is still outstanding, but the scan above found nothing
  version-sensitive and found that the workflow engine already runs 18 upstream, which it did not mention.
- P5's `redis:7-alpine` is still fine; Valkey is now the suggested default.
- P6 proposed an ingress location; the consumers' base-URL settings make a localtest-app route the smaller change.
- Section 4.5's BFF env `MASKINPORTEN_ISSUER=…/authentication/api/v1/localtest/maskinporten/` must become
  `…/localtest/api/maskinporten/`.

## Summary for the orchestrator

- Six concrete changes, all small, none requiring a directive in vendored code: a token endpoint in the localtest
  app (reusing `Mint`), one shared `IMaskinportenService` source file compiled into each .NET consumer (the client
  library's URLs are hard-coded and the interface is identical for all three at 10.1.0), four in-memory OIDC
  repositories plus a seeded client in the authentication component, the shared Postgres to 18 with a new volume and
  mount path, one Valkey/Redis container with a logical db per consumer, and a committed `altinn-orgs.json` served by
  the localtest app that four of five consumers can be pointed at by configuration.
- Contracts the other strands build on: token URL `http://local.altinn.cloud:8000/localtest/api/maskinporten/token`,
  client id `991825827` (convention) or `Maskinporten__Clients__<id>=<orgnr>`, response `{access_token, token_type,
  expires_in: 1800, scope}`; fake options `Localtest:Maskinporten:{Issuer,ExchangeEndpoint}`, registration
  `services.AddLocaltestMaskinporten(configuration)` after production wiring, Dockerfile `COPY --from=shared`;
  OIDC client `arbeidsflate-localtest` with redirect `http://af.local.altinn.cloud:8000/api/cb` and the BFF's four
  scopes; CDN base `http://local.altinn.cloud:8000/localtest/api/cdn/`; Redis `redis:6379` db 0 (Dialogporten) and
  `redis://redis:6379/1` (BFF).
- Bug found on the way: Authentication's localtest `OidcRefreshTokenPepper` is not base64; production decodes it, so
  client-secret auth and refresh tokens fail until it is `bG9jYWx0ZXN0LXBlcHBlcg==`.
- Main risks: the Postgres move on a shared stack (mechanical, needs a regression run) and the registered-client OIDC
  path (verified by reading only; the `init-session` seam remains the fallback for automated tests).
- The app frontend needs a three-line monorepo change to link to the local inbox; nothing in the backend or app-lib v8.
