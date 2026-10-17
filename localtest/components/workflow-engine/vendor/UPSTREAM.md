# Vendored copy of the workflow engine from Altinn/altinn-studio

- Repository: https://github.com/Altinn/altinn-studio
- Commit: [`447e53eb9797d4fffe63d0c61d05a1f692b4ec48`](https://github.com/Altinn/altinn-studio/tree/447e53eb9797d4fffe63d0c61d05a1f692b4ec48) (main, 2026-10-16)
- Copied paths under `src/`, relative layout preserved (the same build context `src/` the upstream
  `Runtime/workflow-engine-app/Dockerfile` uses):
  - `Runtime/workflow-engine-app/`: the host `src/WorkflowEngine.App/`, plus `Directory.Build.props`,
    `Directory.Packages.props`, `global.json`, `.editorconfig`, `AGENTS.md`, `README.md`, and the upstream
    `Dockerfile` / `Dockerfile.dockerignore` for reference.
  - `Runtime/workflow-engine/`: the library projects `src/WorkflowEngine.{Core,Commands,Data,Models,Resilience,Telemetry}/`
    and `src/Directory.Build.props`, plus `Directory.Build.props`, `Directory.Packages.props`, `global.json`,
    `.editorconfig`, `.csharpierrc.yaml`, `.csharpierignore`, `.config/dotnet-tools.json`, `AGENTS.md`, `README.md`.
  - `common/dotnet/Altinn.Studio.Common/` (whole; referenced by `WorkflowEngine.Core`).
- Not copied: `tests/`, `docs/`, `infra/`, Makefiles, solution and compose files. Nothing above these folders is
  needed: there is no `Directory.Build.props`, `Directory.Packages.props` or `global.json` in `src/`, `src/Runtime/`
  or `src/common/`, and every copied `.editorconfig` is `root = true`, so the monorepo root `.editorconfig` does not
  apply.
- Copied with rsync of the clean working tree at that commit (bin/obj/node_modules excluded; every copied file is
  tracked upstream), no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder must be a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.
