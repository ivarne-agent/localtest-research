# Receipt: upstreaming

- Upstream: [Altinn/altinn-receipt](https://github.com/Altinn/altinn-receipt)
- Pin: `babc0ed` (main, 2026-10-20), [`../components/receipt/vendor/UPSTREAM.md`](../components/receipt/vendor/UPSTREAM.md)
- Group: none
- Reminders: Renovate moves the frontend almost daily; re-pin on backend changes or on request.
- Localtest modifications: 4 directive sites in `src/backend/Altinn.Receipt/Program.cs`, the hook
  `fakes/LocaltestReceipt.cs`, the `IStorage` decorator `fakes/SelfLinksStorage.cs`, the shared copies
  `fakes/ServiceDiscovery/` (the OTLP helper's copy was inlined into the site in task 63), two build overrides, and the `localtest:` deviations in `Dockerfile` and
  `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/receipt/` (sites relative to its `vendor/`).

## Phase 1: easy fixes

- [ ] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Add `OpenTelemetry.Exporter.OpenTelemetryProtocol`
      (at the host's OpenTelemetry version, 1.19.1) to `Altinn.Platform.Receipt.csproj` and call `UseOtlpExporter()` in
      `ConfigureServices` when the variable is set, next to the Azure Monitor block; add the environment resource
      detector after `AddAttributes` so `OTEL_SERVICE_NAME` wins over `platform-receipt`.
  - Why: without it, Receipt sends telemetry only to Azure Monitor with a Key Vault connection string, so a local run
    exports nothing.
  - Size: trivial.
  - Removes: site `Program.cs:190-206` (with the OTLP rule inlined in it since task 63), the OTLP `PackageReference` in `localtest.build.targets` and the
    `CS8321` `NoWarn` in `localtest.build.props`.
  - Upstream: none found.
  - Detail: [`../reviews/otel/receipt.md`](../reviews/otel/receipt.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Configurable base for the sign-in `goto` URL.** In `Configure`, the status-code page builds the 401 redirect's
      `goto` as `https://platform.{GeneralSettings:Hostname}{path}{query}`. Read the origin from a setting (for example
      `GeneralSettings:PlatformBaseUrl`, defaulting to the current expression) instead of hard-coding the scheme and the
      `platform.` prefix.
  - Why: locally there is no `platform.` host and no TLS, so the browser is sent back to a host that does not resolve.
  - Size: small.
  - Removes: site `Program.cs:278-284`.
  - Upstream: none found. Using the request's own scheme and host, as localtest does, depends on the production ingress
    passing the browser's `Host` and scheme through; I could not confirm that, so the setting is the safer proposal.
  - Detail: `UPSTREAM.md`, site 2.

- [ ] **Make attachment and PDF links relative to the page always.** `src/frontend/src/utils/urlHelper.ts`,
      `makeUrlRelativeIfSameDomain`, keeps a data element's `selfLinks.platform` absolute unless its host equals the
      page's. Receipt is served from the same `platform.` host as Storage in every environment, so the frontend can use
      the link's path, query and fragment whatever its host.
  - Why: Storage writes `https://platform.{Hostname}/storage/...` into every self link, which neither resolves nor
    serves TLS locally.
  - Size: small.
  - Removes: `fakes/SelfLinksStorage.cs` and the hook `fakes/LocaltestReceipt.cs` with its call, site
    `Program.cs:52-54` (the hook does nothing else).
  - Upstream: none found. The other way to remove the fake is Storage's self-link base, a phase 2 item in
    [`storage.md`](storage.md); either one is enough for Receipt.
  - Detail: [`../components/receipt/README.md`](../components/receipt/README.md), "What is faked";
    [`../../planned-components/receipt/receipt.md`](../../planned-components/receipt/receipt.md).

## Phase 2: structural changes

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder`, so `PlatformSettings` endpoints such as `http://storage/storage/api/v1/` resolve
      through `services__<name>__http__0`. Agree with the team on the pattern the four `AltinnServiceDefaults` hosts
      already use (see the README's "Across services").
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `Program.cs:36-41`, `fakes/ServiceDiscovery/` and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md).

- [ ] **Publish the image where localtest can pull it.** `.github/workflows/build-and-push-app.yml` pushes to the
      private Azure Container Registry only. Also push the same image to a public registry (for example
      `ghcr.io/altinn/altinn-receipt`), tagged by commit.
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the vendored copy
    itself.
  - Upstream: none found.

## Stays local

- `Dockerfile`: `COPY vendor/...` paths and the `/src` layout with `DirectoryBuildPropsPath` and
  `DirectoryBuildTargetsPath`: the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `NODE_EXTRA_CA_CERTS` for `yarn --immutable` when `/run/agent/tls/ca-bundle.pem` exists: the agent
  sandbox's TLS-intercepting proxy; a no-op elsewhere.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://+:5060`: deployment settings, which
  upstream's chart supplies; with a published image they move to the manifest. Development already lets the cookie
  scheme read OpenID metadata over http, so nothing upstream is needed for it.
- `.dockerignore`: the same entries under `vendor/`: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false` and the vendored-project test: the mechanism
  that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.

Coverage: 4 sites, 3 fakes (the hook, the decorator, `ServiceDiscovery/`), 2 build overrides, 4 Dockerfile and
1 `.dockerignore` deviations: all accounted for.
