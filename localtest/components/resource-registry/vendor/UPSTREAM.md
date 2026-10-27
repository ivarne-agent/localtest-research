# Vendored copy of Altinn/altinn-resource-registry

```json vendor-sync
{
  "repository": "Altinn/altinn-resource-registry",
  "branch": "main",
  "commit": "a3422419579832833845e47d3be57c148364bed0",
  "date": "2026-09-25",
  "paths": [
    "src/apps/Altinn.ResourceRegistry/src",
    "src/apps/Altinn.ResourceRegistry/Dockerfile",
    "src/Directory.Build.props",
    "src/Directory.Build.targets",
    "src/Directory.Packages.props",
    "src/stylecop.json",
    "Settings.StyleCop",
    "global.json"
  ],
  "group": null,
  "local": "directives",
  "mirrors": [
    {"upstream": "src/apps/Altinn.ResourceRegistry/Dockerfile", "ours": "Dockerfile", "merge": true}
  ],
  "watch": [],
  "projects": ["src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry"],
  "follow": [],
  "reminders": [
    "Altinn/altinn-resource-registry is archived (2026-09-25); the code lives on in Altinn/altinn-auth `src/apps/Altinn.ResourceRegistry/`: retarget with `sync resource-registry --repository Altinn/altinn-auth --paths ... --group altinn-auth` (TODO.md task 58, next task)"
  ]
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-resource-registry
- Commit: [`a3422419579832833845e47d3be57c148364bed0`](https://github.com/Altinn/altinn-resource-registry/tree/a3422419579832833845e47d3be57c148364bed0) (main, 2026-09-25)
<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/apps/Altinn.ResourceRegistry/src/` (the host `Altinn.ResourceRegistry` and
  `.Core`, `.Persistence` with the embedded `Migration/**/*.sql`, `.Integration`), `src/Directory.Build.props`,
  `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/stylecop.json`, the root `Settings.StyleCop` and
  `global.json`, and the upstream `src/apps/Altinn.ResourceRegistry/Dockerfile` for reference. Not copied: `test/`,
  `compose.yml`, `conf.json`, `.github/`, the solution files.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs` | 46-49 | addition | `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestConfi...` |
| `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs` | 191-193 | addition | `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestResou...` |
<!-- vendor-sync:end sites -->

Both are pure additions in `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs`:

1. Right after `AltinnHost.CreateWebApplicationBuilder(...)`: `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestConfiguration(builder);`
2. Right before `return builder.Build();`: `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestResourceRegistry(builder);`

## Syncing

`python3 localtest/tools/vendor-sync.py sync resource-registry` from the repository root stages the sync and prints what
to triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

`Altinn/altinn-resource-registry` was archived on 2026-09-25, at this pin; the code lives on in `Altinn/altinn-auth`
`src/apps/Altinn.ResourceRegistry/`. The retarget, `sync resource-registry --repository Altinn/altinn-auth --paths …
--group altinn-auth`, is the next task (TODO.md task 58).

`../Dockerfile` mirrors upstream's `src/apps/Altinn.ResourceRegistry/Dockerfile` (base images and digests, build,
publish, the `dotnet` user), and every deviation is marked `localtest:`; the tool three-way merges upstream's changes
into it when that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a different
build.

What the localtest code follows: `../fakes/`, the overrides and `../postgres-init/`.
