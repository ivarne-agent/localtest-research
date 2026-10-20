# Localtest app review (2026-10-20)

> **Status (2026-10-20):** handled. Fixed: 1 (`ProblemDetails` in the JSON context), 2 (400 on a non-form post),
> 3 (the fake sends `iss`; Authentication's `ValidateCallbackIssuer` and `StrictIdTokenValidation` are on), 4 (control
> characters and backslashes refused in `RedirectUrl`), 5 (`redirect_uri` must be on the platform origin), 6 (default log
> level Warning), 7 (client aborts logged at Debug), 8 (app routes cannot shadow platform paths), 9 (an unreachable
> register or Authentication gives a 502 with the reason), 10 (one default level, 2, for the picker and `/Home`; an
> unknown level is a 400; the dead members of Authentication's table removed), 11 (no `token_endpoint` in the Maskinporten
> metadata; unknown `localtest/api/…` paths get the localtest app's own 404), 12 for the start page (`GET` and `HEAD`),
> 13 (pinned digests, the `app` user, `ASPNETCORE_HTTP_PORTS`, the stale comment), 14 (the issue time lives in the code
> store), 15 (the README's paths and a verification record), 16 (smoke checks for the picker, the token endpoint and the
> id_token; a note on the internet-dependent org check). Open: `HEAD` on the other endpoints (12), and the WebSocket
> pass-through is still untested (16). Verified: the localtest app smoke test 65/65 and the other smoke tests.

Scope: `localtest/localtest/` in ivarne-agent/localtest-research. Read-only review. Nothing was built, no repo file
was edited, and no container was touched. Paths below are relative to `localtest/localtest/` unless stated.

## Summary

The app is small, readable and mostly faithful to the protocols. The JWT writer, the JWKS, PKCE S256, single-use
60-second codes, nonce, `aud`/`iss`, and the claim shapes all match what production Authentication's
`UpstreamTokenValidator`, `GetUserFromToken`, `exchange/maskinporten` and `exchange/altinnstudio` read. Cookie rewriting
is correct for what production Authentication actually sends (SameSite=Lax, no `__Host-`/`__Secure-` names, one
`Set-Cookie` per header). Shared state is thread-safe.

Main problems:
1. Every `Results.Problem(...)` (the 502s that explain a failed sign-in or exchange, and the 500 for a missing
   certificate) almost certainly fails under Native AOT, because `ProblemDetails` has no JSON type info. The caller
   then gets a bare 500 and loses the diagnostic text.
2. The form endpoints (`POST token`, `POST authorize`) answer 500 with a stack trace in the log when the body is not a
   form. I confirmed this at runtime.
3. Two production checks never run locally, because neither the fake nor Authentication's localtest config turns them
   on: the RFC 9207 callback `iss` check and `StrictIdTokenValidation`.
4. Some smaller issues: the open-redirect guard can be bypassed by browser URL normalisation, logging is at
   Information level (about 130k log lines in about 10 minutes), the container runs as root on unpinned base tags, and
   some docs are stale.

## Features and what they stand in for

| Feature | Code | Stands in for | Production code it exercises |
| --- | --- | --- | --- |
| Proxy | `Proxy/` (`Forwarder.cs`, `PlatformRoutes.cs`, `AppRoutes.cs`, `AppRoutesApi.cs`) | The nginx ingress, which is itself a stand-in for the Altinn platform ingress, and app routing | none (infrastructure) |
| ID-porten | `IdPorten/IdPortenApi.cs`, `AuthorizationCodes.cs`, `TestUsers.cs`, `AuthLevels.cs` | ID-porten's OIDC authorization-code flow, with the test-user picker as its login page | Authentication's `OidcProviderService.GetTokens` (token POST with PKCE), `UpstreamTokenValidator` (signature against JWKS, issuer, lifetime, nonce), `GetUserFromToken`, session and cookie creation |
| Maskinporten | `Maskinporten/MaskinportenApi.cs` | Maskinporten's metadata and JWKS, and token minting (in-process, with no token endpoint) | `exchange/maskinporten` (signature, issuer containment, consumer, service-owner scope, `test=true` gives ttd) |
| Home | `Home/HomeApi.cs`, `Home/AuthenticationClient.cs` | The old localtest `/Home` contract (start page, `LogInTestUser`, `GetTest*Token`, `Localtest/Version`) | The full browser sign-in run headlessly over HTTP; `exchange/maskinporten`; `exchange/altinnstudio` |
| Tokens | `Tokens/SigningKey.cs` | IdentityModel's JWT writer and signing credentials | n/a; its output is validated by production IdentityModel |

## Findings

1. **`Results.Problem` cannot serialize `ProblemDetails` under Native AOT.** Severity: medium.
   - Where: `Home/HomeApi.cs:191`, `:204`, `:219`, `:239`. The resolver chain is set at `Program.cs:41`, and
     `LocaltestJsonContext.cs:12-22` has no `ProblemDetails`.
   - Why it matters: `ProblemHttpResult.ExecuteAsync` first asks for an `IProblemDetailsService`. None is registered,
     because `AddProblemDetails()` is never called. It then calls
     `HttpResultsHelper.WriteResultAsJsonAsync<ProblemDetails>`, which calls `JsonSerializerOptions.GetTypeInfo(typeof(ProblemDetails))`. With reflection off, `JsonOptions` has an empty
     resolver plus `LocaltestJsonContext`, so that call throws `NotSupportedException`. I checked this by decompiling
     ASP.NET Core 10.0.12 `Microsoft.AspNetCore.Http.Results.dll` and `Http.Extensions.dll`.
   - Result: exactly the paths meant to explain a failure ("Production sign-in answered … without the AltinnStudioRuntime
     cookie", "exchange/maskinporten answered 401", "certificate not at …") become a bare 500 with an empty body.
     This is not a build warning, so `TreatWarningsAsErrors` does not catch it.
   - Fix: call `builder.Services.AddProblemDetails()` (it registers `ProblemDetailsJsonContext`), or answer these
     errors with `Results.Text(error, statusCode: 502)`.
   - Not verified at runtime: I could not trigger a failing sign-in or exchange without side effects.

2. **A non-form POST to `token` or `authorize` throws and answers 500.** Severity: low.
   - Where: `IdPorten/IdPortenApi.cs:65` and `:106` call `request.ReadFormAsync` unconditionally.
   - Verified: a POST to `/localtest/api/idporten/token` with no body, `Content-Type: application/json`, or
     `text/plain` answers `500` with an empty body. The log shows `fail: … System.InvalidOperationException: Incorrect Content-Type`.
   - Why it matters: real ID-porten answers `400 invalid_request`. A 500 plus a stack trace makes a misconfigured
     client look like a crash.
   - Fix: check `request.HasFormContentType` first and return `TokenError("invalid_request", …)` for `token`, or a 400
     for `authorize`.

3. **Production's RFC 9207 callback-issuer check and strict id_token validation never run.** Severity: medium, given
   the project's "as much production code as possible" goal.
   - Where: `IdPorten/IdPortenApi.cs:101` redirects with only `code` and `state`, and discovery (`:27-37`) does not
     advertise `authorization_response_iss_parameter_supported`. Authentication's localtest config
     (`components/authentication/fakes/LocaltestAuthenticationExtensions.cs:59-74`) sets neither
     `OidcProviders:idporten:ValidateCallbackIssuer` nor `StrictIdTokenValidation`. Both default to false
     (vendor `Model/OidcProvider.cs`).
   - Why it matters: `OidcServerService.ValidateCallbackIssuer` and the strict `aud`/exact-issuer branch of
     `UpstreamTokenValidator.ValidateToken` (lines 129-155) are skipped locally. The fake already emits an exact issuer
     and `aud` = client id, so turning these on should cost nothing.
   - Fix:
     - Add `iss` to the authorize redirect, both in `IdPortenApi` and in the in-process code path at
       `Home/AuthenticationClient.cs:52`.
     - Advertise `authorization_response_iss_parameter_supported: true`.
     - Enable both flags in the authentication fake's configuration, or mirror whatever production's ID-porten provider
       config uses.
   - Not verified: production's actual values for these two flags (they are not in the vendored appsettings).

4. **The open-redirect guard can be bypassed by browser URL normalisation.** Severity: low (local-only, needs a
   cross-site form POST to the developer's machine).
   - Where: `Home/HomeApi.cs:305-314` (`IsOwnOrigin`) and `:163` (`AppPathSelection`).
   - Why it matters:
     - `RedirectUrl=/<TAB>/evil.example` passes the leading-`/` test. Kestrel allows a tab in a header value, and
       browsers strip tabs and newlines from URLs, so the Location becomes `//evil.example`.
     - `AppPathSelection=\evil.example` is not validated at all and is redirected to `/\evil.example/`, which browsers
       treat as `//evil.example/`.
   - Fix: resolve the value against `PlatformEndpoint` (`new Uri(new Uri(PlatformEndpoint), url)`) and compare the
     scheme, host and port of the result; reject any control character or `\`. Validate `AppPathSelection` against
     `AppRoutes.Segment()` for org and app.
   - Not verified at runtime: POSTs to `/Home` were out of bounds.

5. **The fake ID-porten accepts any absolute `redirect_uri`.** Severity: low.
   - Where: `IdPorten/IdPortenApi.cs:68`, `:101`.
   - Why it matters: real ID-porten only redirects to registered URIs. Here a POST to `authorize` redirects anywhere
     with a valid code. `javascript:` also passes `Uri.TryCreate`, although browsers will not follow it from a
     Location header.
   - Fix: allow only `http(s)` URIs on the platform origin, or a configured list
     (`{platform}authentication/api/v1/upstream/callback`).

6. **Logging at Information for every request and every HttpClient call.** Severity: medium for operations (the
   machine runs Cypress), low otherwise.
   - Where: `Program.cs:17`. There is no `appsettings.json` and no `Logging__*` variable in the `Dockerfile`.
   - Why it matters: `podman logs localtest_localtest_1` had 132,188 lines about 10 minutes after start, during a
     Cypress run. Real problems (finding 2's stack traces) drown in the noise, and the log file grows without bound.
   - Fix: set `Logging__LogLevel__Default=Warning` and `Logging__LogLevel__Microsoft.Hosting.Lifetime=Information` in
     the Dockerfile, or a minimal `appsettings.json`.

7. **Client aborts are logged as warnings with full stack traces.** Severity: low.
   - Where: `Proxy/Forwarder.cs:75-78`.
   - Why it matters: `RequestCanceled` and `ResponseBodyCanceled` are normal for SSE (`events/api/localtest/stream`)
     and for Cypress navigations. Each produces a multi-line warning (6 seen).
   - Fix: log those two `ForwarderError` values at Debug.

8. **App routes can shadow platform routes.** Severity: low.
   - Where: `Proxy/AppRoutesApi.cs:18` accepts any `^[a-z0-9][a-z0-9-]*$` org. `Proxy/Forwarder.cs:39-58` resolves
     app routes before platform routes.
   - Why it matters: `PUT apps/authentication/api` (or org `storage`, `register`, `events`, …) silently hijacks a
     platform prefix, including the sign-in. Org `localtest` would catch unmapped `localtest/api/...` paths. This
     matches the documented order (`README.md` routes 4 before 5) but is a foot-gun.
   - Fix: reject orgs equal to a platform prefix name or `localtest`, `app-frontend`, `home` (400).

9. **An unreachable register gives a 500 on sign-in.** Severity: low.
   - Where: `IdPorten/IdPortenApi.cs:83` and `Home/HomeApi.cs:225` call `TestUsers.GetAll` without the try/catch that
     the GET pages use (`IdPortenApi.cs:194-205`, `HomeApi.cs:61-69`).
   - Why it matters: an `HttpRequestException` (register down, or still starting after `podman-compose up`) becomes an
     unhandled 500 instead of the helpful notice the pages show.
   - Fix: catch it and answer 502 with the same message.

10. **The default level differs between the picker and Home, and an invalid picker level passes silently.**
    Severity: low.
    - Where: `IdPorten/AuthLevels.cs:23` (`Default = 3`) versus `Home/HomeApi.cs:26` (`DefaultLevel = 2`).
      Authentication's `fakes/Login/LocaltestAuthLevels.cs:30` (`Default = 2`) plus `ForLevel`, `ForAcr`, `Parse` and
      `Label` are now unused there (no references outside the file). `IdPortenApi.cs:89-95` silently falls back to 3
      for an unknown `AuthenticationLevel`; Home answers 400 for the same input (`HomeApi.cs:142-145`).
    - Why it matters: Authentication sends no `acr_values` when no level is requested (`AuthLevels` configured, so
      `GetDefaultUpstreamAcrValues` returns null). The browser picker then defaults to 3, while `/Home` defaults to 2.
    - Fix: pick one default and document it; return 400 for an unknown level in `POST authorize`; delete the dead
      members in Authentication's `LocaltestAuthLevels`.

11. **The Maskinporten metadata advertises a token endpoint that does not exist.** Severity: low.
    - Where: `Maskinporten/MaskinportenApi.cs:24`.
    - Verified: `GET /localtest/api/maskinporten/token` answers the proxy's 404 ("No app is registered for this
      path…").
    - Fix: drop `token_endpoint`, or let the unmapped `localtest/api/...` paths answer their own 404 instead of the
      app-runner hint.

12. **`HEAD` on the localtest app's own endpoints falls through to the proxy's 404.** Severity: low.
    - Where: `Home/HomeApi.cs:33-35` (MapGet only) and `Program.cs:88`.
    - Verified: `curl -I http://local.altinn.cloud:8000/` answers `404 text/plain`.
    - Why it matters: link checkers and some health probes use HEAD. The old MVC localtest had the same limitation, so
      this is not a regression.
    - Fix: `MapMethods(..., ["GET","HEAD"], ...)` for the start page and `/health`.

13. **Dockerfile.** Severity: low.
    - Base images use floating tags, not digests (`Dockerfile:3`, `:15`).
    - The container runs as root. `podman exec … id` printed `uid=0(root)`, and `podman inspect` shows an empty
      `Config.User`. Port 8000 needs no root, and runtime-deps ships the `app` user (UID 1654). Fix: `USER app`, and
      `chown app /data` in the image. Note that the existing `localtest-data` volume holds a root-owned
      `/data/app-routes.json`, so it needs a one-off chown.
    - `ASPNETCORE_URLS` triggers the startup warning "Overriding HTTP_PORTS '8080'"; use `ASPNETCORE_HTTP_PORTS=8000`.
    - The comment at `:1` says "(today: the proxy)", which is stale.
    - There is no HEALTHCHECK. Podman's OCI images would drop one anyway, so put it in compose if wanted.

14. **`AuthorizationGrant.IssuedAt` is a placeholder overwritten by `Issue`.** Severity: nit.
    - Where: `IdPorten/AuthorizationCodes.cs:21`, `:30-38`; callers pass `default` (`IdPortenApi.cs:100`,
      `AuthenticationClient.cs:51`).
    - Fix: keep the issue time in the dictionary value (`(grant, issuedAt)`), not in the caller-built record.

15. **Docs drift.** Severity: low.
    - `README.md:97` says JWTs are written in `IdPorten/SigningKey.cs`; the file is `Tokens/SigningKey.cs`.
    - `README.md:146-156` ("Verified (2026-10-19)") predates the identity features. Nothing in the README records
      verification of ID-porten, Maskinporten or Home (2026-10-20).
    - `planned-components/authentication/localtest-app-identity.md:3` still says "steps 2-4 open". Commit `180fd3c`
      ("fake Maskinporten and the /Home contract move into the localtest app") did steps 2-3; its open questions about
      the picker URL and ownership are probably settled.
    - `README.md:4` says a 16 MB binary and 34 MB image; measured 17.3 MB (16.5 MiB) and 35.6 MB. That is fine.

16. **Smoke-test gaps** (it covers most of what it claims). Severity: low.
    - Not covered:
      - the picker GET itself (status, hidden fields, preselected level);
      - that `POST authorize` echoes `state`;
      - a token request with a wrong `redirect_uri` or `client_id`;
      - the id_token's `kid` matching JWKS and its `aud`/`nonce`. These are covered only indirectly through the
        production sign-in.
    - WebSocket pass-through is claimed in `README.md:57` but never tested.
    - `GetTestOrgToken`'s `ttd` check (`smoke-test.sh:99`) needs internet access (`altinncdn.no` org lookup). The
      README says so but the script does not.
    - The ID-porten issuer check (`smoke-test.sh:59`) fails when `BASE` differs from the configured
      `PlatformEndpoint`.

Checked and found correct (no finding):
- **JWT construction** (`Tokens/SigningKey.cs`):
  - base64url without padding via `System.Buffers.Text.Base64Url`; RS256 PKCS#1 over the ASCII signing input.
  - The header `kid` equals the JWKS `kid` (live: `fKBrqQxPNO3tcGR0`).
  - For the certificate: `kid` is the uppercase hex thumbprint, which is what `X509SecurityKey.KeyId` produces, so the
    production `LocaltestPublicSigningKeyProvider` key matches. `x5t` is base64url SHA-1.
  - `iat`, `nbf` and `exp` are numbers, `amr` is an array, `aud` is a string, and `consumer` is
    `{authority:"iso6523-actorid-upis", ID:"0192:…"}`, exactly as `GetOrganisationNumberFromConsumerClaim` parses it.
- **Issuers**: the ID-porten issuer has no trailing slash and equals Authentication's configured
  `OidcProviders:idporten:Issuer`. The Maskinporten issuer has a trailing slash and is contained in
  `MaskinportenWellKnownConfigEndpoint`.
- **Self-identified users**: `urn:altinn:userid` is read by `GetUserFromToken` (`AuthenticationHelper.cs:86-90`).
- **PKCE**: S256 is compared against the base64url SHA-256 of the ASCII verifier. Only S256 is accepted on
  `POST authorize`, and the verifier is required whenever a challenge was stored.
- **Codes**: `TryRemove` makes them atomic and single-use, with a 60-second lifetime checked on redeem. A failed check
  still burns the code, which is acceptable and is what the smoke test asserts.
- **Upstream state**: production keeps it server-side (`GetForCallbackByStateAsync`), so the headless flow without a
  cookie jar (`UseCookies = false`) is correct.
- **Cookie rewriting** (`Home/AuthenticationClient.cs:82-96`, `Proxy/Forwarder.cs:110-123`):
  - Splitting on `;` is safe, because a cookie value cannot contain `;`, and commas in `Expires` do not matter for
    that split.
  - `HttpResponseHeaders.TryGetValues("Set-Cookie")` returns one entry per header.
  - Attribute order does not matter.
  - Production sets SameSite=Lax (`OidcServerService.cs:335-376`), so removing `Secure` does not produce a
    SameSite=None cookie that browsers reject, and there are no `__Host-` or `__Secure-` names.
  - The proxy regex only removes a `secure` attribute that follows a `;`.
- **Host header**: the dedicated hosts are matched case-insensitively without the port. The proxy and
  `AuthenticationClient` send the public `Host`, and YARP's `HttpTransformer.Default` adds `X-Forwarded-*` (checked by
  decompiling YARP 2.3.0), as the README says.
- **Concurrency**:
  - `AuthorizationCodes`: `ConcurrentDictionary`, and enumerating it while cleaning up is safe.
  - `AppRoutes`: the dictionary is concurrent, and `Save` snapshots inside a lock after the mutation, so the last save
    always reflects the current state.
  - `TestUsers`: double-checked under a `SemaphoreSlim`. The unlocked fast-path read of `_cached`/`_cachedAt` can race
    (a torn `DateTimeOffset` read), but the worst case is one extra or one skipped refetch.
- **AOT**:
  - Keyed services and `int?` query binding go through the request delegate generator. This is evident from the
    running binary and the `GeneratedRouteBuilderExtensions` frames in the log.
  - `AppRoute`, `string`, `Dictionary<string,string>`, `JsonWebKeySet`, the token records and `TestUser[]` are all in
    the context.
  - The only gap found is `ProblemDetails` (finding 1).
- **Proxy**: the route table matches the README; the catch-all uses `{**path}` (file-like paths are forwarded); the
  slash redirects keep the query; responses stream (SSE verified by the logs' `ResponseBodyCanceled` on the stream and
  by the smoke test's design). The 30-second connection lifetime, 5-second connect timeout, 5-minute activity timeout
  and 200 MB body limit match the README. Routes persist atomically: `.tmp` plus `File.Move`, under a lock; the live
  `/data/app-routes.json` held the routes of the running apps.

## Verification done

Ran (all read-only):
- Read the four docs, every `.cs` file, `Localtest.csproj`, `Dockerfile`, `.dockerignore` and `smoke-test.sh`.
- Read Authentication's fakes (`LocaltestAuthenticationExtensions.cs`, `LocaltestAuthLevels.cs`,
  `LocaltestKeyProviders.cs`) and the vendored production files `UpstreamTokenValidator.cs`, `OidcProviderService.cs`
  (token request), `OidcServerService.cs` (callback state and issuer validation, authorize parameters, cookies),
  `AuthenticationHelper.GetUserFromToken`, and `AuthenticationController` (`exchange/*`, Maskinporten, Studio).
- Decompiled with `ilspycmd` (no build): `ProblemHttpResult`, `HttpResultsHelper`, `JsonOptions` and
  `ProblemDetailsServiceCollectionExtensions` from the ASP.NET Core 10.0.12 shared framework, and `HttpTransformer`
  from YARP 2.3.0.
- `curl` GET against `http://local.altinn.cloud:8000`:
  - ID-porten discovery and JWKS (issuer correct, RS256 key, `kid` present), and Maskinporten metadata.
  - `GET maskinporten/token` (404 proxy text).
  - `GET idporten/authorize`: without state gives 400 as a JSON string. With a `state` holding `"><script>` it comes
    back HTML-encoded in the hidden field, and `acr_values=level0 idporten-loa-high` preselects 4.
  - `/Home/`, `/home` (200 start page), `/localtest/api/proxy/apps` and `upstreams` (200 JSON),
    `/Home/GetTestUserToken` (400 "userId is required."), `?userId=abc` (400 empty), `/Home/Localtest/Version`
    (`8`, application/json), `/storage` (301), `/localtest/api/proxy` (proxy 404), `HEAD /` (404).
- `curl` POST to `/localtest/api/idporten/token` only:
  - no body: 500;
  - `grant_type=password`: 400 `unsupported_grant_type` (indented JSON);
  - `grant_type=authorization_code&code=junk`: 400 `invalid_grant`;
  - JSON body: 500;
  - text/plain body: 500.
  - The 500s were confirmed in `podman logs` as `InvalidOperationException: Incorrect Content-Type`.
- `podman ps`, `podman inspect` (empty `User`), `podman exec … id` (root), `ls /app /data` and
  `cat /data/app-routes.json`, `podman image ls` (35.6 MB), and `podman logs` (line count and warnings).

Not verified:
- Finding 1 at runtime: I could not provoke a failing sign-in or exchange without side effects. The conclusion comes
  from decompiled framework code.
- Findings 4 and 5 in a browser (no POST to `/Home` or `authorize`).
- The build itself (AOT and trim warnings), the smoke tests and WebSocket pass-through. None were run, per the
  constraints.
- Production ID-porten's real `ValidateCallbackIssuer` and `StrictIdTokenValidation` settings, and whether real
  ID-porten sends `iss` in the authorization response (I believe it does, but I have not checked).
