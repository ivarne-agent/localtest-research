# Vendored copy of Altinn/altinn-resource-registry

- Repository: https://github.com/Altinn/altinn-resource-registry
- Commit: [`a3422419579832833845e47d3be57c148364bed0`](https://github.com/Altinn/altinn-resource-registry/tree/a3422419579832833845e47d3be57c148364bed0) (main, 2026-09-25; still `origin/main` on 2026-10-20)
- Copied paths, relative layout preserved: `src/apps/Altinn.ResourceRegistry/src/` (the host `Altinn.ResourceRegistry` and
  `.Core`, `.Persistence` with the embedded `Migration/**/*.sql`, `.Integration`), `src/Directory.Build.props`,
  `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/stylecop.json`, the root `Settings.StyleCop` and
  `global.json`, and the upstream `src/apps/Altinn.ResourceRegistry/Dockerfile` for reference. Not copied: `test/`,
  `compose.yml`, `conf.json`, `.github/`, the solution files.
- Copied with `git archive` of that commit, no subtree yet. The first commit holds the files unmodified; every later
  change inside this folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure
  `#if LOCALTEST … #endif` addition.

## Directive sites

Both are pure additions in `src/apps/Altinn.ResourceRegistry/src/Altinn.ResourceRegistry/ResourceRegistryHost.cs`:

1. right after `AltinnHost.CreateWebApplicationBuilder(...)`: `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestConfiguration(builder);`
2. right before `return builder.Build();`: `Altinn.ResourceRegistry.Localtest.LocaltestResourceRegistryExtensions.AddLocaltestResourceRegistry(builder);`

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-resource-registry` with both commits, re-pin with the shared script, which takes
   upstream's new version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites,
   adds and deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src/apps/Altinn.ResourceRegistry/src src/apps/Altinn.ResourceRegistry/Dockerfile src/Directory.Build.props src/Directory.Build.targets src/Directory.Packages.props src/stylecop.json Settings.StyleCop global.json`
  .
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `ResourceRegistryHost.cs` may differ.
3. **Keep `../Dockerfile` as equal to `src/apps/Altinn.ResourceRegistry/Dockerfile` as possible.** It mirrors the
   upstream file (base images and digests, build, publish, the `dotnet` user), and every deviation is marked
   `localtest:`. When `git diff <pin> <new> -- src/apps/Altinn.ResourceRegistry/Dockerfile` shows a change, make the
   same change in `../Dockerfile`.
4. Fix the localtest code (`../fakes/`, the overrides, `../postgres-init/`) for upstream's changes, update the commit
   line above, build the image (`podman build -t localtest-resource-registry:dev ..`) and run `../smoke-test.sh`.
   Commit the re-pin of the vendored files on its own, separate from localtest changes.
