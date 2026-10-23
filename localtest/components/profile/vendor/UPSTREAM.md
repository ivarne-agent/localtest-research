# Vendored copy of Altinn/altinn-profile

- Repository: https://github.com/Altinn/altinn-profile
- Commit: [`d1f9871c40bf00eda949338957ce4259e4ae5a24`](https://github.com/Altinn/altinn-profile/tree/d1f9871c40bf00eda949338957ce4259e4ae5a24)
  (main, 2026-10-19, "Document the notification settings DELETE response without a body (#1064)"; the pin of the
  research in `planned-components/profile/profile.md`)
- Copied paths, relative layout preserved: `src/` (the host `Altinn.Profile`, `.Core`, `.Integrations` with the EF
  `Migrations/` and the legacy Yuniql `Migration/` folder the Dockerfile still copies, `.Models`, and the two vendored
  libraries `ServiceDefaults.Jobs` and `ServiceDefaults.Leases`), `Dockerfile`, `.dockerignore`, `docker-compose.yml`,
  `.editorconfig`, `stylecop.json`. Not copied: `test/`, `docs/`, `.github/`, `dbsetup.sh`, the solution file. Upstream
  has no `Directory.Build.props/.targets`, no central package management and no lock files.
- Copied with `git archive d1f9871 src Dockerfile .dockerignore docker-compose.yml .editorconfig stylecop.json`, no
  subtree yet. The first commit holds the files unmodified; every later change inside this folder is a `#if !LOCALTEST`
  / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST … #endif` addition.

## Directive sites

Three, all in `src/Altinn.Profile/Program.cs`, all pure additions (no `#if !LOCALTEST`; line numbers checked 2026-10-22):

1. Lines 64-66, after `SetConfigurationProviders(builder.Configuration);`:
   `#if LOCALTEST` … `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestConfiguration(builder.Configuration);` … `#endif`.
   It must come before `ConfigureServices`: `AddRegisterService` reads the connection string, `AddMaskinportenClient`
   binds its settings and `AddSyncJobs` reads `JobSettings` at registration time.
2. Lines 230-232, after `services.AddSyncJobs(config);` in `ConfigureServices`:
   `#if LOCALTEST` … `Altinn.Profile.Localtest.LocaltestProfileExtensions.AddLocaltestServices(services, config);` … `#endif`.
   It must come after `AddRegisterService` and `AddMaskinportenClient`, whose KRR and brreg clients it replaces.
3. Lines 54-59, right after `var builder = WebApplication.CreateBuilder(args);`: the service-discovery registration
   (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25,
   `planned-components/service-discovery.md`).

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
