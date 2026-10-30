# Access Management GUI: upstreaming

- Upstream: [Altinn/altinn-access-management-frontend](https://github.com/Altinn/altinn-access-management-frontend), the
  SPA (`src/`) and its BFF (`backend/src/Altinn.AccessManagement.UI`)
- Pin: `76c599d` (main, 2026-10-30),
  [`../components/access-management-ui/vendor/UPSTREAM.md`](../components/access-management-ui/vendor/UPSTREAM.md)
- Group: none
- Reminders: new settings or client base addresses go into the manifest's `access-management-ui` envs and the README; a
  cookie written outside `Response.Cookies` is not seen by the cookie policy; a new hard-coded host in the SPA needs patch
  0003's treatment; move the base image digests when upstream's move.
- Localtest modifications: no directive sites in `vendor/`. Three build-time patches in `patches/` (0001 the BFF's hooks
  in `Program.cs`, 0002 the access-token certificate in `AccessTokenProvider.cs`, 0003 the SPA's links in
  `pathUtils.tsx`; the C# hunks are `#if LOCALTEST` blocks), two copies of shared fakes (`fakes/ServiceDiscovery/`,
  `fakes/Cookies/`), two build overrides, and the `localtest:` deviations in `Dockerfile` and `.dockerignore`.

Paths in "Removes" are relative to `../components/access-management-ui/`; file paths in patches are relative to
upstream's repository root. Research: [`../../planned-components/access-management-ui/plan.md`](../../planned-components/access-management-ui/plan.md).

## Phase 1: easy fixes

- [ ] **The SPA's links from configuration or the page's origin.** `src/resources/utils/pathUtils.tsx` maps the page's
      host to one of five environments and every other host to production (`getAmBaseUrl`, `getPlatformUrl`,
      `getAfUrl`, `getHostUrl`, `getAltinnStartPageUrl`). Take the bases from the BFF's configuration (the Razor view
      already hands the SPA `window.featureFlags`; the same object, or `config.ts`'s JSON config element, could carry
      `platformUrl`, `afUrl`, `amBaseUrl`), or derive them from the page's origin for an unknown host.
  - Why: on `am.ui.local.altinn.cloud` the party switch (`changeandredirect`), logout, login on an expired token and the
    inbox links go to production hosts.
  - Size: small.
  - Removes: patch `0003-spa-links-follow-origin.patch`.
  - Upstream: none found.

- [ ] **Cookies `Secure` by the request's scheme, not by the environment.** `HomeController` (`XSRF-TOKEN`,
      `selectedLanguage`, the party cookies through `PartyCookieHelper`), `ReporteeController` (the party cookies) and
      `AuthenticationController.Refresh` (`AltinnStudioRuntime`, always `Secure`) set `Secure = !IsDevelopment()` or
      `true`. `Secure = Request.IsHttps` keeps production's attributes behind TLS.
  - Why: a browser refuses a `Secure` cookie over http, and without `XSRF-TOKEN` the SPA sends no antiforgery header, so
    every POST of the GUI fails on the stack.
  - Size: small (five call sites).
  - Removes: `fakes/Cookies/` and its line in patch 0001's first block.
  - Upstream: none found.

- [ ] **`RequireHttpsMetadata` as a setting.** `Program.cs` sets it to `false` for the JWT cookie handler in Development
      only; a `PlatformSettings:RequireHttpsMetadata` (default `true`) lets a non-Development environment read http
      metadata.
  - Why: the stack's Authentication is plain http, and the image runs Production (Development loads the SPA from Vite's
    dev server and turns the mocks on).
  - Size: trivial.
  - Removes: patch 0001's second block.
  - Upstream: none found.

- [ ] **The platform access token's certificate from a file.** `AccessTokenProvider` reads the PKCS#12 certificate from
      Key Vault only. Bind `AccessTokenSettings` and, when `AccessTokenSigningCertificateFullPath` is set, read the file
      (as `Altinn.Common.AccessTokenClient`'s own `SigningCredentialsResolver` does).
  - Why: there is no Key Vault locally; the stack's components accept tokens signed with the shared test certificate.
  - Size: small.
  - Removes: patch `0002-bff-access-token-certificate.patch` and the `AccessTokenSettings` line of patch 0001.
  - Upstream: none found.

- [ ] **The image's environment `Production`, its port the .NET default.** `Dockerfile` sets
      `ASPNETCORE_ENVIRONMENT=Development` (production's Container App overrides it) and `ASPNETCORE_HTTP_PORTS=80`.
  - Why: an image run as published must not load Vite's dev server; 8080 is the stack's convention and the .NET 8+
    default.
  - Size: trivial (production sets its own values).
  - Removes: two `Dockerfile` deviations (`ENV ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_HTTP_PORTS` and `EXPOSE`).
  - Upstream: none found.

- [ ] **OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** The BFF has Application Insights only (with the Key Vault
      key). The stack's rule (TODO.md task 53) is OpenTelemetry over OTLP exactly when the variable is set.
  - Why: `--with monitoring` shows nothing of the GUI today (a known gap, not a modification).
  - Size: small (the OpenTelemetry packages, ASP.NET Core and HttpClient instrumentation, `UseOtlpExporter()`).
  - Removes: nothing local today; closes the gap in the component README.
  - Upstream: none found.

## Phase 2: structural changes

- [ ] **Service discovery by name.** The BFF's clients take whole base URLs from `PlatformSettings`. A registration of
      `Microsoft.Extensions.ServiceDiscovery` (without the pass-through) would let a local host name the services
      (`http://access-management/...`), as the stack does in every .NET component.
  - Why: the stack's convention ([`../CONVENTIONS.md`](../CONVENTIONS.md#service-discovery)); without it the manifest
    would point the clients at the front door or the public origin.
  - Size: small in code, but a decision for the team.
  - Removes: `fakes/ServiceDiscovery/`, its line in patch 0001, the `Microsoft.Extensions.ServiceDiscovery` reference in
    `localtest.build.targets`.
  - Upstream: none found; the same change in other services: [README](README.md#across-services).

- [ ] **Run upstream's published image.** `template-build-and-push-container.yml` pushes
      `ghcr.io/altinn/altinn-access-management-frontend`. With the items above in, localtest could run that image
      configured by the manifest alone.
  - Why: the end state of this list.
  - Size: medium (the items above, then a check that the image is public).
  - Removes: the vendored copy, the patches, the `Dockerfile`, the build overrides.
  - Upstream: none.

## Stays local

- `Dockerfile`: the `source` stage (`git apply` of `patches/`) and `COPY --from=source` instead of `COPY . .` / `COPY
backend .`: the mechanism that carries the patches; gone with the last one.
- `Dockerfile`: the backend stage's component layout and the `DirectoryBuildPropsPath`/`DirectoryBuildTargetsPath`
  variables: the vendoring layout that defines `LOCALTEST` and compiles `fakes/`; gone with the patches.
- `Dockerfile`: `NODE_EXTRA_CA_CERTS` when `/run/agent/tls/ca-bundle.pem` exists (the sandbox's TLS-intercepting proxy;
  a no-op elsewhere) and `docker.io/library/` before `node:22-slim` (Podman's short-name resolution outside a terminal).
- `.dockerignore`: a developer's `node_modules`, `dist`, `bin` and `obj` under `vendor/`, the README and the smoke test:
  the vendoring layout.
- `localtest.build.props` and `.targets`: `LOCALTEST`, the analyzer relaxations for the fakes, compiling `fakes/` and the
  service-discovery package: the mechanism that carries the sites.

Coverage: 0 sites, 3 patches (0001 in three items, 0002, 0003), 2 fakes (`ServiceDiscovery/`, `Cookies/`), 2 build
overrides, 7 Dockerfile and 1 `.dockerignore` deviations: all accounted for.
