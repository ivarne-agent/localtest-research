# Production Register in localtest: vendoring analysis

Research for replacing localtest's 2019-era Register emulation with the production `Altinn/altinn-register`
source, compiled into localtest with `#if !LOCALTEST` blocks and an in-memory persistence fed by the test-data model.

- Upstream: `Altinn/altinn-register` at `f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0` (= upstream `main` on 2026-10-16,
  911 commits), reference clone `.reference/altinn-register`.
- Monorepo: `Altinn/altinn-studio` at `447e53eb9797d4fffe63d0c61d05a1f692b4ec48`.
- Paths below are relative to the upstream repo root unless prefixed with `altinn-studio/`. [`Host/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/) means
  [`src/apps/Altinn.Register/src/Altinn.Register/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/), [`Core/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/) means [`src/apps/Altinn.Register/src/Altinn.Register.Core/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/).

## How this was verified

Everything marked **verified** was compiled or run in a scratch copy; no repository was modified.

1. **Compile probe.** A copy of LocalTest ([`src/Runtime/localtest/src`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/) + [`src/Runtime/common`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/common)) with the upstream repo
   copied to `src/Runtime/localtest/vendor/altinn-register/`, `ProjectReference` to [`Altinn.Register.Core.csproj`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Altinn.Register.Core.csproj), the
   needed host files linked with `<Compile Include>`, and the existing [`Controllers/Register/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register) deleted. It builds with
   0 warnings after three changes: 6 added packages, one package bump, one `#if` site (details below).
2. **DI probe.** `AddRegisterCoreServices()` + `AddUnitOfWorkManager()` with `ValidateOnBuild = true`: exactly 11 descriptor
   errors, all from A2/bridge/CCR handlers in Core. Removing those 11 makes the container validate.
3. **End-to-end probe.** A web host with the production controllers, a stub `IPartyPersistence` (one organization),
   lenient auth policies and `Altinn:register:ApiSource:Default=DB`. Responses:
   - `GET /register/api/v1/organizations/310000000` returns 200 with the V1 organization.
   - `GET /register/api/v1/parties/500000` returns 200 with `"partyTypeName":2`; unknown id returns the production
     `REG-00007` problem (404).
   - `GET /register/api/v2/internal/parties/{uuid}?fields=party,org` returns 200 with the V2 contract.
4. **API-versioning probe.** Asp.Versioning added to a host with unversioned `[ApiController]` controllers (like localtest's).
5. **`git subtree split`** run on a full clone of upstream.

## Executive summary

- **Vendoring is practical and small.** Localtest needs 3 upstream projects (`Altinn.Register.Contracts`,
  `Altinn.Register.Contracts.MassTransit`, `Altinn.Authorization.ServiceDefaults.MassTransit.Abstractions`) plus
  `Altinn.Register.Core` as project references, and about 40 files from the `Altinn.Register` host project compiled
  into `LocalTest.csproj`. No Postgres, Npgsql, full MassTransit, Aspire, Azure Monitor or Service Bus code is
  compiled. The only MassTransit dependency is `MassTransit.Abstractions` 8.5.10 (Apache-2.0).
- **The host composition is not reused.** `RegisterHost.Create` builds its own `WebApplication` through
  `AltinnHost.CreateWebApplicationBuilder` (ServiceDefaults NuGet: Key Vault, App Configuration, OpenTelemetry, Azure
  Monitor, health checks, service discovery). Localtest is one host, so `Program.cs` and `RegisterHost.cs` are simply
  not compiled, and a localtest-owned `AddLocaltestRegister()` registers the ~15 services the controllers need.
- **Very few `#if` sites in vendored code are required.**
  - Verified minimum: **1 site**, [`Host/Controllers/PersonsController.cs:96`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PersonsController.cs#L96), because localtest's
    `JWTCookieAuthentication` 3.0.1 has no `AltinnCoreClaimTypes.PartyUUID`. Bumping to 4.0.4 breaks localtest's own
    `OpenIdController`/`AuthenticationService` (verified).
  - Recommended second site: [`Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs:42`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L42)
    and [`:100-118`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L100-L118) (A2 and CCR handler registrations). This needs `LOCALTEST` defined for the Core project (see build
    section). The alternative is 8 throwing stub registrations in localtest code, which needs no `#if`.
- **The A2 to DB switch is configuration only.** The V1 endpoints route through a mediator that picks an "A2" (SBL
  bridge) or "DB" handler per endpoint. The default is A2 ([`Core/Mediator/ApiSourceSwitchProvider.cs:36`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Mediator/ApiSourceSwitchProvider.cs#L36)), and
  `Altinn:register:ApiSource:Default=DB` selects the persistence-backed handlers.
- **The persistence seam is clean.**
  - Implement `IPartyPersistence` (14 methods; 8 must work), `IPartyExternalRolePersistence` (6; 5 must work),
    `IExternalRoleDefinitionPersistence` (4, a static table) and `IRateLimitProvider` (2).
  - Reuse Core's `UnitOfWorkManager` unchanged: register the services with `AddUnitOfWorkService<T>(factory)`. No
    participant is needed.
  - Rough size is 1,200–1,500 lines plus tests, against about 3,850 lines of Postgres implementation for the same
    interfaces. The largest part is `LookupParties` with field masking and the main-unit/sub-unit transforms.
- **Three blocking data and behaviour issues:**
  - **Invalid identifiers.** `PersonIdentifier`/`OrganizationIdentifier` enforce mod-11 checksums. 3 of localtest's
    built-in persons and 5 of its orgs fail, including `01017512345` and `897069650`/`897069651`.
  - **JSON enum format.** Localtest's global `JsonStringEnumConverter` turns V1 `partyTypeName` from `2` into
    `"Organisation"` (verified). V2 enums are unaffected.
  - **API versioning.** `AddApiVersioning()` makes every unversioned localtest `[ApiController]` return 400 unless
    `AssumeDefaultVersionWhenUnspecified = true` (verified both ways).
- **Subtree recommendation: vendor two split subtrees, not the whole repo.**
  - The two subtrees are [`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register) (234 commits) and [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts)
    (45 commits). Each split takes about 4 s and is deterministic.
  - The vendored root [`Directory.Build.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.props), [`Directory.Build.targets`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.targets), [`Directory.Packages.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Packages.props) and
    [`stylecop.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/stylecop.json) become monorepo-owned.
  - This layout survives Register's planned move into `Altinn/altinn-auth`
    ([altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056)), which keeps both paths, and lets the
    monorepo's root props define `LOCALTEST` for Core.
  - The whole-repo subtree stops working after the move, because `altinn-auth` has 3,426 commits, about 46 MB and 5
    other apps.

## 1. Project graph

TFM `net10.0`, `LangVersion 14.0` (Core uses C# 14 `extension(...)` blocks), `Nullable`, `ImplicitUsings`,
`GenerateDocumentationFile=true`, StyleCop + Roslynator analyzers ([`Directory.Build.props:1-24`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.props#L1-L22),
[`Directory.Build.targets:81-87`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.targets#L81-L87)). Central package management with transitive pinning ([`Directory.Packages.props:2-4`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Packages.props#L2-L4))
and a `GlobalPackageReference` to `DotNet.ReproducibleBuilds`. [`global.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/global.json) pins SDK 10.0.203 (rollForward
latestFeature). Non-test projects get `InternalsVisibleTo $(AssemblyName).Tests` ([`Directory.Build.targets:75`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.targets#L75)).
Packable projects (Contracts) get a `_CheckVersion` target and `Microsoft.Build.Artifacts`.

| Project (path under [`src/apps/Altinn.Register/src/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/) unless noted) | Project refs | Key packages | Localtest needs? |
| --- | --- | --- | --- |
| [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) | none | Altinn.Authorization.ModelUtils 3.5.0, Altinn.Swashbuckle.Abstractions, Altinn.Urn 4.3.0, System.ComponentModel.Annotations; global `using System.Text.Json.Serialization` | **yes** (ref via Core) |
| [`Altinn.Register.Contracts.MassTransit`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Contracts.MassTransit) (5 files) | Contracts, MT.Abstractions | — | **yes** (Core refs it) |
| [`ServiceDefaults.MassTransit.Abstractions`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/ServiceDefaults.MassTransit.Abstractions) (7 files) | none | MassTransit.Abstractions 8.5.10 | **yes** (transitive) |
| [`Altinn.Register.Core`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/) (159 files) | Contracts, Contracts.MassTransit | ModelUtils, ProblemDetails.Abstractions/Validation 5.4.0, ServiceDefaults.Telemetry 5.5.0, Swashbuckle.Abstractions, KeyDerivation, Nerdbank.Streams, Polly | **yes** (ProjectReference, compiles unchanged) |
| [`Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/) (host, 131 files, Web SDK) | Core, Persistence, Integrations.*, ServiceDefaults.{Jobs,MassTransit,StorageQueues} | ServiceDefaults 5.5.0 (+HttpClient, MaskinPorten), Common.AccessToken 5.1.11, Common.PEP 4.2.3, JWTCookieAuthentication 4.0.4, Asp.Versioning 8.1.1, Swashbuckle 9.0.6, ApplicationInsights, HybridCache | **~40 files only**, compiled into LocalTest |
| [`Altinn.Register.Persistence`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/) | Core, ServiceDefaults.Jobs, .Leases | ServiceDefaults.Npgsql(+Yuniql, TestSeed), Polly | no (replaced) |
| [`Altinn.Register.Integrations.{Npr,Sire,Ccr.Xml,Ccr.FileImport}`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/) | Core | HttpClient, SSH.NET | no |
| [`ServiceDefaults.{MassTransit,Jobs,Leases,StorageQueues,AzureIdentity}`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/) | — | MassTransit(+Azure SB, RabbitMQ, Quartz), Azure.Identity, Azure.Storage.Queues | no |
| [`Aspire.Npgsql`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Aspire.Npgsql), [`Altinn.Register.AppHost`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.AppHost) | — | Aspire 13.4.6 | no |

Note: `Altinn.Authorization.ServiceDefaults*` (incl. `AltinnHost`) is a **NuGet package**, not in this repo; the
in-repo `ServiceDefaults.*` folders are separate projects.

### Minimal project set (verified compiling)

`Altinn.Register.Core` as `ProjectReference`, which pulls in Contracts, Contracts.MassTransit and
MassTransit.Abstractions. Host files compiled into [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj):

```
Controllers/{AccessManagement,AltinnApps,Correspondence,DialogPorten,Notifications,Organizations,Parties,Persons}Controller.cs
Controllers/V2/{Metadata,Party}Controller.cs
Authorization/**  Mapping/**  ModelBinding/**  Models/**  Operations/**   (Operations = request validators)
Utils/ContractsMapper.cs  Utils/Base64UrlEncoder.cs
Extensions/HttpClientExtension.cs  Extensions/SpanExtensions.cs  Extensions/ModelBinderProviderListExtensions.cs
Clients/AuthorizationClient.cs  Clients/Interfaces/IAuthorizationClient.cs        (or replace, see §6)
Configuration/GeneralSettings.cs  Configuration/PlatformSettings.cs
```

The compile closure needs these packages added to [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj), all verified necessary:
- `Altinn.Authorization.ModelUtils.Swashbuckle` 3.5.0: `FlagsEnum<>` in [`V2/PartyController`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/PartyController.cs), and Swagger schema
  filters in [`Models/*`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Models).
- `Altinn.Authorization.ProblemDetails` and `.Validation` 5.4.0.
- `Altinn.Common.AccessToken` 5.1.11: `IAccessTokenRequirement` in [`Authorization/InternalScopeOrAccessTokenRequirement.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Authorization/InternalScopeOrAccessTokenRequirement.cs).
- `Asp.Versioning.Mvc.ApiExplorer` 8.1.1.
- `Swashbuckle.AspNetCore.Annotations` 9.0.6.

It also needs one bump: `Microsoft.IdentityModel.Protocols.OpenIdConnect` 8.3.1 → 8.22.0. Without it, NU1605 reports a
downgrade forced by `Altinn.Common.AccessToken` 5.1.11.

Resolved graph check: no `Npgsql`, `MassTransit` (full), `OpenTelemetry.*Azure*`, `Aspire` in `project.assets.json`.
`Azure.Identity`/`Azure.Security.KeyVault.Secrets` arrive via `Altinn.Common.AccessToken`.

### Build interactions with the monorepo

- No `Directory.Build.props` or `Directory.Packages.props` exists at the monorepo root or under
  [`src/Runtime/localtest`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest) (verified by `find`). Vendored props therefore only affect projects inside the vendored tree,
  as long as the vendored root is **not an ancestor** of [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj). For example
  `src/Runtime/localtest/vendor/...` or `src/Runtime/register/...` is fine, but placing it at [`src/Runtime/localtest/`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest)
  itself would not be.
- Files linked with `<Compile Include>` into LocalTest are compiled under **LocalTest's** settings: no CPM, no
  StyleCop, `LOCALTEST` defined. Vendored `.csproj` projects (Core, Contracts) build under the **vendored**
  props: CPM, StyleCop and Roslynator, and `LOCALTEST` **not defined**.
- **MSBuild stops at the first `Directory.Build.props`.** The chain is
  [`src/apps/Altinn.Register/src/Directory.Build.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Directory.Build.props) → (GetPathOfFileAbove) → root [`Directory.Build.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.props), which
  does not import further.
  - A props file higher in the localtest tree is never imported.
  - To get `LOCALTEST` into Core, the define must go in the vendored root props. That is only acceptable if the root
    props is monorepo-owned, which the split-subtree layout in §7 provides.
- `InternalsVisibleTo`: no conflict. The host needs no Core internals (Core's IVT list is test-only, and
  `Altinn.Register.Persistence` itself uses only Core's public API), so an in-memory persistence in LocalTest can be
  written against the public API.
- Core is `net10.0`/C# 14. LocalTest is `net10.0` with SDK 10.0.302 ([`altinn-studio/src/Runtime/localtest/global.json`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/global.json)), which is fine.
- Docker: [`altinn-studio/src/Runtime/localtest/Dockerfile:4-6`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/Dockerfile#L4-L6) copies only [`LocalTest.csproj`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/LocalTest.csproj) before `dotnet restore`.
  Restore will need the vendored `.csproj`s plus root [`Directory.*.props/targets`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0), [`stylecop.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/stylecop.json), and
  [`Version.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/Version.props) for Contracts. [`Dockerfile.dockerignore`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/Dockerfile.dockerignore) must whitelist the vendored path.

## 2. Host composition ([`Host/Program.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs), [`Host/RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs))

Cleanest cut overall: **(a) do not compile [`Program.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs) and [`RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs)**, and add a localtest-owned
`src/Runtime/localtest/src/Register/RegisterServiceCollectionExtensions.cs` that registers the subset below. Lines are
in [`Host/RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs) unless noted.

| Lines | Production registration | Localtest action | Cut |
| --- | --- | --- | --- |
| 68 | `AltinnHost.CreateWebApplicationBuilder` (ServiceDefaults NuGet): Key Vault, DB secrets, Azure App Configuration, OpenTelemetry + OTLP + Azure Monitor, health checks, service discovery + standard resilience on every HttpClient, `AddAltinnScopesAuthorizationHandlers`, `IMetricsProvider`, `AltinnServiceDescriptor` | none; localtest has its own host, `/health`, and no telemetry export | a |
| 73-74 | `AddMemoryCache`, `AddHybridCache` | memory cache already in localtest; HybridCache only used by A2 `PersonLookupService` | a |
| 76-87 | `AddControllers().AddControllersAsServices()`, model binders `PartyComponentOptionModelBinder`, `PartyFieldIncludesModelBinder` | add the two binders via `AddMvcOptions`; **do not** call `AddControllersAsServices` for all of localtest, register `V2.PartyController` as transient (needed by the 6 facade controllers that take it in their constructor) | c |
| 89-94 | `ConditionalControllerConvention` (`[DevTestCondition]`, `[LocalDevCondition]`) | not needed (Debug controller excluded) | a |
| 96-111 | `AddApiVersioning().AddMvc().AddApiExplorer()` | **required** (routes use `v{version:apiVersion}`); set `AssumeDefaultVersionWhenUnspecified = true`, otherwise all unversioned localtest `[ApiController]`s return 400 (verified) | c |
| 113, 116 | `GeneralSettings`, `PlatformSettings` | bind; add `"JwtCookieName": "AltinnStudioRuntime"` to localtest's `GeneralSettings` section; `PlatformSettings:ApiAuthorizationEndpoint` already exists in localtest [`appsettings.json:45-47`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/appsettings.json#L45-L47) | c |
| 114-115 | `KeyVaultSettings`, `AccessTokenSettings` | skip | a |
| 117-118 | `PersonLookupSettings`, `AddRateLimitPolicy(PersonLookupService.FailedAttemptsRateLimitPolicyName)` | call `AddRateLimitPolicy("person-lookup-failed-attempts")` + config `Altinn:Register:RateLimit:Policy:person-lookup-failed-attempts` (see [`Host/appsettings.Development.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/appsettings.Development.json)) | c |
| 119 | `AddOptions<V2.PartyController.Settings>()` | keep (stream page sizes) | c |
| 121-130 | `A2PartyImportSettings` | skip | a |
| 132 | `AccessTokenHandler` (+135 `PublicSigningKeyProvider`, Key Vault) | replace by localtest handler for `IAccessTokenRequirement` (§6) | c |
| 133-134 | `ScopeAccessHandler`, `IHttpContextAccessor` | already in localtest ([`Startup.cs:136`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L136), [`:111`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L111)) | — |
| 137 | `IPersonLookup` → `PersonLookupService` (A2) | skip | a |
| 139-140 | OpenTelemetry tracer/meter for `RegisterTelemetry` | skip (ActivitySource without listener is harmless) | a |
| 142-163 | JwtCookie authentication | already in localtest ([`Startup.cs:156-171`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L156-L171)) | — |
| 165-176 | policies `PlatformAccess`, `AuthorizationLevel2`, `InternalOrPlatformAccess`, `Debug` | re-register `PlatformAccess` + `InternalOrPlatformAccess` in localtest (`AuthorizationLevel2` exists at [`Startup.cs:197-199`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L197-L199)); skip `Debug` | c |
| 178 | `AddRegisterCoreServices()` | keep; but A2/CCR handler registrations fail `ValidateOnBuild` (Development) → `#if` in Core or stubs (see table below) | b or c |
| 180-182 | `AddCcrFileProcessor/XmlProcessor/FileImportServices` | skip | a |
| 184-187 | HttpClients `IOrganizationClient`, `IPersonClient`, `IV1PartyService` (A2 bridge), `IAuthorizationClient` | only `IAuthorizationClient` (or an in-proc localtest implementation) | a / c |
| 189-224 | A2 import, SystemUser import, AccessManagement workaround, SBL profile bridge, NPR, SIRE clients (Maskinporten) | skip | a |
| 231-236 | `TryAddPlatformTokenProvider`, `IAccessTokenGenerator`, `ISigningCredentialsResolver` (outbound platform tokens) | skip | a |
| 238 | `AddUnitOfWorkManager()` | keep | c |
| 240-248 | `AddRegisterPersistence()` (Npgsql) + `AddLeaseManager()`, else throwing `IImportJobTracker` | replace with in-memory `AddUnitOfWorkService<IPartyPersistence>(…)`, `AddUnitOfWorkService<IPartyExternalRolePersistence>(…)`, scoped `IExternalRoleDefinitionPersistence`, singleton `IRateLimitProvider` | c |
| 250-356 | MassTransit (`AddAltinnMassTransit`, consumers, `SagaManager`), DB-size job condition, recurring jobs (A2 party/profile, SystemUser, NPR, SIRE, saga cleanup, CCR), CCR federation, Storage queues | skip | a |
| 353-356 | `AddAltinnMassTransitMigrations` (init-only) | skip | a |
| 358-422 | Swagger (`AddSwaggerGen`, example providers, URN/ModelUtils swagger support) | skip (packages still needed at compile time for attributes in `Models/*`) | a |
| 424-431 | init-only service-provider options | skip | a |
| [`Program.cs:6-19`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs#L6-L19) | `Altinn:RunInitOnly` | skip | a |
| [`Program.cs:21`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs#L21) | `AddDefaultAltinnMiddleware` (exception handler `/register/api/v1/error`, forwarded headers) | skip; ProblemDetails responses still produced by `ToActionResult()` (verified 404 body) | a |
| [`Program.cs:23-40, 45`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs#L23-L40) | Swagger UI, `MapDefaultAltinnEndpoints` (health/alive) | skip | a |

Additional localtest-side registrations:
- `TryAddSingleton(TimeProvider.System)`, needed by `V2.PartyController`. A bare generic host does not register
  `TimeProvider` (verified). Localtest's `InstanceLockRepository` already resolves one today, presumably via YARP, but
  that is not confirmed.
- Configuration `Altinn:register:ApiSource:Default = DB`.

### Required `#if !LOCALTEST` sites in vendored code

| File:line | Production does | Localtest branch does | Necessity |
| --- | --- | --- | --- |
| [`Host/Controllers/PersonsController.cs:96`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PersonsController.cs#L96) | reads claim `AltinnCoreClaimTypes.PartyUUID` (JWTCookieAuthentication ≥ 4) | `c.Type.Equals("urn:altinn:party:uuid")` | **required** unless LocalTest moves to JWTCookieAuthentication 4.x (breaks [`Controllers/Authentication/OpenIdController.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authentication/OpenIdController.cs) and [`Services/Authentication/Implementation/AuthenticationService.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/Authentication/Implementation/AuthenticationService.cs), verified) |
| [`Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs:42`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L42) | `TryAddScoped<CcrService>()` (needs `ICcrXmlProcessor`, `ICcrUpdateFederator`, `ILocationLookupProvider`) | omit | required for `ValidateOnBuild` unless stubs; needs `LOCALTEST` in Core |
| same file [`:100`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L100), [`:102`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L102), [`:104`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L104), [`:106`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L106), [`:108`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L108), [`:110`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L110), [`:112`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L112), [`:114`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L114), [`:116`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L116), [`:118`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Microsoft.Extensions.DependencyInjection/RegisterCoreServiceCollectionExtensions.cs#L118) | registers the 10 `*FromA2*`/`*FromBridge*` handlers (need `IOrganizationClient`, `IV1PartyService`, `IPersonLookup`, `ISblProfileBridgeClient`, `ICommandSender`) | omit (DB handlers stay) | as above |

The alternative to the Core sites is option (c): register throwing stubs for the 8 public interfaces
`IV1PartyService`, `IOrganizationClient`, `IPersonLookup`, `ISblProfileBridgeClient`, `ICommandSender`,
`ICcrXmlProcessor`, `ICcrUpdateFederator` and `ILocationLookupProvider`. All 8 are `public` (verified). This needs no
`#if` and no `LOCALTEST` in Core, but it leaves dead registrations. With `ApiSource=DB` the A2 handlers are never
resolved ([`Core/Mediator/RegisterMediator.cs:92-106`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Mediator/RegisterMediator.cs#L92-L106)). The self-identified-user handler choice
([`RegisterMediator.cs:74-86`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Mediator/RegisterMediator.cs#L74-L86)) is only reached from the excluded `SelfIdentifiedUsersController`.

Optional sites (not needed to compile or run):
- [`Host/Clients/AuthorizationClient.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Clients/AuthorizationClient.cs): instead of `#if`, do not compile it and register a localtest `IAuthorizationClient`
  that calls localtest's authorization service in process (§6).

### Files to exclude from compilation

The recommended approach is an **allowlist** in `LocalTest.csproj` (the list in §1), so excluding means "not listed":

| Path | Reason |
| --- | --- |
| [`Host/Program.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Program.cs), [`Host/RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs) | separate host; localtest composes services itself |
| [`Host/ApiDescriptions/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/ApiDescriptions) | Swagger schema filters (only used by `AddSwaggerGen`) |
| [`Host/Ccr/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Ccr), [`Host/Cleanup/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Cleanup), [`Host/PartyImport/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/PartyImport), [`Host/JobNames.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/JobNames.cs), [`Host/LeaseNames.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/LeaseNames.cs), [`Host/Extensions/RegisterJobEnabledBuilderExtensions.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Extensions/RegisterJobEnabledBuilderExtensions.cs) | CCR federation, saga cleanup, import jobs/sagas/consumers (MassTransit, jobs, leases) |
| [`Host/Clients/{BridgeOrgContactPointLookup,OrganizationClient,PartiesClient,PersonClient,SblProfileBridgeClient}.cs`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Clients), [`Host/Clients/Interfaces/IPersonClient.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Clients/Interfaces/IPersonClient.cs) | SBL bridge (A2) clients |
| [`Host/TempWorkarounds/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/TempWorkarounds), [`Host/Services/PersonLookupService.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Services/PersonLookupService.cs), [`Host/Core/PersonLookupSettings.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Core/PersonLookupSettings.cs) | A2 person lookup, AM workaround |
| [`Host/Configuration/{A2PartyImportSettings,CcrFileImportSettings}.cs`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Configuration) | import settings |
| [`Host/Conventions/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Conventions) | conditional controller convention (Debug only) |
| [`Host/Exceptions/PlatformHttpException.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Exceptions/PlatformHttpException.cs), [`Host/Utils/HttpReciliencyExtensions.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Utils/HttpReciliencyExtensions.cs) | unused / needs `Microsoft.Extensions.Http.Resilience` |
| [`Host/Controllers/CcrController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/CcrController.cs) | CCR push import (`enhets-registeret/api/v1/update.svc`) |
| [`Host/Controllers/DebugController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DebugController.cs) | NPR/SIRE debug, A2 import trigger |
| [`Host/Controllers/ErrorController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/ErrorController.cs) | target of `AddDefaultAltinnMiddleware` |
| [`Host/Controllers/OrgContactPointController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/OrgContactPointController.cs) | returns 410 Gone |
| [`Host/Controllers/SupportDashboardController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/SupportDashboardController.cs) | support dashboard (optional; trivially compiles if wanted) |
| [`Host/Controllers/V2/SelfIdentifiedUsersController.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/SelfIdentifiedUsersController.cs) | SI user creation via bridge + AM workaround |
| projects [`Altinn.Register.Persistence`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/), [`Integrations.*`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/), [`ServiceDefaults.{MassTransit,Jobs,Leases,StorageQueues,AzureIdentity}`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/), [`Aspire.Npgsql`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Aspire.Npgsql), [`AppHost`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.AppHost), all [`test/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/test) | not referenced |
| [`altinn-studio/src/Runtime/localtest/src/Controllers/Register/*.cs`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Register) (localtest's own) | **must** be removed: same routes as production → ambiguous matches |

Core needs no exclusions. Its CCR, PartyImport and ImportJobs code compiles against `MassTransit.Abstractions` only.

## 3. Controllers

All routes are prefixed `register/api/v{version}/`.

| Controller (file:line of route) | Route | Version | Auth | Depends on | Localtest |
| --- | --- | --- | --- | --- | --- |
| [`PartiesController.cs:29`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PartiesController.cs#L29) | `parties`: `GET {partyId:int}` (:56, extra `[Authorize]`), `GET byuuid/{uuid}` (:108, `[Authorize]`), `POST lookup` (:150), `POST nameslookup` (:190), `POST partylist?fetchSubUnits` (:242), `POST partylistbyuuid` (:288), `GET identifiers` (:316) | 1 | `InternalOrPlatformAccess` | mediator `IRequestSender<…>` → DB handlers → `IPartyPersistence.GetPartyById`, `LookupParties`; `IAuthorizationClient.ValidateSelectedParty` when caller is not an org (:409-435) | **keep** (app: `{id}`, `lookup`, `partylist?fetchSubUnits=true`) |
| [`OrganizationsController.cs:18`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/OrganizationsController.cs#L18) | `organizations/{orgNr}` (:29) | 1 | `InternalOrPlatformAccess` | mediator → `GetOrganizationByIdentifier` | **keep** (app) |
| [`PersonsController.cs:25`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PersonsController.cs#L25) | `persons` GET (headers `X-Ai-NationalIdentityNumber`, `X-Ai-LastName`) (:39) | 1 | `PlatformAccess` **and** `AuthorizationLevel2` | mediator → `GetPersonByIdentifier`, `IRateLimiter` | **keep** (app) |
| [`AltinnAppsController.cs:17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/AltinnAppsController.cs#L17) | `apps/parties/query` (:37) | 1 | `InternalOrPlatformAccess` | `V2.PartyController.Query` | **keep** (app, `fields=id,user.id`) |
| [`AccessManagementController.cs:17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/AccessManagementController.cs#L17) | `access-management/parties/{uuid}` (:28), `…/query` (:53) | 1 | `InternalOrPlatformAccess` | V2 `GetPartyByUuid`, `Query` | **keep** (app-lib [`AltinnPartyClient.cs:193`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Infrastructure/Clients/Register/AltinnPartyClient.cs#L193) calls `access-management/parties/query`) |
| [`NotificationsController.cs:17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/NotificationsController.cs#L17) | `notifications/parties/query` (:37) | 1 | `InternalOrPlatformAccess` | V2 `Query` | keep |
| [`CorrespondenceController.cs:21`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/CorrespondenceController.cs#L21) | `correspondence/parties/{uuid}/roles/correspondence-roles` (:45), `main-units` (:106), `{uuid}` (:121), `query` (:146) | 1 | `InternalOrPlatformAccess` | `IPartyExternalRolePersistence.GetExternalRoleAssignmentsFromParty`, V2 | keep |
| [`DialogPortenController.cs:17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DialogPortenController.cs#L17) | `dialogporten/parties/query` (:37), `set-username` (:52) | 1 | `InternalOrPlatformAccess` | V2 `Query`, `SetUsername` | keep |
| [`V2/PartyController.cs:32`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/PartyController.cs#L32) | `internal/parties`: `stream` (:93), `external-roles/assignments/events/stream` (:146), `{uuid}/external-roles` (:187), `{uuid}/customers/ccr/{revisor,regnskapsforer,forretningsforer}` (:215/:234/:253, versions 1 and 2), `{uuid}/holders/ccr/daglig-leder` (:318, v1+v2), `{uuid}` (:374), `query` (:413), `set-username` (:579), `main-units` (:696) | 2 | `InternalOrPlatformAccess` | `IUnitOfWorkManager` → `IPartyPersistence` (`GetPartyById`, `LookupParties`, `GetPartyStream`, `GetMaxPartyVersionId`, `GetExternalRoleAssignmentStream`, `GetMaxExternalRoleAssignmentVersionId`, `UpsertParty`), `IPartyExternalRolePersistence` (`From`/`To`); `TimeProvider`; `IConfiguration` (`Altinn:register:Party:CreatePartyId`, set-username returns `FeatureDisabled` when false) | **keep** |
| [`V2/MetadataController.cs:17`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/MetadataController.cs#L17) | `internal/metadata/external-roles` (:37) | 2 | `InternalOrPlatformAccess` | `IExternalRoleDefinitionPersistence.GetAllRoleDefinitions` | keep |
| [`V2/SelfIdentifiedUsersController.cs:29`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/SelfIdentifiedUsersController.cs#L29) | `internal/parties/self-identified` POST | 2 | `PlatformAccess` | SBL bridge, `ICommandSender`, `TempWorkarounds.AccessManagementClient` | exclude |
| [`SupportDashboardController.cs:16`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/SupportDashboardController.cs#L16) | `support-dashboard/parties/{uuid}` | 1 | `InternalOrPlatformAccess` | V2 | exclude (optional) |
| [`CcrController.cs:28`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/CcrController.cs#L28) | `enhets-registeret/api/v1/update.svc` | 1 | Basic auth vs CCR client settings | CCR | exclude |
| [`DebugController.cs:34`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/DebugController.cs#L34) | `debug/*` | 0 | `Debug` + `[DevTestCondition]` | NPR/SIRE clients | exclude |
| [`ErrorController.cs:14`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/ErrorController.cs#L14) | `error` | 1 | anonymous | — | exclude |
| [`OrgContactPointController.cs:13`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/OrgContactPointController.cs#L13) | `organizations/contactpoint/lookup` | 1 | none | returns 410 | exclude |

Facade controllers call the inner `V2.PartyController` directly (`inner.Query(...)`), which is why production uses
`AddControllersAsServices()`. Registering `V2.PartyController` as a transient service is sufficient (verified by the
DI probe).

`V2.PartyController` subunit/main-unit endpoints rely on `PartyListTransforms.ReplaceWithMainUnits` (`main-units`,
:705-708) and V1 `partylist?fetchSubUnits=true` relies on `IncludeSubUnits`.

## 4. Persistence seam

All interfaces are in Core, all `public`. Reached via `IUnitOfWork.GetPartyPersistence()` /
`GetPartyExternalRolePersistence()` ([`Core/UnitOfWork/UnitOfWorkExtensions.cs:17-26`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/UnitOfWork/UnitOfWorkExtensions.cs#L17-L26)). The Postgres implementation lives in
[`src/apps/Altinn.Register/src/Altinn.Register.Persistence/`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/) ([`PostgreSqlPartyPersistence*.cs`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/), 3,851 lines across the four
partial files; registrations in [`Microsoft.Extensions.Hosting/RegisterPersistenceExtensions.cs:86-108`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Microsoft.Extensions.Hosting/RegisterPersistenceExtensions.cs#L86-L108)).

**Unit of work:** reuse Core unchanged.
- Production registers `AddUnitOfWorkParticipant<NpgsqlUnitOfWorkParticipant.Factory>()` and
  `AddUnitOfWorkService<IPartyPersistence>(s => ...)` ([`RegisterPersistenceExtensions.cs:89-92`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Microsoft.Extensions.Hosting/RegisterPersistenceExtensions.cs#L89-L92)).
- `UnitOfWorkManager.Impl` ([`Core/UnitOfWork/UnitOfWorkManager.cs:53-101`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/UnitOfWork/UnitOfWorkManager.cs#L53-L101)) accepts zero participants, so localtest
  only registers the service factories. No `IUnitOfWork`, `IUnitOfWorkHandle` or participant implementation is
  needed. `CommitAsync` with no participants is a no-op.

### `IPartyPersistence` ([`Core/Parties/IPartyPersistence.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/IPartyPersistence.cs), 14 abstract members + 1 default)

| Method (line) | Used by kept code | Localtest |
| --- | --- | --- |
| `GetPartyById(Guid, PartyFieldIncludes)` (:24) | V2 `{uuid}`, V1 byuuid | implement |
| `GetPartyById(uint, PartyFieldIncludes)` (:41) | V1 `{partyId}` | implement |
| `GetOrganizationByIdentifier` (:111) | V1 organizations | implement |
| `GetPersonByIdentifier` (:125) | V1 persons | implement |
| `LookupParties(uuids, ids, externalUrns, orgIds, personIds, userIds, usernames, selfIdentifiedEmails, include, transforms)` (:163) | V1 lookup/nameslookup/partylist*/identifiers, V2 query/main-units/customers/holders | implement (the bulk): OR of identifier lists, dedupe, ordering by `version_id` then sub-units after parent ([`PostgreSqlPartyPersistence.PartyQuery.cs:836-837`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/PostgreSqlPartyPersistence.PartyQuery.cs#L836-L837)), `ReplaceWithMainUnits` and `IncludeSubUnits` via external role assignments whose role is in the main-unit set `hovedenhet`, `ikke-naeringsdrivende-hovedenhet`, `administrativ-enhet-offentlig-sektor` ([`Migration/v0.65-main-units/00-table.sql`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.65-main-units/00-table.sql), query at [`PartyQuery.cs:1278-1328`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/PostgreSqlPartyPersistence.PartyQuery.cs#L1278-L1328)). `V1PartyMapper.ToV1PartyList` asserts that ordering ([`Core/Operations/Mapping/V1PartyMapper.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Operations/Mapping/V1PartyMapper.cs), `ToV1PartyList`) |
| `GetPartyStream(fromExclusive, limit, include, filterByPartyType)` (:187) | V2 stream | implement (order by version id) |
| `GetExternalRoleAssignmentStream(fromExclusive, limit)` (:203) | V2 role stream | implement (synthesized "added" events) |
| `GetMaxPartyVersionId` (:213), `GetMaxExternalRoleAssignmentVersionId` (:220) | V2 streams | implement |
| `UpsertParty` (:52) | V2/DialogPorten `set-username` (only when `CreatePartyId` flag true) | NotSupported (or in-memory overlay) |
| `UpsertParties(IAsyncEnumerable)` (:62) (+ default overload :72) | imports | NotSupported |
| `GetOrCreateSelfIdentifiedEmailUser` (:83), `…EduUser` (:94) | excluded SI controller | NotSupported |
| `GetPartyByUserId` (:139) | no caller outside persistence | NotSupported or trivial |

`PartyFieldIncludes` ([`Core/Parties/PartyFieldIncludes.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/PartyFieldIncludes.cs), `ulong` flags):
- Party: `PartyUuid`, `PartyId`, `PartyType`, `PartyExternalUrn`, `PartyDisplayName`, `PartyPersonIdentifier`,
  `PartyOrganizationIdentifier`, `PartyCreatedAt`, `PartyModifiedAt`, `PartyIsDeleted`, `PartyDeletedAt`,
  `PartyVersionId`, `PartyOwnerUuid`.
- Person: `Source`, `FirstName`, `MiddleName`, `LastName`, `ShortName`, `Address`, `MailingAddress`, `DateOfBirth`,
  `DateOfDeath`.
- Organization: `Source`, `UnitStatus`, `UnitType`, `TelephoneNumber`, `MobileNumber`, `FaxNumber`, `EmailAddress`,
  `InternetAddress`, `MailingAddress`, `BusinessAddress`.
- Other: `SelfIdentifiedUser*`, `SystemUserType`, `UserId`, `Username`.
- Composites: `Identifiers`, `Party`, `Person`, `Organization`, `User`.

Records use `FieldValue<T>` (Altinn.Authorization.ModelUtils: Unset / Null / value). The in-memory implementation
must mask non-included fields to `FieldValue.Unset`. No masking helper exists in Core (not confirmed for test utils).
`PartyListTransforms` ([`Core/Parties/PartyListTransforms.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/PartyListTransforms.cs)): `None`, `ReplaceWithMainUnits`, `IncludeSubUnits`. There
is no `PartyQueryFilters` in Core; [`PartyQueryFilters.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/PartyQueryFilters.cs) is Postgres-internal.

Paging and stream cursors are `ulong` version ids. The controller wraps them in `Opaque<ulong>` tokens
([`Host/Models/Opaque.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Models/Opaque.cs)). The page size comes from `V2.PartyController.Settings` (1,000).

### `IPartyExternalRolePersistence` ([`Core/Parties/IPartyExternalRolePersistence.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/IPartyExternalRolePersistence.cs), 6 members)

- `GetExternalRoleAssignmentsFromParty(uuid)`, `(uuid, ExternalRoleReference)` and `(uuid, IReadOnlyList<ExternalRoleReference>)`
  (:23/:39/:56).
- `GetExternalRoleAssignmentsToParty(uuid)` and `(uuid, role)` (:72/:88).
- Implement those 5 over the synthesized assignments, honouring `PartyExternalRoleAssignmentFieldIncludes`
  (`RoleSource`, `RoleIdentifier`, `RoleFromParty`, `RoleToParty`, `RoleDefinitionName`, `RoleDefinitionDescription`).
- `UpsertExternalRolesFromPartyBySource` (:103) is NotSupported.

### `IExternalRoleDefinitionPersistence` ([`Core/ExternalRoles/IExternalRoleDefinitionPersistence.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/ExternalRoles/IExternalRoleDefinitionPersistence.cs), 4 members)

The members are `TryGetRoleDefinition(source, identifier)`, `TryGetRoleDefinitionByRoleCode(code)`,
`GetAllRoleDefinitions()` and `GetRoleDefinitionLookup()`.

This is a static table. The production data is spread over SQL migrations:
- [`Migration/v0.04-external-roles/05-data-external-roles.sql`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.04-external-roles/05-data-external-roles.sql)
- [`v0.10-role-code/02-data-external-roles.sql`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.10-role-code/02-data-external-roles.sql): 59 CCR roles with codes such as `DAGL`
- [`v0.15-ffor-role-definition`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.15-ffor-role-definition)
- [`v0.18-role-identifiers/01-role-identifiers.sql`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.18-role-identifiers/01-role-identifiers.sql): renames to kebab identifiers such as `daglig-leder`
- [`v0.42`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.42-guardianship-roles)/[`v0.48`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.48-guardianship-updates) guardianship: about 42 `cra` roles, generated from [`data/guardianships.yaml`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/data/guardianships.yaml)
- [`v0.65-main-units`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.65-main-units): `MainUnit` flag

Options:
- Generate a JSON/C# snapshot when vendoring, by replaying the migrations into a throwaway Postgres or by calling
  `GET /register/api/v2/internal/metadata/external-roles` against at22/tt02.
- Ship it as a localtest-owned embedded resource.

### Other services the kept code reaches

| Interface | Methods | Localtest |
| --- | --- | --- |
| `IRateLimitProvider` ([`Core/RateLimiting/IRateLimitProvider.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/RateLimiting/IRateLimitProvider.cs)) | `GetStatus`, `Record` | in-memory (about 60 lines) or always not-blocked; production policy config needed (`RateLimiter` reads `IOptionsMonitor<RateLimitPolicySettings>`) |
| `IUnitOfWorkManager` | — | Core's own |
| `IAuthorizationClient` (host) | `ValidateSelectedParty` | keep the HTTP client to localtest's `/authorization/api/v1/parties/{id}/validate` (exists, [`altinn-studio/src/Runtime/localtest/src/Controllers/Authorization/PartiesController.cs:57`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Authorization/PartiesController.cs#L57)) or an in-process localtest implementation |
| `IImportJobStatePersistence`, `ISagaStatePersistence`, `ISagaStateCleanup`, `IImportJobTracker`, `ILeaseProvider`, `ILocationLookupProvider`, `IPartyPersistenceCleanupService` | — | not reached by kept code (only by excluded jobs, sagas and CCR) |

**Size estimate**, not confirmed by implementation:

| Component | Lines |
| --- | --- |
| `InMemoryPartyPersistence` | 500–700 |
| External-role persistence | about 100 |
| Role-definition table | about 100 entries of data plus 60 lines of code |
| Test-data → record mapper | 250 |
| Rate-limit provider | 60 |
| DI composition and auth handler | 150 |

Persistence semantics worth a contract test reused from upstream tests: [`test/Altinn.Register.Persistence.Tests`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/test/Altinn.Register.Persistence.Tests)
(not inspected in depth).

## 5. Data model mapping

`testData.json` contract (`https://altinncdn.no/schemas/json/test-users/test-users.schema.v1.json`, fetched):
- `persons[]` requires `partyId`, `userId`, `ssn` and `partyRoles`. `ssn: ""` means self-identified.
  `partyRoles: {"<partyId>": [{type, value}]}`.
- `orgs[]` requires `partyId` and `orgNumber`; `parentPartyId` is optional.
- `systems[]` contains `systemUsers[] {id (uuid), orgNumber, actions[]}`.
- `additionalProperties: false` everywhere, so new fields need a schema version bump.

Localtest model: [`altinn-studio/src/Runtime/localtest/src/Services/TestData/AppTestDataModel.cs`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/AppTestDataModel.cs) (`AppTestPerson` :281-403, `AppTestOrg` :194-279,
`AppTestSystem` :405-423). It converts into the old `TestDataModel` ([`TestDataModel.cs:12-51`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/TestDataModel.cs#L12-L51)) and is cached for at most
30 s ([`TestDataService.cs:78-82`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/TestDataService.cs#L78-L82)), so the in-memory persistence must rebuild deterministically from each snapshot.

### Party (all types) — [`Core/Parties/Records/PartyRecord.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/Records/PartyRecord.cs), contract [`src/pkgs/.../Party.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/src/Altinn.Register.Contracts/Party.cs)

| Production field | Source in localtest | Synthesis |
| --- | --- | --- |
| `PartyUuid` | `partyUuid` (optional) | when absent: deterministic. The built-in data uses `00000000-0000-0000-0000-000000{partyId:D6}` ([`testdata/Register/Party/500001.json`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Register/Party/500001.json)); every localtest service must use the **same** function |
| `PartyId` (`uint`) | `partyId` (`int`) | reject negative values |
| `PartyType` | person with ssn → Person; `ssn == ""` → SelfIdentifiedUser; org → Organization; system user → SystemUser (optional) | — |
| `ExternalUrn` | — | `urn:altinn:person:identifier-no:{ssn}` / `urn:altinn:organization:identifier-no:{orgNo}` (`PartyExternalRefUrn.PersonId/OrganizationId.Create`); SI: none (not confirmed what prod uses) |
| `DisplayName` | names / `name` | person: `"First Middle Last"` (production A2 import [`Host/PartyImport/A2/A2PartyImportService.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/PartyImport/A2/A2PartyImportService.cs), `MapPersonName`); org: `name` |
| `PersonIdentifier`, `OrganizationIdentifier` | `ssn`, `orgNumber` | **mod-11 validated**, see open question 1 |
| `CreatedAt`, `ModifiedAt` | — | fixed timestamp (for example the Unix epoch or the test-data load time); keep stable across reloads |
| `IsDeleted`, `DeletedAt` | — (old V1 `isDeleted` only in built-in Party JSON) | `false`, `Null` |
| `VersionId` | — | deterministic, for example by ordinal over (party type, partyId); streams depend on monotonicity |
| `OwnerUuid` | — | `Null`; system user → owning org's uuid |
| `UserIds`, `Usernames` (`PartyHistoricalAggregate`) | person `userId`, `userName` | `CreateCurrent(userId)`; orgs: empty |

### Person — [`PersonRecord.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/Records/PersonRecord.cs)

| Production | Localtest | Note |
| --- | --- | --- |
| `Source` | — | `PersonSource.NationalPopulationRegister` |
| `FirstName`, `MiddleName`, `LastName` | same | — |
| `ShortName` | — | A2 convention is the A2 `party.Name` ([`A2PartyImportService.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/PartyImport/A2/A2PartyImportService.cs), `ShortName = shortName ?? displayName`). **V1 `Name` uses `ShortName` if set** (`V1PartyMapper.ToV1Person`). Choose between `ShortName = DisplayName`, which keeps today's localtest V1 name, and `"LAST FIRST MIDDLE"` (A2 style, not confirmed) |
| `Address` (`StreetAddressRecord`: municipalNumber, municipalName, streetName, houseNumber, houseLetter, postalCode, city) | `address*` fields | 1:1 |
| `MailingAddress` (address, postalCode, city) | `mailingAddress`, `mailingPostalCode`, `mailingPostalCity` | 1:1 |
| `DateOfBirth` | — | derive from ssn exactly like production `CalculateDateOfBirth` ([`A2PartyImportService.cs:471-515`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/PartyImport/A2/A2PartyImportService.cs#L471-L515): handles +80 test month, +40 D-number) |
| `DateOfDeath` | — | `Null` |

### Organization — [`OrganizationRecord.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/Records/OrganizationRecord.cs)

| Production | Localtest | Note |
| --- | --- | --- |
| `Source` | — | `OrganizationSource.CentralCoordinatingRegister` |
| `UnitStatus` | `unitStatus` | default `"N"` when absent (not confirmed what prod defaults) |
| `UnitType` | `unitType` | the built-in data has inconsistent values ([`AppTestDataModel.cs:131`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Services/TestData/AppTestDataModel.cs#L131) comment) |
| `TelephoneNumber`, `MobileNumber`, `FaxNumber`, `EmailAddress`, `InternetAddress` | `telephoneNumber`, `mobileNumber`, `faxNumber`, `eMailAddress`, `internetAddress` | 1:1 |
| `MailingAddress`, `BusinessAddress` | `mailing*`, `business*` | 1:1 |
| `ParentOrganizationUuid` | `parentPartyId` | resolve parent's uuid; **also** synthesize role assignment below |

### External role assignments — [`PartyExternalRoleAssignmentRecord.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Parties/Records/PartyExternalRoleAssignmentRecord.cs) / contract [`ExternalRoleAssignment.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/src/Altinn.Register.Contracts/ExternalRoleAssignment.cs)

| Production | Localtest source | Synthesis |
| --- | --- | --- |
| sub-unit → main-unit link | `orgs[].parentPartyId` | `{source: ccr, identifier: "hovedenhet", from: child, to: parent}`. Use `ikke-naeringsdrivende-hovedenhet` for `AAFY` children (from the CCR role semantics in [`v0.18`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Persistence/Migration/v0.18-role-identifiers); not confirmed). This is what drives `IncludeSubUnits`/`ReplaceWithMainUnits` |
| CCR role holder (for example daglig leder) | `persons[].partyRoles[orgPartyId][].value` when the code is a CCR role code (`DAGL`, `LEDE`, `MEDL`, `REGN`, `REVI`, `KOMP`…, matched against the role definitions' `Code`) | `{source: ccr, identifier: lookup(code) e.g. "daglig-leder", from: org uuid, to: person uuid}`. The direction is from the organization to the holder, as in [`V2/PartyController.cs:318-356`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/V2/PartyController.cs#L318-L356) (`holders/ccr/daglig-leder`) |
| Name / Description | role definition table | — |
| Event stream `ExternalRoleAssignmentEvent` (`VersionId`, `Type`, `RoleSource`, `RoleIdentifier`, `ToParty`, `FromParty`) | — | one "added" event per synthesized assignment |

Role `type` in test data is `"altinn"`/`"Altinn"` for both Altinn 2 roles (`A0237`, `UTINN`, `PRIV`…) and CCR
codes (`DAGL`, `REGNA`, `LEDE`). Only codes that match a CCR definition become external role assignments. The rest stay
localtest authorization data.

### Localtest fields with no production Register home

| Field | Where it belongs |
| --- | --- |
| `persons[].customClaims` | localtest authentication |
| `persons[].partyRoles` (non-CCR codes) | localtest authorization (Altinn 2 roles) |
| `persons[].email`, `telephoneNumber`, `mobileNumber`, `language` | Profile (production Register persons have no contact data) |
| `systems[].name`, `systemUsers[].actions` | Authentication and authorization. A system user can optionally become a `SystemUserRecord` party (uuid = `id`, owner = org, `SystemUserType`) |
| built-in [`testdata/Register/Party/*.json`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/testdata/Register/Party) `onlyHierarchyElementWithNoAccess`, `childParties` | computed at query time in production |

### Production fields with no localtest source

`createdAt`, `modifiedAt`, `versionId`, `isDeleted`, `deletedAt`, `dateOfBirth` (derivable), `dateOfDeath`, `shortName`,
person and organization `source`, `ownerUuid`, self-identified `email`/`extRef`/`selfIdentifiedUserType`, external-role
source/identifier (derivable from codes), and guardianship (`cra`) roles. Consider a `test-users.schema.v2.json` if
apps need to express these.

## 6. Auth

Production policies ([`RegisterHost.cs:165-176`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs#L165-L176)):
- `InternalOrPlatformAccess`: `InternalScopeOrAccessTokenRequirement("altinn:register/partylookup.admin")`
  ([`Host/Authorization/InternalScopeOrAccessTokenRequirement.cs:12-28`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Authorization/InternalScopeOrAccessTokenRequirement.cs#L12-L28)). It implements **both**
  `IAccessTokenRequirement` (handled by `AccessTokenHandler`: a valid `PlatformAccessToken` header signed by a platform
  or app certificate from Key Vault) and `IScopeAccessRequirement` (handled by PEP's `ScopeAccessHandler`: the scope
  claim). Either handler succeeding satisfies it.
- `PlatformAccess`: `AccessTokenRequirement`.
- `AuthorizationLevel2`: `urn:altinn:authlevel` ∈ {2,3,4}.
- V1 `parties/{id}` and `byuuid` additionally require an authenticated user. For non-org callers they check
  `urn:altinn:userid` and call Authorization `parties/{id}/validate` ([`PartiesController.cs:409-435`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/Controllers/PartiesController.cs#L409-L435)).

What localtest already has:
- JwtCookie auth (cookie `AltinnStudioRuntime` + bearer) with tokens from its own OpenID endpoint.
- PEP `ScopeAccessHandler` registered ([`Startup.cs:136`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L136)).
- No access-token handler, and no `PlatformAccess` policy. Localtest's own [`Controllers/Storage/DataController.cs:1099`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Controllers/Storage/DataController.cs#L1099)
  references the policy, so that endpoint has no matching policy today.
- App-lib sends both a bearer token and a `PlatformAccessToken` header to Register
  ([`altinn-studio/src/App/backend/src/Altinn.App.Core/Internal/Registers/RegisterClient.cs:107-118`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/src/Altinn.App.Core/Internal/Registers/RegisterClient.cs#L107-L118)).

Proposal: no `#if` in vendored code, because the policies are defined in the uncompiled [`RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs).
1. In localtest's composition, register `InternalOrPlatformAccess` with the **production requirement type**
   (`new InternalScopeOrAccessTokenRequirement("altinn:register/partylookup.admin")`) and `PlatformAccess` with
   `new AccessTokenRequirement()`.
2. Add a localtest `AuthorizationHandler<IAccessTokenRequirement>` that succeeds when a `PlatformAccessToken` header is
   present, and optionally validates it against localtest's signing certificate, which is not confirmed to match what
   apps sign with locally. Do not use `AccessTokenHandler` with `DisableAccessTokenVerification`. That handler still
   fails a present-but-unverifiable token unless key lookup throws (decompiled `AccessTokenHandler.HandleRequirementAsync`).
3. The scope path works unchanged for test tokens carrying `altinn:register/partylookup.admin`.
4. Bind Register's `GeneralSettings.JwtCookieName` (`AltinnStudioRuntime`) so `AuthorizationClient` can forward the
   token. Better, replace `AuthorizationClient` with an in-process `IAuthorizationClient` that calls localtest's
   authorization `PartiesService`, which avoids a self-HTTP call to `PlatformSettings:ApiAuthorizationEndpoint`.
5. `PersonsController` needs `urn:altinn:party:uuid` in the user token (the `#if` site above). Check that localtest's
   token generator emits it (not confirmed).

## 7. Subtree practicality

Facts:
- **Today the monorepo uses non-squashed subtrees.** `localtest` was added with history (commit [`e7f38bd6a7`](https://github.com/Altinn/altinn-studio/commit/e7f38bd6a7059ba39046a362186045988fc58545),
  [`git-subtree-split: d9c4e61…`](https://github.com/Altinn/app-localtest/commit/d9c4e618176389141b1c8daa9765be707050f959)). [`Makefile:5-9`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile#L5-L9) lists subtrees as `name|prefix|url|branch`. `pull-subtree`
  ([`Makefile:90-99`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile#L90-L99)) runs `git subtree pull --prefix=$(2) $(3) $(4)` against a **remote branch**. Sync PRs must be
  merge-committed, not squashed ([`Makefile:139`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile#L139)).
- **Upstream size:** 911 commits, `.git` 4.7 MB, about 1,500 files and 12 MB working tree (host src 5.0 MB, tests 5.3 MB).
  A whole-repo subtree is feasible today.
- **`git subtree split` was tested on a full clone:**

  | Prefix | Commits | Time | Deterministic? |
  | --- | --- | --- | --- |
  | [`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register) | 234 (history starts 2025-07-03, "new party model #499") | about 4.2 s | yes, re-split gives the identical SHA |
  | [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) | 45 | — | — |

  The apps split tree contains only [`Altinn.Register.sln`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/Altinn.Register.sln), [`Directory.Build.targets`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/Directory.Build.targets), [`conf.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/conf.json), [`src`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src) and [`test`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/test).
  It **lacks** the root [`Directory.Build.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Build.props), [`Directory.Packages.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/Directory.Packages.props), [`stylecop.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/stylecop.json) and [`global.json`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/global.json) that
  its projects import via `GetPathOfFileAbove`.
- **Planned move.** Register moves into `Altinn/altinn-auth`
  ([altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056), open).
  - The move uses `git merge --allow-unrelated-histories --no-ff`, preserving the original SHAs.
  - It **keeps [`src/apps/Altinn.Register/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register) and [`src/pkgs/Altinn.Register.Contracts/**`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts) in place**.
  - The `altinn-auth` placeholder [`src/apps/Altinn.Register/{Altinn.Register.slnx,conf.json,docker-bake.hcl,infra,src}`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Register)
    exists today.
  - `altinn-auth` is 3,426 commits, about 46 MB, and has 5 more apps plus [`src/libs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs).
  - The issue also plans to merge CPM and move `ServiceDefaults.*` into [`src/libs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs) or [`Altinn.Authorization.Host`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs/Altinn.Authorization.Host).
    `Contracts.MassTransit` depends on `ServiceDefaults.MassTransit.Abstractions`, so the vendored closure may gain a
    [`src/libs`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2/src/libs) path.

Recommendation:
- Use **two split-based subtrees** under one vendored root, with the upstream relative layout preserved:
  - `<root>/src/apps/Altinn.Register` ← split of [`src/apps/Altinn.Register`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register)
  - `<root>/src/pkgs/Altinn.Register.Contracts` ← split of [`src/pkgs/Altinn.Register.Contracts`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts)
  - `<root>/{Directory.Build.props, Directory.Build.targets, Directory.Packages.props, stylecop.json}`: monorepo-owned
    copies. This is the place to define `LOCALTEST` for Core and to relax analyzers if wanted. Update them by hand when
    upstream changes package versions.
- `<root>` is outside [`src/Runtime/localtest/src`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/), for example `src/Runtime/localtest/vendor/altinn-register` or
  `src/Runtime/register`.
- The relative `ProjectReference` `..\..\..\..\pkgs\Altinn.Register.Contracts\…` in Core ([`Altinn.Register.Core.csproj:21`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register.Core/Altinn.Register.Core.csproj#L21))
  then resolves unchanged.
- **After the move**, splitting the same prefixes in `altinn-auth` should reproduce the same split commits for the
  pre-move history, because the original commits are kept. Later pulls should then continue as normal merges. This is
  expected but **not confirmed** until the move happens; the plan's `premerge` restructuring adds commits under the
  prefix, which is fine.
- [`Makefile`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/Makefile) needs a new macro. The existing `pull-subtree` cannot pull a subdirectory. The new macro should:
  1. `git fetch <url> <branch>` into a temporary ref
  2. `git subtree split --prefix=<upstream-prefix> <ref> -b tmp-split`
  3. `git subtree merge --prefix=<dest> tmp-split`, or `pull` from `.`
  4. delete the temporary refs

  Splitting `altinn-auth` walks all 3,426 commits. Its run time is not measured; it should be tens of seconds.
- A whole-repo subtree of `altinn-register` would work now, but it cannot continue after the move without a one-time
  re-add.

## Open questions

1. **Invalid identifiers in built-in test data.** Production identifier types reject them.
   - Persons: `01017512345`, `01039012345`, `01899699001`.
   - Orgs: `897069631`, `897069650`, `897069651`, `897069653`, `900000001`.
   - These numbers are referenced across the monorepo, for example [`src/App/frontend/test/e2e/pageobjects/party-mocks.ts`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/frontend/test/e2e/pageobjects/party-mocks.ts),
     [`src/App/backend/test/.../Register/*`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/App/backend/test), and frontend unit mocks.
   - The real app `testData.json` checked ([`src/test/apps/signering-brukerstyrt`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/test/apps/signering-brukerstyrt)) uses valid Tenor numbers.
   - Options: replace the built-in users (breaking), skip invalid entries with a warning, or `#if` the checksum in
     Contracts (changes a published contract type).
2. **V1 JSON enum format.** Production sends `partyTypeName: 2`. Localtest's global `JsonStringEnumConverter`
   ([`Startup.cs:75-79`](https://github.com/Altinn/altinn-studio/blob/447e53eb9797d4fffe63d0c61d05a1f692b4ec48/src/Runtime/localtest/src/Startup.cs#L75-L79)) makes it `"Organisation"`. Do we scope production JSON options to Register controllers (for
   example through a controller convention or output formatter) to match the cloud?
3. **ShortName / V1 `name` for persons.** Production derives it from A2 `party.Name`. Should localtest emit A2 style or
   keep `"First Middle Last"`?
4. **One party-UUID and role source for all localtest services.** Authorization, Storage, Profile and AccessManagement
   `AuthorizedParties` keep reading `TestDataService` through `IParties`/`IPersons`/`IOrganizations`. Should they move
   to the new persistence now, or only share a synthesis helper?
5. **Strictness of `PlatformAccessToken` checks** in localtest (presence only, or signature validation).
6. **Core `#if` versus stubs** for the 11 A2/CCR registrations. This decides whether the monorepo-owned vendored root
   props define `LOCALTEST`.
7. **Role-definition snapshot source:** replay the SQL migrations, or fetch from at22/tt02. How is it refreshed on
   upstream sync?
8. **Contracts version.** Source at this commit is 1.7.0 ([`version.txt`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/version.txt), [`Version.props`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/pkgs/Altinn.Register.Contracts/Version.props)), while NuGet has 1.8.0. It is
   not confirmed which source built 1.8.0. AM uses 1.8.0 according to [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056).
9. **MassTransit licensing.** If upstream moves to MassTransit v9, the licence of `MassTransit.Abstractions` may change.
   This is not confirmed.
10. **Version of `test-users.schema`.** Does the extended data (roles by CCR identifier, unit status, date of birth,
    system-user parties) warrant `test-users.schema.v2.json`?

## Unlinked references

- `src/Runtime/localtest/src/Register/RegisterServiceCollectionExtensions.cs`, `src/Runtime/localtest/vendor/...`,
  `src/Runtime/register/...`, `<root>/...` and `test-users.schema.v2.json` are proposed paths that do not exist yet.
- The monorepo-root `Directory.Build.props`/`Directory.Packages.props` in §1 are stated not to exist.
- The host file list in the §1 code block is relative to `Host/`; Markdown links do not render inside code blocks.
- `.reference/altinn-register` is a local clone and `project.assets.json` is a build output.
- `Directory.Build.props:1-24` links to lines 1-22, the whole file at the pinned commit.
