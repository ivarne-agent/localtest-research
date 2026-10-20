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
- Not copied: `tests/`, `docs/`, `infra/`, `.docker/` (the Postgres tuning SQL the monorepo's compose mounts), `.k6/`,
  `.gitignore`, `Runtime/workflow-engine/Dockerfile` and its `Dockerfile.dockerignore` (the standalone engine image;
  localtest builds the app), Makefiles, solution and compose files. Nothing above these folders is
  needed: there is no `Directory.Build.props`, `Directory.Packages.props` or `global.json` in `src/`, `src/Runtime/`
  or `src/common/`, and every copied `.editorconfig` is `root = true`, so the monorepo root `.editorconfig` does not
  apply.
- Copied with rsync of the clean working tree at that commit (bin/obj/node_modules excluded; every copied file is
  tracked upstream), no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder must be a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-studio` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, adds and deletes files, and reports conflicts (there are no
   `LOCALTEST` sites today):
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src/Runtime/workflow-engine-app src/Runtime/workflow-engine src/common/dotnet/Altinn.Studio.Common`.
   The paths are the folders `vendor/src/` mirrors; the script also brings in upstream's `tests/`, `docs/` and other
   folders this copy leaves out, so delete those again (or narrow the paths to the copied ones above).
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`, ignoring the
   folders left out on purpose.
3. **Keep `../Dockerfile` as equal to `src/Runtime/workflow-engine-app/Dockerfile` as possible, and `../.dockerignore`
   to its `Dockerfile.dockerignore`.** The Dockerfile mirrors the upstream one line for line (base images and digests,
   the copy of the three folders, the pruning, restore and publish, `USER $APP_UID`), and every deviation is marked
   `localtest:`. When `git diff <pin> <new> -- src/Runtime/workflow-engine-app/Dockerfile*` shows a change, make the
   same change here.
4. Update the commit line above, build the image (`podman build -t localtest-workflow-engine:dev ..`) and run an app
   with the v9 backend through it. Commit the re-pin on its own.
