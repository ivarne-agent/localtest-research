# The apps' Maskinporten clients on localtest-v2 (task 43, 2026-10-23)

The question: do the monorepo app backend's (`src/App/backend`, `altinn-studio` `819ad7362e`) and app-lib 8's
(`app-lib-dotnet` `c3eaf6b3`; the sample app runs the released 8.12.15, and the files below are unchanged between
`v8.12.15` and that commit) `MaskinportenClient` and `AuthenticationTokenResolver` need any adaptation for
localtest-v2, at the default origin and at a custom one? The stack: studioctl's dev build from main (`/home/agent/sh`),
pin `sha-6fd85ef`, default group.

## Recommendation

1. **Nothing changes now.** At the default origin both libraries' service-owner tokens come from the front door's
   `/Home/GetTestOrgToken`, which follows the stack and works (proof below); no library, runner or fake change is
   needed for that.
2. **Known limit, recorded, not fixed now:** at a custom host (`--host`, task 39) the apps leave the short-cut, because
   the localtest detection is a fixed host-name list. The monorepo app then **does not start** unless a Maskinporten
   client is stored; app-lib 8 starts, and every service-owner request fails. A user-token form works in both. The
   switch below removes the limit; nothing smaller is worth doing before it.
3. **When the new localtest becomes the default** (owner's decision, 2026-10-23): the apps use the proper fakes, the
   real `MaskinportenClient` against the front door's Maskinporten fake and production Authentication's exchange. The
   proof shows that path already works end to end today, with one gap: the exchange makes the token `digdir`, never
   `ttd`. The switch needs four changes, listed in [The switch to the proper fakes](#the-switch-to-the-proper-fakes).

## How the libraries decide

`RuntimeEnvironment.IsLocaltestPlatform()` (both libraries, identical): true when `GeneralSettings:HostName`, port
stripped, is `local.altinn.cloud` or `altinn3local.no`. The runners set `HostName` to the public host
(`studioctl app env`: `local.altinn.cloud` at the default origin, `local.altinn.test` with `--host local.altinn.test`;
Aspire the same from `PublicHost`). `GetPlatformBaseUrl()` is the scheme, host and port of
`PlatformSettings:ApiStorageEndpoint`, so the short-cut's URL follows any origin; the *decision* does not.

`AuthenticationTokenResolver.GetAccessToken` (both): `AltinnToken` (service owner, `AuthenticationMethod.ServiceOwner()`
and every `StorageAuthenticationMethod.ServiceOwner()`) goes to `GET {platform}/Home/GetTestOrgToken?org=…&orgNumber=991825827&authenticationLevel=3&scopes=…`
when `IsLocaltestPlatform()`, else to `IMaskinportenClient.GetAltinnExchangedToken`. `MaskinportenToken` always goes to
`IMaskinportenClient.GetAccessToken`. The monorepo additionally rewrites `UserToken` to `ServiceOwner` inside a
workflow-engine callback, so the callbacks are service-owner requests.

Code that calls `IMaskinportenClient` directly, never through the resolver, always takes the real client:
`MaskinportenDelegatingHandler` (`UseMaskinportenAuthorisation`, `UseMaskinportenAltinnAuthorisation` on an app's own
`HttpClient`), `FiksIOMaskinportenClient`, and in app-lib 8 also `CorrespondenceAuthorisationFactory` (Correspondence
when the payload has no `AuthenticationMethod`), `LegacyMaskinportenTokenProvider` and eFormidling's status check.

The real client: `Authority` and `ClientId` from `MaskinportenSettings` (required), `jwk` or `jwkBase64`; audience =
the `issuer` of `{Authority}.well-known/oauth-authorization-server` (resolved with `new Uri(new Uri(Authority), …)`, so
an authority without a trailing slash resolves one level up, the fetch fails and the client falls back to
`Authority + "/"`, which the fake does not check); grant posted to `Authority.Trim('/') + "/token"`; exchange
`GET {PlatformSettings:ApiAuthenticationEndpoint}exchange/maskinporten` with the Maskinporten token, **without**
`test=true`.

Where the client's settings come from:

| | Monorepo backend | app-lib 8 |
| --- | --- | --- |
| File | `$RUNTIME_APP_SECRETS_DIR/$RUNTIME_APP_SECRETS_MASKINPORTEN_FILENAME` (the runners set both; studioctl's directory `~/.altinn-studio/apps/<org>/<app>/secrets`) | `{AppSettings:RuntimeSecretsDirectory}/maskinporten-settings.json`, default `/mnt/app-secrets`; **the runners' variables are not read** |
| Who writes it | the developer, `studioctl app maskinporten set` (refuses a non-https authority) | nobody locally |
| Missing at start | `MaskinportenSettingsStartupCheck`: on localtest an Information line; elsewhere the options validation **stops the host** | nothing at start; the first use throws `OptionsValidationException` |
| Well-known refresh | `MaskinportenWellKnownRefreshService` skipped on localtest | on first use, cached one hour |

## Decision tables

What runs for each token kind. "Short-cut" is `/Home/GetTestOrgToken`; "real client" is `MaskinportenClient`.

**Monorepo backend** (`src/App/backend`)

| Token | Default origin (`local.altinn.cloud`) | Custom host (`local.altinn.test`) |
| --- | --- | --- |
| Service owner through the resolver (`AltinnToken`: Storage, Register, Profile, … as the org; workflow-engine callbacks) | short-cut → `ttd`, Storage 200 | real client. No client stored: **app does not start**. A client against the fake: token `digdir`, Storage as `ttd` 403, instantiation 500 |
| `GetAltinnExchangedToken` direct (delegating handler) | real client: without a client `MaskinportenConfigurationException`; with a client against the fake `digdir` | same |
| `GetAccessToken` (Maskinporten token, through the resolver or direct) | real client, the stored client's authority (test Maskinporten for a real client) | same |

**app-lib 8** (8.12.15)

| Token | Default origin | Custom host |
| --- | --- | --- |
| Service owner through the resolver | short-cut → `ttd`, Storage 200 | real client, reads `/mnt/app-secrets`: `OptionsValidationException`; with `AppSettings:RuntimeSecretsDirectory` and a client against the fake: `digdir`, Storage as `ttd` 403 |
| `GetAltinnExchangedToken` direct (delegating handler, Correspondence's default authorisation, eFormidling) | real client; no client is ever read from studioctl's directory, so it fails | same |
| `GetAccessToken` | real client, same | same |

## Answers

**1. Where the tokens come from.** Above. At the default origin the short-cut runs for every service-owner request,
and the front door mints the fake Maskinporten token and exchanges it with `test=true`, so the app acts as `ttd`. At a
custom host the real client runs. Whenever the real client runs against the fake (written by hand, below):

- `Authority = <origin>/localtest/api/maskinporten/` works: the discovery document answers with the issuer on the
  public origin, `/token` accepts the client's signed `jwt-bearer` grant (it ignores the signature, `aud` and `exp`),
  and the start-up check accepts the settings (it checks presence only).
- The exchange reaches production Authentication's `exchange/maskinporten` on the public origin, which validates the
  fake's issuer and signature (its well-known URLs point at the fake on the public origin, at a custom origin too) and
  answers 200.
- The result is `urn:altinn:org=digdir`: the client id is not nine digits, so the fake uses Digdir's 991825827, and
  without `test=true` the exchange keeps `digdir`. `ttd` has no organisation number in `altinn-orgs.json`, so no
  client id or claim the fake can issue gives `ttd` through the production exchange. A Storage query as `digdir`
  answers 200; as `ttd` 403.
- "The `jwk` the runners write": the runners write none. studioctl writes only `app-codes.json`; the Maskinporten file
  is the developer's (`studioctl app maskinporten set`), and that command refuses an `http` authority, so a client
  against the fake can only be written by hand today. Aspire writes none either.

**2. What needs to change.** Now: nothing (recommendation 1). For the switch: below.

**Task 15 (`Altinn.ApiClients.Maskinporten`).** The monorepo backend does not reference the package (no reference in
`src/App/backend`). **app-lib 8 does**: `Altinn.App.Core.csproj` references it (version 8.0.1) for the legacy
`IMaskinportenTokenProvider` / `MaskinportenJwkTokenProvider` (`AddMaskinportenJwkTokenProvider`) and the first
eFormidling status-check handler (`EformidlingStatusCheckEventHandler`, which takes `IMaskinportenService`). So the
brief's premise holds for the monorepo and not fully for app-lib 8. Task 15's change (configurable token and exchange
endpoints in the package) would let those legacy app-lib 8 paths point at the fake by settings; it does not touch
`MaskinportenClient` or the resolver, which do not use the package, and nothing in this document depends on it.

## The switch to the proper fakes

What it takes to drop the short-cut and run the real client against the fakes when the new localtest is the default.

| # | Change | Where | Size | Needed |
| --- | --- | --- | --- | --- |
| 1 | The runners write a local client into the secrets directory on every run against localtest-v2, unless the developer stored one: `{"MaskinportenSettings": {"authority": "<origin>/localtest/api/maskinporten/", "clientId": "<org>-localtest", "jwk": <a generated RSA key, all twelve fields>}}`. The trailing slash matters (well-known resolution). It is not the developer's file: it must not go through `maskinporten set`'s https check, and `maskinporten show` must say it is the local one | studioctl `internal/appsecrets` and `internal/cmd/app` (or `run`), Aspire `AppSecrets.cs`, the shared golden | ~60 lines Go, ~40 C#, golden | yes |
| 2 | The exchange must make the app's org. The fake Maskinporten accepts everything the client sends already (grant, scopes, any signature); what is missing is `ttd`. Smallest: Authentication's localtest fakes register an `IOrganisationsService` decorator that answers `ttd` for 991825827, the `test=true` rule applied always (every token the stack mints for 991825827 is already exchanged with `test=true` by `/Home/GetTestOrgToken`). An app of another org needs the client id to be that org's nine-digit number (the fake's rule 2), which the runner would look up | `components/authentication/fakes/` (new image pin) | ~30 lines | yes, for `ttd` apps |
| 3 | The resolver's localtest branch goes: `AltinnToken` always takes the real client, so the resolver needs no localtest detection at all. For the transition (old localtest still in use, it has no exchange for a client), keep the branch only when no client is configured: `AltinnToken when _isDev && !clientConfigured`. With (1) the client is always configured on localtest-v2, at any origin | monorepo `AuthenticationTokenResolver` (+ tests) | ~10 lines, later ~40 removed | yes |
| 4 | app-lib 8 reads the client from `AppSettings:RuntimeSecretsDirectory`: the runners also set `AppSettings__RuntimeSecretsDirectory` to the secrets directory for a process run. The resolver change (3) is the same file in app-lib 8 and trivial, but needs a release; without it app-lib 8 apps keep the short-cut at the default origin, which still works | runners (one line + golden); app-lib 8 optional | 1 line; app-lib 8 ~10 | runner yes, app-lib 8 nice to have |

With (1) the remaining `IsLocaltestPlatform()` uses do not matter for Maskinporten: the start-up check finds a client
everywhere, the well-known refresh skip is harmless. They still matter elsewhere (`LocaltestValidation`'s version
probe, the old-localtest token handling in `AuthenticationContext`), which is task 22's question, not this one.

Open point for the switch, not settled here: a developer who stores a real client (test Maskinporten, for an external
API) replaces the local one, and then service-owner requests exchange a real Maskinporten token at the local
Authentication, whose well-known URLs point only at the fake: 401. Either the runner keeps its local client for the
exchange in a second file (app-lib 8 already has a `maskinporten-settings-internal.json` variant, unused by its own
code; the monorepo has none), or the Authentication fakes set `MaskinportenWellKnownAlternativeConfigEndpoint` to test
Maskinporten, so that a real token is accepted too (it would then come out as the client's real organisation).
Untested.

## Proof on the stack

`STUDIOCTL_HOME=/home/agent/sh`, `studioctl env up -r localtest-v2`. Apps: the monorepo app
`src/test/apps/expression-validation-test` from a detached worktree of `altinn-studio` at `819ad7362e` (project
references to `src/App/backend`), and a scratch copy of `localtest/aspire/samples/app/App` (`ttd/testapp`,
Altinn.App 8.12.15), both started with `studioctl run -d`. Each got a scratch probe, `GET /{org}/{app}/probe/token`
(script kept in `/home/agent/code/.scripts/task43/TokenProbe.cs`, not committed): `path=resolver` calls the library's
internal `IAuthenticationTokenResolver` (by reflection) with `AuthenticationMethod.ServiceOwner()`, `path=exchanged`
and `path=maskinporten` call `IMaskinportenClient` directly; each answers the token's claims and the status of
`GET {ApiStorageEndpoint}instances?org=ttd&appId=<app>&size=1` (or `?org=<org>`) made with the token. The "fake client"
is a `maskinporten-settings.json` with `authority` `<origin>/localtest/api/maskinporten/`, `clientId` `task43-client`
and a fresh 2048-bit RSA JWK, written by hand (`umask 077`) into the app's secrets directory because
`studioctl app maskinporten set --file` refuses it: `invalid Maskinporten client: authority
"http://local.altinn.cloud:8000/localtest/api/maskinporten/" is not an https URL`. The machine restarted mid-proof;
everything below is from the second run.

### Default origin (`http://local.altinn.cloud:8000`)

| App | Probe | Client stored | `iss` | `urn:altinn:org` | `client_id` | Storage |
| --- | --- | --- | --- | --- | --- | --- |
| monorepo | resolver | none | `…:8000/authentication/api/v1/openid/` | `ttd` | `localtest` | 200 (`org=ttd&appId=…`) |
| monorepo | exchanged, maskinporten | none | `MaskinportenConfigurationException`: "The app's Maskinporten credentials are missing or invalid … No Maskinporten client is stored for this local run. Store one with 'studioctl app maskinporten set'" | | | |
| monorepo | maskinporten | fake | `…:8000/localtest/api/maskinporten/` | (none; `consumer` `0192:991825827`) | `task43-client` | 401 (not an Altinn token) |
| monorepo | exchanged | fake | `…:8000/authentication/api/v1/openid/` | `digdir` | `task43-client` | 403 as `ttd`, **200 as `digdir`** |
| monorepo | resolver | fake | `…/openid/` | `ttd` | `localtest` | 200 (the short-cut still wins) |
| app-lib 8 | resolver | none | `…/openid/` | `ttd` | `localtest` | 200 |
| app-lib 8 | exchanged | none, then fake in studioctl's directory | `OptionsValidationException` both times (the runner's `RUNTIME_APP_SECRETS_DIR=/home/agent/sh/apps/ttd/testapp/secrets` is not read) | | | |
| app-lib 8 | maskinporten | fake, `-- --AppSettings:RuntimeSecretsDirectory=<dir>` | `…/localtest/api/maskinporten/` | | `task43-client` | 401 |
| app-lib 8 | exchanged | same | `…/openid/` | `digdir` | `task43-client` | 403 as `ttd`, **200 as `digdir`** |
| app-lib 8 | resolver | same | `…/openid/` | `ttd` | `localtest` | 200 |

The short-cut's token (monorepo): `scope` `altinn:serviceowner altinn:serviceowner/instances.read
altinn:serviceowner/instances.write` (app-lib 8 without the bare `altinn:serviceowner`), `consumer`
`0192:991825827`, `urn:altinn:orgNumber` `991825827`, `urn:altinn:authenticatemethod` `maskinporten`,
`urn:altinn:authlevel` 3, 30 minutes. The real client's exchanged token has the same shape with `client_id`
`task43-client` and `digdir`.

**Workflow-engine callbacks (monorepo app).** `POST /ttd/expression-validation-test/instances?instanceOwnerPartyId=501337`
with user 1337's token (`/Home/GetTestUserToken/1337`): 201, instance on `Task_1`. The app log for that request: eight
`workflow-engine-callbacks/*` requests (`AcquireProcessingStatus`, `UnlockTaskData`, `CleanupGeneratedFromTask`,
`OnTaskStartingHook`, `CommonTaskInitialization`, `StartTask`, `CommitProcessState`, `ReleaseProcessingStatus`), all
200; four `GET http://local.altinn.cloud:8000/Home/GetTestOrgToken?*` → 200, each followed by `POST
…/storage/api/v1/instances/501337/<id>/mutations` → 200.

### Custom origin (`env up --host local.altinn.test --port 18000`, hosts line added and removed with `sudo`)

The front door follows the origin: the fake's discovery answers `issuer`
`http://local.altinn.test:18000/localtest/api/maskinporten/`, and `/Home/GetTestOrgToken` answers a token with `iss`
`http://local.altinn.test:18000/authentication/api/v1/openid/`, `urn:altinn:org` `ttd`. `studioctl app env`:
`GeneralSettings__HostName=local.altinn.test`, the platform endpoints on `http://local.altinn.test:18000/`. The apps'
resolvers do **not** follow it:

| App | Client stored | Result |
| --- | --- | --- |
| monorepo | none | `studioctl run`: "app exited before becoming reachable through localtest: signal: aborted"; log: "Hosting failed to start … OptionsValidationException … No Maskinporten credentials were read from '/home/agent/sh/apps/ttd/expression-validation-test/secrets/maskinporten-settings.json' …" from `MaskinportenSettingsStartupCheck.StartAsync` |
| monorepo | fake (`http://local.altinn.test:18000/localtest/api/maskinporten/`) | starts. resolver → real client: `iss` `…:18000/authentication/api/v1/openid/`, `digdir`, `client_id` `task43-client`; Storage as `ttd` 403, as `digdir` 200. Instantiation: 500 "Instance initialization failed … the workflow failed before instance initialization completed"; the callbacks' `…/mutations` → 403, `AcquireProcessingStatus` → 500 (seven attempts) |
| app-lib 8 | none | starts; resolver → `OptionsValidationException` (Authority, ClientId required) |
| app-lib 8 | fake, `AppSettings:RuntimeSecretsDirectory` | resolver → real client: `digdir`; Storage as `ttd` 403, as `digdir` 200 |

So the real path (fake token → production exchange → Storage as the org) works today at both origins; only the org is
wrong for a `ttd` app, which is change 2 of the switch.

### Clean-up

`studioctl stop` for each app, `env down`, the hosts line removed (`/etc/hosts` identical to the copy taken before),
the stored test clients deleted (each secrets directory holds `app-codes.json` only), `podman ps -a` empty, the four
`localtest-v2-*` volumes present, the MSBuild and Roslyn servers of these builds stopped by pid, the `altinn-studio`
worktree removed and the scratch sample deleted. `/home/agent/sh/bin/studioctl` untouched.
