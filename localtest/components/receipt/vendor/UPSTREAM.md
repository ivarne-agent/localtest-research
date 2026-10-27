# Vendored copy of Altinn/altinn-receipt

```json vendor-sync
{
  "repository": "Altinn/altinn-receipt",
  "branch": "main",
  "commit": "babc0ed23483e8c3fb62dad4da903c6a88024914",
  "date": "2026-10-20",
  "paths": ["."],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "Dockerfile", "ours": "Dockerfile", "merge": true},
    {"upstream": ".dockerignore", "ours": ".dockerignore", "merge": true}
  ],
  "watch": [],
  "projects": ["src/backend/Altinn.Receipt/Altinn.Platform.Receipt.csproj"],
  "follow": [
    {"package": "OpenTelemetry.Exporter.OpenTelemetryProtocol", "follows": "OpenTelemetry"}
  ],
  "reminders": [
    "Renovate moves the frontend almost daily: sync on backend changes or on request (decided 2026-10-20)"
  ]
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-receipt
- Commit: [`babc0ed23483e8c3fb62dad4da903c6a88024914`](https://github.com/Altinn/altinn-receipt/tree/babc0ed23483e8c3fb62dad4da903c6a88024914) (main, 2026-10-20)
<!-- vendor-sync:end pin -->
- Copied paths: the whole repository (the backend `src/backend/Altinn.Receipt`, the React frontend `src/frontend` with
  its committed Yarn release, `test/`, the root build and editor files, `.github/`, `.deploy/` and the upstream
  `Dockerfile` for reference). It is small, and the frontend and tests stay comparable with upstream that way.
- Copied with `git archive` of that commit, no subtree yet. The first commit (`receipt: vendor Altinn/altinn-receipt
  at babc0ed, unmodified`) holds the files unmodified, blob for blob; every later change inside this folder is a
  `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST … #endif` addition.
- Pin cadence (decided 2026-10-20): Renovate updates the frontend almost daily; re-pin on backend changes or on
  request only.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/backend/Altinn.Receipt/Program.cs` | 36-41 | addition | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery.AddLocaltestService...` |
| `src/backend/Altinn.Receipt/Program.cs` | 52-54 | addition | `Altinn.Platform.Receipt.Localtest.LocaltestReceipt.Apply(builder.Services, builder.Conf...` |
| `src/backend/Altinn.Receipt/Program.cs` | 190-199 | replacement | `global::Localtest.Shared.Otlp.LocaltestOtlp.AddLocaltestOtlp(services.AddOpenTelemetry(...` |
| `src/backend/Altinn.Receipt/Program.cs` | 271-277 | replacement | `string url = $"{request.Scheme}://{request.Host}{request.Path}{request.QueryString}";` |
<!-- vendor-sync:end sites -->

All four in `src/backend/Altinn.Receipt/Program.cs`:

1. After `ConfigureServices(builder.Services, builder.Configuration);`, a pure addition: `#if LOCALTEST`
   `Altinn.Platform.Receipt.Localtest.LocaltestReceipt.Apply(builder.Services, builder.Configuration);` `#endif`
   (the hook into `../fakes/`).
2. In `Configure`, the status-code page's `goto` URL for a 401: `#if !LOCALTEST` the production
   `https://platform.{GeneralSettings:Hostname}{path}{query}` `#else` the request's own scheme and host `#endif`. There
   is no `platform.` host and no TLS locally, and the proxy keeps the browser's `Host`.
3. Right after `var builder = WebApplication.CreateBuilder(args);`, a pure addition: the
   service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`).
4. The Azure Monitor exporters in `ConfigureServices`: `#if !LOCALTEST` production's `if` and call
   `#else` `LocaltestOtlp.AddLocaltestOtlp`, OTLP for traces, metrics and logs from the standard variables when `OTEL_EXPORTER_OTLP_ENDPOINT` is set, nothing otherwise (`../fakes/Otlp/`, a copy of `components/shared/fakes/Otlp/`; TODO.md task 53). (The Key Vault read that would set the connection string,
   `ConnectToKeyVaultAndSetApplicationInsights`, stays unguarded: it does nothing without `kvSetting:SecretUri`.)

The frontend has no sites: it is built unchanged.

## Syncing

`python3 localtest/tools/vendor-sync.py sync receipt` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

The copy is the whole repository (`paths` is `.`), so a top-level entry upstream adds or deletes is handled too.

`../Dockerfile` mirrors upstream's `Dockerfile` (base images and digests, the Node bundle stage, the SDK publish stage,
the chiseled runtime as `$APP_UID`), and every deviation is marked `localtest:`; the tool three-way merges upstream's
changes into it when that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a
different build. The same for `../.dockerignore`.
