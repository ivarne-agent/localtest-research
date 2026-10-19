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

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-storage`, list what changed in the copied paths since the pin:
   `git diff --stat <pin> <new> -- src/Storage src/DbTools Directory.Build.props .editorconfig .csharpierignore .config/dotnet-tools.json infra/postgres_init.sql Dockerfile`.
2. Copy the changed files over the vendored ones (`git show <new>:<path> > <path>`, or `git archive <new> <paths> | tar -x`
   for added files; delete files deleted upstream). The `LOCALTEST` sites (`src/Storage/Program.cs`,
   `src/Storage/Controllers/InstancesController.cs`) need a three-way merge instead: take the upstream file and put
   the directive blocks back unchanged.
3. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and the files with `LOCALTEST` sites may differ.
4. **Keep `../Dockerfile` as equal to `vendor/Dockerfile` as possible.** It mirrors the upstream file line for line
   (base images and digests, DbTools build, `Migration` copy, `USER`), and every deviation is a line or block marked
   `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in `../Dockerfile`; never
   let it drift into a different build.
5. Update the commit line above, build the image (`podman build -t localtest-storage:dev ..`) and run
   `../../../smoke-test.sh` against it. Commit the sync on its own, separate from localtest changes.
