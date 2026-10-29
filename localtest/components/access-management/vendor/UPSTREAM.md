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
    {
      "upstream": "containers/Dockerfile.aspnet-builder",
      "ours": "Dockerfile"
    },
    {
      "upstream": "containers/Dockerfile.aspnet-runtime",
      "ours": "Dockerfile"
    },
    {
      "upstream": "src/apps/Altinn.AccessManagement/containers/Dockerfile.api",
      "ours": "Dockerfile"
    }
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
- Copied with `rsync`, no subtree (later syncs: `tools/vendor-sync.py sync`, task 58). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                                         | Lines   | Kind        | First line of the `LOCALTEST` branch                                                         |
| ------------------------------------------------------------------------------------------------------------ | ------- | ----------- | -------------------------------------------------------------------------------------------- |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs` | 25-30   | replacement | `private readonly HttpClient _httpClient;`                                                   |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement.Integration/Clients/ResourceRegistryClient.cs` | 42-51   | replacement | `public ResourceRegistryClient(IOptions<PlatformSettings> settings, ...`                     |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/AccessManagementHost.cs`                       | 70-73   | addition    | `Altinn.AccessManagement.Localtest.LocaltestAccessManagementExtensions....`                  |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/AccessManagementHost.cs`                       | 168-170 | addition    | `Altinn.AccessManagement.Localtest.LocaltestAccessManagementExtensions....`                  |
| `src/apps/Altinn.AccessManagement/src/Altinn.AccessManagement/Program.cs`                                    | 19-35   | replacement | `await Init();`                                                                              |
| `src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/TokenGenerator.cs`  | 12-17   | replacement | `internal class TestTool(IOptions<AltinnIntegrationOptions> options, IHttpClientFactory ...` |
| `src/libs/Altinn.Authorization.Integration/src/Altinn.Authorization.Integration.Platform/TokenGenerator.cs`  | 45-47   | addition    | `return platformGenerator.GenerateAccessToken(issuer, app);`                                 |

<!-- vendor-sync:end sites -->

What each site does and why it is there: [`../README.md`](../README.md#directive-sites-in-vendor) (the host's
`Program.cs` and `AccessManagementHost.cs`, and the Resource Registry client's base address and constructor).

## Syncing

Run it for the group: Authentication, Authorization and Access Management vendor one `Altinn/altinn-auth` commit and
share libraries (`Api.Contracts`, `ABAC`, `PEP`): they are the group `altinn-auth`, which the tool moves as one (`sync
altinn-auth`, or any of the three). `python3 localtest/tools/vendor-sync.py sync altinn-auth` from the repository root
stages the sync and prints what to triage, build and verify; the agent finishes it by hand as
[`docs/vendor-sync.md`](../../../docs/vendor-sync.md) describes (resolve, `check`, build, verify, two commits). Without
`--to` it takes the head of `main`. The metadata block above drives it: the copied `paths`, the `mirrors` it merges or
reports, the `watch`ed paths, the `projects` to build, the package versions that `follow` the host, and the `reminders`
it prints.

`../Dockerfile` follows the release image, which `src/apps/Altinn.AccessManagement/docker-bake.hcl` builds from
`containers/Dockerfile.api` in the app folder over the named contexts `aspnet-builder` and `aspnet-runtime` (the
vendored `containers/` files): it is those three files in order, with the context names as stage names, and every
deviation is marked `localtest:`. The tool prints upstream's changes to the three (`mirrors`) and to `docker-bake.hcl`
(`watch`); carry them over by hand. The app folder's own `Dockerfile` is only used by the manually run
`publish-flux-artifacts.yml`.

What the localtest code follows: `../fakes/` and the overrides, for upstream's moves and renames (see `../README.md`).
