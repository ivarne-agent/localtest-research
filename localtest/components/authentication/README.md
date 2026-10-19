# authentication

Production Altinn Authentication (`vendor/`, see `UPSTREAM.md`) with Postgres replaced by memory and Key Vault by the
shared test certificate. Everything that stands in for a system outside Authentication lives in the localtest app
(`../../localtest/`): the fake ID-porten with the test-user picker, the fake Maskinporten, and the old localtest's
start page and `/Home` test helpers. Authentication reaches them by configuration and runs its own OIDC client, token
validation, Profile client, session handling and token exchanges unchanged.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `#if` sites (`git diff` against the baseline commit shows them); syncing is described in `vendor/UPSTREAM.md` |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): configuration and DI composition (`LocaltestAuthenticationExtensions.cs`, `LocaltestAuthenticationOptions.cs`), key providers (`Keys/`), the level table (`Login/LocaltestAuthLevels.cs`), in-memory repositories (`Persistence/`) |
| `Dockerfile` | Mirrors the upstream `vendor/src/apps/Altinn.Authentication/Dockerfile` (same pinned Alpine images, publish, non-root `dotnet` user); each difference is marked `localtest:`. Listens on 5101, the port the stack uses |

## Directive sites in `vendor/`

1. `AuthenticationHost.cs`: `AddLocaltestAuthentication(builder)` just before the persistence layer. Its settings
   are read eagerly there and its repositories use TryAdd, so the in-memory ones registered first win.
2. `AuthenticationController.cs`, `IsSafeSameOrSubdomainHttps`: the `goto` guard accepts plain http as well,
   because localtest serves `local.altinn.cloud` without TLS.

## What the fakes replace

| Production | Localtest |
| --- | --- |
| Postgres: sessions, upstream login transactions, unregistered client requests | `Persistence/`: in memory, so a restart signs everyone out |
| Postgres health check | Removed from the health checks; the service's own stays |
| Key Vault: keys for incoming platform access tokens | `Keys/LocaltestKeyProviders.cs`, `LocaltestPublicSigningKeyProvider`: the shared test certificate (`AccessTokenSettings:AccessTokenSigningCertificateFullPath`) for every issuer |
| `SigningKeysRetriever` (provider metadata and JWKS over https only, cached 12 h) | `LocaltestSigningKeysRetriever`: over plain http, no cache, so a restarted localtest app's new keys are picked up at once |

Everything else is configuration (`fakes/LocaltestAuthenticationExtensions.cs`):

- The `idporten` OIDC provider points at the localtest app's fake ID-porten (`localtest/api/idporten/`): issuer,
  authorize (public), token and metadata. The level table `OidcProviders:idporten:AuthLevels` comes from
  `Login/LocaltestAuthLevels.cs`; the localtest app's picker has the same table (`IdPorten/AuthLevels.cs`).
- `MaskinportenWellKnownConfigEndpoint` (and the alternative) point at the fake Maskinporten
  (`localtest/api/maskinporten/.well-known/oauth-authorization-server`). The exchange accepts a token when that URL
  contains its `iss`, so the public URL is used.
- `PlatformSettings:ApiProfileEndpoint` points at the register component, which answers Profile's `internal/user`
  (`Localtest:Authentication:ProfileEndpoint` overrides it).
- Everything derived from `GeneralSettings:PlatformEndpoint` is forced (the vendored `appsettings.json` carries at22
  values); `PlatformEndpoint` itself comes from the environment. `Localtest:Authentication:InternalPlatformEndpoint`
  points the service's own calls elsewhere when the public host name is not reachable from where it runs.
- No database, no migrations, no audit log, no Key Vault.

ServiceDefaults runs in environment `UNKNOWN`: the defaults are added after `AltinnHost` read its own settings, and
`LOCAL` is not wanted (it would fetch platform tokens from the remote test token generator).

## A sign-in

1. The app sends the browser to production `authentication?goTo=…`, which redirects to the fake ID-porten's
   `authorize` with state, nonce and an S256 PKCE challenge.
2. The picker (each test user's own party, level 0-4) answers with a one-time code to production `upstream/callback`.
3. Production `OidcProviderService` redeems the code at the fake's token endpoint with the verifier; production
   `UpstreamTokenValidator` checks the id and access tokens against the fake's JWKS (signature, issuer, lifetime,
   nonce).
4. Production `GetUserFromToken`, the Profile lookup, session creation, the cookie JWT and the cookies follow. Persons
   sign in by `pid`; a self-identified test user's token carries `urn:altinn:userid`, which takes production's
   Profile-by-userId branch.

The old localtest's `/Home/LogInTestUser` and `/Home/GetTestUserToken` run the same steps over HTTP from the localtest
app, which posts the picker for the chosen user and rewrites the party cookies to the selected party.

Authentication levels map to acr values from production's built-in ID-porten table (`OidcAuthLevelDefaults.IdPorten`);
production claim shaping turns the acr into `urn:altinn:authlevel`:

| Level | acr | `SecurityLevel` |
| --- | --- | --- |
| 0 | `level0` | `SelfIdentifed` |
| 1 | `level1` | `NotSensitive` |
| 2 | `level2` | `QuiteSensitive` |
| 3 | `idporten-loa-substantial` | `Sensitive` |
| 4 | `idporten-loa-high` | `VerySensitive` |

Cookies are `Secure`; the localtest app removes the flag from every `Set-Cookie` (forwarded or from `/Home`), so a
browser on plain http keeps them.

## Verified

The localtest app's `smoke-test.sh` (sections `identity providers` and `home`) and the root `smoke-test.sh` cover the
sign-in at every level, the party cookies, `refresh`, the org token through the fake Maskinporten and the production
exchange, and the Studio token.

## Run from an IDE

Authentication needs the rest of the stack (register for Profile, the localtest app for the identity providers). Run
the stack, point the localtest app's `authentication` upstream at the IDE instance, and start it:

```sh
# in localtest/: Proxy__Upstreams__authentication=http://host.docker.internal:5101 on the localtest service, then
podman-compose up -d --no-build
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:5101 \
  GeneralSettings__PlatformEndpoint=http://local.altinn.cloud:8000/ \
  AccessTokenSettings__AccessTokenSigningCertificateFullPath=$PWD/../../certs/localtest-accesstoken.pfx \
  dotnet run --no-launch-profile --project vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication
```
