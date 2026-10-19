# Vendored copy of Altinn/altinn-register

- Repository: https://github.com/Altinn/altinn-register
- Commit: [`f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0`](https://github.com/Altinn/altinn-register/tree/f6b5c6034609da6b69a1ffdf8ef3b5265768f0f0) (main, 2026-10-16)
- Copied paths, relative layout preserved: `src/apps/Altinn.Register/`, `src/pkgs/Altinn.Register.Contracts/`,
  and the root build files `.editorconfig`, `Directory.Build.props`, `Directory.Build.targets`,
  `Directory.Packages.props`, `global.json`, `stylecop.json`; plus the upstream `Dockerfile` and `.dockerignore`
  (added 2026-10-19, same commit) for reference.
- Copied with `rsync` of a clean checkout, no subtree yet; identical to `git archive` of the commit (checked
  2026-10-19). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first.

## Syncing to a newer upstream commit

1. In a clone of `Altinn/altinn-register`, list what changed in the copied paths since the pin:
   `git diff --stat <pin> <new> -- src/apps/Altinn.Register src/pkgs/Altinn.Register.Contracts .editorconfig Directory.Build.props Directory.Build.targets Directory.Packages.props global.json stylecop.json Dockerfile .dockerignore`.
   If Register has moved into `Altinn/altinn-auth` (altinn-auth#4056), re-vendor from there instead.
2. Copy the changed files over the vendored ones (`git archive <new> <paths> | tar -x`; delete files deleted
   upstream). The `LOCALTEST` site (`src/apps/Altinn.Register/src/Altinn.Register/RegisterHost.cs`) needs a three-way
   merge: take the upstream file and put the directive block back unchanged.
3. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `RegisterHost.cs` may differ.
4. **Keep `../Dockerfile` as equal to `vendor/Dockerfile` as possible.** It mirrors the upstream file line for line
   (base images and digests, build and publish, tzdata, the `dotnet` user), and every deviation is a line or block
   marked `localtest:`. When `git diff <pin> <new> -- Dockerfile` shows a change, make the same change in
   `../Dockerfile`.
5. Package versions in `../fakes/Altinn.Register.Localtest/Altinn.Register.Localtest.csproj` follow
   `Directory.Packages.props`; update them with it.
6. Update the commit line above, build the image (`podman build -t localtest-register:dev ..`) and run
   `../smoke-test.sh`. Commit the sync on its own, separate from localtest changes.
