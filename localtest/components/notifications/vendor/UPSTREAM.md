# Vendored copy of Altinn/altinn-notifications

- Repository: https://github.com/Altinn/altinn-notifications
- Commit: [`a4aa959a5c50094048aceb98af8dccd9db17fca8`](https://github.com/Altinn/altinn-notifications/tree/a4aa959a5c50094048aceb98af8dccd9db17fca8) (main, 2026-10-20; still `origin/main` when vendored)
- Copied paths, relative layout preserved: `components/api/src/` (the orders API host `Altinn.Notifications`, `.Core`,
  `.Integrations`, `.Persistence` with the Yuniql `Migration/` folders, and the build-time tool `DbTools`),
  `components/shared/src/` (`Altinn.Notifications.Shared`), the root `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json` and `stylecop.json`, and the upstream `components/api/Dockerfile` for
  reference. Not copied: `components/email-service` and `components/sms-service` (the hosts behind the queue, not run
  locally), every `test/` folder, `tools/`, `docs/`, `.github/`, the solution files. Upstream has no lock files.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Directive sites

All three are in `components/api/src/Altinn.Notifications/Program.cs` (line numbers checked 2026-10-22):

1. Lines 51-53, after `SetConfigurationProviders(builder.Configuration);`, a pure addition:
   `#if LOCALTEST` … `Altinn.Notifications.Localtest.LocaltestNotificationsExtensions.AddLocaltestConfiguration(builder.Configuration);` … `#endif`.
2. Lines 236-240, `services.AddWolverineServices(config, builder.Environment);` in `ConfigureServices`, production first:
   `#if !LOCALTEST` (the production call) `#else` `…LocaltestNotificationsExtensions.AddLocaltestMessaging(services, config);` `#endif`.
   `AddWolverineServices` throws without a Service Bus connection string and registers hosted services that connect at
   start, so it cannot be neutralised by configuration or a later `Replace`.
3. Lines 40-45, right after `var builder = WebApplication.CreateBuilder(args);`, a pure addition: the
   service-discovery registration (`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(http =>
   http.AddServiceDiscovery())`, task 25, `planned-components/service-discovery.md`).

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-notifications` with both commits, re-pin with the shared script, which takes upstream's
   new version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites, adds and
   deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor components/api/src components/shared/src components/api/Dockerfile Directory.Build.props Directory.Build.targets Directory.Packages.props global.json stylecop.json`.
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `components/api/src/Altinn.Notifications/Program.cs` may differ.
3. **Keep `../Dockerfile` as equal to `components/api/Dockerfile` as possible.** It mirrors the upstream file (base images
   and digests, the layered restore, publish, `tzdata`, the `Migration` and `Views` copies, the `dotnet` user), and every
   deviation is marked `localtest:`. When `git diff <pin> <new> -- components/api/Dockerfile` shows a change, make the
   same change in `../Dockerfile`.
4. Fix the localtest code (`../fakes/`, the overrides, `../postgres-init/`) for upstream's changes (the notification
   services, the publisher interfaces and `UpdateSendStatus` are what the fakes depend on), update the commit line above,
   build the image (`podman build -t localtest-notifications:dev ..`) and run `../smoke-test.sh`. Every re-pin runs the
   Yuniql migrations again on a fresh volume. Commit the re-pin of the vendored files on its own, separate from localtest
   changes.
