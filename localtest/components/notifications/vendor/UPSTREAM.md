# Vendored copy of Altinn/altinn-notifications

- Repository: https://github.com/Altinn/altinn-notifications
- Commit: [`a4aa959a5c50094048aceb98af8dccd9db17fca8`](https://github.com/Altinn/altinn-notifications/tree/a4aa959a5c50094048aceb98af8dccd9db17fca8) (main, 2026-10-20; still `origin/main` when vendored)
- Copied paths, relative layout preserved: `components/api/src/` (the orders API host `Altinn.Notifications`, `.Core`,
  `.Integrations`, `.Persistence` with the Yuniql `Migration/` folders, and the build-time tool `DbTools`),
  `components/shared/src/` (`Altinn.Notifications.Shared`), the root `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json` and `stylecop.json`, and the upstream `components/api/Dockerfile` for
  reference. Not copied: `components/email-service` and `components/sms-service` (the hosts behind the queue, not run
  locally), every `test/` folder, `tools/`, `docs/`, `.github/`, the solution files. Upstream has no lock files.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.
