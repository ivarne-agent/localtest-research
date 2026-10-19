# authentication

Production Altinn Authentication (`vendor/`, see `UPSTREAM.md`) with the upstream identity provider replaced by a
test-user picker, Profile replaced by the register seed, Postgres replaced by memory and Key Vault by a local key.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `#if` sites (`git diff` against the baseline commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): picker and start page, headless sign-in, fake upstream provider and validator, Profile over the register seed, in-memory repositories, local signing key, token endpoints, DI composition |
| `Dockerfile` | Publishes the vendored host with the overrides |

Directive sites in `vendor/`:

1. `AuthenticationHost.cs`: `AddLocaltestAuthentication(builder)` just before the persistence layer. Its settings
   are read eagerly there and its repositories use TryAdd, so the in-memory ones registered first win.
2. `AuthenticationController.cs`, `IsSafeSameOrSubdomainHttps`: the `goto` guard accepts plain http as well,
   because localtest serves `local.altinn.cloud` without TLS.

How a login works: production `authentication?goTo=` redirects to the configured upstream authorization endpoint,
which is the picker page in this host (`authentication/api/v1/localtest/login`). Submitting it issues a one-time code
and sends the browser to the production `upstream/callback`; the fake `IOidcProvider` turns the code into an id_token
for the picked user, the fake `IUpstreamTokenValidator` reads it, and production `GetUserFromToken`, Profile lookup
(fake, from the register seed), session creation, cookie JWT and cookies run unchanged. The picker offers each user's
own party only, because the production callback sets the party cookies; the app's party selection does the rest.

The old localtest's login contract (what the app-frontend Cypress suite, app-lib and developers use), in
`fakes/Controllers/LocaltestHomeController.cs` and `LocaltestTokenController.cs`, all anonymous:

| Endpoint | Behaviour |
| --- | --- |
| `GET /`, `/Home`, `/Home/Index` | Start page: `<select id="UserSelect">` with `userId.partyId` options (`Name`, and `Name for Org` for each party in the user's register `parties`), `AuthenticationLevel` 0-4 (default 2), optional `AppPathSelection`; `?goto=` becomes `RedirectUrl` |
| `POST /Home/LogInTestUser` | Form or JSON: `UserSelect`, `AuthenticationLevel` (-1 skips the login), `action`, `RedirectUrl`, `AppPathSelection`. Runs the production sign-in headlessly, appends its cookies with `AltinnPartyId`/`AltinnPartyUuid` replaced by the selected party (the JWT keeps the user's own party, as in production). `action=reauthenticate` answers 204; otherwise redirects to `RedirectUrl` (same origin only), else `/{AppPathSelection}/`, else `/` |
| `GET /Home/Localtest/Version` | `8` (`application/json`), the version `LocaltestValidation` in app-lib probes |
| `GET /Home/GetTestUserToken[/{userId}]?userId&partyId&authenticationLevel` | Same headless sign-in; returns the JWT and sets the cookies (party cookies on `partyId` when given). Default level 2 |
| `GET /Home/GetTestOrgToken[/{org}]?org&orgNumber&authenticationLevel&scopes` | Mints a Maskinporten-shaped token with a local key and runs it through the production `exchange/maskinporten` (level 3 from the exchange; default service-owner scopes) |
| `GET /Home/GetTestStudioToken` | Designer's credential: a platform access token with issuer `studio` and app `studio.designer`, signed with the shared test certificate, exchanged through the production `exchange/altinnstudio`. Carries `urn:altinn:app=studio.designer`, which Storage's and Authorization's `StudioDesignerAccess` policy requires for pushing metadata, texts and policies |

Authentication levels map to acr values from production's built-in ID-porten table (`OidcAuthLevelDefaults.IdPorten`),
set as the `OidcProviders:idporten:AuthLevels` default from `fakes/Login/LocaltestAuthLevels.cs`; production claim
shaping turns the acr into `urn:altinn:authlevel`:

| Level | acr | `SecurityLevel` |
| --- | --- | --- |
| 0 | `level0` | `SelfIdentifed` |
| 1 | `level1` | `NotSensitive` |
| 2 | `level2` | `QuiteSensitive` |
| 3 | `idporten-loa-substantial` | `Sensitive` |
| 4 | `idporten-loa-high` | `VerySensitive` |

Persons sign in by pid (Profile lookup by ssn). A test user with an empty `ssn` is self-identified: the fake id_token
carries `urn:altinn:userid`, which takes production's Profile-by-userId branch. Test users are read from the register
component's `test-users`; unknown fields are ignored.

The proxy routes `/` (exact), `/Home` (exact) and `/Home/` to this service. `smoke-test.sh` checks the contract
(`BASE` = the proxy or the service itself).

Localtest defaults are set in `fakes/LocaltestAuthenticationExtensions.cs`: everything derived from
`GeneralSettings:PlatformEndpoint` is forced (the vendored `appsettings.json` carries at22 values), and
`PlatformEndpoint` itself is set by the environment, and `Localtest:Authentication:InternalPlatformEndpoint` can point
the service's own calls elsewhere when the public host name is not reachable from where it runs. Cookies are `Secure`;
the proxy removes the flag from every `Set-Cookie`, so a browser on plain http keeps them.

Run locally without Docker (register must be running on 5020):

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:8000 \
  GeneralSettings__PlatformEndpoint=http://local.altinn.cloud:8000/ \
  Localtest__Authentication__RegisterLocaltestEndpoint=http://127.0.0.1:5020/register/api/localtest/ \
  dotnet run --no-launch-profile --project vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication
open 'http://local.altinn.cloud:8000/authentication/api/v1/authentication?goTo=http://local.altinn.cloud:8000/ttd/app/'
BASE=http://local.altinn.cloud:8000 ./smoke-test.sh
```
