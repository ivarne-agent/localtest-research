# Vendored copy of Altinn/altinn-register

```json vendor-sync
{
  "repository": "Altinn/altinn-register",
  "branch": "main",
  "commit": "f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0",
  "date": "2026-10-13",
  "paths": [
    "src/apps/Altinn.Register",
    "src/pkgs/Altinn.Register.Contracts",
    ".editorconfig",
    "Directory.Build.props",
    "Directory.Build.targets",
    "Directory.Packages.props",
    "global.json",
    "stylecop.json",
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
    "src/apps/Altinn.Register/src/Altinn.Register/Altinn.Register.csproj"
  ],
  "follow": [
    {
      "package": "Altinn.Common.AccessToken",
      "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"
    },
    {
      "package": "Asp.Versioning.Mvc.ApiExplorer",
      "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"
    },
    {
      "package": "Microsoft.Extensions.ServiceDiscovery",
      "file": "fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj"
    }
  ],
  "reminders": [
    "Register waits for altinn-auth#4056 (https://github.com/Altinn/altinn-auth/issues/4056, an issue: move altinn-register into Altinn/altinn-auth; open on 2026-10-27): check it and its linked pull requests at every sync; once the move has landed, retarget with `sync register --repository Altinn/altinn-auth --paths ... --group altinn-auth`"
  ]
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-register
- Commit: [`f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) (main, 2026-10-13)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved: `src/apps/Altinn.Register/`, `src/pkgs/Altinn.Register.Contracts/`,
  and the root build files `.editorconfig`, `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, `stylecop.json`; plus the upstream `Dockerfile` and `.dockerignore`
  (added 2026-10-19, same commit) for reference.
- Copied with `rsync` of a clean checkout, no subtree (later syncs: `tools/vendor-sync.py sync`, task 58); identical to `git archive` of the commit (checked
  2026-10-19). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                           | Lines   | Kind     | First line of the `LOCALTEST` branch                                                   |
| -------------------------------------------------------------- | ------- | -------- | -------------------------------------------------------------------------------------- |
| `src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs` | 249-251 | addition | `Altinn.Register.Localtest.LocaltestRegisterExtensions.AddLocaltestRegister(builder);` |

<!-- vendor-sync:end sites -->

One site, a pure addition in `src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs`:
`AddLocaltestRegister(builder)` after the production persistence block.

## Syncing

`python3 localtest/tools/vendor-sync.py sync register` from the repository root stages the sync and prints what to
triage, build and verify; the agent finishes it by hand as [`docs/vendor-sync.md`](../../../docs/vendor-sync.md)
describes (resolve, `check`, build, verify, two commits). Without `--to` it takes the head of `main`. The metadata block
above drives it: the copied `paths`, the `mirrors` it merges or reports, the `watch`ed paths, the `projects` to build,
the package versions that `follow` the host, and the `reminders` it prints.

**Register waits for [altinn-auth#4056](https://github.com/Altinn/altinn-auth/issues/4056)** (an issue, open on
2026-10-27: move altinn-register into `Altinn/altinn-auth`, which has a skeleton `src/apps/Altinn.Register/` so far):
check it and its linked pull requests at every sync (the tool prints the reminder), and once the move has landed,
retarget with `sync register --repository Altinn/altinn-auth --paths … --group altinn-auth`.

`../Dockerfile` mirrors upstream's `Dockerfile` (base images and digests, build and publish, tzdata, the `dotnet` user),
and every deviation is marked `localtest:`; the tool three-way merges upstream's changes into it when that is clean, and
otherwise prints the hunk to carry over by hand. Never let it drift into a different build.

Package versions in `../fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj` follow
`Directory.Packages.props` and what the host resolves (`obj/project.assets.json`: service discovery from
`Altinn.Authorization.ServiceDefaults`); `check` compares them after a host build (`follow`).
