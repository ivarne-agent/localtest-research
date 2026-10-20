# Vendoring plan: Dialogporten adapter (`localtest/components/dialogporten-adapter/`) and the Storage change that feeds it

Research date: 2026-10-20. Scope: `Altinn/altinn-dialogporten-adapter` as a vendored component in the model of
[`../mvp-vendoring-model.md`](../mvp-vendoring-model.md), plus the smallest Storage-side change that delivers
`SyncInstanceCommand`s to it without Azure Service Bus. Dialogporten itself, the Resource Registry, Arbeidsflate and
the shared prerequisites are planned by the sibling reports in this folder
([`dialogporten-core.md`](dialogporten-core.md), [`resource-registry.md`](resource-registry.md),
[`shared-prerequisites.md`](shared-prerequisites.md)); section 13 states what this strand needs from them.

Verified today, beyond reading: a scratch build of the WebApi project with the planned `localtest.build.props` /
`.targets`, SDK 10.0.401 (the image's SDK), all planned directive sites applied and the fakes compiled in (0 warnings,
0 errors); the built adapter started with localtest-shaped settings and, against the running stack's Authentication,
answered `/health` 200, the planned enqueue endpoint 401 without a token, 403 with a user token, 202 with an org
token from `/Home/GetTestOrgToken` carrying `altinn:storage/instances.syncadapter`, and the accepted command ran
through Wolverine's local queue into the production handler and sync service (which then called Storage and
Dialogporten). Scratch output is deleted; the applied directive hunks are kept as
[`adapter-prototype/directive-sites.patch`](adapter-prototype/directive-sites.patch) (with the three scratch fakes beside it) for the implementer. Everything else marked "not verified".

## Pins

| Repo | Commit | What was read |
| --- | --- | --- |
| [`Altinn/altinn-dialogporten-adapter`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/0a329856ba79101acdf54c29fd84a5063189a42d) | `0a32985` (main, 2026-10-08); origin/main `86364b8` (3 commits ahead, section 14) | Whole `src/`, `Dockerfile`, `Directory.Build.props`, `global.json`, `README.md` |
| [`Altinn/altinn-storage`](https://github.com/Altinn/altinn-storage/tree/9e72a9f128debd7b9815b615d842a2d1a6d343c5) | `9e72a9f` = the commit vendored in [`localtest/components/storage/vendor`](../../localtest/components/storage/vendor/UPSTREAM.md) | `Program.cs`, outbox (`OutboxService`, `PgOutboxRepository`, `OutboxInsertRowFactory`, `OutboxEventSyncPolicy`, `WolverineSettings`), `StorageAccessHandler`, `ProcessAuthorizer`, `AuthorizationService`, the controllers the adapter calls; diffed against the vendored copy (identical except the two `LOCALTEST` sites) |
| [`Altinn/altinn-register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) | `f6b5c60` = vendored pin | `RegisterHost.cs` policies, `DialogPortenController`, `V2/PartyController.Query`, `PartyUrn` |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810` = vendored Authentication pin | `AuthenticationController.ExchangeExternalSystemToken` (what the exchanged token carries), `OrganisationsService` |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` | `src/WebApiClient/*` (the `Altinn.ApiClients.Dialogporten` package the adapter uses at 1.118.0; HEAD is newer, see Not verified), `ClaimsPrincipalExtensions` |
| [`Altinn/altinn-apiclient-maskinporten`](https://github.com/Altinn/altinn-apiclient-maskinporten/tree/15d3c63a1f320816acccefd1f221d936ccb286db) | `15d3c63` | `IMaskinportenService`, `MaskinportenService`, `MaskinportenTokenHandler`, DI registration |
| [`Altinn/altinn-studio`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) | `447e53e` | `urlHelper.ts`, `PlatformFrontendSettings.cs`, grep of app-lib v9 for dialog code |
| `Altinn/app-lib-dotnet` | `4740bb2` (clone at `/home/agent/code/app-lib-dotnet`) | grep for dialog code only |
| Localtest stack | worktree `research/dialogporten-vendoring` as of 2026-10-20 | `localtest/components/storage` (fakes, README, Dockerfile, props/targets), `localtest/localtest` (proxy routes, Maskinporten, Home), `components/register` (fakes, README), `components/authentication/README.md`, `components/events` (layout reference), `docker-compose.yml`, `run-app.sh`, `smoke-test.sh` |

## Summary

- Vendor two projects (`src/Altinn.DialogportenAdapter.WebApi`, `src/Altinn.DialogportenAdapter.Contracts`, 7 700
  lines incl. the EventSimulator, which is not built) plus `Directory.Build.props` and `global.json`. The build chain
  is simple: no central package management, no custom analyzers beyond `AnalysisMode=Recommended` with
  `TreatWarningsAsErrors`, which the props file relaxes. The upstream Dockerfile leaves `global.json` out of the build
  context (it pins `10.0.400` with `rollForward: disable` while the image carries `10.0.401`); the component
  Dockerfile does the same. Verified with a scratch build on SDK 10.0.401: 0 warnings.
- **Message path.** The vendored Storage already does everything up to the bus: with `WolverineSettings:EnableSending=true`
  every instance mutation writes an outbox row, `OutboxService` polls that table and `PublishAsync`es a
  `SyncInstanceToDialogportenCommand`, and in `Development` (which the storage container runs with) Wolverine routes
  it to an in-process sequential local queue. Today nothing handles it, because the storage fakes force
  `EnableSending=false`. The smallest change is in `components/storage/fakes/` only, no directive: stop forcing the
  flag off when an adapter URL is configured, and add one Wolverine handler (discovered because the fakes are
  compiled into the host) that POSTs the command to the adapter. On the adapter side the ASB bootstrap has to go
  behind `#if !LOCALTEST` anyway (it throws on an empty connection string); the `#else` is a local queue, and a
  localtest-only `POST storage/dialogporten/api/localtest/enqueue` publishes to it, so the production handler and the
  production retry/error policies (409, 410, 412, 422, party not found, 5xx) run unchanged. Verified end to end in the
  scratch run. Alternative (a), POSTing to the production `syncDialog` endpoint, needs no extra route but makes the
  Storage handler wait for the whole sync and own the retries; it stays available for manual re-sync. A Service Bus
  emulator (c) is rejected: Azure images plus MSSQL for a queue the local stack does not need.
- **Storage endpoints and auth.** The adapter reads `instances/{p}/{g}`, `.../events`, `applications/{org}/{app}`,
  `.../texts/{lang}`, writes `.../datavalues` and deletes through `sbl/instances/{p}/{g}`. Storage has a built-in
  bypass for the scope `altinn:storage/instances.syncadapter` (`GeneralSettings:InstanceSyncAdapterScope`) on exactly
  these paths, so no PDP decision is involved; the vendored Storage serves them with that scope. The adapter's token
  is a Maskinporten token exchanged to an Altinn token (`ExhangeToAltinnToken: true`), which the vendored
  Authentication's production exchange issues from the fake Maskinporten's token with all original claims (`scope`,
  `consumer`) kept plus `urn:altinn:org`; `/Home/GetTestOrgToken` already mints exactly that, decoded today.
- **Adapter directive sites:** four, all small: the ASB block and the two ASB listeners in
  `ServiceCollectionExtensions.AddInfrastructure`; `GetAppUriForOrg`/`GetPlatformUri` in `Settings.cs` (production
  needs `{org}.apps.{host}` and `platform.{host}` sub-domains, the stack has one origin); two one-line
  `selfLinks.platform` reads in the merger (Storage hard-codes `https://platform.{host}` in the Interface package);
  three pure-addition hooks in `Program.cs` (configuration defaults, DI, routes). Fakes: configuration defaults,
  `Configure<JwtBearerOptions>` for http metadata (`PostConfigureAll` is too late: the framework's post-configure
  throws first; found in the scratch run), the shared `IMaskinportenService` fake, the enqueue endpoint, the
  self-link rewrite. About 200 lines.
- **Register** serves `register/api/v1/dialogporten/parties/query` in the vendored production code with policy
  `InternalOrPlatformAccess` = scope `altinn:register/partylookup.admin` or a platform access token; the adapter's
  scope list carries it; the in-memory persistence resolves party-id and user-id URNs.
- **Altinn CDN**: nothing local today; Authentication fetches `altinncdn.no` from the internet. The adapter's base
  URL is configurable, so it joins the `localtest/api/cdn/orgs/altinn-orgs.json` route the shared-prerequisites
  report plans (section 6 there). No adapter code.
- Effort: 3 to 5 days once Dialogporten, the Resource Registry and the shared Maskinporten fake exist. Risk medium,
  concentrated in runtime behaviour not reproducible in the scratch (the Storage handler under Wolverine, dialog-token
  validation, attachments through `authentication?goto=`).

## 0. Corrections to the earlier research ([`dialogporten-arbeidsflate.md`](../../localtest-components/dialogporten-arbeidsflate.md), 2026-10-18)

| Earlier | Now |
| --- | --- |
| Storage pinned at `1c59473` | The vendored Storage is `9e72a9f` (2026-10-19). Line numbers in the outbox code moved; everything cited below is at `9e72a9f`. The `Program.cs` Wolverine block is the same code |
| "No directive in Storage, configuration and one fake handler" | Still true, but the storage fakes **force** `WolverineSettings:EnableSending=false` over the environment ([`LocaltestStorageExtensions.cs`](../../localtest/components/storage/fakes/LocaltestStorageExtensions.cs) `AddLocaltestConfiguration`), so compose cannot turn it on; the fake itself has to change (section 2.3) |
| Storage fake handler "gets an Altinn token with `altinn:storage/instances.syncadapter` (P2)" | Storage has no Maskinporten client package; the handler should not drag the shared `IMaskinportenService` fake in. It asks the localtest app's `/Home/GetTestOrgToken` (fake mint plus production exchange, exists today, verified) and caches the token (section 2.3) |
| Handler POSTs to the production `syncDialog` | Recommended target is the localtest enqueue endpoint on Wolverine's local queue, so the adapter's production error policies apply (section 2.2); `syncDialog` is the fallback and the manual re-sync tool |
| Adapter hook: "`RequireHttpsMetadata=false` on the JWT bearer" via `PostConfigure` | Must be `Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, …)`: the framework's `JwtBearerPostConfigureOptions` runs before any later post-configure and throws on an `http` metadata address. Verified by failure, then success, in the scratch run |
| "Attachments in the inbox will not open without a fix there [Storage]" | Fixed on the adapter side with two one-line `#else`s in the merger (section 3.1, site 4); Storage stays untouched |
| Maskinporten token endpoint under `authentication/api/v1/localtest/maskinporten/token` | Moved to the localtest app: `localtest/api/maskinporten/token` ([`shared-prerequisites.md`](shared-prerequisites.md) section 1) |
| Ingress "`/storage/dialogporten/` → adapter (before the `/storage/` rule)" in nginx | The YARP proxy in `localtest/localtest/Proxy/PlatformRoutes.cs`; the prefix table is an ordered array, first match wins (section 7.2) |
| P6 "Static `orgs/altinn-orgs.json` served by the ingress" | Served by the localtest app under `localtest/api/cdn/` (shared-prerequisites section 6); the adapter only needs its base URL |

## 1. The component

### 1.1 Paths to vendor

```
localtest/components/dialogporten-adapter/
  Dockerfile                      # mirrors vendor/Dockerfile (section 1.4)
  localtest.build.props           # section 1.3
  localtest.build.targets
  README.md
  smoke-test.sh                   # section 9
  fakes/                          # section 3.2, compiled into the WebApi host
  vendor/
    UPSTREAM.md
    Directory.Build.props         # upstream root props (TFM, nullable, analyzers, warnings as errors)
    global.json                   # kept for reference; NOT copied into the image (section 1.2)
    Dockerfile                    # upstream, reference only
    src/Altinn.DialogportenAdapter.WebApi/      # the host, 5 800 lines
    src/Altinn.DialogportenAdapter.Contracts/   # SyncInstanceCommand, queue names, Wolverine ASB defaults (referenced by relative path)
```

`UPSTREAM.md` follows the Storage one: repository, commit, copied paths (`src/Altinn.DialogportenAdapter.WebApi`,
`src/Altinn.DialogportenAdapter.Contracts`, `Directory.Build.props`, `global.json`, `Dockerfile`, `.dockerignore`),
copied with `git archive` (no subtree yet, as the other components), the sync recipe (`git diff --stat <pin> <new> --
src/Altinn.DialogportenAdapter.WebApi src/Altinn.DialogportenAdapter.Contracts Directory.Build.props global.json Dockerfile`,
three-way merge for the four files with `LOCALTEST` sites, `diff -rq` against a fresh archive, carry Dockerfile
changes over). Not vendored: `src/Altinn.DialogportenAdapter.EventSimulator` (Azure Table-backed migration tool, its
own Dockerfile), `tests/`, `AzureServiceBusEmulator/`, `docker-compose-service-bus.yml`, `.github/`. Nothing else in
the repository is referenced by the WebApi project: the csproj's only `ProjectReference` is Contracts
([`Altinn.DialogportenAdapter.WebApi.csproj:17-19`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj#L17-L19)).

Pin: re-pin to origin/main `86364b8` rather than `0a32985` before vendoring; the three commits since are package and
image bumps plus two telemetry lines (section 14), none in a directive site.

### 1.2 Build chain facts (read, and verified by the scratch build)

| Item | Upstream | Localtest |
| --- | --- | --- |
| SDK | [`global.json`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/global.json#L2-L4): `10.0.400`, `rollForward: disable`. The [`Dockerfile`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/Dockerfile#L1) builds on `sdk:10.0.401` and copies only `Directory.Build.props` and `src` ([L4-L15](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/Dockerfile#L4-L15)), so the pin never applies in the image (it would fail: `disable` needs the exact version) | Same: keep `global.json` in `vendor/` for the record, do not `COPY` it. On this machine 10.0.401 is installed; the scratch build used it by writing a `global.json` with `10.0.401` next to the overrides (a convenience for developer builds outside the image; optional) |
| Root props | [`Directory.Build.props`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/Directory.Build.props#L3-L16): `net10.0`, implicit usings, nullable, `UserSecretsId`; `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild`, `AnalysisMode=Recommended`, `EnableNETAnalyzers`, `AnalysisLevel=10` | Imported by the props file (it is in the image, unlike Storage's); `TreatWarningsAsErrors=false` layered on top. The fakes compile under `Recommended` + `EnforceCodeStyleInBuild` with 0 warnings as written; keep them warning-free rather than widening `NoWarn` |
| Central package management, `Directory.Build.targets`, `.editorconfig`, rulesets, CSharpier | none | the `Import` of a vendored `.targets` in the targets file is guarded and inert |
| Packages (WebApi) | [`csproj:4-15`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj#L4-L15): `Altinn.ApiClients.Dialogporten` 1.118.0, `Altinn.ApiClients.Maskinporten` 10.1.0, `Altinn.Platform.Storage.Interface` 4.7.1, `Azure.Identity`, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Monitor.OpenTelemetry.AspNetCore`, JwtBearer, OpenApi, Refit, `DeterministicGuids`, `WolverineFx.AzureServiceBus` 5.40.1, FusionCache | Nothing removed. The Azure packages are inert: Key Vault only with all four `kvSetting` values ([`ConfigurationExtensions.cs:20-33`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ConfigurationExtensions.cs#L20-L33)), Azure Monitor only when `ApplicationInsights:Enabled` ([`ServiceCollectionExtensions.cs:55-59`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L55-L59), which throws on an empty connection string, so the flag must be `false`), ASB only through the code behind `#if !LOCALTEST` |
| Packages (Contracts) | [`csproj:4-5`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.Contracts/Altinn.DialogportenAdapter.Contracts.csproj#L4-L5): `AsyncKeyedLock`, `WolverineFx.AzureServiceBus` | unchanged; `WolverineOptionsExtentions.ConfigureAdapterDefaults` compiles but is not called under `LOCALTEST` |
| Restore | nuget.org only | restored here in about a second with a warm cache |
| Build | `dotnet build -c Release -o out` (not `publish`) in the upstream Dockerfile ([L15](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/Dockerfile#L15)) | mirror it |

Scratch build result (SDK 10.0.401, `Release`): both projects, 0 warnings, 0 errors, 2.6 s incremental; `LOCALTEST`
reached the compiled fakes (checked by decompiling a probe constant) while the SDK's and the csproj's own constants
stayed intact, as the model's mechanism promises.

### 1.3 `localtest.build.props` / `localtest.build.targets`

Same shape as [`components/storage/localtest.build.props`](../../localtest/components/storage/localtest.build.props)
and the events component; this is the content that was built:

```xml
<!-- localtest.build.props -->
<Project>
  <PropertyGroup>
    <LocaltestIsVendoredProject>$([System.String]::new('$(MSBuildProjectDirectory)').StartsWith('$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)vendor'))'))</LocaltestIsVendoredProject>
    <!-- vendor/Directory.Build.props is in the image (upstream's Dockerfile copies it too). -->
    <LocaltestVendoredBuildProps Condition="'$(LocaltestIsVendoredProject)' == 'true'">$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildProjectDirectory)'))</LocaltestVendoredBuildProps>
  </PropertyGroup>
  <Import Project="$(LocaltestVendoredBuildProps)" Condition="'$(LocaltestVendoredBuildProps)' != '' And Exists('$(LocaltestVendoredBuildProps)')" />
  <PropertyGroup Condition="'$(LocaltestIsVendoredProject)' == 'true'">
    <DefineConstants>$(DefineConstants);LOCALTEST</DefineConstants>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

```xml
<!-- localtest.build.targets -->
<Project>
  <PropertyGroup>
    <LocaltestVendoredBuildTargets Condition="'$(LocaltestIsVendoredProject)' == 'true'">$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildProjectDirectory)'))</LocaltestVendoredBuildTargets>
  </PropertyGroup>
  <Import Project="$(LocaltestVendoredBuildTargets)" Condition="'$(LocaltestVendoredBuildTargets)' != '' And Exists('$(LocaltestVendoredBuildTargets)')" />
  <!-- The fakes use the host's internal types (Settings, IDialogportenApi registrations, the localtest routes), so
       they are compiled into the host, as the storage and events components do. -->
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Altinn.DialogportenAdapter.WebApi'">
    <Compile Include="$(MSBuildThisFileDirectory)fakes/**/*.cs" LinkBase="Localtest" />
    <!-- The shared Maskinporten fake (shared-prerequisites section 2.3), copied into /src/shared by the Dockerfile. -->
    <Compile Include="$(MSBuildThisFileDirectory)shared/fakes/Maskinporten/*.cs" LinkBase="Localtest/Shared/Maskinporten" />
  </ItemGroup>
</Project>
```

No `Compile Remove`, no `PackageReference Remove`: every Azure path is already switched by configuration or sits
behind the directives. The `shared/fakes` line follows the shared-prerequisites contract (compose
`additional_contexts: shared: ./components/shared`); the scratch build used a local stub with the same interface
instead.

### 1.4 Dockerfile to mirror

Upstream [`Dockerfile`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/Dockerfile):
`sdk:10.0.401@sha256:e70cdb7f…` build stage, `COPY Directory.Build.props`, csproj copies and `dotnet restore`, `COPY
src`, `dotnet build -c Release -o out`; final `aspnet:10.0.12@sha256:222759b3…` (10.0.13 at origin/main), `EXPOSE
5011`, user `dotnet` (uid/gid 3000, `/bin/false`), `RUN mkdir /tmp/logtelemetry`, `ENTRYPOINT ["dotnet",
"Altinn.DialogportenAdapter.WebApi.dll"]`. Mirror line for line; deviations marked `localtest:`:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
# localtest: the component layout under /src (vendor/ beside the overrides, which decide "vendored" by that path);
# global.json stays out, as upstream's build context leaves it out.
WORKDIR /src
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
# localtest: the fakes shared by the Maskinporten clients, from the compose additional context "shared".
COPY --from=shared fakes/ shared/fakes/
COPY vendor/Directory.Build.props vendor/
COPY vendor/src/Altinn.DialogportenAdapter.WebApi/*.csproj vendor/src/Altinn.DialogportenAdapter.WebApi/
COPY vendor/src/Altinn.DialogportenAdapter.Contracts/*.csproj vendor/src/Altinn.DialogportenAdapter.Contracts/
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet restore vendor/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj
COPY vendor/src/Altinn.DialogportenAdapter.WebApi vendor/src/Altinn.DialogportenAdapter.WebApi
COPY vendor/src/Altinn.DialogportenAdapter.Contracts vendor/src/Altinn.DialogportenAdapter.Contracts
RUN dotnet build -c Release -o out vendor/src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4 AS final
WORKDIR /app
EXPOSE 5011
COPY --from=build /src/out .
RUN groupadd --gid 3000 dotnet && useradd --uid 3000 --gid dotnet --no-create-home --shell /bin/false dotnet
USER dotnet
RUN mkdir /tmp/logtelemetry
# localtest: settings production gets from its deployment. Nothing in the adapter keys off Development except the
# optional appsettings.local.json and the LocalDevelopment section, both absent here (section 3.3).
ENV ASPNETCORE_ENVIRONMENT=Development \
    ASPNETCORE_URLS=http://+:5011
ENTRYPOINT [ "dotnet", "Altinn.DialogportenAdapter.WebApi.dll" ]
```

Upstream copies `src` wholesale (the EventSimulator included; it is not built, only present); copying the two
projects is a deviation worth marking, or copy `vendor/src` whole to stay closer. No `HEALTHCHECK` upstream. The
image was not built here (podman build is not allowed on the shared machine).

## 2. The message path from Storage to the adapter

### 2.1 What the vendored Storage does, verified in `vendor/` at `9e72a9f`

| Step | Code | Localtest state today |
| --- | --- | --- |
| Every instance mutation with instance events builds an outbox row inside the same SQL transaction; `OutboxInsertRowFactory.TryBuild` returns null when `WolverineSettings:EnableSending` is false | [`PgInstanceMutationRepository.BuildOutboxPayload`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/PgInstanceMutationRepository.cs#L532-L556), [`OutboxInsertRowFactory.cs:24-29`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/OutboxInsertRowFactory.cs#L24-L29) | no rows: the fakes force `EnableSending=false` |
| Row: `instanceid` (unique; a second event for the same instance only moves `validfrom` earlier), `validfrom` = now + delay by priority (urgent 0 s for create/delete, high 5 s, low 60 s for save/process events) | [`PgOutboxRepository.cs:24-28`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/PgOutboxRepository.cs#L24-L28), [`OutboxEventSyncPolicy.cs:32-49`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/OutboxEventSyncPolicy.cs#L32-L49), [`WolverineSettings.cs:50-63`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Configuration/WolverineSettings.cs#L50-L63) | |
| `OutboxService` (hosted) returns at once when `EnableSending` is false; otherwise takes the `outbox` lease in `storage.leases`, polls `storage.outbox where validfrom <= now()` every `PollIdleTimeMs` (1 s), `PublishAsync`es each row as `SyncInstanceToDialogportenCommand` and deletes it | [`OutboxService.cs:44-47`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Services/OutboxService.cs#L44-L47), [`:132-144`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Services/OutboxService.cs#L132-L144), [`PgOutboxRepository.cs:32-34`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/PgOutboxRepository.cs#L32-L34) | registered ([`Program.cs:387`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L387)), idle |
| Wolverine: outside `Development`, conventional local routing is disabled and the command is published to the ASB queue `altinn.dialogportenadapter.webapi`; in `Development`, conventional local routing with one **sequential** local queue per message namespace. No Postgres persistence for Wolverine (`WolverineSettings:PostgresConnectionString` is declared and unused) | [`Program.cs:390-437`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L390-L437) (`IsDevelopment` at [L398](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L398), ASB queue at [L420](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L420), sequential local queues at [L426-L434](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L426-L434)) | the storage container runs with `ASPNETCORE_ENVIRONMENT=Development` (component Dockerfile `ENV`; confirmed with `podman inspect` on the running container), so the local-queue branch is active |
| Message identity `Altinn.DialogportenAdapter.SyncInstanceCommand`, fields `AppId`, `PartyId`, `InstanceId` (string), `InstanceCreatedAt` (`DateTime`), `IsMigration`, `EventType` | [`SyncInstanceToDialogportenCommand.cs:11-20`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Messages/SyncInstanceToDialogportenCommand.cs#L11-L20); adapter side [`SyncInstanceCommand.cs:5-17`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.Contracts/SyncInstanceCommand.cs#L5-L17) (`Guid`, `DateTimeOffset`, no `EventType`); JSON-compatible, the adapter ignores the extra field (the 202 probe posted exactly this shape) | |
| `WolverineSettings` values: `EnableSending` (the switch), `PollMaxSize` 100, `PollIdleTimeMs` 1000, `PollErrorDelayMs` 10000, `LeaseSecs` 120, `TryGettingPollMasterIntervalSecs` 60 (only waited when the lease is held by another pod; a single pod acquires at once), `LowPriorityDelaySecs` 60, `HighPriorityDelaySecs` 5, `UrgentPriorityDelaySecs` 0, `EnableA2Migration` true (A2 paths also check it; A2 endpoints are disabled locally), `ServiceBusConnectionString`, `PostgresConnectionString` (unused) | [`WolverineSettings.cs`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Configuration/WolverineSettings.cs#L13-L68), [`appsettings.json:53-57`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/appsettings.json#L53-L57) | |

With `EnableSending=true` and no handler, Wolverine's `PublishAsync` has no subscriber and the command is dropped
silently (publish never throws). A handler is what gives the Development branch a destination. The adapter's own
write-back (`PUT .../datavalues` with `dialog.id`) is a plain SQL update without instance events
([`PgInstanceRepository.UpdateDataValues`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Repository/PgInstanceRepository.cs#L570-L595)),
so it produces no outbox row and no loop.

### 2.2 Alternatives weighed

| | (a) Storage fake handler → production `POST syncDialog` | (b) Storage fake handler → localtest `POST api/localtest/enqueue` → adapter local queue (recommended) | (c) Azure Service Bus emulator |
| --- | --- | --- | --- |
| Storage change | fakes only: configuration + 1 handler (~120 lines) | the same handler, different URL | configuration only (`EnableSending`, connection string) but **not** in Development: the ASB branch needs a non-Development environment, and the storage container needs `Development` for http metadata and the local queues; a directive in `ConfigureWolverine` |
| Adapter change | `#if !LOCALTEST` around the ASB bootstrap and listeners (required in every option: `ConfigureAdapterDefaults` throws on an empty connection string, [`WolverineOptionsExtentions.cs:42`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.Contracts/WolverineOptionsExtentions.cs#L42)); `#else` can be empty | the same `#else` with a named sequential local queue and a publish rule (3 lines), plus a ~12-line endpoint in `fakes/` | none in code; `WolverineSettings__ServiceBusConnectionString` with `UseDevelopmentEmulator=true` and the management string |
| Production retry/error policies (409, 410, 412, 422, `PartyNotFound`, 5xx indefinitely rescheduled, [`ServiceCollectionExtensions.cs:75-160`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L75-L160)) | not applied: `syncDialog` calls the sync service directly ([`WebApplicationExtensions.cs:31-39`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L31-L39)); a failure is a 500 to Storage's handler, which has to retry | applied (verified: a failing sync in the scratch run went through Wolverine's failure handling) | applied |
| Latency seen by Storage's sequential queue | the whole sync (several HTTP calls; up to the 15-minute Dialogporten client timeout) | milliseconds (202 Accepted) | milliseconds |
| Extra containers | 0 | 0 | 2 (`servicebus-emulator`, `mssql`, both `linux/amd64` only, see upstream [`docker-compose-service-bus.yml`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/docker-compose-service-bus.yml)); ~2 GB RAM; against the model's "no Azure dependencies" |
| Fidelity | production sync code, production HTTP endpoint | production sync code, production handler and policies, a localtest ingress into the queue | closest to production transport, at the highest cost |

Recommendation: **(b)**. The Storage handler is the same in (a) and (b), the adapter `#else` exists in both, and (b)
keeps the production handler, its policies and its asynchrony. The production `syncDialog` and
`syncDialog/simple/{partyId}/{instanceGuid}` endpoints stay as the manual re-sync tools (the smoke test and a future
inspection GUI use them). (c) is rejected unless the team wants to test the ASB mapper itself.

Nothing polls across components: Storage polls its own outbox table (production behaviour), the handler pushes, the
adapter's queue is in-process.

### 2.3 The Storage-side change (fakes only, no directive)

In [`components/storage/fakes/`](../../localtest/components/storage/fakes/LocaltestStorageExtensions.cs):

1. `LocaltestStorageOptions`: add `DialogportenAdapterUrl` (default empty) and `TokenEndpoint` (default
   `{PlatformEndpoint}Home/GetTestOrgToken`).
2. `AddLocaltestConfiguration`: replace the forced `["WolverineSettings:EnableSending"] = "false"` with: when
   `Localtest:Storage:DialogportenAdapterUrl` is empty, force `false` (today's behaviour); otherwise force
   `EnableSending=true`, `EnableA2Migration=false`, and insert localtest defaults (below the environment, like the
   platform URLs) `LowPriorityDelaySecs=2`, `HighPriorityDelaySecs=0` so a tester sees a saved form in the inbox
   within seconds instead of 60. `UrgentPriorityDelaySecs` stays 0. `ServiceBusConnectionString` is irrelevant in
   Development.
3. `fakes/Dialogporten/LocaltestDialogportenAdapterClient.cs` (~80 lines): typed `HttpClient` from
   `IHttpClientFactory`; a cached bearer from
   `GET {TokenEndpoint}?orgNumber=991825827&scopes=altinn:storage/instances.syncadapter` (the localtest app mints the
   fake Maskinporten token and runs the production `exchange/maskinporten?test=true`; the result decoded today carries
   `scope` as requested, `consumer {iso6523-actorid-upis, 0192:991825827}`, `urn:altinn:org=ttd`,
   `urn:altinn:orgNumber`, issuer `…/authentication/api/v1/openid/`, 30-minute lifetime; refreshed after 25 minutes or
   on a 401); `POST {DialogportenAdapterUrl}/api/localtest/enqueue` with
   `{appId, partyId, instanceId, instanceCreatedAt, isMigration}`; on connection failure or 5xx retry after 1 s, 5 s and
   15 s, then log a warning with the instance id and drop (the outbox row is already deleted: `OutboxService` deletes
   after a successful `PublishAsync`, and a local-queue publish succeeds at enqueue). Storage has no Maskinporten
   client package, and the shared `IMaskinportenService` fake would be a foreign body here; the Home helper is the
   same two steps.
4. `fakes/Dialogporten/LocaltestDialogportenSyncHandler.cs` (~25 lines):

   ```csharp
   namespace Altinn.Platform.Storage.Localtest.Dialogporten;

   /// Discovered by Wolverine (public class named *Handler in the host assembly, the fakes are compiled into it);
   /// receives what OutboxService publishes to the Development local queue and pushes it to the adapter.
   public sealed class LocaltestDialogportenSyncHandler
   {
       public static Task Handle(SyncInstanceToDialogportenCommand command, LocaltestDialogportenAdapterClient adapter, CancellationToken cancellationToken)
           => adapter.Enqueue(command, cancellationToken);
   }
   ```

   Registered in `AddLocaltestStorage` only when the URL is set (`services.AddHttpClient<LocaltestDialogportenAdapterClient>()`);
   with no URL the handler still exists but `EnableSending` is false, so nothing is published. Wolverine's conventional
   local routing names the queue by message namespace (`Altinn.Platform.Storage.Messages`) and makes it sequential
   ([`Program.cs:426-434`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Program.cs#L426-L434)),
   so messages reach the adapter in order.
5. Compose: `Localtest__Storage__DialogportenAdapterUrl: ${DIALOGPORTEN_ADAPTER_URL:-}` on the `storage` service, with
   `DIALOGPORTEN_ADAPTER_URL=http://local.altinn.cloud:8000/storage/dialogporten` documented next to
   `COMPOSE_PROFILES=dialogporten` in `.env` (one file turns the inbox profile on). Setting it unconditionally also
   works (the handler drops with a warning when the adapter is absent) but puts a warning in Storage's log for every
   instance mutation when the inbox is off.
6. README: the "Forced" line (`EnableSending=false`) becomes conditional; the differences table's "Dialogporten sync:
   Off" row changes.

Handler discovery in the Storage host is the same mechanism the scratch verified in the adapter host (Wolverine
searched `Altinn.DialogportenAdapter.WebApi` and found the handler compiled in from `fakes/`); it was not run inside
Storage (Not verified).

### 2.4 Storage endpoints the adapter uses, and their auth in the vendored Storage

The adapter presents the exchanged Altinn token with the scopes in
[`appsettings.json:12`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/appsettings.json#L12)
(`digdir:dialogporten.serviceprovider[.admin|.legacyhtml]`, `altinn:serviceowner/instances.read|write`,
`altinn:storage/instances.syncadapter`, `altinn:register/partylookup.admin`) plus `Ocp-Apim-Subscription-Key` (ignored
locally). Storage reads scopes from `urn:altinn:scope` or `scope`
([`AuthorizationService.cs:512-526`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Authorization/AuthorizationService.cs#L512-L526));
the exchanged token carries `scope`.

| Adapter call ([`IStorageApi`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/Storage/IStorageApi.cs#L9-L21), [`IApplicationsApi`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/Storage/IApplicationsApi.cs#L8-L12)) | Storage authorization | With the sync-adapter scope |
| --- | --- | --- |
| `GET storage/api/v1/instances/{p}/{g}` | `[Authorize]`, then an explicit scope check **before** the PDP ([`InstancesController.cs:271`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/InstancesController.cs#L271)) | served, no PDP, deleted data elements included |
| `GET .../events?eventTypes=…` | policy `POLICY_INSTANCE_READ` → `StorageAccessHandler`, which succeeds at once for `read`/`write`/`delete` when the scope is present ([`StorageAccessHandler.cs:84-88`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Authorization/StorageAccessHandler.cs#L84-L88), [`:202-216`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Authorization/StorageAccessHandler.cs#L202-L216)); route [`InstanceEventsController.cs:136-137`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/InstanceEventsController.cs#L136-L137) | served |
| `PUT .../datavalues` (writes `dialog.id`) | `[Authorize]` + `ProcessAuthorizer.AuthorizeDataValuesUpdate`, which bypasses for the scope ([`ProcessAuthorizer.cs:53-55`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Authorization/ProcessAuthorizer.cs#L53-L55), [`:77-84`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Authorization/ProcessAuthorizer.cs#L77-L84)); route [`InstancesController.cs:1074`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/InstancesController.cs#L1074) | served |
| `DELETE storage/api/v1/sbl/instances/{p}/{g}?hard=false` (the inbox's delete action) | `POLICY_INSTANCE_DELETE` → the same bypass; route [`MessageboxInstancesController.cs:371-372`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/MessageboxInstancesController.cs#L371-L372) | served; the soft delete is a mutation with a `Deleted` event, so it feeds the outbox (urgent) and the dialog follows |
| `GET storage/api/v1/applications/{org}/{app}` | `[AllowAnonymous]` ([`ApplicationsController.cs:86-87`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/ApplicationsController.cs#L86-L87)) | served once the app runner has pushed the metadata; 404 before (the adapter then throws `UnreachableException`, section 9) |
| `GET .../texts/{nb,nn,en}` (the adapter asks for all three, tolerates 404, [`IApplicationRepository.cs:45-59`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/Storage/IApplicationRepository.cs#L45-L59)) | `[Authorize]` only ([`TextsController.cs:69-74`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/TextsController.cs#L69-L74)) | served |

The scope string is `GeneralSettings:InstanceSyncAdapterScope`, `altinn:storage/instances.syncadapter` in the vendored
[`appsettings.json:30`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/appsettings.json#L30).
The vendored Storage therefore serves everything the adapter needs with the token the adapter will present; no PDP
policy, role or delegation is involved. The response `selfLinks.platform` values are built from
`GeneralSettings:Hostname` as `https://platform.{host}/storage/api/v1/…`
([`InstancesController.cs:79`](https://github.com/Altinn/altinn-storage/blob/9e72a9f128debd7b9815b615d842a2d1a6d343c5/src/Storage/Controllers/InstancesController.cs#L79);
the `https://platform.` prefix is inside `Altinn.Platform.Storage.Interface`'s `SetPlatformSelfLinks`, decompiled from
4.7.2), which section 6 handles on the adapter side.

## 3. Directive sites and fakes in the adapter

### 3.1 Directive sites (exact lines at `0a32985`; applied and built in the scratch)

1. **ASB bootstrap**, [`ServiceCollectionExtensions.cs:63-67`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L63-L67):

   ```csharp
   #if !LOCALTEST
                   opts.ConfigureAdapterDefaults(
                       environment,
                       settings.WolverineSettings.ServiceBusConnectionString,
                       settings.WolverineSettings.ManagementConnectionString
                   );
   #else
                   // localtest: no Azure Service Bus. The handler runs on Wolverine's in-process local queue, which the
                   // localtest enqueue endpoint (fakes/) publishes to, so the error policies below still apply.
                   opts.EnableAutomaticFailureAcks = false;
                   opts.EnableRemoteInvocation = false;
                   opts.MultipleHandlerBehavior = MultipleHandlerBehavior.Separated;
   #endif
   ```

   The three settings are the non-ASB part of `ConfigureAdapterDefaults`
   ([`WolverineOptionsExtentions.cs:44-47`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.Contracts/WolverineOptionsExtentions.cs#L44-L47))
   minus `DisableConventionalLocalRouting`. `opts.Policies.AllListeners(x => x.ProcessInline())` and the policies
   ([L68-L169](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L68-L169))
   stay as they are.
2. **ASB listeners**, [`ServiceCollectionExtensions.cs:171-176`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L171-L176):

   ```csharp
   #if !LOCALTEST
                   opts.ListenToAzureServiceBusQueue(ContractConstants.AdapterQueueName) … ;
                   opts.ListenToAzureServiceBusQueue(ContractConstants.AdapterHistoryQueueName) … ;
   #else
                   // localtest: one sequential local queue, so two syncs of the same instance never race into a 412.
                   opts.LocalQueue(ContractConstants.AdapterQueueName).Sequential();
                   opts.PublishMessage<SyncInstanceCommand>().ToLocalQueue(ContractConstants.AdapterQueueName);
   #endif
   ```

   Verified at runtime: Wolverine started with `stub://replies/` only, discovered
   [`SyncDialogOnInstanceUpdatedHandler`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/SyncDialogOnInstanceUpdatedHandler.cs#L5-L11)
   in the host assembly, and an enqueued command reached `SyncInstanceToDialogService.Sync`.
3. **URL building**, [`Settings.cs:43-44`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Settings.cs#L43-L44):

   ```csharp
   #if !LOCALTEST
       public Uri GetAppUriForOrg(string org, string appId) => new($"{BaseUri.Scheme}://{org}.apps.{BaseUri.Host}/{appId}");
       public Uri GetPlatformUri() => new($"{BaseUri.Scheme}://platform.{BaseUri.Host}");
   #else
       // localtest: one origin for apps and platform (http://local.altinn.cloud:8000), no org or platform sub-domains.
       public Uri GetAppUriForOrg(string org, string appId) => new($"{BaseUri.ToString().TrimEnd('/')}/{appId}");
       public Uri GetPlatformUri() => new(BaseUri.ToString().TrimEnd('/'));
   #endif
   ```

   Production drops the port and assumes sub-domains; every link in section 6 goes through these two.
4. **Attachment URLs**, [`StorageDialogportenDataMerger.cs:366`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L366)
   and [`:393`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L393)
   (identical lines in `CreateAttachmentDto` and `CreateTransmissionAttachmentDto`):

   ```csharp
   #if !LOCALTEST
               var platformUrl = element.SelfLinks.Platform;
   #else
               var platformUrl = Localtest.LocaltestLinks.ToLocalPlatformUrl(element.SelfLinks.Platform, _settings);
   #endif
   ```

   `LocaltestLinks` (fakes) replaces the `https://platform.{Altinn:BaseUri host}/` prefix with `{Altinn:BaseUri}`;
   everything else about attachments (consumer type, `ToPortalUri` for GUI links) stays production.
5. **`Program.cs` hooks**, pure additions around
   [`Program.cs:36-49`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Program.cs#L36-L49):

   ```csharp
       builder.Configuration
           .AddCoreClusterSettings()
           .AddAzureKeyVault()
           .AddLocalDevelopmentSettings(builder.Environment)
           .AddUserSecrets<DialogportenAdapterSettings>();
   #if LOCALTEST
       Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.AddLocaltestConfiguration(builder.Configuration);
   #endif

       builder.Services
           .ConfigureDialogportenAdapterServices(builder.Configuration, builder.Environment, new Clock())
           .ReplaceLocalDevelopmentResources(builder.Configuration, builder.Environment);
   #if LOCALTEST
       Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.AddLocaltestAdapter(builder);
   #endif

       using var app = builder
           .Build()
           .RegisterRoutes();
   #if LOCALTEST
       Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions.MapLocaltestRoutes(app);
   #endif
   ```

   The configuration hook must sit before `ConfigureDialogportenAdapterServices`, which reads
   `configuration.Get<Settings>()` eagerly ([`ServiceCollectionExtensions.cs:55`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L55));
   the DI hook after it, because the shared Maskinporten fake must `RemoveAll` after the package's `TryAddSingleton`
   ([`ServiceCollectionExtensions.cs:170`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Extensions/ServiceCollectionExtensions.cs#L170)).
   The DI and routes hooks were built and run; the configuration hook is the same mechanism as Storage's
   `AddLocaltestConfiguration` (the scratch used environment variables instead).

Nothing in the sync logic, the merger's content, the receipt or the delete flow needs a directive.

### 3.2 Fakes (`fakes/`, namespace `Altinn.DialogportenAdapter.WebApi.Localtest`, compiled into the host)

| File | Responsibility | Size |
| --- | --- | --- |
| `LocaltestAdapterExtensions.cs` | `AddLocaltestConfiguration(ConfigurationManager)`: localtest defaults inserted below the environment sources (the Storage pattern), derived from `Localtest:Adapter:PlatformEndpoint` (default `http://local.altinn.cloud:8000/`): every `DialogportenAdapter:*` URL in section 7.1, the dummy Maskinporten client values, `ApplicationInsights:Enabled=false`, `WolverineSettings:ServiceBusConnectionString=""` (bound but unused), `Adapter:FeatureFlag:EnableSubmissionTransmissions=true`. `AddLocaltestAdapter(builder)`: `services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.RequireHttpsMetadata = false)` (the OpenID metadata of the vendored Authentication is plain http; `PostConfigureAll` does not work, see section 0), then `services.AddLocaltestMaskinporten(configuration)` from the shared fake. `MapLocaltestRoutes(app)`: `POST storage/dialogporten/api/localtest/enqueue` with the `SyncInstanceCommand` body → `IMessageBus.PublishAsync` → 202, `.RequireAuthorization()` so the production default policy (scope `altinn:storage/instances.syncadapter`) applies | ~80 |
| `LocaltestLinks.cs` | `ToLocalPlatformUrl(platformUrl, settings)` for directive site 4 | ~15 |
| shared `Maskinporten/LocaltestMaskinportenService.cs` (+ options, extensions) | the shared-prerequisites fake (section 2.3 there): unsigned jwt-bearer assertion to `localtest/api/maskinporten/token`, then the production `exchange/maskinporten?test=true`; replaces `IMaskinportenService` for the four Refit clients and the Dialogporten client package | shared, ~150 |

Removed from the earlier list: a replacement for the Dialogporten client's key fetch. The package fetches the
Ed25519 JWKS with a plain Refit client from `{Dialogporten:BaseUri}/api/v1/.well-known/jwks.json`
([`RefitterInterface.cs:296`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/WebApiClient/Digdir.Library.Dialogporten.WebApiClient/Features/V1/RefitterInterface.cs#L296),
[`EdDsaSecurityKeysCacheService.cs:99-103`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/WebApiClient/Digdir.Library.Dialogporten.WebApiClient.Common/Services/EdDsaSecurityKeysCacheService.cs#L99-L103)),
no https requirement; with `ThrowOnPublicKeyFetchInit=false` (the adapter sets it,
[`ServiceCollectionExtensions.cs:242`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L242))
it retries every 10 s until the first success, then every 12 h
([`:33-35`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/WebApiClient/Digdir.Library.Dialogporten.WebApiClient.Common/Services/EdDsaSecurityKeysCacheService.cs#L33-L35),
[`:56-71`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/WebApiClient/Digdir.Library.Dialogporten.WebApiClient.Common/Services/EdDsaSecurityKeysCacheService.cs#L56-L71)).
Seen in the scratch log: the fetch against the (absent) Dialogporten failed and the host kept running. Consequence: a
Dialogporten restart with **new** keys is picked up after up to 12 h; the committed key pair from
[`dialogporten-core.md`](dialogporten-core.md) section 8.3 makes keys stable across restarts.

### 3.3 Inbound authentication, and what the localtest token sources supply

| Endpoint ([`WebApplicationExtensions.cs`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L21-L128)) | Auth | Local token source |
| --- | --- | --- |
| `POST storage/dialogporten/api/v1/syncDialog`, `POST .../syncDialog/simple/{partyId}/{instanceGuid}`, localtest `POST .../api/localtest/enqueue` | JWT bearer against `Authentication:JwtBearerWellKnown` (`MapInboundClaims=false`, issuer and audience not validated, [`ServiceCollectionExtensions.cs:308-322`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L308-L322)); default policy: authenticated + `scope` claim containing `altinn:storage/instances.syncadapter` (space-split, [`AuthorizationExtensions.cs:7-14`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/AuthorizationExtensions.cs#L7-L14)); fallback policy: authenticated | `/Home/GetTestOrgToken?orgNumber=991825827&scopes=altinn:storage/instances.syncadapter` (Storage's handler, the smoke test). Verified: 401 without a token, 403 with `/Home/GetTestUserToken` (no scope), 202 with the org token, metadata fetched over http from the vendored Authentication |
| `DELETE storage/dialogporten/api/v1/instance/{owner}/{guid}` | `AllowAnonymous`; `IDialogTokenValidator.Validate(token, dialogId, ["delete"])` or `["delete,urn:altinn:task:{current}"]` ([`InstanceService.cs:69-88`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Delete/InstanceService.cs#L69-L88)): Ed25519 signature against Dialogporten's JWKS, dialog id, action | A dialog token minted by Dialogporten for the signed-in user: Arbeidsflate gets it with the dialog; for a test, `GET /dialogporten/api/v1/enduser/dialogs/{id}` with a user token (the `/Home/GetTestUserToken` token carries `digdir:dialogporten.noconsent`, decoded in [`dialogporten-core.md`](dialogporten-core.md) section 7) returns `dialogToken`. No localtest source mints dialog tokens, and none should: the validation is the point |
| `GET storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}?lang=` | `AllowAnonymous`; `IDialogTokenValidator.Validate(token, dialogId)` | the same dialog token |
| `GET /health`, `GET /openapi/v1.json` | anonymous | – |
| `ReplaceLocalDevelopmentResources` (`LocalDevelopment:MockDialogportenApi`, `:DisableAuth`) | only in Development **and** only when a `LocalDevelopment` section exists ([`ServiceCollectionExtensions.cs:354-371`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L354-L371)) | not configured: the stack wants the real Dialogporten and real auth. Verified absent in the scratch run (Development, no section, 401/403 enforced) |

`UseHttpsRedirection` ([`WebApplicationExtensions.cs:16`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L16))
logs "Failed to determine the https port for redirect" once and does nothing on an http-only host; CORS is
`AllowAnyOrigin` ([`:304-307`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs#L304-L307)),
which the inbox's front-channel receipt and delete calls need.

## 4. Register

The vendored Register (pin `f6b5c60`) serves the adapter's only Register call,
`POST register/api/v1/dialogporten/parties/query`
([`IRegisterApi.cs:7-8`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/Register/IRegisterApi.cs#L7-L8)),
as production code: [`DialogPortenController`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs#L16-L17)
(`[Authorize(Policy = "InternalOrPlatformAccess")]`, route `register/api/v{version}/dialogporten/parties`,
[`query`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs#L37-L45)
delegates to `V2.PartyController.Query`). The policy is scope `altinn:register/partylookup.admin` **or** a platform
access token ([`RegisterHost.cs:170-171`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs#L170-L171));
the adapter's scope list includes it. The adapter asks for `urn:altinn:party:id:{partyId}` (instance owner) and
`urn:altinn:user:id:{userId}` (actors of instance events) and maps the answer to `urn:altinn:person:identifier-no:`,
`urn:altinn:organization:identifier-no:` or the display-name URNs
([`IRegisterRepository.cs:77-86`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/Register/IRegisterRepository.cs#L77-L86));
both URN kinds are in [`PartyUrn`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/src/Altinn.Register.Contracts/PartyUrn.cs#L17)
and the localtest in-memory persistence resolves party ids and user ids
([`InMemoryPartyPersistence.cs`](../../localtest/components/register/fakes/Altinn.Register.Localtest/Persistence/InMemoryPartyPersistence.cs)).
Proxy: `/register/` → register, already routed. A party the Register does not know makes the adapter throw
`PartyNotFoundException`, rescheduled by its policy; with the seed's valid synthetic identifiers that only happens
for an instance owner outside `testData.json`. Runtime check of the query with the adapter's token: not done (the
stack was being recreated by another session at the time, Not verified).

## 5. Altinn CDN (`orgs/altinn-orgs.json`)

The adapter reads it once per 5 minutes (FusionCache) through
[`IAltinnCdnApi.GetAltinnOrgs`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Infrastructure/AltinnCdn/IAltinnCdnApi.cs#L8-L9)
on the base URL `DialogportenAdapter:Altinn:AltinnCdn`
([`appsettings.json:23`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/appsettings.json#L23)),
to put the service owner's name on the dialog; the fetch is part of every sync's parallel block
([`ISyncInstanceToDialogService.cs:61-67`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/ISyncInstanceToDialogService.cs#L61-L67)),
so without it no dialog is created. Today nothing in the stack serves it: the vendored Authentication's
`exchange/maskinporten` fetches `https://altinncdn.no/orgs/altinn-orgs.json` from the internet
(`GeneralSettings:OrganisationRepositoryLocation`, [`OrganisationsService.cs:46`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OrganisationsService.cs#L46)),
documented in `localtest/localtest/README.md` and open in `reviews/authentication.md` (finding 8). The shared
prerequisites report plans the fix for all consumers: a committed copy served by the localtest app at
`GET localtest/api/cdn/orgs/altinn-orgs.json` ([`shared-prerequisites.md`](shared-prerequisites.md) section 6). For
this strand: `DialogportenAdapter__Altinn__AltinnCdn=http://local.altinn.cloud:8000/localtest/api/cdn` (no trailing
slash: Refit appends `/orgs/altinn-orgs.json`), nothing in code. The adapter reads only `name` and `orgnr`; `ttd` is
absent in the real file, and the adapter's own `ttd`→Digdir mapping matches the stack's
([`resource-registry.md`](resource-registry.md) section 6.2). Until the route exists the adapter can point at
`https://altinncdn.no` (reachable from this machine today) at the cost of an internet dependency.

## 6. Links in the dialogs, and the link back

With `Altinn:BaseUri=http://local.altinn.cloud:8000/`, `Adapter:BaseUri=http://local.altinn.cloud:8000/storage/dialogporten`
and directive sites 3 and 4, for instance `500000/{guid}` of `ttd/testapp`:

| Dialog element | Code | Local URL | Resolves to |
| --- | --- | --- | --- |
| Go-to GUI action (primary), instance in progress | [`StorageDialogportenDataMerger.cs:779-792`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L779-L792), wrapped by [`ToPortalUri`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L1013-L1020) | `http://local.altinn.cloud:8000/authentication/api/v1/authentication?goto=http%3A%2F%2Flocal.altinn.cloud%3A8000%2Fttd%2Ftestapp%3FdontChooseReportee%3Dtrue%23%2Finstance%2F500000%2F{guid}` | the vendored Authentication's production `authentication?goto=` (its `goto` guard accepts plain http, `components/authentication/README.md` directive 2), then the running app's instance page through the app route |
| Go-to, archived instance | same, `GetPlatformUri() + /receipt/{id}` | `http://local.altinn.cloud:8000/receipt/500000/{guid}` (inside the same `goto`) | **404**: no receipt app in the stack (open question 2) |
| Delete GUI action | [`:796-812`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L796-L812) | `DELETE http://local.altinn.cloud:8000/storage/dialogporten/api/v1/instance/500000/{guid}` with the dialog token | proxy → adapter → Storage `sbl/instances` soft delete → outbox → dialog deleted |
| Copy GUI action (archived, `copyInstanceSettings.enabled`) | [`:836-851`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L836-L851) | `…?goto=http://local.altinn.cloud:8000/ttd/testapp/legacy/instances/500000/{guid}/copy` | the app |
| API action "read" | [`:854-866`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L854-L866) | `http://local.altinn.cloud:8000/ttd/testapp/instances/500000/{guid}`, archived: `http://local.altinn.cloud:8000/storage/api/v1/instances/500000/{guid}` | the app / Storage |
| Attachments (data elements) and transmission attachments | [`:364-369`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L364-L369), site 4 | `selfLinks.platform` `https://platform.local.altinn.cloud/storage/api/v1/instances/500000/{guid}/data/{id}` → `http://local.altinn.cloud:8000/storage/api/v1/instances/500000/{guid}/data/{id}`, GUI ones inside `authentication?goto=` | Storage's data endpoint with the browser's `AltinnStudioRuntime` cookie after the goto sign-in (end to end not verified) |
| Transmission receipt URL | [`:424-427`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L424-L427) | `http://local.altinn.cloud:8000/storage/dialogporten/api/v1/receipt/{dialogId}/{transmissionId}` | adapter, front-channel with the dialog token, markdown |
| `serviceResource` | [`:889`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs#L889) | `urn:altinn:resource:app_ttd_testapp` | must exist in the Resource Registry (section 13) |

The link back: the adapter writes `dialog.id` into the instance's data values on the first sync
([`ISyncInstanceToDialogService.cs:118-125`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/ISyncInstanceToDialogService.cs#L118-L125),
[`Constants.cs:12`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Constants.cs#L12);
the dialog id is a UUIDv7 derived from the instance id and creation time). The app frontend reads it
(`getDialogIdFromDataValues`) and builds the inbox link in
[`urlHelper.ts:36-57`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/src/utils/urls/urlHelper.ts#L36-L57)
from `platformFrontendSettings.arbeidsflateInboxUrl` / `arbeidsflateDialogUrl` (templates from the app backend's
[`PlatformFrontendSettings`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Configuration/PlatformFrontendSettings.cs#L45-L53)),
but on `local.altinn.cloud(:port)` returns `http://{host}/` first (lines 36-39). The three-line frontend change and the
two `PlatformFrontendSettings__Arbeidsflate*` exports in `run-app.sh` are planned in
[`shared-prerequisites.md`](shared-prerequisites.md) section 7; nothing in this strand. The app backend sets nothing
dialog-related: app-lib v9 (`src/App/backend`) and v8 (`app-lib-dotnet` at `4740bb2`) mention Dialogporten only in
the correspondence reference type and the notification order's dialog association, and `InstancesController` only in
a comment about data-value updates.

## 7. Configuration, proxy, app runner

### 7.1 Compose

```yaml
  # Production Dialogporten adapter (components/dialogporten-adapter): turns Storage's instance mutations into dialogs.
  # Storage's localtest handler posts the outbox commands to storage/dialogporten/api/localtest/enqueue through the proxy.
  dialogporten-adapter:
    <<: *platform-host
    build:
      context: ./components/dialogporten-adapter
      additional_contexts:
        shared: ./components/shared
    image: localhost/localtest-dialogporten-adapter:dev
    profiles: ["dialogporten"]
    environment:
      Localtest__Adapter__PlatformEndpoint: http://local.altinn.cloud:8000/
    depends_on:
      - storage
      - register
      - dialogporten-webapi
```

Defaults the fakes derive from `Localtest:Adapter:PlatformEndpoint` (all overridable with the production keys):

| Key | Value |
| --- | --- |
| `DialogportenAdapter:Altinn:BaseUri`, `InternalStorageEndpoint`, `InternalRegisterEndpoint` | `http://local.altinn.cloud:8000/` (the platform host every container maps to the localtest app) |
| `DialogportenAdapter:Altinn:SubscriptionKey` | `localtest` |
| `DialogportenAdapter:Altinn:AltinnCdn` | `http://local.altinn.cloud:8000/localtest/api/cdn` |
| `DialogportenAdapter:Dialogporten:BaseUri` | `http://local.altinn.cloud:8000/dialogporten` |
| `DialogportenAdapter:Adapter:BaseUri` | `http://local.altinn.cloud:8000/storage/dialogporten` (public: the inbox browser calls it) |
| `DialogportenAdapter:Adapter:FeatureFlag:EnableSubmissionTransmissions` | `true` (transmissions with receipts, as tt02's development settings) |
| `DialogportenAdapter:Authentication:JwtBearerWellKnown` | `http://local.altinn.cloud:8000/authentication/api/v1/openid/.well-known/openid-configuration` |
| `DialogportenAdapter:Maskinporten:Environment`/`TokenExchangeEnvironment`/`ClientId`/`EncodedJwk`/`ExhangeToAltinnToken`/`Scope` | `test` / `tt02` / `991825827` / `eyJrdHkiOiJSU0EifQ==` / `true` / upstream's scope string (the shared fake reads `ClientId`, `Scope`, `ExhangeToAltinnToken`; the rest satisfy the package's validators) |
| `ApplicationInsights:Enabled` | `false` (forced) |
| `WolverineSettings:ServiceBusConnectionString` | `""` (bound, unused under `LOCALTEST`) |
| `Logging:LogLevel:Altinn.DialogportenAdapter` | `Information` (upstream's default is Warning; the sync logs no-ops and skips at Warning/Information) |
| `kvSetting:*` | unset: Key Vault never connects |

Storage: `Localtest__Storage__DialogportenAdapterUrl: ${DIALOGPORTEN_ADAPTER_URL:-}` (section 2.3). Dialogporten's
own service must accept the exchanged Altinn token as a service owner (it does: scope
`digdir:dialogporten.serviceprovider` plus a parseable `consumer` claim, [`dialogporten-core.md`](dialogporten-core.md)
section 7; the exchange keeps every original claim,
[`AuthenticationController.cs:468-471`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs#L468-L471),
and adds `urn:altinn:org` at [L487](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Controllers/AuthenticationController.cs#L487)).

### 7.2 Proxy route

In [`localtest/localtest/Proxy/PlatformRoutes.cs`](../../localtest/localtest/Proxy/PlatformRoutes.cs): upstream
`["dialogporten-adapter"] = "http://dialogporten-adapter:5011"`, and in `Prefixes` the entry
`("/storage/dialogporten/", "dialogporten-adapter")` **before** `("/storage/", "storage")`. `Resolve` walks the array
in order and returns the first `StartsWith` match (ordinal), so order is the whole rule; the README's route list and
`IsPrefixWithoutSlash` need no change (`/storage/dialogporten` without a slash redirects to `/storage/dialogporten/`,
which is fine). The adapter's own routes already start with `storage/dialogporten/`
([`WebApplicationExtensions.cs:21-22`](https://github.com/Altinn/altinn-dialogporten-adapter/blob/0a329856ba79101acdf54c29fd84a5063189a42d/src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/WebApplicationExtensions.cs#L21-L22)),
so nothing is stripped. `/health` and `/openapi` of the adapter are not reachable through the proxy; `podman exec`
or a published port for debugging, as access-management does.

### 7.3 App runner

No change for the sync itself: the adapter reads the application metadata and texts the runner already pushes to
Storage, and the resource `app_ttd_testapp` is derived from Storage's application list by the Resource Registry
([`resource-registry.md`](resource-registry.md) section 6.2). Changes owned by other strands that the inbox needs
after a push: Dialogporten's subject-resource sync (core report section 12), the `PlatformFrontendSettings__Arbeidsflate*`
exports (shared report section 7). Optional for this strand: `components/dialogporten-adapter/resync.sh <partyId>
<instanceGuid>` calling `POST storage/dialogporten/api/v1/syncDialog/simple/{partyId}/{guid}` with
`/Home/GetTestOrgToken` (`isMigration=false`), the manual re-sync for an instance created while the inbox profile was
off.

### 7.4 Test data

None of its own. Instance owners are the Register seed's parties (person `500000` and the others in
`components/register/testdata/testData.json`); actors are their user ids; the service owner is Digdir's number
(`ttd`). Dialogs are created with `isSilentUpdate=false` (`IsMigration=false`), so Dialogporten emits its events and
notifications as in production.

## 8. Effort and risk

| Item | Effort | Risk |
| --- | --- | --- |
| Vendor, props/targets, Dockerfile, fakes, README, UPSTREAM.md | 1 to 1.5 days | low: the build chain is verified, the directive sites are built and partly run |
| Storage fakes (options, configuration, client, handler), README | 0.5 to 1 day | medium: Wolverine handler discovery in the Storage host, the outbox lease and delete loop under `EnableSending=true`, not run here |
| Compose, proxy, `.env` documentation | 0.5 day | low |
| Smoke test (section 9), runs against Dialogporten and the Resource Registry | 1 to 1.5 days | medium: the first run surfaces every contract mismatch between the adapter, Dialogporten's validation (service resource, party URNs, activities), the dialog-token flow and attachments |
| Total | **3 to 5 days** after the Dialogporten, Resource Registry and shared-prerequisite work | |

Resource cost: one container, about 150 to 250 MB (not measured).

## 9. Verification plan (`components/dialogporten-adapter/smoke-test.sh`, through the proxy)

Preconditions: the stack with `COMPOSE_PROFILES=dialogporten` and `DIALOGPORTEN_ADAPTER_URL` set, the `ttd/testapp`
fixture pushed (`smoke/apps/ttd/testapp`, as the root smoke test does), the Resource Registry serving
`app_ttd_testapp`. Tokens: `ORG` = `/Home/GetTestOrgToken?orgNumber=991825827&scopes=altinn:serviceowner/instances.read altinn:serviceowner/instances.write altinn:storage/instances.syncadapter digdir:dialogporten.serviceprovider digdir:dialogporten.serviceprovider.search`,
`USR` = `/Home/GetTestUserToken?userId=1337&authenticationLevel=3`. The test itself may poll; the components do not.

1. Auth: `POST storage/dialogporten/api/v1/syncDialog` without a token → 401, with `USR` → 403 (no scope).
2. Create: `POST storage/api/v1/instances?appId=ttd/testapp` for party 500000 as `USR` → 201 (an urgent outbox row,
   handler, enqueue, sync). Within 10 s: `GET dialogporten/api/v1/serviceowner/dialogs?serviceOwnerLabels=urn:altinn:integration:storage:500000/{guid}`
   with `ORG` → one dialog, `serviceResource=urn:altinn:resource:app_ttd_testapp`, `party=urn:altinn:person:identifier-no:{ssn of 500000}`,
   status for a fresh instance, the go-to action's URL as in section 6; `GET storage/api/v1/instances/500000/{guid}`
   shows `dataValues["dialog.id"]` equal to the dialog's id.
3. Update: the app starts the process (`PUT .../process` with `ORG`, as the root smoke) and uploads a data element as
   `USR` → within 10 s (low priority, 2 s locally) the dialog has an attachment whose URL starts with
   `http://local.altinn.cloud:8000/` and the process activity.
4. Submit: the app ends the process → the dialog's status changes and, with transmissions on, a transmission with a
   receipt URL exists; `GET` that URL with a dialog token (`GET dialogporten/api/v1/enduser/dialogs/{dialogId}` as
   `USR` returns `dialogToken`) → 200 `text/markdown`; the same without a token → 401.
5. Delete: `DELETE storage/dialogporten/api/v1/instance/500000/{guid}` with the dialog token → 204; Storage shows
   `status.isSoftDeleted`; within 10 s the service-owner `GET .../dialogs/{id}` shows `deleted: true`.
6. Re-sync: `POST storage/dialogporten/api/v1/syncDialog/simple/500000/{guid}` with `ORG` → 204 (idempotent).
7. Off-switch: with `DIALOGPORTEN_ADAPTER_URL` empty, step 2 creates no dialog and Storage logs nothing (the
   implementer runs this on a private `up`).

Also add to the root `smoke-test.sh`: nothing (the inbox is an opt-in profile); the Cypress platform regression is
unaffected, since the default profile keeps `EnableSending=false`.

## 10. Known gaps and differences from production

| Area | Production | Localtest |
| --- | --- | --- |
| Transport | ASB queues (`webapi` and `-history`), duplicate detection, 50 listeners | one in-process sequential local queue fed over HTTP; no history queue (migration path unused) |
| Error queue | ASB dead-letter, manual inspection | Wolverine's `MoveToErrorQueue` on a non-durable local queue logs and discards; a dropped instance is re-synced with `syncDialog/simple` |
| Delivery guarantee | outbox row survives until ASB accepts | outbox row deleted at local enqueue; a dead adapter loses the command after the Storage handler's retries (warning with the instance id) |
| Links | `{org}.apps.{host}`, `platform.{host}` | one origin (sites 3 and 4) |
| Archived go-to | platform receipt app | 404 (open question 2) |
| Service-owner name | altinncdn.no | the localtest app's copy |
| Telemetry | Azure Monitor | console only |

## 11. Not verified

- The Storage-side handler under Wolverine inside the Storage host (discovery, the sequential queue, `OutboxService`
  with `EnableSending=true`, lease acquisition on `storage.leases`); the same discovery mechanism was verified in the
  adapter host only.
- Register's `dialogporten/parties/query` and Storage's reads with the sync-adapter token at runtime: the stack was
  being recreated by another session during the probes (502/401 from a half-started stack). Code-level evidence in
  sections 2.4 and 4.
- Dialog-token validation against Dialogporten's JWKS, the receipt and delete endpoints end to end, attachments
  opened through `authentication?goto=` with the inbox user's cookie (the inbox runs on another host name; whether
  the `AltinnStudioRuntime` cookie domain covers it is the Arbeidsflate strand's question).
- The image build (`podman build` not allowed here) and the `additional_contexts` copy of the shared fake.
- `Altinn.ApiClients.Dialogporten` 1.118.0 versus the `src/WebApiClient` source read at `0946da8` (tag `v1.124.1`
  era): the key-cache behaviour and `ThrowOnPublicKeyFetchInit` were confirmed by the 1.118.0 package's log lines in
  the scratch run, the rest by source.
- `Altinn.Platform.Storage.Interface` 4.7.1 in the adapter against the vendored Storage's 5.0.0-rc.3 models: JSON
  shape compatibility assumed (the adapter reads a subset); `SetPlatformSelfLinks` decompiled from 4.7.2.
- The configuration hook `AddLocaltestConfiguration` (the scratch used environment variables; the mechanism is
  Storage's).
- The `ScopeAccessHandler` claim type in the Register's policy (`scope` vs `urn:altinn:scope`); the exchanged token
  carries `scope`, which Storage's own handler accepts, and the Register's handler comes from the shared
  authorization package.

## 12. Open questions for the product owner

1. Opt-in profile `dialogporten` for the adapter (with Dialogporten, Redis, Arbeidsflate), switched together with
   `DIALOGPORTEN_ADAPTER_URL` in `.env`? Or always on, with Storage warning when the adapter is absent?
2. Archived instances link to the platform receipt (`/receipt/{id}`), which the stack lacks: accept the 404, add a
   third merger directive that links to the app's own receipt page (`{app}/#/instance/{id}`) locally, or vendor the
   receipt app later?
3. Local outbox delays `LowPriorityDelaySecs=2`, `HighPriorityDelaySecs=0` (production 60/5): fast feedback, but a
   tester never sees production's batching. Keep production values behind a setting?
4. Should the manual re-sync be exposed in the planned inspection GUI (one button per instance calling
   `syncDialog/simple`)?

## 13. What this strand needs from the other strands

- **Dialogporten** ([`dialogporten-core.md`](dialogporten-core.md)): WebApi at `http://local.altinn.cloud:8000/dialogporten`
  accepting the exchanged Altinn token (Altinn scheme, `consumer` + `digdir:dialogporten.serviceprovider[.admin]`) on
  `api/v1/serviceowner/dialogs[...]` including `isSilentUpdate`, `serviceOwnerLabels` search, activities,
  transmissions, purge and restore; `api/v1/.well-known/jwks.json` with the committed Ed25519 pair; end-user
  `dialogs/{id}` returning `dialogToken` for the smoke test; CORS not needed on the WebApi for the adapter's calls.
- **Resource Registry** ([`resource-registry.md`](resource-registry.md)): `app_ttd_testapp` (and every pushed app)
  resolvable by Dialogporten with owner `991825827`; nothing for the adapter itself.
- **Shared prerequisites** ([`shared-prerequisites.md`](shared-prerequisites.md)): (1) the token endpoint
  `POST localtest/api/maskinporten/token` with client id `991825827` → `consumer 0192:991825827`, scope echoed; the
  adapter's assertion carries its full scope string from `appsettings.json:12`, nine scopes, all to be echoed; (2) the
  `IMaskinportenService` fake compiled into `Altinn.DialogportenAdapter.WebApi` via the `shared/fakes/Maskinporten/*.cs`
  include, exchanging with `test=true` so the token comes out as `ttd`, serving the four Refit clients (Storage,
  Applications, Register, Dialogporten) and the `Altinn.ApiClients.Dialogporten` package's clients (same interface,
  same DI); the adapter calls `GetToken(IClientDefinition, MaskinportenRequestContext, bool)` through the package's
  handler ([`MaskinportenTokenHandler.cs:55-65`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Handlers/MaskinportenTokenHandler.cs#L55-L65))
  and nothing else; (3) `GET localtest/api/cdn/orgs/altinn-orgs.json`; (4) the app-frontend inbox link.
- **Arbeidsflate**: the delete and receipt calls go to `http://local.altinn.cloud:8000/storage/dialogporten/api/v1/…`
  with the dialog token (CORS open on the adapter); attachments and go-to links land on `local.altinn.cloud:8000`
  behind `authentication?goto=`.
- **Storage component owner**: the fakes change in section 2.3 (same component, this strand implements it).

## 14. Upstream since the pin (`0a32985` → `86364b8`, 3 commits)

`8ff64fc` bumps the runtime images to `aspnet:10.0.13@sha256:d5633778…` (WebApi and EventSimulator Dockerfiles);
`1ab6a91` bumps `Microsoft.AspNetCore.OpenApi` to 10.0.13 (and `Microsoft.OpenApi` in the simulator); `86364b8` adds
`x.TracesPerSecond = null; x.EnableTraceBasedLogsSampler = false;` inside `ConfigureTelemetry`
(`ServiceCollectionExtensions.cs`, the Azure Monitor block, inert under `ApplicationInsights:Enabled=false`). No
directive site, contract, route or setting changed. Vendor at `86364b8` and mirror the 10.0.13 runtime digest in the
component Dockerfile.

## Summary for the orchestrator

1. The adapter is a small, clean vendoring: two projects, no CPM, analyzers relaxed by one property; the scratch build
   with every planned directive and the fakes passed with 0 warnings on the image's SDK 10.0.401 (`global.json` must
   stay out of the image, as upstream's own Dockerfile does).
2. The message path needs **no Storage directive**: the vendored Storage runs in `Development`, where Wolverine
   already routes the outbox commands to a sequential in-process queue; the storage **fakes** currently force
   `EnableSending=false` and must instead enable it when `Localtest:Storage:DialogportenAdapterUrl` is set, plus one
   Wolverine handler (compiled into the host) that POSTs each command to the adapter.
3. Recommended target: a localtest `POST storage/dialogporten/api/localtest/enqueue` that publishes to the adapter's
   local queue, so the production handler and its retry/error policies run; verified end to end in a scratch run
   (401 / 403 / 202 with stack-issued tokens, handler executed). The production `syncDialog` stays for manual re-sync.
   A Service Bus emulator is rejected.
4. Storage serves every adapter call with the `altinn:storage/instances.syncadapter` scope through its built-in
   bypass (no PDP); `/Home/GetTestOrgToken` already produces that token (decoded today) and is what Storage's handler
   should use.
5. Adapter directive sites: ASB bootstrap and listeners (`#else` local queue), `GetAppUriForOrg`/`GetPlatformUri`,
   two `selfLinks.platform` lines, three `Program.cs` hooks. Trap found: `RequireHttpsMetadata=false` must be a
   `Configure<JwtBearerOptions>`, not `PostConfigure`.
6. Register's `dialogporten/parties/query` is production code in the vendored Register with the scope the adapter
   carries; the CDN org list and the Maskinporten fake come from the shared strand (base URLs only here).
7. Links resolve to the one local origin except archived instances' receipt (`/receipt/{id}`, no receipt app: open
   question); the inbox link from the app is the shared strand's frontend change.
8. Effort 3 to 5 days after the sibling strands; main risks are runtime-only: the Storage handler under Wolverine,
   dialog-token validation and attachments through `authentication?goto=`, none reproducible without Dialogporten.
9. Re-pin to `86364b8` (image and package bumps only).
