# Dialogporten core: vendoring plan for `localtest/components/dialogporten/`

Research date: 2026-10-20. Strand "dialogporten-core": the Dialogporten service itself (WebApi, GraphQL, Service, Janitor
hosts; Domain, Application, Infrastructure libraries). The adapter, Arbeidsflate and the Resource Registry are other
strands; this report only states what it needs from them (section 4 and the final summary).

Verified by reading the pinned code, by HTTP calls against the running stack, and by a scratch `dotnet restore
--locked-mode` + `dotnet build -c Release` of the WebApi host with the localtest overrides (section 1.3; deleted
afterwards). Nothing was built into an image or started. Claims inferred without running are marked "not verified".

| Repo | Commit | What |
| --- | --- | --- |
| [`Altinn/dialogporten`](https://github.com/Altinn/dialogporten/tree/0946da81c676b928db98780afa01fe50943af609) | `0946da8` (main, 2026-10-16); `origin/main` `1a7c584` checked for drift (section 10) | Dialogporten |
| [`Altinn/altinn-apiclient-maskinporten`](https://github.com/Altinn/altinn-apiclient-maskinporten/tree/15d3c63a1f320816acccefd1f221d936ccb286db) | `15d3c63` (the NuGet 10.1.0 Dialogporten references has the same `MaskinportenService`; not confirmed tag by tag) | The `IMaskinportenService` client Dialogporten uses |
| [`Altinn/altinn-auth`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) | `3d51810` (the pin of the vendored authentication, authorization and access-management components) | Which tokens and endpoints the stack serves today |
| [`Altinn/altinn-register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) | `f6b5c60` (vendored register) | Party lookup endpoint |
| [`Altinn/altinn-events`](https://github.com/Altinn/altinn-events/tree/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6) | `1a4a80c` (vendored events) | Generic event publishing |
| This repo | `localtest/` as of 2026-10-20 (compose, `localtest/localtest/Proxy/PlatformRoutes.cs`, the components' READMEs) | The stack the plan fits |

Earlier research this builds on: [`../../localtest-components/dialogporten-arbeidsflate.md`](../../localtest-components/dialogporten-arbeidsflate.md)
(2026-10-18). Where it is stale or wrong, section 0 says so.

## Summary

- Vendor the seven projects the four hosts need (section 1.1), relative layout preserved under `vendor/src/`, plus
  `Directory.Build.props`, `.editorconfig` and `global.json`. Leave out the samples, tools, `WebApiClient/`, tests,
  docs and the k6/CDC/OTel tooling.
- The build chain is friendlier than the earlier report assumed: `Directory.Build.props` is a plain props file (no
  central package management, no analyzer packages, `TreatWarningsAsErrors` and `AnalysisMode=Recommended`), and the
  existing `localtest.build.props`/`.targets` pattern works unchanged. **Constraint found and verified:** every project
  has a `packages.lock.json` and the upstream Dockerfiles restore with `--locked-mode`; a `ProjectReference` or
  `PackageReference` added from the targets file fails that restore with NU1004, while `Compile Include` of a `fakes/`
  folder passes. So the fakes are compiled into the hosts (the events-component pattern), not a `fakes/*.csproj`.
- The SDK pin is `10.0.401` with `rollForward: disable`; the upstream SDK image `sdk:10.0.401` carries exactly that,
  and this machine has it too (`dotnet --list-sdks`: 10.0.302, 10.0.401, 11.0.100-rc.2). `global.json` only applies
  when `dotnet` runs from a directory under it, so the Dockerfile runs the build from `/src/vendor/...` as upstream's
  does from `/src`.
- Three hosts are enough: WebApi, GraphQL, Service. The Janitor's two sync commands are MediatR commands in the
  Application project; a localtest-only endpoint in the WebApi fakes runs them on the app runner's push, so no Janitor
  image and no `podman exec` from the runner. No migration-bundle image either: the production
  `DevelopmentMigratorHostedService` migrates on start in `Development`, and compose ordering makes the WebApi the one
  that migrates first.
- All thirteen `LocalDevelopment` flags are re-verified (section 3). Two corrections to the earlier table: `DisableCache`
  only replaces the *default* `IFusionCache`, which no production client uses (they all take named caches through
  `IFusionCacheProvider`), so it does not shorten the 15-minute authorization caches; and
  `UseLocalDevelopmentOrganizationRegister` is read by nothing.
- Outbound dependencies (section 4): Access Management (authorized parties, role and package metadata), Register
  (party names), the PDP (`authorize`), Events (`events/api/v1/events`) and Authentication (OpenID metadata) are
  served by the stack today and were exercised with an org token from `/Home/GetTestOrgToken`. The gaps are the
  Resource Registry (nothing at `/resourceregistry/`, 404 at the proxy), Maskinporten (no token endpoint; solved
  inside this component by a fake `IMaskinportenService` that calls `/Home/GetTestOrgToken`), and the Altinn CDN
  (`altinncdn.no`, internet).
- Tokens (section 7): the picker's user token already carries `digdir:dialogporten.noconsent` (the earlier report
  said it did not), but it is Authentication's *cookie* token, which by production design has no `pid` claim, and
  Dialogporten's end-user API classifies the caller by `pid`. The end-user API therefore needs an access token from
  Authentication's OIDC `token` endpoint, which is the Arbeidsflate strand's OIDC-client work; until then the smoke
  test covers the service-owner API, the well-known endpoints and GraphQL schema requests.
- Postgres (section 6): one migration uses PostgreSQL 18's built-in `uuidv7()`; a 12-line SQL shim in the init script
  makes it run on the shared `postgres:16-alpine`. Moving the shared container to 18 is a separate decision (the
  `postgres:18` image changed its data-directory layout; four other components' migrations are untested on it).
  Reset on start: drop and recreate the `public` schema in a localtest `IHostedLifecycleService.StartingAsync`, then
  the production migrator rebuilds it. Redis is required (HotChocolate subscriptions connect eagerly through
  `ConnectionMultiplexer.Connect`); one `redis` compose service.
- Effort: about 5 to 7 working days for the component alone (section 14), plus the Resource Registry strand, which
  gates every service-owner call that creates a dialog.

## 0. Corrections to the earlier report (2026-10-18)

| Claim there | Status today |
| --- | --- |
| "the cookie token has no such scope today" (section 4.8) | Wrong. Authentication's `GeneralSettings.DefaultPortalScopes` is `openid digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read` ([`GeneralSettings.cs:168`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Configuration/GeneralSettings.cs#L168)) and the decoded token from `/Home/GetTestUserToken?userId=1337` carries it. What the cookie token lacks is `pid` (section 7). |
| `DisableCache=true` to avoid the 15-minute authorization caches (section 4.4 table) | Ineffective: the flag replaces the default `IFusionCache` ([`InfrastructureExtensions.cs:379`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L379)); the PDP, authorized-parties, subject-resource, resource-registry and metadata caches are named caches resolved through `IFusionCacheProvider.GetCache(...)` ([`AltinnAuthorizationClient.cs:75-82`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L75-L82)). No production class injects the default `IFusionCache` (grep). A cache-clearing localtest endpoint is the replacement (section 5.3). |
| P1 "Maskinporten token endpoint in the authentication component" | Not needed for Dialogporten: all three Maskinporten-backed clients exchange to an Altinn token (`ExhangeToAltinnToken = true`, [`InfrastructureExtensions.cs:525-564`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L525-L564)), which is exactly what `/Home/GetTestOrgToken` returns. The fake `IMaskinportenService` calls that (section 5.2). Whether the adapter or Correspondence strands still want a token endpoint is theirs to say. The fake Maskinporten and `/Home` have moved from the authentication component to the localtest app (`localtest/localtest/Maskinporten/MaskinportenApi.cs`). |
| "Build three images from one tree ... optionally a fourth for Janitor" | Three hosts, no Janitor image (section 2 and 5.3). |
| P4 "Postgres 18 ... a shim diverges from production" | Both options stand; the recommendation is the shim now (section 6.1). |
| Directive-site list (replace `ITokenIssuerCache`, `RequireHttpsMetadata=false`, replace `IMaskinportenService`) | Confirmed, with two precisions: `RequireHttpsMetadata` must be set with `ConfigureAll`, not `PostConfigureAll` (section 5.1), and the GraphQL host has its own `ITokenIssuerCache` type, so two small fakes (section 5.2). |
| Ingress "`/dialogporten/graphql` → GraphQL, rewritten to `/graphql` as APIM does" | Confirmed; the localtest proxy passes prefixes through unchanged today, so the rewrite is a small change in `localtest/localtest/Proxy/` (section 8). |
| "App-runner step: run `dialogporten-janitor sync-...`" | Replaced by an HTTP push to the WebApi's localtest endpoint (section 12). |
| Resource cost "1.5 to 2.5 GB more" for the whole inbox chain | For this component: three .NET hosts and Redis, roughly 0.6 to 1 GB by analogy with the running components (section 9; not measured). |

## 1. What to vendor and how it builds

### 1.1 Paths

Copy from `Altinn/dialogporten` at `0946da8` (later `1a7c584`, section 10), relative layout preserved under
`components/dialogporten/vendor/`:

| Path | Why |
| --- | --- |
| `Directory.Build.props`, `.editorconfig`, `global.json` | The build chain ([`Directory.Build.props`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/Directory.Build.props), [`global.json`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/global.json)). `.editorconfig` because `EnforceCodeStyleInBuild=true` reads it (the Dockerfiles copy it first). `global.json` for anyone building on the host; the image gets its SDK from the base image |
| `src/Digdir.Domain.Dialogporten.WebApi/` | Host 1 (REST, `UsePathBase("/dialogporten")`, well-known endpoints) |
| `src/Digdir.Domain.Dialogporten.GraphQL/` | Host 2 (HotChocolate, end-user only, subscriptions) |
| `src/Digdir.Domain.Dialogporten.Service/` | Host 3 (MassTransit consumers: Altinn Events forwarding, search index, cache invalidation, GraphQL subscription publishing) |
| `src/Digdir.Domain.Dialogporten.Janitor/` | Copy for completeness (the subtree stays whole), **not built** (section 5.3) |
| `src/Digdir.Domain.Dialogporten.Application/`, `.Domain/`, `.Infrastructure/` | The libraries; Infrastructure holds the EF migrations (71 files), the embedded `Persistence/Sql/**/*.sql` and the Altinn clients |
| `src/Digdir.Library.Entity.Abstractions/`, `src/Digdir.Library.Entity.EntityFrameworkCore/`, `src/Digdir.Library.Utils.AspNet/` | Referenced by relative path from the hosts and libraries |
| `src/Digdir.Domain.Dialogporten.Infrastructure/MigrationBundle.dockerfile`, the four `Dockerfile`s | Stay inside their project folders as upstream reference for the component `Dockerfile` (section 2) |
| Every `packages.lock.json` next to its csproj | Required by `--locked-mode` (section 1.3) |

Leave out: `src/Digdir.Library.Dialogporten.WebApiClient.*.WebApiSample/`, `src/Digdir.Tool.Dialogporten.*`,
`src/WebApiClient/` (its own `Directory.Build.props`, NuGet-published client), `tests/`, `docs/`, `scripts/`,
`local-otel-configuration/`, the root `docker-compose*.yml` and `nginx-*.conf` (keep a copy of the two nginx files
only if the proxy section needs them as reference; they are plain pass-throughs), `.github/`, `sonar-project.properties`,
`release-please-config.json`, `Digdir.Domain.Dialogporten.slnx*`.

### 1.2 Build chain facts

| Fact | Where | Consequence |
| --- | --- | --- |
| `TargetFramework=net10.0`, `Nullable`, `ImplicitUsings`, `RestorePackagesWithLockFile=true` | [`Directory.Build.props:3-9`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/Directory.Build.props#L3-L9) | Lock files are regenerated on an ordinary restore; `--locked-mode` (the Dockerfiles) refuses any change to the dependency graph |
| `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisMode=Recommended`, `EnableNETAnalyzers`, `AnalysisLevel=10`; NU1901-NU1904 kept as warnings | [`Directory.Build.props:11-19`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/Directory.Build.props#L11-L19) | Only the SDK's built-in analyzers; no StyleCop or Sonar package (grep of every csproj). The localtest props relaxes `TreatWarningsAsErrors` for the vendored projects so the fakes do not have to satisfy `Recommended` style rules |
| No `Directory.Packages.props` (no central package management) | root listing | Nothing to bypass for fakes |
| SDK `10.0.401`, `rollForward: disable` | [`global.json`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/global.json) | The image must carry exactly 10.0.401 (`mcr.microsoft.com/dotnet/sdk:10.0.401`, as upstream). On this machine the 10.0.401 SDK is installed; `dotnet` picks it only when run from a directory under `global.json` (verified: from the component root it picked 11.0.100-rc.2 and still built, with a preview warning) |
| WebApi `GenerateDocumentationFile` + `NoWarn 1591`; an `NSwag` target runs `aspnetcore2openapi` after a Release build | [`Digdir.Domain.Dialogporten.WebApi.csproj:44-52`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Digdir.Domain.Dialogporten.WebApi.csproj#L44-L52) | The target boots the host's DI inside the build with `ASPNETCORE_ENVIRONMENT=Development` to generate three OpenAPI documents. It ran fine in the scratch build with `LOCALTEST` defined. Fakes must therefore not connect to anything in service registration (they do not). If a future fake breaks it, a target named `NSwag` in `localtest.build.targets` overrides the csproj's (last definition wins) and is the documented fallback |
| Maskinporten client is a NuGet (`Altinn.ApiClients.Maskinporten` 10.1.0) | [`Digdir.Domain.Dialogporten.Infrastructure.csproj:4`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Digdir.Domain.Dialogporten.Infrastructure.csproj#L4) | Its `IMaskinportenService` is reachable from fakes compiled into Infrastructure or a host (verified in the scratch build) |
| `Microsoft.IdentityModel.Protocols.OpenIdConnect` is in the hosts' graphs (via `Microsoft.AspNetCore.Authentication.JwtBearer`) but **not** in Infrastructure's | scratch build: a probe in `fakes/Infrastructure/` failed with CS0234, the same probe in `fakes/WebApi/` compiled | The token-issuer-cache fakes live in the host fakes folders |

### 1.3 `localtest.build.props` / `localtest.build.targets` (verified)

Scratch setup: `git archive` of the WebApi closure into `vendor/`, the events component's props/targets with the
project names changed, two probe files under `fakes/`. Results:

- `dotnet restore <WebApi>.csproj --locked-mode` with `Compile Include` of `fakes/**` → success (lock files untouched).
- `dotnet build -c Release --no-restore` from `vendor/` with SDK 10.0.401 → `Build succeeded`, 0 errors; the probe's
  `#if LOCALTEST` branch was the one compiled (checked with `ilspycmd` on the output); the `NSwag` target ran three
  times and wrote the documents.
- The same with a `<ProjectReference Include="…/fakes-proj/Dialogporten.Localtest.csproj" />` added from the targets
  file → `error NU1004: A new project reference to Dialogporten.Localtest was found for net10.0 target framework. The
  packages lock file is inconsistent with the project dependencies so restore can't be run in locked mode`.
- Scratch footprint: 860 MB NuGet cache, 20 MB tree; restore 20 s, build 40 s on this machine. Deleted afterwards.

Therefore: fakes are compiled into the hosts and Infrastructure by `Compile Include`; no `PackageReference Remove`
(Azure packages stay in the graph, unused at runtime, as upstream's local development does); no fakes csproj.

`localtest.build.props` (identical to the events component's except the comment):

```xml
<Project>
  <PropertyGroup>
    <LocaltestIsVendoredProject>$([System.String]::new('$(MSBuildProjectDirectory)').StartsWith('$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)vendor'))'))</LocaltestIsVendoredProject>
    <LocaltestVendoredBuildProps Condition="'$(LocaltestIsVendoredProject)' == 'true'">$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildProjectDirectory)'))</LocaltestVendoredBuildProps>
  </PropertyGroup>
  <!-- Upstream's Directory.Build.props (net10.0, lock files, analyzers) is imported first; LOCALTEST is layered on top. -->
  <Import Project="$(LocaltestVendoredBuildProps)" Condition="'$(LocaltestVendoredBuildProps)' != '' And Exists('$(LocaltestVendoredBuildProps)')" />
  <PropertyGroup Condition="'$(LocaltestIsVendoredProject)' == 'true'">
    <DefineConstants>$(DefineConstants);LOCALTEST</DefineConstants>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

`localtest.build.targets`:

```xml
<Project>
  <PropertyGroup>
    <LocaltestVendoredBuildTargets Condition="'$(LocaltestIsVendoredProject)' == 'true'">$([MSBuild]::GetPathOfFileAbove('Directory.Build.targets', '$(MSBuildProjectDirectory)'))</LocaltestVendoredBuildTargets>
  </PropertyGroup>
  <Import Project="$(LocaltestVendoredBuildTargets)" Condition="'$(LocaltestVendoredBuildTargets)' != '' And Exists('$(LocaltestVendoredBuildTargets)')" />
  <!-- Upstream restores with --locked-mode: a ProjectReference or PackageReference here fails restore (NU1004), so the
       fakes are compiled into the assemblies that own the interfaces they replace. -->
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Digdir.Domain.Dialogporten.Infrastructure'">
    <Compile Include="$(MSBuildThisFileDirectory)fakes/Infrastructure/**/*.cs" LinkBase="Localtest" />
    <EmbeddedResource Include="$(MSBuildThisFileDirectory)fakes/Infrastructure/**/*.sql" LinkBase="Localtest" LogicalName="Localtest.%(Filename)%(Extension)" />
  </ItemGroup>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Digdir.Domain.Dialogporten.WebApi'">
    <Compile Include="$(MSBuildThisFileDirectory)fakes/WebApi/**/*.cs" LinkBase="Localtest" />
  </ItemGroup>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Digdir.Domain.Dialogporten.GraphQL'">
    <Compile Include="$(MSBuildThisFileDirectory)fakes/GraphQL/**/*.cs" LinkBase="Localtest" />
  </ItemGroup>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Digdir.Domain.Dialogporten.Service'">
    <Compile Include="$(MSBuildThisFileDirectory)fakes/Service/**/*.cs" LinkBase="Localtest" />
  </ItemGroup>
</Project>
```

The Infrastructure fakes (Maskinporten, the reset script, the shared `LocaltestDialogportenOptions`) are visible to
all three hosts; the host fakes hold what only that host's assembly can see (`ITokenIssuerCache`, FastEndpoints
endpoints, GraphQL types).

## 2. Dockerfile

Upstream has one Dockerfile per host, identical except the project name
([WebApi](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Dockerfile),
[GraphQL](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Dockerfile),
[Service](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Service/Dockerfile),
[Janitor](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Dockerfile)):
`FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS base`
(`EXPOSE 8080` in the two web hosts), `FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build`,
copy `.editorconfig` and `Directory.Build.props`, copy every csproj and `packages.lock.json` of the closure,
`dotnet restore … --locked-mode`, copy `src/`, `dotnet publish … -c Release -o /app/publish /p:UseAppHost=false --no-restore`
(the Janitor's line lacks `/p:UseAppHost=false`), `USER $APP_UID`, `ENTRYPOINT ["dotnet", "<Project>.dll"]`.

**One component `Dockerfile` with a build argument**, mirroring the WebApi Dockerfile line for line, selects the host:

```dockerfile
# Mirrors vendor/src/Digdir.Domain.Dialogporten.WebApi/Dockerfile (the GraphQL and Service ones differ only in the
# project name). localtest: PROJECT picks the host; compose builds the same context three times.
ARG PROJECT=WebApi
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.401@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
ARG PROJECT
WORKDIR /src
# localtest: the component layout (vendor/ next to the overrides and fakes/); the two variables redirect MSBuild's
# Directory.Build.props/targets lookup to the overrides, which import upstream's props and define LOCALTEST.
COPY localtest.build.props localtest.build.targets ./
COPY fakes/ fakes/
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
WORKDIR /src/vendor
COPY ["vendor/.editorconfig", "."]
COPY ["vendor/Directory.Build.props", "."]
COPY ["vendor/global.json", "."]                                   # localtest: pins the SDK the image carries anyway
# Main project and dependencies (csproj + packages.lock.json for each, as upstream)
COPY ["vendor/src/Digdir.Domain.Dialogporten.${PROJECT}/Digdir.Domain.Dialogporten.${PROJECT}.csproj", "src/Digdir.Domain.Dialogporten.${PROJECT}/"]
COPY ["vendor/src/Digdir.Domain.Dialogporten.${PROJECT}/packages.lock.json", "src/Digdir.Domain.Dialogporten.${PROJECT}/"]
# … the six library csproj/lock pairs exactly as upstream lists them …
RUN dotnet restore "src/Digdir.Domain.Dialogporten.${PROJECT}/Digdir.Domain.Dialogporten.${PROJECT}.csproj" --locked-mode
COPY ["vendor/src/", "src/"]
WORKDIR "/src/vendor/src/Digdir.Domain.Dialogporten.${PROJECT}"
RUN dotnet publish "Digdir.Domain.Dialogporten.${PROJECT}.csproj" -c Release -o /app/publish /p:UseAppHost=false --no-restore

FROM base AS final
ARG PROJECT
WORKDIR /app
USER $APP_UID
COPY --from=build /app/publish .
# localtest: ENTRYPOINT cannot expand ARG; the shell form reads the baked environment variable.
ENV LOCALTEST_PROJECT=${PROJECT} ASPNETCORE_ENVIRONMENT=Development
ENTRYPOINT ["sh", "-c", "exec dotnet Digdir.Domain.Dialogporten.${LOCALTEST_PROJECT}.dll"]
```

Compose: `build: { context: ./components/dialogporten, args: { PROJECT: WebApi } }` and the same with `GraphQL` and
`Service`; image tags `localhost/localtest-dialogporten-webapi:dev` etc. Podman builds the shared stages once per
distinct `PROJECT` (the `COPY vendor/src/` layer is shared; restore and publish are not). Two deviations to mark:
`ASPNETCORE_ENVIRONMENT=Development` baked in (upstream sets it in compose), and the shell-form entrypoint. No
`HEALTHCHECK` instruction (podman's OCI images drop it; compose `healthcheck:` instead, section 9).

Migrations: no `MigrationBundle.dockerfile` mirror. In `Development` every host runs
[`DevelopmentMigratorHostedService`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Development/DevelopmentMigratorHostedService.cs#L21-L31)
(`Database.MigrateAsync` at start), so the first host to start migrates and the others find the schema current. The
compose file orders this: `dialogporten-graphql` and `dialogporten-service` depend on `dialogporten-webapi` with
`condition: service_healthy`. The WebApi is also where the localtest reset runs (section 6.2), so there is exactly one
writer of the schema at start. If the team prefers the production path, the bundle Dockerfile mirrors
[`MigrationBundle.dockerfile`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/MigrationBundle.dockerfile#L9-L10)
(installs `dotnet-ef` 10.0.12, `dotnet ef migrations bundle`) and runs as a one-shot compose service with
`service_completed_successfully`; it is a second image build of the same closure and gains nothing locally.

## 3. `LocalDevelopment` flags and feature toggles

All flags are read only when `ASPNETCORE_ENVIRONMENT=Development`
([`InfrastructureExtensions.cs:369-380`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L369-L380),
[`ApplicationExtensions.cs:93-112`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/ApplicationExtensions.cs#L93-L112)),
which localtest needs anyway for the in-memory bus and the migrator. Every flag defaults to `true`
([`LocalDevelopmentSettings.cs:5-22`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/LocalDevelopmentSettings.cs#L5-L22)),
and the hosts' `appsettings.Development.json` set most of them to `true` explicitly, so the localtest configuration
(section 11) sets each one.

| Flag | Localtest value | Read at | `true` selects | `false` selects |
| --- | --- | --- | --- | --- |
| `UseLocalDevelopmentUser` | `false` | WebApi [`Program.cs:200`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Program.cs#L200), GraphQL [`Program.cs:153`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L153), Application [`ApplicationExtensions.cs:99-107`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/ApplicationExtensions.cs#L99-L107) | `IUser` = a fixed `LocalDevelopmentUser`; decorators that fake the user's resources and name | `ApplicationUser` from the HTTP principal |
| `DisableAuth` | `false` | WebApi [`Program.cs:201-203`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Program.cs#L201-L203), GraphQL [`Program.cs:154-155`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L154-L155) | `AllowAnonymousHandler` for every policy; WebApi also swaps `ITokenIssuerCache` for `DevelopmentTokenIssuerCache` (issuer `null`, so scheme selection never rejects) | Production policies and the real issuer cache |
| `UseLocalDevelopmentAltinnAuthorization` | `false` | [`InfrastructureExtensions.cs:378`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L378) | `LocalDevelopmentAltinnAuthorization`: allow everything, parties from the dialogs table | `AltinnAuthorizationClient`: PDP and Access Management (section 4) |
| `UseLocalDevelopmentResourceRegister` | `false` (needs the Resource Registry strand; `true` is the fallback that makes service-owner dialog creation work without one) | [`InfrastructureExtensions.cs:377`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L377), also `ApplicationExtensions.cs:99-102` | `LocalDevelopmentResourceRegistry`: any resource id is accepted, owner `742859274`/`ttd` or the first seen, empty sync feeds | `ResourceRegistryClient` over HTTP |
| `UseLocalDevelopmentCloudEventBus` | `false` | [`InfrastructureExtensions.cs:376`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L376) | `ConsoleLogEventBus` (logs the event type) | `AltinnEventsClient`: `POST events/api/v1/events` with the Maskinporten-exchanged token |
| `DisableCache` | `true` (default; no effect on the clients) | [`InfrastructureExtensions.cs:379`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L379), [`FusionCacheWarmupHostedService.cs:36`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/FusionCache/FusionCacheWarmupHostedService.cs#L36) | Default `IFusionCache` = `NullFusionCache` (nobody injects it); the backplane warmup ping is skipped | A real default cache; one Redis ping at start |
| `UseLocalDevelopmentNameRegister` | `false` | [`ApplicationExtensions.cs:104-107`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/ApplicationExtensions.cs#L104-L107) | `LocalDevelopmentUserRegistryDecorator` (fake user name) | `UserRegistry` → `IPartyNameRegistry` (Register) |
| `UseLocalDevelopmentPartyNameRegistry` | `false` | [`InfrastructureExtensions.cs:380`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L380) | `LocalPartNameRegistryClient` ("Gunnar Gunnarson") | `PartyNameRegistryClient`: Register `dialogporten/parties/query` |
| `UseLocalDevelopmentOrganizationRegister` | irrelevant | **read nowhere** (grep of `src/`); `ServiceOwnerNameRegistryClient` is always the CDN client | – | – |
| `UseLocalDevelopmentCompactJwsGenerator` | `false` | [`ApplicationExtensions.cs:109-110`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/ApplicationExtensions.cs#L109-L110) | Dialog tokens are the string `local-development-jws` | `Ed25519Generator` with the configured key pair (section 8.3); the GraphQL host validates dialog tokens against the same keys regardless of the flag |
| `UseInMemoryServiceBusTransport` | `true` | [`InfrastructureExtensions.cs:460-463`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L460-L463) and [`504-507`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L504-L507) | MassTransit `UsingInMemory`; the EF Core outbox in Postgres stays, WebApi/GraphQL write it with delivery disabled, the Service host's delivery service drains it into its in-process bus and consumers | Azure Service Bus (`Infrastructure:MassTransit:Host`) |
| `DisableSubjectResourceSyncOnStartup` | `true` | [`DevelopmentSubjectResourceSyncHostedService.cs:29`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Development/DevelopmentSubjectResourceSyncHostedService.cs#L29) | Nothing at start | Every host sends `SyncSubjectMapCommand` at start (three concurrent syncs against the Resource Registry). The app runner triggers the sync instead (section 12) |
| `DisablePolicyInformationSyncOnStartup` | `true` | [`DevelopmentResourcePolicyInformationSyncHostedService.cs:29`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Development/DevelopmentResourcePolicyInformationSyncHostedService.cs#L29) | Nothing at start | `SyncPolicyCommand` at start: fetches *every* resource's policy XML (15 concurrent requests) |
| `UseLocalMetricsAggregationStorage` | irrelevant | Janitor only ([`Program.cs:101-106`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Program.cs#L101-L106), [`Commands.cs:115`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Commands.cs#L115)) | Azurite blob client | Azure Storage |

Other `Development`-conditioned behaviour, all wanted: `DevelopmentCleanupOutboxHostedService` deletes outbox rows
older than 12 h at start; `EnableSensitiveDataLogging`; no `DelayedShutdownHostLifetime`; the `yt01` environment
check ([`InfrastructureExtensions.cs:364-367`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L364-L367)) is moot.

Feature toggles (`Application:FeatureToggle`, [`ApplicationSettings.cs:16-24`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/ApplicationSettings.cs#L16-L24)),
all default `false` and left so: `UseAltinnAutoAuthorizedPartiesQueryParameters` (adds `includePartiesViaKeyRoles=auto…`
to the authorized-parties URL; the vendored Access Management accepts both forms), `EnablePartyFiltersForSystemUsers`,
`EnablePartyFiltersForEmailUsers`, `EnablePartyCacheForEmailUsers`, `EnableGraphQlAuthorizedServiceResources`,
`UseCorrectPersonNameOrdering` (person names come back "Last First" for Arbeidsflate; set `true` only if the
Arbeidsflate strand's pin expects the corrected order).

Azure is off by configuration, no directive: App Configuration and Key Vault only when `AZURE_APPCONFIG_URI` is set
([`AzureAppConfigurationExtensions.cs:29-32`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Library.Utils.AspNet/AzureAppConfigurationExtensions.cs#L29-L32)),
OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is a URL ([`OpenTelemetryExtensions.cs:33`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Library.Utils.AspNet/OpenTelemetryExtensions.cs#L33)),
Serilog to the console otherwise. Warmup (`Infrastructure:Warmup`) is switched off: its optional phases call the
Resource Registry and run an end-user search as the synthetic pid `14886498226`
([`InfrastructureSettings.cs:33`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs#L33),
[`WarmupPhases.cs:92-113`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/HealthChecks/WarmupPhases.cs#L92-L113)),
and the package gates readiness on it (not verified in the package).

## 4. Outbound dependencies

Every client takes its base address from `Infrastructure:Altinn:BaseUri` (= `http://local.altinn.cloud:8000/`), the
events client from `Infrastructure:Altinn:EventsBaseUri`, the CDN client from `Infrastructure:AltinnCdn:BaseUri`
([`InfrastructureExtensions.cs:521-567`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L521-L567)).
Three clients go through the Maskinporten handler with `ExhangeToAltinnToken = true` (`ICloudEventBus`,
`IPartyNameRegistry`, `IAltinnAuthorization`), so what reaches the platform is an Altinn token with the configured
scopes (`Infrastructure:Maskinporten:Scope`, upstream's development value: `altinn:events.publish
altinn:events.publish.admin altinn:register/partylookup.admin altinn:authorization/authorize.admin
altinn:accessmanagement/authorizedparties.admin`). The PDP and name-registry clients also send
`Ocp-Apim-Subscription-Key` (`Infrastructure:Altinn:SubscriptionKey`; set a dummy so the header value is not null).
"Served today" was checked with `GET /Home/GetTestOrgToken?orgNumber=991825827&scopes=<those scopes>`, whose
exchanged token carries `scope`, `consumer` (`0192:991825827`), `urn:altinn:orgNumber` and issuer
`http://local.altinn.cloud:8000/authentication/api/v1/openid/`.

| Dependency | Call Dialogporten makes | Token | Served today | Gap |
| --- | --- | --- | --- | --- |
| Authentication, OpenID metadata | `GET` the configured well-known of each scheme (`WebApi:Authentication:JwtBearerTokenSchemas[*].WellKnown`) through `ConfigurationManager<OpenIdConnectConfiguration>` twice: JwtBearer's own metadata manager and [`TokenIssuerCache.cs:58-64`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/TokenIssuerCache.cs#L58-L64) (issuer per scheme). Also as soft health probes | none | `http://local.altinn.cloud:8000/authentication/api/v1/openid/.well-known/openid-configuration` (issuer `…/openid/`, `jwks_uri` under it) and the fake Maskinporten's `…/localtest/api/maskinporten/.well-known/oauth-authorization-server` (issuer `…/localtest/api/maskinporten/`), both checked | Both retrievers refuse plain http (section 5.1) |
| PDP | `POST authorization/api/v1/authorize`, XACML JSON ([`AltinnAuthorizationClient.cs:29`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L29), [`DecisionRequestHelper.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/DecisionRequestHelper.cs)): subject = the user's claims (`pid`/`urn:altinn:person:identifier-no`, system user, …), action per check, resource = `urn:altinn:resource:<id>` plus `urn:altinn:org`/`urn:altinn:app` for `app_{org}_{app}`, party attributes; one multi-request per dialog-details authorization, results correlated by count | exchanged org token, scope `altinn:authorization/authorize.admin` | Yes: the vendored PDP's `authorize` route answered 200 to an XACML request with the org token | None known. The decisions themselves need the app policy pushed by the runner and the Register roles (as for apps) |
| Access Management, authorized parties | `POST accessmanagement/api/v1/resourceowner/authorizedparties?includeAltinn2=true&includeAccessPackages=&includeRoles=&includeResources=&includeInstances=` with body `{ "type": "urn:altinn:person:identifier-no", "value": "<pid>", "partyFilter": [] }` ([`AltinnAuthorizationClient.cs:450-461`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L450-L461), [`AuthorizedPartiesRequest.cs:17-29`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AuthorizedPartiesRequest.cs#L17-L29)); mapped by [`AuthorizedPartiesHelper.cs:34-77`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AuthorizedPartiesHelper.cs#L34-L77) (`type` Person/Organization/SelfIdentified, `authorizedRoles`, `authorizedAccessPackages`, `authorizedResources`, `authorizedInstances`, `subunits`). An empty list for a person is an `UpstreamServiceException` ([`:218-232`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Authorization/AltinnAuthorizationClient.cs#L218-L232)) | exchanged org token; the vendored AM policy accepts `altinn:accessmanagement/authorizedparties.resourceowner` or `.admin` ([`AccessManagementHost.cs:389`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/AccessManagementHost.cs#L389), [`AuthorizedPartiesController.cs:53-54`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Api.ServiceOwner/Controllers/AuthorizedPartiesController.cs#L53-L54)) | Yes: for Sophie Salt (`01839012345`) it returned her person party and four organisations with lower-case roles (`dagl`, `regna`, `a0212`, …), access packages (`a-ordning`, …) and sub-units, 200 | `authorizedResources` and `authorizedInstances` were empty; search authorization is built from roles and packages × subject-resource mappings, so that is fine until instance delegations matter |
| Access Management, metadata | `GET accessmanagement/api/v1/meta/info/roles` and `…/meta/info/accesspackages/export`, `Accept-Language` nb/en/nn ([`AccessManagementMetadataClient.cs:11-12`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/AccessManagement/AccessManagementMetadataClient.cs#L11-L12)); also the WebApi's hard health probe | none (plain `AddHttpClient`) | Yes: both 200, anonymous | – |
| Register | `POST register/api/v1/dialogporten/parties/query` body `{ "data": ["urn:altinn:person:identifier-no:<pid>"] }` (or an organisation or system-user URN), reads `data[0].displayName` ([`PartyNameRegistryClient.cs:110-113`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/NameRegistry/PartyNameRegistryClient.cs#L110-L113)) | exchanged org token, scope `altinn:register/partylookup.admin` (policy `InternalOrPlatformAccess`, [`DialogPortenController.cs:16-17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs#L16-L17)) | Yes: `{"data":[{"partyType":"person","personIdentifier":"01839012345",…,"displayName":"Sophie Salt"}]}`, 200 | – |
| Resource Registry | `GET resourceregistry/api/v1/resource/resourcelist?includeMigratedApps=true` (fields `identifier`, `hasCompetentAuthority.organization` + `.orgcode`, `resourceType` ∈ `GenericAccessResource`/`AltinnApp`/`MigratedApp`/`CorrespondenceService`, `delegable`, `status`, `title`, `description`; [`ResourceRegistryClient.cs:209-237`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L209-L237), [`Constants.cs:36-42`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/Constants.cs#L36-L42)); `GET …/resource/updated?since=<ISO>&limit=<n>` paged by `links.next` with `data: [{ subjectUrn, resourceUrn, updatedAt, deleted }]` ([`:90-111`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L90-L111), [`IResourceRegistry.cs:50`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Externals/IResourceRegistry.cs#L50)); `GET …/resource/{id}/policy` (XACML XML, read for the `urn:altinn:minimum-authenticationlevel` obligation, [`:179-207`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/ResourceRegistry/ResourceRegistryClient.cs#L179-L207)) | none | **No**: `/resourceregistry/` is not a proxy route (404) | The Resource Registry strand. The service-owner API validates every `serviceResource` against `resourcelist` and refuses resources whose `organization` is not the token's consumer org ([`IServiceResourceAuthorizer.cs:53-85`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/IServiceResourceAuthorizer.cs#L53-L85)); search authorization is roles × `updated` feed. Needed from that strand: `app_{org}_{app}` resources with `hasCompetentAuthority.organization` = the org number the runner's token uses (`ttd` ↔ `991825827`, the convention Access Management's stand-in already follows), `resourceType: AltinnApp`, the `updated` feed with `urn:altinn:rolecode:<code>` / `urn:altinn:accesspackage:<urn>` subjects taken from the pushed app policies, and the policy endpoint |
| Events | `POST events/api/v1/events`, `application/cloudevents+json`, `source` `{Application:Dialogporten:BaseUri}/api/v1/enduser/dialogs/<id>`, `resource` the dialog's service resource, `subject` the party ([`AltinnEventsClient.cs:17-24`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/Events/AltinnEventsClient.cs#L17-L24), [`DomainEventToAltinnForwarderBase.cs:24-25`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/Common/Events/AltinnForwarders/DomainEventToAltinnForwarderBase.cs#L24-L25)); sent by the Service host's consumers | exchanged org token with `altinn:events.publish` **and** `altinn:events.publish.admin` | Yes, with the admin scope: the vendored Events short-circuits the PDP for `altinn:events.publish.admin` ([`AuthorizationService.cs:119`](https://github.com/Altinn/altinn-events/blob/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6/src/Events/Services/AuthorizationService.cs#L119)). A probe with only `altinn:events.publish` got 403 (the PDP decision for a Digdir consumer on `app_ttd_testapp`), so keep upstream's development scope list, which has both | The events component counts every `local.altinn.cloud` source as an app event (its README); Dialogporten's sources are `…/dialogporten/api/v1/enduser/dialogs/…`. Whether its app-source filters mis-classify them is for the events component to confirm |
| Altinn CDN | `GET orgs/altinn-orgs.json` on `Infrastructure:AltinnCdn:BaseUri` ([`ServiceOwnerNameRegistryClient.cs:57`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Altinn/OrganizationRegistry/ServiceOwnerNameRegistryClient.cs#L57)) for service-owner names in the resource metadata catalogue and identifier lookups; also a soft health probe | none | Only the internet: production Authentication's `exchange/maskinporten` reads the same file from `altinncdn.no` today (`localtest/localtest/README.md`) | Keep `https://altinncdn.no/` as the default (offline: 24 h fail-safe cache, then errors in the metadata catalogue only). A static copy served by the localtest app under `localtest/api/cdn/` would remove the internet dependency for both; it belongs to the localtest app, not this component |
| Maskinporten | `POST https://test.maskinporten.no/token` then `…/authentication/api/v1/exchange/maskinporten` on `platform.{env}.altinn.cloud`, hard-coded per environment ([`MaskinportenService.cs:404-445`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Services/MaskinportenService.cs#L404-L445)) | the configured JWK | No | Replace `IMaskinportenService` (section 5.2). The library registers it with `TryAddSingleton` ([`ServiceCollectionExtensions.cs:170`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Extensions/ServiceCollectionExtensions.cs#L170)) and the handler calls `GetToken(IClientDefinition, MaskinportenRequestContext, bool)` ([`MaskinportenTokenHandler.cs:55-66`](https://github.com/Altinn/altinn-apiclient-maskinporten/blob/15d3c63a1f320816acccefd1f221d936ccb286db/src/Altinn.ApiClients.Maskinporten/Handlers/MaskinportenTokenHandler.cs#L55-L66)) |
| Redis | FusionCache L2 and backplane, HotChocolate subscriptions (section 6.3) | – | No Redis in the stack | New compose service |
| Postgres | `Infrastructure:DialogDbConnectionString` | – | Shared container | Database, roles, `uuidv7()` shim (section 6) |

## 5. Directive sites and fakes

### 5.1 Directive sites (one pure-addition hook per host)

The hooks go after the production registrations so `Replace` wins, and before `builder.Build()`.

1. WebApi [`Program.cs:193`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Program.cs#L193), after `.AddAuthorization();` and before the `if (builder.Environment.IsDevelopment())` block:

   ```csharp
   #if LOCALTEST
       Digdir.Domain.Dialogporten.WebApi.Localtest.LocaltestWebApi.Apply(builder);
   #endif
   ```

   `Apply` does: `services.ConfigureAll<JwtBearerOptions>(o => o.RequireHttpsMetadata = false)` (must be
   `ConfigureAll`, not `PostConfigureAll`: `JwtBearerPostConfigureOptions` builds the metadata `ConfigurationManager`
   with `RequireHttps = options.RequireHttpsMetadata`, and post-configure callbacks run in registration order, so one
   added here would run after it); `services.ReplaceSingleton<ITokenIssuerCache, LocaltestTokenIssuerCache>()`
   (production registers it with `AddSingleton` at
   [`AuthenticationBuilderExtensions.cs:26`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/AuthenticationBuilderExtensions.cs#L26));
   `LocaltestInfrastructure.Apply(services, configuration)` (shared part, below); the reset-on-start hosted service.
   The `LocalDevelopment` flags are left to configuration, so the Development block that follows is a no-op.
2. GraphQL [`Program.cs:147`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L147), same place, `LocaltestGraphQl.Apply(builder)`: the same `ConfigureAll`, the GraphQL host's own `ITokenIssuerCache` replaced ([`TokenIssuerCache.cs:53-54`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/TokenIssuerCache.cs#L53-L54), registered at [`AuthenticationBuilderExtensions.cs:31`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/AuthenticationBuilderExtensions.cs#L31)), `LocaltestInfrastructure.Apply`. The `Dialogporten` scheme (dialog tokens, [`:73-100`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Common/Authentication/AuthenticationBuilderExtensions.cs#L73-L100)) has no metadata address and needs nothing.
3. Service [`Program.cs:91`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Service/Program.cs#L91), after `.AddDialogportenHealthChecks(...)`: `LocaltestInfrastructure.Apply(builder.Services, builder.Configuration)` only. `app.UseHttpsRedirection()` at [`:95`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Service/Program.cs#L95) logs one warning without an https port and does nothing (not verified at runtime).

No other `#if` is needed: the hard-wired pieces (Azure, OTLP, bus transport, migrator, syncs) are all configuration or
environment switches (section 3). A smaller alternative for the issuer cache is an in-place
`#if !LOCALTEST … #else … #endif` around the two retriever lines in each `TokenIssuerCache.cs`; the model prefers a
DI replacement when an interface exists, so the fakes are the recommendation and the in-place directive the fallback.

### 5.2 Fakes

| File (under `fakes/`) | Replaces / adds | Shape | Size |
| --- | --- | --- | --- |
| `Infrastructure/LocaltestDialogportenOptions.cs` | – | `Localtest:Dialogporten:PlatformEndpoint` (default `http://local.altinn.cloud:8000/`), `OrgNumber` (`991825827`), `ResetOnStart` (`true`) | 30 lines |
| `Infrastructure/LocaltestInfrastructure.cs` | entry point | `Apply(IServiceCollection, IConfiguration)`: `RemoveAll<IMaskinportenService>()` + `AddSingleton<IMaskinportenService, LocaltestMaskinportenService>()`; binds the options; registers the HTTP client to the localtest app | 40 lines |
| `Infrastructure/Maskinporten/LocaltestMaskinportenService.cs` | `IMaskinportenService` (NuGet) | `GetToken(IClientDefinition def, MaskinportenRequestContext ctx, bool disableCaching)`: `GET {PlatformEndpoint}Home/GetTestOrgToken?orgNumber={OrgNumber}&scopes={def.ClientSettings.Scope}` → `TokenResponse { AccessToken, ExpiresIn }`, cached per scope string until `exp - 60 s` read from the JWT; the other overloads delegate or throw `NotSupportedException`. The exchange is already done by `/Home/GetTestOrgToken` (`exchange/maskinporten?test=true`, which also turns Digdir into `ttd`), matching `ExhangeToAltinnToken = true` | 90 lines |
| `Infrastructure/Startup/LocaltestDatabaseStartup.cs` + `reset-on-start.sql` + `prepare.sql` | – | `IHostedLifecycleService.StartingAsync` (runs before every `StartAsync`, so before the production migrator): when `ResetOnStart`, `DROP SCHEMA public CASCADE; CREATE SCHEMA public;` then `prepare.sql` (extensions and the `uuidv7()` shim, the same text as the init script). Registered only from the WebApi hook (one writer) | 60 lines + 15 lines SQL |
| `WebApi/Authentication/LocaltestTokenIssuerCache.cs` | `ITokenIssuerCache` (WebApi) | The production class with `new ConfigurationManager<OpenIdConnectConfiguration>(wellKnown, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = false })` | 45 lines |
| `GraphQL/Authentication/LocaltestTokenIssuerCache.cs` | `ITokenIssuerCache` (GraphQL) | Same, over `GraphQlSettings` | 45 lines |
| `WebApi/Localtest/SyncEndpoint.cs` | adds `POST /dialogporten/api/v1/localtest/sync` | A FastEndpoints `EndpointWithoutRequest`, `AllowAnonymous()`, auto-discovered by the host's assembly scan, so no second hook: sends `SyncSubjectMapCommand` then `SyncPolicyCommand` (the Janitor's two commands, [`Commands.cs:19-47`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Commands.cs#L19-L47)) through `ISender`, optional `?since=` | 50 lines |
| `WebApi/Localtest/CacheEndpoint.cs` | adds `POST /dialogporten/api/v1/localtest/caches/clear` | Clears the named FusionCaches that hold authorization data (`Authorization`, `AuthorizedPartiesResult`, `SubjectResource`, `ResourceRegistry`, `AccessManagementMetadata`, the resource-metadata catalogue) through `IFusionCacheProvider` and FusionCache's `ClearAsync()`; with the Redis backplane the clear reaches the GraphQL and Service hosts (FusionCache 2.x behaviour, not verified). Replaces the role the earlier report gave `DisableCache` | 40 lines |
| `WebApi/Localtest/ResetEndpoint.cs` | adds `DELETE /dialogporten/api/v1/localtest/dialogs` | Truncates the dialog tables for a test run without a restart (the same statement list as the events component keeps for its reset, generated from `DialogDbContext.Model`), then clears the caches | 50 lines |
| `WebApi/Localtest/InspectionEndpoints.cs` (optional, later) | adds `GET /dialogporten/api/v1/localtest/dialogs?party=&resource=` | Plain reads of `Dialog` for the inspection GUI, bypassing authorization | 80 lines |

Nothing in `Service/` or beyond the above is needed for the MVP; the `Service` folder exists so the targets file is
uniform.

### 5.3 Why no Janitor host

The Janitor is a Cocona console ([`Program.cs:47`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Program.cs#L47)) whose production jobs are the two Resource Registry syncs, `reindex-dialogsearch`, cost metrics (Azure Monitor, Blob, Parquet; `LogsQueryClient(new DefaultAzureCredential())` at [`:94`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Janitor/Program.cs#L94)) and custom metrics. The syncs are MediatR commands the WebApi can send (above); the search index is maintained by the Service host's `DialogSearchIndexer` consumer on every create and update, so a reindex is only for recovery; the metrics jobs are Azure-only. Running the Janitor would also mean a fourth image and `podman exec` from the app runner, against the "runner pushes over HTTP" rule.

## 6. Postgres and Redis

### 6.1 `uuidv7()` and the Postgres version

One migration, [`20260807085531_AddAuthorizationContext.cs:19`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/Migrations/20260807085531_AddAuthorizationContext.cs#L19),
declares `defaultValueSql: "uuidv7()"` (and the model snapshot mirrors it). Nothing else in the migrations or in
`Persistence/Sql/**/*.sql` needs PostgreSQL 17 or 18 (grep for `MERGE`, `JSON_TABLE`, virtual generated columns,
temporal constraints: none; `GENERATED ALWAYS AS IDENTITY`, `gen_random_uuid()`, `pg_trgm`, `btree_gin` are all in 16).
Upstream's compose and integration tests use `postgres:18.3` ([`docker-compose-db-redis.yml`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/docker-compose-db-redis.yml),
[`DialogApplication.cs:61`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/tests/Digdir.Domain.Dialogporten.Application.Integration.Tests/Common/DialogApplication.cs#L61)).

Recommendation: stay on the shared `postgres:16-alpine` and add a shim in the init script and in `prepare.sql`:

```sql
-- localtest: PostgreSQL 18 has uuidv7() built in; this is the common plpgsql equivalent for 16 (millisecond
-- timestamp in the first 48 bits, version 7, random tail). Dropped automatically when the container moves to 18.
CREATE OR REPLACE FUNCTION public.uuidv7() RETURNS uuid LANGUAGE sql VOLATILE PARALLEL SAFE AS $$
  SELECT encode(
    set_bit(set_bit(
      overlay(uuid_send(gen_random_uuid())
              placing substring(int8send((extract(epoch FROM clock_timestamp()) * 1000)::bigint) FROM 3)
              FROM 1 FOR 6),
      52, 1), 53, 1), 'hex')::uuid
$$;
```

The migration calls `uuidv7()` unqualified, so a function in `public` resolves. Moving the shared container to 18 is
a stack-wide decision: the `postgres:18` image moved `PGDATA` to `/var/lib/postgresql/18/docker` and expects the volume
at `/var/lib/postgresql` (the current compose mounts `/var/lib/postgresql/data`), and Storage's PL/pgSQL, Access
Management's, Events' and the workflow engine's migrations have not been run on 18 here. Cheap to try later; not a
prerequisite.

### 6.2 Database, roles, reset, migrator

`components/dialogporten/postgres-init/40-dialogporten.sh` in the style of `30-events.sh`: role `dialogporten`
(LOGIN, password `Password`), database `dialogporten` owned by it, in that database `CREATE EXTENSION IF NOT EXISTS
pg_trgm; CREATE EXTENSION IF NOT EXISTS btree_gin;` (both trusted, so the owner could also create them, which is what
`HasPostgresExtension` in [`DialogDbContext.cs:168-169`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/Persistence/DialogDbContext.cs#L168-L169)
makes the first migration do) and the shim. Idempotent, runnable by hand against an existing volume as the README
describes. Compose mounts it into `docker-entrypoint-initdb.d`. Upstream runs everything as one superuser locally;
there is no admin/app role split to mirror (the Azure path uses Entra tokens, `Infrastructure:DialogDbAuth:Mode`,
left at `Password`).

Reset on start (section 5.2): drop and recreate `public` in the WebApi's `StartingAsync`, then the production
`DevelopmentMigratorHostedService` applies all 71 migrations (a few seconds; not measured). Truncating 60-odd tables
in dependency order would be the events-component style, but the schema changes often upstream and a drop needs no
maintenance. MassTransit's outbox tables and the search tables are part of the same schema and come back with it.

### 6.3 Redis

| Host | Use | Where |
| --- | --- | --- |
| WebApi, GraphQL, Service | FusionCache distributed tier (`AddStackExchangeRedisCache`) and backplane for every named cache | [`InfrastructureExtensions.cs:178-180`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L178-L180) |
| WebApi, GraphQL (publishers), Service (publisher through `Lazy<ITopicEventSender>`), GraphQL (subscriber) | HotChocolate Redis subscriptions, `ConnectionMultiplexer.Connect` | [`:481-486`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L481-L486), [`:513-518`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L513-L518) |
| all | `redis` health check, Degraded only | [`:595-600`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureExtensions.cs#L595-L600) |

Not optional: the cache would degrade gracefully (2-second circuit breaker, memory tier), but the subscription
transport connects for real and the Service's consumers publish dialog updates through it. One compose service
`redis` (`docker.io/library/redis:7-alpine`, digest pinned at implementation; upstream's local compose uses
`redis:6.0-alpine`), no persistence, `Infrastructure__Redis__ConnectionString=redis:6379`. About 10 MB.

## 7. Tokens

| API | Scheme | Policy | What the token must carry |
| --- | --- | --- | --- |
| End user (`/dialogporten/api/v1/enduser/*`, GraphQL) | `Altinn` (or `Idporten` in production) | `EndUser`: authenticated + scope `digdir:dialogporten` or `digdir:dialogporten.noconsent` ([`AuthorizationOptionsSetup.cs:29-40`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authorization/AuthorizationOptionsSetup.cs#L29-L40)); then `UserTypeValidationMiddleware` allows only Person, SystemUser, IdportenEmailIdentifiedUser ([`UserTypeValidationMiddleware.cs:16-22`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/UserTypeValidationMiddleware.cs#L16-L22)) | `pid` for a person ([`ClaimsPrincipalExtensions.cs:296-323`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Extensions/ClaimsPrincipalExtensions.cs#L296-L323)), `acr` or `urn:altinn:authlevel` for the level ([`:221-243`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Extensions/ClaimsPrincipalExtensions.cs#L221-L243)), `iss` equal to the scheme's discovered issuer |
| Service owner (`/dialogporten/api/v1/serviceowner/*`) | `Maskinporten` or `Altinn` | `ServiceProvider`: scope `digdir:dialogporten.serviceprovider` + a parseable `consumer` claim (`iso6523-actorid-upis`, `0192:<orgnr>`) ([`:42-45`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authorization/AuthorizationOptionsSetup.cs#L42-L45)); search adds `.search`, admin `.admin`, transmissions `.changetransmissions`, correspondence `digdir:dialogporten.correspondence` ([`Constants.cs:45-85`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/Authorization/Constants.cs#L45-L85)) | No `pid` (with one it becomes `ServiceOwnerOnBehalfOfPerson`, which the `?enduserid=` query also produces, [`ServiceOwnerOnBehalfOfPersonMiddleware.cs:22-58`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/ServiceOwnerOnBehalfOfPersonMiddleware.cs#L22-L58)) |
| Dialog-token consumers (GraphQL `Dialogporten` scheme; the adapter and Correspondence outside) | Ed25519 JWS from `/dialogporten/api/v1/.well-known/jwks.json` | – | `iss` = `{Application:Dialogporten:BaseUri}/api/v1` ([`IDialogTokenGenerator.cs:125-139`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/IDialogTokenGenerator.cs#L125-L139)) |

Scheme selection: `JwtSchemeSelectorMiddleware` reads the token's `iss`, and each scheme's `OnMessageReceived`
accepts the token only when that issuer equals the one from its well-known
([`AuthenticationBuilderExtensions.cs:49-64`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/AuthenticationBuilderExtensions.cs#L49-L64)).
Localtest schemes: `Maskinporten` → `http://local.altinn.cloud:8000/localtest/api/maskinporten/.well-known/oauth-authorization-server`
(issuer `…/localtest/api/maskinporten/`; only raw, un-exchanged Maskinporten tokens use it), `Altinn` →
`http://local.altinn.cloud:8000/authentication/api/v1/openid/.well-known/openid-configuration` (issuer
`…/authentication/api/v1/openid/`). No `Idporten` scheme: the stack's user tokens are Altinn-issued.

How the stack's tokens map (decoded from the running stack on 2026-10-20):

- `GET /Home/GetTestOrgToken?orgNumber=991825827&scopes=digdir:dialogporten.serviceprovider digdir:dialogporten.serviceprovider.search`:
  issuer `…/authentication/api/v1/openid/`, `scope` as requested, `consumer {authority: iso6523-actorid-upis, ID: 0192:991825827}`,
  `urn:altinn:orgNumber`, `urn:altinn:authlevel: 3`. Passes `ServiceProvider` (and `Search`), user type
  `ServiceOwner` with org `991825827`. **The service-owner API works with the existing helper.** The resources it may
  reference must be owned by `991825827` in the Resource Registry.
- `GET /Home/GetTestUserToken?userId=1337&authenticationLevel=3`: issuer the same, `scope: "openid
  digdir:dialogporten.noconsent altinn:portal/enduser altinn:instances.read"`, `acr: idporten-loa-substantial`,
  `urn:altinn:authlevel: 3`, `urn:altinn:userid`, `urn:altinn:partyid`, `urn:altinn:party:uuid`, **no `pid`**. It is
  Authentication's `AltinnStudioRuntime` cookie token (the localtest app's `AuthenticationClient` extracts it from the
  production sign-in), and the cookie principal omits `pid` by design
  ([`ClaimsPrincipalBuilder.cs:202`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L202),
  `!isAuthCookie`). Dialogporten answers the `EndUser` policy with 200 and then 403 "unable to determine valid user
  type" from `UserTypeValidationMiddleware`. The access token Authentication's OIDC `token` endpoint issues does carry
  `pid` ([`:80`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Helpers/ClaimsPrincipalBuilder.cs#L80),
  from the session's `ExternalId = urn:altinn:person:identifier-no:<ssn>`,
  [`OidcServerService.cs:1353`](https://github.com/Altinn/altinn-auth/blob/3d51810374b255aeaa179993e53864d122586a65/src/apps/Altinn.Authentication/src/Altinn.Platform.Authentication/Services/OidcServerService.cs#L1353)),
  which is the token Arbeidsflate gets through the authorization-code flow. That flow needs the in-memory OIDC client,
  code and refresh-token repositories plus a seeded client (the earlier report's section 3.1, still valid, owned by the
  Arbeidsflate strand). **The end-user API therefore has no caller in the stack until that lands.** A localtest
  helper that drives the code flow headlessly for a seeded test client (`/Home/GetTestUserToken?client=…`, same
  mechanics as the existing sign-in) would give the smoke test and Playwright a `pid` token without Arbeidsflate; it
  belongs to the localtest app.
- `GET /Home/GetTestStudioToken`: not relevant to Dialogporten.

Smoke-test matrix: service-owner create/get/search/delete and the sync endpoint with the org token; `.well-known`
and GraphQL schema requests anonymously; end-user REST and GraphQL queries once the code-flow token exists.

## 8. Proxy, CORS, keys

### 8.1 Routes in `localtest/localtest/Proxy/PlatformRoutes.cs`

- Upstreams: `["dialogporten-webapi"] = "http://dialogporten-webapi:8080"`, `["dialogporten-graphql"] = "http://dialogporten-graphql:8080"`.
- Prefixes, in this order (matching is ordinal and first-wins): `("/dialogporten/graphql", "dialogporten-graphql")`
  before `("/dialogporten/", "dialogporten-webapi")`.
- The WebApi serves everything under its `UsePathBase("/dialogporten")`
  ([`Program.cs:207`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Program.cs#L207)),
  so the prefix passes through unchanged as for the other components: `/dialogporten/api/v1/...`,
  `/dialogporten/swagger`, `/dialogporten/scalar`, `/dialogporten/health/*`.
- The GraphQL host maps `/graphql` with no path base ([`Program.cs:168`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L168)),
  so `/dialogporten/graphql[/…]` must be forwarded as `/graphql[/…]`, which is what APIM does in production and the
  reason Arbeidsflate's `DIALOGPORTEN_URL` is `…/dialogporten`. Today's `Forwarder.Send` appends the request path to
  the destination unchanged; the change is a `StripPrefix` on `Route` applied in `LocaltestTransformer.TransformRequestAsync`
  (rebuild `proxyRequest.RequestUri` from the destination and the path minus the prefix). The alternative that keeps
  the proxy untouched is a pure addition `#if LOCALTEST app.UsePathBase("/dialogporten"); #endif` before `app.UseCors()`
  in the GraphQL `Program.cs`, a second directive in that host; the proxy change is preferred because the localtest app
  is ours.
- Streaming is already unbuffered (SSE for GraphQL subscriptions works through the existing forwarder); WebSockets
  pass through but are untested (the localtest app review's open item 16).

### 8.2 CORS

Only the GraphQL host has CORS (`GraphQl:Cors:AllowedOrigins`, [`Program.cs:79-108`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.GraphQL/Program.cs#L79-L108)),
for Arbeidsflate's browser-side subscription stream. Set `GraphQl__Cors__AllowedOrigins__0` to the origin the
Arbeidsflate strand chooses (the earlier report proposed `http://af.local.altinn.cloud:8000`); it is a required list,
so one entry must exist. The WebApi needs none: the browser does not call it directly (dialog content is fetched from
service-owner URLs with the dialog token).

### 8.3 Ed25519 dialog-token keys

`Application:Dialogporten:Ed25519KeyPairs:{Primary,Secondary}:{Kid,PrivateComponent,PublicComponent}` are base64url
of the raw 32-byte Ed25519 seed and the raw 32-byte public key (`Key.Import(..., RawPrivateKey)` /
`PublicKey.Import(..., RawPublicKey)`, [`ICompactJwsGenerator.cs:83-93`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Common/ICompactJwsGenerator.cs#L83-L93));
the JWKS publishes `PublicComponent` as `x` with `kid` ([`GetJwksQuery.cs:19-36`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/WellKnown/Jwks/Queries/Get/GetJwksQuery.cs#L19-L36)),
and the GraphQL host builds `EdDsaSecurityKey`s from the same public components. Generate without dialogporten-utils:

```sh
openssl genpkey -algorithm ed25519 -out k.pem
openssl pkey -in k.pem -outform DER | tail -c 32 | basenc --base64url -w0 | tr -d '='          # PrivateComponent (seed)
openssl pkey -in k.pem -pubout -outform DER | tail -c 32 | basenc --base64url -w0 | tr -d '='  # PublicComponent
```

(PKCS#8 DER of an Ed25519 key is 48 bytes with the seed last; SPKI DER is 44 bytes with the key last; a seed
re-wrapped in PKCS#8 reproduced the same public key here.) Test pair generated today for the implementer to commit,
test-only like `certs/`:

| | Kid | PrivateComponent | PublicComponent |
| --- | --- | --- | --- |
| Primary | `localtest-primary` | `YTY1c-1T_IK_gSeyThx4wzHDKJI87LSdHgpZS9CXiCc` | `Xg0SfHY0oNB6M3om3bY6jJ6JFE16gP4JY6mQeL-1FR4` |
| Secondary | `localtest-secondary` | `f99ScXcAgl6XGG9MUIN3nRXnR0kYNlXsdDO-amjKrWc` | `wzuM7xyqsySfrhbWomQh_cfa3HETBjleagfPZQwLva8` |

Where: `localtest/certs/dialogporten-ed25519.env` (six `Application__Dialogporten__Ed25519KeyPairs__…` lines,
`env_file:` on the three services) keeps the key material next to the other test keys, or inline in the compose
file; either is fine for test-only values. The adapter and Correspondence validate dialog tokens against
`http://local.altinn.cloud:8000/dialogporten/api/v1/.well-known/jwks.json`.

## 9. Resources, profile, ordering

- Containers: `dialogporten-webapi`, `dialogporten-graphql`, `dialogporten-service`, `redis`. The running .NET
  components use 95 to 300 MB each (`podman stats` today: storage 183 MB, events 193 MB, access-management 299 MB);
  the three Dialogporten hosts share the same libraries and EF model, so 200 to 350 MB each is the expectation, about
  0.6 to 1 GB plus Redis (not measured). CPU is idle outside requests except the Service's outbox polling.
- Opt-in profile: yes, `profiles: ["dialogporten"]` on the four services (and later on the adapter and Arbeidsflate),
  started with `podman-compose --profile dialogporten up -d`. The localtest app's `Proxy__Upstreams__dialogporten-*`
  defaults can stay; a route to a stopped container is a 502 with the reason, as for any component. Apps never call
  Dialogporten, so the default stack is unaffected.
- Startup order: `postgres` healthy → `redis` → `dialogporten-webapi` (reset, migrate; `healthcheck: curl -f
  http://localhost:8080/health/readiness`, Altinn convention paths `/health`, `/health/readiness`, `/health/startup`,
  `/health/deep`, liveness pinned to `/health/liveness`,
  [`DialogportenHealthCheckExtensions.cs:26`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Library.Utils.AspNet/DialogportenHealthCheckExtensions.cs#L26);
  the aspnet image has no curl, so use `dotnet`-free `wget`-less check through compose `test: ["CMD", "/bin/sh", "-c",
  "exec 3<>/dev/tcp/127.0.0.1/8080 …"]` or publish the port and let the localtest app's smoke wait, decide at
  implementation) → `dialogporten-graphql`, `dialogporten-service` (`condition: service_healthy` on the WebApi).
  `register`, `authorization`, `access-management` and `events` are `service_started` dependencies of the WebApi; the
  first authorization call waits for them anyway (3 retries with jittered backoff).
- The localtest app's `depends_on` does not list the profile's services (a profile member cannot be a hard dependency
  of a default one).

## 10. Upstream since the pin (`0946da8` → `1a7c584`, 5 commits)

`git diff --stat`: HotChocolate 16.6.7 → 16.6.8 in the GraphQL and Infrastructure csproj and `Utils.AspNet`, the
matching `packages.lock.json` changes in every project, `StrawberryShake.Server` in the E2E test project,
`.config/dotnet-tools.json`, and four GitHub Actions bumps plus a manual E2E dispatch workflow. No source, Dockerfile,
migration or configuration change. The plan is unaffected; pinning at `1a7c584` instead costs nothing, and the lock
files must be copied with the csproj files whichever pin is chosen.

## 11. Configuration (compose environment, all three hosts unless noted)

| Key | Value | Why |
| --- | --- | --- |
| `ASPNETCORE_ENVIRONMENT` | `Development` | In-memory bus, migrator, the `LocalDevelopment` section; baked into the image |
| `ASPNETCORE_HTTP_PORTS` | `8080` (image default) | |
| `Infrastructure__DialogDbConnectionString` | `Host=postgres;Port=5432;Database=dialogporten;Username=dialogporten;Password=Password;Include Error Detail=True` | |
| `Infrastructure__Redis__ConnectionString` | `redis:6379` | |
| `Infrastructure__Altinn__BaseUri`, `__EventsBaseUri` | `http://local.altinn.cloud:8000/` | The platform host every component maps to the localtest app (`x-platform-host`) |
| `Infrastructure__Altinn__SubscriptionKey` | `localtest` | Header value; validator does not require it, a null header is avoided |
| `Infrastructure__AltinnCdn__BaseUri` | `https://altinncdn.no/` (upstream default) | Section 4 |
| `Infrastructure__Maskinporten__Environment`/`ClientId`/`Scope`/`EncodedJwk` | `test` / `localtest` / upstream's development scope string / `eyJrdHkiOiJSU0EifQ==` (base64 of `{"kty":"RSA"}`) | All four are `NotEmpty`-validated ([`InfrastructureSettings.cs:224-233`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Infrastructure/InfrastructureSettings.cs#L224-L233)); the fake never reads the JWK |
| `Infrastructure__Warmup__Enabled` | `false` | Section 3 |
| `Application__Dialogporten__BaseUri` | `http://local.altinn.cloud:8000/dialogporten` | Token issuer, event sources, OpenAPI server URL |
| `Application__Dialogporten__Ed25519KeyPairs__…` | section 8.3 | |
| `LocalDevelopment__*` | section 3 | Thirteen keys, set explicitly because the defaults and the development files say `true` |
| `WebApi__Authentication__JwtBearerTokenSchemas__0__Name`/`__WellKnown`, `__1__…` (WebApi); `GraphQl__Authentication__…` (GraphQL) | `Maskinporten` / `http://local.altinn.cloud:8000/localtest/api/maskinporten/.well-known/oauth-authorization-server`; `Altinn` / `http://local.altinn.cloud:8000/authentication/api/v1/openid/.well-known/openid-configuration` | Section 7 |
| `WebApi__OpenApi__EnableTryItOut`, `IdportenAuthorizationUrl`, `IdportenTokenUrl`, `IdportenLogoutUrl`, `MaskinportenTokenUrl`, `IdportenClientId` (WebApi) | `true`, the fake ID-porten's `…/localtest/api/idporten/authorize`, `…/token`, `…/logout`, a placeholder for Maskinporten, `localtest` | Four URLs are `NotEmpty`-validated ([`DialogportenOpenApiOptionsValidator.cs`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.WebApi/Common/Authentication/DialogportenOpenApiOptionsValidator.cs)); only Swagger UI uses them |
| `GraphQl__Cors__AllowedOrigins__0` (GraphQL) | the Arbeidsflate origin | Section 8.2 |
| `Serilog__MinimumLevel__Default` | `Information` | The hosts default to Warning; upstream's compose uses Debug |
| `Localtest__Dialogporten__PlatformEndpoint`, `__OrgNumber`, `__ResetOnStart` | defaults | Section 5.2 |

Not set: `AZURE_APPCONFIG_URI`, `OTEL_EXPORTER_OTLP_ENDPOINT`, `Infrastructure__MassTransit__Host` (in-memory).

## 12. App runner (`run-app.sh`)

After step 4 (the Access Management refresh), when the Dialogporten profile is running:

| Step | Call | What it does |
| --- | --- | --- |
| 5 | `POST localtest/api/…` of the Resource Registry strand, if it needs a push to pick up the new app policy | Their contract |
| 6 | `POST dialogporten/api/v1/localtest/sync` | Runs `SyncSubjectMapCommand` (merges the `updated` feed since the table's last `UpdatedAt`, [`SyncSubjectMapCommand.cs:48-49`](https://github.com/Altinn/dialogporten/blob/0946da81c676b928db98780afa01fe50943af609/src/Digdir.Domain.Dialogporten.Application/Features/V1/ResourceRegistry/Commands/SyncSubjectMap/SyncSubjectMapCommand.cs#L48-L49)) and `SyncPolicyCommand`, then clears the resource-registry and subject-resource caches. Skipped with a note when the WebApi is not reachable (profile off) |

Nothing else: dialogs for app instances come from the adapter strand, not from the runner.

## 13. Verification plan (`components/dialogporten/smoke-test.sh`, against the stack through the proxy)

1. Readiness: `GET /dialogporten/health/readiness` 200; `GET /dialogporten/api/v1/.well-known/jwks.json` lists
   `localtest-primary` and `localtest-secondary`; `GET /dialogporten/api/v1/.well-known/oauth-authorization-server`
   issuer `http://local.altinn.cloud:8000/dialogporten/api/v1`.
2. Service owner: `ORG=$(curl -s "$BASE/Home/GetTestOrgToken?orgNumber=991825827&scopes=digdir:dialogporten.serviceprovider%20digdir:dialogporten.serviceprovider.search")`;
   `POST /dialogporten/api/v1/serviceowner/dialogs` with `serviceResource: urn:altinn:resource:app_ttd_testapp`,
   `party: urn:altinn:person:identifier-no:01839012345`, a title and summary → 201 and a UUIDv7 id (requires the
   Resource Registry to list the resource under `991825827`; until then, this step runs with
   `UseLocalDevelopmentResourceRegister=true` and is marked as such); a token without the scope → 403; a resource of
   another org → 403; `GET …/serviceowner/dialogs/{id}` → the dialog; `GET …/serviceowner/dialogs?party=…` → one hit
   (`.search`); `DELETE …/serviceowner/dialogs/{id}` → 204.
3. Sync: `POST /dialogporten/api/v1/localtest/sync` → 200 and a non-empty `SubjectResource` count for `app_ttd_testapp`
   (read back through the inspection endpoint or `psql`).
4. Service host: after step 2 the events component's inspection API (`events/api/localtest/events?type=…`) shows the
   `dialogporten.dialog.created.v1` event published with the exchanged token, and the search index row exists
   (`psql`), proving the outbox → in-memory bus → consumers path.
5. End user (gated on the code-flow token): with a `pid` token for Sophie Salt (`priv` on herself) `GET
   /dialogporten/api/v1/enduser/dialogs` lists the dialog, `GET …/enduser/dialogs/{id}` returns it with a dialog token
   (`dialogToken` claim `iss` as above), `POST /dialogporten/graphql {"query":"{ dialogs { items { id } } }"}` the same;
   Kari Nordvik (no role on `app_ttd_testapp`) sees nothing; the PDP denial for details is a 403.
6. Reset: restart `dialogporten-webapi`; the dialog is gone and readiness returns.
7. Cold path: with the cookie token from `/Home/GetTestUserToken`, `GET …/enduser/dialogs` is 403 with the
   user-type problem text, documented as the expected state until the OIDC client exists.

## 14. Effort and risk

| Item | Effort | Risk |
| --- | --- | --- |
| Vendor copy, `UPSTREAM.md`, props/targets, Dockerfile, compose profile, init script, keys | 1 day | Low: verified build chain |
| Fakes (Maskinporten, two issuer caches, reset, sync and cache endpoints) and the three hooks | 1.5 days | Low |
| Proxy rewrite for `/dialogporten/graphql` | 0.5 day | Low |
| First boot against the stack (migrations on 16 with the shim, Redis, warmup off, health, token schemes) | 1 day | Medium: untested combinations (the shim, in-memory outbox delivery across hosts) |
| Service-owner and sync smoke, events check | 1 day | Medium: depends on the Resource Registry strand's shape; `UseLocalDevelopmentResourceRegister=true` is the stop-gap |
| End-user smoke | 0.5 day once a `pid` token exists | Depends on the Arbeidsflate strand (or a localtest-app helper) |
| README, review notes | 0.5 day | |
| Total | **5 to 7 days** | |

Main risks: (1) the Resource Registry contract (resource owner org numbers, `updated` feed, `AltinnApp` type) decides
whether any dialog can be created; (2) the end-user API has no caller until the OIDC client work lands; (3) the search
authorization needs the subject-resource mappings to use the same role codes Access Management returns (lower-case
`urn:altinn:rolecode:dagl`, `urn:altinn:accesspackage:…`), which the RR strand must take from the pushed policies the
way Access Management's stand-in does; (4) the 15-minute named caches hide test-data changes unless the cache
endpoint is called (the runner can call it after each push).

## 15. Open questions for the product owner

1. Shim on Postgres 16 now (recommended), or move the shared container to `postgres:18` first (volume path change,
   four other components' migrations to re-run)?
2. Is a `/Home` helper that completes Authentication's authorization-code flow for a seeded test client acceptable
   as the way to get `pid` tokens for Dialogporten's end-user API before Arbeidsflate, or should the end-user API wait
   for Arbeidsflate's own login?
3. Opt-in compose profile `dialogporten` (recommended) or always on (about 1 GB more RAM)?
4. The Altinn CDN: keep the internet dependency (Authentication already has it) or add a static
   `altinn-orgs.json` to the localtest app for both?
5. Should the Events component treat Dialogporten's `…/dialogporten/api/v1/enduser/dialogs/…` sources as generic
   events (its app-source heuristic is the platform host), and does anyone subscribe locally?

## 16. Not verified

- No image was built and no host was started: the in-memory outbox delivery from WebApi writes to Service consumers,
  the reset-then-migrate sequence, the `uuidv7()` shim under EF's migration, warmup/readiness behaviour with
  `Warmup:Enabled=false`, Redis subscriptions, and FusionCache `ClearAsync` propagation over the backplane are all
  read from code only.
- Memory per host is an analogy with the running components, not a measurement.
- The Maskinporten NuGet 10.1.0 was compared with the reference clone's `main` (`15d3c63`), not the 10.1.0 tag.
- The `ServiceResourceAuthorizer` path was read, not exercised; the Resource Registry does not exist locally.
- `UseHttpsRedirection()` in the Service host without an https endpoint: expected to log one warning.
- The `NSwag` build target was verified in the scratch build with probe fakes, not with the real fakes.
