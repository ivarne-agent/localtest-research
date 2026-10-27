# Vendored copy of the Resource Registry from Altinn/altinn-auth

```json vendor-sync
{
  "repository": "Altinn/altinn-auth",
  "branch": "main",
  "commit": "3d51810374b255aeaa179993e53864d122586a65",
  "date": "2026-10-19",
  "paths": [
    "src/apps/Altinn.ResourceRegistry/src",
    "src/apps/Altinn.ResourceRegistry/Dockerfile",
    "src/apps/Altinn.ResourceRegistry/Directory.Build.props",
    "src/apps/Altinn.ResourceRegistry/Directory.Build.targets",
    "src/apps/Altinn.ResourceRegistry/Directory.Packages.props",
    "src/apps/Altinn.ResourceRegistry/stylecop.json",
    "src/apps/Altinn.ResourceRegistry/.editorconfig",
    "global.json"
  ],
  "group": "altinn-auth",
  "local": "directives",
  "mirrors": [
    {
      "upstream": "src/apps/Altinn.ResourceRegistry/Dockerfile",
      "ours": "Dockerfile",
      "merge": true
    }
  ],
  "watch": ["src/apps/Altinn.ResourceRegistry/docker-bake.hcl", "containers"],
  "projects": ["src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry"],
  "follow": [],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`3d51810374b255aeaa179993e53864d122586a65`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) (main, 2026-10-19)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: the vertical `src/apps/Altinn.ResourceRegistry/` of the altinn-auth
  monorepo, its `src/` (the host `Altinn.ResourceRegistry` and `.Core`, `.Persistence` with the embedded
  `Migration/**/*.sql`, `.Integration`, and `src/Directory.Build.props`), the vertical's own `Directory.Build.props`,
  `Directory.Build.targets`, `Directory.Packages.props` (the vertical builds on its own: none of them imports
  altinn-auth's `src/Directory.*`), `stylecop.json` and `.editorconfig` (it carries the StyleCop severities), the
  repository's `global.json`, and the upstream `Dockerfile` for reference. Not copied: `test/`, `docs/`, `compose.yml`,
  `conf.json`, `docker-bake.hcl` (watched), the solution file, the agent files.
- Every change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition; `vendor-sync.py check` proves it.
- History: vendored from `Altinn/altinn-resource-registry` at `a342241` (2026-09-25), the day that repository was
  archived and imported into altinn-auth with its history (`30a6fc91`); retargeted to altinn-auth in task 58's
  follow-up (2026-10-27) at the `altinn-auth` group's commit, which the vertical shares with Authentication,
  Authorization and Access Management.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                   | Lines   | Kind     | First line of the `LOCALTEST` branch                                        |
| -------------------------------------------------------------------------------------- | ------- | -------- | --------------------------------------------------------------------------- |
| `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs` | 46-49   | addition | `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions....` |
| `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs` | 192-194 | addition | `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions....` |

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

The Resource Registry is in the `altinn-auth` group: `sync altinn-auth` (or `sync resource-registry`) moves it with
Authentication, Authorization and Access Management to one commit, and `check` fails when their pins differ.

`../Dockerfile` mirrors upstream's `src/apps/Altinn.ResourceRegistry/Dockerfile` (base images and digests, build,
publish, the `dotnet` user; byte for byte the archived repository's at `a342241`), and every deviation is marked
`localtest:`; the tool three-way merges upstream's changes into it when that is clean, and otherwise prints the hunk to
carry over by hand. Never let it drift into a different build. The vertical's `docker-bake.hcl` (and altinn-auth's
`containers/` it names) are watched: at the pin its default group publishes nothing, so the `Dockerfile` is still the
build; when the release moves to the bake file, as Authorization's did, the mirror needs a decision.

What the localtest code follows: `../fakes/`, the overrides and `../postgres-init/`.
