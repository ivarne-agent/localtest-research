# Vendored copy of Altinn/altinn-profile

```json vendor-sync
{
  "repository": "Altinn/altinn-profile",
  "branch": "main",
  "commit": "d0cd92d785cc6ce66839d189ae7aa132c2e6b5a2",
  "date": "2026-10-22",
  "paths": ["src", "Dockerfile", ".dockerignore", "docker-compose.yml", ".editorconfig", "stylecop.json"],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["src/Altinn.Profile/Altinn.Profile.csproj"],
  "follow": [
    {"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "follows": "OpenTelemetry"}
  ],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-profile
- Commit: [`d0cd92d785cc6ce66839d189ae7aa132c2e6b5a2`](https://github.com/Altinn/altinn-profile/tree/d0cd92d785cc6ce66839d189ae7aa132c2e6b5a2) (main, 2026-10-22)
<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/` (the host `Altinn.Profile`, `.Core`, `.Integrations` with the EF
  `Migrations/` and the legacy Yuniql `Migration/` folder the Dockerfile still copies, `.Models`, and the two vendored
  libraries `ServiceDefaults.Jobs` and `ServiceDefaults.Leases`), `Dockerfile`, `.dockerignore`, `docker-compose.yml`,
  `.editorconfig`, `stylecop.json`. Not copied: `test/`, `docs/`, `.github/`, `dbsetup.sh`, the solution file. Upstream
  has no `Directory.Build.props/.targets`, no central package management and no lock files.
- Copied with `git archive d1f9871 src Dockerfile .dockerignore docker-compose.yml .editorconfig stylecop.json`, no
  subtree yet. The first commit holds the files unmodified; every later change inside this folder is a `#if !LOCALTEST`
  / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST … #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/Altinn.Profile/Program.cs` | 54-59 | addition | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestService...` |
| `src/Altinn.Profile/Program.cs` | 64-66 | addition | `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestConfiguration(builder.C...` |
| `src/Altinn.Profile/Program.cs` | 152-158 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
| `src/Altinn.Profile/Program.cs` | 236-238 | addition | `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestServices(services, conf...` |
<!-- vendor-sync:end sites -->

Four, all in `src/Altinn.Profile/Program.cs`, three pure additions and one `#if !LOCALTEST` / `#else`:

1. After `SetConfigurationProviders(builder.Configuration);`:
   `#if LOCALTEST` … `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestConfiguration(builder.Configuration);` … `#endif`.
   It must come before `ConfigureServices`: `AddRegisterService` reads the connection string, `AddMaskinportenClient`
   binds its settings and `AddSyncJobs` reads `JobSettings` at registration time.
2. After `services.AddSyncJobs(config);` in `ConfigureServices`:
   `#if LOCALTEST` … `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestServices(services, config);` … `#endif`.
   It must come after `AddRegisterService` and `AddMaskinportenClient`, whose KRR and brreg clients it replaces.
3. Right after `var builder = WebApplication.CreateBuilder(args);`: the service-discovery registration
   (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25,
   `planned-components/service-discovery.md`).
4. `AddAzureMonitorTelemetryExporters(services, config);` in `ConfigureServices`: `#if !LOCALTEST` the
   production call `#else` `LocaltestOtlp.AddLocaltestOtlp`, OTLP for traces, metrics and logs from the standard variables when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, nothing otherwise (`../fakes/Otlp/`, a copy of `components/shared/fakes/Otlp/`; TODO.md task 53).

## Syncing

`python3 localtest/tools/vendor-sync.py sync profile` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `Dockerfile` (base images and digests, the layered restore, publish, the `Migration`
copy, the `dotnet` user, `/tmp/logtelemetry`), and every deviation is marked `localtest:`; the tool three-way merges
upstream's changes into it when that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift
into a different build.

What the localtest code follows: the fakes depend on `IContactRegisterHttpClient` and the KRR change log models,
`IOrganizationNotificationAddressUpdateClient`, `IOrganizationNotificationAddressSyncClient` and the brreg change log
models (`Entry`, `EntryContent`, `DataMapper`), `IProfileSettingsRepository`, `ProfileDbContext`, `JobLeaseNames`, and
`PostgreSqlSettings` with the admin connection string. New EF migrations run at the next start (no volume reset needed).
The OTLP exporter in `../localtest.build.targets` follows the host's OpenTelemetry version (`follow`).
