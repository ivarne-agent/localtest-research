# Vendored copy of Altinn/altinn-events

```json vendor-sync
{
  "repository": "Altinn/altinn-events",
  "branch": "main",
  "commit": "1a4a80c4599e9e176c04bce1b5da7861ca1c54a6",
  "date": "2026-10-19",
  "paths": ["src/Events", "src/Events.Common", "src/DbTools", "stylecop.json", ".editorconfig", "Dockerfile"],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["src/Events/Altinn.Platform.Events.csproj"],
  "follow": [
    {"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "follows": "OpenTelemetry"}
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
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/Events/Controllers/SubscriptionController.cs` | 193-198 | replacement | `return uri is not null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps \|\| u...` |
| `src/Events/Controllers/SubscriptionController.cs` | 213-221 | replacement | `else if (!string.IsNullOrEmpty(subscription.SourceFilter?.ToString()) &&` |
| `src/Events/Program.cs` | 59-64 | addition | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestService...` |
| `src/Events/Program.cs` | 69-71 | addition | `Altinn.Platform.Events.Localtest.LocaltestEventsExtensions.AddLocaltestConfiguration(bu...` |
| `src/Events/Program.cs` | 76-78 | addition | `Altinn.Platform.Events.Localtest.LocaltestEventsExtensions.AddLocaltestEvents(builder);` |
| `src/Events/Program.cs` | 170-176 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
| `src/Events/Services/OutboundService.cs` | 429-435 | replacement | `if (source.DnsSafeHost.Contains(_platformSettings.AppsDomain) &&` |
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

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-events` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites, adds and
   deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src/Events src/Events.Common src/DbTools stylecop.json .editorconfig Dockerfile`.
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and the files with `LOCALTEST` sites (`src/Events/Program.cs`,
   `src/Events/Controllers/SubscriptionController.cs`, `src/Events/Services/OutboundService.cs`) may differ.
3. **Keep `../Dockerfile` as equal to `Dockerfile` as possible.** It mirrors the upstream file line for line (base
   images and digests, the DbTools build, build and publish, the `Migration` copy, the `dotnet` user), and every
   deviation is marked `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in
   `../Dockerfile`.
4. Fix the localtest code (`../fakes/`, the overrides, `../postgres-init/`) for upstream's changes, update the commit
   line above, build the image (`podman build -t localtest-events:dev ..`) and run `../smoke-test.sh`. Commit the
   re-pin of the vendored files on its own, separate from localtest changes.
