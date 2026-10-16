# Vendored copy of Altinn/altinn-auth (Authentication)

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`392f9578d925b947331a1e90fb609c031654d3d2`](https://github.com/Altinn/altinn-auth/tree/392f9578d925b947331a1e90fb609c031654d3d2) (main, 2026-10-16)
- Copied paths, relative layout preserved: `src/apps/Altinn.Authentication/`, `src/pkgs/JWTCookieAuthentication/`
  (referenced by relative path from the host csproj), and the build files on the path from the repository root:
  `.editorconfig`, `global.json`, `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`
  at the root, `src/`, `src/apps/` and `src/pkgs/` levels where they exist.
- Copied with `rsync`, no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first.
