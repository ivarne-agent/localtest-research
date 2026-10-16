# authentication

Production Altinn Authentication (`vendor/`, see `UPSTREAM.md`) with the upstream identity provider replaced by a
test-user picker, Profile replaced by the register seed, Postgres replaced by memory and Key Vault by a local key.

| Path | What |
| --- | --- |
| `vendor/` | Unmodified upstream copy except two `#if` sites (`git diff` against the baseline commit shows them) |
| `localtest.build.props` / `.targets` | MSBuild overrides selected by `DirectoryBuildPropsPath` / `DirectoryBuildTargetsPath`: define `LOCALTEST`, relax the documentation rules, compile `fakes/` into the host |
| `fakes/` | Compiled into the host assembly (its interfaces live there): picker, fake upstream provider and validator, Profile over the register seed, in-memory repositories, local signing key, token endpoints, DI composition |
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
(fake, from the register seed), session creation, cookie JWT and cookies run unchanged.

Token endpoints kept from the old localtest contract: `GET /Home/GetTestOrgToken?org&orgNumber&authenticationLevel&scopes`
(mints a Maskinporten-shaped token with a local key and runs it through the production `exchange/maskinporten`) and
`GET /Home/GetTestUserToken?userId&authenticationLevel` (runs the production sign-in headlessly, so the token has a real
session and `refresh` works). Both are anonymous.

Localtest defaults are set in `fakes/LocaltestAuthenticationExtensions.cs`: everything derived from
`GeneralSettings:PlatformEndpoint` is forced (the vendored `appsettings.json` carries at22 values), and
`PlatformEndpoint` itself is set by the environment. Known gap: cookies are `Secure`, so a browser on plain http
drops them unless the ingress rewrites the flag (`proxy_cookie_flags ~ nosecure` in nginx).

Run locally without Docker (register must be running on 5020):

```sh
export DirectoryBuildPropsPath=$PWD/localtest.build.props DirectoryBuildTargetsPath=$PWD/localtest.build.targets
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://0.0.0.0:8000 \
  GeneralSettings__PlatformEndpoint=http://local.altinn.cloud:8000/ \
  Localtest__Authentication__RegisterLocaltestEndpoint=http://127.0.0.1:5020/register/api/localtest/ \
  dotnet run --project vendor/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication
open 'http://local.altinn.cloud:8000/authentication/api/v1/authentication?goTo=http://local.altinn.cloud:8000/ttd/app/'
```
