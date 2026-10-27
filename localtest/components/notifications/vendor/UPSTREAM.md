# Vendored copy of Altinn/altinn-notifications

```json vendor-sync
{
  "repository": "Altinn/altinn-notifications",
  "branch": "main",
  "commit": "a4aa959a5c50094048aceb98af8dccd9db17fca8",
  "date": "2026-10-20",
  "paths": [
    "components/api/src",
    "components/shared/src",
    "components/api/Dockerfile",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "stylecop.json"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "components/api/Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["components/api/src/Altinn.Notifications/Altinn.Notifications.csproj"],
  "follow": [],
  "reminders": [
    "new Yuniql migrations (`Migration/` folders) run only on a fresh volume: `env reset -r localtest-v2 --yes` before the smoke test"
  ]
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-notifications
- Commit: [`a4aa959a5c50094048aceb98af8dccd9db17fca8`](https://github.com/Altinn/altinn-notifications/tree/a4aa959a5c50094048aceb98af8dccd9db17fca8) (main, 2026-10-20)
<!-- vendor-sync:end pin -->
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

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `components/api/src/Altinn.Notifications/Program.cs` | 40-45 | addition | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestService...` |
| `components/api/src/Altinn.Notifications/Program.cs` | 51-53 | addition | `Altinn.Notifications.Localtest.LocaltestNotificationsExtensions.AddLocaltestConfigurati...` |
| `components/api/src/Altinn.Notifications/Program.cs` | 191-198 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
| `components/api/src/Altinn.Notifications/Program.cs` | 242-246 | replacement | `Altinn.Notifications.Localtest.LocaltestNotificationsExtensions.AddLocaltestMessaging(s...` |
<!-- vendor-sync:end sites -->

All four are in `components/api/src/Altinn.Notifications/Program.cs`:

1. After `SetConfigurationProviders(builder.Configuration);`, a pure addition:
   `#if LOCALTEST` … `Altinn.Notifications.Localtest.LocaltestNotificationsExtensions.AddLocaltestConfiguration(builder.Configuration);` … `#endif`.
2. `services.AddWolverineServices(config, builder.Environment);` in `ConfigureServices`, production first:
   `#if !LOCALTEST` (the production call) `#else` `…LocaltestNotificationsExtensions.AddLocaltestMessaging(services, config);` `#endif`.
   `AddWolverineServices` throws without a Service Bus connection string and registers hosted services that connect at
   start, so it cannot be neutralised by configuration or a later `Replace`.
3. Right after `var builder = WebApplication.CreateBuilder(args);`, a pure addition: the
   service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`).
4. `AddAzureMonitorTelemetryExporters(services, config);` in `ConfigureServices`: `#if !LOCALTEST` the
   production call (its own `OtelSettings:Endpoint` traces exporter and the Azure Monitor exporters) `#else`
   `LocaltestOtlp.AddLocaltestOtlp`, OTLP for traces, metrics and logs from the standard variables when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, nothing otherwise (`../fakes/Otlp/`, a copy of `components/shared/fakes/Otlp/`; TODO.md task 53). The skipped function holds the only signal-specific
   `AddOtlpExporter`, so `UseOtlpExporter()` does not clash with it.

## Syncing

`python3 localtest/tools/vendor-sync.py sync notifications` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `components/api/Dockerfile` (base images and digests, the layered restore, publish,
`tzdata`, the `Migration` and `Views` copies, the `dotnet` user), and every deviation is marked `localtest:`; the tool
three-way merges upstream's changes into it when that is clean, and otherwise prints the hunk to carry over by hand.
Never let it drift into a different build.

What the localtest code follows: `../fakes/`, the overrides and `../postgres-init/`; the fakes depend on the
notification services, the publisher interfaces and `UpdateSendStatus`. Every sync runs the Yuniql migrations again on a
fresh volume (a reminder).
