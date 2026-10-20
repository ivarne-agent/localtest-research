# Vendored copy of Altinn/altinn-dialogporten-adapter

- Repository: https://github.com/Altinn/altinn-dialogporten-adapter
- Commit: [`86364b876adbff3d98843d723cee76ae215203a2`](https://github.com/Altinn/altinn-dialogporten-adapter/tree/86364b876adbff3d98843d723cee76ae215203a2)
  (main, 2026-10-19; `origin/main` of the reference clone on 2026-10-20, the pin the planning report recommends)
- Copied paths, relative layout preserved: `src/Altinn.DialogportenAdapter.WebApi` (the host), `src/Altinn.DialogportenAdapter.Contracts`
  (`SyncInstanceCommand`, the queue names, the Service Bus defaults; referenced by relative path from the host csproj),
  `Directory.Build.props`, `global.json` (kept for the record: upstream's Dockerfile leaves it out of the build, and it pins 10.0.400 with
  `rollForward: disable` while the image carries 10.0.401), the upstream `Dockerfile` and `.dockerignore` for reference.
  Not copied: `src/Altinn.DialogportenAdapter.EventSimulator` (the Azure Table-backed migration tool, its own image), `tests/`,
  `AzureServiceBusEmulator/`, `docker-compose-service-bus.yml`, `.github/`, the solution file.
- Copied with `git archive` of that commit, no subtree. The first commit holds the files unmodified; every later change inside this folder
  is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST ... #endif` addition.

## Directive sites

| File | Site |
| --- | --- |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs:42-44` | `#if LOCALTEST` `LocaltestAdapterExtensions.AddLocaltestConfiguration(builder.Configuration)`: the localtest defaults, before `ConfigureDialogportenAdapterServices` reads `Settings` eagerly |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs:49-51` | `#if LOCALTEST` `LocaltestAdapterExtensions.AddLocaltestAdapter(builder)`: http OpenID metadata, the Maskinporten fake (after the package's `TryAdd`) |
| `src/Altinn.DialogportenAdapter.WebApi/Program.cs:56-58` | `#if LOCALTEST` `LocaltestAdapterExtensions.MapLocaltestRoutes(app)`: the anonymous enqueue endpoint |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs:63-75` | `ConfigureAdapterDefaults` (Azure Service Bus; throws on an empty connection string) under `#if !LOCALTEST`; `#else` its three non-transport settings |
| `src/Altinn.DialogportenAdapter.WebApi/Common/Extensions/ServiceCollectionExtensions.cs:179-191` | The two Service Bus listeners under `#if !LOCALTEST`; `#else` one sequential local queue under the production queue name and the publish rule for `SyncInstanceCommand` |
| `src/Altinn.DialogportenAdapter.WebApi/Settings.cs:43-50` | `GetAppUriForOrg` / `GetPlatformUri` build `{org}.apps.{host}` and `platform.{host}` without the port; `#else` the one origin of `Altinn:BaseUri`, port kept |
| `src/Altinn.DialogportenAdapter.WebApi/Features/Command/Sync/StorageDialogportenDataMerger.cs:366-371`, `:398-403` | `element.SelfLinks.Platform` (Storage hard-codes `https://platform.{host}`); `#else` `LocaltestLinks.ToLocalPlatformUrl`, the same URL on the stack's origin. Identical lines in `CreateAttachmentDto` and `CreateTransmissionAttachmentDto` |

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-dialogporten-adapter` with both commits, re-pin with the shared script, which takes upstream's new version of
   every file that is unchanged locally, three-way merges the four files with `LOCALTEST` sites, adds and deletes files, and reports conflicts:
   `../../../tools/repin-vendor.py <clone> <pin> <new> . src/Altinn.DialogportenAdapter.WebApi src/Altinn.DialogportenAdapter.Contracts Directory.Build.props global.json Dockerfile .dockerignore`
   (run from this folder).
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only `UPSTREAM.md` and the four
   files with sites (`Program.cs`, `ServiceCollectionExtensions.cs`, `Settings.cs`, `StorageDialogportenDataMerger.cs`) may differ.
3. **Keep `../Dockerfile` as equal to `vendor/Dockerfile` as possible.** When `git diff <pin> <new> -- Dockerfile` shows a change (a base
   image digest, a step), make the same change in `../Dockerfile`.
4. Fix the fakes (`../fakes/`) for upstream's changes (a new required setting goes into `LocaltestAdapterExtensions.AddLocaltestConfiguration`),
   update the commit line above, build the image (`podman build -t localtest-dialogporten-adapter:dev ..`) and run `../smoke-test.sh`.
   Commit the re-pin of the vendored files on its own, separate from localtest changes.
