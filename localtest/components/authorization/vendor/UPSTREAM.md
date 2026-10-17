# Vendored copy of Altinn/altinn-auth (Authorization PDP)

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`392f9578d925b947331a1e90fb609c031654d3d2`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2) (main, 2026-10-16)
- Copied paths, relative layout preserved:
  - `src/apps/Altinn.Authorization/` (the PDP host and its tests)
  - `src/libs/Altinn.Authorization.Api.Contracts/` (project reference from the host, not on NuGet)
  - `src/pkgs/Altinn.Authorization.ABAC/` and `src/pkgs/Altinn.Authorization.PEP/` (project references from the
    host; PEP references ABAC)
  - build files on the path from the repository root: `.editorconfig`, `global.json`, `src/Directory.Build.props`,
    `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/Altinn.ruleset`, `src/Stylecop.json`,
    `Directory.Build.props/targets` in `src/apps/`, `src/libs/` and `src/pkgs/`, `src/pkgs/Directory.Packages.props`
    and `src/pkgs/README.md` (package readme fallback).
- Copied with `rsync`, no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.
