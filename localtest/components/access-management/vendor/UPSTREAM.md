# Vendored copy of Altinn/altinn-auth (Access Management)

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`392f9578d925b947331a1e90fb609c031654d3d2`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2) (main, 2026-10-16)
- Copied paths, relative layout preserved:
  - `src/apps/Altinn.AccessManagement/` (host, API projects, Core, Integration, Persistence, PersistenceEF, AppHost
    and tests; AppHost is never built here)
  - `src/libs/Altinn.Authorization.Api.Contracts/`, `src/libs/Altinn.Authorization.Host/` (Host, Host.Database,
    Host.Lease, Host.Pipeline are referenced; Host.MassTransit is not), `src/libs/Altinn.Authorization.Integration/`
    (Integration.Platform is referenced)
  - `src/pkgs/Altinn.Authorization.ABAC/` and `src/pkgs/Altinn.Authorization.PEP/`
  - build files on the path from the repository root: `.editorconfig`, `global.json`, `src/Directory.Build.props`,
    `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/Altinn.ruleset`, `src/Stylecop.json`, and the
    `Directory.Build.*`, `Directory.Packages.props` and `README.md` files in `src/apps/`, `src/libs/` and `src/pkgs/`.
- Copied with `rsync`, no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.
