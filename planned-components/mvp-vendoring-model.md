# MVP sketch: one compose file, one folder per component, vendored source changed only by directives

Status: sketch for review (2026-10-16). Refines the vendoring model from [`register/README.md`](register/README.md).
No Aspire anywhere: docker compose is the orchestrator, and vendored hosts that bootstrap through Aspire-style
service defaults get a plain bootstrap under `#else`.

## Shape

```
localtest/
  docker-compose.yml              # ingress + one service per component + one shared postgres
  ingress/                        # nginx (or YARP) config: local.altinn.cloud/<prefix> -> component container
  testdata/                       # built-in test users; mounted read-only into the components that need it
  components/
    authentication/               # FAKE ONLY: login page, user picker, token issuing, openid/jwks, testData.json merge
    storage/
      Dockerfile                  # builds vendor/src/Storage with the two env vars below
      localtest.build.props       # imports vendor/Directory.Build.props, then defines LOCALTEST, relaxes analyzers
      localtest.build.targets     # Compile Remove / PackageReference Remove / ProjectReference ../fakes
      vendor/                     # git subtree of Altinn/altinn-storage; edits only inside #if !LOCALTEST … #else … #endif
      fakes/                      # Storage.Localtest.csproj: in-memory or file-backed replacements, DI override entry point
    authorization/                # vendor/ = split subtree of altinn-auth src/apps/Altinn.Authorization (+ src/libs closure), fakes/
    register/                     # vendor/ = split subtrees (apps/Altinn.Register, pkgs/Altinn.Register.Contracts), fakes/
    profile/                      # FAKE ONLY to start (see assessment), vendor later
    events/                       # FAKE ONLY: contract-shaped stub
    notifications/                # FAKE ONLY: contract-shaped stub
    pdf/                          # existing pdf3 image, no vendoring
```

Rules:

- `vendor/` is a subtree; the only diffs against upstream are `#if !LOCALTEST` (production first) / `#else` /
  `#endif` blocks. Nothing is deleted or moved, so `git subtree pull` keeps merging.
- `fakes/` is a normal project next to `vendor/`. It holds every localtest replacement: in-memory repositories,
  no-op queue clients, a signing-key provider that reads the shared localtest certificate, and one
  `LocaltestOverrides.Apply(IServiceCollection, IConfiguration)` entry point that `Replace()`s registrations.
- Components without a production counterpart worth vendoring (authentication, events, notifications, profile for
  now) are fake-only folders with their own small project and Dockerfile, in the same layout minus `vendor/`.
- Upstream csproj files are never edited. The props and targets files do the overriding (next section).

## Build mechanics (verified in a scratch project today)

MSBuild lets an environment variable redirect the `Directory.Build.props` and `Directory.Build.targets` lookup.
The Dockerfile sets both before `dotnet restore`/`publish` of the vendored project:

```dockerfile
ENV DirectoryBuildPropsPath=/src/localtest.build.props \
    DirectoryBuildTargetsPath=/src/localtest.build.targets
RUN dotnet publish /src/vendor/src/Storage/Altinn.Platform.Storage.csproj -c Release -o /app
```

`localtest.build.props` imports the vendored chain first so nothing upstream is lost, then layers overrides:

```xml
<Project>
  <Import Project="$(MSBuildThisFileDirectory)vendor/Directory.Build.props" Condition="Exists('$(MSBuildThisFileDirectory)vendor/Directory.Build.props')" />
  <PropertyGroup>
    <DefineConstants>$(DefineConstants);LOCALTEST</DefineConstants>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`localtest.build.targets` is evaluated after the project file, which is where item removal has to happen:

```xml
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'Altinn.Platform.Storage'">
    <Compile Remove="$(MSBuildProjectDirectory)/Repository/Pg*.cs" />          <!-- example -->
    <PackageReference Remove="Azure.Identity" />
    <ProjectReference Include="$(MSBuildThisFileDirectory)fakes/Storage.Localtest.csproj" />
  </ItemGroup>
</Project>
```

Checked: `LOCALTEST` is defined while the SDK's `NET10_0_OR_GREATER` and the csproj's own constants stay intact
(passing `-p:DefineConstants` on the command line would have clobbered them); a vendored root
`Directory.Build.props` value still flows through; `Compile Remove`, `PackageReference Remove` and the added
`ProjectReference` all take effect from the targets file. Not yet checked: interplay with central package
management (`Directory.Packages.props` in altinn-auth and altinn-register), which the fakes project must either opt
into or sit outside of, and analyzers pulled in by the vendored chain.

## Where the directives go

One hook per host, placed right after the production service registration so production wiring stays intact
and the fake layer replaces what it must:

```csharp
#if !LOCALTEST
    await ConnectToKeyVaultAndSetApplicationInsights(config);
#else
    Localtest.Storage.LocaltestOverrides.Apply(builder.Services, builder.Configuration);
#endif
```

Beyond that, directives only where code is hard-wired to infrastructure outside DI (a Key Vault connect, a
Wolverine or Npgsql bootstrap, an Aspire-style host builder). Everything reachable through an interface is
replaced in `fakes/` with no directive at all.

## Dependency assessment per component

Legend: **mock** = in-process replacement in `fakes/`; **container** = compose service; **off** = configuration or
environment switch; **directive** = `#if !LOCALTEST` site.

### Storage ([`Program.cs`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs), 484 lines)

| Dependency | Production | MVP | Note |
| --- | --- | --- | --- |
| Postgres (12 repository interfaces, logic partly in ~2 300 lines of PL/pgSQL) | Npgsql + Yuniql migrations | **container** (shared `postgres`, db `storagedb`, init SQL + migrations run by the service) | Mocking means re-implementing the SQL semantics (the Storage strand's main finding). Start real, mock later if ever. |
| Blob storage | Azure Blob via [`IBlobRepository`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Repository/IBlobRepository.cs) | **mock**: file-backed implementation on a volume | No Azurite. Today's localtest `BlobRepository` (148 lines) is the template; retained blob versions (altinn-storage#1125) need a version suffix. |
| File-scan queue | Azure Queue via [`IFileScanQueueClient`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Clients/IFileScanQueueClient.cs) | **mock**: marks the element clean immediately | Replaces the two `#if LOCALTEST` sites localtest carries today. |
| Key Vault | [`ConnectToKeyVaultAndSetApplicationInsights`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs#L138) + [`IPublicSigningKeyProvider`](https://github.com/Altinn/altinn-storage/blob/1c594731249c9efb8ef41fab037780e47f659049/src/Storage/Program.cs#L262) | **directive** around the connect, **mock** provider returning the shared localtest cert | Removes the fragile command-line `SecretUri` workaround. |
| Wolverine / Service Bus | outbox to Dialogporten | **off** (`ASPNETCORE_ENVIRONMENT=Development`, `EnableSending=false`) | In-process queues in Development. |
| App Insights / OTel | Azure Monitor | **off** | Console exporter only. |
| SBL bridge (A2) | parties-with-instances, correspondence | **off** (`DisableA2Endpoints=true`) | Clients become no-ops. |
| Authorization PDP, Register, openid metadata | HTTP | **container** (the other components, via ingress or compose DNS) | Config keys only. |
| Application metadata and texts | in Postgres, pushed by Designer | **new**: the authentication/testdata component or a tiny "app registrar" pushes them with a `studio.designer` token when an app comes up | The one piece with no production analogue locally. |

### Authorization PDP ([`Program.cs`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs), 413 lines, plain `WebApplication` builder, no Aspire)

| Dependency | Production | MVP |
| --- | --- | --- |
| Database | none | – |
| Policy blob store | [`IPolicyRepository`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L198) over Azure Blob | **mock**: reads app policies from a mounted folder or from the running app, resource policies from testdata |
| Roles / delegations (Access Management PIP) | [`IAccessManagementWrapper`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L201) | **mock** over testdata, or HTTP to the Register fake's external roles |
| Profile, Register, Resource Registry, OED | [`IProfile`, `IRegisterService`, `IResourceRegistry`, `IOedRoleAssignmentWrapper`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L191-L214) | **mock** (or HTTP to the register/profile components) |
| Key Vault, App Insights | skipped when `kvSetting` empty | **off** |
| Audit/event queue | [`IEventsQueueClient`](https://github.com/Altinn/altinn-auth/blob/392f9578d925b947331a1e90fb609c031654d3d2/src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs#L221) | **mock** no-op |
| Platform access token | `AccessTokenHandler` + Key Vault certs | **mock** lenient handler |

Fully mockable, no container dependencies beyond the other components. Vendoring closure: the PDP references
`src/libs/Altinn.Authorization.Api.Contracts` by relative path, so the split subtree must include that lib (or the
NuGet, if one is published; not confirmed).

### Register ([`RegisterHost.cs`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs))

| Dependency | Production | MVP |
| --- | --- | --- |
| Host bootstrap | [`AltinnHost.CreateWebApplicationBuilder`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs#L68) from the `Altinn.Authorization.ServiceDefaults` package (Key Vault, App Configuration, OTel, Azure Monitor, service discovery) | **directive**: plain `WebApplication.CreateBuilder` under `#else` unless the package proves harmless without Azure config (not confirmed) |
| Postgres persistence | `Altinn.Register.Persistence` | **mock**: in-memory `IPartyPersistence`, `IPartyExternalRolePersistence`, `IExternalRoleDefinitionPersistence` over testdata (1 200 to 1 500 lines, see the Register plan); persistence project removed from compilation |
| MassTransit, imports, jobs, leases | A2/NPR/CCR import sagas | **off** by not compiling them (targets file) plus throwing stubs for the 8 public A2/CCR interfaces |
| Aspire AppHost | `Altinn.Register.AppHost` | not built |

### Fake-only components

| Component | Why fake-only | Contents |
| --- | --- | --- |
| authentication | production is tied to ID-porten/Maskinporten; the user picker has no production analogue | today's `HomeController`, `TestAuthenticationController`, `OpenIdController`, token issuing, `testData.json` merge, `DebugUsers`, Tenor import; also serves `/Home/Localtest/Version` for the app's `LocaltestValidation` probe |
| profile | production needs Postgres, Register v2 for every lookup and KRR | thin fake over the Register fake's store: `users/{id}`, `users/current`, `users/byuuid`, `POST users`; vendor later if Profile grows endpoints apps need |
| events, notifications | write-only locally; real services need Service Bus and providers | contract-shaped stubs incl. `future/orders` and `orders/{id}/cancel`, optional "sent" page |

### Cross-cutting

- **Ingress.** One reverse proxy on `local.altinn.cloud:8000` with path-prefix rules (`/storage/` → storage,
  `/authorization/` → authorization, `/register/` → register, `/profile/`, `/events/`, `/notifications/`,
  `/authentication/` and `/Home/` → authentication, `/{org}/{app}/` → the app on the developer host). The app keeps
  today's `PlatformSettings` defaults. Reaching the host app from containers needs `host.docker.internal` or the
  studioctl host bridge; the MVP can start with `extra_hosts`.
- **One signing certificate** mounted into authentication (issuer), storage and authorization (verifiers via the
  fake `IPublicSigningKeyProvider`), so platform access tokens and user tokens validate everywhere.
- **One Postgres** container with per-component databases created by an init script; only Storage uses it in the
  MVP.
- **Test data** mounted into authentication and register; everything else reads it through those two.
- **Hot reload** of policies and app metadata is lost compared with today's in-process reads unless the fakes
  poll the app; acceptable for an MVP, worth a setting.

## MVP order

1. Root, compose, ingress, `authentication` fake extracted from today's localtest, `pdf`. Apps log in and get
   tokens; platform calls still 404.
2. `register` vendored with in-memory persistence. Validates the props/targets mechanism on a real upstream
   repo (CPM, analyzers, ServiceDefaults bootstrap).
3. `authorization` vendored with fakes over the register store. First real PDP decisions locally.
4. `storage` vendored with the shared Postgres, file blob mock, scan mock, metadata push. Apps complete a full
   instance lifecycle on production Storage SQL.
5. `profile`, `events`, `notifications` stubs; retire the old localtest Storage and authorization copies.

## Open questions

1. Repository home: a new top-level folder in the monorepo (`src/Runtime/localtest-next/`?) or a separate repo
   first; studioctl's manifest model should eventually generate what the compose file expresses.
2. Subtree granularity for `altinn-auth` (one split per app plus `src/libs`), and whether `Api.Contracts` and
   `ServiceDefaults` are available as NuGet so fewer trees are needed.
3. Central package management: does a vendored `Directory.Packages.props` make the fakes project's package
   references fail without versions, and does `PackageReference Remove` interact cleanly with CPM pins.
4. Whether `Altinn.Authorization.ServiceDefaults` starts without Azure configuration, which decides the size of
   the Register host directive.
5. Storage app-metadata push: who owns it (authentication component, a registrar sidecar, or studioctl).
6. Image publishing: the compose file builds locally; CI should publish per-component images so developers pull
   instead of compiling five services.
