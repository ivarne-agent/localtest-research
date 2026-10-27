# Vendored copy of Altinn/altinn-dialogporten-adapter

```json vendor-sync
{
  "repository": "Altinn/altinn-dialogporten-adapter",
  "branch": "main",
  "commit": "86364b876adbff3d98843d723cee76ae215203a2",
  "date": "2026-10-19",
  "paths": [
    "src/Altinn.DialogportenAdapter.WebApi",
    "src/Altinn.DialogportenAdapter.Contracts",
    "Directory.Build.props",
    "global.json",
    "Dockerfile",
    ".dockerignore"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    { "upstream": "Dockerfile", "ours": "Dockerfile", "merge": true }
  ],
  "watch": [],
  "projects": [
    "src/Altinn.DialogportenAdapter.WebApi/Altinn.DialogportenAdapter.WebApi.csproj"
  ],
  "follow": [
    {
      "package": "OpenTelemetry.Exporter.OpenTelemetryProtocol",
      "follows": "OpenTelemetry"
    }
  ],
  "reminders": [
    "a new required setting goes into `fakes/` `LocaltestAdapterExtensions.AddLocaltestConfiguration`"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-dialogporten-adapter
- Commit: [`86364b876adbff3d98843d723cee76ae215203a2`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/86364b876adbff3d98843d723cee76ae215203a2) (main, 2026-10-19)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/Altinn.DialogportenAdapter.WebApi` (the host), `src/Altinn.DialogportenAdapter.Contracts`
  (`SyncInstanceCommand`, the queue names, the Service Bus defaults; referenced by relative path from the host csproj),
  `Directory.Build.props`, `global.json` (kept for the record: upstream's Dockerfile leaves it out of the build, and it pins 10.0.400 with
  `rollForward: disable` while the image carries 10.0.401), the upstream `Dockerfile` and `.dockerignore` for reference.
  Not copied: `src/Altinn.DialogportenAdapter.EventSimulator` (the Azure Table-backed migration tool, its own image), `tests/`,
  `AzureServiceBusEmulator/`, `docker-compose-service-bus.yml`, `.github/`, the solution file.
- Copied with `git archive` of that commit, no subtree. The first commit holds the files unmodified; every later change inside this folder
  is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST ... #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                           | Lines   | Kind        | First line of the `LOCALTEST` branch                                                     |
| ---------------------------------------------------------------------------------------------- | ------- | ----------- | ---------------------------------------------------------------------------------------- |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs`       | 56-65   | replacement | `Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterTelemetry....`              |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs`       | 69-81   | replacement | `opts.EnableAutomaticFailureAcks = false;`                                               |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs`       | 185-197 | replacement | `opts.LocalQueue(ContractConstants.AdapterQueueName).Sequential();`                      |
| `src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs` | 366-371 | replacement | `var platformUrl = Localtest.LocaltestLinks.ToLocalPlatformUrl(element.SelfLinks....`    |
| `src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs` | 398-403 | replacement | `var platformUrl = Localtest.LocaltestLinks.ToLocalPlatformUrl(element.SelfLinks....`    |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs`                                             | 31-36   | addition    | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery....`                |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs`                                             | 48-50   | addition    | `Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions....`             |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs`                                             | 55-57   | addition    | `Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions....`             |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs`                                             | 62-64   | addition    | `Altinn.DialogportenAdapter.WebApi.Localtest.LocaltestAdapterExtensions....`             |
| `src/Altinn.DialogportenAdapter.WebApi/Settings.cs`                                            | 43-50   | replacement | `public Uri GetAppUriForOrg(string org, string appId) => new($"{BaseUri.GetLeftPart(...` |

<!-- vendor-sync:end sites -->

| File | Site |
| --- | --- |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs` | `#if LOCALTEST` right after `WebApplication.CreateBuilder`: the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`) |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs` | `#if LOCALTEST` `LocaltestAdapterExtensions.AddLocaltestConfiguration(builder.Configuration)`: the localtest defaults, before `ConfigureDialogportenAdapterServices` reads `Settings` eagerly |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs` | `#if LOCALTEST` `LocaltestAdapterExtensions.AddLocaltestAdapter(builder)`: http OpenID metadata, the Maskinporten fake (after the package's `TryAdd`) |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs` | `#if LOCALTEST` `LocaltestAdapterExtensions.MapLocaltestRoutes(app)`: the anonymous enqueue endpoint |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs` | `if (settings.ApplicationInsights.Enabled) services.ConfigureTelemetry(settings);` (`UseAzureMonitor`, which throws without a connection string) under `#if !LOCALTEST`; `#else` `LocaltestAdapterTelemetry.AddLocaltestAdapterTelemetry(services, configuration)` (`../fakes/LocaltestAdapterTelemetry.cs`): when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, the same pipeline without Azure Monitor (the health-check filter, `service.name` `platform-dialogporten-adapter`, ASP.NET Core and HttpClient traces and metrics) and OTLP for all three signals through `../fakes/Otlp/` (TODO.md task 53); nothing otherwise |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs` | `ConfigureAdapterDefaults` (Azure Service Bus; throws on an empty connection string) under `#if !LOCALTEST`; `#else` its three non-transport settings |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs` | The two Service Bus listeners under `#if !LOCALTEST`; `#else` one sequential local queue under the production queue name and the publish rule for `SyncInstanceCommand` |
| `src/Altinn.DialogportenAdapter.WebApi/Settings.cs` | `GetAppUriForOrg` / `GetPlatformUri` build `{org}.apps.{host}` and `platform.{host}` without the port; `#else` the one origin of `Altinn:BaseUri`, port kept |
| `src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs` (two blocks) | `element.SelfLinks.Platform` (Storage hard-codes `https://platform.{host}`); `#else` `LocaltestLinks.ToLocalPlatformUrl`, the same URL on the stack's origin. Identical lines in `CreateAttachmentDto` and `CreateTransmissionAttachmentDto` |

## Syncing

`python3 localtest/tools/vendor-sync.py sync dialogporten-adapter` from the repository root stages the sync and prints
what to triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`../Dockerfile` mirrors upstream's `Dockerfile` (a base image digest, the steps), and every deviation is marked
`localtest:`; the tool three-way merges upstream's changes into it when that is clean, and otherwise prints the hunk to
carry over by hand. Never let it drift into a different build.

What the localtest code follows: `../fakes/`; a new required setting goes into
`LocaltestAdapterExtensions.AddLocaltestConfiguration`.
