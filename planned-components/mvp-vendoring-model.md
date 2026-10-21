# MVP sketch: one compose file, one folder per component, vendored source changed only by directives

## Status (2026-10-21)

The model is what every component in [`../localtest/components/`](../localtest/components/) follows, with the
changes listed in "The model as applied (2026-10-21)" at the end: no compose file (studioctl's `localtest-v2` runtime
runs the stack), `git archive` copies instead of subtrees, fakes compiled into the vendored host, and no fake-only
platform components (Authentication, Profile, Events and Notifications are all vendored production code). The body
below is the 2026-10-17 sketch; "Where the model was bent" lists the exceptions made since.

Status: sketch reviewed 2026-10-17; decisions recorded at the end. Refines the vendoring model from [`register/README.md`](register/README.md).
No Aspire anywhere: docker compose is the orchestrator, and vendored hosts that bootstrap through Aspire-style
service defaults get a plain bootstrap under `#else`.

## Shape

```
localtest/
  docker-compose.yml              # the localtest app + one service per component + one shared postgres
  localtest/                      # the localtest app (C#, Native AOT; nginx before 2026-10-19): its proxy routes local.altinn.cloud/<prefix> -> component container
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
  *2026-10-21: `vendor/` is a `git archive` copy with `vendor/UPSTREAM.md`, not a subtree; additive `#if LOCALTEST` blocks are allowed too (see "The model as applied").*
- `fakes/` is a normal project next to `vendor/`. It holds every localtest replacement: in-memory repositories,
  no-op queue clients, a signing-key provider that reads the shared localtest certificate, and one
  `LocaltestOverrides.Apply(IServiceCollection, IConfiguration)` entry point that `Replace()`s registrations.
- Components without a production counterpart worth vendoring (authentication, events, notifications, profile for
  now) are fake-only folders with their own small project and Dockerfile, in the same layout minus `vendor/`.
  They are written fresh; the current localtest source is not copied, only consulted for the behaviours apps rely on.
  *2026-10-21: superseded: Authentication, Events, Notifications and Profile are vendored production code; no platform component is fake-only.*
- Upstream csproj files are never edited. The props and targets files do the overriding (next section).
- A vendored component's Dockerfile mirrors the upstream Dockerfile (same pinned base images, build steps and runtime
  user); every difference is a line marked `localtest:`, and a sync carries upstream Dockerfile changes over
  (rule added 2026-10-19; every vendored component follows it; for Access Management the reference is the release image's bake files, see `localtest/components/storage/vendor/UPSTREAM.md`).

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
`ProjectReference` all take effect from the targets file. Also checked (2026-10-17): central package management in
the vendored tree does not reach a `fakes/` project that sits outside `vendor/`, because NuGet locates
`Directory.Packages.props` by walking up from the project directory; a fakes project with ordinary versioned
package references builds unchanged, and a conditional `ManagePackageVersionsCentrally=false` in the props file
works if it ever has to live inside. Not yet checked: analyzers pulled in by the vendored chain.

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
| Application metadata and texts | in Postgres, pushed by Designer | **deferred** to the storage step | No discovery of running apps in the MVP; test users come from a static `testData.json` in the repo. How app metadata gets into Storage (static seed, manual post, or push by the core component) is decided when the storage component is implemented. |

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
| Host bootstrap | [`AltinnHost.CreateWebApplicationBuilder`](https://github.com/Altinn/altinn-register/blob/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0/src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs#L68) from the `Altinn.Authorization.ServiceDefaults` package | **off**: verified in the package source ([`AltinnHost.cs`](https://github.com/Altinn/altinn-authorization-utils/blob/98291827769327482cb72536049e060775116fb3/src/pkgs/Altinn.Authorization.ServiceDefaults/src/ServiceDefaults/AltinnHost.cs) is a thin wrapper over `WebApplication.CreateBuilder`; Key Vault loads only with `kvSetting:SecretUri`, App Configuration only with `Altinn:AppConfiguration:Endpoint`, Azure Monitor only with an Application Insights connection string; `Altinn:LocalDev=true` in Development marks the service LOCAL). No directive. The package is on NuGet (6.0.0). |
| Postgres persistence | `Altinn.Register.Persistence` | **mock**: in-memory `IPartyPersistence`, `IPartyExternalRolePersistence`, `IExternalRoleDefinitionPersistence` over testdata (1 200 to 1 500 lines, see the Register plan); persistence project removed from compilation |
| MassTransit, imports, jobs, leases | A2/NPR/CCR import sagas | **off** by not compiling them (targets file) plus throwing stubs for the 8 public A2/CCR interfaces |
| Aspire AppHost | `Altinn.Register.AppHost` | not built |

### Fake-only components

| Component | Why fake-only | Contents |
| --- | --- | --- |
| authentication | production is tied to ID-porten/Maskinporten; the user picker has no production analogue | written fresh (today's localtest is reference only, not a code base): login page and user picker, token issuing, openid/jwks, `testData.json` loading, debug-user listing; also serves `/Home/Localtest/Version` for the app's `LocaltestValidation` probe |
| profile | production needs Postgres, Register v2 for every lookup and KRR | thin fake over the Register fake's store: `users/{id}`, `users/current`, `users/byuuid`, `POST users`; vendor later if Profile grows endpoints apps need |
| events, notifications | write-only locally; real services need Service Bus and providers | contract-shaped stubs incl. `future/orders` and `orders/{id}/cancel`, optional "sent" page |

### Cross-cutting

- **Ingress.** (Since 2026-10-19 the YARP proxy of the localtest app in `localtest/localtest/`, same rules.) Plain nginx on `local.altinn.cloud:8000` with path-prefix rules (`/storage/` → storage,
  `/authorization/` → authorization, `/register/` → register, `/profile/`, `/events/`, `/notifications/`,
  `/authentication/` and `/Home/` → authentication (since 2026-10-20 `/` and `/Home/` are the localtest app's own), `/{org}/{app}/` → the app on the developer host via
  `host.docker.internal`). The app keeps today's `PlatformSettings` defaults. Today's cookie-domain rewrite and
  frontend version override are dropped for the MVP (decision 2026-10-17).
- **One signing certificate** mounted into authentication (issuer), storage and authorization (verifiers via the
  fake `IPublicSigningKeyProvider`), so platform access tokens and user tokens validate everywhere.
- **One Postgres** container with per-component databases created by an init script; only Storage uses it in the
  MVP.
- **Test data** is the Register seed. The static `testData.json` is mounted into the register component only; its
  in-memory persistence is built from it (persons, organisations, party users, external roles). The authentication
  fake is a Register client, as production Authentication is: the user picker lists parties through the Register
  API, login resolves the chosen person to party id, party UUID and user id through Register, and only
  authentication-specific extras (custom claims, authentication level, system users) come from a small file the
  authentication fake owns. Profile and the PDP fakes read Register the same way. This keeps one source of truth
  and exercises the vendored Register API on every login. The picker itself does not need the production
  `internal/parties` endpoints (and their platform-token policy): the register component's `fakes/` project adds
  one localtest-only, anonymous controller that lists the seeded persons and parties with user ids for the picker.
- **Hot reload** of policies and app metadata is lost compared with today's in-process reads unless the fakes
  poll the app; acceptable for an MVP, worth a setting.

## MVP order

1. Root, compose, ingress, `authentication` fake written fresh (today's localtest as reference only), `pdf`. Apps
   log in and get tokens; platform calls still 404.
2. `register` vendored with in-memory persistence. Validates the props/targets mechanism on a real upstream
   repo (CPM, analyzers, ServiceDefaults bootstrap).
3. `authorization` vendored with fakes over the register store. First real PDP decisions locally.
4. `storage` vendored with the shared Postgres, file blob mock, scan mock, metadata push. Apps complete a full
   instance lifecycle on production Storage SQL.
5. `profile`, `events`, `notifications` stubs; retire the old localtest Storage and authorization copies.

## Decisions (2026-10-17)

| Question | Decision |
| --- | --- |
| Repository home | Separate repo for steps 1 and 2 (authentication fake, vendored Register). Move into `altinn-studio` once the props/targets mechanism has survived one real upstream subtree pull. |
| altinn-auth subtree granularity | Two splits for the authorization component: `src/apps/Altinn.Authorization` and `src/libs/Altinn.Authorization.Api.Contracts` (not on NuGet), relative layout preserved. Ask the auth team to publish `Api.Contracts` in parallel so the lib tree can be dropped later. |
| Central package management | Non-issue: `fakes/` sits outside `vendor/` and never sees the vendored `Directory.Packages.props` (verified). |
| Service defaults without Azure | Configuration only, no directive (verified in the package source, see the Register table). |
| App metadata into Storage | Deferred to the storage step. The MVP has no discovery of running apps; test users come from a static `testData.json` in the repo. |
| Ingress | Plain nginx with path-prefix routing. Cookie rewrite and frontend version override are out of scope. **Superseded 2026-10-19:** a YARP-based C# proxy published with Native AOT, part of the localtest app (`localtest/localtest/`), same routes, with app routes pushed by the app runner over HTTP instead of drop-in nginx files and a reload. |
| Images | Local `docker compose build` only during the MVP. Registry and pin policy are decided at the move into the monorepo. |

Still open: analyzers pulled in by the vendored build chains, and how the compose file later maps onto studioctl's
manifest model.

## The model as applied (2026-10-21)

Checked against `TODO.md`'s inherited rules, the component READMEs and their `vendor/UPSTREAM.md` files.

- **Directive sites.** A change to a `vendor/` file is only ever a `#if !LOCALTEST` (production first, unchanged) /
  `#else` / `#endif` block, or a pure addition in `#if LOCALTEST … #endif`; everything else goes in `fakes/`. Most
  components have only additive hooks in `Program.cs` or the host builder (Register 1, Resource Registry 2, Profile 2,
  Correspondence 2, Dialogporten 3, Receipt 1 of 2); `#if !LOCALTEST` sites are kept for code hard-wired to
  infrastructure or to `https` (Storage's Key Vault connect and request links, Notifications' Wolverine bootstrap,
  the adapter's Service Bus and link builders, Events' and Authentication's https checks, Receipt's `goto`).
- **Report before a further site.** A brief names the expected sites; one more needs a report and the owner's
  approval first (task 2: "a third directive site in Authentication is not acceptable without a report first"; the
  Correspondence and Profile briefs: "a third site needs a report first"). Task 7b followed it:
  [`correspondence/party-ids.md`](correspondence/party-ids.md), then approval.
- **Fakes compiled in.** `localtest.build.props` (selected by `DirectoryBuildPropsPath`) imports upstream's props
  where there are any and defines `LOCALTEST`; `localtest.build.targets` adds `Compile Include="fakes/**/*.cs"` to the
  host, which also keeps upstream's `packages.lock.json` restores working (Dialogporten's `--locked-mode`). Only the
  Register links a separate fakes project (`fakes/Altinn.Register.Localtest/`).
- **Copies, not subtrees.** `vendor/` is a `git archive` (or an identical `rsync`) of the pinned upstream paths,
  committed unmodified first; `vendor/UPSTREAM.md` holds the pin, the copied paths, every directive site and the
  re-pin steps (`localtest/tools/repin-vendor.py`). The automated check that `vendor/` is upstream plus directive
  blocks is task 1, deferred.
- **Dockerfiles mirror upstream.** Same base images, build steps and runtime user, every difference a `localtest:`
  line; Correspondence pins upstream's floating tags by digest as such a deviation.
- **The shared Maskinporten fake is copied.** One canonical source in
  [`../localtest/components/shared/fakes/Maskinporten/`](../localtest/components/shared/README.md), copied unchanged
  into each consumer's `fakes/` (Notifications, Dialogporten, the adapter, Correspondence); no shared build context
  (task 4). Task 15, unapproved, would make it unnecessary upstream.
- **Groups.** A component the default stack does not need is an optional group in studioctl's group table
  (`localtest/studioctl/src/cli/internal/cmd/env/localtestv2/groups.go`): `dialogporten`, `receipt`, `correspondence`
  (implies `dialogporten`) and `everything`, all off by default, `--with` and `--without` on `env up`. A route of a
  component that is off answers `503` problem details naming the `--with` command.
- **Orchestration.** No compose file; studioctl's `localtest-v2` runtime starts the images built by
  `.github/workflows/images.yml`, studioctl-server is the front door and provisions apps, and the localtest app keeps
  the fake ID-porten, Maskinporten and `/Home`.

## Where the model was bent (2026-10-21)

| Where | What | Why |
| --- | --- | --- |
| Dialogporten, Application project `FluentValidationStringExtensions.cs` (task 10) | A `#if !LOCALTEST` site added by the adapter's task in another component: `IsValidHttpsUrl` accepts `http` too | The stack is plain http, and production's validators refused every dialog the adapter built (400). It is a validation rule, not infrastructure, and lets a service owner create http links locally that production refuses (documented in `components/dialogporten/README.md`) |
| Events, `OutboundService.GetSourceFilter` and `SubscriptionController.IsAppSubscription` (task 12) | Two `#if !LOCALTEST` sites: a source on the platform host is an app's only when its first path segment is not a platform component's prefix | Production tells app sources by host (`apps.altinn.no`); the stack serves apps and platform components on one host, so Dialogporten's sources were cut to `.../dialogporten/api` (one cached authorization decision for every app's dialog events) and a Dialogporten source filter made an app subscription. Both are private decisions selecting a production code path; no fake reaches them. Reported first in `components/events/README.md`, "Directive sites" |
| Correspondence, `XacmlRequestFactory` (task 7b, **in progress**) | The component's first `#if !LOCALTEST` site: any 64-bit integer party id is a party id | Production guesses a party id's type by its length (8 or 10 digits) and the seed's 6-digit ids fail every PDP-checked call; renumbering the seed would break Cypress and apps' test data. Reported first ([`correspondence/party-ids.md`](correspondence/party-ids.md)), approved 2026-10-21, with an upstream fix proposed |
| studioctl ([`../localtest/studioctl/`](../localtest/studioctl/UPSTREAM.md)) | Vendored, but changed by **ordinary commits** on top of the unmodified baseline `3ecc3fb`, no `#if LOCALTEST` | The change is meant to be upstreamed to `Altinn/altinn-studio` as-is (branch `feat/studioctl-localtest-v2-stack` on `ivarne-agent/altinn-studio`); the patch is regenerated with `git diff 3ecc3fb HEAD` |
| Register | Fakes in a separate project linked by `ProjectReference`, not compiled into the host | Built first (2026-10-17), before the compiled-in pattern settled; not changed since |
