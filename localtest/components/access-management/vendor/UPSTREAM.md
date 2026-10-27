# Vendored copy of Altinn/altinn-auth (Access Management)

```json vendor-sync
{
  "repository": "Altinn/altinn-auth",
  "branch": "main",
  "commit": "3d51810374b255aeaa179993e53864d122586a65",
  "date": "2026-10-19",
  "paths": [
    ".editorconfig",
    "global.json",
    "containers/Dockerfile.aspnet-builder",
    "containers/Dockerfile.aspnet-runtime",
    "src/Altinn.ruleset",
    "src/Directory.Build.props",
    "src/Directory.Build.targets",
    "src/Directory.Packages.props",
    "src/Stylecop.json",
    "src/apps/Altinn.AccessManagement",
    "src/apps/Directory.Build.props",
    "src/apps/Directory.Build.targets",
    "src/libs/Altinn.Authorization.Api.Contracts",
    "src/libs/Altinn.Authorization.Host",
    "src/libs/Altinn.Authorization.Integration",
    "src/libs/Directory.Build.props",
    "src/libs/Directory.Build.targets",
    "src/pkgs/Altinn.Authorization.ABAC",
    "src/pkgs/Altinn.Authorization.PEP",
    "src/pkgs/Directory.Build.props",
    "src/pkgs/Directory.Build.targets",
    "src/pkgs/Directory.Packages.props",
    "src/pkgs/README.md"
  ],
  "group": "altinn-auth",
  "local": "directives",
  "mirrors": [
    {"upstream": "containers/Dockerfile.aspnet-builder", "ours": "Dockerfile"},
    {"upstream": "containers/Dockerfile.aspnet-runtime", "ours": "Dockerfile"},
    {"upstream": "src/apps/Altinn.AccessManagement/containers/Dockerfile.api", "ours": "Dockerfile"}
  ],
  "watch": ["src/apps/Altinn.AccessManagement/docker-bake.hcl"],
  "projects": ["src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement"],
  "follow": [],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->
- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`3d51810374b255aeaa179993e53864d122586a65`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) (main, 2026-10-19)
<!-- vendor-sync:end pin -->
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
  - `containers/Dockerfile.aspnet-builder` and `containers/Dockerfile.aspnet-runtime` (added 2026-10-20): the shared
    build and runtime images `docker-bake.hcl` builds Access Management's release image with, for reference.
- Copied with `rsync`, no subtree yet. The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->
| File | Lines | Kind | First line of the `LOCALTEST` branch |
| --- | --- | --- | --- |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs` | 25-30 | replacement | `private readonly HttpClient _httpClient;` |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs` | 42-51 | replacement | `public ResourceRegistryClient(IOptions<PlatformSettings> settings, ILogger<IResourceReg...` |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/AccessManagementHost.cs` | 70-73 | addition | `Altinn.AccessManagement.Localtest.LocaltestAccessManagementExtensions.AddLocaltestConfi...` |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/AccessManagementHost.cs` | 168-170 | addition | `Altinn.AccessManagement.Localtest.LocaltestAccessManagementExtensions.AddLocaltestAcces...` |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Program.cs` | 19-35 | replacement | `await Altinn.AccessManagement.Localtest.Database.LocaltestDatabase.EnsureAsync(app.Serv...` |
<!-- vendor-sync:end sites -->

What each site does and why it is there: [`../README.md`](../README.md#directive-sites-in-vendor) (the host's
`Program.cs` and `AccessManagementHost.cs`, and the Resource Registry client's base address and constructor).

## Syncing to a newer upstream commit

Authentication, Authorization and Access Management vendor the same `Altinn/altinn-auth` commit and share libraries
(`Api.Contracts`, `ABAC`, `PEP`), so re-pin all three together.

1. In a clone of `Altinn/altinn-auth` with both commits, re-pin with the shared script, which takes upstream's new
   version of every file that is unchanged locally, three-way merges the files with `LOCALTEST` sites, adds and
   deletes files, and reports conflicts:
   `../../tools/repin-vendor.py <clone> <pin> <new> vendor src/apps/Altinn.AccessManagement src/libs/Altinn.Authorization.Api.Contracts src/libs/Altinn.Authorization.Host src/libs/Altinn.Authorization.Integration src/pkgs/Altinn.Authorization.ABAC src/pkgs/Altinn.Authorization.PEP containers/Dockerfile.aspnet-builder containers/Dockerfile.aspnet-runtime <build files>`.
2. Check the result: `git archive <new> <paths>` into an empty folder and `diff -rq` it against `vendor/`. Only
   `UPSTREAM.md` and the files with `LOCALTEST` sites (`src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Program.cs`, `AccessManagementHost.cs` and, since task 40, `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs`) may differ.
3. **Keep `../Dockerfile` as equal to the release image as possible.** Access Management is released through `src/apps/Altinn.AccessManagement/docker-bake.hcl`: `containers/Dockerfile.api` in the app folder over the named contexts `aspnet-builder` and `aspnet-runtime` (the vendored `containers/` files). `../Dockerfile` is those three files in order, with the context names as stage names, and every deviation is marked `localtest:`. When `git diff <pin> <new> -- containers src/apps/Altinn.AccessManagement/containers src/apps/Altinn.AccessManagement/docker-bake.hcl` shows a change, make the same change in `../Dockerfile`. The app folder's own `Dockerfile` is only used by the manually run `publish-flux-artifacts.yml`.
4. Fix the localtest code (`../fakes/`, the overrides) for upstream's moves and renames, update the commit line above,
   build the image (`podman build -t localtest-access-management:dev ..`) and run the smoke tests. Commit the re-pin of the
   vendored files on its own, separate from localtest changes.
