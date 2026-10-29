# Correspondence: upstreaming

- Upstream: [Altinn/altinn-correspondence](https://github.com/Altinn/altinn-correspondence)
- Pin: `d2dc912` (main, 2026-10-22; synced from `6dcf561` in task 58's follow-up),
  [`../components/correspondence/vendor/UPSTREAM.md`](../components/correspondence/vendor/UPSTREAM.md)
- Group: none
- Reminders: grep for other hard-coded `Secure`/`SameSite=None` cookie writes besides `ExpireCookie`; `ConfigureAuthentication`
  must keep the correlation cookie name and callback path; update the smoke test's EF migration count.
- Localtest modifications: 7 directive sites (4 pure additions in `src/Altinn.Correspondence.API/Program.cs`,
  an `#if !LOCALTEST` block in `IdportenTokenValidator.cs`, and one each in `RecurringJobRegistration.cs` and
  `Auth/DependencyInjection.cs` (`ExpireCookie`) from task 63; the
  `XacmlRequestFactory.cs` block was removed in task 58's follow-up), the hook
  `fakes/LocaltestCorrespondence.cs` with its options class, the fakes `Auth/`, `Storage/`,
  `Inspection/`, the shared copies `Maskinporten/` and `ServiceDiscovery/` (the OTLP helper's copy was inlined into the site
  in task 63), two build overrides,
  the `localtest:` deviations in `Dockerfile`, `.dockerignore` and the Postgres init script.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/correspondence/` (sites relative to its `vendor/`). The pin is upstream `main` (checked 2026-10-27).

## Phase 1: easy fixes

- [x] **Name the party by `urn:altinn:partyid:{id}` in PDP requests.** Merged upstream as
      [altinn-correspondence#2157](https://github.com/Altinn/altinn-correspondence/pull/2157) (`d2dc912`, 2026-10-22):
      `AltinnAuthorizationService` and `AltinnTokenXacmlMapper` pass the resolved party id with its prefix, so
      `XacmlRequestFactory.CreateResourceCategory` takes the prefix branch whatever the id's length. Block
      removed in task 58's follow-up (synced to `d2dc912`).
  - Why: `IsPartyId` accepts a bare party id of 8 or 10 digits only, and the test data's 6-digit ids (Digdir 500006,
    Sophie Salt 501337) made every PDP-checked call answer 500.
  - Size: trivial (done).
  - Removed: site `src/Altinn.Correspondence.Integrations/Altinn/Authorization/XacmlRequestFactory.cs:31-36`, in task
    58's follow-up. The smoke test's app-lib and recipient checks passed without it (78 `ok`, 2026-10-27).
  - Upstream: [altinn-correspondence#2157](https://github.com/Altinn/altinn-correspondence/pull/2157), merged.
  - Detail: [`../../planned-components/correspondence/party-ids.md`](../../planned-components/correspondence/party-ids.md),
    option (c).

- [ ] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** In
      `src/Altinn.Correspondence.Integrations/OpenTelemetry/DependencyInjection.cs`, `ConfigureOpenTelemetry`, call
      `UseOtlpExporter()` when the variable is set, beside the Azure Monitor block; add
      `OpenTelemetry.Exporter.OpenTelemetryProtocol` (at the host's OpenTelemetry version, 1.18.0) to the Integrations
      project and the environment resource detector after `AddAttributes`, so `OTEL_SERVICE_NAME` wins over
      `altinn-correspondence`. Drop the unused `Microsoft.ApplicationInsights` (API and Integrations) and
      `Azure.Monitor.OpenTelemetry.AspNetCore` (API) references in the same PR.
  - Why: the pipeline exports only to Azure Monitor with `GeneralSettings:ApplicationInsightsConnectionString`, so a
    local run exports nothing.
  - Size: trivial.
  - Removes: site `src/Altinn.Correspondence.API/Program.cs:66-78` (the OTLP rule, inlined there since task 63) and the OTLP `PackageReference` in
    `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../reviews/otel/correspondence.md`](../reviews/otel/correspondence.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Allow http metadata in Development.** In `src/Altinn.Correspondence.API/Auth/DependencyInjection.cs`,
      `ConfigureAuthentication`, set `RequireHttpsMetadata = !hostEnvironment.IsDevelopment()` on the three JWT bearer
      schemes and the OpenID Connect scheme. In `Auth/IdportenTokenValidator.cs`, `ValidateTokenAsync`, build the
      document retriever from the scheme's options:
      `new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata }`.
  - Why: Authentication, the fake Maskinporten and Dialogporten publish their metadata and JWKS over plain http locally.
    The validator builds its own retriever, which ignores the scheme's setting, so the browser download of an attachment
    looped between Correspondence and Authentication.
  - Size: small.
  - Removes: site `src/Altinn.Correspondence.API/Auth/IdportenTokenValidator.cs:26-31` and the two `ConfigureAll`
    lines of `fakes/Auth/LocaltestAuthentication.cs`.
  - Upstream: none found.
  - Detail: `UPSTREAM.md`, the `IdportenTokenValidator.cs` row; [`../components/correspondence/README.md`](../components/correspondence/README.md),
    "The browser download of attachments".

- [ ] **Make the Maskinporten scheme's metadata address a setting.** `ConfigureAuthentication` hard-codes
      `https://maskinporten.no/...` in Production and `https://test.maskinporten.no/.well-known/oauth-authorization-server`
      elsewhere for `AuthorizationConstants.MaskinportenScheme`. Read it from a setting (for example
      `MaskinportenSettings:MetadataAddress`), defaulting to the current expressions.
  - Why: locally the Maskinporten tokens come from the localtest app's fake Maskinporten, whose metadata is at
    `{origin}localtest/api/maskinporten/.well-known/oauth-authorization-server`; Notifications' condition check presents
    such a token.
  - Size: trivial.
  - Removes: the `MaskinportenScheme` `Configure` in `fakes/Auth/LocaltestAuthentication.cs`.
  - Upstream: none found.
  - Detail: [`../components/correspondence/README.md`](../components/correspondence/README.md), "What is faked".

- [ ] **Let the OIDC cookies follow the request on http.** In `ConfigureAuthentication`, the OpenID Connect scheme sets
      the correlation cookie to `SecurePolicy=Always`, `SameSite=None` (the nonce cookie keeps the framework's same
      defaults), and the "Correlation failed" retry path's `ExpireCookie` hard-codes `Secure = true`,
      `SameSite=None`. Use `SameAsRequest` and `Lax` for both cookies in Development, and build the expiry from the
      scheme's own cookie builders (`context.Options.CorrelationCookie` / `NonceCookie`) so it carries the attributes
      the cookie was set with. `Lax` is enough in every environment (the callback is a top-level GET redirect on the same
      site), which the team may prefer to the Development switch.
  - Why: over http a browser drops a `Secure` cookie and a `SameSite=None` cookie without `Secure`, so the callback
    failed with "Correlation failed" and the expiry on the retry path never applied.
  - Size: small.
  - Removes: the site in `Auth/DependencyInjection.cs` (`ExpireCookie`, which replaced the copy of the shared cookie policy
    in task 63), and the `OpenIdConnectDefaults` `Configure` in
    `fakes/Auth/LocaltestAuthentication.cs`.
  - Upstream: none found. `SameAsRequest` alone outside Development depends on the production ingress passing the scheme
    through; I could not confirm that, so the proposal keeps `Always` there.
  - Detail: [`../reviews/cookies.md`](../reviews/cookies.md), rows 10 to 12.

- [ ] **Fetch the dialog-token keys again on an unknown key id.** `src/Altinn.Correspondence.API/Auth/EdDsaSecurityKeysCacheService.cs`
      loads Dialogporten's JWKS at start and then every 12 hours. Give the Dialogporten scheme an
      `IssuerSigningKeyResolver` that refetches when a token names a `kid` not in the list, rate-limited (localtest uses
      at most once per 10 s), and keeps the previous keys on a failed fetch.
  - Why: a Correspondence that starts before Dialogporten serves its keys refuses every dialog token for 12 hours. The
    same applies in production after a key rotation that lands between two refreshes.
  - Size: small.
  - Removes: `fakes/Auth/LocaltestDialogTokenKeys.cs` and its registration; with the three items above, all of
    `fakes/Auth/`.
  - Upstream: none found.
  - Detail: [`../components/correspondence/README.md`](../components/correspondence/README.md), "What is faked".

- [ ] **Skip the Azure Resource Manager work when Azure is not configured.** `src/Altinn.Correspondence.API/Helpers/RecurringJobRegistration.cs`
      registers the daily "Update IP restrictions to apimIp and current EventGrid IPs" job unconditionally, and
      `IResourceManager` is always `AzureResourceManagerService`. Register the job and the Azure implementation only
      when a setting says so (for example the container app's resource id being set), and a no-op `IResourceManager`
      otherwise.
  - Why: the job fails its ten Hangfire retries on `DefaultAzureCredential` every day; the storage-account deployment is
    already skipped in Development.
  - Size: small.
  - Removes: the site in `RecurringJobRegistration.cs` (it replaced `fakes/AzureResources/` in task 63).
  - Upstream: the draft [altinn-correspondence#2152](https://github.com/Altinn/altinn-correspondence/pull/2152)
    ("Compatibility with Scaleway") adds `NoOpResourceManager` and skips the job, keyed on the environment name
    `ScalewayPoc`; ask for a setting instead of the environment name so localtest can use it.

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

- [ ] **Bump `Altinn.ApiClients.Maskinporten` to 10.2.0.** `src/Altinn.Correspondence.Integrations/Altinn.Correspondence.Integrations.csproj`
      references 10.1.0 (also on upstream `main`). Then set `MaskinportenSettings:TokenEndpoint`, `AssertionAudience`
      and `TokenExchangeEndpoint` in localtest's manifest, and a throwaway JWK as `EncodedJwk` instead of today's `e30=`
      (the package signs the assertion). Correspondence's nine platform clients and KRR take their settings from the
      `MaskinportenSettings` section, so the new keys bind without code changes. Its own `MaskinportenTokenService`
      serves only the JWK rotation, which is off unless enabled.
  - Why: without the bump the package can only call the real Maskinporten, so localtest replaces `IMaskinportenService`.
  - Size: trivial.
  - Removes: `fakes/Maskinporten/` and its `AddLocaltestMaskinporten` call.
  - Upstream: the open Renovate PR
    [altinn-correspondence#1964](https://github.com/Altinn/altinn-correspondence/pull/1964) ("Update All dependencies")
    includes 10.1.0 to 10.2.0. The localtest token endpoint reads the assertion's payload only, not its header or
    signature (`../localtest/Maskinporten/MaskinportenApi.cs`, `TryReadAssertion`), so a signed assertion passes.
  - Detail: [`../components/shared/README.md`](../components/shared/README.md), "`IMaskinportenService` fake"; TODO
    task 15 in [`../../TODO.md`](../../TODO.md).

## Phase 2: structural changes

- [ ] **A supported local blob store.** `src/Altinn.Correspondence.Persistence/Repositories/StorageRepository.cs` uses
      `DefaultAzureCredential` per storage account, with a plain connection-string path "for local testing" for the
      default account. Agree on one local store that the upstream code supports: either that connection-string path
      against Azurite (the SAS URL is built with `SasProtocol.Https`; I could not confirm that Azurite accepts it over
      http) or the S3 repository of the draft #2152 against a local S3 server. Part of "a supported local configuration
      without Key Vault/Azure Blob/queues" ([`README.md#across-services`](README.md#across-services)).
  - Why: there is no Azure Storage locally; the fake keeps attachments and reports on a volume and serves them through
    the inspection route.
  - Size: medium; needs agreement with the team, and a new container in the stack.
  - Removes: `fakes/Storage/` with its `RemoveAll`/`Add` in `fakes/LocaltestCorrespondence.cs`, and the `blobs/{id}` and
    `reports/{name}` calls of `fakes/Inspection/`.
  - Upstream: [altinn-correspondence#2152](https://github.com/Altinn/altinn-correspondence/pull/2152) (draft) adds
    `S3StorageRepository`, selected when `AttachmentStorageOptions:S3Endpoint` is set.
  - Detail: [`../components/correspondence/README.md`](../components/correspondence/README.md), "What is faked".

- [ ] **Service discovery by name, with a base URL per platform service.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder`, as the pattern in the README's "Across services" describes. Split
      `AltinnOptions:PlatformGatewayUrl`: it is the base of the nine platform clients (`RegisterAltinnHttpClient` in
      `Integrations/DependencyInjection.cs:61-69`, and `ResourceRegistryService.cs:26`, which sets its base address in its
      constructor), of the dialog-token JWKS URL (`EdDsaSecurityKeysCacheService.cs:63`), and also the OIDC authority and
      expected issuer. Give each client an optional base URL of its own (defaulting to `PlatformGatewayUrl`), so the
      clients can use `http://register/` and the like while the authority stays public.
  - Why: the stack injects one `services__<name>__http__0` per container, but the authority and issuer must stay the
    public origin, so the nine clients cannot reach the services by name without a per-request rewrite.
  - Size: medium; the split touches the client registration, and the pattern needs agreement across services.
  - Removes: site `src/Altinn.Correspondence.API/Program.cs:44-49`, `fakes/ServiceDiscovery/` (`LocaltestServiceDiscovery.cs`
    and `LocaltestPrefixRouting.cs` with its `AddTo` call) and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md),
    "Correspondence" and section 6.

- [ ] **Run upstream's published image.** `ci-cd.yaml` already pushes the API image to
      `ghcr.io/altinn/altinn-correspondence`, which is public (tags `<version>-<commit>`; the tag list answers an
      anonymous token). Once the items above are merged, localtest pulls that image, moves the defaults of
      `AddLocaltestConfiguration` and `ASPNETCORE_ENVIRONMENT=Development` into the manifest's environment, and runs the
      inspection API out of process (see "Stays local"). Nothing to change upstream.
  - Why: the vendored build exists only to carry the sites and fakes.
  - Size: small for localtest; no upstream change.
  - Removes: sites `src/Altinn.Correspondence.API/Program.cs:56-58` and `Program.cs:179-181`, `fakes/LocaltestCorrespondence.cs`
    with `LocaltestCorrespondenceOptions.cs`, `Dockerfile`, `.dockerignore`, `localtest.build.props`,
    `localtest.build.targets` and the vendored copy itself.
  - Upstream: nothing needed; the image is published.

## Stays local

- `fakes/Inspection/`: the anonymous, read-only `correspondence/api/localtest/` (correspondences with their Hangfire jobs,
  the job list, migration status) is for tests only. To run upstream's image unchanged it moves to its own container
  reading the `correspondence` database; until then it keeps the hook at `Program.cs:179-181`.
- `postgres-init/70-correspondence.sh`: the role and database on the shared Postgres; production's infrastructure
  provisions them.
- `Dockerfile`: upstream's floating `sdk:10.0` and `aspnet:10.0` pinned by digest: localtest's reproducible builds;
  gone with the vendored copy.
- `Dockerfile`: `COPY` of the overrides and `fakes/`, `DirectoryBuildPropsPath` and `DirectoryBuildTargetsPath`: the
  mechanism that compiles the sites and fakes; gone with the vendored copy.
- `Dockerfile`: the `vendor/` prefix on the restore, copy and publish paths: the vendoring layout.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` (migrations at start, the simulated malware scan, no storage-account
  deployment): a deployment setting, which moves to the manifest with a published image.
- `.dockerignore`: keeps host `bin/` and `obj/` out of the build context: the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the `CS1591` `NoWarn` for the undocumented fakes
  and the vendored-project test: the mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into `Altinn.Correspondence.API`: as above.

Coverage: 7 sites (another removed in task 58's follow-up), 6 fakes (`Auth/`,
`Inspection/`, `Maskinporten/`, `ServiceDiscovery/`, `Storage/`, the hook `LocaltestCorrespondence.cs` with
its options), 2 build overrides, 4 Dockerfile and 1 `.dockerignore` deviations, 1 Postgres init script: all accounted
for.
