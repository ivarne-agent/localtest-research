# Vendored copy of Altinn/altinn-storage

```json vendor-sync
{
  "repository": "Altinn/altinn-storage",
  "branch": "main",
  "commit": "9e72a9f128debd7b9815b615d842a2d1a6d343c5",
  "date": "2026-10-19",
  "paths": [
    "src/Storage",
    "src/DbTools",
    "Directory.Build.props",
    ".editorconfig",
    ".csharpierignore",
    ".config/dotnet-tools.json",
    "infra/postgres_init.sql",
    "Dockerfile"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["src/Storage/Altinn.Platform.Storage.csproj"],
  "follow": [
    {"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "follows": "OpenTelemetry"}
  ],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-storage
- Commit: [`9e72a9f128debd7b9815b615d842a2d1a6d343c5`](https://github.com/Altinn/altinn-storage/tree/9e72a9f128debd7b9815b615d842a2d1a6d343c5) (main, 2026-10-19)
<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/Storage/` (the host, including `Migration/`), `src/DbTools/`
  (referenced by relative path from the host csproj), and the build files on the path from the repository root:
  `Directory.Build.props`, `.editorconfig`, `.csharpierignore`, `.config/dotnet-tools.json`; plus
  `infra/postgres_init.sql` (database, schema and role setup run by `../postgres-init/10-storage.sh` in the stack's
  Postgres image) and the upstream
  `Dockerfile` for reference. There is no `global.json`, `Directory.Packages.props` or ruleset upstream.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/Storage/Controllers/InstancesController.cs` | 1465-1471 | replacement | `string host = $"{Request.Scheme}://{Request.Host}";` |
| `src/Storage/Program.cs` | 58-63 | addition | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestService...` |
| `src/Storage/Program.cs` | 77-79 | addition | `Altinn.Platform.Storage.Localtest.LocaltestStorageExtensions.AddLocaltestStorage(builder);` |
| `src/Storage/Program.cs` | 142-146 | replacement | `Altinn.Platform.Storage.Localtest.LocaltestStorageExtensions.AddLocaltestConfiguration(...` |
| `src/Storage/Program.cs` | 248-257 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
<!-- vendor-sync:end sites -->

Five sites:

- `src/Storage/Program.cs`, right after `WebApplication.CreateBuilder`, a pure addition: the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`).
- `src/Storage/Program.cs`, after `ConfigureWolverine`, a pure addition: `AddLocaltestStorage(builder)`, the fakes.
- `src/Storage/Program.cs`, in `SetConfigurationProviders`: `#if !LOCALTEST` the Key Vault read
  (`ConnectToKeyVaultAndSetApplicationInsights`) `#else` `AddLocaltestConfiguration(config)`, the localtest defaults.
- `src/Storage/Program.cs`, the Azure Monitor exporters in `ConfigureServices`: `#if !LOCALTEST` production's call
  `#else` `LocaltestOtlp.AddLocaltestOtlp`, OTLP for traces, metrics and logs from the standard variables when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, nothing otherwise (`../fakes/Otlp/`, a copy of `components/shared/fakes/Otlp/`; TODO.md task 53).
- `src/Storage/Controllers/InstancesController.cs`, `BuildRequestLink`.

## Syncing

`python3 localtest/tools/vendor-sync.py sync storage` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `Dockerfile` (base images and digests, the DbTools build, the `Migration` copy,
`USER`), and every deviation is marked `localtest:`; the tool three-way merges upstream's changes into it when that is
clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a different build.

Storage has no smoke test of its own: the root `../../../smoke-test.sh` exercises it, against an image built with
`--dev-storage`.
