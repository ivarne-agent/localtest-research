# Vendored copy of Altinn/altinn-auth (Authorization PDP)

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`3d51810374b255aeaa179993e53864d122586a65`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) (main, 2026-10-19), re-pinned from `392f9578` on 2026-10-20 together with the other altinn-auth components
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
  There are two sites, both pure additions in `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs` (line
  numbers checked 2026-10-22): `:53-58`, right after `WebApplication.CreateBuilder`, the service-discovery registration (`AddServiceDiscovery()` and `ConfigureHttpClientDefaults(http => http.AddServiceDiscovery())`, task 25, `planned-components/service-discovery.md`); the package comes
  transitively from `Altinn.Authorization.ServiceDefaults`; and `:67-71` (`AddLocaltestAuthorization(builder)` after
  `ConfigureServices`).

## Syncing to a newer upstream commit

Authentication, Authorization and Access Management vendor the same `Altinn/altinn-auth` commit and share libraries
(`Api.Contracts`, `ABAC`, `PEP`), so re-pin all three together.

1. In a clone of `Altinn/altinn-auth` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites, adds and
   deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src/apps/Altinn.Authorization src/libs/Altinn.Authorization.Api.Contracts src/pkgs/Altinn.Authorization.ABAC src/pkgs/Altinn.Authorization.PEP <build files>`.
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs` may differ.
3. **Keep `../Dockerfile` as equal to `src/apps/Altinn.Authorization/Dockerfile` as possible.** It mirrors that file line for line (base images and digests, publish, tzdata and krb5-libs, the `dotnet` user), and every deviation is marked `localtest:`. When `git diff <pin> <new> -- src/apps/Altinn.Authorization/Dockerfile` shows a change, make the same change in `../Dockerfile`. (`docker-bake.hcl` has an empty default group at the 2026-10-19 pin, so nothing is released from it.)
4. Fix the localtest code (`../fakes/`, the overrides) for upstream's moves and renames, update the commit line above,
   build the image (`podman build -t localtest-authorization:dev ..`) and run the smoke tests. Commit the re-pin of the
   vendored files on its own, separate from localtest changes.
