# Vendored copy of Altinn/altinn-events

```json vendor-sync
{
  "repository": "Altinn/altinn-events",
  "branch": "main",
  "commit": "1a4a80c4599e9e176c04bce1b5da7861ca1c54a6",
  "date": "2026-10-19",
  "paths": [
    "src/Events",
    "src/Events.Common",
    "src/DbTools",
    "stylecop.json",
    ".editorconfig",
    "Dockerfile"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    { "upstream": "Dockerfile", "ours": "Dockerfile", "merge": true }
  ],
  "watch": [],
  "projects": ["src/Events/Altinn.Platform.Events.csproj"],
  "follow": [
    {
      "package": "OpenTelemetry.Exporter.OpenTelemetryProtocol",
      "follows": "OpenTelemetry"
    }
  ],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-events
- Commit: [`1a4a80c4599e9e176c04bce1b5da7861ca1c54a6`](https://github.com/Altinn/altinn-events/tree/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6) (main, 2026-10-19)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/Events/` (the host, including `Migration/`, unused locally),
  `src/Events.Common/` and `src/DbTools/` (both referenced by relative path from the host csproj), the root
  `stylecop.json` and `.editorconfig`, and the upstream `Dockerfile` for reference. Not copied: `src/Events.Functions`
  (Azure Functions for the legacy Storage Queue path), `emulator/`, `test/`. There is no `Directory.Build.props`,
  `Directory.Packages.props` or `global.json` upstream.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                               | Lines   | Kind        | First line of the `LOCALTEST` branch                                                         |
| -------------------------------------------------- | ------- | ----------- | -------------------------------------------------------------------------------------------- |
| `src/Events/Controllers/SubscriptionController.cs` | 193-198 | replacement | `return uri is not null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps \|\| ...`  |
| `src/Events/Controllers/SubscriptionController.cs` | 213-221 | replacement | `else if (!string.IsNullOrEmpty(subscription.SourceFilter?.ToString()) &&`                   |
| `src/Events/Program.cs`                            | 59-64   | addition    | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery....`                    |
| `src/Events/Program.cs`                            | 69-71   | addition    | `Altinn.Platform.Events.Localtest.LocaltestEventsExtensions.AddLocaltestConfiguration(...`   |
| `src/Events/Program.cs`                            | 76-78   | addition    | `Altinn.Platform.Events.Localtest.LocaltestEventsExtensions.AddLocaltestEvents(builder);`    |
| `src/Events/Program.cs`                            | 170-176 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
| `src/Events/Services/OutboundService.cs`           | 429-435 | replacement | `if (source.DnsSafeHost.Contains(_platformSettings.AppsDomain) &&`                           |

<!-- vendor-sync:end sites -->

Seven sites:

- `src/Events/Program.cs`, right after `WebApplication.CreateBuilder`: the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`).
- `src/Events/Program.cs`, two pure additions: `AddLocaltestConfiguration(builder.Configuration)` (the localtest
  defaults) and `AddLocaltestEvents(builder)` (the fakes).
- `src/Events/Program.cs`, `AddAzureMonitorTelemetryExporters` in `ConfigureServices`: `#if !LOCALTEST` production's
  call `#else` `LocaltestOtlp.AddLocaltestOtlp`, OTLP for traces, metrics and logs from the standard variables when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, nothing otherwise (`../fakes/Otlp/`, a copy of `components/shared/fakes/Otlp/`; TODO.md task 53).
- `src/Events/Controllers/SubscriptionController.cs`: `IsValidUri` and `IsAppSubscription`.
- `src/Events/Services/OutboundService.cs`: `GetSourceFilter`. This one and `IsAppSubscription` (task 12) call
  `fakes/Sources/LocaltestAppSources.cs`.

## Syncing

`python3 localtest/tools/vendor-sync.py sync events` from the repository root stages the sync and prints what to triage,
build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md) describes
(resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block above
drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build, the
package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `Dockerfile` (base images and digests, the DbTools build, build and publish, the
`Migration` copy, the `dotnet` user), and every deviation is marked `localtest:`; the tool three-way merges upstream's
changes into it when that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a
different build.

What the localtest code follows: `../fakes/`, the overrides and `../postgres-init/`.
