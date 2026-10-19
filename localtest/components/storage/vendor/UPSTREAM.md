# Vendored copy of Altinn/altinn-storage

- Repository: https://github.com/Altinn/altinn-storage
- Commit: [`9e72a9f128debd7b9815b615d842a2d1a6d343c5`](https://github.com/Altinn/altinn-storage/tree/9e72a9f128debd7b9815b615d842a2d1a6d343c5) (main, 2026-10-19)
- Copied paths, relative layout preserved: `src/Storage/` (the host, including `Migration/`), `src/DbTools/`
  (referenced by relative path from the host csproj), and the build files on the path from the repository root:
  `Directory.Build.props`, `.editorconfig`, `.csharpierignore`, `.config/dotnet-tools.json`; plus
  `infra/postgres_init.sql` (database, schema and role setup used by the compose init script) and the upstream
  `Dockerfile` for reference. There is no `global.json`, `Directory.Packages.props` or ruleset upstream.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.
