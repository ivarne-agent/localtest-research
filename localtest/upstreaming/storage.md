# Storage: upstreaming

- Upstream: [Altinn/altinn-storage](https://github.com/Altinn/altinn-storage)
- Pin: `9e72a9f` (main, 2026-10-19), [`../components/storage/vendor/UPSTREAM.md`](../components/storage/vendor/UPSTREAM.md)
- Group: none
- Reminders: none.
- Localtest modifications: 5 directive sites (4 in `src/Storage/Program.cs`, 1 in
  `src/Storage/Controllers/InstancesController.cs`), the fakes `Auth/`, `Blobs/`, `Dialogporten/`, `FileScan/`, the
  composition `LocaltestStorageExtensions.cs` and `LocaltestStorageOptions.cs`, the shared copies `ServiceDiscovery/`
  and `Otlp/`, two build overrides, the Postgres init script `postgres-init/10-storage.sh`, and the `localtest:`
  deviations in `Dockerfile` and `.dockerignore`.

Site lines are those of the site table in `UPSTREAM.md`; paths in "Removes" are relative to
`../components/storage/` (sites relative to its `vendor/`).

## Phase 1: easy fixes

- [ ] **Export over OTLP when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Add `OpenTelemetry.Exporter.OpenTelemetryProtocol`
      (at the host's OpenTelemetry version, 1.19.1) to `Altinn.Platform.Storage.csproj` and call `UseOtlpExporter()` in
      `ConfigureServices` when the variable is set, next to the Azure Monitor block; add the environment resource
      detector after `AddAttributes` so `OTEL_SERVICE_NAME` wins over `platform-storage`.
  - Why: without it, Storage sends telemetry only to Azure Monitor with a connection string read from Key Vault, so a
    local run exports nothing.
  - Size: trivial.
  - Removes: site `src/Storage/Program.cs:248-257`, `fakes/Otlp/` and the OTLP `PackageReference` in
    `localtest.build.targets`. The `CS8321` `NoWarn` in `localtest.build.props` also covers the Key Vault function
    under site `Program.cs:142-146`, so it goes with the image item below.
  - Upstream: none found. altinn-storage#1148 (merged before our pin) stopped the Azure Monitor exporter from sampling;
    it does not add OTLP.
  - Detail: [`../reviews/otel/storage.md`](../reviews/otel/storage.md). Carry the sampler note from
    [`../reviews/otel/README.md`](../reviews/otel/README.md) into the PR.

- [ ] **Configurable base for the paging `next` link.** `InstancesController.BuildRequestLink` builds the `next` link of
      paged instance queries as `https://platform.{GeneralSettings:Hostname}{path}{query}`. Read the origin from a
      setting (for example `GeneralSettings:PlatformBaseUrl`, defaulting to the current expression) instead of
      hard-coding the scheme and the `platform.` prefix. The self-link item below can use the same setting.
  - Why: apps follow `next` when a party has more than one page of instances; locally there is no `platform.` host and
    no TLS, so the link does not resolve.
  - Size: small.
  - Removes: site `src/Storage/Controllers/InstancesController.cs:1465-1471`.
  - Upstream: none found. Localtest uses the request's own scheme and host; that depends on the production ingress
    passing the browser's `Host` and scheme through, which I could not confirm, so the setting is the safer proposal.
  - Detail: [`../components/storage/README.md`](../components/storage/README.md), "Directive sites in `vendor/`", 3.

- [ ] **Make `infra/postgres_init.sql` independent of the superuser's name.** The script assumes it runs as the
      container superuser named `platform_storage_admin` (upstream's `docker-compose.yml` sets `POSTGRES_USER` to it).
      Create `platform_storage_admin` when it is missing (a `DO` block that ignores `duplicate_object`) and make it the
      owner of `storagedb` and the `storage` schema.
  - Why: the stack has one shared Postgres whose superuser is not `platform_storage_admin`, so the vendored script
    alone leaves the database and schema owned by the wrong role and the migrations fail.
  - Size: small; development tooling only, production does not run the script.
  - Removes: nothing here. Localtest sets up its own users and databases (TODO.md task 60): `postgres-init/10-storage.sh`
    no longer runs the script (watched in `UPSTREAM.md`, not copied); the fix is for upstream's own local setups.
  - Upstream: none found.
  - Detail: [`../components/storage/README.md`](../components/storage/README.md), "Postgres".

## Phase 2: structural changes

- [ ] **Storage's self-link base.** `SetPlatformSelfLinks` in `Altinn.Platform.Storage.Interface`
      (`src/Storage.Interface/Models/Instance.cs:104-111`, `DataElement.cs:159-167`) prepends a hard-coded
      `https://platform.` to the `storageHostAndBase` its callers pass (`{GeneralSettings:Hostname}/storage/api/v1/`,
      built in `InstancesController`, `DataController`, `ProcessController` and `InstanceMutationsController`). Add an
      overload that takes the whole base URL, and pass it from the configurable base of the phase 1 item, defaulting to
      today's `https://platform.{Hostname}/storage/api/v1/`.
  - Why: every stored and returned `selfLinks.platform` says `https://platform.{host}/storage/...`, which neither
    resolves nor serves TLS locally, so every consumer that follows one has to rewrite it.
  - Size: small in code; phase 2 because `Storage.Interface` is a published NuGet package (Storage itself references
    `5.0.0-rc.3`) used by app-lib, Receipt and the Dialogporten adapter, so it needs a package release and agreement on
    the signature.
  - Removes: nothing in Storage: the links keep production's format, with `GeneralSettings:Hostname` derived from the
    platform URL in `AddLocaltestConfiguration` (that default goes with the image item). In other components:
    Receipt's `fakes/SelfLinksStorage.cs` with its hook and site (see [`receipt.md`](receipt.md); its frontend item is
    the other way), and the Dialogporten adapter's sites `StorageDialogportenDataMerger.cs:366-371` and `:398-403` with
    `fakes/LocaltestLinks.cs` (see [`dialogporten-adapter.md`](dialogporten-adapter.md)).
  - Upstream: none found. Note for the PR: app-lib's integration tests have 58 snapshots with
    `https://platform.local.altinn.cloud/…`; with the default unchanged they still match.
  - Detail: [`../../planned-components/receipt/receipt.md`](../../planned-components/receipt/receipt.md),
    [`../../planned-components/dialogporten/dialogporten-adapter.md`](../../planned-components/dialogporten/dialogporten-adapter.md),
    [`../../planned-components/studioctl-integration.md`](../../planned-components/studioctl-integration.md).

- [ ] **Service discovery by name.** Register `AddServiceDiscovery()` and
      `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())` (package `Microsoft.Extensions.ServiceDiscovery`)
      after `WebApplication.CreateBuilder`, so `PlatformSettings:ApiAuthorizationEndpoint`,
      `RegisterServiceSettings:ApiRegisterEndpoint` and `GeneralSettings:OndemandEndpoint` such as
      `http://authorization/authorization/api/v1/` resolve through `services__<name>__http__0`. Storage is not one of
      the `AltinnServiceDefaults` hosts, so it needs the registration; agree on the pattern in the README's
      [Across services](README.md#across-services).
  - Why: the stack injects one `services__<name>__http__0` per container; without service discovery every URL has to be
    a hand-written host name.
  - Size: small in code; needs agreement on the shared pattern.
  - Removes: site `src/Storage/Program.cs:58-63`, `fakes/ServiceDiscovery/` and the `Microsoft.Extensions.ServiceDiscovery`
    `PackageReference` in `localtest.build.targets`.
  - Upstream: none found.
  - Detail: [`../../planned-components/service-discovery.md`](../../planned-components/service-discovery.md), "Storage".

- [ ] **Blob storage without Azure.** Part of the cross-cutting "supported local configuration without Key Vault,
      Azure Blob and queues" ([Across services](README.md#across-services)). Upstream already has a development path:
      `BlobRepository` uses `AzureStorageConfiguration:BlobEndPoint` with a shared key when `AccountName` is
      `devstoreaccount1`, and `docker-compose.yml` starts Azurite. Two ways: localtest runs Azurite against that path
      (no upstream change, one more container), or upstream accepts a file-system `IBlobRepository` selected by a
      setting (our `FileBlobRepository`, which keeps the Azure container layout).
  - Why: production writes to per-org Azure storage accounts with `DefaultAzureCredential`.
  - Size: medium; a choice for the team (and for localtest, which prefers no extra container).
  - Removes: `fakes/Blobs/`; the Dockerfile's `/data/blobs` folder (`RUN mkdir -p /data/blobs`) and, from the last
    `localtest:` block, the `COPY --chown` of `/data/blobs` and `Localtest__Storage__BlobRootPath`.
  - Upstream: none found. I could not confirm that the Azurite path works today: it signs with
    `AzureStorageConfiguration:OrgStorageAccount` (`{0}altinndevstrg01`, unformatted) as the account name, and I did not
    find code that creates the per-org containers.
  - Detail: [`../components/storage/README.md`](../components/storage/README.md), "Fakes".

- [ ] **File scan without the queue.** Part of the cross-cutting local configuration. Production enqueues a scan
      request on an Azure Storage queue (`FileScanQueueClient`) and the external file-scan service later calls
      `PUT dataelements/{id}/filescanstatus`. Offer a supported local mode, for example a setting under which the scan
      request marks the element `Clean` at once through `IDataRepository.UpdateFileScanStatus`, as our fake does.
  - Why: with Azurite the request is queued but nothing consumes it, so every element stays `Pending`.
  - Size: small in code; needs the team's agreement that such a mode belongs in production code.
  - Removes: `fakes/FileScan/`.
  - Upstream: none found. Running `Altinn/altinn-file-scan` locally is the alternative; I could not confirm what it needs.

- [ ] **Outbox to the Dialogporten adapter without Service Bus.** Part of the cross-cutting local configuration.
      `ConfigureWolverine` picks the Azure Service Bus transport by `!IsDevelopment()`; in `Development` the
      `SyncInstanceToDialogportenCommand` goes to an in-process local queue that nothing handles. Choose the transport
      by configuration instead (Service Bus when `WolverineSettings:ServiceBusConnectionString` is set), so a local
      Storage can use the Service Bus emulator together with the adapter, or agree with both teams on an HTTP hand-off
      to the adapter.
  - Why: locally the outbox's commands never reach the adapter.
  - Size: medium; involves the Storage and Dialogporten adapter teams. The emulator needs a SQL Server container
    besides.
  - Removes: `fakes/Dialogporten/` (and, in the adapter, its `api/localtest/enqueue` route, see
    [`dialogporten-adapter.md`](dialogporten-adapter.md)).
  - Upstream: none found.
  - Detail: [`../components/storage/README.md`](../components/storage/README.md), "Dialogporten outbox".

- [ ] **Signing keys without Key Vault.** Part of the cross-cutting local configuration. `PublicSigningKeyProvider` from
      `Altinn.Common.AccessToken` (repository `Altinn/altinn-accesstoken`) requires a Key Vault URI. Let it read the
      public certificates from files when configured, so a local Storage validates platform access tokens with the
      shared test certificate. The same fake is in Authentication, Authorization, Access Management, Events,
      Notifications, Profile, Register and Resource Registry.
  - Why: without a vault URI the provider throws, and every platform access token is rejected.
  - Size: small in the package, then a package bump in each service.
  - Removes: `fakes/Auth/`.
  - Upstream: [altinn-accesstoken#136](https://github.com/Altinn/altinn-accesstoken/issues/136) (open), which names
    Storage. Events' open [altinn-events#1149](https://github.com/Altinn/altinn-events/pull/1149) shows the other way,
    an emulated Key Vault (Lowkey Vault) with no code change.

- [ ] **Publish the image where localtest can pull it.** No GitHub workflow in the repository builds or pushes the
      image (they build, test and push the `Storage.Interface` NuGet package); I could not confirm where the
      production image is built. Also push the same image to a public registry (for example
      `ghcr.io/altinn/altinn-storage`), tagged by commit.
  - Why: once the items above are merged, upstream's image runs locally unchanged, but localtest cannot pull it.
  - Size: small in code; a release and ownership decision for the team.
  - Removes: `Dockerfile`, `.dockerignore`, `localtest.build.props` (with the `CS8321` `NoWarn`), `localtest.build.targets`
    and the vendored copy itself. With the four local-mode items done, site `src/Storage/Program.cs:77-79`,
    `LocaltestStorageExtensions.cs` and `LocaltestStorageOptions.cs` have nothing left to register, and site
    `Program.cs:142-146` goes: upstream already skips Key Vault when `kvSetting:SecretUri` is empty, so its
    configuration defaults (endpoints, `Hostname`, cache times, Wolverine delays, `DisableA2Endpoints`) become
    environment variables in the manifest.
  - Upstream: none found.

## Stays local

- `Dockerfile`: `COPY vendor/...` paths, the `/src` layout and the `DirectoryBuildPropsPath` /
  `DirectoryBuildTargetsPath` variables (the first two `localtest:` blocks; upstream's commented-out local-development
  lines are left out with them): the vendoring layout; gone with the vendored copy.
- `Dockerfile`: `ASPNETCORE_ENVIRONMENT=Development` and `ASPNETCORE_URLS=http://+:5010`: deployment settings, which
  upstream's chart supplies; with a published image they move to the manifest. Development already allows JWT
  metadata over http and Wolverine's local queues.
- `.dockerignore`: the component's own (`bin`, `obj`, `.vs`, `Dockerfile`): the vendoring layout.
- `localtest.build.props`: `LOCALTEST`, `TreatWarningsAsErrors=false`, the CSharpier bypass, `CS1591` and the
  vendored-project test: the mechanism that carries the sites; gone with the last one.
- `localtest.build.targets`: compiling `fakes/**/*.cs` into the host: as above.
- `postgres-init/10-storage.sh`: the roles, `storagedb`, its schema and grants on the shared Postgres (what upstream's
  `infra/postgres_init.sql` does), with the passwords from the manifest: users and databases are set up by localtest
  (TODO.md task 60), as a deployment does outside the image.

Coverage: 5 sites, 8 fakes (`Auth/`, `Blobs/`, `Dialogporten/`, `FileScan/`, `Otlp/`, `ServiceDiscovery/`,
`LocaltestStorageExtensions.cs`, `LocaltestStorageOptions.cs`), 2 build overrides, 4 Dockerfile and 1 `.dockerignore`
deviations (the fourth Dockerfile block split between the blob item and "Stays local"), 1 Postgres init script: all
accounted for.
