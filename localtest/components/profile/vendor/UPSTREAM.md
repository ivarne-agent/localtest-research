# Vendored copy of Altinn/altinn-profile

```json vendor-sync
{
  "repository": "Altinn/altinn-profile",
  "branch": "main",
  "commit": "d1f9871c40bf00eda949338957ce4259e4ae5a24",
  "date": "2026-10-19",
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
- Commit: [`d1f9871c40bf00eda949338957ce4259e4ae5a24`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24) (main, 2026-10-19)
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

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-profile` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites, adds and deletes
   files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src Dockerfile .dockerignore docker-compose.yml .editorconfig stylecop.json`.
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `src/Altinn.Profile/Program.cs` may differ.
3. **Keep `../Dockerfile` as equal to `Dockerfile` as possible.** It mirrors the upstream file (base images and digests,
   the layered restore, publish, the `Migration` copy, the `dotnet` user, `/tmp/logtelemetry`), and every deviation is
   marked `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in `../Dockerfile`.
4. Fix the localtest code (`../fakes/`, the overrides, `../postgres-init/`) for upstream's changes. The fakes depend on
   `IContactRegisterHttpClient` and the KRR change log models, `IOrganizationNotificationAddressUpdateClient`,
   `IOrganizationNotificationAddressSyncClient` and the brreg change log models (`Entry`, `EntryContent`, `DataMapper`),
   `IProfileSettingsRepository`, `ProfileDbContext`, `JobLeaseNames`, and `PostgreSqlSettings` with the admin connection
   string. New EF migrations run at the next start (no volume reset needed). Update the commit line above, build the
   image (`podman build -t localtest-profile:dev ..`) and run `../smoke-test.sh`. Commit the re-pin of the vendored
   files on its own, separate from localtest changes.
