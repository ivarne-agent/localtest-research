# Vendored copy of Altinn/altinn-events

- Repository: https://github.com/Altinn/altinn-events
- Commit: [`1a4a80c4599e9e176c04bce1b5da7861ca1c54a6`](https://github.com/Altinn/altinn-events/tree/1a4a80c4599e9e176c04bce1b5da7861ca1c54a6) (main, 2026-10-19)
- Copied paths, relative layout preserved: `src/Events/` (the host, including `Migration/`, unused locally),
  `src/Events.Common/` and `src/DbTools/` (both referenced by relative path from the host csproj), the root
  `stylecop.json` and `.editorconfig`, and the upstream `Dockerfile` for reference. Not copied: `src/Events.Functions`
  (Azure Functions for the legacy Storage Queue path), `emulator/`, `test/`. There is no `Directory.Build.props`,
  `Directory.Packages.props` or `global.json` upstream.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.
