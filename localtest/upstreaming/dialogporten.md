# Dialogporten: upstreaming

- Upstream: [Altinn/dialogporten](https://github.com/Altinn/dialogporten)
- Pin: `ec333e4` (main, 2026-10-20),
  [`../components/dialogporten/vendor/UPSTREAM.md`](../components/dialogporten/vendor/UPSTREAM.md)
- Group: none
- Reminders: the host builds rewrite each host's `packages.lock.json` (`git checkout -- vendor` before `check`); a new
  `LocalDevelopment` flag goes into `fakes/` `LocaltestConfiguration`.
- Localtest modifications: 9 directive sites (one hook per host's `Program.cs`, two in `OpenTelemetryExtensions.cs`, two
  in `FluentValidationStringExtensions.cs`, one in each host's `TokenIssuerCache.cs` from task 63), the fakes `GraphQL/`, `Infrastructure/` (with the shared copies
  `Maskinporten/` and `ServiceDiscovery/LocaltestPrefixRouting.cs`), `Service/`, `WebApi/` and the shared copy
  `ServiceDiscovery/`, two build overrides, the `localtest:` deviations in `Dockerfile.webapi`, `.graphql` and
  `.service`, `.dockerignore` and `postgres-init/`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/dialogporten/` (sites relative to its `vendor/src/`). Upstream `main` has ten commits after the pin (up to
`a11e9fd`); none touches a site, a fake's counterpart or a Dockerfile beyond the SDK digest.

Upstream's own local mode is `podman compose up` with `ASPNETCORE_ENVIRONMENT=Development`, user secrets, Postgres,
Redis and a migration-bundle container, and the `LocalDevelopment` flags that swap the Azure Service Bus for an
in-process bus and, by default, every platform dependency for a local stub. Localtest already uses that mode: the
Development environment, the in-process bus and the flags, with the stubs off so the real platform answers. No Azure
service is involved (App Configuration, Key Vault, Application Insights and the Service Bus are all off when their
settings are unset or the flags say so), so there is no "local configuration without Azure" item here. What remains are
http, the Maskinporten package, service discovery and the localtest endpoints.

## Phase 1: easy fixes

- [ ] **Let the OTLP exporter read the endpoint and protocol itself.** In
      `src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs`, `AddDialogportenTelemetry`, drop
      `options.Endpoint = endpoint; options.Protocol = otlpProtocol;` from the trace and metric `AddOtlpExporter`
      calls, so the SDK reads `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_EXPORTER_OTLP_PROTOCOL` and appends `/v1/traces`
      and `/v1/metrics`. Optionally default the protocol instead of throwing when it is unset.
  - Why: an endpoint set in code gets no signal path, so with `http/protobuf` traces and metrics post to the bare
    endpoint, where a collector answers 404; only `grpc` works today. The Serilog sink already appends `/v1/logs`.
  - Size: trivial.
  - Removes: sites `Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs:106-112` and `:134-139`.
  - Upstream: none found ([dialogporten#1465](https://github.com/Altinn/dialogporten/issues/1465), closed, added the
    OTLP exporters themselves).
  - Detail: [`../reviews/otel/dialogporten.md`](../reviews/otel/dialogporten.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Allow OpenID metadata over http in Development.** In the WebApi's and the GraphQL host's
      `Common/Authentication/AuthenticationBuilderExtensions.cs`, set `options.RequireHttpsMetadata` from the
      environment (false in Development) or from a per-scheme setting on `JwtBearerTokenSchemasOptions`; in both
      `Common/Authentication/TokenIssuerCache.cs`, pass the same value to the retriever:
      `new OpenIdConnectConfigurationRetriever()` with `new HttpDocumentRetriever { RequireHttps = ... }`.
  - Why: the stack's Authentication and the fake Maskinporten and ID-porten publish their metadata over plain http; the
    JWT schemes refuse it, and the issuer cache's retriever refuses it whatever the scheme says.
  - Size: small.
  - Removes: the sites in `Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs` and
    `Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/TokenIssuerCache.cs` (which replaced the two
    `Authentication/` fakes in task 63) and the `ConfigureAll<JwtBearerOptions>` in `fakes/WebApi/LocaltestWebApi.cs` and
    `fakes/GraphQL/LocaltestGraphQl.cs`.
  - Upstream: none found.
  - Detail: [`../components/dialogporten/README.md`](../components/dialogporten/README.md), "Fakes (`fakes/`)".

- [ ] **Accept http URLs in dialogs when a setting allows it.** Both `IsValidHttpsUrl` overloads in
      `src/Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs`
      require `https`. Add a setting (for example `Application:Dialogporten:AllowHttpUrls`, default false) that also
      accepts `http`, and leave the HTML content rule unchanged.
  - Why: the stack is plain http on `http://local.altinn.cloud:8000`, so every link the adapter puts in a dialog (GUI
    and API actions, attachments, transmission attachments) is http, and Dialogporten refused the dialog with 400
    `'Url' is not a well-formatted HTTPS URL.`
  - Size: small. The rules are static FluentValidation extensions without access to options, so the setting has to reach
    them some other way (a validator that takes `IOptions`, or a static switch set at start); I could not confirm which
    the team prefers.
  - Removes: sites `Digdir.Domain.Dialogporten.Application/Common/Extensions/FluentValidation/FluentValidationStringExtensions.cs:29-34`
    and `:39-44`.
  - Upstream: none found.
  - Detail: `UPSTREAM.md`, the `FluentValidationStringExtensions.cs` row;
    [`dialogporten-adapter.md`](dialogporten-adapter.md).

- [x] **Configurable Maskinporten and token-exchange endpoints in `Altinn.ApiClients.Maskinporten`.** Merged as
      [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92) (2026-10-26)
      and released in 10.2.0: `MaskinportenSettings` has `TokenEndpoint`, `AssertionAudience` and
      `TokenExchangeEndpoint`, applied when tokens are requested through a client definition.
  - Why: the package hard-coded `https://*.maskinporten.no` and the exchange hosts.
  - Size: small (done).
  - Removes: nothing on its own; the consumer bump below removes the fake.
  - Upstream: [altinn-apiclient-maskinporten#92](https://github.com/Altinn/altinn-apiclient-maskinporten/pull/92),
    merged.
  - Detail: [`../reviews/2026-10-24-maskinporten-pr92.md`](../reviews/2026-10-24-maskinporten-pr92.md).

- [ ] **Bump `Altinn.ApiClients.Maskinporten` to 10.2.0.**
      `src/Digdir.Domain.Dialogporten.Infrastructure/Digdir.Domain.Dialogporten.Infrastructure.csproj` references 10.1.0
      (also on upstream `main`). Then set `Infrastructure:Maskinporten:TokenEndpoint`, `AssertionAudience` and
      `TokenExchangeEndpoint` in localtest's manifest, and a throwaway JWK as `EncodedJwk` instead of today's
      `{"kty":"RSA"}` placeholder (the package signs the assertion). The three Maskinporten-backed clients (events, party
      names, authorization) register through `AddMaskinportenHttpClient` with `SettingsJwkClientDefinition` and bind
      `Infrastructure:Maskinporten`, so the new keys bind without code changes.
  - Why: without the bump the package can only call the real Maskinporten, so localtest replaces `IMaskinportenService`.
  - Size: trivial.
  - Removes: `fakes/Infrastructure/Maskinporten/` and its `AddLocaltestMaskinporten` call in
    `fakes/Infrastructure/LocaltestInfrastructure.cs`.
  - Upstream: none found (no open Renovate PR for 10.2.0). The localtest token endpoint reads the assertion's payload
    only, not its header or signature (`../localtest/Maskinporten/MaskinportenApi.cs`, `TryReadAssertion`), so a signed
    assertion passes.
  - Detail: [`../components/shared/README.md`](../components/shared/README.md), "`IMaskinportenService` fake"; TODO
    task 15 in [`../../TODO.md`](../../TODO.md).

## Phase 2: structural changes

- [ ] **Separate internal bases for the clients on `Infrastructure:Altinn:BaseUri`.** In
      `src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs` and `InfrastructureExtensions.cs`, give
      the Resource Registry, Access Management metadata, party-name and PDP/authorized-parties clients their own
      optional base URIs (defaulting to `BaseUri`), as `EventsBaseUri` already is for Events. `MetadataLinkProvider`
      keeps building client-facing links from `BaseUri`.
  - Why: upstream uses one base for four services, and the same value is the public origin in links handed to clients.
    Locally the services are separate containers, so one base cannot serve both.
  - Size: medium; touches the settings, their validation and four client registrations.
  - Removes: `fakes/Infrastructure/ServiceDiscovery/` (`LocaltestPrefixRouting.cs`) and
    `fakes/Infrastructure/LocaltestInfrastructure.cs` (its only other content goes with the Maskinporten bump).
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), the
    Dialogporten table and section 6; the Correspondence counterpart in [`correspondence.md`](correspondence.md).

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      in the three hosts, or once in `AddInfrastructure`, and commit the updated `packages.lock.json` files, so URLs such
      as `http://events/` resolve through `services__<name>__http__0` (pattern in the README's
      ["Across services"](README.md#across-services)).
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name. Adding the package outside upstream also breaks upstream's locked restore (NU1004), which
    is why localtest turns locked mode off.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: `fakes/ServiceDiscovery/`, the `Microsoft.Extensions.ServiceDiscovery` `PackageReference`,
    `RestoreLockedMode=false` and the `fakes/ServiceDiscovery` compile item in `localtest.build.targets`, and the
    `--locked-mode` deviation in `Dockerfile.webapi`, `Dockerfile.graphql` and `Dockerfile.service`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), "Locked-mode
    restore".

- [ ] **A supported way to refresh the Resource Registry data and caches on demand.** Production runs
      `SyncSubjectMapCommand` and `SyncPolicyCommand` from the Janitor on a schedule and lets the named FusionCaches
      (Resource Registry 20 minutes, authorization 15) expire. Ask for a development-only admin endpoint, or a setting for
      the cache durations, so a freshly published resource is visible at once. Localtest could then run the published
      `janitor` image's `sync-subject-resource-mappings` and `sync-resource-policy-information` commands instead of its
      own sync endpoint; I could not confirm that the Janitor runs against the stack unchanged.
  - Why: localtest provisions apps at runtime and expects Dialogporten to know a new app's resource immediately; with
    production timings a dialog for a just-pushed app is refused with 422 for up to 20 minutes.
  - Size: medium; a design decision for the team.
  - Removes: `fakes/WebApi/Localtest/` (`LocaltestEndpoints.cs`) and `fakes/Infrastructure/LocaltestCaches.cs`. The two
    test inspection endpoints in the same file (`GET` and `DELETE localtest/dialogs`) move to the localtest side, reading
    the `dialogporten` database directly, or go.
  - Upstream: none found.
  - Detail: [`../components/dialogporten/README.md`](../components/dialogporten/README.md), "Localtest endpoints" and
    "Provisioning: step 5".

- [ ] **Configure the hosts from the manifest and pull the published images.** Upstream already publishes public
      images, `ghcr.io/altinn/dialogporten-{webapi,graphql,service,migration-bundle,janitor}`, tagged by version and
      `version-sha` (`.github/workflows/workflow-publish-docker-images.yml`). Once the items above are merged, localtest
      moves the defaults of `LocaltestConfiguration` (connection strings, Redis, the platform URLs, the JWT schemes'
      well-known URLs, the OpenAPI URLs, the CORS origin, the Ed25519 test keys, the fourteen `LocalDevelopment` flags,
      warmup off) into the manifest as environment variables, sets `ASPNETCORE_ENVIRONMENT=Development` there, and runs
      the `migration-bundle` image before the hosts as upstream's `docker-compose.yml` does, instead of ordering the
      hosts' starts so only one migrates.
  - Why: the hook and its defaults exist only to inject configuration into a vendored build.
  - Size: small for localtest; no upstream change beyond the items above.
  - Removes: sites `Digdir.Domain.Dialogporten.WebApi/Program.cs:63-69`, `Digdir.Domain.Dialogporten.GraphQL/Program.cs:45-51`
    and `Digdir.Domain.Dialogporten.Service/Program.cs:41-47` (they also hold the service-discovery registration, so
    they go with that item too); the hooks `fakes/WebApi/LocaltestWebApi.cs`, `fakes/GraphQL/LocaltestGraphQl.cs`,
    `fakes/Service/LocaltestService.cs`; `fakes/Infrastructure/LocaltestConfiguration.cs`,
    `LocaltestDialogportenOptions.cs` and `LocaltestOverrides.cs`; then `Dockerfile.webapi`, `Dockerfile.graphql`,
    `Dockerfile.service`, `.dockerignore`, `localtest.build.props`, `localtest.build.targets` and the vendored copy
    itself.
  - Upstream: nothing needed; the images are published.
  - Detail: [`../components/dialogporten/README.md`](../components/dialogporten/README.md), "Configuration";
    [`../../planned-components/dialogporten/dialogporten-core.md`](../../planned-components/dialogporten/dialogporten-core.md).

## Stays local

- `Dockerfile.webapi`, `.graphql`, `.service`: `COPY` of the overrides and `fakes/`, `DirectoryBuildPropsPath` and
  `DirectoryBuildTargetsPath`, and the `vendor/` prefix with the `/src/vendor` working directory: the vendoring layout;
  gone with the vendored copy.
- `Dockerfile.webapi`, `.graphql`, `.service`: `COPY vendor/global.json`, which upstream leaves out: pins the SDK so a
  host build matches the image; gone with the vendored copy.
- `Dockerfile.webapi`, `.graphql`, `.service`: `ASPNETCORE_ENVIRONMENT=Development`: a deployment setting (upstream's
  compose sets it too); with a published image it moves to the manifest.
- `Dockerfile.webapi`: a password-less `Infrastructure__DialogDbConnectionString` on the publish step only: NSwag starts
  the host at build time and parses the connection string, which has no default in the fakes (the manifest sets it at
  run time, TODO.md task 60).
- `.dockerignore`: keeps host `bin/` and `obj/` out of the build context: the vendoring layout.
- `postgres-init/50-dialogporten.sh`: the role, database and extensions on the shared Postgres; production's
  infrastructure provisions them.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the `CS1591` `NoWarn` and the vendored-project
  test: the mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/Infrastructure`, `fakes/WebApi`, `fakes/GraphQL` and `fakes/Service` into
  their projects: as above.

Coverage: 9 sites, 12 fakes (`GraphQL/`: the hook; `Infrastructure/`: `LocaltestInfrastructure.cs`,
`LocaltestOverrides.cs`, `LocaltestConfiguration.cs`, `LocaltestCaches.cs`, `LocaltestDialogportenOptions.cs`,
`Maskinporten/`, `ServiceDiscovery/`; `Service/`: the hook; `ServiceDiscovery/`; `WebApi/`: the hook, `Localtest/`), 2 build overrides, 13 Dockerfile deviations (4 in each of the three, 1 more in `Dockerfile.webapi`), 1 `.dockerignore` deviation, 1
Postgres init script: all accounted for.
