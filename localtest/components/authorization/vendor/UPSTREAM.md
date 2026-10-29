# Vendored copy of Altinn/altinn-auth (Authorization PDP)

```json vendor-sync
{
  "repository": "Altinn/altinn-auth",
  "branch": "main",
  "commit": "3d51810374b255aeaa179993e53864d122586a65",
  "date": "2026-10-19",
  "paths": [
    ".editorconfig",
    "global.json",
    "src/Altinn.ruleset",
    "src/Directory.Build.props",
    "src/Directory.Build.targets",
    "src/Directory.Packages.props",
    "src/Stylecop.json",
    "src/apps/Altinn.Authorization",
    "src/apps/Directory.Build.props",
    "src/apps/Directory.Build.targets",
    "src/libs/Altinn.Authorization.Api.Contracts",
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
      "upstream": "src/apps/Altinn.Authorization/Dockerfile",
      "ours": "Dockerfile",
      "merge": true
    }
  ],
  "watch": [
    "src/apps/Altinn.Authorization/docker-bake.hcl",
    "src/apps/Altinn.Authorization/containers",
    "containers"
  ],
  "projects": ["src/apps/Altinn.Authorization/src/Altinn.Authorization"],
  "follow": [],
  "reminders": []
}
```

<!-- vendor-sync:begin pin -->

- Repository: https://github.com/Altinn/altinn-auth
- Commit: [`3d51810374b255aeaa179993e53864d122586a65`](https://github.com/Altinn/altinn-auth/tree/3d51810374b255aeaa179993e53864d122586a65) (main, 2026-10-19)

<!-- vendor-sync:end pin -->
- Copied paths, relative layout preserved:
  - `src/apps/Altinn.Authorization/` (the PDP host and its tests)
  - `src/libs/Altinn.Authorization.Api.Contracts/` (project reference from the host, not on NuGet)
  - `src/pkgs/Altinn.Authorization.ABAC/` and `src/pkgs/Altinn.Authorization.PEP/` (project references from the
    host; PEP references ABAC)
  - build files on the path from the repository root: `.editorconfig`, `global.json`, `src/Directory.Build.props`,
    `src/Directory.Build.targets`, `src/Directory.Packages.props`, `src/Altinn.ruleset`, `src/Stylecop.json`,
    `Directory.Build.props/targets` in `src/apps/`, `src/libs/` and `src/pkgs/`, `src/pkgs/Directory.Packages.props`
    and `src/pkgs/README.md` (package readme fallback).
- Copied with `rsync`, no subtree (later syncs: `tools/vendor-sync.py sync`, task 58). The first commit holds the files unmodified; every later change inside this
  folder is a `#if !LOCALTEST` / `#else` / `#endif` block, production code first, or a pure `#if LOCALTEST` addition.

## Directive sites

<!-- vendor-sync:begin sites -->

| File                                                                                                         | Lines   | Kind        | First line of the `LOCALTEST` branch                                           |
| ------------------------------------------------------------------------------------------------------------ | ------- | ----------- | ------------------------------------------------------------------------------ |
| `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`                                          | 53-58   | addition    | `global::Localtest.Shared.ServiceDiscovery.LocaltestServiceDiscovery....`      |
| `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`                                          | 67-71   | addition    | `Altinn.Platform.Authorization.Localtest.LocaltestAuthorizationExtensions....` |
| `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`                                          | 278-304 | replacement | `Altinn.Platform.Authorization.Localtest.LocaltestAuthorizationTelemetry....`  |
| `src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/OedRoleAssignmentWrapper.cs` | 47-50   | addition    | `return [];`                                                                   |
| `src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/PolicyRetrievalPoint.cs`     | 54-57   | addition    | `_memoryCache.Remove(policyPath);`                                             |

<!-- vendor-sync:end sites -->

Five sites, three in `src/apps/Altinn.Authorization/src/Altinn.Authorization/Program.cs`, the first two pure additions:

- Right after `WebApplication.CreateBuilder`: the service-discovery registration (`LocaltestServiceDiscovery.AddLocaltestServiceDiscovery(builder.Services)` from `fakes/ServiceDiscovery/`, no pass-through since task 40; site from task 25, `planned-components/service-discovery.md`); the package comes transitively from
  `Altinn.Authorization.ServiceDefaults`.
- `AddLocaltestAuthorization(builder)` after `ConfigureServices`.
- Task 53: a `#if !LOCALTEST` / `#else` around the Application Insights block in `ConfigureServices`, whose `#else` calls
  `LocaltestAuthorizationTelemetry.AddLocaltestTelemetry(services, config)` (OpenTelemetry over OTLP when
  `OTEL_EXPORTER_OTLP_ENDPOINT` is set, `../fakes/LocaltestAuthorizationTelemetry.cs`).

Two additions in place of fakes (task 63), in `src/apps/Altinn.Authorization/src/Altinn.Authorization/Services/Implementation/`:

- `OedRoleAssignmentWrapper.cs`, `GetOedRoleAssignments`: returns an empty list before the Maskinporten call (no OED locally).
- `PolicyRetrievalPoint.cs`, `GetPolicyAsync(org, app)`: removes the app policy's cache entry before the read, so a
  pushed policy applies to the next decision.

## Syncing

Run it for the group: Authentication, Authorization and Access Management vendor one `Altinn/altinn-auth` commit and
share libraries (`Api.Contracts`, `ABAC`, `PEP`): they are the group `altinn-auth`, which the tool moves as one (`sync
altinn-auth`, or any of the three). `python3 localtest/tools/vendor-sync.py sync altinn-auth` from the repository root
stages the sync and prints what to triage, build and verify; the agent finishes it by hand as
[`docs/vendor-sync.md`](../../../docs/vendor-sync.md) describes (resolve, `check`, build, verify, two commits). Without
`--to` it takes the head of `main`. The metadata block above drives it: the copied `paths`, the `mirrors` it merges or
reports, the `watch`ed paths, the `projects` to build, the package versions that `follow` the host, and the `reminders`
it prints.

`../Dockerfile` mirrors upstream's `src/apps/Altinn.Authorization/Dockerfile` (base images and digests, publish, tzdata
and krb5-libs, the `dotnet` user), and every deviation is marked `localtest:`; the tool three-way merges upstream's
changes into it when that is clean, and otherwise prints the hunk to carry over by hand. Never let it drift into a
different build. `docker-bake.hcl` had an empty default group at the 2026-10-19 pin. Upstream moved the release to
`docker-bake.hcl` and `src/apps/Altinn.Authorization/containers/Dockerfile.api` on 2026-10-27 (`0a905aa`, `6228127`);
the tool reports both as watched paths, and the next sync decides what `../Dockerfile` mirrors.
